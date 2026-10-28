namespace SDVXEditor.Core.Charts;

/// <summary>The camera curves that can be edited as anchor paths: the five kson graphs and manual tilt.</summary>
/// <remarks>The values equal <c>CamGraph</c> 0..4 (and the camera tab's lane order); <see cref="Tilt"/> = 5.</remarks>
public enum CamPathLane
{
    ZoomTop,
    ZoomBottom,
    ZoomSide,
    CenterSplit,
    RotationDeg,
    Tilt,
}

/// <summary>
/// Stored anchors of one camera run (<see cref="CamRun"/>): <see cref="Y"/> = pulse of the first anchor, the path's
/// anchor pulses are relative to it (like a laser section).
/// </summary>
public sealed class CameraAnchorPath
{
    public CameraAnchorPath(CamPathLane lane, long y, LaserPath path)
    {
        Lane = lane;
        Y = y;
        Path = path;
    }

    public CamPathLane Lane { get; }
    public long Y { get; }
    public LaserPath Path { get; }

    public CameraAnchorPath Clone() => new(Lane, Y, Path.Clone());

    public override string ToString() => $"{CameraPaths.Name(Lane)} @{Y}: {Path.Count} anchors";
}

/// <summary>
/// One continuous stretch of a camera curve, the camera counterpart of a <see cref="LaserSection"/>: a graph lane is one
/// run (all its points), manual tilt has one run per stretch of manual values that interpolate into each other.
/// <see cref="Points"/> are the chart's points relative to <see cref="Y"/>; <see cref="Start"/>/<see cref="Count"/>
/// locate them in the lane's list. A tilt run may begin at a point that switches from an auto mode to a manual value
/// (<see cref="HeadAuto"/>) and end at one that switches from a manual value to an auto mode (<see cref="TailAuto"/>);
/// those modes are kept when the run is rebuilt.
/// </summary>
public sealed class CamRun
{
    public CamRun(CamPathLane lane, long y, int start, List<LaserPoint> points, CameraAnchorPath? stored = null,
                  TiltMode? headAuto = null, TiltMode? tailAuto = null)
    {
        Lane = lane;
        Y = y;
        Start = start;
        Points = points;
        Stored = stored;
        HeadAuto = headAuto;
        TailAuto = tailAuto;
    }

    public CamPathLane Lane { get; }
    public long Y { get; }
    public int Start { get; }
    public int Count => Points.Count;
    public List<LaserPoint> Points { get; }

    /// <summary>The stored anchors when they are still valid for <see cref="Points"/>, else null.</summary>
    public CameraAnchorPath? Stored { get; }

    public TiltMode? HeadAuto { get; }
    public TiltMode? TailAuto { get; }

    public bool IsEmpty => Points.Count == 0;
    public long EndY => Points.Count == 0 ? Y : Y + Points[^1].Ry;

    /// <summary>True when the run has stored anchors with curve types (its points are a bake, not the anchors).</summary>
    public bool Curved => Stored is { Path.IsTrivial: false };

    /// <summary>A copy of the anchors to edit: the stored path, else one straight anchor per point (kson curves kept as ease).</summary>
    public LaserPath Anchors() => Stored?.Path.Clone() ?? LaserPath.FromPoints(Points, CameraPaths.Range(Lane));

    public override string ToString() => $"{CameraPaths.Name(Lane)} run @{Y} ({Count} points{(Curved ? ", curved" : "")})";
}

/// <summary>
/// Anchor paths (osu!lazer slider editing, <see cref="LaserPath"/>) for the camera curves. The chart keeps plain kson
/// graph / tilt points; a curved run stores its anchors in <see cref="CameraInfo.Paths"/> and its points are the bake.
/// Same rules as <see cref="LaserPaths"/>: a stored path is trusted only while a fingerprint of the run's points equals
/// its bake's (anything that edits the points directly makes it stale: the run then shows one straight anchor per
/// point); trivial paths (every segment straight) are not stored.
/// <para>
/// Value axes and bake error bounds per lane (the curve plane maps the lane's view range to one unit, so a perfect
/// curve is a circle as drawn in the 104 px lane):
/// </para>
/// <list type="table">
/// <item>zoom_top / zoom_bottom / zoom_side: range ±300, bound 0.5 units (zoom_top 100 ≈ 12.7° pitch → ≤ 0.07°;
/// zoom_bottom 100 ≈ ×0.61 distance → ≤ 0.3 %; zoom_side 100 = 0.36 track widths → ≤ 0.002).</item>
/// <item>center_split: range ±200, bound 0.5 units (100 = one BT lane of gap → ≤ 0.005 lanes).</item>
/// <item>rotation_deg: range ±180, bound 0.1°.</item>
/// <item>tilt (manual): range ±2, bound 0.005 (±1 = a laser at the edge; 10° per unit → ≤ 0.05° roll).</item>
/// </list>
/// The bounds hold on the kson/VOX bake (5-pulse grid = one VOX tick) and the ksh bake (10-pulse grid, no kson
/// curves: ksh2vox ignores ksh camera curves) wherever the curve does not move faster than the grid allows.
/// </summary>
public static class CameraPaths
{
    public static readonly CamPathLane[] All = Enum.GetValues<CamPathLane>();

    public static bool IsGraph(CamPathLane lane) => lane != CamPathLane.Tilt;

    public static string Name(CamPathLane lane) => lane switch
    {
        CamPathLane.ZoomTop => "zoom_top",
        CamPathLane.ZoomBottom => "zoom_bottom",
        CamPathLane.ZoomSide => "zoom_side",
        CamPathLane.CenterSplit => "center_split",
        CamPathLane.RotationDeg => "rotation_deg",
        _ => "tilt",
    };

    public static CamPathLane? Parse(string name) => All.Cast<CamPathLane?>().FirstOrDefault(l => Name(l!.Value) == name);

    /// <summary>The lane's value axis = the camera tab's view range, unclamped (kson allows any value).</summary>
    public static PathRange Range(CamPathLane lane) => lane switch
    {
        CamPathLane.RotationDeg => rotation,
        CamPathLane.CenterSplit => split,
        CamPathLane.Tilt => tilt,
        _ => zoom,
    };

    private static readonly PathRange zoom = new(-300, 300, false);
    private static readonly PathRange split = new(-200, 200, false);
    private static readonly PathRange rotation = new(-180, 180, false);
    private static readonly PathRange tilt = new(-2, 2, false);

    /// <summary>Maximum value error of a bake against the anchors' curve, in the lane's own units (see the class remarks).</summary>
    public static double Tolerance(CamPathLane lane) => lane switch
    {
        CamPathLane.RotationDeg => 0.1,
        CamPathLane.Tilt => 0.005,
        _ => 0.5,
    };

    /// <summary>Chart / kson / VOX: exact kson curves for 3-anchor Beziers, otherwise samples on the 5-pulse VOX tick grid.</summary>
    public static LaserBakeOptions KsonOptions(CamPathLane lane) => kson[(int)lane];

    /// <summary>ksh: no curves (ksh2vox reads ksh camera curves as straight lines), samples on a 10-pulse grid.</summary>
    public static LaserBakeOptions KshOptions(CamPathLane lane) => ksh[(int)lane];

    private static readonly LaserBakeOptions[] kson = Enum.GetValues<CamPathLane>().Select(l => new LaserBakeOptions(true, 5, 5, 5, Tolerance(l))).ToArray();
    private static readonly LaserBakeOptions[] ksh = Enum.GetValues<CamPathLane>().Select(l => new LaserBakeOptions(false, 10, 10, 10, Tolerance(l))).ToArray();

    public static List<GraphPoint> Graph(CameraInfo cam, CamPathLane lane) => lane switch
    {
        CamPathLane.ZoomTop => cam.Body.ZoomTop,
        CamPathLane.ZoomBottom => cam.Body.ZoomBottom,
        CamPathLane.ZoomSide => cam.Body.ZoomSide,
        CamPathLane.CenterSplit => cam.Body.CenterSplit,
        CamPathLane.RotationDeg => cam.Body.RotationDeg,
        _ => throw new ArgumentException("tilt is not a graph", nameof(lane)),
    };

    // ---- runs ------------------------------------------------------------------

    private static bool manualIn(TiltPoint t) => !t.Value.IsAuto;
    private static bool manualOut(TiltPoint t) => !(t.Final ?? t.Value).IsAuto;

    /// <summary>The runs of a lane in time order (a graph: none when empty, else exactly one).</summary>
    public static List<CamRun> Runs(CameraInfo cam, CamPathLane lane)
    {
        var runs = new List<CamRun>();
        if (IsGraph(lane))
        {
            var g = Graph(cam, lane);
            if (g.Count == 0) return runs;
            long y = g[0].Y;
            var pts = g.Select(p => new LaserPoint(p.Y - y, p.V, p.Vf is { } f && f != p.V ? f : null, p.Curve)).ToList();
            runs.Add(new CamRun(lane, y, 0, pts, validStored(cam, lane, y, pts)));
            return runs;
        }

        var list = cam.Tilt;
        int i = 0;
        while (i < list.Count)
        {
            var first = list[i];
            if (!manualIn(first) && !manualOut(first))
            {
                i++;
                continue;
            }

            int j = i;
            while (j + 1 < list.Count && manualOut(list[j]) && manualIn(list[j + 1]) && list[j + 1].Y > list[j].Y) j++;

            long y = first.Y;
            var pts = new List<LaserPoint>();
            for (int k = i; k <= j; k++)
            {
                var t = list[k];
                double v = t.Value.IsAuto ? (t.Final ?? t.Value).Manual : t.Value.Manual;
                double? vf = !t.Value.IsAuto && t.Final is { IsAuto: false } f && f.Manual != v ? f.Manual : null;
                pts.Add(new LaserPoint(t.Y - y, v, vf, manualOut(t) ? t.Curve : null));
            }

            TiltMode? head = first.Value.Auto;
            TiltMode? tail = manualIn(list[j]) && list[j].Final is { Auto: { } m } ? m : null;
            runs.Add(new CamRun(lane, y, i, pts, validStored(cam, lane, y, pts), head, tail));
            i = j + 1;
        }

        return runs;
    }

    /// <summary>The run whose time span contains <paramref name="pulse"/> (inclusive), or null.</summary>
    public static CamRun? RunAt(CameraInfo cam, CamPathLane lane, double pulse) =>
        Runs(cam, lane).FirstOrDefault(r => pulse >= r.Y && pulse <= r.EndY);

    /// <summary>
    /// The run a new point at <paramref name="y"/> belongs to: a graph's only run; for tilt the run it would interpolate
    /// from (the previous point carries a manual value on) or into (the next point is manual), else a new, empty run
    /// at its place in the list. Null when the lane already has a point at that pulse.
    /// </summary>
    public static CamRun? TargetFor(CameraInfo cam, CamPathLane lane, long y)
    {
        if (IsGraph(lane))
        {
            var g = Graph(cam, lane);
            if (g.Any(p => p.Y == y)) return null;
            return Runs(cam, lane).FirstOrDefault() ?? new CamRun(lane, y, 0, new List<LaserPoint>());
        }

        var list = cam.Tilt;
        if (list.Any(t => t.Y == y)) return null;
        int prev = list.FindLastIndex(t => t.Y < y);
        var runs = Runs(cam, lane);
        if (prev >= 0 && manualOut(list[prev])) return runs.First(r => prev >= r.Start && prev < r.Start + r.Count);
        if (prev + 1 < list.Count && manualIn(list[prev + 1])) return runs.First(r => r.Start == prev + 1);
        return new CamRun(lane, y, prev + 1, new List<LaserPoint>());
    }

    private static CameraAnchorPath? validStored(CameraInfo cam, CamPathLane lane, long y, List<LaserPoint> pts)
    {
        foreach (var s in cam.Paths)
        {
            if (s.Lane != lane || s.Y != y || s.Path.Count < 2) continue;
            s.Path.Range = Range(lane);
            s.Path.Signature ??= LaserPaths.Fingerprint(LaserPathBaker.Bake(s.Path, y, KsonOptions(lane)));
            if (s.Path.Signature == LaserPaths.Fingerprint(pts)) return s;
        }

        return null;
    }

    // ---- edits -----------------------------------------------------------------

    /// <summary>
    /// The undoable command that replaces <paramref name="run"/> by the bake of <paramref name="edited"/> (normalised,
    /// rebased onto its first anchor, stored unless trivial); an empty path removes the run. Null when the edit is not
    /// possible: a pulse below 0, or a tilt run that would reach or cross the tilt points around it.
    /// </summary>
    public static SetCameraLaneCommand? Replace(CameraInfo cam, CamRun run, LaserPath? edited, string description)
    {
        if (edited == null) return null;
        var lane = run.Lane;
        var baked = new List<LaserPoint>();
        long y0 = run.Y;
        CameraAnchorPath? store = null;

        if (edited.Count > 0)
        {
            var p = edited.Clone();
            p.Range = Range(lane);
            p.Normalize();
            long d = p.Anchors[0].Ry;
            y0 = run.Y + d;
            if (y0 < 0) return null;
            if (d != 0)
                foreach (var a in p.Anchors) a.Ry -= d;

            baked = LaserPathBaker.Bake(p, y0, KsonOptions(lane));
            if (!p.IsTrivial)
            {
                p.Signature = LaserPaths.Fingerprint(baked);
                store = new CameraAnchorPath(lane, y0, p);
            }
        }

        var result = edited.Count > 0 ? (store?.Path ?? normalisedCopy(edited, lane, run.Y)) : null;
        var paths = cam.Paths.Where(s => s.Lane == lane && !IsGraph(lane) && s.Y != run.Y).ToList();
        if (store != null) paths.Add(store);

        if (IsGraph(lane))
        {
            var graph = baked.Select(pt => new GraphPoint(y0 + pt.Ry, pt.V, pt.IsSlam ? pt.Vf : null, pt.Curve)).ToList();
            return new SetCameraLaneCommand(description, lane, graph, null, paths) { Result = result, ResultY = y0 };
        }

        var list = cam.Tilt;
        var before = list.Take(run.Start).ToList();
        var after = list.Skip(run.Start + run.Count).ToList();
        if (baked.Count > 0)
        {
            if (before.Count > 0 && before[^1].Y >= y0) return null;
            if (after.Count > 0 && after[0].Y <= y0 + baked[^1].Ry) return null;
        }

        var tiltPoints = new List<TiltPoint>();
        for (int k = 0; k < baked.Count; k++)
        {
            var pt = baked[k];
            long y = y0 + pt.Ry;
            bool last = k == baked.Count - 1;
            if (k == 0 && run.HeadAuto is { } head)
                tiltPoints.Add(new TiltPoint(y, TiltValue.Of(head), TiltValue.Of(pt.ValueAfter), last ? null : pt.Curve));
            else if (last && run.TailAuto is { } tail)
                tiltPoints.Add(new TiltPoint(y, TiltValue.Of(pt.V), TiltValue.Of(tail)));
            else
                tiltPoints.Add(new TiltPoint(y, TiltValue.Of(pt.V), pt.IsSlam ? TiltValue.Of(pt.Vf!.Value) : null, pt.Curve));
        }

        return new SetCameraLaneCommand(description, lane, null, before.Concat(tiltPoints).Concat(after).ToList(), paths) { Result = result, ResultY = y0 };
    }

    /// <summary>The edited path as <see cref="Replace"/> bakes it: normalised and rebased onto its first anchor.</summary>
    private static LaserPath normalisedCopy(LaserPath edited, CamPathLane lane, long y)
    {
        var p = edited.Clone();
        p.Range = Range(lane);
        p.Normalize();
        long d = p.Anchors[0].Ry;
        if (d != 0)
            foreach (var a in p.Anchors) a.Ry -= d;
        return p;
    }

    /// <summary>True when <paramref name="path"/> (anchors relative to <paramref name="y"/>) bakes to exactly the run's points.</summary>
    public static bool BakesTo(LaserPath path, long y, CamRun run) =>
        run.Y == y && path.Count > 0 && LaserPaths.Fingerprint(LaserPathBaker.Bake(path, y, KsonOptions(run.Lane))) == LaserPaths.Fingerprint(run.Points);

    // ---- formats ----------------------------------------------------------------

    /// <summary>The stored paths that are valid for the chart's current points (what the kson writer stores).</summary>
    public static IEnumerable<CameraAnchorPath> ValidPaths(CameraInfo cam) =>
        All.SelectMany(l => Runs(cam, l)).Where(r => r.Curved).Select(r => r.Stored!);

    /// <summary>
    /// Attaches a path read from a file to the run of <paramref name="lane"/> starting at <paramref name="y"/> when its bake
    /// matches that run's points (values within 1e-6); false otherwise (the points were edited elsewhere).
    /// </summary>
    public static bool TryAttach(CameraInfo cam, CamPathLane lane, long y, LaserPath path)
    {
        if (path.Count < 2) return false;
        var run = Runs(cam, lane).FirstOrDefault(r => r.Y == y);
        if (run == null) return false;
        var p = path.Clone();
        p.Range = Range(lane);
        p.Normalize();
        if (p.Anchors[0].Ry != 0) return false;
        var baked = LaserPathBaker.Bake(p, y, KsonOptions(lane));
        if (!LaserPaths.SamePoints(baked, run.Points, 1e-6)) return false;
        p.Signature = LaserPaths.Fingerprint(run.Points);
        cam.Paths.RemoveAll(s => s.Lane == lane && s.Y == y);
        cam.Paths.Add(new CameraAnchorPath(lane, y, p));
        return true;
    }

    /// <summary>A graph's points for a format: curved runs baked with <paramref name="options"/>, otherwise the chart's points.</summary>
    public static List<GraphPoint> GraphFor(CameraInfo cam, CamPathLane lane, Func<CamPathLane, LaserBakeOptions> options)
    {
        var run = Runs(cam, lane).FirstOrDefault();
        if (run is not { Curved: true }) return Graph(cam, lane);
        return LaserPathBaker.Bake(run.Stored!.Path, run.Y, options(lane))
                             .Select(pt => new GraphPoint(run.Y + pt.Ry, pt.V, pt.IsSlam ? pt.Vf : null, pt.Curve)).ToList();
    }

    /// <summary>The tilt points for a format: curved manual runs baked with <paramref name="options"/>, the rest as they are.</summary>
    public static List<TiltPoint> TiltFor(CameraInfo cam, Func<CamPathLane, LaserBakeOptions> options)
    {
        var runs = Runs(cam, CamPathLane.Tilt);
        if (!runs.Any(r => r.Curved)) return cam.Tilt;
        var copy = new CameraInfo { Tilt = cam.Tilt.ToList(), Paths = cam.Paths.Select(p => p.Clone()).ToList() };
        // replace from the last run backwards so the earlier runs' list positions stay valid
        foreach (var run in Runs(copy, CamPathLane.Tilt).Where(r => r.Curved).Reverse())
        {
            var baked = LaserPathBaker.Bake(run.Stored!.Path, run.Y, options(CamPathLane.Tilt));
            var points = new List<TiltPoint>();
            for (int k = 0; k < baked.Count; k++)
            {
                var pt = baked[k];
                bool last = k == baked.Count - 1;
                long y = run.Y + pt.Ry;
                if (k == 0 && run.HeadAuto is { } head) points.Add(new TiltPoint(y, TiltValue.Of(head), TiltValue.Of(pt.ValueAfter), last ? null : pt.Curve));
                else if (last && run.TailAuto is { } tail) points.Add(new TiltPoint(y, TiltValue.Of(pt.V), TiltValue.Of(tail)));
                else points.Add(new TiltPoint(y, TiltValue.Of(pt.V), pt.IsSlam ? TiltValue.Of(pt.Vf!.Value) : null, pt.Curve));
            }

            copy.Tilt.RemoveRange(run.Start, run.Count);
            copy.Tilt.InsertRange(run.Start, points);
        }

        return copy.Tilt;
    }

    /// <summary>Largest value difference between a run's anchors and a bake of them (tests, smoke report).</summary>
    public static double MaxError(LaserPath path, long y, IReadOnlyList<LaserPoint> points) => LaserPathBaker.MaxError(path, y, points, y);
}

/// <summary>
/// Replaces one camera lane's points (a graph or the tilt list) and its stored anchor paths; undo puts the exact old point
/// objects and paths back. Built by <see cref="CameraPaths.Replace"/>.
/// </summary>
public sealed class SetCameraLaneCommand : SDVXEditor.Core.Editing.IEditCommand
{
    private readonly List<GraphPoint>? graph;
    private readonly List<TiltPoint>? tilt;
    private readonly List<CameraAnchorPath> paths;
    private List<GraphPoint>? oldGraph;
    private List<TiltPoint>? oldTilt;
    private List<CameraAnchorPath> oldPaths = new();

    public SetCameraLaneCommand(string description, CamPathLane lane, List<GraphPoint>? graph, List<TiltPoint>? tilt, List<CameraAnchorPath> paths)
    {
        Description = description;
        Lane = lane;
        this.graph = graph;
        this.tilt = tilt;
        this.paths = paths;
    }

    public string Description { get; }
    public CamPathLane Lane { get; }

    /// <summary>The edited run's anchors as baked (normalised, pulses relative to <see cref="ResultY"/>); null when the run was removed.</summary>
    public LaserPath? Result { get; init; }

    /// <summary>Pulse of the edited run's first anchor.</summary>
    public long ResultY { get; init; }

    /// <summary>The lane's points after the edit (graph lanes).</summary>
    public IReadOnlyList<GraphPoint>? GraphPoints => graph;

    /// <summary>The tilt list after the edit (tilt lane).</summary>
    public IReadOnlyList<TiltPoint>? TiltPoints => tilt;

    public void Execute(Chart chart)
    {
        var cam = chart.Camera;
        if (graph != null)
        {
            var list = CameraPaths.Graph(cam, Lane);
            oldGraph = list.ToList();
            list.Clear();
            list.AddRange(graph);
        }

        if (tilt != null)
        {
            oldTilt = cam.Tilt.ToList();
            cam.Tilt.Clear();
            cam.Tilt.AddRange(tilt);
        }

        oldPaths = cam.Paths.Where(p => p.Lane == Lane).ToList();
        cam.Paths.RemoveAll(p => p.Lane == Lane);
        cam.Paths.AddRange(paths);
    }

    public void Undo(Chart chart)
    {
        var cam = chart.Camera;
        if (oldGraph != null)
        {
            var list = CameraPaths.Graph(cam, Lane);
            list.Clear();
            list.AddRange(oldGraph);
        }

        if (oldTilt != null)
        {
            cam.Tilt.Clear();
            cam.Tilt.AddRange(oldTilt);
        }

        cam.Paths.RemoveAll(p => p.Lane == Lane);
        cam.Paths.AddRange(oldPaths);
    }
}
