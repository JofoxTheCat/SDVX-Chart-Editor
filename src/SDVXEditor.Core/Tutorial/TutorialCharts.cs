using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tutorial;

/// <summary>
/// The tutorial's charts: what each chapter starts with, the ghost targets of the guided exercises and their checks.
/// Everything is placed in measures/beats of <see cref="TutorialSongDefinition"/> (0-based measure indices), so a
/// different song only changes the timing.
/// </summary>
public static class TutorialCharts
{
    private static long at(int measure, double beat = 0) => TutorialSongDefinition.At(measure, beat);
    private const long beat = TutorialSongDefinition.Beat;

    // ---- chapter 1: chips, holds, FX -------------------------------------------------------

    /// <summary>The stray chip chapter 1 starts with (deleted with a right click).</summary>
    public static TutorialTarget StrayChip => TutorialTarget.Chip(NoteKind.Bt, 0, at(0, 1.5));

    /// <summary>Measure 1, the kick drum: four chips on the beats, lanes B C B C.</summary>
    public static IReadOnlyList<TutorialTarget> Chips => new[]
    {
        TutorialTarget.Chip(NoteKind.Bt, 1, at(0, 0)),
        TutorialTarget.Chip(NoteKind.Bt, 2, at(0, 1)),
        TutorialTarget.Chip(NoteKind.Bt, 1, at(0, 2)),
        TutorialTarget.Chip(NoteKind.Bt, 2, at(0, 3)),
    };

    /// <summary>Measure 2 = measure 1 pasted (before the mirror).</summary>
    public static IReadOnlyList<TutorialTarget> Pasted => Chips.Select(t => t with { Y = t.Y + TutorialSongDefinition.Measure }).ToList();

    /// <summary>Measure 2 after Ctrl+H: lanes C B C B.</summary>
    public static IReadOnlyList<TutorialTarget> Mirrored => Pasted.Select(t => t with { Lane = 3 - t.Lane }).ToList();

    /// <summary>Measure 3, the lead: two long notes.</summary>
    public static IReadOnlyList<TutorialTarget> Holds => new[]
    {
        TutorialTarget.Hold(NoteKind.Bt, 0, at(2, 0), 2 * beat),
        TutorialTarget.Hold(NoteKind.Bt, 3, at(2, 2), 2 * beat),
    };

    /// <summary>Measure 4, the snare on 2 and 4.</summary>
    public static IReadOnlyList<TutorialTarget> FxChips => new[]
    {
        TutorialTarget.Chip(NoteKind.Fx, 0, at(3, 1)),
        TutorialTarget.Chip(NoteKind.Fx, 1, at(3, 3)),
    };

    /// <summary>Measure 5: one FX hold.</summary>
    public static IReadOnlyList<TutorialTarget> FxHold => new[] { TutorialTarget.Hold(NoteKind.Fx, 0, at(4, 0), 2 * beat) };

    /// <summary>Chapter 1's own practice: measure 6 (0-based 5), four chips on the beats.</summary>
    public const int ChipPracticeMeasure = 5;

    // ---- chapter 2: lasers ---------------------------------------------------------------

    /// <summary>Breakdown, laser L: a straight sweep left → right over three beats (it ends before laser R's slam on the next downbeat).</summary>
    public static TutorialTarget StraightLaser => TutorialTarget.Laser(0, at(6), false, new LaserPoint(0, 0), new LaserPoint(3 * beat, 1));

    /// <summary>Laser R: a slam from right to left on the downbeat, then held for two beats.</summary>
    public static TutorialTarget SlamLaser => TutorialTarget.Laser(1, at(7), false, new LaserPoint(0, 1, 0), new LaserPoint(2 * beat, 0));

    /// <summary>Speed-up, laser L: three anchors = a perfect-curve arc.</summary>
    public static TutorialTarget CurveLaser => TutorialTarget.Laser(0, at(8), false,
        new LaserPoint(0, 0.2), new LaserPoint(2 * beat, 0.8), new LaserPoint(4 * beat, 0.2)) with { Curve = true };

    /// <summary>A wide laser R from the far left to the far right (half of it lies past the lanes).</summary>
    public static TutorialTarget WideLaser => TutorialTarget.Laser(1, at(9), true, new LaserPoint(0, 0), new LaserPoint(2 * beat, 1));

    /// <summary>Chapter 2's practice: measures 11-12 (0-based 10..11).</summary>
    public static (int From, int To) LaserPractice => (10, 12);

    /// <summary>Chapter 6's free mapping: the last four measures (0-based 12..15).</summary>
    public static (int From, int To) FinalPractice => (12, 16);

    // ---- charts --------------------------------------------------------------------------

    /// <summary>Timing, metadata and section bookmarks of the tutorial song, no notes.</summary>
    public static Chart Base(TutorialSongDefinition def, int slot = Difficulty.Exhaust)
    {
        var chart = SongImporter.NewChart(def.Title, def.Artist, def.Audio, slot);
        chart.Beat.Bpm.Clear();
        chart.Beat.Bpm.Add(new BpmChange(0, def.Bpm));
        chart.Beat.Bpm.Add(new BpmChange(at(def.SpeedUp.Measure), def.SpeedUp.Bpm));
        chart.Audio.Bgm.OffsetMs = def.OffsetMs;
        chart.Audio.Bgm.PreviewOffsetMs = def.PreviewMs;
        chart.Audio.Bgm.PreviewDurationMs = 10000;
        chart.Meta.ChartAuthor = "Tutorial";
        chart.Meta.Level = slot == Difficulty.Advanced ? 4 : 8;
        chart.Meta.Information = def.License;
        chart.Editor.Bookmarks.Add(new PulseValue<Bookmark>(at(0), new Bookmark("intro")));
        chart.Editor.Bookmarks.Add(new PulseValue<Bookmark>(at(2), new Bookmark("main")));
        chart.Editor.Bookmarks.Add(new PulseValue<Bookmark>(at(def.Breakdown.From), new Bookmark("breakdown", "#9b59ff")));
        chart.Editor.Bookmarks.Add(new PulseValue<Bookmark>(at(def.SpeedUp.Measure), new Bookmark("speed-up", "#ff5a5a")));
        chart.EndPulse = at(def.Measures);
        return chart;
    }

    /// <summary>
    /// The chart a chapter starts with: the base plus the solutions of every earlier chapter (chapter 0 starts from
    /// the bare audio and has no chart).
    /// </summary>
    public static Chart ForChapter(TutorialSongDefinition def, int chapter)
    {
        var chart = Base(def);
        if (chapter >= 1) chart.Notes.Bt[StrayChip.Lane].Add(new Note(StrayChip.Y));
        if (chapter >= 2)
        {
            chart.Notes.Bt[StrayChip.Lane].Clear();
            foreach (var t in Chips.Concat(Mirrored).Concat(Holds).Concat(FxChips).Concat(FxHold)) Place(chart, t);
            for (int b = 0; b < 4; b++) Place(chart, TutorialTarget.Chip(NoteKind.Bt, b % 2 == 0 ? 0 : 3, at(ChipPracticeMeasure, b)));
        }

        if (chapter >= 3)
        {
            foreach (var t in new[] { StraightLaser, SlamLaser, CurveLaser, WideLaser }) Place(chart, t);
            var (from, _) = LaserPractice;
            for (int b = 0; b < 8; b++) Place(chart, TutorialTarget.Chip(NoteKind.Bt, b % 4, at(from, b)));
            Place(chart, TutorialTarget.Laser(1, at(from + 1, 2), false, new LaserPoint(0, 0, 1), new LaserPoint(beat, 1)));
        }

        chart.Notes.SortAll();
        return chart;
    }

    /// <summary>The reference difficulty of chapter 3 (ADV): just the kick drum.</summary>
    public static Chart Reference(TutorialSongDefinition def)
    {
        var chart = Base(def, Difficulty.Advanced);
        for (int m = 0; m < def.Measures; m++)
            Place(chart, TutorialTarget.Chip(NoteKind.Bt, m % 2 == 0 ? 1 : 2, at(m)));
        chart.Notes.SortAll();
        return chart;
    }

    // ---- the generic placement and checks live in TutorialChecks (shared with the main tutorial) ----------------

    /// <summary>Puts a target into the chart (solutions, tests).</summary>
    public static void Place(Chart chart, TutorialTarget t) => TutorialChecks.Place(chart, t);

    /// <summary>Laser value tolerance for a reproduced ghost (2.5 ksh steps).</summary>
    public const double ValueTolerance = TutorialChecks.ValueTolerance;

    public static bool IsPlaced(Chart chart, TutorialTarget t) => TutorialChecks.IsPlaced(chart, t);
    public static int CountPlaced(Chart chart, IEnumerable<TutorialTarget> targets) => TutorialChecks.CountPlaced(chart, targets);
    public static int CountObjects(Chart chart, long from, long to) => TutorialChecks.CountObjects(chart, from, to);
    public static int CountBeatChips(Chart chart, long from, long to) => TutorialChecks.CountBeatChips(chart, from, to);
    public static IReadOnlyList<long> OffGrid(Chart chart, long from, long to, int divisor) => TutorialChecks.OffGrid(chart, from, to, divisor);
    public static int CountSlams(Chart chart, long from, long to) => TutorialChecks.CountSlams(chart, from, to);
    public static int CountLasers(Chart chart, long from, long to) => TutorialChecks.CountLasers(chart, from, to);
    public static int CountOverlaps(Chart chart) => TutorialChecks.CountOverlaps(chart);
}
