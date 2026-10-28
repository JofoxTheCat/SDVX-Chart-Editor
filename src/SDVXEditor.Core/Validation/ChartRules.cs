using System.Globalization;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Validation;

// Built-in chart-check rules. Each class is one IChartCheck, found by ChartChecker through reflection.
// Thresholds that are our own choice (not from a spec) are named constants with the reason beside them.

/// <summary>BPM, time signatures, stops, std/disp BPM.</summary>
public sealed class TimingChecks : IChartCheck
{
    public string Id => "timing";
    public string Category => "Timing";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        var c = ctx.Chart;
        if (c.Beat.Bpm.Count == 0)
            yield return new ChartIssue("timing.bpm-missing", IssueSeverity.Warning, "The chart has no BPM: 120 BPM is assumed everywhere.", 0);

        foreach (var b in c.Beat.Bpm)
        {
            if (!(b.Bpm > 0) || double.IsInfinity(b.Bpm))
                yield return new ChartIssue("timing.bpm-invalid", IssueSeverity.Error, $"BPM {b.Bpm.ToString(CultureInfo.InvariantCulture)} at {ctx.Where(b.Y)} is not a positive number (readers drop it).", b.Y);
        }

        foreach (var s in c.Beat.TimeSignatures)
        {
            long pulse = s.MeasureIndex >= 0 ? ctx.Timing.MeasureStart(s.MeasureIndex) : 0;
            if (s.Numerator < 1 || s.Denominator < 1)
                yield return new ChartIssue("timing.signature-invalid", IssueSeverity.Error, $"Time signature {s.Numerator}/{s.Denominator} at measure {s.MeasureIndex + 1} is invalid.", pulse);
            else if (Timebase.PulsesPerWholeNote % s.Denominator != 0)
                yield return new ChartIssue("timing.signature-denominator", IssueSeverity.Warning,
                    $"Time signature {s.Numerator}/{s.Denominator} at measure {s.MeasureIndex + 1}: the denominator does not divide 960, so the measure length is rounded (kson needs 960 % d == 0).", pulse);
        }

        foreach (var s in c.Beat.Stops)
        {
            if (s.Length <= 0)
                yield return new ChartIssue("timing.stop-length", IssueSeverity.Error, $"Stop at {ctx.Where(s.Y)} has no length.", s.Y);
        }

        var bpms = c.Beat.Bpm.Where(b => b.Bpm > 0).Select(b => b.Bpm).ToList();
        if (c.Meta.StdBpm is { } std && bpms.Count > 0 && (std < bpms.Min() - 1e-9 || std > bpms.Max() + 1e-9))
        {
            yield return new ChartIssue("timing.std-bpm", IssueSeverity.Warning,
                $"Standard BPM (hi-speed basis) {std.ToString("0.##", CultureInfo.InvariantCulture)} lies outside the chart's BPM range {bpms.Min():0.##}-{bpms.Max():0.##} (kson: it should lie between them).");
        }

        if (c.Meta.DispBpm.Any(ch => !(char.IsAsciiDigit(ch) || ch is '-' or '.')))
            yield return new ChartIssue("timing.disp-bpm", IssueSeverity.Warning, $"Display BPM \"{c.Meta.DispBpm}\" has characters other than 0-9, '-' and '.' (kson).");
    }
}

/// <summary>Notes: negative positions, overlaps, effects on chips.</summary>
public sealed class NoteChecks : IChartCheck
{
    public string Id => "notes";
    public string Category => "Notes";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        var c = ctx.Chart;
        foreach (var kind in new[] { NoteKind.Bt, NoteKind.Fx })
        {
            for (int lane = 0; lane < NoteData.LaneCount(kind); lane++)
            {
                var notes = c.Notes.Lane(kind, lane).OrderBy(n => n.Y).ToList();
                string laneName = LaneName(kind, lane);
                for (int i = 0; i < notes.Count; i++)
                {
                    var n = notes[i];
                    var r = new NoteRef(kind, lane, n);
                    if (n.Y < 0 || n.Length < 0)
                        yield return new ChartIssue("notes.negative", IssueSeverity.Error, $"{laneName} note at pulse {n.Y} (length {n.Length}) lies before the chart start or has a negative length.", Math.Max(0, n.Y)) { Notes = new[] { r } };

                    if (i + 1 < notes.Count && OverlapRule.Conflicts(n.Y, n.Length, notes[i + 1].Y, notes[i + 1].Length))
                    {
                        yield return new ChartIssue("notes.overlap", IssueSeverity.Error, $"{laneName}: two notes overlap at {ctx.Where(notes[i + 1].Y)}.", notes[i + 1].Y)
                        {
                            Notes = new[] { r, new NoteRef(kind, lane, notes[i + 1]) },
                        };
                    }
                    else if (i + 1 < notes.Count && n.Length > 0 && notes[i + 1].Length > 0 && n.EndY == notes[i + 1].Y)
                    {
                        yield return new ChartIssue("notes.touching-holds", IssueSeverity.Info, $"{laneName}: two holds touch at {ctx.Where(n.EndY)} - ksh writes them as one long hold.", n.EndY)
                        {
                            Category = "Formats",
                            Notes = new[] { r, new NoteRef(kind, lane, notes[i + 1]) },
                        };
                    }

                    if (kind == NoteKind.Fx && n.Length == 0 && n.Effects.Count > 0)
                        yield return new ChartIssue("notes.chip-effect", IssueSeverity.Warning, $"{laneName} chip at {ctx.Where(n.Y)} carries an FX effect; only holds play effects, it is dropped on save.", n.Y) { Notes = new[] { r } };
                }
            }
        }

        if (c.Notes.Bt.All(l => l.Count == 0) && c.Notes.Fx.All(l => l.Count == 0) && c.Notes.Lasers.All(l => l.Count == 0))
            yield return new ChartIssue("notes.empty", IssueSeverity.Info, "The chart has no notes and no lasers.");
    }

    public static string LaneName(NoteKind kind, int lane) => kind == NoteKind.Bt ? $"BT-{(char)('A' + lane)}" : lane == 0 ? "FX-L" : "FX-R";
}

/// <summary>Laser structure: values, point order, widths, overlapping sections, degenerate sections.</summary>
public sealed class LaserChecks : IChartCheck
{
    public string Id => "laser";
    public string Category => "Lasers";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        for (int lane = 0; lane < 2; lane++)
        {
            string name = lane == 0 ? "Laser L" : "Laser R";
            var sections = ctx.Chart.Notes.Lasers[lane].OrderBy(s => s.Y).ToList();
            for (int i = 0; i < sections.Count; i++)
            {
                var s = sections[i];
                var first = s.Points.Count > 0 ? new[] { new LaserPointRef(lane, s, s.Points[0]) } : Array.Empty<LaserPointRef>();

                if (s.Width is not (1 or 2))
                    yield return new ChartIssue("laser.width", IssueSeverity.Error, $"{name} at {ctx.Where(s.Y)} has width {s.Width} (only 1 and 2 exist).", s.Y) { LaserPoints = first };

                if (s.Points.Count == 0)
                {
                    yield return new ChartIssue("laser.empty-section", IssueSeverity.Error, $"{name} section at {ctx.Where(s.Y)} has no points.", s.Y);
                    continue;
                }

                if (s.Points.Count == 1 && !s.Points[0].IsSlam)
                    yield return new ChartIssue("laser.single-point", IssueSeverity.Warning, $"{name} at {ctx.Where(s.Y)} is a single point without a slam: it is invisible and unplayable.", s.Y) { LaserPoints = first };

                if (s.Points[0].Ry != 0)
                    yield return new ChartIssue("laser.first-point", IssueSeverity.Error, $"{name} at {ctx.Where(s.Y)}: the first point is not at the section start.", s.Y) { LaserPoints = first };

                for (int k = 1; k < s.Points.Count; k++)
                {
                    if (s.Points[k].Ry <= s.Points[k - 1].Ry)
                    {
                        long y = s.Y + s.Points[k].Ry;
                        yield return new ChartIssue("laser.point-order", IssueSeverity.Error, $"{name} at {ctx.Where(y)}: two points share a pulse or are out of order (a slam is one point with two values).", y)
                        {
                            LaserPoints = new[] { new LaserPointRef(lane, s, s.Points[k - 1]), new LaserPointRef(lane, s, s.Points[k]) },
                        };
                    }
                }

                foreach (var p in s.Points)
                {
                    bool bad(double v) => double.IsNaN(v) || v < 0 || v > 1;
                    if (bad(p.V) || p.Vf is { } vf && bad(vf))
                    {
                        long y = s.Y + p.Ry;
                        yield return new ChartIssue("laser.value-range", IssueSeverity.Error, $"{name} at {ctx.Where(y)}: value {p} lies outside 0..1.", y) { LaserPoints = new[] { new LaserPointRef(lane, s, p) } };
                    }
                }

                // the latest-ending earlier section (a long one can reach past several short ones)
                var prev = i > 0 ? sections.Take(i).Where(x => x.Points.Count > 0).MaxBy(x => x.EndY) : null;
                if (prev != null)
                {
                    var pair = prev.Points.Take(1).Select(p => new LaserPointRef(lane, prev, p)).Concat(first).ToList();
                    long gap = OverlapRule.SectionGap(prev, s);
                    if (gap <= 0)
                        yield return new ChartIssue("laser.overlap", IssueSeverity.Error, $"{name}: two laser sections overlap or touch at {ctx.Where(s.Y)} (two lasers on one lane at once).", s.Y) { LaserPoints = pair };
                    else if (gap < OverlapRule.MinLaserGap)
                        yield return new ChartIssue("laser.gap", IssueSeverity.Warning,
                            $"{name}: a section starts {gap} pulse(s) after the previous one ends at {ctx.Where(s.Y)} — less than 1/96 note (2 VOX ticks), it reads as two lasers at once. Join them (a slam) or move one.", s.Y) { LaserPoints = pair };
                }
            }
        }

        if (ctx.Chart.StaleLaserPaths > 0)
            yield return new ChartIssue("laser.stale-paths", IssueSeverity.Info, $"{ctx.Chart.StaleLaserPaths} stored laser curve path(s) no longer matched their points when the file was read and were dropped (the points are kept, as straight anchors).") { Category = "Formats" };
        if (ctx.Chart.StaleCameraPaths > 0)
            yield return new ChartIssue("camera.stale-paths", IssueSeverity.Info, $"{ctx.Chart.StaleCameraPaths} stored camera curve path(s) no longer matched their points when the file was read and were dropped.") { Category = "Formats" };
    }
}

/// <summary>
/// Hard-to-play slam patterns, as warnings instead of K-Shoot Editor's silent placement suppression: slams in 1/32
/// succession, a 1/16 (or shorter) moving segment right after a slam, and narrow slams.
/// </summary>
public sealed class SlamChecks : IChartCheck
{
    /// <summary>Two slams on one lane closer than this (1/32 note) — K-Shoot Editor's "32nd successive slams".</summary>
    public const long SuccessiveMaxPulses = Timebase.PulsesPerWholeNote / 32;

    /// <summary>A moving segment this short (1/16 note) right after a slam — K-Shoot Editor's "16th segment after slam".</summary>
    public const long ShortSegmentMaxPulses = Timebase.PulsesPerWholeNote / 16;

    /// <summary>
    /// A slam narrower than this, in normal-laser widths (a wide laser's value range counts twice), is "narrow". Our
    /// own threshold: the kson spec ties <c>vol_auto</c> to slam width but defines no number; 0.1 = 5 ksh value steps,
    /// about a third of a BT lane on screen.
    /// </summary>
    public const double NarrowWidth = 0.1;

    public string Id => "slam";
    public string Category => "Lasers";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        for (int lane = 0; lane < 2; lane++)
        {
            string name = lane == 0 ? "Laser L" : "Laser R";
            var points = ctx.Chart.Notes.Lasers[lane].OrderBy(s => s.Y)
                .SelectMany(s => s.Points.Select((p, i) => (Section: s, Point: p, Index: i, Y: s.Y + p.Ry))).ToList();

            (LaserSection Section, LaserPoint Point, long Y)? lastSlam = null;
            foreach (var (section, p, index, y) in points)
            {
                if (!p.IsSlam) continue;
                var r = new LaserPointRef(lane, section, p);

                if (lastSlam is { } prev && y - prev.Y > 0 && y - prev.Y <= SuccessiveMaxPulses)
                {
                    yield return new ChartIssue("slam.successive", IssueSeverity.Warning, $"{name}: slams 1/32 or less apart at {ctx.Where(y)} - very hard to read and to hit.", y)
                    {
                        LaserPoints = new[] { new LaserPointRef(lane, prev.Section, prev.Point), r },
                    };
                }

                lastSlam = (section, p, y);

                double width = Math.Abs(p.Vf!.Value - p.V) * section.Width;
                if (width < NarrowWidth)
                    yield return new ChartIssue("slam.narrow", IssueSeverity.Warning, $"{name}: narrow slam at {ctx.Where(y)} ({width:0.###} of the lane width) - looks like a stutter, easy to miss.", y) { LaserPoints = new[] { r } };

                if (index + 1 < section.Points.Count)
                {
                    var next = section.Points[index + 1];
                    long len = next.Ry - p.Ry;
                    if (len <= ShortSegmentMaxPulses && Math.Abs(next.V - p.ValueAfter) > 1e-9)
                    {
                        yield return new ChartIssue("slam.short-segment", IssueSeverity.Warning, $"{name}: a moving segment of 1/16 or less right after the slam at {ctx.Where(y)} - the knob cannot follow both.", y)
                        {
                            LaserPoints = new[] { r, new LaserPointRef(lane, section, next) },
                        };
                    }
                }
            }
        }
    }
}

/// <summary>Effects and key sounds that point at nothing.</summary>
public sealed class EffectUseChecks : IChartCheck
{
    public string Id => "effect-use";
    public string Category => "Effects";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        var c = ctx.Chart;
        var fxNames = EffectCatalog.AvailableNames(c, laser: false).ToHashSet();
        var laserNames = EffectCatalog.AvailableNames(c, laser: true).ToHashSet();

        for (int lane = 0; lane < 2; lane++)
        {
            foreach (var n in c.Notes.Fx[lane])
            {
                foreach (var e in n.Effects.Where(e => !fxNames.Contains(e.Name)))
                {
                    yield return new ChartIssue("effect.undefined-fx", IssueSeverity.Warning,
                        $"{NoteChecks.LaneName(NoteKind.Fx, lane)} hold at {ctx.Where(n.Y)} uses the effect \"{e.Name}\", which is neither defined nor a preset (players hear no effect).", n.Y)
                    {
                        Notes = new[] { new NoteRef(NoteKind.Fx, lane, n) },
                    };
                }
            }
        }

        foreach (var (name, pulses) in c.Audio.LaserEffects.PulseEvents)
        {
            if (laserNames.Contains(name) || pulses.Count == 0) continue;
            long y = pulses.Min();
            yield return new ChartIssue("effect.undefined-laser", IssueSeverity.Warning, $"Laser filter \"{name}\" ({pulses.Count}x, first at {ctx.Where(y)}) is neither defined nor a preset.", y);
        }

        foreach (var (laser, changes) in new[] { (false, c.Audio.FxEffects.ParamChanges), (true, c.Audio.LaserEffects.ParamChanges) })
        {
            var names = laser ? laserNames : fxNames;
            foreach (var (effect, byParam) in changes)
            {
                if (names.Contains(effect)) continue;
                long? y = byParam.Values.SelectMany(l => l).Select(v => (long?)v.Y).Min();
                yield return new ChartIssue("effect.undefined-param-change", IssueSeverity.Warning, $"Parameter changes for the undefined {(laser ? "laser" : "FX")} effect \"{effect}\".", y);
            }
        }

        foreach (var (sound, pulses) in c.Audio.KeySound.LaserSlamEvents)
        {
            foreach (long y in pulses.Where(y => !ctx.SlamPulses.Contains(y)))
            {
                yield return new ChartIssue("keysound.slam-not-on-slam", IssueSeverity.Warning, $"Slam sound \"{sound}\" at {ctx.Where(y)} is on no slam (kson: such events are ignored).", y);
            }
        }

        if (c.Audio.KeySound.OrphanFxChipEvents.Count > 0)
        {
            var o = c.Audio.KeySound.OrphanFxChipEvents;
            yield return new ChartIssue("keysound.chip-not-on-chip", IssueSeverity.Info, $"{o.Count} FX chip sound event(s) lie on no FX chip (kept in the file, never heard).", o.Min(e => e.Y));
        }

        foreach (var (list, kind) in new[] { (c.Camera.Spin, "Spin"), (c.Camera.HalfSpin, "Half spin"), (c.Camera.Swing, "Swing") })
        {
            foreach (var p in list.Where(p => !ctx.SlamPulses.Contains(p.Y)))
            {
                yield return new ChartIssue("camera.pattern-without-slam", IssueSeverity.Warning, $"{kind} at {ctx.Where(p.Y)} is on no slam (kson ignores it).", p.Y) { Category = "Camera" };
            }
        }
    }
}

/// <summary>Song metadata: level, gauge total, files, ASCII where formats want it.</summary>
public sealed class MetadataChecks : IChartCheck
{
    public string Id => "meta";
    public string Category => "Metadata";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        var m = ctx.Chart.Meta;
        if (m.Level < 1 || m.Level > 20)
            yield return new ChartIssue("meta.level-range", IssueSeverity.Error, $"Level {m.Level.ToString(CultureInfo.InvariantCulture)} is outside 1-20.");

        if (m.Title.Trim().Length == 0)
            yield return new ChartIssue("meta.title-empty", IssueSeverity.Warning, "The chart has no title.");

        if (ctx.Chart.GaugeTotal is > 0 and < 100)
            yield return new ChartIssue("meta.gauge-total", IssueSeverity.Warning, $"Gauge total {ctx.Chart.GaugeTotal} is below 100: readers treat it as automatic.");

        foreach (var (label, value) in new[] { ("Romanised title", m.TitleTranslit), ("Romanised artist", m.ArtistTranslit) })
        {
            if (!IsAscii(value))
                yield return new ChartIssue("meta.non-ascii-translit", IssueSeverity.Warning, $"{label} \"{value}\" has non-ASCII characters; the romanised fields are meant for ASCII (English metadata).");
        }

        // titles the VOX music_db cannot take fall back to an id-based ascii name in ksh2vox; romanised fields fix that
        if (!IsAscii(m.Title) && m.TitleTranslit.Trim().Length == 0)
            yield return new ChartIssue("meta.translit-missing", IssueSeverity.Info, "The title is not ASCII and has no romanised title: VOX export names the song folder by its id.");

        var c = ctx.Chart;
        foreach (var (label, file) in new[] { ("Audio file", c.Audio.Bgm.Filename), ("Jacket", m.JacketFilename), ("Genre icon", m.IconFilename), ("Title image", m.TitleImgFilename), ("Artist image", m.ArtistImgFilename) })
        {
            if (file.Length == 0) continue;
            if (!IsAscii(file))
                yield return new ChartIssue("meta.non-ascii-filename", IssueSeverity.Info, $"{label} \"{file}\" has a non-ASCII file name; some tools and older readers mangle it.");
            if (ctx.SongFolder != null && !File.Exists(Path.Combine(ctx.SongFolder, file)))
                yield return new ChartIssue("meta.file-missing", IssueSeverity.Warning, $"{label} \"{file}\" does not exist in the song folder.");
        }

        if (c.Audio.Bgm.Filename.Length == 0)
            yield return new ChartIssue("meta.no-audio", IssueSeverity.Warning, "No audio file is set.");
    }

    public static bool IsAscii(string s) => s.All(ch => ch is >= ' ' and <= '~');
}

/// <summary>Objects after the end of the song audio.</summary>
public sealed class AudioLengthChecks : IChartCheck
{
    public string Id => "audio";
    public string Category => "Timing";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        if (ctx.AudioLengthMs is not { } length || length <= 0) yield break;

        // chart time t plays at track time t + offset (EditorClock / PlaytestClock)
        double offset = ctx.Chart.Audio.Bgm.OffsetMs;
        var c = ctx.Chart;
        var notes = new List<NoteRef>();
        foreach (var kind in new[] { NoteKind.Bt, NoteKind.Fx })
            for (int lane = 0; lane < NoteData.LaneCount(kind); lane++)
                notes.AddRange(c.Notes.Lane(kind, lane).Where(n => ctx.Timing.PulseToMs(n.EndY) + offset > length).Select(n => new NoteRef(kind, lane, n)));
        var lasers = new List<LaserPointRef>();
        for (int lane = 0; lane < 2; lane++)
            foreach (var s in c.Notes.Lasers[lane])
                lasers.AddRange(s.Points.Where(p => ctx.Timing.PulseToMs(s.Y + p.Ry) + offset > length).Select(p => new LaserPointRef(lane, s, p)));

        if (notes.Count + lasers.Count == 0) yield break;
        long first = notes.Select(n => n.Note.Y).Concat(lasers.Select(p => p.Section.Y + p.Point.Ry)).Min();
        yield return new ChartIssue("audio.past-end", IssueSeverity.Warning,
            $"{notes.Count + lasers.Count} object(s) lie after the end of the audio ({length / 1000:0.0} s), the first at {ctx.Where(first)}.", first)
        {
            Notes = notes,
            LaserPoints = lasers,
        };
    }
}

/// <summary>What ksh and VOX round or lose (informational: the kson keeps everything).</summary>
public sealed class FormatChecks : IChartCheck
{
    /// <summary>One VOX tick (1/192 of a 4/4 measure) in pulses; ksh2vox puts every object on this grid.</summary>
    public const long VoxTick = Timebase.PulsesPerKsh192Unit;

    public string Id => "format";
    public string Category => "Formats";

    public IEnumerable<ChartIssue> Run(ChartCheckContext ctx)
    {
        var c = ctx.Chart;

        // ksh laser values: 51 steps
        for (int lane = 0; lane < 2; lane++)
        {
            var off = c.Notes.Lasers[lane]
                .SelectMany(s => s.Points.Select(p => (s, p)))
                .Where(x => !OnKshGrid(x.p.V) || x.p.Vf is { } vf && !OnKshGrid(vf))
                .ToList();
            if (off.Count == 0) continue;
            long y = off.Min(x => x.s.Y + x.p.Ry);
            yield return new ChartIssue("format.ksh-laser-values", IssueSeverity.Info,
                $"{(lane == 0 ? "Laser L" : "Laser R")}: {off.Count} point value(s) are between the 51 ksh steps and are rounded in a .ksh export (kson and VOX keep them).", y)
            {
                LaserPoints = off.Select(x => new LaserPointRef(lane, x.s, x.p)).ToList(),
            };
        }

        // VOX grid: objects between 1/192-measure ticks
        var notes = new List<NoteRef>();
        foreach (var kind in new[] { NoteKind.Bt, NoteKind.Fx })
            for (int lane = 0; lane < NoteData.LaneCount(kind); lane++)
                notes.AddRange(c.Notes.Lane(kind, lane).Where(n => n.Y % VoxTick != 0 || n.EndY % VoxTick != 0).Select(n => new NoteRef(kind, lane, n)));
        var points = new List<LaserPointRef>();
        for (int lane = 0; lane < 2; lane++)
            foreach (var s in c.Notes.Lasers[lane])
                points.AddRange(s.Points.Where(p => (s.Y + p.Ry) % VoxTick != 0).Select(p => new LaserPointRef(lane, s, p)));
        if (notes.Count + points.Count > 0)
        {
            long y = notes.Select(n => n.Note.Y).Concat(points.Select(p => p.Section.Y + p.Point.Ry)).Min();
            yield return new ChartIssue("format.vox-grid", IssueSeverity.Info,
                $"{notes.Count + points.Count} object(s) lie between VOX ticks (1/192 measure, snap 1/48 or coarser); the VOX export moves them to the nearest tick.", y)
            {
                Notes = notes,
                LaserPoints = points,
            };
        }

        foreach (var s in c.Beat.Stops.Where(s => s.Length % VoxTick != 0))
            yield return new ChartIssue("format.stop-rounding", IssueSeverity.Info, $"Stop at {ctx.Where(s.Y)}: its length is rounded to 1/192 measure in ksh and VOX.", s.Y);
    }

    public static bool OnKshGrid(double v) => Math.Abs(v * 50 - Math.Round(v * 50)) < 1e-6;
}
