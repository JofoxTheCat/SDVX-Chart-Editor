using osu.Framework.Bindables;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Screens.Edit.Components;

namespace SDVXEditor.Game.Screens.Edit.Compose;

/// <summary>A pointer position on the playfield: audio time under the cursor and the local x.</summary>
public readonly record struct PlayfieldPointer(double Ms, float X);

public enum PointerButton
{
    Left,
    Right,
}

/// <summary>
/// The compose tools, independent of the drawable: the playfield turns mouse events into
/// <see cref="PlayfieldPointer"/>s and calls <see cref="PointerDown"/> / <see cref="PointerDrag"/> /
/// <see cref="PointerUp"/>; the smoke test drives the same methods. Every change goes through
/// Core commands on the session's <see cref="CommandStack"/>; a drag is one transaction (one undo step).
/// <list type="bullet">
/// <item>BT/FX chip: click places at the snapped pulse; a click on a hold of that kind makes it a chip at its start. BT/FX hold:
/// drag from head to tail (osu! hold); pressed on an existing chip / hold of that kind the drag sets its length from its
/// start (osu!mania), back to the start = a chip.</item>
/// <item>Right click (any tool): delete the object under the cursor.</item>
/// <item>Select: click / Ctrl+click / drag box; drag a selected object to move (snapped, lane + time).</item>
/// <item>Laser: drag on empty time draws a straight section; a CLICK on empty time starts osu!lazer slider placement
/// (each click adds an anchor in time order, the type follows the count: 2 straight, 3 perfect curve, 4+ Bezier; a
/// click on a placed anchor cycles its type; a click on the last anchor's pulse with another value makes a slam —
/// also as the very first two clicks, finished as the slam plus a one-tick tail; right click puts a last anchor at the
/// cursor (when it lies after the last one) and finishes, Esc cancels).
/// Click on a section inserts a point / anchor, drag a point or anchor to move it; Ctrl = free value (default snap
/// 1/50), Shift = wide for a new section, a plain point dragged onto its neighbour's pulse becomes a slam (curved
/// paths keep anchors apart).</item>
/// <item>Classic lasers (the classic-laser key held, <see cref="ClassicLasers"/>): K-Shoot / kson-editor placement — every
/// click a plain straight point (Core <see cref="ClassicLaser"/>), values on a 1/10 grid (1/20 wide), same beat + other
/// value = slam, a click on the last point again finishes; the chain may switch modes between clicks.</item>
/// <item>Slams: the two ends of a slam are two points (<see cref="LaserSlamEndRef"/>) — hovered, selected (click,
/// Ctrl+click, box), dragged (start end = V, landing end = Vf, either moves the slam in time) and deleted (right click
/// / Delete / menu: the slam becomes a plain anchor at the other value) one by one.</item>
/// <item>Select + lasers: hovering a laser outlines it and shows its anchors; a click selects the section; the
/// anchors of the selected section drag (time + value, snapped; Ctrl = free value); Ctrl+click on its path inserts
/// an anchor; right click on an anchor / the path opens the curve-type menu (<see cref="ContextMenuRequested"/>).</item>
/// </list>
/// </summary>
public sealed class ComposeEditor
{
    private const float hit_px = 9;
    private const float laser_hit_px = 12;

    /// <summary>Grab zone around a hold's end: a press there drags the length instead of moving the note.</summary>
    public const float TAIL_GRAB_PX = 7;

    private readonly EditorSession session;
    private readonly EditorToolState tools;

    public readonly BindableInt Divisor = new BindableInt(4);
    public readonly BindableBool GridSnap = new BindableBool(true);
    public readonly BindableBool WideLaser = new BindableBool();
    public readonly BindableBool LockNotes = new BindableBool();

    /// <summary>
    /// osu!stable: hovering a laser (select and laser tool) shows its anchors and their types without selecting it
    /// (<see cref="AnchorPreview"/>). Options → Editor "Show laser anchors on hover"; the playfield binds it to editor.ini.
    /// </summary>
    public readonly BindableBool HoverAnchors = new BindableBool(true);

    /// <summary>Current pixels per millisecond of the playfield (pixel tolerances of hit tests).</summary>
    public float PxPerMs { get; set; } = PlayfieldLayout.PX_PER_MS;

    /// <summary>Short user feedback (refused placements).</summary>
    public Action<string>? Message { get; set; }

    /// <summary>Layers panel: hidden note / laser layers are not hit, hovered or box-selected (null = all visible).</summary>
    public Panels.LayerVisibility? Layers { get; set; }

    private bool shown(NoteKind kind) => Layers?.IsVisible(kind == NoteKind.Bt ? Panels.EditorLayer.Bt : Panels.EditorLayer.Fx) != false;

    private bool shownLaser(int lane) => Layers?.IsVisible(lane == 0 ? Panels.EditorLayer.LaserLeft : Panels.EditorLayer.LaserRight) != false;

    /// <summary>The toolbox "FX Effect" for new FX holds (null = none); set by the editor screen.</summary>
    public Func<string?>? NewHoldEffect { get; set; }

    /// <summary>The toolbox "Slam Sound" for the slams of new laser sections (null = default sound); set by the editor screen.</summary>
    public Func<string?>? NewSlamSound { get; set; }

    // ---- state the renderer reads -----------------------------------------

    public PlayfieldPointer? Hover { get; private set; }

    /// <summary>Laser section under the pointer while the select tool is active (outlined, anchors shown).</summary>
    public (int Lane, LaserSection Section)? HoveredLaser { get; private set; }

    /// <summary>Anchor under the pointer (of the hovered / active section, the placement draft or a curved path).</summary>
    public AnchorHit? HoveredAnchor { get; private set; }

    /// <summary>
    /// The laser whose anchors are shown because the pointer is over it (<see cref="HoverAnchors"/> on, select or laser
    /// tool, no placement or drag in progress); null otherwise. The selected laser shows its anchors regardless.
    /// </summary>
    public (int Lane, LaserSection Section)? AnchorPreview =>
        HoverAnchors.Value && mode == DragMode.None && Placement == null && Tool is EditorTool.Select or EditorTool.Laser ? HoveredLaser : null;

    private (int Lane, LaserSection Section)? activeLaser;

    /// <summary>
    /// The selected laser section whose anchors are editable in select mode: the last one clicked, as long as it is still
    /// in the chart and selected (as a whole, or one of its slam ends).
    /// </summary>
    public (int Lane, LaserSection Section)? ActiveLaser
    {
        get
        {
            if (activeLaser is not { } a) return null;
            if (!chart.Notes.Lasers[a.Lane].Contains(a.Section) || a.Section.Points.Count == 0) return null;
            if (!selection.Contains(new LaserPointRef(a.Lane, a.Section, a.Section.Points[0])) && !selection.SlamEnds.Any(e => e.Section == a.Section))
                return null;
            return a;
        }
    }

    /// <summary>osu!lazer slider placement in progress (laser tool); not in the chart until finished.</summary>
    public LaserPlacement? Placement { get; private set; }

    /// <summary>Raised on a right click on a laser anchor or path in select mode; the playfield shows the items as a popup.</summary>
    public event Action<PlayfieldPointer, IReadOnlyList<LaserMenuItem>>? ContextMenuRequested;

    /// <summary>Hold being dragged (not in the chart yet).</summary>
    public (NoteKind Kind, int Lane, long Y, long Length)? NoteDraft { get; private set; }

    /// <summary>New laser section being drawn (not in the chart yet).</summary>
    public (int Lane, LaserSection Section)? LaserDraft { get; private set; }

    /// <summary>Selection rectangle (time + x of both corners).</summary>
    public (double Ms1, float X1, double Ms2, float X2)? SelectionBox { get; private set; }

    /// <summary>Raised when draft/box/hover state changed (the playfield redraws).</summary>
    public event Action? StateChanged;

    private Chart chart => session.Chart;
    private Selection selection => session.Selection;

    public ComposeEditor(EditorSession session, EditorToolState tools)
    {
        this.session = session;
        this.tools = tools;
        // leaving the laser tool finishes a placement (a draft of one anchor is dropped)
        tools.EffectiveTool.BindValueChanged(e =>
        {
            if (e.NewValue != EditorTool.Laser) FinishPlacement();
        });
        // the classic-laser key changes the value grid and the placement preview under the pointer
        tools.ClassicLaserHeld.BindValueChanged(_ =>
        {
            updateHover();
            StateChanged?.Invoke();
        });
    }

    /// <summary>The classic-laser key is held: K-Shoot style laser placement and the coarse value grid (Core <see cref="ClassicLaser"/>).</summary>
    public bool ClassicLasers => tools.ClassicLaserHeld.Value;

    public EditorTool Tool => tools.EffectiveTool.Value;
    public int LaserSide => tools.LaserSide.Value;

    // ---- time helpers ------------------------------------------------------

    /// <summary>Pulse under a time; snapped to the beat divisor while grid snap is on.</summary>
    public long PulseAt(double ms, bool snap = true)
    {
        long p = (long)Math.Round(session.MsToPulse(ms));
        if (snap && GridSnap.Value) p = session.Timing.Snap(p, Divisor.Value);
        return Math.Max(0, p);
    }

    /// <summary>One snap tick after <paramref name="pulse"/>.</summary>
    public long TickAfter(long pulse) => Math.Max(1, session.Timing.NextSnap(pulse, Divisor.Value) - pulse);

    private double ms(long pulse) => session.PulseToMs(pulse);
    private double tolMs(float px) => px / Math.Max(0.001f, PxPerMs);

    // ---- hit tests ---------------------------------------------------------

    public NoteRef? NoteAt(PlayfieldPointer p)
    {
        if (shown(NoteKind.Bt) && PlayfieldLayout.BtLaneAt(p.X) is int bt && findNote(NoteKind.Bt, bt, p.Ms) is Note n)
            return new NoteRef(NoteKind.Bt, bt, n);
        if (shown(NoteKind.Fx) && PlayfieldLayout.FxLaneAt(p.X) is int fx && findNote(NoteKind.Fx, fx, p.Ms) is Note f)
            return new NoteRef(NoteKind.Fx, fx, f);
        return null;
    }

    private Note? findNote(NoteKind kind, int lane, double atMs)
    {
        double tol = tolMs(hit_px);
        Note? best = null;
        double bestDist = double.MaxValue;
        foreach (var n in chart.Notes.Lane(kind, lane))
        {
            double s = ms(n.Y);
            if (s - tol > atMs) break; // sorted by Y
            double e = ms(n.EndY);
            if (atMs > e + tol) continue;
            double d = atMs < s ? s - atMs : atMs > e ? atMs - e : 0;
            if (d < bestDist)
            {
                bestDist = d;
                best = n;
            }
        }

        return best;
    }

    /// <summary>
    /// Hold whose tail end is under the pointer (<see cref="TAIL_GRAB_PX"/>, nearer the end than the start), the nearest
    /// one, BT before FX on a tie; <paramref name="kind"/> limits it to one kind (the note tools).
    /// </summary>
    public NoteRef? HoldTailAt(PlayfieldPointer p, NoteKind? kind = null)
    {
        NoteRef? best = null;
        double bestPx = double.MaxValue;
        double tol = tolMs(TAIL_GRAB_PX);

        foreach (var k in new[] { NoteKind.Bt, NoteKind.Fx })
        {
            if (kind is { } only && only != k) continue;
            if (!shown(k) || laneAt(k, p.X) is not int lane) continue;

            foreach (var n in chart.Notes.Lane(k, lane))
            {
                if (ms(n.Y) > p.Ms + tol) break; // sorted by Y
                if (!n.IsHold) continue;
                double end = ms(n.EndY);
                if (end < p.Ms - tol) continue;

                double fromEnd = Math.Abs(end - p.Ms) * PxPerMs;
                double fromStart = Math.Abs(ms(n.Y) - p.Ms) * PxPerMs;
                if (HoldTailResize.IsTailGrab(fromEnd, fromStart, TAIL_GRAB_PX) && fromEnd < bestPx)
                {
                    bestPx = fromEnd;
                    best = new NoteRef(k, lane, n);
                }
            }
        }

        return best;
    }

    /// <summary>Which hold tails the current tool can grab: select = any kind, a BT/FX note tool = its own kind, laser = none.</summary>
    private bool tailKind(out NoteKind? kind)
    {
        kind = Tool switch
        {
            EditorTool.BtChip or EditorTool.BtHold => NoteKind.Bt,
            EditorTool.FxChip or EditorTool.FxHold => NoteKind.Fx,
            _ => null,
        };
        return Tool != EditorTool.Laser && !LockNotes.Value;
    }

    /// <summary>A hit on a laser point (plain sections) or, with <see cref="Anchor"/>, on an anchor of a curved path.</summary>
    public readonly record struct LaserHit(int Lane, LaserSection Section, int Index, bool Vf, bool Anchor = false);

    /// <summary>An anchor near the pointer: index into <see cref="LaserPaths.AnchorsOf"/> of the section.</summary>
    public readonly record struct AnchorHit(int Lane, LaserSection Section, int Index, bool Vf);

    /// <summary>
    /// Anchor of <paramref name="section"/> near the pointer (null section = every section near the pointer; curved paths
    /// and plain sections alike, plain sections have one anchor per point).
    /// </summary>
    public AnchorHit? AnchorAt(PlayfieldPointer p, (int Lane, LaserSection Section)? section = null, int? preferLane = null)
    {
        AnchorHit? best = null;
        double bestDist = laser_hit_px;
        double tol = tolMs(laser_hit_px);

        for (int lane = 0; lane < 2; lane++)
        {
            if (!shownLaser(lane)) continue;
            foreach (var s in chart.Notes.Lasers[lane])
            {
                if (section is { } only && (only.Lane != lane || only.Section != s)) continue;
                if (ms(s.Y) - tol > p.Ms) break;
                if (ms(s.EndY) + tol < p.Ms) continue;
                considerAnchors(p, lane, s, s.Y, LaserPaths.AnchorsOf(s), s.Width == 2, preferLane, ref best, ref bestDist);
            }
        }

        return best;
    }

    private void considerAnchors(PlayfieldPointer p, int lane, LaserSection s, long y, LaserPath anchors, bool wide, int? preferLane, ref AnchorHit? best, ref double bestDist)
    {
        for (int i = 0; i < anchors.Count; i++)
        {
            var a = anchors.Anchors[i];
            double dy = Math.Abs(ms(y + a.Ry) - p.Ms) * PxPerMs;
            if (dy > laser_hit_px) continue;

            for (int k = 0; k < (a.IsSlam ? 2 : 1); k++)
            {
                double dx = Math.Abs(PlayfieldLayout.LaserX(k == 0 ? a.V : a.Vf!.Value, wide) - p.X);
                double d = Math.Sqrt(dx * dx + dy * dy) - (preferLane == lane ? 3 : 0);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = new AnchorHit(lane, s, i, k == 1);
                }
            }
        }
    }

    /// <summary>
    /// Laser control point near the pointer (preferring <paramref name="preferLane"/>). Sections with a curved path are
    /// hit on their anchors (<see cref="LaserHit.Anchor"/>), never on the baked points.
    /// </summary>
    public LaserHit? LaserPointAt(PlayfieldPointer p, int? preferLane = null)
    {
        LaserHit? best = null;
        double bestDist = laser_hit_px;
        double tol = tolMs(laser_hit_px);

        for (int lane = 0; lane < 2; lane++)
        {
            if (!shownLaser(lane)) continue;
            foreach (var s in chart.Notes.Lasers[lane])
            {
                if (ms(s.Y) - tol > p.Ms) break;
                if (ms(s.EndY) + tol < p.Ms) continue;

                bool wide = s.Width == 2;
                if (LaserPaths.ValidPath(s) is { IsTrivial: false } curved)
                {
                    AnchorHit? ah = null;
                    double ad = bestDist;
                    considerAnchors(p, lane, s, s.Y, curved, wide, preferLane, ref ah, ref ad);
                    if (ah is { } h)
                    {
                        bestDist = ad;
                        best = new LaserHit(lane, s, h.Index, h.Vf, true);
                    }
                    continue;
                }

                for (int i = 0; i < s.Points.Count; i++)
                {
                    var pt = s.Points[i];
                    double dy = Math.Abs(ms(s.Y + pt.Ry) - p.Ms) * PxPerMs;
                    if (dy > laser_hit_px) continue;

                    consider(Math.Abs(PlayfieldLayout.LaserX(pt.V, wide) - p.X), false);
                    if (pt.IsSlam) consider(Math.Abs(PlayfieldLayout.LaserX(pt.Vf!.Value, wide) - p.X), true);

                    void consider(double dx, bool vf)
                    {
                        double d = Math.Sqrt(dx * dx + dy * dy) - (preferLane == lane ? 3 : 0);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            best = new LaserHit(lane, s, i, vf);
                        }
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Section of a lane covering a pulse.</summary>
    public LaserSection? SectionAt(int lane, long pulse) =>
        chart.Notes.Lasers[lane].FirstOrDefault(s => s.Y <= pulse && pulse <= s.EndY);

    /// <summary>Section whose drawn path passes under the pointer.</summary>
    public (int Lane, LaserSection Section)? LaserSegmentAt(PlayfieldPointer p)
    {
        double pulse = session.MsToPulse(p.Ms);
        for (int lane = 0; lane < 2; lane++)
        {
            if (!shownLaser(lane)) continue;
            foreach (var s in chart.Notes.Lasers[lane])
            {
                if (LaserMath.ValueAt(s, pulse) is not double v) continue;
                if (Math.Abs(PlayfieldLayout.LaserX(v, s.Width == 2) - p.X) <= PlayfieldLayout.LASER_HALF_WIDTH + 2)
                    return (lane, s);
            }
        }

        return null;
    }

    // ---- pointer -----------------------------------------------------------

    private enum DragMode
    {
        None,
        Move,
        Box,
        Hold,
        Resize,
        LaserNew,
        LaserPoint,
        Anchor,
        TailResize,
    }

    private DragMode mode;

    // move
    private List<NoteRef> moveNotes = new List<NoteRef>();
    private List<LaserPointRef> movePoints = new List<LaserPointRef>();
    private List<LaserSlamEndRef> moveEnds = new List<LaserSlamEndRef>();
    private long anchorPulse;
    private NoteKind? anchorKind;
    private int anchorLane;
    private (long Pulse, int Lane) applied;

    // box
    private PlayfieldPointer boxStart;
    private bool boxAdditive;

    // hold
    private long holdStart;

    // laser
    private long laserStartPulse;
    private double laserStartValue;
    private bool laserWide;
    private int laserLane;
    private LaserSection? laserOriginal;  // section in the chart being edited
    private LaserSection? laserBase;      // what drags are computed from (original, or original + inserted point)
    private int laserIndex;
    private bool laserVf;
    private (long Pulse, double Value)? laserApplied;
    private LaserSection? laserCurrent;   // what this drag has put into the chart so far

    public void PointerMove(PlayfieldPointer p)
    {
        Hover = p;
        updateHover();
        StateChanged?.Invoke();
    }

    /// <summary>Hold tail under the pointer that a press would resize (the playfield draws the resize hint on it).</summary>
    public NoteRef? HoveredTail { get; private set; }

    /// <summary>The tail to highlight: the one being dragged, else the hovered one while nothing is dragged.</summary>
    public NoteRef? TailHint => mode == DragMode.TailResize ? tailGrabbed : mode == DragMode.None ? HoveredTail : null;

    public void PointerLeave()
    {
        Hover = null;
        HoveredLaser = null;
        HoveredAnchor = null;
        HoveredTail = null;
        StateChanged?.Invoke();
    }

    private void updateHover()
    {
        HoveredLaser = null;
        HoveredAnchor = null;
        HoveredTail = null;
        if (Hover is not { } p) return;

        if (tailKind(out var tk)) HoveredTail = HoldTailAt(p, tk);

        switch (Tool)
        {
            case EditorTool.Select:
                var active = ActiveLaser;
                HoveredAnchor = active is { } a ? AnchorAt(p, a) : null;
                if (HoveredAnchor is { } ha)
                    HoveredLaser = (ha.Lane, ha.Section);
                else if (LaserSegmentAt(p) is { } seg)
                    HoveredLaser = seg;
                else if (AnchorAt(p, null, LaserSide) is { } any)
                    HoveredLaser = (any.Lane, any.Section);

                if (HoveredAnchor == null && HoveredLaser is { } hl)
                    HoveredAnchor = AnchorAt(p, hl);
                break;

            case EditorTool.Laser:
                if (Placement is { } pl)
                {
                    // classic: only the finishing click on the last point is an anchor hit
                    bool hits = ClassicLasers ? placementFinishAt(p, false) : !placementSlamAt(p, false);
                    if (hits && placementAnchorAt(p) is { } ph) HoveredAnchor = ph with { Section = pl.Preview };
                }
                else if (LaserPointAt(p, LaserSide) is { } h)
                {
                    // plain sections have one anchor per point, so the point index is the anchor index
                    HoveredAnchor = new AnchorHit(h.Lane, h.Section, h.Index, h.Vf);
                    HoveredLaser = (h.Lane, h.Section);
                }
                else if (LaserSegmentAt(p) is { } seg)
                    HoveredLaser = seg;
                break;
        }
    }

    /// <returns>true when the press started something (the caller then sends drags and the release).</returns>
    public bool PointerDown(PlayfieldPointer p, PointerButton button, bool ctrl, bool shift)
    {
        Hover = p;

        if (button == PointerButton.Right)
        {
            if (LockNotes.Value) return false;
            if (Tool == EditorTool.Laser && Placement != null)
            {
                // osu!: right click while placing puts a last anchor at the cursor, then finishes
                placeLastAt(p, ctrl);
                FinishPlacement();
                return false;
            }

            if (Tool == EditorTool.Select && openLaserMenu(p)) return false;
            // K-Shoot Editor 2.2: Ctrl+Shift+right click on a laser toggles wide keeping its values
            if (Tool == EditorTool.Laser && ctrl && shift && toggleWidthAt(p, keepPosition: false)) return false;
            DeleteAt(p);
            return false;
        }

        // K-Shoot Editor: Ctrl+click on an FX hold (with an FX tool) cycles its effect (Shift+Ctrl backwards); no drag
        if (ctrl && Tool is EditorTool.FxChip or EditorTool.FxHold && !LockNotes.Value && HoldTailAt(p, NoteKind.Fx) == null
            && PlayfieldLayout.FxLaneAt(p.X) is int fxLane && findNote(NoteKind.Fx, fxLane, p.Ms) is { IsHold: true } fxHold)
        {
            cycleHoldEffect(new NoteRef(NoteKind.Fx, fxLane, fxHold), shift ? -1 : 1);
            return false;
        }

        switch (Tool)
        {
            case EditorTool.Select:
                selectDown(p, ctrl);
                break;

            case EditorTool.BtChip:
            case EditorTool.FxChip:
                if (LockNotes.Value) return false;
                // a press on a hold's tail drags its length (the chip tool elsewhere on a hold turns it into a chip)
                if (HoldTailAt(p, Tool == EditorTool.BtChip ? NoteKind.Bt : NoteKind.Fx) is { } chipTail)
                {
                    beginTailResize(chipTail, false);
                    break;
                }

                placeChip(p, Tool == EditorTool.BtChip ? NoteKind.Bt : NoteKind.Fx);
                return false;

            case EditorTool.BtHold:
            case EditorTool.FxHold:
                if (LockNotes.Value) return false;
                holdDown(p, Tool == EditorTool.BtHold ? NoteKind.Bt : NoteKind.Fx);
                break;

            case EditorTool.Laser:
                if (LockNotes.Value) return false;
                laserDown(p, ctrl, shift);
                break;
        }

        StateChanged?.Invoke();
        return mode != DragMode.None;
    }

    /// <param name="snap">The drag-snap key is held (Options → Input → Compose, Shift by default): edits of existing
    /// laser points / anchors snap to the beat divisor and the value grid; without it they follow the pointer freely.</param>
    public void PointerDrag(PlayfieldPointer p, bool ctrl, bool snap = false)
    {
        Hover = p;
        dragSnapKey = snap;
        switch (mode)
        {
            case DragMode.Move: moveDrag(p); break;
            case DragMode.Box: boxDrag(p); break;
            case DragMode.Hold: holdDrag(p); break;
            case DragMode.Resize: resizeDrag(p); break;
            case DragMode.LaserNew: laserNewDrag(p, ctrl); break;
            case DragMode.LaserPoint: laserPointDrag(p, ctrl); break;
            case DragMode.Anchor: anchorDrag(p, ctrl); break;
            case DragMode.TailResize: tailDrag(p); break;
        }

        StateChanged?.Invoke();
    }

    public void PointerUp(PlayfieldPointer p)
    {
        switch (mode)
        {
            case DragMode.Move:
            case DragMode.LaserPoint:
                session.Commands.Commit();
                break;

            case DragMode.Anchor:
                session.Commands.Commit();
                if (anchorCurrent != null && Tool == EditorTool.Select) reselectAfterAnchorEdit(anchorCurrent);
                break;

            case DragMode.Hold:
                holdUp();
                break;

            case DragMode.Resize:
                resizeUp();
                break;

            case DragMode.TailResize:
                tailUp();
                break;

            case DragMode.LaserNew:
                laserNewUp();
                break;
        }

        mode = DragMode.None;
        SelectionBox = null;
        NoteDraft = null;
        LaserDraft = null;
        StateChanged?.Invoke();
    }

    /// <summary>Right click: the object under the cursor goes (laser point, note, then laser section).</summary>
    public bool DeleteAt(PlayfieldPointer p)
    {
        if (LaserPointAt(p, LaserSide) is LaserHit hit)
        {
            // one end of a slam: only that end goes (the slam becomes a plain anchor at the other value)
            if (IsSlamAnchor(hit.Section, hit.Index))
            {
                if (LaserPathCommands.DeleteSlamEnd(chart, hit.Lane, hit.Section, hit.Index, hit.Vf) is { } end) session.Commands.Execute(end);
                return true;
            }

            if (hit.Anchor)
            {
                if (LaserPathCommands.Delete(chart, hit.Lane, hit.Section, hit.Index) is { } del) session.Commands.Execute(del);
                return true;
            }

            if (hit.Section.Points.Count <= 2)
                session.Commands.Execute(new RemoveLaserSectionCommand(hit.Lane, hit.Section));
            else
                session.Commands.Execute(new RemoveLaserPointCommand(hit.Section, hit.Section.Points[hit.Index]));
            return true;
        }

        if (NoteAt(p) is NoteRef note)
        {
            session.Commands.Execute(new RemoveNotesCommand(new[] { note }, "Delete note"));
            return true;
        }

        if (LaserSegmentAt(p) is { } seg)
        {
            session.Commands.Execute(new RemoveLaserSectionCommand(seg.Lane, seg.Section));
            return true;
        }

        return false;
    }

    // ---- chips / holds -----------------------------------------------------

    private int? laneAt(NoteKind kind, float x) => kind == NoteKind.Bt ? PlayfieldLayout.BtLaneAt(x) : PlayfieldLayout.FxLaneAt(x);

    private bool placeChip(PlayfieldPointer p, NoteKind kind)
    {
        if (laneAt(kind, p.X) is not int lane) return false;

        // osu!mania: the chip tool on a hold turns it back into a chip at the hold's start (one undo step)
        if (findNote(kind, lane, p.Ms) is { IsHold: true } hold)
        {
            session.Commands.Execute(new SetNoteLengthCommand(new NoteRef(kind, lane, hold), 0));
            return true;
        }

        return place(kind, lane, PulseAt(p.Ms), 0);
    }

    private bool place(NoteKind kind, int lane, long y, long length)
    {
        var placed = new[] { (kind, lane, y, length) };
        if (!OverlapRule.CanPlace(chart, placed, new HashSet<Note>()))
        {
            Message?.Invoke("Overlaps an existing note");
            return false;
        }

        var note = new Note(y, length);
        // a new FX hold gets the toolbox's FX effect (part of the note, so one undo step)
        if (kind == NoteKind.Fx && length > 0 && NewHoldEffect?.Invoke() is { } effect) note.Effects.Add(new FxEffectEvent(effect));
        session.Commands.Execute(new AddNotesCommand(new[] { new NoteRef(kind, lane, note) }, length > 0 ? "Add hold" : "Add chip"));
        return true;
    }

    /// <summary>Ctrl+click on an FX hold: the next effect (definitions, presets, none), one undo step.</summary>
    private void cycleHoldEffect(NoteRef hold, int direction)
    {
        string? next = HoldEffects.Next(chart, hold.Note, direction);
        if (HoldEffects.Assign(new[] { hold }, next) is { } cmd) session.Commands.Execute(cmd);
        Message?.Invoke($"FX effect: {(next == null ? "none" : EffectCatalog.Find(next)?.Label ?? next)} (Ctrl+click cycles, Shift+Ctrl+click backwards)");
    }

    /// <summary>
    /// Adds a new laser section; with a toolbox slam sound its slams get it in the same undo step ("Add laser").
    /// </summary>
    private void addSection(int lane, LaserSection section)
    {
        IEditCommand cmd = new AddLaserSectionCommand(lane, section);
        var slams = section.Points.Where(pt => pt.IsSlam).Select(pt => section.Y + pt.Ry).ToList();
        if (slams.Count > 0 && NewSlamSound?.Invoke() is { } sound && SlamSounds.Set(chart, slams, sound) is { } setSound)
            cmd = new CompositeCommand("Add laser", new IEditCommand[] { cmd, setSound });
        session.Commands.Execute(cmd);
    }

    private void holdDown(PlayfieldPointer p, NoteKind kind)
    {
        if (laneAt(kind, p.X) is not int lane) return;

        // osu!mania: the hold tool on an existing chip / hold of this kind drags its length (start stays)
        if (findNote(kind, lane, p.Ms) is Note existing)
        {
            resizing = new NoteRef(kind, lane, existing);
            resizeApplied = resizeOriginal = existing.Length;
            session.Commands.BeginTransaction(existing.IsHold ? "Hold length" : "Chip to hold");
            mode = DragMode.Resize;
            return;
        }

        holdStart = PulseAt(p.Ms);
        NoteDraft = (kind, lane, holdStart, 0);
        mode = DragMode.Hold;
    }

    private void holdDrag(PlayfieldPointer p)
    {
        if (NoteDraft is not { } d) return;
        long end = PulseAt(p.Ms);
        NoteDraft = d with { Y = Math.Min(holdStart, end), Length = Math.Abs(end - holdStart) };
    }

    private void holdUp()
    {
        if (NoteDraft is not { } d) return;
        long length = d.Length > 0 ? d.Length : TickAfter(d.Y);
        place(d.Kind, d.Lane, d.Y, length);
    }

    // ---- chip <-> hold, hold length (hold tool on an existing note) --------------

    private NoteRef? resizing;
    private long resizeApplied;
    private long resizeOriginal;

    /// <summary>The note whose length the hold tool is dragging (in the chart, changed live; one undo step on release).</summary>
    public NoteRef? ResizingNote => mode == DragMode.Resize ? resizing : null;

    private void resizeDrag(PlayfieldPointer p)
    {
        if (resizing is not { } r) return;
        // the length follows the pointer from the note's start; at or before the start it is a chip
        long length = Math.Max(0, PulseAt(p.Ms) - r.Note.Y);
        if (length == resizeApplied) return;

        if (!OverlapRule.CanResize(chart, r, length))
        {
            Message?.Invoke("Overlaps an existing note"); // keeps the last length that fitted
            return;
        }

        session.Commands.Rollback();
        session.Commands.BeginTransaction(r.Note.IsHold && length > 0 ? "Hold length" : length == 0 ? "Hold to chip" : "Chip to hold");
        if (length != resizeOriginal) session.Commands.Execute(new SetNoteLengthCommand(r, length));
        session.NotifyChanged(); // commands inside a transaction raise no Changed
        resizeApplied = length;
    }

    private void resizeUp()
    {
        // released without changing the length: nothing happened (a chip stays a chip)
        if (resizing != null && resizeApplied != resizeOriginal) session.Commands.Commit();
        else session.Commands.Rollback();
        resizing = null;
    }

    // ---- hold tail drag (select tool, chip tools) ---------------------------------

    private NoteRef? tailGrabbed;
    private List<(NoteRef Note, long Length)> tailTargets = new List<(NoteRef, long)>();
    private long tailApplied;

    /// <summary>The hold whose tail is being dragged (smoke test / playfield hint).</summary>
    public NoteRef? TailResizing => mode == DragMode.TailResize ? tailGrabbed : null;

    /// <summary>
    /// Press on a hold's tail: the grabbed hold's end follows the pointer; when it is part of the selection, every
    /// selected hold changes by the same delta (Core <see cref="HoldTailResize"/>). Select tool: an unselected hold
    /// becomes the selection, like a click on it.
    /// </summary>
    private void beginTailResize(NoteRef tail, bool select)
    {
        var holds = new List<NoteRef> { tail };
        if (selection.Contains(tail))
            holds.AddRange(selection.Notes.Where(n => n.Note.IsHold && n.Note != tail.Note));
        else if (select)
        {
            selection.Clear();
            selection.Add(tail);
        }

        tailGrabbed = tail;
        tailTargets = holds.Select(n => (n, n.Note.Length)).ToList();
        tailApplied = 0;
        session.Commands.BeginTransaction("Hold length");
        mode = DragMode.TailResize;
    }

    private void tailDrag(PlayfieldPointer p)
    {
        if (tailGrabbed is not { } g) return;
        long delta = HoldTailResize.GrabbedLength(g.Note.Y, PulseAt(p.Ms)) - tailTargets[0].Length;
        if (delta == tailApplied) return;

        if (!applyTail(delta))
        {
            Message?.Invoke("Overlaps an existing note"); // keeps the last length that fitted
            applyTail(tailApplied);
        }
    }

    private bool applyTail(long delta)
    {
        session.Commands.Rollback();
        var lengths = tailTargets.Select((t, i) => (t.Note, Length: i == 0
            ? t.Length + delta
            : HoldTailResize.OtherLength(t.Length, delta, TickAfter(t.Note.Note.Y)))).ToList();
        bool chip = lengths.Count == 1 && lengths[0].Length == 0;
        session.Commands.BeginTransaction(chip ? "Hold to chip" : "Hold length");

        if (delta != 0)
        {
            if (lengths.Any(x => !OverlapRule.CanResize(chart, x.Note, x.Length))) return false;
            foreach (var (note, length) in lengths)
                if (length != note.Note.Length) session.Commands.Execute(new SetNoteLengthCommand(note, length));
        }

        session.NotifyChanged(); // commands inside a transaction raise no Changed
        tailApplied = delta;
        return true;
    }

    private void tailUp()
    {
        if (tailApplied != 0) session.Commands.Commit();
        else session.Commands.Rollback();
        tailGrabbed = null;
        tailTargets.Clear();
    }

    // ---- select / move -----------------------------------------------------

    private void selectDown(PlayfieldPointer p, bool ctrl)
    {
        // the selected laser: its anchors drag, Ctrl+click on its path inserts one
        if (ActiveLaser is { } active && !LockNotes.Value)
        {
            if (AnchorAt(p, active) is { } ah)
            {
                if (SlamEndAt(active.Lane, active.Section, ah.Index, ah.Vf) is { } activeEnd)
                {
                    pressSlamEnd(activeEnd, ah.Index, ctrl);
                    return;
                }

                // part of a bigger selection: the whole selection moves (osu!: dragging a selected object)
                if (movesSelection(active.Lane, active.Section))
                {
                    // moved by the pointer's delta from the grabbed anchor
                    beginMove(active.Section.Y + LaserPaths.AnchorsOf(active.Section).Anchors[ah.Index].Ry, null, 0);
                    return;
                }

                beginAnchorEdit(active.Lane, active.Section, LaserPaths.AnchorsOf(active.Section), ah.Index, ah.Vf);
                return;
            }

            if (ctrl && LaserSegmentAt(p) is { } on && on.Section == active.Section)
            {
                long pulse = PulseAt(p.Ms);
                double v = laserValue(p.X, active.Section.Width == 2, false);
                if (LaserPathCommands.Insert(chart, active.Lane, active.Section, pulse, v) is { } ins)
                {
                    session.Commands.Execute(ins);
                    var n = ins.Result.Single();
                    activate(n.Lane, n.Section);
                }
                else Message?.Invoke("No room for an anchor there");
                return;
            }
        }

        if (LaserPointAt(p, LaserSide) is LaserHit hit)
        {
            // one end of a slam (plain point or anchor of a curved path): selected on its own, dragged as that end
            if (SlamEndAt(hit.Lane, hit.Section, hit.Index, hit.Vf) is { } end)
            {
                pressSlamEnd(end, hit.Index, ctrl);
                return;
            }

            if (hit.Anchor)
            {
                // a curved path is selected as a whole (its baked points are not objects of their own); a press on an
                // anchor drags that anchor at once (osu!: no click to select first), unless a bigger selection moves
                bool multi = !ctrl && movesSelection(hit.Lane, hit.Section);
                if (!ctrl && !multi) selection.Clear();
                session.SelectSections(new[] { (hit.Lane, hit.Section) });
                activeLaser = (hit.Lane, hit.Section);
                if (ctrl) return;
                if (multi) beginMove(hit.Section.Y + LaserPaths.AnchorsOf(hit.Section).Anchors[hit.Index].Ry, null, 0);
                else if (!LockNotes.Value) beginAnchorEdit(hit.Lane, hit.Section, LaserPaths.AnchorsOf(hit.Section), hit.Index, hit.Vf);
                return;
            }

            var r = new LaserPointRef(hit.Lane, hit.Section, hit.Section.Points[hit.Index]);
            activeLaser = (hit.Lane, hit.Section);
            if (ctrl)
            {
                if (selection.Contains(r)) selection.Remove(r);
                else selection.Add(r);
                return;
            }

            if (movesSelection(hit.Lane, hit.Section) && selection.Contains(r))
            {
                beginMove(hit.Section.Y + r.Point.Ry, null, 0);
                return;
            }

            // a plain point: selected (Delete removes just it) and dragged at once
            if (!selection.Contains(r))
            {
                selection.Clear();
                selection.Add(r);
            }

            if (!LockNotes.Value) beginAnchorEdit(hit.Lane, hit.Section, LaserPaths.AnchorsOf(hit.Section), hit.Index, hit.Vf);
            return;
        }

        // a hold's tail: drag its length (and that of the other selected holds), never a move
        if (!ctrl && !LockNotes.Value && HoldTailAt(p) is { } tail)
        {
            beginTailResize(tail, true);
            return;
        }

        if (NoteAt(p) is NoteRef note)
        {
            if (ctrl)
            {
                selection.Toggle(note);
                return;
            }

            if (!selection.Contains(note))
            {
                selection.Clear();
                selection.Add(note);
            }

            // a hold grabbed on its body moves by the pointer's delta from where it was grabbed (it used to put its head
            // under the pointer, so the whole hold jumped); a chip keeps snapping its head to the pointer
            beginMove(note.Note.IsHold ? Math.Clamp(PulseAt(p.Ms), note.Note.Y, note.Note.EndY) : note.Note.Y, note.Kind, note.Lane);
            return;
        }

        // the body of a laser: select the whole section (osu!: click a slider body), drag moves it
        if (LaserSegmentAt(p) is { } body)
        {
            bool all = body.Section.Points.All(pt => selection.Contains(new LaserPointRef(body.Lane, body.Section, pt)));
            if (ctrl)
            {
                foreach (var pt in body.Section.Points)
                {
                    var r = new LaserPointRef(body.Lane, body.Section, pt);
                    if (all) selection.Remove(r);
                    else selection.Add(r);
                }
                if (!all) activeLaser = body;
                return;
            }

            if (!all)
            {
                selection.Clear();
                session.SelectSections(new[] { body });
            }

            activeLaser = body;
            beginMove(body.Section.Y, null, 0);
            return;
        }

        boxAdditive = ctrl;
        activeLaser = null;
        if (!ctrl) selection.Clear();
        moveNotes = selection.Notes.ToList();
        movePoints = selection.LaserPoints.ToList();
        moveEnds = selection.SlamEnds.ToList();
        boxStart = p;
        SelectionBox = (p.Ms, p.X, p.Ms, p.X);
        mode = DragMode.Box;
    }

    private void beginMove(long pulse, NoteKind? kind, int lane)
    {
        if (LockNotes.Value) return;

        moveNotes = selection.Notes.ToList();
        movePoints = selection.LaserPoints.ToList();
        moveEnds = selection.SlamEnds.ToList();
        anchorPulse = pulse;
        anchorKind = kind;
        anchorLane = lane;
        applied = (0, 0);
        session.Commands.BeginTransaction("Move");
        mode = DragMode.Move;
    }

    private void moveDrag(PlayfieldPointer p)
    {
        long dp = PulseAt(p.Ms) - anchorPulse;
        int dl = anchorKind is NoteKind k ? PlayfieldLayout.NearestLane(k, p.X) - anchorLane : 0;
        if ((dp, dl) == applied) return;

        if (!applyMove(dp, dl))
            applyMove(applied.Pulse, applied.Lane);
    }

    private bool applyMove(long dp, int dl)
    {
        session.Commands.Rollback();
        session.Commands.BeginTransaction("Move");
        restoreSelection(moveNotes, movePoints, moveEnds);

        if (dp == 0 && dl == 0)
        {
            applied = (0, 0);
            return true;
        }

        var cmd = SelectionOps.Move(chart, selection, dp, dl);
        if (cmd == null) return false;

        session.Commands.Execute(cmd);
        session.NotifyChanged(); // commands inside a transaction raise no Changed
        selection.Clear();
        Reselect(cmd);
        followActive(cmd);
        applied = (dp, dl);
        return true;
    }

    private void restoreSelection(IEnumerable<NoteRef> notes, IEnumerable<LaserPointRef> points, IEnumerable<LaserSlamEndRef> ends)
    {
        selection.Clear();
        foreach (var n in notes) selection.Add(n);
        foreach (var lp in points) selection.Add(lp);
        foreach (var e in ends) selection.Add(e);
    }

    /// <summary>
    /// The laser under the pointer switches between normal and wide (one undo step): <paramref name="keepPosition"/> keeps
    /// it where it is on screen (values rescaled, narrowing clamps what lies beyond the lanes), else it keeps its values.
    /// </summary>
    private bool toggleWidthAt(PlayfieldPointer p, bool keepPosition)
    {
        (int Lane, LaserSection Section)? target = LaserPointAt(p, LaserSide) is { } h ? (h.Lane, h.Section) : LaserSegmentAt(p);
        if (target is not { } t) return false;
        var (s, clamped) = LaserEditing.WithWidth(t.Section, t.Section.Width == 2 ? 1 : 2, keepPosition);
        string name = (s.Width == 2 ? "Wide laser" : "Normal laser") + (keepPosition ? " (keep position)" : " (keep values)");
        session.Commands.Execute(new ReplaceLaserSectionsCommand(name, new[] { (t.Lane, t.Section, t.Lane, s) }));
        Message?.Invoke(clamped > 0 ? $"{name}: {clamped} value(s) beyond the normal range were moved to the edge" : name);
        updateHover();
        return true;
    }

    /// <summary>
    /// True when the selection holds this section (a point, a slam end or all of it) and something else as well: a press
    /// on it then moves the whole selection instead of dragging one anchor.
    /// </summary>
    private bool movesSelection(int lane, LaserSection s)
    {
        bool touches = selection.LaserPoints.Any(x => x.Section == s) || selection.SlamEnds.Any(x => x.Section == s);
        return touches && (selection.Notes.Count > 0 || selection.Sections.Any(x => x.Section != s));
    }

    // ---- slam ends ---------------------------------------------------------

    /// <summary>True when anchor <paramref name="index"/> of the section (<see cref="LaserPaths.AnchorsOf"/>; plain sections: the point) is a slam.</summary>
    public static bool IsSlamAnchor(LaserSection s, int index)
    {
        var anchors = LaserPaths.AnchorsOf(s);
        return index >= 0 && index < anchors.Count && anchors.Anchors[index].IsSlam;
    }

    /// <summary>The slam end at anchor <paramref name="index"/> of a section, or null when that anchor is no slam.</summary>
    public static LaserSlamEndRef? SlamEndAt(int lane, LaserSection s, int index, bool end)
    {
        var anchors = LaserPaths.AnchorsOf(s);
        if (index < 0 || index >= anchors.Count || !anchors.Anchors[index].IsSlam) return null;
        long ry = anchors.Anchors[index].Ry;
        return s.Points.FirstOrDefault(pt => pt.Ry == ry && pt.IsSlam) is { } point ? new LaserSlamEndRef(lane, s, point, end) : null;
    }

    /// <summary>
    /// A press on a slam end (select tool): Ctrl toggles it in the selection; otherwise it becomes the selection (unless
    /// already selected, so a selected pair drags together) and its anchor end is dragged; the section is the active laser.
    /// </summary>
    private void pressSlamEnd(LaserSlamEndRef end, int anchorIndex, bool ctrl)
    {
        activeLaser = (end.Lane, end.Section);
        if (ctrl)
        {
            selection.Toggle(end);
            return;
        }

        if (!selection.IsEndSelected(end.PointRef, end.End))
        {
            selection.Clear();
            selection.Add(end);
        }

        if (LockNotes.Value) return;
        beginAnchorEdit(end.Lane, end.Section, LaserPaths.AnchorsOf(end.Section), anchorIndex, end.End,
            (selection.IsEndSelected(end.PointRef, false), selection.IsEndSelected(end.PointRef, true)));
    }

    /// <summary>After an anchor edit: the given ends of the slam at <paramref name="pulse"/> are the selection (whole section when it is no slam any more).</summary>
    private void selectEnds(int lane, LaserSection s, long pulse, (bool Start, bool End) ends)
    {
        if (s.Points.FirstOrDefault(pt => s.Y + pt.Ry == pulse && pt.IsSlam) is not { } point)
        {
            activate(lane, s);
            return;
        }

        selection.Clear();
        if (ends.Start) selection.Add(new LaserSlamEndRef(lane, s, point, false));
        if (ends.End) selection.Add(new LaserSlamEndRef(lane, s, point, true));
        activeLaser = (lane, s);
    }

    /// <summary>After a command that replaced laser sections, select the new ones (notes keep their identity).</summary>
    public void Reselect(IEditCommand cmd)
    {
        switch (cmd)
        {
            case CompositeCommand c:
                foreach (var sub in c.Commands) Reselect(sub);
                break;

            case ReplaceLaserSectionsCommand r:
                session.SelectSections(r.Result);
                break;

            case TransformNotesCommand t:
                foreach (var n in t.Result) selection.Add(n);
                break;

            case AddNotesCommand a:
                foreach (var n in a.Notes) selection.Add(n);
                break;

            case AddLaserSectionCommand s:
                session.SelectSections(new[] { (s.Lane, s.Section) });
                break;
        }
    }

    /// <summary>Keeps <see cref="ActiveLaser"/> on the section that replaced it.</summary>
    private void followActive(IEditCommand cmd)
    {
        if (activeLaser is not { } a) return;
        foreach (var r in replacements(cmd))
        {
            if (r.Old != a.Section) continue;
            activeLaser = (r.NewLane, r.New);
            return;
        }
    }

    private static IEnumerable<(LaserSection Old, int NewLane, LaserSection New)> replacements(IEditCommand cmd)
    {
        switch (cmd)
        {
            case CompositeCommand c:
                foreach (var sub in c.Commands)
                foreach (var r in replacements(sub))
                    yield return r;
                break;

            case ReplaceLaserSectionsCommand r:
                foreach (var item in r.Items) yield return (item.Old, item.NewLane, item.New);
                break;
        }
    }

    /// <summary>Makes a section the selected, editable laser (selection = its points).</summary>
    private void activate(int lane, LaserSection s)
    {
        selection.Clear();
        session.SelectSections(new[] { (lane, s) });
        activeLaser = (lane, s);
    }

    private void boxDrag(PlayfieldPointer p)
    {
        SelectionBox = (boxStart.Ms, boxStart.X, p.Ms, p.X);
        SelectInBox(boxStart, p, boxAdditive);
    }

    /// <summary>Selects everything inside the rectangle spanned by two pointers.</summary>
    public void SelectInBox(PlayfieldPointer a, PlayfieldPointer b, bool additive)
    {
        restoreSelection(additive ? moveNotes : Array.Empty<NoteRef>(), additive ? movePoints : Array.Empty<LaserPointRef>(),
            additive ? moveEnds : Array.Empty<LaserSlamEndRef>());

        double ms1 = Math.Min(a.Ms, b.Ms), ms2 = Math.Max(a.Ms, b.Ms);
        float x1 = Math.Min(a.X, b.X), x2 = Math.Max(a.X, b.X);
        long p1 = (long)Math.Floor(session.MsToPulse(ms1)), p2 = (long)Math.Ceiling(session.MsToPulse(ms2));
        bool insideX(double v, bool wide) => PlayfieldLayout.LaserX(v, wide) is var x && x >= x1 && x <= x2;

        foreach (var kind in new[] { NoteKind.Bt, NoteKind.Fx })
        {
            if (!shown(kind)) continue;
            for (int lane = 0; lane < NoteData.LaneCount(kind); lane++)
            {
                float left = PlayfieldLayout.LaneLeft(kind, lane), right = left + PlayfieldLayout.LaneWidth(kind);
                if (right < x1 || left > x2) continue;

                foreach (var n in chart.Notes.Lane(kind, lane))
                {
                    if (n.Y > p2) break;
                    if (n.Y >= p1) selection.Add(new NoteRef(kind, lane, n));
                }
            }
        }

        for (int lane = 0; lane < 2; lane++)
        {
            if (!shownLaser(lane)) continue;
            foreach (var s in chart.Notes.Lasers[lane])
            {
                if (s.Y > p2) break;
                if (s.EndY < p1) continue;

                foreach (var pt in s.Points)
                {
                    long y = s.Y + pt.Ry;
                    if (y < p1 || y > p2) continue;
                    bool start = insideX(pt.V, s.Width == 2), end = pt.IsSlam && insideX(pt.Vf!.Value, s.Width == 2);
                    // a slam whose box holds only one end selects that end
                    if (start && (end || !pt.IsSlam)) selection.Add(new LaserPointRef(lane, s, pt));
                    else if (start || end) selection.Add(new LaserSlamEndRef(lane, s, pt, end));
                }
            }
        }
    }

    // ---- lasers ------------------------------------------------------------

    private double laserValue(float x, bool wide, bool ctrl)
    {
        double v = PlayfieldLayout.LaserValue(x, wide);
        return ctrl ? Math.Round(v, 6) : ClassicLasers ? ClassicLaser.Snap(v, wide) : PlayfieldLayout.SnapLaserValue(v);
    }

    /// <summary>The value a laser click at <paramref name="x"/> would get (snapped; classic grid while the classic key is held) — the cursor ghost.</summary>
    public double LaserValueAt(float x, bool wide) => laserValue(x, wide, false);

    private void laserDown(PlayfieldPointer p, bool ctrl, bool shift)
    {
        // 0. placement in progress: a click on a placed anchor cycles its type, any other click adds an anchor
        if (Placement != null)
        {
            placementClick(p, ctrl);
            return;
        }

        // K-Shoot Editor 2.2: Ctrl+Shift+click on a laser toggles wide keeping its place on screen
        if (ctrl && shift && toggleWidthAt(p, keepPosition: true)) return;

        // 1. grab an existing point / anchor (switching sides if it is on the other laser)
        if (LaserPointAt(p, LaserSide) is LaserHit hit)
        {
            tools.LaserSide.Value = hit.Lane;
            // curved anchors and both ends of a slam drag through the anchor model (a slam end moves the slam in time too)
            if (hit.Anchor || IsSlamAnchor(hit.Section, hit.Index)) beginAnchorEdit(hit.Lane, hit.Section, LaserPaths.AnchorsOf(hit.Section), hit.Index, hit.Vf);
            else beginPointEdit(hit.Lane, hit.Section, hit.Section, hit.Index, hit.Vf);
            return;
        }

        int lane = LaserSide;
        long pulse = PulseAt(p.Ms);

        // 2a. inside a curved path of the active side: insert an anchor and drag it
        if (SectionAt(lane, pulse) is LaserSection curved && LaserPaths.HasCurvedPath(curved))
        {
            var ins = LaserPathEditing.InsertAnchor(LaserPaths.AnchorsOf(curved), pulse - curved.Y, laserValue(p.X, curved.Width == 2, ctrl));
            if (ins is { } r)
            {
                beginAnchorEdit(lane, curved, r.Path, r.Index, false, snapped: true);
                anchorApplied = null;
                applyAnchorEdit(pulse, r.Path.Anchors[r.Index].V, true);
            }
            return;
        }

        // 2b. inside a section of the active side: insert a point there (or grab the one on that pulse)
        if (SectionAt(lane, pulse) is LaserSection section)
        {
            int existing = section.Points.FindIndex(pt => section.Y + pt.Ry == pulse);
            if (existing >= 0)
            {
                beginPointEdit(lane, section, section, existing, false);
                return;
            }

            var withPoint = section.Clone();
            long ry = pulse - withPoint.Y;
            int index = withPoint.Points.FindLastIndex(pt => pt.Ry < ry) + 1;
            withPoint.Points.Insert(index, new LaserPoint(ry, laserValue(p.X, section.Width == 2, ctrl)));
            beginPointEdit(lane, section, withPoint, index, false, snapped: true);
            laserApplied = null;
            applyPointEdit(pulse, withPoint.Points[index].V, true);
            return;
        }

        // 3. empty time: draw a new section
        laserLane = lane;
        laserWide = WideLaser.Value ^ shift;
        laserStartPulse = pulse;
        laserStartValue = laserValue(p.X, laserWide, ctrl);
        LaserDraft = (lane, BuildNewSection(laserStartPulse, laserStartValue, pulse, laserStartValue, laserWide) ?? new LaserSection(pulse, new[] { new LaserPoint(0, laserStartValue) }, laserWide ? 2 : 1));
        mode = DragMode.LaserNew;
    }

    /// <summary>A new section from (y0, v0) to (y1, v1); see <see cref="LaserEditing.NewSection"/> (same pulse = a lone slam, no tail).</summary>
    public LaserSection? BuildNewSection(long y0, double v0, long y1, double v1, bool wide) =>
        LaserEditing.NewSection(y0, v0, y1, v1, wide ? 2 : 1);

    private void laserNewDrag(PlayfieldPointer p, bool ctrl)
    {
        long pulse = PulseAt(p.Ms);
        double v = laserValue(p.X, laserWide, ctrl);
        var s = BuildNewSection(laserStartPulse, laserStartValue, pulse, v, laserWide);
        LaserDraft = s != null ? (laserLane, s) : (laserLane, new LaserSection(laserStartPulse, new[] { new LaserPoint(0, laserStartValue) }, laserWide ? 2 : 1));
    }

    private void laserNewUp()
    {
        if (LaserDraft is not { } d) return;

        // a click without movement starts osu!lazer-style placement with this first anchor (a sideways drag on one beat
        // is a lone slam: one point, kept as it is)
        if (d.Section.Points.Count < 2 && !d.Section.Points[0].IsSlam)
        {
            var first = new LaserAnchor(0, laserStartValue, type: LaserCurveType.Linear, auto: true);
            Placement = newPlacement(laserLane, laserWide, laserStartPulse, new LaserPath(new[] { first }));
            return;
        }

        if (!OverlapRule.CanPlace(chart, new[] { (d.Lane, d.Section) }, new HashSet<LaserSection>()))
        {
            Message?.Invoke(OverlapRule.LaserGapMessage);
            return;
        }

        addSection(d.Lane, d.Section);
    }

    private void beginPointEdit(int lane, LaserSection original, LaserSection basis, int index, bool vf, bool snapped = false)
    {
        var pt = basis.Points[index];
        beginGrab(basis.Y + pt.Ry, vf ? pt.ValueAfter : pt.V, basis.Width == 2, snapped);
        laserLane = lane;
        laserOriginal = original;
        laserBase = basis;
        laserIndex = index;
        laserVf = vf;
        laserCurrent = null;
        laserApplied = (basis.Y + basis.Points[index].Ry, vf ? basis.Points[index].ValueAfter : basis.Points[index].V);
        session.Commands.BeginTransaction("Edit laser");
        mode = DragMode.LaserPoint;
    }

    private void laserPointDrag(PlayfieldPointer p, bool ctrl)
    {
        if (laserBase == null) return;
        var (pulse, v) = dragTarget(p, laserBase.Width == 2, ctrl, false);
        if (laserApplied is { } a && a.Pulse == pulse && a.Value == v) return;

        if (!applyPointEdit(pulse, v, false) && laserApplied is { } last)
            applyPointEdit(last.Pulse, last.Value, true);
    }

    private bool applyPointEdit(long pulse, double value, bool force)
    {
        if (laserOriginal == null || laserBase == null) return false;

        var edited = EditPoint(laserBase, laserIndex, pulse, value, laserVf);
        if (edited == null) return false;
        var ignore = new HashSet<LaserSection> { laserOriginal };
        if (laserCurrent != null) ignore.Add(laserCurrent);

        if (!OverlapRule.CanPlace(chart, new[] { (laserLane, edited) }, ignore))
        {
            if (!force)
            {
                Message?.Invoke(OverlapRule.LaserGapMessage);
                return false;
            }
        }

        session.Commands.Rollback();
        session.Commands.BeginTransaction("Edit laser");
        session.Commands.Execute(new ReplaceLaserSectionsCommand("Edit laser", new[] { (laserLane, laserOriginal, laserLane, edited) }));
        session.NotifyChanged();
        laserCurrent = edited;
        laserApplied = (pulse, value);
        return true;
    }

    // ---- anchor drag (curved paths, the selected laser) -----------------------

    private LaserPath? anchorBase;
    private LaserSection? anchorOriginal;
    private LaserSection? anchorCurrent;
    private int editLane;
    private int anchorIndex;
    private bool anchorVf;
    private (long Pulse, double Value)? anchorApplied;

    /// <summary>Slam ends selected while their anchor is dragged (select tool); null = the section is selected as a whole after the drag.</summary>
    private (bool Start, bool End)? anchorEnds;

    /// <summary>The anchor end being dragged (smoke test / playfield): section as it is in the chart now, anchor index, end.</summary>
    public (LaserSection Section, int Index, bool End)? DraggedAnchor =>
        mode == DragMode.Anchor && (anchorCurrent ?? anchorOriginal) is { } s ? (s, anchorIndex, anchorVf) : null;

    private void beginAnchorEdit(int lane, LaserSection original, LaserPath basis, int index, bool vf, (bool Start, bool End)? ends = null, bool snapped = false)
    {
        var grabbed = basis.Anchors[index];
        beginGrab(original.Y + grabbed.Ry, vf ? grabbed.ValueAfter : grabbed.V, original.Width == 2, snapped);
        editLane = lane;
        anchorOriginal = original;
        anchorBase = basis;
        anchorIndex = index;
        anchorVf = vf;
        anchorEnds = ends;
        anchorCurrent = null;
        var a = basis.Anchors[index];
        anchorApplied = (original.Y + a.Ry, vf ? a.ValueAfter : a.V);
        session.Commands.BeginTransaction("Move anchor");
        mode = DragMode.Anchor;
    }

    private void anchorDrag(PlayfieldPointer p, bool ctrl)
    {
        if (anchorOriginal == null || anchorBase == null) return;
        var (pulse, v) = dragTarget(p, anchorOriginal.Width == 2, ctrl, anchorBase.IsControl(anchorIndex));
        if (anchorApplied is { } a && a.Pulse == pulse && a.Value == v) return;

        if (!applyAnchorEdit(pulse, v, false) && anchorApplied is { } last)
            applyAnchorEdit(last.Pulse, last.Value, true);
    }

    private bool applyAnchorEdit(long pulse, double value, bool force)
    {
        if (anchorOriginal == null || anchorBase == null) return false;

        var path = LaserPathEditing.MoveAnchor(anchorBase, anchorIndex, pulse - anchorOriginal.Y, value, anchorVf,
            PlayfieldLayout.ControlLimits(anchorOriginal.Width == 2));
        if (path == null) return false;
        var edited = LaserPaths.ToSection(path, anchorOriginal.Y, anchorOriginal.Width);
        var ignore = new HashSet<LaserSection> { anchorOriginal };
        if (anchorCurrent != null) ignore.Add(anchorCurrent);
        if (edited.Y < 0) return false;
        if (!force && !OverlapRule.CanPlace(chart, new[] { (editLane, edited) }, ignore))
        {
            Message?.Invoke(OverlapRule.LaserGapMessage);
            return false;
        }

        session.Commands.Rollback();
        session.Commands.BeginTransaction("Move anchor");
        session.Commands.Execute(new ReplaceLaserSectionsCommand("Move anchor", new[] { (editLane, anchorOriginal, editLane, edited) }));
        session.NotifyChanged();
        anchorCurrent = edited;
        anchorApplied = (pulse, value);
        anchorPulseNow = anchorOriginal.Y + path.Anchors[anchorIndex].Ry;
        if (Tool == EditorTool.Select) reselectAfterAnchorEdit(edited);
        return true;
    }

    // ---- free drags of existing points / anchors -------------------------------

    /// <summary>Pointer minus the grabbed point at the press (pulse, raw value): a drag moves the point by the pointer's delta.</summary>
    private (double Pulse, double Value) grab;

    /// <summary>The drag places something new (an inserted point / anchor): it keeps snapping like placement.</summary>
    private bool dragSnapped;

    /// <summary>
    /// Time step of free anchor drags: 5 pulses = one VOX tick = 1/192 of a 4/4 measure (ksh2vox rounds kson pulses to
    /// ticks, the ksh writer needs at most 192 lines per measure for it); every measure start lies on it (960·n/d with d ≤ 64).
    /// </summary>
    public const long FREE_DRAG_PULSES = 5;

    /// <summary>The drag-snap key during the current drag event (<see cref="PointerDrag"/>).</summary>
    private bool dragSnapKey;

    private void beginGrab(long pulse, double value, bool wide, bool snapped)
    {
        dragSnapped = snapped;
        dragSnapKey = false;
        grab = !snapped && Hover is { } h ? (session.MsToPulse(h.Ms) - pulse, PlayfieldLayout.LaserValueRaw(h.X, wide) - value) : (0, 0);
    }

    /// <summary>
    /// Where a drag of an existing point / anchor puts it: by default free — the pointer's delta from the press, time to
    /// <see cref="FREE_DRAG_PULSES"/> (1/192 measure: the finest step kson, ksh and VOX all hold exactly), value unsnapped
    /// (6 decimals). With the drag-snap key (or for a just inserted point) the time snaps to
    /// the beat divisor and the value to the laser grid (1/50; classic lasers 1/10), Ctrl still frees the value. Values
    /// are clamped to 0..1 except for Bezier controls (<see cref="PlayfieldLayout.ControlLimits"/>).
    /// </summary>
    private (long Pulse, double Value) dragTarget(PlayfieldPointer p, bool wide, bool ctrl, bool control)
    {
        bool snap = dragSnapKey || dragSnapped;
        long pulse = Math.Max(0, (long)Math.Round((session.MsToPulse(p.Ms) - grab.Pulse) / FREE_DRAG_PULSES) * FREE_DRAG_PULSES);
        if (dragSnapKey || (dragSnapped && GridSnap.Value)) pulse = Math.Max(0, session.Timing.Snap(pulse, Divisor.Value));

        double raw = PlayfieldLayout.LaserValueRaw(p.X, wide) - grab.Value;
        double v;
        if (!snap || ctrl) v = Math.Round(raw, 6);
        else if (ClassicLasers)
        {
            int steps = wide ? 2 * ClassicLaser.VALUE_STEPS : ClassicLaser.VALUE_STEPS;
            v = Math.Round(raw * steps) / steps;
        }
        else v = PlayfieldLayout.SnapLaserValue(raw);

        if (!control) v = Math.Clamp(v, 0, 1);
        else
        {
            var (min, max) = PlayfieldLayout.ControlLimits(wide);
            v = Math.Clamp(v, min, max);
        }

        return (pulse, v);
    }

    /// <summary>
    /// Right click while placing (osu!): a last anchor at the cursor when it lies after the draft's last anchor (or makes
    /// a slam of it on its beat); on the last anchor itself or before it nothing is added. Classic lasers alike.
    /// </summary>
    private void placeLastAt(PlayfieldPointer p, bool ctrl)
    {
        if (Placement is not { } pl) return;
        long ry = PulseAt(p.Ms) - pl.Y;
        double v = laserValue(p.X, pl.Wide, ctrl);
        var last = pl.Path.Anchors[^1];
        if (ry < last.Ry || (ry == last.Ry && !LaserPathEditing.IsSlamClick(pl.Path, ry, v))) return;
        if (withPointer(pl, p, ctrl) is not { } next) return;
        if (!OverlapRule.CanPlace(chart, new[] { (pl.Lane, next.Preview) }, new HashSet<LaserSection>())) return;
        Placement = next;
    }

    /// <summary>Absolute pulse of the dragged anchor after the last applied step.</summary>
    private long anchorPulseNow;

    private void reselectAfterAnchorEdit(LaserSection edited)
    {
        if (anchorEnds is { } ends) selectEnds(editLane, edited, anchorPulseNow, ends);
        else activate(editLane, edited);
    }

    // ---- context menu (select tool) ---------------------------------------

    private static readonly (LaserCurveType Type, string Label)[] curve_types =
    {
        (LaserCurveType.Linear, "Straight"),
        (LaserCurveType.PerfectCurve, "Perfect curve"),
        (LaserCurveType.Bezier, "Bezier"),
    };

    /// <summary>
    /// The items a right click at <paramref name="p"/> offers: on an anchor (of the selected laser first) its type
    /// (Inherit / Straight / Perfect curve / Bezier) and Delete; on a laser's path the curve type of that segment and
    /// Delete laser. Null = no laser there.
    /// </summary>
    public IReadOnlyList<LaserMenuItem>? LaserMenuAt(PlayfieldPointer p)
    {
        var active = ActiveLaser;
        AnchorHit? hit = active is { } a ? AnchorAt(p, a) : null;
        hit ??= AnchorAt(p, null, LaserSide);

        if (hit is { } h)
        {
            var anchors = LaserPaths.AnchorsOf(h.Section);
            var anchor = anchors.Anchors[h.Index];
            bool last = h.Index == anchors.Count - 1;
            bool mustStart = h.Index == 0 || anchor.IsSlam;
            string end = anchor.IsSlam ? h.Vf ? " (slam end)" : " (slam start)" : "";
            var items = new List<LaserMenuItem>
            {
                LaserMenuItem.Title($"Anchor {h.Index + 1} of {anchors.Count}{end}"),
                new LaserMenuItem("Inherit", () => run(LaserPathCommands.SetType(chart, h.Lane, h.Section, h.Index, null)),
                    anchor.Type == null, !mustStart && !last),
            };
            foreach (var (type, label) in curve_types)
                items.Add(new LaserMenuItem(label, () => run(LaserPathCommands.SetType(chart, h.Lane, h.Section, h.Index, type)),
                    anchor.Type == type, !last));
            // a slam's two ends are two points: the end under the cursor goes on its own, or the whole anchor
            if (anchor.IsSlam)
                items.Add(new LaserMenuItem(h.Vf ? "Delete slam end" : "Delete slam start",
                    () => run(LaserPathCommands.DeleteSlamEnd(chart, h.Lane, h.Section, h.Index, h.Vf))));
            items.Add(new LaserMenuItem(anchors.Count <= 2 ? "Delete laser" : anchor.IsSlam ? "Delete anchor (both ends)" : "Delete anchor",
                () => run(LaserPathCommands.Delete(chart, h.Lane, h.Section, h.Index))));
            return items;
        }

        if (LaserSegmentAt(p) is { } seg)
        {
            var anchors = LaserPaths.AnchorsOf(seg.Section);
            double ry = session.MsToPulse(p.Ms) - seg.Section.Y;
            int index = Math.Clamp(anchors.Anchors.FindLastIndex(x => x.Ry <= ry), 0, anchors.Count - 2);
            var segment = anchors.SegmentOf(index);
            var items = new List<LaserMenuItem> { LaserMenuItem.Title("Curve type") };
            foreach (var (type, label) in curve_types)
                items.Add(new LaserMenuItem(label, () => run(LaserPathCommands.SetSegmentType(chart, seg.Lane, seg.Section, index, type)),
                    segment is { } s && anchors.Anchors[s.Start].Type == type));
            items.Add(new LaserMenuItem("Delete laser", () => run(new RemoveLaserSectionCommand(seg.Lane, seg.Section))));
            return items;
        }

        return null;
    }

    private bool openLaserMenu(PlayfieldPointer p)
    {
        if (LaserMenuAt(p) is not { } items) return false;
        ContextMenuRequested?.Invoke(p, items);
        return true;
    }

    private void run(IEditCommand? cmd)
    {
        if (cmd == null)
        {
            Message?.Invoke("Not possible here");
            return;
        }

        session.Commands.Execute(cmd);
        bool replaced = false;
        foreach (var r in replacements(cmd))
        {
            activate(r.NewLane, r.New);
            replaced = true;
        }

        if (!replaced) selection.Clear();
        updateHover();
        StateChanged?.Invoke();
    }

    // ---- placement (laser tool, osu!lazer slider placement) ----------------

    private void setPlacement(LaserPlacement? placement)
    {
        Placement = placement;
        updateHover();
        StateChanged?.Invoke();
    }

    /// <summary>A new draft (a lone slam in it finishes as the slam alone, see <see cref="LaserPlacement"/>).</summary>
    private LaserPlacement newPlacement(int lane, bool wide, long y, LaserPath path) => new(lane, wide, y, path);

    /// <summary>The placement anchor (and slam end) under the pointer, or null.</summary>
    private AnchorHit? placementAnchorAt(PlayfieldPointer p)
    {
        if (Placement is not { } pl) return null;
        AnchorHit? best = null;
        double dist = laser_hit_px;
        considerAnchors(p, pl.Lane, pl.Preview, pl.Y, pl.Path, pl.Wide, null, ref best, ref dist);
        return best;
    }

    /// <summary>
    /// True when a click here would make a slam of the draft's last anchor (its beat, another snapped value). Such a
    /// click makes the slam even over that anchor's handle — a small slam lies inside the hit radius, and the first two
    /// clicks of a slam used to cycle the type instead. With Ctrl (free value) the handle keeps the click.
    /// </summary>
    private bool placementSlamAt(PlayfieldPointer p, bool ctrl)
    {
        if (Placement is not { } pl || ctrl) return false;
        return LaserPathEditing.IsSlamClick(pl.Path, PulseAt(p.Ms) - pl.Y, laserValue(p.X, pl.Wide, false));
    }

    /// <summary>Classic: a click here hits the draft's last point again (same beat and value, or the same beat after a slam) and finishes.</summary>
    private bool placementFinishAt(PlayfieldPointer p, bool ctrl) =>
        Placement is { } pl && ClassicLaser.IsFinishClick(pl.Path, PulseAt(p.Ms) - pl.Y, laserValue(p.X, pl.Wide, ctrl));

    private void placementClick(PlayfieldPointer p, bool ctrl)
    {
        var pl = Placement!;
        if (ClassicLasers)
        {
            // K-Shoot / kson editor: the last point clicked again finishes; anchors are never retyped by a click
            if (placementFinishAt(p, ctrl))
            {
                FinishPlacement();
                return;
            }
        }
        else if (!placementSlamAt(p, ctrl) && placementAnchorAt(p) is { } hit)
        {
            setPlacement(pl.With(pl.Y, LaserPathEditing.CycleType(pl.Path, hit.Index)));
            return;
        }

        if (withPointer(pl, p, ctrl) is not { } next)
        {
            Message?.Invoke("An anchor already sits on that beat");
            return;
        }

        if (!OverlapRule.CanPlace(chart, new[] { (pl.Lane, next.Preview) }, new HashSet<LaserSection>()))
        {
            Message?.Invoke(OverlapRule.LaserGapMessage);
            return;
        }

        setPlacement(next);
    }

    /// <summary>The draft plus an anchor at the pointer (snapped time, value from x); null when that pulse is taken.</summary>
    private LaserPlacement? withPointer(LaserPlacement pl, PlayfieldPointer p, bool ctrl)
    {
        long pulse = PulseAt(p.Ms);
        double v = laserValue(p.X, pl.Wide, ctrl);
        long y = pl.Y;
        var path = pl.Path.Clone();
        if (pulse < y)
        {
            foreach (var a in path.Anchors) a.Ry += y - pulse;
            y = pulse;
        }

        var added = ClassicLasers ? ClassicLaser.Add(path, pulse - y, v) : LaserPathEditing.AddAnchor(path, pulse - y, v);
        return added != null ? newPlacement(pl.Lane, pl.Wide, y, added) : null;
    }

    /// <summary>The draft with the pointer as its next anchor (live preview), or the draft itself.</summary>
    public LaserPlacement? PlacementPreview()
    {
        if (Placement is not { } pl) return null;
        if (Hover is not { } h || mode != DragMode.None) return pl;
        if (ClassicLasers ? placementFinishAt(h, false) : !placementSlamAt(h, false) && placementAnchorAt(h) != null) return pl;
        return withPointer(pl, h, false) ?? pl;
    }

    /// <summary>Right click / leaving the tool: puts the drafted section into the chart (needs two anchors).</summary>
    public bool FinishPlacement()
    {
        if (Placement is not { } pl) return false;
        Placement = null;
        bool added = false;
        // two anchors, or one slam (a lone-slam section, no tail)
        if (pl.Result is { } section)
        {
            if (OverlapRule.CanPlace(chart, new[] { (pl.Lane, section) }, new HashSet<LaserSection>()))
            {
                addSection(pl.Lane, section);
                added = true;
            }
            else Message?.Invoke(OverlapRule.LaserGapMessage);
        }

        updateHover();
        StateChanged?.Invoke();
        return added;
    }

    /// <summary>Esc: drops the draft.</summary>
    public bool CancelPlacement()
    {
        if (Placement == null) return false;
        setPlacement(null);
        return true;
    }

    /// <summary>See <see cref="LaserEditing.MovePoint"/>.</summary>
    public static LaserSection? EditPoint(LaserSection s, int index, long pulse, double value, bool vf) =>
        LaserEditing.MovePoint(s, index, pulse, value, vf);
}
