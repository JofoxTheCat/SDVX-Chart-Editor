using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tutorial;

/// <summary>
/// The main tutorial's charts. Everything starts from ONE solution chart — the mapper's chart once it ships as
/// <c>solution.kson</c>, until then <see cref="Placeholder"/> — and every chapter's starting chart is that solution with
/// the parts the user rebuilds in this and later chapters cut out (plus a stray chip / a too-short hold to fix).
/// The ghost targets are read from the solution inside the exercise windows of <see cref="MainTutorialSong.Exercises"/>,
/// so a different chart needs no code change as long as each window holds what its exercise asks for
/// (<see cref="Validate"/> says what is missing; docs/TUTORIAL.md has the table).
/// </summary>
public static class MainTutorialCharts
{
    private const long beat = MainTutorialSong.Beat;
    private static long at(int measure, double b = 0) => MainTutorialSong.At(measure, b);

    // ---- the solution ---------------------------------------------------------------------

    /// <summary>
    /// The chart the tutorial rebuilds: <paramref name="solutionKson"/> (the shipped <c>solution.kson</c>) with the
    /// song's timing, names, bookmarks and sources applied, or the placeholder when there is none.
    /// </summary>
    public static Chart Solution(MainTutorialSong def, string? solutionKson = null)
    {
        if (string.IsNullOrWhiteSpace(solutionKson)) return Placeholder(def);

        var chart = KsonReader.Read(solutionKson);
        if (chart.Beat.Bpm.Count != 1 || Math.Abs(chart.Beat.Bpm[0].Bpm - def.Bpm) > 0.001)
            throw new FormatException($"solution.kson: the tutorial needs one constant tempo of {def.Bpm} BPM (tutorial.json)");
        if (chart.Beat.TimeSignatures.Any(t => t.Numerator != 4 || t.Denominator != 4))
            throw new FormatException("solution.kson: the tutorial needs 4/4 throughout");
        applySong(chart, def);
        chart.Notes.SortAll();
        return chart;
    }

    /// <summary>Timing, names, preview, bookmarks and song sources of the tutorial song, no notes.</summary>
    public static Chart Base(MainTutorialSong def, int slot = Difficulty.Exhaust)
    {
        var chart = SongImporter.NewChart(def.Title, def.Artist, def.Audio, slot);
        chart.Meta.Level = slot == Difficulty.Advanced ? 4 : 8;
        applySong(chart, def);
        return chart;
    }

    private static void applySong(Chart chart, MainTutorialSong def)
    {
        chart.Meta.Title = def.Title;
        chart.Meta.Artist = def.Artist;
        chart.Meta.ChartAuthor = def.Charter;
        chart.Meta.Information = def.License;
        chart.Beat.Bpm.Clear();
        chart.Beat.Bpm.Add(new BpmChange(0, def.Bpm));
        chart.Audio.Bgm.Filename = def.Audio;
        chart.Audio.Bgm.OffsetMs = def.OffsetMs;
        chart.Audio.Bgm.PreviewOffsetMs = def.PreviewMs;
        chart.Audio.Bgm.PreviewDurationMs = 10000;
        chart.Editor.SongSources = def.SongSources.ToArray();
        chart.Editor.Bookmarks.Clear();
        foreach (var s in def.Sections)
            chart.Editor.Bookmarks.Add(new PulseValue<Bookmark>(at(s.Measure), new Bookmark(s.Name, s.Colour)));
        chart.EndPulse = at(def.Measures);
    }

    /// <summary>
    /// The placeholder chart (until the mapper's chart arrives): readable for beginners at 235 BPM — quarter notes,
    /// a few eighths, half-bar holds, simple lasers — and laid out on the song's sections (intro bars 1–7, the fill in
    /// bar 8, the build-up 9–15, the drop from bar 16, the last hit in bar 48).
    /// </summary>
    public static Chart Placeholder(MainTutorialSong def)
    {
        var chart = Base(def);
        void chip(int lane, int m, double b) => TutorialChecks.Place(chart, TutorialTarget.Chip(NoteKind.Bt, lane, at(m, b)));
        void hold(int lane, int m, double b, double beats) => TutorialChecks.Place(chart, TutorialTarget.Hold(NoteKind.Bt, lane, at(m, b), (long)Math.Round(beats * beat)));
        void fxChip(int lane, int m, double b) => TutorialChecks.Place(chart, TutorialTarget.Chip(NoteKind.Fx, lane, at(m, b)));
        void fxHold(int lane, int m, double b, double beats) => TutorialChecks.Place(chart, TutorialTarget.Hold(NoteKind.Fx, lane, at(m, b), (long)Math.Round(beats * beat)));
        void laser(int lane, int m, double b, bool wide, params LaserPoint[] points) => TutorialChecks.Place(chart, TutorialTarget.Laser(lane, at(m, b), wide, points));
        void curve(int lane, int m, double b, params (double Beats, double V)[] anchors) =>
            NoteData.InsertSorted(chart.Notes.Lasers[lane], LaserPaths.ToSection(new LaserPath(anchors.Select((a, i) =>
                new LaserAnchor((long)Math.Round(a.Beats * beat), a.V, type: i == 0 ? LaserCurveType.PerfectCurve : null))), at(m, b), 1));
        LaserPoint p(double beats, double v, double? vf = null) => new((long)Math.Round(beats * beat), v, vf);

        // intro (bars 1-7): the chords land on every beat
        for (int b = 0; b < 4; b++) chip(b % 2 == 0 ? 1 : 2, 0, b); // chips (B C B C)
        for (int b = 0; b < 4; b++) chip(b % 2 == 0 ? 2 : 1, 1, b); // paste + mirror (C B C B)
        hold(0, 2, 0, 2);
        hold(3, 2, 2, 2);
        chip(0, 3, 0);
        chip(3, 3, 2);
        fxChip(0, 3, 1);
        fxChip(1, 3, 3);
        fxHold(0, 4, 0, 2);
        chip(2, 4, 2);
        chip(3, 4, 3);
        for (int b = 0; b < 4; b++) chip(3 - b, 5, b); // chip practice
        hold(0, 6, 0, 2); // hold modifier
        hold(3, 6, 2, 2); // hold resize

        // the fill (bar 8): hits on beats 2 and 3, a slam on the first
        laser(1, 7, 1, false, p(0, 1, 0), p(2, 0));
        chip(1, 7, 1);
        chip(2, 7, 2);

        // build-up (bars 9-15)
        laser(0, 8, 0, false, p(0, 0), p(3, 1));
        for (int b = 0; b < 4; b++) chip(b % 2 == 0 ? 2 : 3, 8, b);
        curve(0, 9, 0, (0, 0.2), (2, 0.8), (4, 0.2));
        chip(2, 9, 0);
        chip(3, 9, 2);
        laser(1, 10, 0, true, p(0, 0), p(2, 1));
        chip(0, 10, 2);
        chip(1, 10, 3);
        for (int i = 0; i < 8; i++) chip(i % 4, 11, i * 0.5); // laser practice
        chip(0, 12, 0);
        chip(1, 12, 1);
        laser(1, 12, 2, false, p(0, 0, 1), p(1, 1));
        for (int i = 0; i < 8; i++) chip(i % 2 == 0 ? 1 : 2, 13, i * 0.5);
        for (int i = 0; i < 6; i++) chip(i % 4, 14, i * 0.5);
        fxChip(0, 14, 3);
        fxChip(1, 14, 3);

        // the drop (bars 16-47): a four-bar pattern, a laser sweep every other bar, a slam every eight bars
        for (int m = def.Drop; m < def.Measures - 1; m++)
        {
            switch ((m - def.Drop) % 4)
            {
                case 0:
                    laser(0, m, 0, false, p(0, 0), p(2, 1));
                    for (int b = 0; b < 4; b++) chip(b % 2 == 0 ? 2 : 3, m, b);
                    break;

                case 1:
                    for (int b = 0; b < 4; b++) chip(b, m, b);
                    fxChip(0, m, 3);
                    break;

                case 2:
                    laser(1, m, 0, false, p(0, 1), p(2, 0));
                    for (int b = 0; b < 4; b++) chip(b % 2 == 0 ? 1 : 0, m, b);
                    break;

                default:
                    for (int b = 0; b < 4; b++) chip(3 - b, m, b);
                    if ((m - def.Drop) % 8 == 7) laser(1, m, 3, false, p(0, 0, 1), p(1, 1));
                    else fxChip(1, m, 3);
                    break;
            }
        }

        // the last hit (bar 48, beat 3)
        int last = def.Measures - 1;
        chip(0, last, 0);
        chip(3, last, 0);
        chip(1, last, 2);
        chip(2, last, 2);
        laser(0, last, 2, false, p(0, 0, 1), p(1, 1));

        // F6 exercise of chapter 0: the notes speed up at the drop
        chart.Beat.ScrollSpeed.Add(new GraphPoint(at(def.Drop), 1, def.DropSpeed));
        chart.Notes.SortAll();
        return chart;
    }

    // ---- exercises: targets read from the solution ------------------------------------------

    /// <summary>The ghost targets of every guided exercise, read from <paramref name="solution"/>.</summary>
    public static MainTutorialTargets Targets(MainTutorialSong def, Chart solution)
    {
        var ex = def.Exercises;
        IReadOnlyList<TutorialTarget> notes(NoteKind kind, bool holds, TutorialWindow w) =>
            (kind == NoteKind.Bt ? solution.Notes.Bt : solution.Notes.Fx)
            .SelectMany((lane, i) => lane.Where(n => w.Contains(n.Y) && (n.Length > 0) == holds)
                                         .Select(n => holds ? TutorialTarget.Hold(kind, i, n.Y, n.Length) : TutorialTarget.Chip(kind, i, n.Y)))
            .OrderBy(t => t.Y).ThenBy(t => t.Lane).ToList();

        TutorialTarget? section(TutorialWindow w, Func<LaserSection, bool> where) =>
            solution.Notes.Lasers.SelectMany((lane, i) => lane.Where(s => w.Contains(s.Y) && where(s)).Select(s => (Lane: i, Section: s)))
                .OrderBy(x => x.Section.Y).Select(x => FromSection(x.Lane, x.Section)).FirstOrDefault();

        var chips = notes(NoteKind.Bt, false, ex.Chips);
        var pasted = chips.Select(t => t with { Y = t.Y + MainTutorialSong.Measure }).ToList();
        var resize = notes(NoteKind.Bt, true, ex.HoldResize).FirstOrDefault();

        return new MainTutorialTargets
        {
            StrayChip = TutorialTarget.Chip(NoteKind.Bt, 0, ex.Chips.FromPulse + beat * 3 / 2),
            Chips = chips,
            Pasted = pasted,
            Mirrored = pasted.Select(t => t with { Lane = 3 - t.Lane }).ToList(),
            Holds = notes(NoteKind.Bt, true, ex.Holds),
            FxChips = notes(NoteKind.Fx, false, ex.FxChips),
            FxHold = notes(NoteKind.Fx, true, ex.FxHold),
            ModifierHold = notes(NoteKind.Bt, true, ex.HoldModifier).FirstOrDefault(),
            ResizeHold = resize,
            ResizeStart = resize == null ? null : resize with { Length = Math.Max(beat / 4, resize.Length / 4 / (beat / 4) * (beat / 4)) },
            Slam = section(ex.Slam, s => s.SlamCount > 0 && s.Width == 1),
            StraightLaser = section(ex.StraightLaser, s => s.Points.Count == 2 && s.SlamCount == 0 && s.Width == 1),
            CurveLaser = section(ex.CurveLaser, s => LaserPaths.HasCurvedPath(s) && s.Width == 1),
            WideLaser = section(ex.WideLaser, s => s.Width == 2),
        };
    }

    /// <summary>A laser section as a ghost target: a curved path as its anchors (checked for a curve), else its points.</summary>
    public static TutorialTarget FromSection(int lane, LaserSection s)
    {
        if (LaserPaths.ValidPath(s) is { IsTrivial: false } path)
            return TutorialTarget.Laser(lane, s.Y, s.Width == 2, path.Anchors.Select(a => new LaserPoint(a.Ry, a.V, a.Vf)).ToArray()) with { Curve = true };

        return TutorialTarget.Laser(lane, s.Y, s.Width == 2, s.Points.Select(p => new LaserPoint(p.Ry, p.V, p.Vf)).ToArray());
    }

    /// <summary>What the solution misses for the exercises (empty = the chart fits every window).</summary>
    public static IReadOnlyList<string> Validate(MainTutorialSong def, Chart solution)
    {
        var t = Targets(def, solution);
        var ex = def.Exercises;
        var problems = new List<string>();
        void need(bool ok, string what)
        {
            if (!ok) problems.Add(what);
        }

        need(t.Chips.Count is >= 2 and <= 8, $"chips ({ex.Chips.BarsText}): 2-8 BT chips (has {t.Chips.Count})");
        need(t.Chips.All(c => (c.Y - ex.Chips.FromPulse) % beat == 0), $"chips ({ex.Chips.BarsText}): the chips sit on whole beats (the step sets snap 1/1)");
        need(t.Holds.Count is >= 1 and <= 4, $"holds ({ex.Holds.BarsText}): 1-4 BT holds (has {t.Holds.Count})");
        need(t.FxChips.Count is >= 1 and <= 4, $"fxChips ({ex.FxChips.BarsText}): 1-4 FX chips (has {t.FxChips.Count})");
        need(t.FxHold.Count is >= 1 and <= 2, $"fxHold ({ex.FxHold.BarsText}): 1-2 FX holds (has {t.FxHold.Count})");
        need(t.ModifierHold != null, $"holdModifier ({ex.HoldModifier}): a BT hold");
        need(t.ResizeHold is { Length: >= beat / 2 }, $"holdResize ({ex.HoldResize}): a BT hold of at least half a beat");
        need(t.Slam != null, $"slam ({ex.Slam.BarsText}): a normal-width laser with a slam");
        need(t.StraightLaser != null, $"straightLaser ({ex.StraightLaser.BarsText}): a two-point laser without a slam");
        need(t.CurveLaser is { Points.Count: 3 }, $"curveLaser ({ex.CurveLaser.BarsText}): a laser drawn as a 3-anchor curve");
        need(t.WideLaser != null, $"wideLaser ({ex.WideLaser.BarsText}): a wide laser");
        need(TutorialChecks.CountSlams(solution, 0, at(def.Drop)) >= 1, "a slam before the drop (chapter 4's spin)");
        need(TutorialChecks.CountOverlaps(solution) == 0, "no overlapping notes");

        // a guided target must not sit in a free-mapping window (it would be cleared there)
        foreach (var (name, w) in new[] { ("chipPractice", ex.ChipPractice), ("laserPractice", ex.LaserPractice), ("finalPractice", ex.FinalPractice) })
        {
            foreach (var x in t.All.Where(x => w.Contains(x.Y)))
                problems.Add($"{name} ({w.BarsText}) overlaps the target {x}");
        }

        return problems;
    }

    // ---- chapter charts ---------------------------------------------------------------------

    /// <summary>
    /// The chart chapter <paramref name="chapter"/> starts with: the solution minus everything chapters ≥ it rebuild,
    /// camera work only from chapter 5 on (chapter 4 teaches it), effects only from chapter 6 on (chapter 5).
    /// Chapter 0 starts from the bare audio and has no chart.
    /// </summary>
    public static Chart ForChapter(MainTutorialSong def, Chart solution, int chapter)
    {
        var chart = KsonReader.Read(KsonWriter.Write(solution)); // a deep copy
        var t = Targets(def, solution);
        var ex = def.Exercises;

        if (chapter <= 1)
        {
            foreach (var x in t.Chips.Concat(t.Holds).Concat(t.FxChips).Concat(t.FxHold)) remove(chart, x);
            if (t.ModifierHold != null) remove(chart, t.ModifierHold);
            if (t.ResizeHold != null) remove(chart, t.ResizeHold);
            clear(chart, ex.Paste, lasers: false, fx: false);
            clear(chart, ex.ChipPractice);
            TutorialChecks.Place(chart, t.StrayChip);
            if (t.ResizeStart != null) TutorialChecks.Place(chart, t.ResizeStart);
        }

        if (chapter <= 2)
        {
            foreach (var x in new[] { t.Slam, t.StraightLaser, t.CurveLaser, t.WideLaser }.OfType<TutorialTarget>()) remove(chart, x);
            clear(chart, ex.LaserPractice);
        }

        if (chapter <= 6) clear(chart, ex.FinalPractice);

        if (chapter <= 4) chart.Camera = new CameraInfo();
        if (chapter <= 5)
        {
            foreach (var n in chart.Notes.Fx.SelectMany(l => l))
            {
                n.Effects.Clear();
                n.KeySound = null;
            }

            chart.Audio.LaserEffects = new LaserEffectInfo();
        }

        chart.Notes.SortAll();
        return chart;
    }

    /// <summary>The reference difficulty of chapter 3 (ADV): the solution's BT chips on the downbeats only.</summary>
    public static Chart Reference(MainTutorialSong def, Chart solution)
    {
        var chart = Base(def, Difficulty.Advanced);
        chart.Beat.ScrollSpeed.AddRange(solution.Beat.ScrollSpeed.Select(g => new GraphPoint(g.Y, g.V, g.Vf, g.Curve)));
        for (int m = 0; m < def.Measures; m++)
            TutorialChecks.Place(chart, TutorialTarget.Chip(NoteKind.Bt, m % 2 == 0 ? 1 : 2, at(m)));
        chart.Notes.SortAll();
        return chart;
    }

    private static void remove(Chart chart, TutorialTarget t)
    {
        if (t.IsLaser)
        {
            chart.Notes.Lasers[t.Lane].RemoveAll(s => s.Y == t.Y);
            return;
        }

        chart.Notes.Lane(t.Kind, t.Lane).RemoveAll(n => n.Y == t.Y);
    }

    /// <summary>Removes notes (and lasers) that start in a window: a free-mapping section or a window rebuilt by hand.</summary>
    private static void clear(Chart chart, TutorialWindow w, bool lasers = true, bool fx = true)
    {
        foreach (var lane in fx ? chart.Notes.Bt.Concat(chart.Notes.Fx) : chart.Notes.Bt) lane.RemoveAll(n => w.Contains(n.Y));
        if (lasers)
            foreach (var lane in chart.Notes.Lasers) lane.RemoveAll(s => w.Contains(s.Y));
    }
}

/// <summary>The guided exercises' ghost targets of the main tutorial (read from the solution chart).</summary>
public sealed record MainTutorialTargets
{
    public TutorialTarget StrayChip { get; init; } = new();
    public IReadOnlyList<TutorialTarget> Chips { get; init; } = Array.Empty<TutorialTarget>();
    public IReadOnlyList<TutorialTarget> Pasted { get; init; } = Array.Empty<TutorialTarget>();
    public IReadOnlyList<TutorialTarget> Mirrored { get; init; } = Array.Empty<TutorialTarget>();
    public IReadOnlyList<TutorialTarget> Holds { get; init; } = Array.Empty<TutorialTarget>();
    public IReadOnlyList<TutorialTarget> FxChips { get; init; } = Array.Empty<TutorialTarget>();
    public IReadOnlyList<TutorialTarget> FxHold { get; init; } = Array.Empty<TutorialTarget>();
    public TutorialTarget? ModifierHold { get; init; }
    public TutorialTarget? ResizeHold { get; init; }

    /// <summary>The too-short version of <see cref="ResizeHold"/> chapter 1 starts with.</summary>
    public TutorialTarget? ResizeStart { get; init; }

    public TutorialTarget? Slam { get; init; }
    public TutorialTarget? StraightLaser { get; init; }
    public TutorialTarget? CurveLaser { get; init; }
    public TutorialTarget? WideLaser { get; init; }

    public IEnumerable<TutorialTarget> All => Chips.Concat(Holds).Concat(FxChips).Concat(FxHold)
                                                   .Concat(new[] { ModifierHold, ResizeHold, Slam, StraightLaser, CurveLaser, WideLaser }.OfType<TutorialTarget>());
}
