using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Pure anchor edits for camera runs (<see cref="CamRun"/>), on top of <see cref="LaserPathEditing"/> (type cycling,
/// explicit types, Ctrl+click insert and anchor moves are shared as they are). Pulses are relative to the run's start.
/// Each returns a NEW path or null when the edit is not possible; <see cref="CameraPaths.Replace"/> bakes the result
/// into an undoable command.
/// </summary>
public static class CameraPathEditing
{
    /// <summary>
    /// A click on empty lane space: a new anchor at (<paramref name="ry"/>, <paramref name="v"/>), with
    /// <paramref name="vf"/> an instant jump there. A fresh click starts a segment (auto type: it is straight until
    /// more anchors join it). While placing (<paramref name="chainStart"/> / <paramref name="chainTail"/> = the anchors
    /// of the previous clicks), a click right after the tail extends the chain osu!lazer style: the tail becomes an
    /// inherit control point unless it is the chain's start, a jump or explicitly typed, so the chain is one auto-typed
    /// segment — 2 anchors straight, 3 a perfect curve, 4 and more a Bezier — and the new anchor starts the (straight)
    /// segment to whatever follows. Null when an anchor already sits on that pulse.
    /// </summary>
    public static (LaserPath Path, int Index)? Add(LaserPath path, long ry, double v, double? vf = null, int? chainStart = null, int? chainTail = null)
    {
        if (path.Anchors.Any(a => a.Ry == ry)) return null;
        var p = path.Clone();
        int i = p.Anchors.FindLastIndex(a => a.Ry < ry) + 1;
        var added = new LaserAnchor(ry, v, vf, LaserCurveType.Linear, auto: true);

        if (chainTail is int tail && chainStart is int start && i == tail + 1 && vf == null && tail != start
            && tail >= 0 && tail < p.Count && p.Anchors[tail] is { Auto: true, IsSlam: false } t)
        {
            t.Type = null;
            t.Auto = false;
        }

        p.Anchors.Insert(i, added);
        p.Normalize();
        return (p, p.Anchors.IndexOf(added));
    }

    /// <summary>Sets (or with null removes) the instant jump of an anchor: the segment before ends at V, the next starts at Vf.</summary>
    public static LaserPath? SetJump(LaserPath path, int index, double? vf)
    {
        if (index < 0 || index >= path.Count) return null;
        var p = path.Clone();
        var a = p.Anchors[index];
        a.Vf = vf is double f && f != a.V ? p.Range.Limit(f) : null;
        if (a.Vf != null && a.Type == null)
        {
            a.Type = LaserCurveType.Linear;
            a.Auto = true;
        }

        p.Normalize();
        return p;
    }

    /// <summary>
    /// The kson ease <c>[a, b]</c> (the old Alt-drag handle / the inspector's curve fields) of the straight two-anchor
    /// segment that starts at anchor <paramref name="index"/>; null curve = linear. Null when that segment is not a
    /// plain straight pair (a curve type shapes it instead).
    /// </summary>
    public static LaserPath? SetEase(LaserPath path, int index, Curve? ease)
    {
        if (path.SegmentOf(index) is not { } s || s.Start != index || s.Count != 2 || s.Effective != LaserCurveType.Linear) return null;
        var p = path.Clone();
        p.Anchors[index].Ease = ease is { IsLinear: false } e ? e : null;
        p.Normalize();
        return p;
    }

    /// <summary>Removes an anchor (down to none: the run disappears). A removed first anchor hands its type to an inherit successor.</summary>
    public static LaserPath? Delete(LaserPath path, int index)
    {
        if (index < 0 || index >= path.Count) return null;
        var p = path.Clone();
        var removed = p.Anchors[index];
        p.Anchors.RemoveAt(index);
        if (index == 0 && p.Count > 0 && p.Anchors[0].Type == null)
        {
            p.Anchors[0].Type = removed.Type;
            p.Anchors[0].Auto = removed.Auto;
        }

        p.Normalize();
        return p;
    }

    /// <summary>Index of the anchor at a relative pulse, or -1.</summary>
    public static int IndexAt(LaserPath path, long ry) => path.Anchors.FindIndex(a => a.Ry == ry);
}

/// <summary>
/// The camera lane tool's placement in progress (osu!lazer slider placement, committed to the chart click by click):
/// the anchors of the run as the last click left them — including the auto flags, which an all-straight path does not
/// keep in the chart — and the chain's first and last click. Valid only while the run still bakes to it (an undo or
/// any other edit ends the chain).
/// </summary>
public sealed class CameraChain
{
    public CameraChain(CamPathLane lane, long runY, LaserPath path, long startY, long tailY)
    {
        Lane = lane;
        RunY = runY;
        Path = path;
        StartY = startY;
        TailY = tailY;
    }

    public CamPathLane Lane { get; }
    public long RunY { get; }
    public LaserPath Path { get; }

    /// <summary>Absolute pulse of the chain's first anchor.</summary>
    public long StartY { get; }

    /// <summary>Absolute pulse of the anchor added last.</summary>
    public long TailY { get; }

    private int? signature;

    /// <summary>True while <paramref name="run"/> is still exactly what this chain left (same start, same baked points).</summary>
    public bool Matches(CamRun run) =>
        run.Lane == Lane && run.Y == RunY && Path.Count > 0
        && (signature ??= LaserPaths.Fingerprint(LaserPathBaker.Bake(Path, RunY, CameraPaths.KsonOptions(Lane)))) == LaserPaths.Fingerprint(run.Points);
}

/// <summary>Builds the undoable camera anchor edits (each a <see cref="SetCameraLaneCommand"/> from <see cref="CameraPaths.Replace"/>).</summary>
public static class CameraPathCommands
{
    /// <summary>
    /// A click with the lane tool at (<paramref name="y"/>, <paramref name="v"/>), <paramref name="vf"/> = Shift jump:
    /// adds an anchor to the run it belongs to (<see cref="CameraPaths.TargetFor"/>), extending <paramref name="chain"/>
    /// when it is still valid and the click lands right after its tail. Returns the command (not executed) and the chain
    /// to continue with; null when the pulse is taken or the edit impossible.
    /// </summary>
    public static (SetCameraLaneCommand Command, CameraChain Chain)? Add(Chart chart, CamPathLane lane, long y, double v, double? vf, CameraChain? chain)
    {
        var run = CameraPaths.TargetFor(chart.Camera, lane, y);
        if (run == null) return null;
        bool extend = chain != null && vf == null && chain.Matches(run);
        var anchors = extend ? chain!.Path.Clone() : run.Anchors();
        int? start = extend ? CameraPathEditing.IndexAt(anchors, chain!.StartY - run.Y) : null;
        int? tail = extend ? CameraPathEditing.IndexAt(anchors, chain!.TailY - run.Y) : null;
        if (start < 0 || tail < 0) start = tail = null;

        var added = CameraPathEditing.Add(anchors, y - run.Y, v, vf, start, tail);
        if (added == null) return null;
        var cmd = CameraPaths.Replace(chart.Camera, run, added.Value.Path, $"Add {CameraPaths.Name(lane)} anchor");
        if (cmd?.Result == null) return null;
        bool continues = extend && tail is int t && added.Value.Index == t + 1;
        return (cmd, new CameraChain(lane, cmd.ResultY, cmd.Result, continues ? chain!.StartY : y, y));
    }

    /// <summary>True when <paramref name="pulse"/> lies strictly inside a curved segment of the lane's anchors (a new anchor there would reshape it).</summary>
    public static bool InsideCurve(Chart chart, CamPathLane lane, long pulse)
    {
        if (CameraPaths.RunAt(chart.Camera, lane, pulse) is not { Curved: true } run) return false;
        var path = run.Stored!.Path;
        long ry = pulse - run.Y;
        return path.Segments().Any(s => s.IsCurve && path.Anchors[s.Start].Ry < ry && ry < path.Anchors[s.End].Ry);
    }
}
