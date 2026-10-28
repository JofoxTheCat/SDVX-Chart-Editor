using osu.Framework.Bindables;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>Which curve lane of the camera tab (graphs 0..4, tilt, patterns).</summary>
public enum CamLane
{
    ZoomTop,
    ZoomBottom,
    ZoomSide,
    CenterSplit,
    RotationDeg,
    Tilt,
    Patterns,
}

/// <summary>A pointer over a camera lane: audio time, lane, x fraction across the lane (0..1).</summary>
public readonly record struct CamPointer(double Ms, CamLane Lane, double Fx);

/// <summary>An anchor of a camera run: the run, the index into its anchors (<see cref="CameraLaneEditor.AnchorsOf"/>), and
/// whether the jump target (Vf) handle was hit.</summary>
public readonly record struct CamAnchorHit(CamRun Run, int Index, bool Vf);

/// <summary>
/// The camera-lane tools, independent of drawables (the smoke test drives the same methods). The five graph lanes and
/// manual tilt are anchor paths (osu!lazer slider editing, like lasers: <see cref="CameraPaths"/>):
/// <list type="bullet">
/// <item>Click on empty lane space adds an anchor at the snapped pulse (value from x) and keeps dragging it. Clicks in a
/// row are a placement chain: 2 anchors straight, 3 a perfect curve, 4+ a Bezier; while placing, a click (without
/// moving) on an anchor of that run cycles its type. Right click on empty space after the chain's tail puts a last
/// anchor there and ends the chain (osu!), elsewhere it just ends it.</item>
/// <item>Drag an anchor: time + value (between its neighbours). Shift on an anchor / on empty space: instant jump (drags
/// the target value). Ctrl+click on a curve inserts an inherit anchor. Alt-drag inside a straight two-anchor segment
/// shapes its kson ease curve (the quick way for a plain pair).</item>
/// <item>Right click on an anchor: Straight / Perfect curve / Bezier / Inherit / Delete; on a segment: its curve type
/// (<see cref="ContextMenuRequested"/>).</item>
/// </list>
/// Auto tilt modes and spin/swing patterns keep their point tools (right click deletes them). Drags are one undo step
/// (transaction, rolled back and re-executed per step, committed on release).
/// </summary>
public sealed class CameraLaneEditor
{
    public const float LANE_WIDTH = 104;
    public const float HIT_PX = 9;

    /// <summary>Manual tilt range shown by the tilt lane (±1 = laser at the edge).</summary>
    public const double TILT_VIEW = 2;

    private readonly EditorSession session;

    public readonly BindableInt Divisor = new BindableInt(4);
    public readonly BindableBool GridSnap = new BindableBool(true);

    /// <summary>Kind and direction new patterns get when no slam is near.</summary>
    public readonly Bindable<CamPatternKind> PatternKind = new Bindable<CamPatternKind>(CamPatternKind.Spin);

    public readonly BindableInt PatternDirection = new BindableInt(1);

    public float PxPerMs { get; set; } = PlayfieldLayout.PX_PER_MS;

    public Action<string>? Message { get; set; }

    /// <summary>Raised after the selection changed (the inspector rebuilds).</summary>
    public event Action? SelectionChanged;

    /// <summary>Raised on a right click on an anchor or a curve; the lanes show the items as a popup.</summary>
    public event Action<CamPointer, IReadOnlyList<LaserMenuItem>>? ContextMenuRequested;

    // ---- selection ---------------------------------------------------------

    /// <summary>Lane of the selected anchor (graph or manual tilt).</summary>
    public CamPathLane? SelectedLane { get; private set; }

    /// <summary>Absolute pulse of the selected anchor (anchors never share a pulse within a lane).</summary>
    public long? SelectedAnchorY { get; private set; }

    /// <summary>A selected auto tilt point (a mode switch, not an anchor).</summary>
    public TiltPoint? SelectedAutoTilt { get; private set; }

    public (CamPatternKind Kind, CamPattern Pattern)? SelectedPattern { get; private set; }

    public CamGraph? SelectedGraph => SelectedLane is { } l && CameraPaths.IsGraph(l) ? (CamGraph)(int)l : null;

    /// <summary>The chart point on the selected anchor's pulse (null for an anchor that is only a curve control point).</summary>
    public GraphPoint? SelectedPoint => SelectedGraph is { } g && SelectedAnchorY is { } y ? CameraGraphs.Get(chart.Camera, g).FirstOrDefault(p => p.Y == y) : null;

    /// <summary>The selected auto tilt point, or the tilt point on the selected tilt anchor's pulse.</summary>
    public TiltPoint? SelectedTilt => SelectedAutoTilt
                                      ?? (SelectedLane == CamPathLane.Tilt && SelectedAnchorY is { } y ? chart.Camera.Tilt.FirstOrDefault(t => t.Y == y) : null);

    /// <summary>The selected anchor: its run, the run's anchors (the placement chain's when it is that run) and the index.</summary>
    public (CamRun Run, LaserPath Path, int Index)? SelectedAnchor
    {
        get
        {
            if (SelectedLane is not { } lane || SelectedAnchorY is not { } y) return null;
            foreach (var run in CameraPaths.Runs(chart.Camera, lane))
            {
                if (y < run.Y || y > run.EndY) continue;
                var path = AnchorsOf(run);
                int i = CameraPathEditing.IndexAt(path, y - run.Y);
                if (i >= 0) return (run, path, i);
            }

            return null;
        }
    }

    public CamPointer? Hover { get; private set; }

    /// <summary>The anchor under the pointer (drawn highlighted).</summary>
    public (CamPathLane Lane, long Y)? HoveredAnchor { get; private set; }

    /// <summary>The placement in progress (null when the last action was not an add).</summary>
    public CameraChain? Chain { get; private set; }

    /// <summary>The lane a drag in progress belongs to (the pointer may leave it).</summary>
    public CamLane? DragLane { get; private set; }

    private enum DragMode
    {
        None,
        AnchorMove,
        AnchorJump,
        AnchorJumpEnd,
        Ease,
        TiltAutoMove,
        Pattern,
        PatternResize,
    }

    private DragMode mode;
    private readonly List<IEditCommand> baseCommands = new List<IEditCommand>();
    private string dragName = "";
    private long dragOriginY;
    private double grabOffsetMs;
    private long lastValidY;

    // anchor drags: the path the drag edits (anchors as when the drag started), the run's start in the state the base
    // commands leave, the anchor index, whether the drag continues the chain, and whether the pointer moved at all
    private LaserPath? dragPath;
    private long dragRunY;
    private int dragIndex;
    private bool dragChain;
    private bool dragMoved;
    private bool dragAdded;
    private int chainStartIndex = -1, chainTailIndex = -1;

    public CameraLaneEditor(EditorSession session) => this.session = session;

    private Chart chart => session.Chart;
    private CommandStack commands => session.Commands;

    public static bool IsGraph(CamLane lane) => lane <= CamLane.RotationDeg;
    public static bool IsPath(CamLane lane) => lane <= CamLane.Tilt;
    public static CamGraph GraphOf(CamLane lane) => (CamGraph)(int)lane;
    public static CamPathLane PathLaneOf(CamLane lane) => (CamPathLane)(int)lane;

    public static string LaneName(CamLane lane) => lane switch
    {
        CamLane.Tilt => "tilt",
        CamLane.Patterns => "spin / swing",
        _ => CameraGraphs.Name(GraphOf(lane)),
    };

    public static (double Min, double Max) Range(CamLane lane) => lane switch
    {
        CamLane.Tilt => (-TILT_VIEW, TILT_VIEW),
        CamLane.Patterns => (-1, 1),
        _ => CameraGraphs.ViewRange(GraphOf(lane)),
    };

    public static double ValueOf(CamLane lane, double fx)
    {
        var (min, max) = Range(lane);
        double v = min + Math.Clamp(fx, 0, 1) * (max - min);
        return lane == CamLane.Tilt ? Math.Round(v * 20) / 20 : Math.Round(v);
    }

    public static double FxOf(CamLane lane, double v)
    {
        var (min, max) = Range(lane);
        return (v - min) / (max - min);
    }

    // ---- time helpers ------------------------------------------------------

    public long PulseAt(double ms, bool snap = true)
    {
        long p = (long)Math.Round(session.MsToPulse(ms));
        if (snap && GridSnap.Value) p = session.Timing.Snap(p, Divisor.Value);
        return Math.Max(0, p);
    }

    private double ms(long pulse) => session.PulseToMs(pulse);
    private double tolMs => HIT_PX / Math.Max(0.001f, PxPerMs);
    private const double tol_fx = HIT_PX / LANE_WIDTH;

    // ---- anchors -------------------------------------------------------------

    /// <summary>The anchors to show and edit for a run: the placement chain's (with its auto flags) when it is that run, else the run's.</summary>
    public LaserPath AnchorsOf(CamRun run) => Chain is { } c && c.Matches(run) ? c.Path.Clone() : run.Anchors();

    /// <summary>The anchor under the pointer in a graph / tilt lane (the selected lane's runs are not preferred: lanes do not overlap).</summary>
    public CamAnchorHit? AnchorAt(CamPointer p)
    {
        if (!IsPath(p.Lane)) return null;
        var lane = PathLaneOf(p.Lane);
        CamAnchorHit? best = null;
        double bestDist = double.MaxValue;
        foreach (var run in CameraPaths.Runs(chart.Camera, lane))
        {
            if (ms(run.Y) - tolMs > p.Ms || ms(run.EndY) + tolMs < p.Ms) continue;
            var path = AnchorsOf(run);
            for (int i = 0; i < path.Count; i++)
            {
                var a = path.Anchors[i];
                double dt = Math.Abs(ms(run.Y + a.Ry) - p.Ms);
                if (dt > tolMs) continue;
                for (int k = 0; k < (a.IsSlam ? 2 : 1); k++)
                {
                    double dx = Math.Abs(FxOf(p.Lane, k == 0 ? a.V : a.Vf!.Value) - p.Fx);
                    // a tilt run's head switches from an auto mode: hit it anywhere across the lane, like an auto point
                    bool head = i == 0 && run.HeadAuto != null;
                    if (dx > tol_fx && !head) continue;
                    double d = dt / tolMs + dx / tol_fx;
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = new CamAnchorHit(run, i, k == 1);
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// The segment under the pointer: the run and the index of the anchor the segment starts at. <paramref name="near"/>
    /// = the pointer must be near the drawn curve (else anywhere between the two anchors' times).
    /// </summary>
    public (CamRun Run, int Start)? SegmentAt(CamPointer p, bool near)
    {
        if (!IsPath(p.Lane)) return null;
        foreach (var run in CameraPaths.Runs(chart.Camera, PathLaneOf(p.Lane)))
        {
            if (run.Count < 2 || p.Ms <= ms(run.Y) || p.Ms >= ms(run.EndY)) continue;
            double ry = session.MsToPulse(p.Ms) - run.Y;
            if (near && LaserPathBaker.PointsValueAt(run.Points, ry) is double v && Math.Abs(FxOf(p.Lane, v) - p.Fx) > tol_fx * 1.5) continue;
            var path = AnchorsOf(run);
            int i = Math.Clamp(path.Anchors.FindLastIndex(a => a.Ry <= ry), 0, path.Count - 2);
            return path.SegmentOf(i) is { } s ? (run, s.Start) : null;
        }

        return null;
    }

    /// <summary>An auto tilt point near the pointer's time (anywhere across the lane); run heads are anchors.</summary>
    public TiltPoint? AutoTiltAt(CamPointer p)
    {
        if (p.Lane != CamLane.Tilt) return null;
        foreach (var t in chart.Camera.Tilt)
        {
            if (!t.Value.IsAuto || !(t.Final ?? t.Value).IsAuto) continue;
            if (Math.Abs(ms(t.Y) - p.Ms) <= tolMs) return t;
        }

        return null;
    }

    /// <summary>Left edge of a pattern kind's bar inside the spin lane, in px (spin, half spin, swing side by side).</summary>
    public static float PatternColumnLeft(CamPatternKind kind) => 8 + (int)kind * 30;

    /// <summary>Width of a pattern bar in px.</summary>
    public const float PATTERN_BAR_WIDTH = 22;

    private static bool inPatternColumn(CamPatternKind kind, double fx)
    {
        double x = fx * LANE_WIDTH, left = PatternColumnLeft(kind);
        return x >= left - HIT_PX / 2 && x <= left + PATTERN_BAR_WIDTH + HIT_PX / 2;
    }

    /// <summary>
    /// The spin / half spin / swing under the pointer: its start line (anywhere across the lane) or its bar body (in its
    /// column). Like a note: a click selects it, a drag moves it (it snaps to a slam), right click deletes it.
    /// </summary>
    public (CamPatternKind Kind, CamPattern Pattern)? PatternAt(CamPointer p)
    {
        if (p.Lane != CamLane.Patterns) return null;
        (CamPatternKind, CamPattern)? body = null;
        foreach (var (kind, pat) in CameraEvaluator.Patterns(chart.Camera))
        {
            if (Math.Abs(ms(pat.Y) - p.Ms) <= tolMs) return (kind, pat);
            if (body == null && inPatternColumn(kind, p.Fx) && p.Ms > ms(pat.Y) && p.Ms < ms(pat.Y + pat.Length)) body = (kind, pat);
        }

        return body;
    }

    /// <summary>
    /// The pattern whose bar END is under the pointer (its column, within the hit radius of the end): a drag there
    /// changes its length, like a hold's tail. Wins over the start line only when the pointer is nearer the end.
    /// </summary>
    public (CamPatternKind Kind, CamPattern Pattern)? PatternEndAt(CamPointer p)
    {
        if (p.Lane != CamLane.Patterns) return null;
        foreach (var (kind, pat) in CameraEvaluator.Patterns(chart.Camera))
        {
            double end = ms(pat.Y + pat.Length);
            if (!inPatternColumn(kind, p.Fx) || Math.Abs(end - p.Ms) > tolMs) continue;
            if (Math.Abs(end - p.Ms) <= Math.Abs(ms(pat.Y) - p.Ms)) return (kind, pat);
        }

        return null;
    }

    /// <summary>The pattern whose end shows the resize hint: hovered end (no drag), or the one being resized.</summary>
    public (CamPatternKind Kind, CamPattern Pattern)? PatternTailHint =>
        mode == DragMode.PatternResize ? SelectedPattern : mode == DragMode.None && Hover is { } h ? PatternEndAt(h) : null;

    /// <summary>Shortest pattern a resize leaves: a 1/16 note.</summary>
    public const long MIN_PATTERN_LENGTH = Timebase.PulsesPerWholeNote / 16;

    // ---- pointer -----------------------------------------------------------

    public void PointerMove(CamPointer p)
    {
        Hover = p;
        HoveredAnchor = AnchorAt(p) is { } h ? (h.Run.Lane, h.Run.Y + AnchorsOf(h.Run).Anchors[h.Index].Ry) : null;
    }

    public void PointerLeave()
    {
        Hover = null;
        HoveredAnchor = null;
    }

    /// <returns>true when a drag started (the caller then sends drags and the release).</returns>
    public bool PointerDown(CamPointer p, PointerButton button, bool shift, bool alt, bool ctrl = false)
    {
        Hover = p;
        if (button == PointerButton.Right)
        {
            rightClick(p);
            return false;
        }

        if (IsPath(p.Lane)) pathDown(p, shift, alt, ctrl);
        else patternDown(p);

        return mode != DragMode.None;
    }

    public void PointerDrag(CamPointer p)
    {
        Hover = p;
        if (mode == DragMode.None) return;

        IEditCommand? step = mode switch
        {
            DragMode.AnchorMove or DragMode.AnchorJump or DragMode.AnchorJumpEnd or DragMode.Ease => anchorStep(p),
            DragMode.TiltAutoMove => tiltAutoStep(p),
            DragMode.PatternResize => patternResizeStep(p),
            _ => patternStep(p),
        };

        if (step == null) return;
        commands.Rollback();
        commands.BeginTransaction(dragName);
        try
        {
            foreach (var c in baseCommands) commands.Execute(c);
            commands.Execute(step);
        }
        catch (InvalidOperationException)
        {
            // a collision: keep the last valid state
            commands.Rollback();
            commands.BeginTransaction(dragName);
            foreach (var c in baseCommands) commands.Execute(c);
            if (lastStep != null) commands.Execute(lastStep);
            return;
        }

        lastStep = step;
    }

    private IEditCommand? lastStep;

    public void PointerUp()
    {
        if (mode != DragMode.None) commands.Commit();
        var finished = mode;
        mode = DragMode.None;

        if (finished is DragMode.AnchorMove or DragMode.AnchorJump or DragMode.AnchorJumpEnd)
        {
            if (!dragMoved && !dragAdded && dragChain && finished == DragMode.AnchorMove)
                cycleChainAnchor();
            else if (dragChain && lastStep is SetCameraLaneCommand { Result: { } result } done)
                Chain = chainAfter(done.Lane, done.ResultY, result);
            else if (!dragChain)
                Chain = null;

            if (lastStep is SetCameraLaneCommand { Result: { } r2 } moved && dragIndex < r2.Count)
                SelectedAnchorY = moved.ResultY + r2.Anchors[dragIndex].Ry;
        }

        DragLane = null;
        baseCommands.Clear();
        lastStep = null;
        dragPath = null;
        SelectionChanged?.Invoke();
    }

    private void begin(DragMode m, string name, long originY, double pointerMs, CamLane lane)
    {
        mode = m;
        dragName = name;
        dragOriginY = originY;
        lastValidY = originY;
        grabOffsetMs = pointerMs - ms(originY);
        DragLane = lane;
        baseCommands.Clear();
        lastStep = null;
        dragMoved = false;
        dragAdded = false;
        commands.BeginTransaction(name);
    }

    private void addBase(IEditCommand c)
    {
        baseCommands.Add(c);
        commands.Execute(c);
    }

    /// <summary>Snapped pulse for a drag step (keeps the grab offset so a click without movement does not jump).</summary>
    private long dragPulse(CamPointer p) => PulseAt(p.Ms - grabOffsetMs);

    // ---- graph / manual tilt anchors ------------------------------------------

    private void pathDown(CamPointer p, bool shift, bool alt, bool ctrl)
    {
        var lane = PathLaneOf(p.Lane);
        string name = CameraPaths.Name(lane);

        // Alt-drag between two anchors: the kson ease of a plain straight pair (the old quick curve handle)
        if (alt && SegmentAt(p, near: false) is { } seg)
        {
            Chain = null;
            var path = AnchorsOf(seg.Run);
            if (CameraPathEditing.SetEase(path, seg.Start, null) == null)
            {
                Message?.Invoke("Alt-drag shapes a straight two-anchor segment; right-click a curve to change its type");
                return;
            }

            selectAnchor(lane, seg.Run.Y + path.Anchors[seg.Start].Ry);
            startAnchorDrag(DragMode.Ease, $"Shape {name} curve", seg.Run, path, seg.Start, p, false);
            return;
        }

        // Ctrl+click on a curve: an inherit anchor inside its segment, dragged right away
        if (ctrl && SegmentAt(p, near: true) is { } ins)
        {
            Chain = null;
            long y = PulseAt(p.Ms);
            var basis = AnchorsOf(ins.Run);
            double v = LaserPathBaker.PointsValueAt(ins.Run.Points, y - ins.Run.Y) is double cv ? Math.Round(cv, 6) : ValueOf(p.Lane, p.Fx);
            if (LaserPathEditing.InsertAnchor(basis, y - ins.Run.Y, v) is not { } inserted
                || CameraPaths.Replace(chart.Camera, ins.Run, inserted.Path, $"Insert {name} anchor") is not { Result: { } } cmd)
            {
                Message?.Invoke("No room for an anchor there");
                return;
            }

            begin(DragMode.AnchorMove, $"Insert {name} anchor", y, ms(y), p.Lane);
            addBase(cmd);
            dragAdded = true;
            dragPath = cmd.Result.Clone();
            dragRunY = cmd.ResultY;
            dragIndex = inserted.Index;
            dragChain = false;
            selectAnchor(lane, y);
            return;
        }

        if (AnchorAt(p) is { } hit)
        {
            var path = AnchorsOf(hit.Run);
            var a = path.Anchors[hit.Index];
            long ay = hit.Run.Y + a.Ry;
            bool inChain = Chain is { } c && c.Lane == lane && c.Matches(hit.Run);
            if (!inChain) Chain = null;
            selectAnchor(lane, ay);
            var m = hit.Vf ? DragMode.AnchorJumpEnd : shift ? DragMode.AnchorJump : DragMode.AnchorMove;
            startAnchorDrag(m, shift && !hit.Vf ? $"Jump {name}" : $"Move {name} anchor", hit.Run, path, hit.Index, p, inChain);
            return;
        }

        if (p.Lane == CamLane.Tilt && AutoTiltAt(p) is { } auto)
        {
            Chain = null;
            selectAutoTilt(auto);
            begin(DragMode.TiltAutoMove, "Move tilt point", auto.Y, p.Ms, p.Lane);
            return;
        }

        // empty lane space: a new anchor (Shift: an instant jump from the current value)
        long ny = PulseAt(p.Ms);
        double nv = ValueOf(p.Lane, p.Fx);
        double? vf = null;
        if (shift)
        {
            vf = nv;
            nv = lane == CamPathLane.Tilt
                ? Math.Round(new CameraEvaluator(chart).EvaluateTilt(ny).Input * 20) / 20
                : Math.Round(Graph.ValueAt(CameraPaths.Graph(chart.Camera, lane), ny, 0), 6);
        }

        if (CameraPathCommands.Add(chart, lane, ny, nv, vf, Chain) is not { } added)
        {
            Chain = null;
            Message?.Invoke($"{name} already has a point there");
            return;
        }

        begin(shift ? DragMode.AnchorJump : DragMode.AnchorMove, $"Add {name} anchor", ny, ms(ny), p.Lane);
        addBase(added.Command);
        dragAdded = true;
        Chain = added.Chain;
        dragPath = added.Chain.Path.Clone();
        dragRunY = added.Chain.RunY;
        dragIndex = CameraPathEditing.IndexAt(dragPath, ny - dragRunY);
        dragChain = true;
        rememberChainIndices();
        selectAnchor(lane, ny);
    }

    private void startAnchorDrag(DragMode m, string name, CamRun run, LaserPath path, int index, CamPointer p, bool chain)
    {
        begin(m, name, run.Y + path.Anchors[index].Ry, p.Ms, p.Lane);
        dragPath = path;
        dragRunY = run.Y;
        dragIndex = index;
        dragChain = chain;
        if (chain) rememberChainIndices();
    }

    private void rememberChainIndices()
    {
        chainStartIndex = chainTailIndex = -1;
        if (Chain is not { } c || dragPath == null) return;
        chainStartIndex = CameraPathEditing.IndexAt(dragPath, c.StartY - dragRunY);
        chainTailIndex = CameraPathEditing.IndexAt(dragPath, c.TailY - dragRunY);
    }

    /// <summary>The chain after a drag of one of its anchors (indices are stable: anchors never pass each other).</summary>
    private CameraChain? chainAfter(CamPathLane lane, long runY, LaserPath result)
    {
        if (chainStartIndex < 0 || chainTailIndex < 0 || chainStartIndex >= result.Count || chainTailIndex >= result.Count) return null;
        return new CameraChain(lane, runY, result, runY + result.Anchors[chainStartIndex].Ry, runY + result.Anchors[chainTailIndex].Ry);
    }

    /// <summary>The run in the state the drag's base commands leave (the run starting at <see cref="dragRunY"/>).</summary>
    private CamRun? dragRun()
    {
        if (DragLane is not { } l || !IsPath(l)) return null;
        return CameraPaths.Runs(chart.Camera, PathLaneOf(l)).FirstOrDefault(r => r.Y == dragRunY);
    }

    private IEditCommand? anchorStep(CamPointer p)
    {
        if (dragPath == null || dragRun() is not { } run) return null;
        var a = dragPath.Anchors[dragIndex];
        double v = ValueOf(p.Lane, p.Fx);
        LaserPath? edited;
        switch (mode)
        {
            case DragMode.AnchorJump:
                edited = CameraPathEditing.SetJump(dragPath, dragIndex, v);
                break;

            case DragMode.AnchorJumpEnd:
                edited = LaserPathEditing.MoveAnchor(dragPath, dragIndex, a.Ry, v, slamEnd: true);
                break;

            case DragMode.Ease:
                var b = dragPath.Anchors[dragIndex + 1];
                edited = CameraPathEditing.SetEase(dragPath, dragIndex, curveFor(dragRunY + a.Ry, a.ValueAfter, dragRunY + b.Ry, b.V, p, p.Lane));
                break;

            default:
                long y = dragPulse(p);
                if (y == dragRunY + a.Ry && v == a.V && !dragMoved) return null;
                edited = LaserPathEditing.MoveAnchor(dragPath, dragIndex, y - dragRunY, v);
                break;
        }

        dragMoved = true;
        return CameraPaths.Replace(chart.Camera, run, edited, dragName);
    }

    /// <summary>A click without movement on an anchor of the placement chain: cycle its type (osu!lazer placement).</summary>
    private void cycleChainAnchor()
    {
        if (dragPath == null || dragRun() is not { } run) return;
        var cycled = LaserPathEditing.CycleType(dragPath, dragIndex);
        if (CameraPaths.Replace(chart.Camera, run, cycled, "Change anchor type") is not { Result: { } result } cmd) return;
        commands.Execute(cmd);
        Chain = chainAfter(cmd.Lane, cmd.ResultY, result);
    }

    /// <summary>
    /// Curve control point (a, b) = (time fraction, value fraction) of the pointer inside the segment,
    /// so the curve bends towards the cursor; linear when the cursor is on the straight line.
    /// </summary>
    private Curve? curveFor(long y0, double v0, long y1, double v1, CamPointer p, CamLane lane)
    {
        double t0 = ms(y0), t1 = ms(y1);
        double tx = Math.Clamp((p.Ms - t0) / Math.Max(1e-9, t1 - t0), 0, 1);
        var (min, max) = Range(lane);
        double pv = min + Math.Clamp(p.Fx, 0, 1) * (max - min);
        double fv = Math.Abs(v1 - v0) < 1e-9 ? tx : Math.Clamp((pv - v0) / (v1 - v0), 0, 1);
        tx = Math.Round(tx, 3);
        fv = Math.Round(fv, 3);
        return Math.Abs(tx - fv) < 0.02 ? null : new Curve(tx, fv);
    }

    // ---- context menu ----------------------------------------------------------

    private static readonly (LaserCurveType Type, string Label)[] curve_types =
    {
        (LaserCurveType.Linear, "Straight"),
        (LaserCurveType.PerfectCurve, "Perfect curve"),
        (LaserCurveType.Bezier, "Bezier"),
    };

    /// <summary>
    /// The items a right click at <paramref name="p"/> offers: on an anchor its type (Inherit / Straight / Perfect curve /
    /// Bezier) and Delete; on a curve the type of that segment. Null = nothing there.
    /// </summary>
    public IReadOnlyList<LaserMenuItem>? MenuAt(CamPointer p)
    {
        if (AnchorAt(p) is { } h)
        {
            var path = AnchorsOf(h.Run);
            var anchor = path.Anchors[h.Index];
            bool last = h.Index == path.Count - 1;
            bool mustStart = h.Index == 0 || anchor.IsSlam;
            var items = new List<LaserMenuItem>
            {
                LaserMenuItem.Title($"Anchor {h.Index + 1} of {path.Count}"),
                new LaserMenuItem("Inherit", () => run(h.Run, LaserPathEditing.SetType(path, h.Index, null), "Change anchor type"), anchor.Type == null, !mustStart && !last),
            };
            foreach (var (type, label) in curve_types)
                items.Add(new LaserMenuItem(label, () => run(h.Run, LaserPathEditing.SetType(path, h.Index, type), "Change anchor type"), anchor.Type == type, !last));
            items.Add(new LaserMenuItem("Delete anchor", () =>
            {
                run(h.Run, CameraPathEditing.Delete(path, h.Index), $"Delete {CameraPaths.Name(h.Run.Lane)} anchor");
                clearSelection();
            }));
            return items;
        }

        if (SegmentAt(p, near: true) is { } seg)
        {
            var path = AnchorsOf(seg.Run);
            var items = new List<LaserMenuItem> { LaserMenuItem.Title("Curve type") };
            foreach (var (type, label) in curve_types)
                items.Add(new LaserMenuItem(label, () => run(seg.Run, LaserPathEditing.SetSegmentType(path, seg.Start, type), "Change curve type"),
                    path.Anchors[seg.Start].Type == type));
            return items;
        }

        return null;
    }

    private void run(CamRun r, LaserPath? edited, string description)
    {
        if (CameraPaths.Replace(chart.Camera, r, edited, description) is not { } cmd)
        {
            Message?.Invoke("Not possible here");
            return;
        }

        commands.Execute(cmd);
        bool chainRun = Chain is { } c && c.Lane == r.Lane && c.RunY == r.Y;
        Chain = chainRun && cmd.Result != null ? new CameraChain(r.Lane, cmd.ResultY, cmd.Result, Chain!.StartY, Chain.TailY) : null;
        if (Chain is { } nc && (CameraPathEditing.IndexAt(nc.Path, nc.StartY - nc.RunY) < 0 || CameraPathEditing.IndexAt(nc.Path, nc.TailY - nc.RunY) < 0)) Chain = null;
        SelectionChanged?.Invoke();
    }

    private void rightClick(CamPointer p)
    {
        // while placing (osu!, like the laser tool): right click after the chain's tail in its lane, off any anchor, puts a
        // last anchor at the cursor and ends the chain (one undo step) - before the menu, whose segment hit reaches past the tail
        if (Chain is { } chain && IsPath(p.Lane) && PathLaneOf(p.Lane) == chain.Lane && PulseAt(p.Ms) > chain.TailY && AnchorAt(p) == null
            && !(p.Lane == CamLane.Tilt && AutoTiltAt(p) != null)
            && CameraPathCommands.Add(chart, chain.Lane, PulseAt(p.Ms), ValueOf(p.Lane, p.Fx), null, chain) is { } last)
        {
            commands.Execute(last.Command);
            Chain = null;
            return;
        }

        if (IsPath(p.Lane) && MenuAt(p) is { } items)
        {
            if (AnchorAt(p) is { } h) selectAnchor(h.Run.Lane, h.Run.Y + AnchorsOf(h.Run).Anchors[h.Index].Ry);
            ContextMenuRequested?.Invoke(p, items);
            return;
        }

        // right click on empty space otherwise ends a placement; on an auto tilt point or a pattern it deletes it
        Chain = null;
        DeleteAt(p);
    }

    // ---- auto tilt -------------------------------------------------------------

    private IEditCommand? tiltAutoStep(CamPointer p)
    {
        if (SelectedAutoTilt is not { } t) return null;
        long y = dragPulse(p);
        if (chart.Camera.Tilt.Any(x => x != t && x.Y == y)) y = lastValidY;
        lastValidY = y;
        return new EditTiltPointCommand(t, y, t.Value, t.Final, t.Curve);
    }

    /// <summary>Adds (or turns the point at that pulse into) an auto tilt mode segment.</summary>
    public void SetTiltMode(TiltMode mode, long pulse)
    {
        var existing = chart.Camera.Tilt.FirstOrDefault(t => t.Y == pulse);
        if (existing != null)
            commands.Execute(new EditTiltPointCommand(existing, pulse, TiltValue.Of(mode), null, null));
        else
        {
            existing = new TiltPoint(pulse, TiltValue.Of(mode));
            commands.Execute(new AddTiltPointCommand(existing));
        }

        Chain = null;
        selectAutoTilt(existing);
    }

    // ---- patterns ----------------------------------------------------------

    /// <summary>How far a pattern snaps to a slam: a 1/8 note.</summary>
    private const long slam_snap = Timebase.PulsesPerWholeNote / 8;

    private void patternDown(CamPointer p)
    {
        Chain = null;
        if (PatternEndAt(p) is { } end)
        {
            selectPattern(end);
            begin(DragMode.PatternResize, $"{CameraCommands.PatternName(end.Kind)} length", end.Pattern.Y + end.Pattern.Length, p.Ms, CamLane.Patterns);
            return;
        }

        if (PatternAt(p) is { } hit)
        {
            selectPattern(hit);
            begin(DragMode.Pattern, $"Move {CameraCommands.PatternName(hit.Kind)}", hit.Pattern.Y, p.Ms, CamLane.Patterns);
            return;
        }

        long y = PulseAt(p.Ms);
        int dir = PatternDirection.Value;
        if (CameraCommands.SlamNear(chart, (long)Math.Round(session.MsToPulse(p.Ms)), slam_snap) is { } slam)
        {
            y = slam.Y;
            dir = slam.Direction;
        }
        else
            Message?.Invoke("no slam here: the game ignores a spin/swing without a matching slam");

        var kind = PatternKind.Value;
        var pattern = new CamPattern(y, dir, Timebase.PulsesPerWholeNote / 2);
        if (kind == CamPatternKind.Swing)
        {
            pattern.SwingScale = 250;
            pattern.SwingRepeat = 3;
            pattern.SwingDecayOrder = 2;
        }

        begin(DragMode.Pattern, $"Add {CameraCommands.PatternName(kind)}", y, ms(y), CamLane.Patterns);
        addBase(new AddPatternCommand(kind, pattern));
        selectPattern((kind, pattern));
    }

    private IEditCommand? patternStep(CamPointer p)
    {
        if (SelectedPattern is not { } sp) return null;
        long y = dragPulse(p);
        var values = sp.Pattern.Clone();
        if (CameraCommands.SlamNear(chart, (long)Math.Round(session.MsToPulse(p.Ms - grabOffsetMs)), slam_snap) is { } slam)
        {
            y = slam.Y;
            values.Direction = slam.Direction;
        }

        values.Y = y;
        return new EditPatternCommand(sp.Kind, sp.Pattern, values);
    }

    private IEditCommand? patternResizeStep(CamPointer p)
    {
        if (SelectedPattern is not { } sp) return null;
        long end = dragPulse(p);
        long length = Math.Max(MIN_PATTERN_LENGTH, end - sp.Pattern.Y);
        if (length == sp.Pattern.Length && lastStep == null) return null;
        var values = sp.Pattern.Clone();
        values.Length = length;
        return new EditPatternCommand(sp.Kind, sp.Pattern, values);
    }

    // ---- delete / inspector edits -------------------------------------------

    /// <summary>Right-click delete of an auto tilt point or a pattern (anchors are deleted from their menu).</summary>
    public bool DeleteAt(CamPointer p)
    {
        if (p.Lane == CamLane.Tilt && AutoTiltAt(p) is { } t)
        {
            commands.Execute(new RemoveTiltPointCommand(t));
            clearSelection();
            return true;
        }

        if (PatternAt(p) is { } pat)
        {
            commands.Execute(new RemovePatternCommand(pat.Kind, pat.Pattern));
            clearSelection();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Changes the selected anchor (inspector): value, jump target, or the kson ease of the straight pair it starts
    /// (curve fields). An auto tilt point keeps its mode; only its curve/jump fields apply to manual values.
    /// </summary>
    public void EditSelected(double? v = null, double? vf = null, bool clearVf = false, Curve? curve = null, bool clearCurve = false)
    {
        if (SelectedAnchor is { } sel)
        {
            var path = sel.Path;
            var a = path.Anchors[sel.Index];
            LaserPath? edited = path;
            if (v is double nv) edited = LaserPathEditing.MoveAnchor(edited, sel.Index, a.Ry, nv);
            if (edited != null && (vf != null || clearVf)) edited = CameraPathEditing.SetJump(edited, sel.Index, clearVf ? null : vf);
            if (edited != null && (curve != null || clearCurve))
            {
                var eased = CameraPathEditing.SetEase(edited, sel.Index, clearCurve ? null : curve);
                if (eased == null) Message?.Invoke("curve a/b shapes a straight two-anchor segment; right-click a curve for its type");
                else edited = eased;
            }

            if (edited != path) run(sel.Run, edited, $"Edit {CameraPaths.Name(sel.Run.Lane)} anchor");
        }
        else if (SelectedAutoTilt is { } t)
        {
            TiltValue? final = clearVf ? null : vf is { } nf ? TiltValue.Of(nf) : t.Final;
            commands.Execute(new EditTiltPointCommand(t, t.Y, t.Value, final, clearCurve ? null : curve ?? t.Curve));
        }

        SelectionChanged?.Invoke();
    }

    public void EditSelectedPattern(Action<CamPattern> change)
    {
        if (SelectedPattern is not { } sp) return;
        var values = sp.Pattern.Clone();
        change(values);
        commands.Execute(new EditPatternCommand(sp.Kind, sp.Pattern, values));
        SelectionChanged?.Invoke();
    }

    public void DeleteSelected()
    {
        if (SelectedAnchor is { } sel) run(sel.Run, CameraPathEditing.Delete(sel.Path, sel.Index), $"Delete {CameraPaths.Name(sel.Run.Lane)} anchor");
        else if (SelectedAutoTilt is { } t) commands.Execute(new RemoveTiltPointCommand(t));
        else if (SelectedPattern is { } sp) commands.Execute(new RemovePatternCommand(sp.Kind, sp.Pattern));
        clearSelection();
    }

    /// <summary>Drops selected objects that an undo/redo removed from the chart.</summary>
    public void Resync()
    {
        // a drag rolls back and re-executes its own commands every step
        if (mode != DragMode.None) return;

        bool changed = false;
        if (SelectedLane != null && SelectedAnchor == null)
        {
            SelectedLane = null;
            SelectedAnchorY = null;
            changed = true;
        }

        if (SelectedAutoTilt is { } t && !chart.Camera.Tilt.Contains(t))
        {
            SelectedAutoTilt = null;
            changed = true;
        }

        if (SelectedPattern is { } sp && !CameraCommands.Patterns(chart.Camera, sp.Kind).Contains(sp.Pattern))
        {
            SelectedPattern = null;
            changed = true;
        }

        if (changed) SelectionChanged?.Invoke();
    }

    /// <summary>Ends a placement in progress (Esc, tool changes).</summary>
    public void EndChain() => Chain = null;

    private void selectAnchor(CamPathLane lane, long y)
    {
        SelectedLane = lane;
        SelectedAnchorY = y;
        SelectedAutoTilt = null;
        SelectedPattern = null;
        SelectionChanged?.Invoke();
    }

    private void selectAutoTilt(TiltPoint t)
    {
        SelectedLane = null;
        SelectedAnchorY = null;
        SelectedAutoTilt = t;
        SelectedPattern = null;
        SelectionChanged?.Invoke();
    }

    private void selectPattern((CamPatternKind, CamPattern) p)
    {
        SelectedLane = null;
        SelectedAnchorY = null;
        SelectedAutoTilt = null;
        SelectedPattern = p;
        SelectionChanged?.Invoke();
    }

    private void clearSelection()
    {
        SelectedLane = null;
        SelectedAnchorY = null;
        SelectedAutoTilt = null;
        SelectedPattern = null;
        SelectionChanged?.Invoke();
    }
}
