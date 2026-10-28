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
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Containers;
using SDVXEditor.Game.Screens.Edit.Components;
using L = SDVXEditor.Game.Screens.Edit.Compose.PlayfieldLayout;

namespace SDVXEditor.Game.Screens.Edit.Compose;

/// <summary>
/// The vertical compose playfield (osu!mania style): time scrolls upward towards the green
/// judgement line near the bottom, spacing is linear in audio time and scaled by the playfield
/// zoom. Lanes LASER-L · BT-A..D · LASER-R, FX under BT-A+B / BT-C+D (drawn beneath BT as in game),
/// lasers over the lanes. Only what is on screen is drawn, as quads of one <see cref="QuadBatch"/>;
/// markers (BPM, scroll speed, time signature, stops) are pooled texts in the side margins.
/// </summary>
public partial class ComposePlayfield : CompositeDrawable
{
    public static readonly Colour4 SelectedColour = Colour4.FromHex("4ea8ff");

    private static readonly Colour4 laser_left = EditorColours.Lanes.LaserLeft;
    private static readonly Colour4 laser_right = EditorColours.Lanes.LaserRight;

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly ComposeEditor editor;
    private readonly EditorToolState tools;
    private readonly IBindable<float> zoom;

    private QuadBatch batch = null!;
    private Container markerLayer = null!;
    private readonly List<SpriteText> markers = new List<SpriteText>();
    private int markerCount;

    private bool pointerActive;

    /// <summary>Zero-size drawable at the right-click position; the laser context menu opens under it.</summary>
    private Container menuAnchor = null!;

    [Resolved(CanBeNull = true)]
    private PopupLayer? popupLayer { get; set; }

    [Resolved(CanBeNull = true)]
    private SDVXEditor.Game.Configuration.EditorConfigManager? config { get; set; }

    /// <summary>editor.ini "show laser anchors on hover", held here: a bound copy is only weakly referenced.</summary>
    private Bindable<bool>? hoverAnchorsSetting;

    /// <summary>The open laser context menu (null when closed).</summary>
    public FillFlowContainer? OpenLaserMenu { get; private set; }

    /// <summary>Quads drawn last frame (perf readout).</summary>
    public int LastQuadCount { get; private set; }

    /// <summary>Time the last <see cref="Update"/> took to build the frame, in ms (perf readout).</summary>
    public double LastUpdateMs { get; private set; }

    private readonly System.Diagnostics.Stopwatch updateWatch = new System.Diagnostics.Stopwatch();

    /// <summary>
    /// A context view (camera / effects tab): ignores the mouse and draws no side markers. The same
    /// chart, clock and zoom as the compose playfield, so the notes line up with the other lanes.
    /// </summary>
    public bool ReadOnly { get; init; }

    private double flashStart = double.NegativeInfinity;

    /// <summary>
    /// Makes the selected notes pulse for a moment (a glowing frame that fades out over <see cref="FLASH_MS"/>): the
    /// feedback of a selection made from a side panel ("select FX in view").
    /// </summary>
    public void FlashSelection() => flashStart = Time.Current;

    public const double FLASH_MS = 900;

    /// <summary>True while a <see cref="FlashSelection"/> is visible (smoke test).</summary>
    public bool Flashing => Time.Current - flashStart < FLASH_MS;

    /// <summary>Layers panel: hidden layers are not drawn (null = everything).</summary>
    public Panels.LayerVisibility? Layers { get; set; }

    private bool layer(Panels.EditorLayer l) => Layers?.IsVisible(l) != false;

    /// <summary>Visible pulse range of the last frame.</summary>
    public (long From, long To) VisiblePulses { get; private set; }

    public ComposePlayfield(EditorSession session, EditorClock clock, ComposeEditor editor, EditorToolState tools, IBindable<float> zoom)
    {
        this.session = session;
        this.clock = clock;
        this.editor = editor;
        this.tools = tools;
        this.zoom = zoom.GetBoundCopy();
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            batch = new QuadBatch(),
            // judgement line with the grey band under it (reference 02)
            new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.CentreLeft,
                X = L.SIDE,
                Y = -L.JUDGEMENT_FROM_BOTTOM,
                Width = L.LANES,
                Height = 6,
                Colour = EditorColours.Osu.JudgementGreen,
            },
            new Box
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.TopLeft,
                X = L.SIDE,
                Y = -L.JUDGEMENT_FROM_BOTTOM + 3,
                Width = L.LANES,
                Height = 5,
                Colour = Colour4.FromHex("bbbbbb"),
            },
            markerLayer = new Container { RelativeSizeAxes = Axes.Both },
            menuAnchor = new Container { Size = Vector2.Zero },
        };

        if (!ReadOnly) editor.ContextMenuRequested += showLaserMenu;

        if (!ReadOnly && config != null)
        {
            hoverAnchorsSetting = config.GetBindable<bool>(SDVXEditor.Game.Configuration.EditorSetting.ShowLaserAnchorsOnHover);
            hoverAnchorsSetting.BindValueChanged(e => editor.HoverAnchors.Value = e.NewValue, true);
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        editor.ContextMenuRequested -= showLaserMenu;
        base.Dispose(isDisposing);
    }

    // ---- laser context menu (osu!lazer: right click on a path control point) --------------

    private void showLaserMenu(PlayfieldPointer p, IReadOnlyList<LaserMenuItem> items)
    {
        if (popupLayer == null) return;

        menuAnchor.Position = new Vector2(p.X, YOfMs(p.Ms));
        var menu = LaserContextMenuItem.CreateMenu(items, () => popupLayer.Close());

        OpenLaserMenu = menu;
        popupLayer.Open(menuAnchor, menu, matchWidth: false, closed: () => OpenLaserMenu = null);
    }


    // ---- coordinates -------------------------------------------------------

    private float pxPerMs => L.PX_PER_MS * zoom.Value;
    private float judgeY => DrawHeight - L.JUDGEMENT_FROM_BOTTOM;

    /// <summary>Local y of an audio time.</summary>
    public float YOfMs(double ms) => judgeY - (float)((ms - clock.CurrentTime.Value) * pxPerMs);

    public float YOfPulse(double pulse) => YOfMs(session.PulseToMs(pulse));

    /// <summary>Pointer for a local position (also used by the smoke test).</summary>
    public PlayfieldPointer PointerAt(Vector2 local) => new PlayfieldPointer(clock.CurrentTime.Value + (judgeY - local.Y) / pxPerMs, local.X);

    /// <summary>Pointer for a pulse and an x (smoke test / tools).</summary>
    public PlayfieldPointer PointerAt(long pulse, float x) => new PlayfieldPointer(session.PulseToMs(pulse), x);

    // ---- input -------------------------------------------------------------

    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
    {
        if (ReadOnly) return false;
        if (base.ReceivePositionalInputAt(screenSpacePos)) return true;

        // Every tool also acts in the wide-laser overhang beside the playfield (the compose tab keeps it free, the
        // field container masks it; toolbars drawn above still get their clicks first). Until 0.1.x this was the laser
        // tool only, so in select mode a wide laser's anchors out there were drawn but neither hovered nor clicked:
        // the playfield was not in the positional input queue at all (smoke compose-input "wide anchor in the overhang").
        var local = ToLocalSpace(screenSpacePos);
        return local.Y >= 0 && local.Y <= DrawHeight && local.X >= -L.OVERHANG && local.X <= DrawWidth + L.OVERHANG;
    }

    // Mouse positions: always ToLocalSpace(e.ScreenSpaceMousePosition). e.MousePosition is in the PARENT's space, which
    // here is the overhang-padded field container (PlayfieldLayout.OVERHANG): using it put every placement 100 px right
    // of the cursor. The smoke part EditorGame.SmokeComposeInput.cs clicks through the real input path to catch this.
    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left && e.Button != MouseButton.Right) return false;

        editor.PxPerMs = pxPerMs;
        // the hold modifier as this press sees it (a key that went down in the same frame has not been polled yet)
        tools.SetModifierKeys(e.ControlPressed, e.AltPressed);
        pointerActive = editor.PointerDown(PointerAt(ToLocalSpace(e.ScreenSpaceMousePosition)), e.Button == MouseButton.Right ? PointerButton.Right : PointerButton.Left,
            e.ControlPressed, e.ShiftPressed);
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e) => pointerActive;

    protected override void OnDrag(DragEvent e)
    {
        if (!pointerActive) return;
        // the drag-snap key (Shift by default) snaps edits of existing laser anchors; Ctrl frees the value unless it is that key
        var snapKey = tools.DragSnapModifier.Value;
        editor.PointerDrag(PointerAt(ToLocalSpace(e.ScreenSpaceMousePosition)), e.ControlPressed && snapKey != Core.Input.DragSnapKey.Ctrl,
            snapKey.IsHeld(e.ShiftPressed, e.ControlPressed, e.AltPressed));
    }

    protected override void OnMouseUp(MouseUpEvent e)
    {
        if (!pointerActive) return;
        pointerActive = false;
        editor.PointerUp(PointerAt(ToLocalSpace(e.ScreenSpaceMousePosition)));
    }

    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        if (!pointerActive) editor.PointerMove(PointerAt(ToLocalSpace(e.ScreenSpaceMousePosition)));
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
        updateWatch.Restart();

        if (!ReadOnly) editor.PxPerMs = pxPerMs;
        batch.Clear();
        markerCount = 0;

        double now = clock.CurrentTime.Value;
        double msTop = now + judgeY / pxPerMs;
        double msBottom = now - (DrawHeight - judgeY) / pxPerMs;
        long pLo = (long)Math.Floor(session.MsToPulse(msBottom)) - 1;
        long pHi = (long)Math.Ceiling(session.MsToPulse(msTop)) + 1;
        VisiblePulses = (pLo, pHi);

        drawLanes();
        if (layer(Panels.EditorLayer.Grid)) drawGrid(pLo, pHi);
        drawSeparators();

        var sel = session.Selection;
        if (layer(Panels.EditorLayer.Fx))
            for (int lane = 0; lane < 2; lane++) drawNotes(NoteKind.Fx, lane, pLo, pHi, sel);
        if (layer(Panels.EditorLayer.Bt))
            for (int lane = 0; lane < 4; lane++) drawNotes(NoteKind.Bt, lane, pLo, pHi, sel);
        if (!ReadOnly && editor.TailHint is { } tail) drawTailHint(tail);

        // point handles only while the laser tool is active (selected points always show); select tool: the hovered and
        // the selected laser are outlined and show their anchors
        bool handles = !ReadOnly && tools.EffectiveTool.Value == EditorTool.Laser;
        bool select = !ReadOnly && tools.EffectiveTool.Value == EditorTool.Select;
        // hover preview (osu!stable): the hovered laser shows its anchors and their types in select and laser mode
        var hovered = select ? editor.HoveredLaser : null;
        var active = select ? editor.ActiveLaser : null;
        var preview = ReadOnly ? null : editor.AnchorPreview;
        for (int lane = 0; lane < 2; lane++)
        {
            if (!layer(lane == 0 ? Panels.EditorLayer.LaserLeft : Panels.EditorLayer.LaserRight)) continue;
            foreach (var s in visibleSections(lane, pLo, pHi))
            {
                bool isActive = active is { } a && a.Lane == lane && a.Section == s;
                bool isHovered = hovered is { } h && h.Lane == lane && h.Section == s;
                bool isPreview = preview is { } pv && pv.Lane == lane && pv.Section == s;
                drawSection(lane, s, 0.62f, handles, sel, outline: isActive || isHovered || isPreview, anchors: isActive || isPreview);
            }
        }

        if (!ReadOnly)
        {
            drawDrafts();
            if (layer(Panels.EditorLayer.TimingMarkers)) drawMarkers(pLo, pHi);
        }

        for (int i = markerCount; i < markers.Count; i++) markers[i].Alpha = 0;
        LastQuadCount = batch.Count;
        LastUpdateMs = updateWatch.Elapsed.TotalMilliseconds;
    }

    private void drawLanes()
    {
        batch.AddRect(L.SIDE, 0, L.LANES, DrawHeight, Colour4.Black);
        batch.AddRect(L.SIDE, 0, L.LASER_LANE, DrawHeight, laser_left.Opacity(0.10f));
        batch.AddRect(L.BT_RIGHT, 0, L.LASER_LANE, DrawHeight, laser_right.Opacity(0.10f));

        // laser tool: normal lasers act L.LASER_REACH beyond the lanes (darker), wide lasers L.WIDE_REACH at true scale (lighter)
        if (!ReadOnly && tools.EffectiveTool.Value == EditorTool.Laser)
        {
            batch.AddRect(L.LANES_LEFT - L.WIDE_REACH, 0, L.WIDE_REACH - L.LASER_REACH, DrawHeight, Colour4.Black.Opacity(0.2f));
            batch.AddRect(L.LANES_RIGHT + L.LASER_REACH, 0, L.WIDE_REACH - L.LASER_REACH, DrawHeight, Colour4.Black.Opacity(0.2f));
            batch.AddRect(L.LANES_LEFT - L.LASER_REACH, 0, L.LASER_REACH, DrawHeight, Colour4.Black.Opacity(0.35f));
            batch.AddRect(L.LANES_RIGHT, 0, L.LASER_REACH, DrawHeight, Colour4.Black.Opacity(0.35f));
        }
    }

    private void drawSeparators()
    {
        for (int i = 0; i <= 4; i++)
        {
            float x = L.BT_LEFT + i * L.BT_LANE;
            batch.AddRect(x - 1, 0, 2, DrawHeight, Colour4.White.Opacity(i is 0 or 4 ? 0.9f : i == 2 ? 0.75f : 0.5f));
        }

        batch.AddRect(L.SIDE, 0, 1, DrawHeight, Colour4.White.Opacity(0.25f));
        batch.AddRect(L.SIDE + L.LANES - 1, 0, 1, DrawHeight, Colour4.White.Opacity(0.25f));
    }

    private void drawGrid(long pLo, long pHi)
    {
        var timing = session.Timing;
        int divisor = Math.Max(1, editor.Divisor.Value);
        long from = Math.Max(0, pLo);
        if (from > pHi) return;

        for (int m = timing.MeasureAt(from); ; m++)
        {
            long start = timing.MeasureStart(m);
            if (start > pHi) break;

            long len = timing.MeasureLength(m);
            long beatLen = timing.BeatLength(m);
            int ticks = (int)Math.Round((double)len * divisor / beatLen);

            // zoomed out, the finest ticks go first (like the timeline): a tick stays while its own snap level (1/2,
            // 1/4, … 1/192) is still at least 4 px apart, so 1/64 … 1/192 degrade to the coarser lines they contain
            double beatPx = (session.PulseToMs(start + beatLen) - session.PulseToMs(start)) * pxPerMs;

            for (int k = 0; k < ticks; k++)
            {
                long pulse = start + (long)Math.Round((double)k * beatLen / divisor, MidpointRounding.AwayFromZero);
                if (pulse < from || pulse > pHi) continue;

                bool beat = k % divisor == 0;
                if (!beat && beatPx * gcd(k % divisor, divisor) / divisor < 4) continue;

                float y = YOfPulse(pulse);
                if (k == 0)
                    batch.AddRect(L.SIDE, y - 1, L.LANES, 2, Colour4.White.Opacity(0.9f));
                else if (beat)
                    batch.AddRect(L.SIDE, y - 0.75f, L.LANES, 1.5f, Colour4.White.Opacity(0.6f));
                else
                    batch.AddRect(L.SIDE, y - 0.75f, L.LANES, 1.5f, EditorColours.Divisor.ForTick(k, divisor).Opacity(0.85f));
            }
        }
    }

    private static int gcd(int a, int b) => b == 0 ? a : gcd(b, a % b);

    private void drawNotes(NoteKind kind, int lane, long pLo, long pHi, Selection sel)
    {
        var list = session.Chart.Notes.Lane(kind, lane);
        float left = L.LaneLeft(kind, lane), width = L.LaneWidth(kind);
        bool fx = kind == NoteKind.Fx;
        var colour = fx ? EditorColours.Lanes.Fx : EditorColours.Lanes.Bt;

        int start = firstIndexEndingAfter(list, kind, lane, pLo);
        for (int i = start; i < list.Count; i++)
        {
            var n = list[i];
            if (n.Y > pHi) break;
            if (n.EndY < pLo) continue;

            bool selected = sel.Contains(new NoteRef(kind, lane, n));
            drawNote(kind, left, width, n.Y, n.Length, selected ? SelectedColour : colour, selected, 1);
            if (selected && Flashing)
            {
                float a = (float)(1 - (Time.Current - flashStart) / FLASH_MS);
                float y0 = YOfPulse(n.Y), y1 = n.Length > 0 ? YOfPulse(n.EndY) : y0 - 10;
                batch.AddFrame(left - 4, y1 - 6, width + 8, y0 - y1 + 12, 3, Colour4.White.Opacity(a));
            }
        }
    }

    private void drawNote(NoteKind kind, float left, float width, long y, long length, Colour4 colour, bool selected, float alpha)
    {
        bool fx = kind == NoteKind.Fx;
        float yStart = YOfPulse(y);

        if (length > 0)
        {
            float yEnd = YOfPulse(y + length);
            float inset = fx ? 6 : 10;
            batch.AddRect(left + inset, yEnd, width - 2 * inset, yStart - yEnd, colour.Opacity((fx ? 0.55f : 0.8f) * alpha));
            if (selected) batch.AddFrame(left + inset, yEnd, width - 2 * inset, yStart - yEnd, 2, Colour4.White.Opacity(alpha));
        }

        // FX chips are taller and BT chips narrower, so an FX chip under two BT chips shows above,
        // below and at the outer edges.
        float h = fx ? L.FX_CHIP_HEIGHT : L.CHIP_HEIGHT;
        float chipInset = fx ? 2 : 6;
        batch.AddRect(left + chipInset, yStart - h / 2, width - 2 * chipInset, h, colour.Opacity(alpha));
        if (selected) batch.AddFrame(left + chipInset - 2, yStart - h / 2 - 2, width - 2 * chipInset + 4, h + 4, 2, Colour4.White.Opacity(alpha));
    }

    // longest hold per lane and longest laser section per side, so a binary search can start early enough
    private int cachedVersion = -1;
    private readonly long[] maxNoteLength = new long[6];
    private readonly long[] maxSectionLength = new long[2];

    private void refreshLengthCache()
    {
        if (cachedVersion == session.Version) return;
        cachedVersion = session.Version;

        var notes = session.Chart.Notes;
        for (int i = 0; i < 6; i++)
        {
            var list = i < 4 ? notes.Bt[i] : notes.Fx[i - 4];
            maxNoteLength[i] = list.Count == 0 ? 0 : list.Max(n => n.Length);
        }

        for (int i = 0; i < 2; i++)
            maxSectionLength[i] = notes.Lasers[i].Count == 0 ? 0 : notes.Lasers[i].Max(x => x.EndY - x.Y);
    }

    private static int lowerBound<T>(List<T> list, long pulse, Func<T, long> y)
    {
        int lo = 0, hi = list.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (y(list[mid]) < pulse) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    private int firstIndexEndingAfter(List<Note> list, NoteKind kind, int lane, long pulse)
    {
        refreshLengthCache();
        return lowerBound(list, pulse - maxNoteLength[kind == NoteKind.Bt ? lane : 4 + lane], n => n.Y);
    }

    private IEnumerable<LaserSection> visibleSections(int lane, long pLo, long pHi)
    {
        refreshLengthCache();
        var list = session.Chart.Notes.Lasers[lane];
        for (int i = lowerBound(list, pLo - maxSectionLength[lane], x => x.Y); i < list.Count && list[i].Y <= pHi; i++)
        {
            if (list[i].EndY >= pLo) yield return list[i];
        }
    }

    /// <summary>
    /// Laser entry / exit like USC (unnamed-sdvx-clone <c>Track.cpp</c> + <c>LaserTrackBuilder::GenerateTrackEntry/Exit</c>,
    /// read 2026-10-27): every section start gets an entry piece below it at the start value, and a section that ends on
    /// a slam an exit piece after it at the landing value, so the player sees where a laser comes in (a slam start, a lone
    /// slam). USC sizes them from its default skin textures, 2 laser widths wide: entry 50×256 px → 2.56 band widths
    /// long, exit 50×200 px → 2 band widths; constant on screen (not time). Drawn fading away from the laser; pure
    /// rendering — nothing is hit-tested or stored.
    /// </summary>
    public const float ENTRY_LENGTH = 2.56f, EXIT_LENGTH = 2f;

    private void drawEntryExit(LaserSection s, bool wide, float hw, Colour4 c)
    {
        if (s.Points.Count == 0) return;
        var faded = c.Opacity(0);

        var first = s.Points[0];
        float y0 = YOfPulse(s.Y);
        float len = ENTRY_LENGTH * 2 * hw;
        if (y0 > -len && y0 < DrawHeight + len)
            batch.AddRect(L.LaserX(first.V, wide) - hw, y0, 2 * hw, len, osu.Framework.Graphics.Colour.ColourInfo.GradientVertical(c, faded));

        var last = s.Points[^1];
        if (!last.IsSlam) return;
        float y1 = YOfPulse(s.Y + last.Ry);
        float exit = EXIT_LENGTH * 2 * hw;
        if (y1 > -exit && y1 < DrawHeight + exit)
            batch.AddRect(L.LaserX(last.Vf!.Value, wide) - hw, y1 - exit, 2 * hw, exit, osu.Framework.Graphics.Colour.ColourInfo.GradientVertical(faded, c));
    }

    /// <summary>The bands of a section (curved point segments in 12 steps), drawn with one half width and colour.</summary>
    private void drawBands(LaserSection s, bool wide, float hw, osu.Framework.Graphics.Colour.ColourInfo colour)
    {
        var pts = s.Points;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            var p = pts[i];
            var q = pts[i + 1];
            float y = YOfPulse(s.Y + p.Ry);
            float yq = YOfPulse(s.Y + q.Ry);
            if ((y < 0 && yq < 0) || (y > DrawHeight && yq > DrawHeight)) continue;

            if (p.Curve is { IsLinear: false })
            {
                const int steps = 12;
                float px = L.LaserX(p.ValueAfter, wide), py = y;
                for (int k = 1; k <= steps; k++)
                {
                    double f = (double)k / steps;
                    float nx = L.LaserX(LaserMath.Interpolate(p, q, f), wide);
                    float ny = YOfPulse(s.Y + p.Ry + (q.Ry - p.Ry) * f);
                    batch.AddBand(px, py, nx, ny, hw, colour);
                    px = nx;
                    py = ny;
                }
            }
            else
                batch.AddBand(L.LaserX(p.ValueAfter, wide), y, L.LaserX(q.V, wide), yq, hw, colour);
        }
    }

    /// <summary>Anchor colours in the osu!lazer spirit: straight white, perfect curve yellow, Bezier red, inherit = small grey.</summary>
    public static Colour4 AnchorColour(LaserCurveType? type) => type switch
    {
        LaserCurveType.Linear => Colour4.White,
        LaserCurveType.PerfectCurve => EditorColours.Divisor.Yellow,
        LaserCurveType.Bezier => EditorColours.Divisor.Red,
        _ => Colour4.FromHex("b0b0b0"),
    };

    /// <summary>
    /// Anchors of a path starting at <paramref name="y"/>: control polygon, then one handle per anchor. A slam has two
    /// handles, its start end and its landing end, hovered and selected one by one (<see cref="LaserSlamEndRef"/>); the
    /// landing end carries a small dot in the laser colour, so the pair reads as two points.
    /// </summary>
    private void drawAnchors(int lane, LaserSection s, long y, LaserPath anchors, bool wide, float alpha, Selection? sel = null)
    {
        var hovered = editor.HoveredAnchor;
        var list = anchors.Anchors;
        for (int i = 0; i < list.Count - 1; i++)
        {
            float y1 = YOfPulse(y + list[i].Ry), y2 = YOfPulse(y + list[i + 1].Ry);
            if ((y1 < 0 && y2 < 0) || (y1 > DrawHeight && y2 > DrawHeight)) continue;
            batch.AddBand(L.LaserX(list[i].ValueAfter, wide), y1, L.LaserX(list[i + 1].V, wide), y2, 0.8f, Colour4.White.Opacity(0.45f * alpha));
        }

        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            float ay = YOfPulse(y + a.Ry);
            if (ay < -20 || ay > DrawHeight + 20) continue;

            var c = AnchorColour(a.Type).Opacity(alpha);
            var slamPoint = a.IsSlam && sel != null ? s.Points.FirstOrDefault(pt => s.Y + pt.Ry == y + a.Ry && pt.IsSlam) : null;
            for (int k = 0; k < (a.IsSlam ? 2 : 1); k++)
            {
                bool hot = hovered is { } h && h.Section == s && h.Index == i && (!a.IsSlam || h.Vf == (k == 1));
                bool picked = slamPoint != null && sel!.Contains(new LaserSlamEndRef(lane, s, slamPoint, k == 1));
                float size = (a.Type == null ? 8 : 12) + (hot ? 4 : 0);
                float ax = L.LaserX(k == 0 ? a.V : a.Vf!.Value, wide);
                batch.AddRect(ax - size / 2 - 1.5f, ay - size / 2 - 1.5f, size + 3, size + 3, Colour4.Black.Opacity(0.8f * alpha));
                batch.AddRect(ax - size / 2, ay - size / 2, size, size, picked ? SelectedColour.Opacity(alpha) : c);
                if (a.Auto && a.Type != null) batch.AddRect(ax - 2, ay - 2, 4, 4, Colour4.Black.Opacity(0.8f * alpha));
                if (k == 1) batch.AddRect(ax + size / 2 - 4, ay - size / 2, 4, 4, (lane == 0 ? laser_left : laser_right).Opacity(alpha));
                if (picked) batch.AddFrame(ax - size / 2 - 4, ay - size / 2 - 4, size + 8, size + 8, 2, SelectedColour.Opacity(alpha));
                if (hot) batch.AddFrame(ax - size / 2 - 3, ay - size / 2 - 3, size + 6, size + 6, 1.5f, Colour4.White.Opacity(alpha));
            }
        }
    }

    private void drawSection(int lane, LaserSection s, float alpha, bool handles, Selection? sel, bool outline = false, bool anchors = false,
                             LaserPath? path = null)
    {
        var colour = lane == 0 ? laser_left : laser_right;
        bool wide = s.Width == 2;
        var pts = s.Points;
        float hw = L.LASER_HALF_WIDTH;
        var curved = path ?? (LaserPaths.ValidPath(s) is { IsTrivial: false } vp ? vp : null);
        bool showAnchors = anchors || (handles && curved != null);

        if (outline) drawBands(s, wide, hw + 3, Colour4.White.Opacity(0.6f));
        drawEntryExit(s, wide, hw, colour.Opacity(alpha));
        drawBands(s, wide, hw, colour.Opacity(alpha));

        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            float y = YOfPulse(s.Y + p.Ry);

            if (y < -20 || y > DrawHeight + 20) continue;

            if (p.IsSlam)
            {
                float x1 = L.LaserX(p.V, wide), x2 = L.LaserX(p.Vf!.Value, wide);
                float left = Math.Min(x1, x2) - hw, right = Math.Max(x1, x2) + hw;
                batch.AddRect(left, y - 7, right - left, 14, colour.Lighten(0.25f).Opacity(Math.Min(1, alpha + 0.25f)));
                addSlamArrows(x1, x2, y, Colour4.Black.Opacity(0.7f * Math.Min(1, alpha + 0.25f)));
            }

            var pref = new LaserPointRef(lane, s, p);
            if (!showAnchors && curved == null && (handles || (sel?.Touches(pref) ?? false)))
            {
                // one handle per point, two for a slam (start end, landing end), each selected on its own
                var hovered = editor.HoveredAnchor;
                for (int k = 0; k < (p.IsSlam ? 2 : 1); k++)
                {
                    bool selected = sel?.IsEndSelected(pref, k == 1) ?? false;
                    bool hot = hovered is { } h && h.Section == s && h.Index == i && (!p.IsSlam || h.Vf == (k == 1));
                    var hc = selected ? EditorColours.Divisor.Yellow : Colour4.White.Opacity(0.85f);
                    float size = (selected ? 10 : 7) + (hot ? 3 : 0);
                    float x = L.LaserX(k == 0 ? p.V : p.Vf!.Value, wide);
                    batch.AddRect(x - size / 2, y - size / 2, size, size, hc);
                    if (hot) batch.AddFrame(x - size / 2 - 2, y - size / 2 - 2, size + 4, size + 4, 1.5f, Colour4.White);
                }
            }
        }

        // a section that is one point only (being drawn)
        if (pts.Count == 1)
        {
            float y = YOfPulse(s.Y);
            float x = L.LaserX(pts[0].V, wide);
            batch.AddRect(x - hw, y - 4, 2 * hw, 8, colour.Opacity(alpha));
        }

        if (showAnchors)
        {
            bool selected = curved != null && pts.Count > 0 && (sel?.Contains(new LaserPointRef(lane, s, pts[0])) ?? false);
            drawAnchors(lane, s, s.Y, curved ?? LaserPaths.AnchorsOf(s), wide, selected || anchors ? 1 : 0.85f, sel);
        }
        else if (curved != null && pts.Count > 0 && (sel?.Contains(new LaserPointRef(lane, s, pts[0])) ?? false))
            drawAnchors(lane, s, s.Y, curved, wide, 1, sel);
    }

    /// <summary>
    /// "›" chevrons inside a slam bar pointing from the start value to the end value, one every ~22 px,
    /// so the direction of a slam is readable at a glance.
    /// </summary>
    private void addSlamArrows(float from, float to, float y, Colour4 colour)
    {
        float dir = to >= from ? 1 : -1;
        float length = Math.Abs(to - from);
        int count = Math.Max(1, (int)(length / 22));
        float step = length / count;

        for (int i = 0; i < count; i++)
        {
            float cx = from + dir * (step * (i + 0.5f));
            float tip = cx + dir * 3, back = cx - dir * 3;
            batch.AddBand(back, y - 5, tip, y, 1.4f, colour);
            batch.AddBand(tip, y, back, y + 5, 1.4f, colour);
        }
    }

    private void drawDrafts()
    {
        if (editor.NoteDraft is { } d)
            drawNote(d.Kind, L.LaneLeft(d.Kind, d.Lane), L.LaneWidth(d.Kind), d.Y, d.Length, d.Kind == NoteKind.Fx ? EditorColours.Lanes.Fx : EditorColours.Lanes.Bt, false, 0.55f);

        if (editor.LaserDraft is { } ld)
            drawSection(ld.Lane, ld.Section, 0.45f, true, null);

        // osu!lazer-style placement: the draft plus the pointer as the next anchor
        if (editor.PlacementPreview() is { } pv)
        {
            drawSection(pv.Lane, pv.Preview, 0.5f, false, null, outline: true);
            drawAnchors(pv.Lane, pv.Preview, pv.Preview.Y, pv.Path, pv.Wide, 1);
        }

        if (editor.SelectionBox is { } box)
        {
            float y1 = YOfMs(box.Ms1), y2 = YOfMs(box.Ms2);
            float x = Math.Min(box.X1, box.X2), y = Math.Min(y1, y2);
            float w = Math.Abs(box.X2 - box.X1), h = Math.Abs(y2 - y1);
            batch.AddRect(x, y, w, h, SelectedColour.Opacity(0.15f));
            batch.AddFrame(x, y, w, h, 1, SelectedColour.Opacity(0.9f));
        }

        // hover ghost
        if (editor.Hover is not { } hover || editor.NoteDraft != null || editor.LaserDraft != null || editor.SelectionBox != null || editor.Placement != null || editor.TailHint != null) return;

        long pulse = editor.PulseAt(hover.Ms);
        switch (tools.EffectiveTool.Value)
        {
            // hold tools (chosen, or a chip tool with the hold modifier) show a one-tick hold so the swap is visible
            case EditorTool.BtChip:
            case EditorTool.BtHold:
                if (L.BtLaneAt(hover.X) is int bt)
                    drawNote(NoteKind.Bt, L.LaneLeft(NoteKind.Bt, bt), L.BT_LANE, pulse, ghostLength(pulse), EditorColours.Lanes.Bt, false, 0.35f);
                break;

            case EditorTool.FxChip:
            case EditorTool.FxHold:
                if (L.FxLaneAt(hover.X) is int fx)
                    drawNote(NoteKind.Fx, L.LaneLeft(NoteKind.Fx, fx), 2 * L.BT_LANE, pulse, ghostLength(pulse), EditorColours.Lanes.Fx, false, 0.4f);
                break;

            case EditorTool.Laser:
                bool wide = editor.WideLaser.Value;
                float x = L.LaserX(editor.LaserValueAt(hover.X, wide), wide);
                float yy = YOfPulse(pulse);
                var c = editor.LaserSide == 0 ? laser_left : laser_right;
                batch.AddRect(x - L.LASER_HALF_WIDTH, yy - 1, 2 * L.LASER_HALF_WIDTH, 2, c.Opacity(0.8f));
                batch.AddRect(x - 1, yy - 10, 2, 20, c.Opacity(0.8f));
                break;
        }
    }

    /// <summary>
    /// Resize hint on a hold's tail (hovered or being dragged): a bright bar across the end plus a short vertical
    /// stroke through it (↕) - the framework has no per-area OS cursor, so the hint is drawn.
    /// </summary>
    private void drawTailHint(NoteRef tail)
    {
        bool fx = tail.Kind == NoteKind.Fx;
        float left = L.LaneLeft(tail.Kind, tail.Lane), width = L.LaneWidth(tail.Kind);
        float inset = fx ? 6 : 10;
        float y = YOfPulse(tail.Note.EndY);
        float cx = left + width / 2;
        batch.AddRect(left + inset - 3, y - 2, width - 2 * inset + 6, 4, Colour4.White);
        batch.AddRect(cx - 1, y - 10, 2, 20, Colour4.White);
        batch.AddRect(cx - 5, y - 10, 10, 2, Colour4.White);
        batch.AddRect(cx - 5, y + 8, 10, 2, Colour4.White);
    }

    private long ghostLength(long pulse) =>
        tools.EffectiveTool.Value is EditorTool.BtHold or EditorTool.FxHold ? editor.TickAfter(pulse) : 0;

    // ---- side markers ------------------------------------------------------

    private void drawMarkers(long pLo, long pHi)
    {
        var beat = session.Chart.Beat;
        var timing = session.Timing;

        foreach (var b in beat.Bpm)
        {
            if (b.Y < pLo || b.Y > pHi) continue;
            marker(b.Y, b.Bpm.ToString("0.##", CultureInfo.InvariantCulture), EditorColours.Divisor.Red, left: true, row: 0);
        }

        foreach (var ts in beat.TimeSignatures)
        {
            long y = timing.MeasureStart(ts.MeasureIndex);
            if (y < pLo || y > pHi) continue;
            marker(y, $"{ts.Numerator}/{ts.Denominator}", Colour4.White, left: true, row: 1);
        }

        foreach (var p in beat.ScrollSpeed)
        {
            if (p.Y < pLo || p.Y > pHi) continue;
            string text = "x" + p.V.ToString("0.##", CultureInfo.InvariantCulture);
            if (p.Vf is double vf) text += ">" + vf.ToString("0.##", CultureInfo.InvariantCulture);
            marker(p.Y, text, EditorColours.Dialog.AddGreen.Lighten(0.4f), left: false, row: 0);
        }

        foreach (var s in beat.Stops)
        {
            if (s.Y < pLo || s.Y > pHi) continue;
            marker(s.Y, "stop", EditorColours.Divisor.Blue, left: false, row: 1);
        }
    }

    private void marker(long pulse, string text, Colour4 colour, bool left, int row)
    {
        float y = YOfPulse(pulse);
        batch.AddRect(left ? L.SIDE - 14 : L.SIDE + L.LANES, y - 1, 14, 2, colour);

        if (markerCount == markers.Count)
        {
            var t = new SpriteText { Font = EditorFont.Bold(15), Shadow = true };
            markers.Add(t);
            markerLayer.Add(t);
        }

        var st = markers[markerCount++];
        st.Alpha = 1;
        if (st.Text.ToString() != text) st.Text = text;
        st.Colour = colour;
        st.Anchor = Anchor.TopLeft;
        st.Origin = left ? Anchor.BottomRight : Anchor.BottomLeft;
        st.X = left ? L.SIDE - 4 : L.SIDE + L.LANES + 4;
        st.Y = y - 2 + row * 16;
    }
}
