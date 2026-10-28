using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Tutorial;

/// <summary>One object the user is asked to reproduce (a ghost in the playfield).</summary>
public sealed record TutorialTarget
{
    public NoteKind Kind { get; init; }

    /// <summary>True for a laser section (then <see cref="Kind"/> is ignored).</summary>
    public bool IsLaser { get; init; }

    public int Lane { get; init; }
    public long Y { get; init; }
    public long Length { get; init; }

    /// <summary>Laser points relative to <see cref="Y"/> (a slam = <see cref="LaserPoint.Vf"/>).</summary>
    public IReadOnlyList<LaserPoint> Points { get; init; } = Array.Empty<LaserPoint>();

    public bool Wide { get; init; }

    /// <summary>Laser: the points are anchors of a curve (drawn as dots, checked for a curved path).</summary>
    public bool Curve { get; init; }

    public long EndY => IsLaser ? Y + (Points.Count > 0 ? Points[^1].Ry : 0) : Y + Length;

    public static TutorialTarget Chip(NoteKind kind, int lane, long y) => new() { Kind = kind, Lane = lane, Y = y };
    public static TutorialTarget Hold(NoteKind kind, int lane, long y, long length) => new() { Kind = kind, Lane = lane, Y = y, Length = length };

    public static TutorialTarget Laser(int lane, long y, bool wide, params LaserPoint[] points) =>
        new() { IsLaser = true, Lane = lane, Y = y, Wide = wide, Points = points };

    public override string ToString() => IsLaser
        ? $"laser {(Lane == 0 ? "L" : "R")} {Y}{(Wide ? " wide" : "")}: {string.Join(" ", Points)}"
        : $"{Kind} {Lane} {(Length > 0 ? $"hold {Y}+{Length}" : $"chip {Y}")}";
}

/// <summary>
/// Placement of tutorial targets and the checks the steps use (ghost matching, free-mapping rules). Shared by every
/// tutorial (classic and main); nothing here knows a song or a chapter.
/// </summary>
public static class TutorialChecks
{
    private const long beat = TutorialSongDefinition.Beat;

    /// <summary>Puts a target into the chart (solutions, tests).</summary>
    public static void Place(Chart chart, TutorialTarget t)
    {
        if (t.IsLaser)
        {
            NoteData.InsertSorted(chart.Notes.Lasers[t.Lane], new LaserSection(t.Y, t.Points.Select(p => p.Clone()), t.Wide ? 2 : 1));
            return;
        }

        NoteData.InsertSorted(chart.Notes.Lane(t.Kind, t.Lane), new Note(t.Y, t.Length));
    }

    // ---- checks --------------------------------------------------------------------------

    /// <summary>Laser value tolerance for a reproduced ghost (2.5 ksh steps).</summary>
    public const double ValueTolerance = 0.06;

    /// <summary>True when the chart holds an object matching <paramref name="t"/>.</summary>
    public static bool IsPlaced(Chart chart, TutorialTarget t)
    {
        if (!t.IsLaser)
            return chart.Notes.Lane(t.Kind, t.Lane).Any(n => n.Y == t.Y && n.Length == t.Length);

        return chart.Notes.Lasers[t.Lane].Any(s => laserMatches(s, t));
    }

    private static bool laserMatches(LaserSection s, TutorialTarget t)
    {
        if (s.Points.Count < 2 || s.Y != t.Y || s.EndY != t.EndY || (s.Width == 2) != t.Wide) return false;

        var first = t.Points[0];
        var last = t.Points[^1];
        if (Math.Abs(s.Points[0].V - first.V) > ValueTolerance || Math.Abs(s.Points[^1].ValueAfter - last.ValueAfter) > ValueTolerance) return false;

        // a slam in the target must be a slam on the same pulse, same direction
        foreach (var p in t.Points.Where(p => p.IsSlam))
        {
            var q = s.Points.FirstOrDefault(x => x.Ry == p.Ry && x.IsSlam);
            if (q == null || Math.Abs(q.V - p.V) > ValueTolerance || Math.Abs(q.ValueAfter - p.ValueAfter) > ValueTolerance) return false;
        }

        // a curve target: the middle anchor must be met (value at its pulse) and the path must be curved
        if (t.Curve)
        {
            if (s.Path == null || !s.Path.Anchors.Any(a => a.Type is LaserCurveType.PerfectCurve or LaserCurveType.Bezier)) return false;
        }

        return true;
    }

    /// <summary>How many of <paramref name="targets"/> are placed.</summary>
    public static int CountPlaced(Chart chart, IEnumerable<TutorialTarget> targets) => targets.Count(t => IsPlaced(chart, t));

    /// <summary>Notes (chips/holds, BT and FX) and laser sections that start in [from, to).</summary>
    public static int CountObjects(Chart chart, long from, long to) =>
        chart.Notes.Bt.Concat(chart.Notes.Fx).Sum(l => l.Count(n => n.Y >= from && n.Y < to))
        + chart.Notes.Lasers.Sum(l => l.Count(s => s.Y >= from && s.Y < to));

    /// <summary>BT chips (not holds) in [from, to) that sit exactly on a beat.</summary>
    public static int CountBeatChips(Chart chart, long from, long to) =>
        chart.Notes.Bt.Sum(l => l.Count(n => n.Length == 0 && n.Y >= from && n.Y < to && n.Y % beat == 0));

    /// <summary>Times (pulses) of objects in [from, to) that are off the 1/<paramref name="divisor"/> grid (starts, hold ends, laser points).</summary>
    public static IReadOnlyList<long> OffGrid(Chart chart, long from, long to, int divisor)
    {
        long step = beat / divisor; // the editor's "1/4" = four snap ticks per beat
        var bad = new List<long>();
        foreach (var n in chart.Notes.Bt.Concat(chart.Notes.Fx).SelectMany(l => l).Where(n => n.Y >= from && n.Y < to))
        {
            if (n.Y % step != 0) bad.Add(n.Y);
            if (n.Length > 0 && n.EndY % step != 0) bad.Add(n.EndY);
        }

        foreach (var s in chart.Notes.Lasers.SelectMany(l => l).Where(s => s.Y >= from && s.Y < to))
        {
            // a curved path bakes extra points between its anchors: only the anchors count
            var pulses = s.Path != null ? s.Path.Anchors.Select(a => s.Y + a.Ry) : s.Points.Select(p => s.Y + p.Ry);
            bad.AddRange(pulses.Where(y => y % step != 0));
        }

        bad.Sort();
        return bad;
    }

    /// <summary>Laser slams that happen in [from, to).</summary>
    public static int CountSlams(Chart chart, long from, long to) =>
        chart.Notes.Lasers.SelectMany(l => l).Sum(s => s.Points.Count(p => p.IsSlam && s.Y + p.Ry >= from && s.Y + p.Ry < to));

    /// <summary>Laser sections that start in [from, to).</summary>
    public static int CountLasers(Chart chart, long from, long to) => chart.Notes.Lasers.Sum(l => l.Count(s => s.Y >= from && s.Y < to));

    /// <summary>Overlapping notes in one lane (a chip inside a hold etc.): the editor refuses most, an imported chart may not.</summary>
    public static int CountOverlaps(Chart chart)
    {
        int n = 0;
        foreach (var lane in chart.Notes.Bt.Concat(chart.Notes.Fx))
        {
            for (int i = 1; i < lane.Count; i++)
                if (lane[i].Y < lane[i - 1].EndY || lane[i].Y == lane[i - 1].Y) n++;
        }

        return n;
    }
}
