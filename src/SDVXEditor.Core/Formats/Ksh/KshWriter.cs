using System.Text;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;
using static SDVXEditor.Core.Formats.Ksh.KshTables;

namespace SDVXEditor.Core.Formats.Ksh;

/// <summary>
/// ksh writer (UTF-8 with BOM, CRLF). Each measure gets the smallest number of lines that puts every
/// event exactly on a line. Slams become two lines up to 1/32 note apart (the reader collapses them
/// back), graph instant changes two option lines on one pulse. What ksh cannot express is listed in
/// <see cref="KnownLosses"/>.
/// </summary>
public static class KshWriter
{
    /// <summary>Things a kson → ksh export loses (the ksh column of the export loss matrix).</summary>
    public static readonly IReadOnlyList<string> KnownLosses = new[]
    {
        "laser values are quantised to 51 steps (round(v*50)/50)",
        "laser anchor curve types (perfect curve / Bezier): the ksh gets the baked points (1/24 grid, within 0.004 of the path before quantising) and reads back as straight anchors",
        "MXM slot and difficulty display names (ksh difficulty= has no MXM; the filename carries it)",
        "an isolated pair of laser points (or a point and a slam) closer than 1/32 note can read back as a slam (dense runs survive, see KshReader.IsSlam)",
        "laser sections or holds that touch without a gap line merge into one",
        "FX effect parameters without a ksh short form (fx-l=Name;n) keep only the effect name",
        "only one lane-spin/swing per chart line; stop lengths are rounded to 1/192 measure",
        "editor app name/version, explicit chart end beyond the last measure, unknown kson keys (Extra); bookmarks survive as //#bookmark and song sources as //#source comment lines (a source with a line break is dropped)",
        "bg.filename, kson std_bpm 0, empty disp_bpm (ksh writes the derived BPM text)",
        "VOX game effects survive as lane_toggle, bg_no= and //#vox_ comment lines; note script names and VOX row lengths finer than 1/192 measure are lost",
    };

    public static void WriteFile(Chart chart, string path)
        => File.WriteAllText(path, Write(chart), new UTF8Encoding(true));

    public static string Write(Chart chart) => new Builder(chart).Build();

    private sealed class Builder
    {
        private readonly Chart chart;
        private readonly TimingMap timing;
        private readonly SortedSet<long> required = new();
        private readonly Dictionary<long, List<string>> options = new();
        private readonly Dictionary<long, string> suffix = new();
        private readonly SortedDictionary<long, char>[] laserChars = { new(), new() };
        private readonly List<(long Start, long Last)>[] laserSpans = { new(), new() };
        private readonly StringBuilder header = new();
        private long end;

        public Builder(Chart chart)
        {
            this.chart = chart;
            timing = new TimingMap(chart.Beat);
        }

        private void Opt(long y, string line)
        {
            if (!options.TryGetValue(y, out var list)) options[y] = list = new List<string>();
            list.Add(line);
        }

        private void Header(string key, string value) => header.Append(key).Append('=').Append(value).Append("\r\n");

        public string Build()
        {
            WriteHeader();
            CollectTiming();
            CollectNotes();
            CollectLasers();
            CollectCameraAndAudio();
            end = ChartEnd();
            foreach (long y in options.Keys) if (y < end) required.Add(y);

            var sb = new StringBuilder(header.ToString());
            sb.Append("--\r\n");
            WriteBody(sb);
            WriteDefinitions(sb);
            return sb.ToString();
        }

        /// <summary>
        /// The stated end (a ksh file's last bar line) if everything fits before it, else the bar line
        /// after the last event. Options may sit exactly on the end (written after the last line).
        /// </summary>
        private long ChartEnd()
        {
            long lastLine = required.Count > 0 ? required.Max : 0;
            long lastOption = options.Count > 0 ? options.Keys.Max() : 0;
            if (chart.EndPulse is { } ep && ep > 0 && ep > lastLine && ep >= lastOption)
            {
                int m = timing.MeasureAt(ep);
                return timing.MeasureStart(m) == ep ? ep : timing.MeasureStart(m + 1);
            }
            return timing.MeasureStart(timing.MeasureAt(Math.Max(lastLine, lastOption)) + 1);
        }

        // ------------------------------------------------------------ header

        private string DerivedBpmText()
        {
            var bpms = chart.Beat.Bpm.Select(b => b.Bpm).Where(b => b > 0).ToList();
            if (bpms.Count == 0) return "120";
            double lo = bpms.Min(), hi = bpms.Max();
            return lo == hi ? Fmt(lo) : Fmt(lo) + "-" + Fmt(hi);
        }

        private void WriteHeader()
        {
            var m = chart.Meta;
            var a = chart.Audio;
            Header("title", m.Title);
            if (m.TitleImgFilename.Length > 0) Header("title_img", m.TitleImgFilename);
            if (m.TitleTranslit.Length > 0) Header("title_translit", m.TitleTranslit);
            Header("artist", m.Artist);
            if (m.ArtistImgFilename.Length > 0) Header("artist_img", m.ArtistImgFilename);
            if (m.ArtistTranslit.Length > 0) Header("artist_translit", m.ArtistTranslit);
            Header("effect", m.ChartAuthor);
            Header("jacket", m.JacketFilename);
            Header("illustrator", m.JacketAuthor);
            Header("difficulty", Difficulty.KshName(m.DifficultySlot));
            Header("level", Fmt(m.Level));
            Header("t", m.DispBpm.Length > 0 ? m.DispBpm : DerivedBpmText());
            if (m.StdBpm is { } std && std != 0) Header("to", Fmt(std));
            Header("m", string.Join(";", new[] { a.Bgm.Filename }.Concat(a.Bgm.LegacyFpFilenames)));
            if (a.Bgm.Volume != 1.0) Header("mvol", Fmt(Math.Round(a.Bgm.Volume * 100, 6)));
            Header("o", a.Bgm.OffsetMs.ToString(Inv));
            if (chart.Bg.LegacyBg.Count > 0) Header("bg", string.Join(";", chart.Bg.LegacyBg));
            if (chart.Bg.Layer is { } layer)
            {
                int flags = (layer.RotateTilt ? 1 : 0) | (layer.RotateSpin ? 2 : 0);
                Header("layer", layer.DurationMs == 0 && flags == 3
                    ? layer.Filename
                    : $"{layer.Filename};{layer.DurationMs.ToString(Inv)};{flags.ToString(Inv)}");
            }
            if (chart.Bg.Movie is { } movie)
            {
                Header("v", movie.Filename);
                if (movie.OffsetMs != 0) Header("vo", movie.OffsetMs.ToString(Inv));
            }
            Header("po", a.Bgm.PreviewOffsetMs.ToString(Inv));
            Header("plength", a.Bgm.PreviewDurationMs.ToString(Inv));
            if (chart.GaugeTotal != 0) Header("total", chart.GaugeTotal.ToString(Inv));

            var le = a.LaserEffects;
            foreach (var g in le.LegacyFilterGain.Where(g => g.Y == 0)) Header("pfiltergain", Fmt(Math.Round(g.Value * 100, 6)));
            if (le.PeakingFilterDelay is { } pd) Header("pfilterdelay", pd.ToString(Inv));
            foreach (var (name, ys) in le.PulseEvents)
                if (ys.Contains(0)) Header("filtertype", FilterToKsh(name));
            if (a.KeySound.LaserVolumeAuto is { } va) Header("chokkakuautovol", va ? "1" : "0");
            foreach (var v in a.KeySound.LaserVolume.Where(v => v.Y == 0)) Header("chokkakuvol", Fmt(Math.Round(v.Value * 100, 6)));
            if (m.IconFilename.Length > 0) Header("icon", m.IconFilename);
            if (m.Information.Length > 0) Header("information", m.Information);
            Header("ver", chart.Compat.KshVersion.Length > 0 ? chart.Compat.KshVersion : "171");
            if (chart.VoxExtras.BgNo is { } bgNo) Header("bg_no", bgNo.ToString(Inv)); // music_db background, read by ksh2vox
            foreach (var (k, v) in chart.Compat.KshUnknownMeta) Header(k, v);
        }

        // ------------------------------------------------------------ collection

        private void CollectTiming()
        {
            foreach (var b in chart.Beat.Bpm) Opt(b.Y, "t=" + Fmt(b.Bpm));
            foreach (var s in chart.Beat.Stops)
                Opt(s.Y, "stop=" + Math.Round((double)s.Length / Timebase.PulsesPerKsh192Unit).ToString(Inv));
            Graph("scroll_speed", chart.Beat.ScrollSpeed);
        }

        private void Graph(string key, List<GraphPoint> graph)
        {
            foreach (var p in graph)
            {
                if (p.Curve is { } c) Opt(p.Y, $"{key}_curve={Fmt(c.A)};{Fmt(c.B)}");
                Opt(p.Y, key + "=" + Fmt(p.V));
                if (p.Vf is { } vf && vf != p.V) Opt(p.Y, key + "=" + Fmt(vf));
            }
        }

        private void CollectNotes()
        {
            foreach (var lane in chart.Notes.Bt.Concat(chart.Notes.Fx))
                foreach (var n in lane)
                {
                    required.Add(n.Y);
                    if (n.Length > 0) required.Add(n.EndY);
                }

            var fxDefs = chart.Audio.FxEffects.Defs;
            string KshEffect(string name) => fxDefs.Any(d => d.Name == name) ? name : EffectToKsh(name);
            string TypeOf(string name) => fxDefs.LastOrDefault(d => d.Name == name)?.Type ?? name;
            void Effect(int lane, long y, string name, Dictionary<string, string> prm)
            {
                string args = InvocationArgs(TypeOf(name), prm) ?? "";
                Opt(y, $"fx-{(lane == 0 ? 'l' : 'r')}={KshEffect(name)}{(args.Length > 0 ? ";" + args : "")}");
            }
            void Sound(int lane, long y, string name, double? vol)
                => Opt(y, $"fx-{(lane == 0 ? 'l' : 'r')}_se={name}{(vol is { } v ? ";" + Fmt(Math.Round(v * 100, 6)) : "")}");

            for (int lane = 0; lane < 2; lane++)
                foreach (var n in chart.Notes.Fx[lane])
                {
                    foreach (var e in n.Effects) Effect(lane, n.Y + e.RelY, e.Name, e.Params);
                    if (n.KeySound is { } ks) Sound(lane, n.Y, ks.Name, ks.Volume);
                }
            foreach (var o in chart.Audio.FxEffects.OrphanLongEvents) Effect(o.Lane, o.Y, o.Name, o.Params);
            foreach (var o in chart.Audio.KeySound.OrphanFxChipEvents) Sound(o.Lane, o.Y, o.Name, o.Volume);
        }

        private static readonly long[] slamGaps = { 30, 20, 15, 12, 10, 6, 5, 4, 3, 2, 1 };

        private void CollectLasers()
        {
            for (int lane = 0; lane < 2; lane++)
            {
                var sections = chart.Notes.Lasers[lane].Where(s => s.Points.Count > 0).OrderBy(s => s.Y).ToList();
                char side = lane == 0 ? 'l' : 'r';
                for (int si = 0; si < sections.Count; si++)
                {
                    var s = sections[si];
                    long nextSection = si + 1 < sections.Count ? sections[si + 1].Y : long.MaxValue;
                    long lastLine = s.Y;
                    if (s.Width != 1) Opt(s.Y, $"laserrange_{side}={s.Width.ToString(Inv)}x");
                    // a curved anchor path is written as its ksh bake (points only, > 1/32 apart, see LaserBakeOptions.Ksh)
                    var points = LaserPaths.PointsFor(s, LaserBakeOptions.Ksh);
                    for (int k = 0; k < points.Count; k++)
                    {
                        var p = points[k];
                        long y = s.Y + p.Ry;
                        laserChars[lane][y] = LaserCharOf(p.V);
                        required.Add(y);
                        lastLine = y;
                        if (p.Curve is { } c) Opt(y, $"laser_{side}_curve={Fmt(c.A)};{Fmt(c.B)}");
                        if (!p.IsSlam) continue;
                        // second line of the slam, before the next point (and leave room for the '-' gap)
                        long limit = k + 1 < points.Count ? s.Y + points[k + 1].Ry : nextSection - 1;
                        foreach (long d in slamGaps)
                        {
                            if (y + d >= limit) continue;
                            laserChars[lane][y + d] = LaserCharOf(p.Vf!.Value);
                            required.Add(y + d);
                            lastLine = y + d;
                            break;
                        }
                    }
                    laserSpans[lane].Add((s.Y, lastLine));
                }
            }
        }

        private void CollectCameraAndAudio()
        {
            // curved anchor runs as their ksh bake (samples, no curves: ksh2vox reads ksh camera curves as straight lines)
            var cam = chart.Camera;
            foreach (var lane in CameraPaths.All.Where(CameraPaths.IsGraph))
                Graph(CameraPaths.Name(lane), CameraPaths.GraphFor(cam, lane, CameraPaths.KshOptions));
            foreach (var t in CameraPaths.TiltFor(cam, CameraPaths.KshOptions))
            {
                if (t.Curve is { } c) Opt(t.Y, $"tilt_curve={Fmt(c.A)};{Fmt(c.B)}");
                Opt(t.Y, "tilt=" + TiltText(t.Value));
                if (t.Final is { } f && f != t.Value) Opt(t.Y, "tilt=" + TiltText(f));
            }

            void Pattern(CamPattern p, string head)
            {
                string len = Math.Round((double)p.Length / Timebase.PulsesPerKsh192Unit).ToString(Inv);
                suffix.TryAdd(p.Y, head + len);
                required.Add(p.Y);
            }
            foreach (var p in chart.Camera.Spin) Pattern(p, p.Direction < 0 ? "@(" : "@)");
            foreach (var p in chart.Camera.HalfSpin) Pattern(p, p.Direction < 0 ? "@<" : "@>");
            foreach (var p in chart.Camera.Swing)
            {
                string extra = p.SwingScale != null || p.SwingRepeat != null || p.SwingDecayOrder != null
                    ? $";{Fmt(p.SwingScale ?? 250)};{(p.SwingRepeat ?? 3).ToString(Inv)};{(p.SwingDecayOrder ?? 2).ToString(Inv)}"
                    : "";
                string len = Math.Round((double)p.Length / Timebase.PulsesPerKsh192Unit).ToString(Inv);
                suffix.TryAdd(p.Y, (p.Direction < 0 ? "S<" : "S>") + len + extra);
                required.Add(p.Y);
            }

            var a = chart.Audio;
            foreach (var (name, ys) in a.LaserEffects.PulseEvents)
                foreach (long y in ys.Where(y => y != 0)) Opt(y, "filtertype=" + FilterToKsh(name));
            foreach (var g in a.LaserEffects.LegacyFilterGain.Where(g => g.Y != 0)) Opt(g.Y, "pfiltergain=" + Fmt(Math.Round(g.Value * 100, 6)));
            foreach (var v in a.KeySound.LaserVolume.Where(v => v.Y != 0)) Opt(v.Y, "chokkakuvol=" + Fmt(Math.Round(v.Value * 100, 6)));
            foreach (var (name, ys) in a.KeySound.LaserSlamEvents)
                foreach (long y in ys) Opt(y, "chokkakuse=" + SlamSoundToKsh(name));
            foreach (var (effect, prms) in a.FxEffects.ParamChanges)
                foreach (var (param, list) in prms)
                    foreach (var pv in list) Opt(pv.Y, $"fx:{effect}:{ParamToKsh(param)}={pv.Value}");
            foreach (var (effect, prms) in a.LaserEffects.ParamChanges)
                foreach (var (param, list) in prms)
                    foreach (var pv in list) Opt(pv.Y, $"filter:{effect}:{ParamToKsh(param)}={pv.Value}");
            foreach (var (key, list) in chart.Compat.KshUnknownOption)
                foreach (var pv in list) Opt(pv.Y, key + "=" + pv.Value);
            // song sources as comment lines at the chart start (KSM and ksh2vox skip them; KshReader reads them back).
            // A source with a line break or control character cannot be one ksh line and is left out.
            foreach (string url in chart.Editor.SongSources)
                if (url.Length > 0 && !url.Any(char.IsControl)) Opt(0, SongSourceLine.ToKshLine(url));
            foreach (var c in chart.Editor.Comments) Opt(c.Y, "//" + c.Value);
            // bookmarks as comment lines KSM and ksh2vox skip; KshReader reads them back (Bookmark.KshPrefix)
            foreach (var b in chart.Editor.Bookmarks) Opt(b.Y, b.Value.ToKshLine());
            // VOX game effects: lane_toggle for plain LaneY toggles, //#vox_… comment lines for the rest (VoxKshLines)
            foreach (var (y, line) in VoxKshLines.Lines(chart.VoxExtras)) Opt(y, line);
            foreach (var l in chart.Compat.KshUnknownLine) Opt(l.Y, l.Value);
        }

        private static string TiltText(TiltValue v) => v.Auto is { } m ? TiltValue.NameOf(m) : Fmt(v.Manual);

        // ------------------------------------------------------------ body

        private static long Gcd(long a, long b) => b == 0 ? Math.Abs(a) : Gcd(b, a % b);

        /// <summary>Smallest line count N with every offset on a line: N = lcm(len / gcd(offset, len)).</summary>
        private static long LinesFor(IEnumerable<long> offsets, long len)
        {
            long n = 1;
            foreach (long o in offsets)
            {
                long need = len / Gcd(o, len);
                n = n / Gcd(n, need) * need;
            }
            return n;
        }

        private List<(long Start, long Len, long N)> Layout()
        {
            var measures = new List<(long, long, long)>();
            for (int m = 0; ; m++)
            {
                long start = timing.MeasureStart(m);
                if (start >= end) break;
                long len = timing.MeasureLength(m);
                long n = LinesFor(required.GetViewBetween(start, start + len - 1).Select(y => y - start), len);
                measures.Add((start, len, n));
            }
            return measures;
        }

        private static bool HasLineBetween(List<(long Start, long Len, long N)> layout, long a, long b)
        {
            foreach (var (start, len, n) in layout)
            {
                if (start + len <= a || start >= b) continue;
                for (long i = 0; i < n; i++)
                {
                    long y = start + i * len / n;
                    if (y > a && y < b) return true;
                }
            }
            return false;
        }

        private void WriteBody(StringBuilder sb)
        {
            var layout = Layout();
            // A laser section only ends at a '-' line; make sure one exists between touching sections.
            for (int pass = 0; pass < 4; pass++)
            {
                bool added = false;
                for (int lane = 0; lane < 2; lane++)
                    for (int i = 0; i + 1 < laserSpans[lane].Count; i++)
                    {
                        long a = laserSpans[lane][i].Last, b = laserSpans[lane][i + 1].Start;
                        if (b - a >= 2 && !HasLineBetween(layout, a, b))
                        {
                            required.Add(a + (b - a) / 2);
                            added = true;
                        }
                    }
                if (!added) break;
                layout = Layout();
            }

            var bt = chart.Notes.Bt;
            var fx = chart.Notes.Fx;
            int[] btIdx = new int[4], fxIdx = new int[2];
            int[] spanIdx = new int[2];
            (int, int) lastSig = (0, 0);

            for (int m = 0; m < layout.Count; m++)
            {
                var (start, len, n) = layout[m];
                var sig = timing.SignatureOfMeasure(m);
                if (m == 0 || (sig.Numerator, sig.Denominator) != lastSig)
                    sb.Append("beat=").Append(sig.Numerator.ToString(Inv)).Append('/').Append(sig.Denominator.ToString(Inv)).Append("\r\n");
                lastSig = (sig.Numerator, sig.Denominator);

                for (long i = 0; i < n; i++)
                {
                    long y = start + i * len / n;
                    if (options.TryGetValue(y, out var opts))
                        foreach (string o in opts) sb.Append(o).Append("\r\n");

                    for (int k = 0; k < 4; k++) sb.Append(NoteChar(bt[k], ref btIdx[k], y, '1', '2'));
                    sb.Append('|');
                    for (int k = 0; k < 2; k++) sb.Append(NoteChar(fx[k], ref fxIdx[k], y, '2', '1'));
                    sb.Append('|');
                    for (int k = 0; k < 2; k++) sb.Append(LaserLineChar(k, ref spanIdx[k], y));
                    if (suffix.TryGetValue(y, out string? sfx)) sb.Append(sfx);
                    sb.Append("\r\n");
                }
                // options that sit exactly on the chart end
                if (m == layout.Count - 1)
                    foreach (var (y, opts) in options.Where(kv => kv.Key >= start + len).OrderBy(kv => kv.Key))
                        foreach (string o in opts) sb.Append(o).Append("\r\n");
                sb.Append("--\r\n");
            }
        }

        /// <summary>#define_fx / #define_filter lines after the last bar line, as KSM writes them.</summary>
        private void WriteDefinitions(StringBuilder sb)
        {
            void Define(string kind, AudioEffectDef d)
            {
                sb.Append(kind).Append(' ').Append(d.Name).Append(" type=").Append(EffectToKsh(d.Type));
                foreach (var (k, v) in d.Params) sb.Append(';').Append(ParamToKsh(k)).Append('=').Append(v);
                sb.Append("\r\n");
            }
            foreach (var d in chart.Audio.FxEffects.Defs) Define("#define_fx", d);
            foreach (var d in chart.Audio.LaserEffects.Defs) Define("#define_filter", d);
        }

        private static char NoteChar(List<Note> lane, ref int idx, long y, char chip, char hold)
        {
            while (idx < lane.Count && lane[idx].EndY < y) idx++;
            for (int j = idx; j < lane.Count && lane[j].Y <= y; j++)
            {
                var n = lane[j];
                if (n.Length == 0 && n.Y == y) return chip;
                if (n.Length > 0 && n.Y <= y && y < n.EndY) return hold;
            }
            return '0';
        }

        private char LaserLineChar(int lane, ref int idx, long y)
        {
            if (laserChars[lane].TryGetValue(y, out char c)) return c;
            var spans = laserSpans[lane];
            while (idx < spans.Count && spans[idx].Last < y) idx++;
            return idx < spans.Count && spans[idx].Start <= y && y <= spans[idx].Last ? ':' : '-';
        }
    }
}
