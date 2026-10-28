using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Containers;
using SDVXEditor.Game.Screens.Edit.Compose;
using L = SDVXEditor.Game.Screens.Edit.Compose.PlayfieldLayout;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>
/// Stacked automation lanes of the camera tab (osu!-style), time vertical exactly like the compose
/// playfield (same judgement line, px/ms and zoom), so a point sits beside the notes it belongs to.
/// One <see cref="QuadBatch"/> draws everything; labels are pooled texts.
/// </summary>
public partial class CameraLanes : CompositeDrawable
{
    public const float GAP = 6;
    public static readonly CamLane[] Lanes = Enum.GetValues<CamLane>();
    public static float TotalWidth => Lanes.Length * CameraLaneEditor.LANE_WIDTH + (Lanes.Length - 1) * GAP;

    private static readonly Colour4 graph_colour = Colour4.FromHex("7fe0ff");
    private static readonly Colour4 tilt_colour = Colour4.FromHex("ffc861");
    private static readonly Colour4 selected = EditorColours.Divisor.Yellow;

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly CameraLaneEditor editor;
    private readonly IBindable<float> zoom;

    private QuadBatch batch = null!;
    private Container textLayer = null!;
    private readonly List<SpriteText> texts = new List<SpriteText>();
    private int textCount;
    private bool dragging;

    public int LastQuadCount { get; private set; }

    public CameraLanes(EditorSession session, EditorClock clock, CameraLaneEditor editor, IBindable<float> zoom)
    {
        this.session = session;
        this.clock = clock;
        this.editor = editor;
        this.zoom = zoom.GetBoundCopy();
        RelativeSizeAxes = Axes.Y;
        Width = TotalWidth;
    }

    [Resolved(CanBeNull = true)]
    private PopupLayer? popupLayer { get; set; }

    /// <summary>Zero-size drawable at the right-click position; the anchor menu opens under it.</summary>
    private Container menuAnchor = null!;

    private LaneColumn[] columns = null!;

    /// <summary>
    /// The drawable covering one lane column, full height (the header with its name and badges lies over its top):
    /// a spotlight / tutorial anchor target. It also draws the lane highlight — a pink frame and a light tint while
    /// the lane is hovered, dragged in or holds the selection (<see cref="HighlightedLane"/>).
    /// </summary>
    public Drawable ColumnFor(CamLane lane) => columns[(int)lane];

    /// <summary>The lane drawn highlighted now: the one being dragged in, else the hovered one, else the selection's.</summary>
    public CamLane? HighlightedLane =>
        editor.DragLane ?? editor.Hover?.Lane
        ?? (editor.SelectedLane is { } sl ? (CamLane)(int)sl : editor.SelectedAutoTilt != null ? CamLane.Tilt : editor.SelectedPattern != null ? CamLane.Patterns : null);

    /// <summary>The open anchor / curve-type menu (null when closed).</summary>
    public FillFlowContainer? OpenMenu { get; private set; }

    [BackgroundDependencyLoader]
    private void load()
    {
        columns = Lanes.Select(l => new LaneColumn(l) { X = LaneLeft(l) }).ToArray();
        InternalChildren = new Drawable[]
        {
            batch = new QuadBatch(),
            new Container { RelativeSizeAxes = Axes.Both, Children = columns },
            textLayer = new Container { RelativeSizeAxes = Axes.Both },
            menuAnchor = new Container { Size = Vector2.Zero },
        };
        editor.ContextMenuRequested += showMenu;
    }

    protected override void Dispose(bool isDisposing)
    {
        editor.ContextMenuRequested -= showMenu;
        base.Dispose(isDisposing);
    }

    private void showMenu(CamPointer p, IReadOnlyList<LaserMenuItem> items)
    {
        if (popupLayer == null) return;
        menuAnchor.Position = new Vector2(LaneLeft(p.Lane) + (float)p.Fx * CameraLaneEditor.LANE_WIDTH, YOfMs(p.Ms));
        var menu = LaserContextMenuItem.CreateMenu(items, () => popupLayer.Close());
        OpenMenu = menu;
        popupLayer.Open(menuAnchor, menu, matchWidth: false, closed: () => OpenMenu = null);
    }

    // ---- coordinates -------------------------------------------------------

    private float pxPerMs => L.PX_PER_MS * zoom.Value;
    private float judgeY => DrawHeight - L.JUDGEMENT_FROM_BOTTOM;

    public static float LaneLeft(CamLane lane) => (int)lane * (CameraLaneEditor.LANE_WIDTH + GAP);

    public float YOfMs(double ms) => judgeY - (float)((ms - clock.CurrentTime.Value) * pxPerMs);
    public float YOfPulse(double pulse) => YOfMs(session.PulseToMs(pulse));

    private static float xOf(CamLane lane, double v) =>
        LaneLeft(lane) + (float)Math.Clamp(CameraLaneEditor.FxOf(lane, v), 0, 1) * CameraLaneEditor.LANE_WIDTH;

    /// <summary>Pointer for a local position (null between lanes).</summary>
    public CamPointer? PointerAt(Vector2 local)
    {
        int i = (int)Math.Floor(local.X / (CameraLaneEditor.LANE_WIDTH + GAP));
        if (i < 0 || i >= Lanes.Length) return null;
        double fx = (local.X - LaneLeft((CamLane)i)) / CameraLaneEditor.LANE_WIDTH;
        if (fx > 1.02) return null;
        return new CamPointer(clock.CurrentTime.Value + (judgeY - local.Y) / pxPerMs, (CamLane)i, Math.Clamp(fx, 0, 1));
    }

    // ---- input -------------------------------------------------------------

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left && e.Button != MouseButton.Right) return false;
        if (PointerAt(e.MousePosition) is not { } p) return false;
        editor.PxPerMs = pxPerMs;
        dragging = editor.PointerDown(p, e.Button == MouseButton.Right ? PointerButton.Right : PointerButton.Left, e.ShiftPressed, e.AltPressed, e.ControlPressed);
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e) => dragging;

    protected override void OnDrag(DragEvent e)
    {
        if (!dragging || PointerAt(new Vector2(Math.Clamp(e.MousePosition.X, 0, DrawWidth - 1), e.MousePosition.Y)) is not { } p) return;
        // keep the lane of the drag even when the cursor leaves it
        if (editor.DragLane is { } l) p = p with { Lane = l, Fx = (e.MousePosition.X - LaneLeft(l)) / CameraLaneEditor.LANE_WIDTH };
        editor.PointerDrag(p);
    }

    protected override void OnMouseUp(MouseUpEvent e)
    {
        if (!dragging) return;
        dragging = false;
        editor.PointerUp();
    }

    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        if (!dragging && PointerAt(e.MousePosition) is { } p) editor.PointerMove(p);
        return base.OnMouseMove(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        editor.PointerLeave();
        base.OnHoverLost(e);
    }

    // ---- drawing -----------------------------------------------------------

    protected override void Update()
    {
        base.Update();
        editor.PxPerMs = pxPerMs;
        batch.Clear();
        textCount = 0;

        double now = clock.CurrentTime.Value;
        double msTop = now + judgeY / pxPerMs;
        double msBottom = now - (DrawHeight - judgeY) / pxPerMs;
        long pLo = (long)Math.Floor(session.MsToPulse(msBottom)) - 1;
        long pHi = (long)Math.Ceiling(session.MsToPulse(msTop)) + 1;

        var eval = new CameraEvaluator(session.Chart);
        long nowPulse = (long)Math.Round(session.MsToPulse(now));
        var state = eval.Evaluate(Math.Max(0, nowPulse));

        foreach (var lane in Lanes)
        {
            float x = LaneLeft(lane);
            batch.AddRect(x, 0, CameraLaneEditor.LANE_WIDTH, DrawHeight, Colour4.Black.Opacity(0.72f));
            if (lane != CamLane.Patterns)
                batch.AddRect(xOf(lane, 0) - 0.5f, 0, 1, DrawHeight, Colour4.White.Opacity(0.18f));
        }

        drawGrid(pLo, pHi);

        for (int i = 0; i < 5; i++)
            drawGraph((CamGraph)i, pLo, pHi);
        drawTilt(pLo, pHi);
        foreach (var lane in CameraPaths.All)
            drawAnchors(lane);
        drawPatterns(pLo, pHi);

        // judgement line and current values
        batch.AddRect(0, judgeY - 2, TotalWidth, 4, EditorColours.Osu.JudgementGreen.Opacity(0.9f));
        foreach (var lane in Lanes)
        {
            string value = lane switch
            {
                CamLane.ZoomTop => fmt(state.ZoomTop),
                CamLane.ZoomBottom => fmt(state.ZoomBottom),
                CamLane.ZoomSide => fmt(state.ZoomSide),
                CamLane.CenterSplit => fmt(state.CenterSplit),
                CamLane.RotationDeg => fmt(state.RotationDeg) + "°",
                CamLane.Tilt => state.TiltManual ? fmt(state.TiltInput) : TiltValue.NameOf(state.TiltMode ?? TiltMode.Normal),
                _ => state.PatternRollDeg != 0 ? fmt(state.PatternRollDeg) + "°" : "-",
            };
            text(LaneLeft(lane) + CameraLaneEditor.LANE_WIDTH / 2, judgeY + 12, value, Colour4.White, 16, Anchor.TopCentre);
            text(LaneLeft(lane) + CameraLaneEditor.LANE_WIDTH / 2, judgeY + 34, CameraLaneEditor.LaneName(lane), EditorColours.TextMuted, 13, Anchor.TopCentre);
        }

        drawHover();

        var hot = HighlightedLane;
        foreach (var c in columns) c.Highlighted = c.Lane == hot;

        for (int i = textCount; i < texts.Count; i++) texts[i].Alpha = 0;
        LastQuadCount = batch.Count;
    }

    private static string fmt(double v) => v.ToString(Math.Abs(v) < 10 ? "0.##" : "0.#", CultureInfo.InvariantCulture);

    private void drawGrid(long pLo, long pHi)
    {
        var timing = session.Timing;
        long from = Math.Max(0, pLo);
        if (from > pHi) return;
        for (int m = timing.MeasureAt(from); ; m++)
        {
            long start = timing.MeasureStart(m);
            if (start > pHi) break;
            long beatLen = timing.BeatLength(m);
            long len = timing.MeasureLength(m);
            for (long p = start; p < start + len; p += beatLen)
            {
                if (p < from || p > pHi) continue;
                float y = YOfPulse(p);
                foreach (var lane in Lanes)
                    batch.AddRect(LaneLeft(lane), y - 0.5f, CameraLaneEditor.LANE_WIDTH, p == start ? 1.5f : 1, Colour4.White.Opacity(p == start ? 0.45f : 0.15f));
            }
        }
    }

    private void line(float x1, float y1, float x2, float y2, Colour4 c, float hw = 1.2f)
    {
        if (Math.Abs(y1 - y2) < 0.5f)
            batch.AddRect(Math.Min(x1, x2), y1 - hw, Math.Abs(x2 - x1) + 0.01f, 2 * hw, c);
        else
            batch.AddBand(x1, y1, x2, y2, hw, c);
    }

    private void drawGraph(CamGraph g, long pLo, long pHi)
    {
        var lane = (CamLane)(int)g;
        var pts = CameraGraphs.Get(session.Chart.Camera, g);
        if (pts.Count == 0) return;
        float yTop = 0, yBottom = DrawHeight;

        // before the first / after the last point the value is constant
        float y0 = YOfPulse(pts[0].Y);
        if (y0 < yBottom) line(xOf(lane, pts[0].V), Math.Min(y0, yBottom), xOf(lane, pts[0].V), yBottom, graph_colour.Opacity(0.5f));
        float yl = YOfPulse(pts[^1].Y);
        if (yl > yTop) line(xOf(lane, pts[^1].ValueAfter), yTop, xOf(lane, pts[^1].ValueAfter), Math.Min(yl, yBottom), graph_colour.Opacity(0.5f));

        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            float y = YOfPulse(p.Y);
            if (i + 1 < pts.Count)
            {
                var q = pts[i + 1];
                float yq = YOfPulse(q.Y);
                if (!(y < yTop && yq < yTop) && !(y > yBottom && yq > yBottom))
                {
                    int steps = p.Curve is { IsLinear: false } ? 16 : 1;
                    float px = xOf(lane, p.ValueAfter), py = y;
                    for (int k = 1; k <= steps; k++)
                    {
                        double pulse = p.Y + (q.Y - p.Y) * (double)k / steps;
                        float nx = xOf(lane, Graph.Interpolate(p, q, pulse)), ny = YOfPulse(pulse);
                        line(px, py, nx, ny, p.Curve is { IsLinear: false } ? graph_colour.Lighten(0.3f) : graph_colour);
                        px = nx;
                        py = ny;
                    }
                }
            }

            if (y < -20 || y > DrawHeight + 20) continue;
            if (p.Vf is { } vf) line(xOf(lane, p.V), y, xOf(lane, vf), y, Colour4.White, 2);
        }
    }

    /// <summary>
    /// The anchors of a graph / manual tilt lane (the editable handles; a curved run's chart points are only its bake).
    /// In the hovered lane, the selected anchor's lane and the placement's run they show their type colours (osu!lazer:
    /// straight white, perfect curve yellow, Bezier red, inherit small grey; a dot = auto type) and a curved run its
    /// control polygon; elsewhere they are plain handles in the lane colour.
    /// </summary>
    private void drawAnchors(CamPathLane pl)
    {
        var lane = (CamLane)(int)pl;
        var baseColour = pl == CamPathLane.Tilt ? tilt_colour : graph_colour;
        bool laneHot = editor.Hover?.Lane == lane || editor.SelectedLane == pl || editor.DragLane == lane;
        foreach (var run in CameraPaths.Runs(session.Chart.Camera, pl))
        {
            float yStart = YOfPulse(run.Y), yEnd = YOfPulse(run.EndY);
            if ((yStart < -20 && yEnd < -20) || (yStart > DrawHeight + 20 && yEnd > DrawHeight + 20)) continue;
            var path = editor.AnchorsOf(run);
            bool chain = editor.Chain is { } c && c.Lane == pl && c.RunY == run.Y;
            bool typed = laneHot || chain;
            var list = path.Anchors;

            if (typed && !path.IsTrivial)
            {
                for (int i = 0; i < list.Count - 1; i++)
                {
                    float y1 = YOfPulse(run.Y + list[i].Ry), y2 = YOfPulse(run.Y + list[i + 1].Ry);
                    if ((y1 < 0 && y2 < 0) || (y1 > DrawHeight && y2 > DrawHeight)) continue;
                    line(xOf(lane, list[i].ValueAfter), y1, xOf(lane, list[i + 1].V), y2, Colour4.White.Opacity(0.4f), 0.6f);
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                long ay = run.Y + a.Ry;
                float y = YOfPulse(ay);
                if (y < -20 || y > DrawHeight + 20) continue;
                bool sel = editor.SelectedLane == pl && editor.SelectedAnchorY == ay;
                bool hot = editor.HoveredAnchor is { } h && h.Lane == pl && h.Y == ay;
                var colour = typed ? ComposePlayfield.AnchorColour(a.Type) : baseColour;
                float size = (a.Type == null ? 7 : 9) + (hot ? 3 : 0);
                for (int k = 0; k < (a.IsSlam ? 2 : 1); k++)
                {
                    float x = xOf(lane, k == 0 ? a.V : a.Vf!.Value);
                    batch.AddRect(x - size / 2 - 1.5f, y - size / 2 - 1.5f, size + 3, size + 3, Colour4.Black.Opacity(0.85f));
                    batch.AddRect(x - size / 2, y - size / 2, size, size, k == 1 && !typed ? Colour4.White : colour);
                    if (typed && a.Auto && a.Type != null) batch.AddRect(x - 1.5f, y - 1.5f, 3, 3, Colour4.Black.Opacity(0.85f));
                    // selection is a pink frame, not a fill: the fill is the anchor type (perfect curve is yellow)
                    if (sel) batch.AddFrame(x - size / 2 - 4, y - size / 2 - 4, size + 8, size + 8, 2, EditorColours.Osu.Pink);
                    else if (hot) batch.AddFrame(x - size / 2 - 3, y - size / 2 - 3, size + 6, size + 6, 1.5f, Colour4.White);
                }
            }

            // placing: the next segment from the last click to the pointer, like osu!lazer's slider placement preview
            if (chain && !dragging && editor.Hover is { } hv && hv.Lane == lane && editor.Chain is { } ch
                && CameraPathEditing.IndexAt(path, ch.TailY - run.Y) is int ti && ti >= 0)
            {
                long hp = editor.PulseAt(hv.Ms);
                if (hp > ch.TailY && (ti == list.Count - 1 || run.Y + list[ti + 1].Ry > hp))
                {
                    var tail = list[ti];
                    line(xOf(lane, tail.ValueAfter), YOfPulse(ch.TailY), xOf(lane, CameraLaneEditor.ValueOf(lane, hv.Fx)), YOfPulse(hp), Colour4.White.Opacity(0.35f), 0.8f);
                }
            }
        }
    }

    private static Colour4 modeColour(TiltMode m) => m switch
    {
        TiltMode.Zero => Colour4.FromHex("666666"),
        TiltMode.Bigger or TiltMode.KeepBigger => Colour4.FromHex("e0803a"),
        TiltMode.Biggest or TiltMode.KeepBiggest => Colour4.FromHex("e04a4a"),
        _ => Colour4.FromHex("4a9ee0"),
    };

    private void drawTilt(long pLo, long pHi)
    {
        const CamLane lane = CamLane.Tilt;
        var list = session.Chart.Camera.Tilt;
        float x0 = LaneLeft(lane), w = CameraLaneEditor.LANE_WIDTH;

        // auto segments as a strip; the default before the first point is "normal"
        float firstY = list.Count > 0 ? YOfPulse(list[0].Y) : -1;
        if (firstY < 0 || firstY > 0)
            batch.AddRect(x0, Math.Max(0, firstY), 10, DrawHeight - Math.Max(0, firstY), modeColour(TiltMode.Normal).Opacity(0.5f));

        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            float y = YOfPulse(t.Y);
            float yNext = i + 1 < list.Count ? YOfPulse(list[i + 1].Y) : -10;
            var after = t.Final ?? t.Value;

            if (after.Auto is { } m)
            {
                float top = Math.Max(0, yNext), bottom = Math.Min(DrawHeight, y);
                if (bottom > top)
                {
                    batch.AddRect(x0, top, w, bottom - top, modeColour(m).Opacity(0.16f));
                    batch.AddRect(x0, top, 10, bottom - top, modeColour(m).Opacity(0.8f));
                }

                if (y > -20 && y < DrawHeight + 20)
                {
                    bool keep = m is TiltMode.KeepNormal or TiltMode.KeepBigger or TiltMode.KeepBiggest;
                    batch.AddRect(x0, y - 1.5f, w, 3, editor.SelectedAutoTilt == t ? selected : modeColour(m));
                    text(x0 + 14, y - 3, TiltValue.NameOf(m).Replace("keep_", "") + (keep ? " (keep)" : ""), Colour4.White, 13, Anchor.BottomLeft);
                }

                continue;
            }

            // manual: hold or interpolate towards the next manual point
            float x = xOf(lane, after.Manual);
            if (i + 1 < list.Count && !list[i + 1].Value.IsAuto)
            {
                var q = list[i + 1];
                int steps = t.Curve is { IsLinear: false } ? 16 : 1;
                float px = x, py = y;
                for (int k = 1; k <= steps; k++)
                {
                    double f = (double)k / steps;
                    double v = after.Manual + (q.Value.Manual - after.Manual) * (t.Curve?.Apply(f) ?? f);
                    float nx = xOf(lane, v), ny = YOfPulse(t.Y + (q.Y - t.Y) * f);
                    line(px, py, nx, ny, tilt_colour);
                    px = nx;
                    py = ny;
                }
            }
            else
                line(x, y, x, Math.Max(0, yNext), tilt_colour.Opacity(0.6f));

            if (y < -20 || y > DrawHeight + 20) continue;
            if (!t.Value.IsAuto && t.Final is { IsAuto: false } f2) line(xOf(lane, t.Value.Manual), y, xOf(lane, f2.Manual), y, Colour4.White, 2);
        }
    }

    private void drawPatterns(long pLo, long pHi)
    {
        const CamLane lane = CamLane.Patterns;
        float x0 = LaneLeft(lane), w = CameraLaneEditor.LANE_WIDTH;
        foreach (var (kind, p) in CameraEvaluator.Patterns(session.Chart.Camera))
        {
            if (p.Y + p.Length < pLo || p.Y > pHi) continue;
            float y = YOfPulse(p.Y), yEnd = YOfPulse(p.Y + p.Length);
            var c = kind switch
            {
                CamPatternKind.Spin => Colour4.FromHex("b561ff"),
                CamPatternKind.HalfSpin => Colour4.FromHex("ff61c8"),
                _ => Colour4.FromHex("61ffb5"),
            };
            float col = CameraLaneEditor.PatternColumnLeft(kind), bw = CameraLaneEditor.PATTERN_BAR_WIDTH;
            bool sel = editor.SelectedPattern is { } sp && sp.Pattern == p;
            // a bar like a hold: body from the start to the end, the end a firmer cap (drag it to change the length)
            batch.AddRect(x0 + col, yEnd, bw, y - yEnd, c.Opacity(sel ? 0.7f : 0.45f));
            batch.AddRect(x0 + col, yEnd - 2, bw, 4, c.Lighten(0.3f));
            batch.AddRect(x0, y - 2, w, 4, sel ? selected : c);
            if (sel) batch.AddFrame(x0 + col - 3, yEnd - 4, bw + 6, y - yEnd + 6, 2, selected);
            if (!CameraCommands.HasMatchingSlam(session.Chart, p))
                batch.AddFrame(x0 + col - 2, yEnd - 2, bw + 4, y - yEnd + 4, 2, EditorColours.Nabla.Danger);
            if (editor.PatternTailHint is { } hint && hint.Pattern == p)
            {
                // the hold-tail resize hint of the compose tab: a bar across the end and a vertical double arrow
                float cx = x0 + col + bw / 2;
                batch.AddRect(x0 + col - 3, yEnd - 2, bw + 6, 4, Colour4.White);
                batch.AddRect(cx - 1, yEnd - 10, 2, 20, Colour4.White);
                batch.AddRect(cx - 5, yEnd - 10, 10, 2, Colour4.White);
                batch.AddRect(cx - 5, yEnd + 8, 10, 2, Colour4.White);
            }

            string name = kind switch { CamPatternKind.Spin => "spin", CamPatternKind.HalfSpin => "half", _ => "swing" };
            text(x0 + 4, y - 4, $"{name} {(p.Direction < 0 ? "L" : "R")}", Colour4.White, 13, Anchor.BottomLeft);
        }
    }

    private void drawHover()
    {
        if (dragging || editor.Hover is not { } h || editor.PatternTailHint != null) return;
        float x = LaneLeft(h.Lane);
        float y = YOfPulse(editor.PulseAt(h.Ms));
        batch.AddRect(x, y - 0.5f, CameraLaneEditor.LANE_WIDTH, 1, Colour4.White.Opacity(0.6f));
        if (h.Lane != CamLane.Patterns)
        {
            double v = CameraLaneEditor.ValueOf(h.Lane, h.Fx);
            float hx = xOf(h.Lane, v);
            batch.AddRect(hx - 3, y - 3, 6, 6, Colour4.White.Opacity(0.6f));
            text(hx + 6, y - 2, fmt(v), Colour4.White.Opacity(0.8f), 13, Anchor.BottomLeft);
        }
    }

    /// <summary>One lane column (see <see cref="ColumnFor"/>): no input of its own, only the highlight.</summary>
    private partial class LaneColumn : Container
    {
        public CamLane Lane { get; }
        private readonly Box tint;
        private bool highlighted;

        public LaneColumn(CamLane lane)
        {
            Lane = lane;
            Width = CameraLaneEditor.LANE_WIDTH;
            RelativeSizeAxes = Axes.Y;
            Masking = true;
            BorderColour = EditorColours.Osu.Pink;
            BorderThickness = 0;
            Child = tint = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.Pink, Alpha = 0, AlwaysPresent = true };
        }

        public bool Highlighted
        {
            get => highlighted;
            set
            {
                if (highlighted == value) return;
                highlighted = value;
                tint.FadeTo(value ? 0.11f : 0, 120);
                BorderThickness = value ? 3 : 0;
            }
        }
    }

    private void text(float x, float y, string s, Colour4 colour, float size, Anchor origin)
    {
        if (textCount == texts.Count)
        {
            var t = new SpriteText { Shadow = true };
            texts.Add(t);
            textLayer.Add(t);
        }

        var st = texts[textCount++];
        st.Alpha = 1;
        if (st.Text.ToString() != s) st.Text = s;
        st.Font = EditorFont.Regular(size);
        st.Colour = colour;
        st.Origin = origin;
        st.Position = new Vector2(x, y);
    }
}
