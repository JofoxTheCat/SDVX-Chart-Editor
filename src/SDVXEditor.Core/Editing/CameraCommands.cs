using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>The five camera graphs of kson <c>camera.cam.body</c>.</summary>
public enum CamGraph
{
    ZoomTop,
    ZoomBottom,
    ZoomSide,
    CenterSplit,
    RotationDeg,
}

public static class CameraGraphs
{
    public static readonly CamGraph[] All = Enum.GetValues<CamGraph>();

    public static List<GraphPoint> Get(CameraInfo cam, CamGraph g) => g switch
    {
        CamGraph.ZoomTop => cam.Body.ZoomTop,
        CamGraph.ZoomBottom => cam.Body.ZoomBottom,
        CamGraph.ZoomSide => cam.Body.ZoomSide,
        CamGraph.CenterSplit => cam.Body.CenterSplit,
        _ => cam.Body.RotationDeg,
    };

    /// <summary>kson / ksh name of the graph.</summary>
    public static string Name(CamGraph g) => g switch
    {
        CamGraph.ZoomTop => "zoom_top",
        CamGraph.ZoomBottom => "zoom_bottom",
        CamGraph.ZoomSide => "zoom_side",
        CamGraph.CenterSplit => "center_split",
        _ => "rotation_deg",
    };

    /// <summary>Value range the editor lane shows (values outside are clipped, not clamped).</summary>
    public static (double Min, double Max) ViewRange(CamGraph g) => CameraPaths.Range((CamPathLane)(int)g) is var r ? (r.Min, r.Max) : default;

    /// <summary>Value snap step of the editor lane (kson stores zoom values as integers in ksh).</summary>
    public static double Step(CamGraph g) => g == CamGraph.RotationDeg ? 1 : 1;

    public static int InsertSorted(List<GraphPoint> list, GraphPoint p)
    {
        int i = list.FindLastIndex(x => x.Y <= p.Y) + 1;
        list.Insert(i, p);
        return i;
    }
}

/// <summary>Adds a point to a camera graph; a second point on the same pulse is refused.</summary>
public sealed class AddGraphPointCommand : IEditCommand
{
    public AddGraphPointCommand(CamGraph graph, GraphPoint point)
    {
        Graph = graph;
        Point = point;
    }

    public CamGraph Graph { get; }
    public GraphPoint Point { get; }
    public string Description => $"Add {CameraGraphs.Name(Graph)} point";

    public void Execute(Chart chart)
    {
        var list = CameraGraphs.Get(chart.Camera, Graph);
        if (list.Any(p => p.Y == Point.Y)) throw new InvalidOperationException($"{CameraGraphs.Name(Graph)} already has a point at {Point.Y}");
        CameraGraphs.InsertSorted(list, Point);
    }

    public void Undo(Chart chart) => CameraGraphs.Get(chart.Camera, Graph).Remove(Point);
}

public sealed class RemoveGraphPointCommand : IEditCommand
{
    private int index;

    public RemoveGraphPointCommand(CamGraph graph, GraphPoint point)
    {
        Graph = graph;
        Point = point;
    }

    public CamGraph Graph { get; }
    public GraphPoint Point { get; }
    public string Description => $"Delete {CameraGraphs.Name(Graph)} point";

    public void Execute(Chart chart)
    {
        var list = CameraGraphs.Get(chart.Camera, Graph);
        index = list.IndexOf(Point);
        if (index < 0) throw new InvalidOperationException("point is not in the graph");
        list.RemoveAt(index);
    }

    public void Undo(Chart chart) => CameraGraphs.Get(chart.Camera, Graph).Insert(index, Point);
}

/// <summary>Sets pulse, value, slam target and curve of a graph point (keeps the graph sorted).</summary>
public sealed class EditGraphPointCommand : IEditCommand
{
    private readonly long y;
    private readonly double v;
    private readonly double? vf;
    private readonly Curve? curve;
    private (long Y, double V, double? Vf, Curve? Curve) old;

    public EditGraphPointCommand(CamGraph graph, GraphPoint point, long y, double v, double? vf, Curve? curve, string? description = null)
    {
        Graph = graph;
        Point = point;
        this.y = y;
        this.v = v;
        this.vf = vf;
        this.curve = curve;
        Description = description ?? $"Edit {CameraGraphs.Name(graph)} point";
    }

    public CamGraph Graph { get; }
    public GraphPoint Point { get; }
    public string Description { get; }

    public void Execute(Chart chart)
    {
        var list = CameraGraphs.Get(chart.Camera, Graph);
        if (!list.Contains(Point)) throw new InvalidOperationException("point is not in the graph");
        if (y != Point.Y && list.Any(p => p != Point && p.Y == y))
            throw new InvalidOperationException($"{CameraGraphs.Name(Graph)} already has a point at {y}");
        old = (Point.Y, Point.V, Point.Vf, Point.Curve);
        Apply(list, y, v, vf, curve);
    }

    public void Undo(Chart chart) => Apply(CameraGraphs.Get(chart.Camera, Graph), old.Y, old.V, old.Vf, old.Curve);

    private void Apply(List<GraphPoint> list, long ny, double nv, double? nvf, Curve? ncurve)
    {
        Point.V = nv;
        Point.Vf = nvf;
        Point.Curve = ncurve;
        if (Point.Y != ny)
        {
            list.Remove(Point);
            Point.Y = ny;
            CameraGraphs.InsertSorted(list, Point);
        }
    }
}

public sealed class AddTiltPointCommand : IEditCommand
{
    public AddTiltPointCommand(TiltPoint point) => Point = point;

    public TiltPoint Point { get; }
    public string Description => "Add tilt point";

    public void Execute(Chart chart)
    {
        var list = chart.Camera.Tilt;
        if (list.Any(p => p.Y == Point.Y)) throw new InvalidOperationException($"tilt already has a point at {Point.Y}");
        list.Insert(list.FindLastIndex(p => p.Y <= Point.Y) + 1, Point);
    }

    public void Undo(Chart chart) => chart.Camera.Tilt.Remove(Point);
}

public sealed class RemoveTiltPointCommand : IEditCommand
{
    private int index;

    public RemoveTiltPointCommand(TiltPoint point) => Point = point;

    public TiltPoint Point { get; }
    public string Description => "Delete tilt point";

    public void Execute(Chart chart)
    {
        index = chart.Camera.Tilt.IndexOf(Point);
        if (index < 0) throw new InvalidOperationException("point is not in the tilt list");
        chart.Camera.Tilt.RemoveAt(index);
    }

    public void Undo(Chart chart) => chart.Camera.Tilt.Insert(index, Point);
}

public sealed class EditTiltPointCommand : IEditCommand
{
    private readonly long y;
    private readonly TiltValue value;
    private readonly TiltValue? final;
    private readonly Curve? curve;
    private (long Y, TiltValue Value, TiltValue? Final, Curve? Curve) old;

    public EditTiltPointCommand(TiltPoint point, long y, TiltValue value, TiltValue? final, Curve? curve)
    {
        Point = point;
        this.y = y;
        this.value = value;
        this.final = final;
        this.curve = curve;
    }

    public TiltPoint Point { get; }
    public string Description => "Edit tilt point";

    public void Execute(Chart chart)
    {
        var list = chart.Camera.Tilt;
        if (!list.Contains(Point)) throw new InvalidOperationException("point is not in the tilt list");
        if (y != Point.Y && list.Any(p => p != Point && p.Y == y)) throw new InvalidOperationException($"tilt already has a point at {y}");
        old = (Point.Y, Point.Value, Point.Final, Point.Curve);
        Apply(list, y, value, final, curve);
    }

    public void Undo(Chart chart) => Apply(chart.Camera.Tilt, old.Y, old.Value, old.Final, old.Curve);

    private void Apply(List<TiltPoint> list, long ny, TiltValue nv, TiltValue? nf, Curve? nc)
    {
        Point.Value = nv;
        Point.Final = nf;
        Point.Curve = nc;
        if (Point.Y != ny)
        {
            list.Remove(Point);
            Point.Y = ny;
            list.Insert(list.FindLastIndex(p => p.Y <= ny) + 1, Point);
        }
    }
}

public sealed class AddPatternCommand : IEditCommand
{
    public AddPatternCommand(CamPatternKind kind, CamPattern pattern)
    {
        Kind = kind;
        Pattern = pattern;
    }

    public CamPatternKind Kind { get; }
    public CamPattern Pattern { get; }
    public string Description => $"Add {CameraCommands.PatternName(Kind)}";

    public void Execute(Chart chart)
    {
        var list = CameraCommands.Patterns(chart.Camera, Kind);
        list.Insert(list.FindLastIndex(p => p.Y <= Pattern.Y) + 1, Pattern);
    }

    public void Undo(Chart chart) => CameraCommands.Patterns(chart.Camera, Kind).Remove(Pattern);
}

public sealed class RemovePatternCommand : IEditCommand
{
    private int index;

    public RemovePatternCommand(CamPatternKind kind, CamPattern pattern)
    {
        Kind = kind;
        Pattern = pattern;
    }

    public CamPatternKind Kind { get; }
    public CamPattern Pattern { get; }
    public string Description => $"Delete {CameraCommands.PatternName(Kind)}";

    public void Execute(Chart chart)
    {
        var list = CameraCommands.Patterns(chart.Camera, Kind);
        index = list.IndexOf(Pattern);
        if (index < 0) throw new InvalidOperationException("pattern is not in the chart");
        list.RemoveAt(index);
    }

    public void Undo(Chart chart) => CameraCommands.Patterns(chart.Camera, Kind).Insert(index, Pattern);
}

/// <summary>Copies all fields of <paramref name="values"/> onto the pattern (keeps the list sorted).</summary>
public sealed class EditPatternCommand : IEditCommand
{
    private readonly CamPattern values;
    private CamPattern old = null!;

    public EditPatternCommand(CamPatternKind kind, CamPattern pattern, CamPattern values)
    {
        Kind = kind;
        Pattern = pattern;
        this.values = values.Clone();
    }

    public CamPatternKind Kind { get; }
    public CamPattern Pattern { get; }
    public string Description => $"Edit {CameraCommands.PatternName(Kind)}";

    public void Execute(Chart chart)
    {
        old = Pattern.Clone();
        Apply(chart, values);
    }

    public void Undo(Chart chart) => Apply(chart, old);

    private void Apply(Chart chart, CamPattern v)
    {
        var list = CameraCommands.Patterns(chart.Camera, Kind);
        if (!list.Remove(Pattern)) throw new InvalidOperationException("pattern is not in the chart");
        Pattern.Y = v.Y;
        Pattern.Direction = v.Direction;
        Pattern.Length = v.Length;
        Pattern.SwingScale = v.SwingScale;
        Pattern.SwingRepeat = v.SwingRepeat;
        Pattern.SwingDecayOrder = v.SwingDecayOrder;
        list.Insert(list.FindLastIndex(p => p.Y <= Pattern.Y) + 1, Pattern);
    }
}

public static class CameraCommands
{
    public static List<CamPattern> Patterns(CameraInfo cam, CamPatternKind kind) => kind switch
    {
        CamPatternKind.Spin => cam.Spin,
        CamPatternKind.HalfSpin => cam.HalfSpin,
        _ => cam.Swing,
    };

    public static string PatternName(CamPatternKind kind) => kind switch
    {
        CamPatternKind.Spin => "spin",
        CamPatternKind.HalfSpin => "half spin",
        _ => "swing",
    };

    /// <summary>
    /// The slam nearest to <paramref name="pulse"/> within <paramref name="tolerance"/> pulses:
    /// its exact pulse and direction (-1 left, 1 right). kson ignores a spin/swing whose pulse and
    /// direction match no slam, so patterns snap to one.
    /// </summary>
    public static (long Y, int Direction)? SlamNear(Chart chart, long pulse, long tolerance)
    {
        (long Y, int Dir)? best = null;
        long bestDist = long.MaxValue;
        foreach (var lane in chart.Notes.Lasers)
            foreach (var s in lane)
            {
                if (s.Y > pulse + tolerance || s.EndY < pulse - tolerance) continue;
                foreach (var p in s.Points)
                {
                    if (!p.IsSlam) continue;
                    long y = s.Y + p.Ry;
                    long d = Math.Abs(y - pulse);
                    if (d <= tolerance && d < bestDist)
                    {
                        bestDist = d;
                        best = (y, p.Vf!.Value > p.V ? 1 : -1);
                    }
                }
            }

        return best;
    }

    /// <summary>True when a slam with the pattern's pulse and direction exists (otherwise the game ignores it).</summary>
    public static bool HasMatchingSlam(Chart chart, CamPattern p) =>
        SlamNear(chart, p.Y, 0) is { } s && s.Direction == (p.Direction < 0 ? -1 : 1);

    /// <summary>Replaces the background block (one undo step; the old block is restored on undo).</summary>
    public static SetValueCommand<BgInfo> SetBg(BgInfo value, string description = "Change background")
        => new(description, c => c.Bg, (c, v) => c.Bg = v, value);

    /// <summary>Replaces the VOX-only editor data (bg_no, post effects, SPCONTROLER rows).</summary>
    public static SetValueCommand<VoxExtrasInfo> SetVoxExtras(VoxExtrasInfo value, string description = "Change VOX extras")
        => new(description, c => c.VoxExtras, (c, v) => c.VoxExtras = v, value);

    public static BgInfo Clone(this BgInfo b) => new()
    {
        Filename = b.Filename,
        LegacyBg = b.LegacyBg.ToList(),
        Layer = b.Layer is { } l ? new BgLayer { Filename = l.Filename, DurationMs = l.DurationMs, RotateTilt = l.RotateTilt, RotateSpin = l.RotateSpin } : null,
        Movie = b.Movie is { } m ? new BgMovie { Filename = m.Filename, OffsetMs = m.OffsetMs } : null,
    };

    /// <summary>KSM background presets (ksh <c>bg=</c>) and layer presets (ksh <c>layer=</c>).</summary>
    public static readonly IReadOnlyList<string> BgPresets = new[]
    {
        "desert", "grass", "night", "deepsea", "sky", "sunset", "ocean", "flame", "cyber", "space", "mars", "cloudy", "fantasy",
    };

    public static readonly IReadOnlyList<string> LayerPresets = new[] { "arrow", "smoke", "wave", "techno", "snow", "sakura", "hidden" };
}
