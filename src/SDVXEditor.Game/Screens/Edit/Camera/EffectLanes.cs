using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Screens.Edit.Compose;
using L = SDVXEditor.Game.Screens.Edit.Compose.PlayfieldLayout;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>
/// The effect lanes of the camera tab's effects view: VOX game effects on the compose time axis (same judgement line,
/// px/ms and zoom as <see cref="CameraLanes"/>). Post-effect lanes show one column per effect (game id order), tween
/// lanes a graph with start / end handles like the camera lanes, the other lanes markers. One <see cref="QuadBatch"/>.
/// </summary>
public partial class EffectLanes : CompositeDrawable
{
    /// <summary>Colour per post effect (game id order).</summary>
    public static readonly Colour4[] EffectColours =
    {
        Colour4.FromHex("ff6b6b"), Colour4.FromHex("ffd166"), Colour4.FromHex("7fe0ff"), Colour4.FromHex("b8b8b8"),
        Colour4.FromHex("8ce99a"), Colour4.FromHex("fff3bf"), Colour4.FromHex("b197fc"), Colour4.FromHex("ff922b"),
    };

    public static readonly string[] EffectShort = { "CA", "CC", "RB", "SN", "CRT", "LUM", "FL", "SH" };

    private static readonly Colour4 graph_colour = Colour4.FromHex("7fe0ff");
    private static readonly Colour4 asset_colour = Colour4.FromHex("e599f7");

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly EffectLaneEditor editor;
    private readonly IBindable<float> zoom;

    private QuadBatch batch = null!;
    private Container textLayer = null!;
    private readonly List<SpriteText> texts = new List<SpriteText>();
    private int textCount;
    private bool dragging;

    public int LastQuadCount { get; private set; }

    public EffectLanes(EditorSession session, EditorClock clock, EffectLaneEditor editor, IBindable<float> zoom)
    {
        this.session = session;
        this.clock = clock;
        this.editor = editor;
        this.zoom = zoom.GetBoundCopy();
        RelativeSizeAxes = Axes.Y;
        Width = EffectLaneEditor.TotalWidth;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            batch = new QuadBatch(),
            textLayer = new Container { RelativeSizeAxes = Axes.Both },
        };
    }

    // ---- coordinates -------------------------------------------------------

    private float pxPerMs => L.PX_PER_MS * zoom.Value;
    private float judgeY => DrawHeight - L.JUDGEMENT_FROM_BOTTOM;

    public float YOfMs(double ms) => judgeY - (float)((ms - clock.CurrentTime.Value) * pxPerMs);
    public float YOfPulse(double pulse) => YOfMs(session.PulseToMs(pulse));

    private static float left(FxLane lane) => EffectLaneEditor.LaneLeft(lane);
    private static float width(FxLane lane) => EffectLaneEditor.WidthOf(lane);

    /// <summary>Local x of fraction <paramref name="fx"/> in <paramref name="lane"/>.</summary>
    public static float XOf(FxLane lane, double fx) => left(lane) + (float)Math.Clamp(fx, 0, 1) * width(lane);

    /// <summary>Local x of the middle of a post effect's column.</summary>
    public static float PostColumnX(FxLane lane, string effect) => left(lane) + (EffectLaneEditor.PostColumn(effect) + 0.5f) * width(lane) / 8;

    public FxPointer? PointerAt(Vector2 local)
    {
        foreach (var lane in EffectLaneEditor.Lanes)
        {
            float l = left(lane), w = width(lane);
            if (local.X < l || local.X > l + w + EffectLaneEditor.GAP / 2) continue;
            return new FxPointer(clock.CurrentTime.Value + (judgeY - local.Y) / pxPerMs, lane, Math.Clamp((local.X - l) / w, 0, 1));
        }

        return null;
    }

    // ---- input -------------------------------------------------------------

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (e.Button != MouseButton.Left && e.Button != MouseButton.Right) return false;
        if (PointerAt(e.MousePosition) is not { } p) return false;
        editor.PxPerMs = pxPerMs;
        dragging = editor.PointerDown(p, e.Button == MouseButton.Right ? PointerButton.Right : PointerButton.Left, e.ShiftPressed);
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e) => dragging;

    protected override void OnDrag(DragEvent e)
    {
        if (!dragging) return;
        var lane = editor.DragLane ?? FxLane.PostBackground;
        float l = left(lane), w = width(lane);
        editor.PointerDrag(new FxPointer(clock.CurrentTime.Value + (judgeY - e.MousePosition.Y) / pxPerMs, lane, Math.Clamp((e.MousePosition.X - l) / w, 0, 1)));
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
        var vox = session.Chart.VoxExtras;
        var eval = new VoxEffectEvaluator(session.Chart, p => session.PulseToMs(p));

        foreach (var lane in EffectLaneEditor.Lanes)
        {
            batch.AddRect(left(lane), 0, width(lane), DrawHeight, Colour4.Black.Opacity(0.72f));
            if (lane <= FxLane.PostLane)
                for (int c = 1; c < 8; c++)
                    batch.AddRect(left(lane) + c * width(lane) / 8 - 0.5f, 0, 1, DrawHeight, Colour4.White.Opacity(0.06f));
            if (EffectLaneEditor.TweenName(lane) != null)
                batch.AddRect(XOf(lane, EffectLaneEditor.FxOf(lane, 0)) - 0.5f, 0, 1, DrawHeight, Colour4.White.Opacity(0.18f));
        }

        drawGrid(pLo, pHi, eval);
        drawPost(vox, FxLane.PostBackground);
        drawPost(vox, FxLane.PostLane);
        foreach (var lane in new[] { FxLane.LaneY, FxLane.BilRotZ, FxLane.Morphing2 }) drawTween(vox, lane);
        drawColours(vox);
        drawBars(vox, pLo, pHi);
        drawAssets(vox);
        drawScripts(vox);

        batch.AddRect(0, judgeY - 2, EffectLaneEditor.TotalWidth, 4, EditorColours.Osu.JudgementGreen.Opacity(0.9f));
        foreach (var lane in EffectLaneEditor.Lanes)
        {
            string value = lane switch
            {
                FxLane.PostBackground or FxLane.PostLane => activeCount(eval, lane, now),
                FxLane.LaneY => fmt(eval.Command("LaneY", now)),
                FxLane.BilRotZ => fmt(eval.Command("BIL_RotZ", now)) + "°",
                FxLane.Morphing2 => fmt(eval.Command("Morphing2", now)),
                FxLane.Colours => eval.LaneColour(now) is { } c ? VoxToken.Hex(c) : "-",
                _ => "",
            };
            float cx = left(lane) + width(lane) / 2;
            text(cx, judgeY + 12, value, Colour4.White, 14, Anchor.TopCentre);
            text(cx, judgeY + 32, EffectLaneEditor.LaneName(lane), EditorColours.TextMuted, 12, Anchor.TopCentre);
        }

        drawHover();
        for (int i = textCount; i < texts.Count; i++) texts[i].Alpha = 0;
        LastQuadCount = batch.Count;
    }

    private static string activeCount(VoxEffectEvaluator eval, FxLane lane, double now)
    {
        int n = eval.PostValues(now).Count(kv => (kv.Key.Stage == 0) == (lane == FxLane.PostBackground)
                                                  && kv.Key.Param is "Strength" or "Active" or "none" or "WholeNoiseStrength" && kv.Value > 0);
        return n == 0 ? "-" : $"{n} on";
    }

    private static string fmt(double v) => v.ToString(Math.Abs(v) < 10 ? "0.##" : "0.#", CultureInfo.InvariantCulture);

    private void drawGrid(long pLo, long pHi, VoxEffectEvaluator eval)
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
                foreach (var lane in EffectLaneEditor.Lanes)
                    batch.AddRect(left(lane), y - 0.5f, width(lane), p == start ? 1.5f : 1, Colour4.White.Opacity(p == start ? 0.45f : 0.15f));
            }
        }
    }

    private bool isSelected(VoxRowRef r) => editor.Selected is { } s && s == r;

    private float endY(VoxExtrasInfo vox, VoxRowRef r)
    {
        if (r.Kind == VoxRowKind.PostEffect && vox.PostEffects[r.Index].LengthMs is { } ms)
            return YOfMs(session.PulseToMs(vox.PostEffects[r.Index].Y) + ms);
        return YOfPulse(VoxEffectEditing.Y(vox, r) + VoxEffectEditing.Length(vox, r));
    }

    private void drawPost(VoxExtrasInfo vox, FxLane lane)
    {
        float colW = width(lane) / 8;
        foreach (var r in editor.RowsIn(lane))
        {
            var row = vox.PostEffects[r.Index];
            float y0 = YOfPulse(row.Y), y1 = endY(vox, r);
            if ((y0 < -20 && y1 < -20) || (y0 > DrawHeight + 20 && y1 > DrawHeight + 20)) continue;
            int col = EffectLaneEditor.PostColumn(row.Name);
            var c = EffectColours[col];
            float x = left(lane) + col * colW + 1;
            float top = Math.Min(y0, y1), h = Math.Max(4, Math.Abs(y0 - y1));
            if (row.IsInstant) top = y0 - 2;
            // the tween's shape: brightness from -> to
            var info = PostEffects.Find(row.Name);
            var prm = info?.Param(row.Param);
            float a0 = alphaOf(prm, row.From), a1 = alphaOf(prm, row.To);
            batch.Add(new osu.Framework.Graphics.Primitives.Quad(new Vector2(x, top), new Vector2(x + colW - 2, top), new Vector2(x, top + h), new Vector2(x + colW - 2, top + h)),
                new osu.Framework.Graphics.Colour.ColourInfo
                {
                    TopLeft = c.Opacity(a1), TopRight = c.Opacity(a1), BottomLeft = c.Opacity(a0), BottomRight = c.Opacity(a0),
                });
            batch.AddRect(x, y0 - 1.5f, colW - 2, 3, c);
            if (row.Stage > 1) batch.AddFrame(x - 1, top - 1, colW, h + 2, 1, Colour4.White);
            if (isSelected(r))
            {
                batch.AddFrame(x - 3, top - 3, colW + 4, h + 6, 2, EditorColours.Osu.Pink);
                text(left(lane) + width(lane) + 2, y0 - 2, $"{row.Name}.{row.Param} {fmt(row.From)}→{fmt(row.To)}", Colour4.White, 12, Anchor.BottomLeft);
            }
        }

        // column header
        for (int i = 0; i < 8; i++)
            text(left(lane) + (i + 0.5f) * colW, 4, EffectShort[i], EffectColours[i], 10, Anchor.TopCentre);
    }

    private static float alphaOf(PostEffectParam? p, double v)
    {
        if (p == null || p.Max <= p.Min) return 0.75f;
        return 0.25f + 0.65f * (float)Math.Clamp((v - p.Min) / (p.Max - p.Min), 0, 1);
    }

    private void line(float x1, float y1, float x2, float y2, Colour4 c, float hw = 1.2f)
    {
        if (Math.Abs(y1 - y2) < 0.5f)
            batch.AddRect(Math.Min(x1, x2), y1 - hw, Math.Abs(x2 - x1) + 0.01f, 2 * hw, c);
        else
            batch.AddBand(x1, y1, x2, y2, hw, c);
    }

    private void drawTween(VoxExtrasInfo vox, FxLane lane)
    {
        var rows = editor.RowsIn(lane).ToList();
        if (rows.Count == 0) return;
        double prevValue = 0;
        float prevY = DrawHeight + 10;
        foreach (var r in rows)
        {
            var row = vox.Commands[r.Index];
            double from = (row.Type & 0xF) == 2 ? VoxToken.Number(row.P1) ?? 0 : prevValue;
            double to = VoxToken.Number(row.P2) ?? 0;
            float y0 = YOfPulse(row.Y), y1 = YOfPulse(row.Y + row.Length);
            float xPrev = XOf(lane, EffectLaneEditor.FxOf(lane, prevValue));
            line(xPrev, prevY, xPrev, y0, graph_colour.Opacity(0.5f)); // hold
            float x0 = XOf(lane, EffectLaneEditor.FxOf(lane, from)), x1 = XOf(lane, EffectLaneEditor.FxOf(lane, to));
            if (Math.Abs(xPrev - x0) > 0.5f) line(xPrev, y0, x0, y0, Colour4.White.Opacity(0.6f), 1);
            line(x0, y0, x1, y1, graph_colour);
            bool sel = isSelected(r);
            handle(x0, y0, sel, row.Type != 2);
            if (row.Length > 0) handle(x1, y1, sel, false);
            prevValue = to;
            prevY = y1;
        }

        float xl = XOf(lane, EffectLaneEditor.FxOf(lane, prevValue));
        if (prevY > 0) line(xl, prevY, xl, 0, graph_colour.Opacity(0.5f));
    }

    private void handle(float x, float y, bool selected, bool fromCurrent)
    {
        if (y < -20 || y > DrawHeight + 20) return;
        const float size = 9;
        batch.AddRect(x - size / 2 - 1.5f, y - size / 2 - 1.5f, size + 3, size + 3, Colour4.Black.Opacity(0.85f));
        batch.AddRect(x - size / 2, y - size / 2, size, size, fromCurrent ? Colour4.Gray : Colour4.White);
        if (selected) batch.AddFrame(x - size / 2 - 4, y - size / 2 - 4, size + 8, size + 8, 2, EditorColours.Osu.Pink);
    }

    private static Colour4 argb(uint c) => new Colour4((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24));

    private void drawColours(VoxExtrasInfo vox)
    {
        const FxLane lane = FxLane.Colours;
        float x = left(lane), w = width(lane);
        var rows = editor.RowsIn(lane).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = vox.Commands[rows[i].Index];
            bool clear = row.P1 == "LANECLEARCOL";
            float y = YOfPulse(row.Y);
            // the colour holds until the next row of the same kind
            float yNext = 0;
            for (int j = i + 1; j < rows.Count; j++)
                if (vox.Commands[rows[j].Index].P1 == row.P1)
                {
                    yNext = YOfPulse(vox.Commands[rows[j].Index].Y);
                    break;
                }

            var c = VoxToken.Argb(row.P2) is { } argbValue ? argb(argbValue) : Colour4.Magenta;
            float half = w / 2;
            float sx = clear ? x + half : x;
            if (y > 0 && yNext < DrawHeight) batch.AddRect(sx + 2, Math.Max(0, yNext), half - 4, Math.Min(DrawHeight, y) - Math.Max(0, yNext), c.Opacity(Math.Max(0.12f, c.A * 0.6f)));
            if (y < -20 || y > DrawHeight + 20) continue;
            batch.AddRect(sx, y - 3, half, 6, Colour4.Black.Opacity(0.8f));
            batch.AddRect(sx + 1, y - 2, half - 2, 4, c.Opacity(Math.Max(0.35f, c.A)));
            if (isSelected(rows[i])) batch.AddFrame(sx - 2, y - 5, half + 4, 10, 2, EditorColours.Osu.Pink);
        }

        text(x + w / 4, 4, "lane", EditorColours.TextMuted, 10, Anchor.TopCentre);
        text(x + 3 * w / 4, 4, "fill", EditorColours.TextMuted, 10, Anchor.TopCentre);
    }

    private void drawBars(VoxExtrasInfo vox, long pLo, long pHi)
    {
        const FxLane lane = FxLane.Bars;
        float x = left(lane), w = width(lane);
        long? on = null;
        foreach (var r in editor.RowsIn(lane))
        {
            var row = vox.Commands[r.Index];
            float y = YOfPulse(row.Y);
            if (row.Name == "BAROFF")
            {
                if (row.P1 == "ON") on ??= row.Y;
                else if (on is { } o)
                {
                    float y0 = YOfPulse(o);
                    batch.AddRect(x + 4, Math.Max(0, y), w - 8, Math.Min(DrawHeight, y0) - Math.Max(0, y), Colour4.FromHex("ff6b6b").Opacity(0.18f));
                    on = null;
                }
            }

            if (y < -20 || y > DrawHeight + 20) continue;
            var c = row.Name == "BAR" ? Colour4.White : row.P1 == "ON" ? Colour4.FromHex("ff6b6b") : Colour4.FromHex("8ce99a");
            batch.AddRect(x, y - 1.5f, w, 3, c);
            text(x + 3, y - 3, row.Name == "BAR" ? "BAR" : "off " + row.P1, c, 11, Anchor.BottomLeft);
            if (isSelected(r)) batch.AddFrame(x - 1, y - 5, w + 2, 10, 2, EditorColours.Osu.Pink);
        }

        if (on is { } open)
        {
            float y0 = YOfPulse(open);
            if (y0 > 0) batch.AddRect(x + 4, 0, w - 8, Math.Min(DrawHeight, y0), Colour4.FromHex("ff6b6b").Opacity(0.18f));
        }
    }

    private void drawAssets(VoxExtrasInfo vox)
    {
        const FxLane lane = FxLane.Assets;
        float x = left(lane), w = width(lane);
        foreach (var r in editor.RowsIn(lane))
        {
            var row = vox.Commands[r.Index];
            float y0 = YOfPulse(row.Y), y1 = YOfPulse(row.Y + row.Length);
            if ((y0 < -20 && y1 < -20) || (y0 > DrawHeight + 20 && y1 > DrawHeight + 20)) continue;
            if (row.Length > 0) batch.AddRect(x + 3, y1, 6, y0 - y1, asset_colour.Opacity(0.45f));
            batch.AddRect(x, y0 - 1.5f, w, 3, asset_colour);
            string kind = row.Kind ?? row.Name;
            text(x + 3, y0 - 3, kind.Length > 9 ? kind[..9] : kind, asset_colour, 10, Anchor.BottomLeft);
            if (isSelected(r)) batch.AddFrame(x - 1, y0 - 6, w + 2, 12, 2, EditorColours.Osu.Pink);
        }
    }

    private void drawScripts(VoxExtrasInfo vox)
    {
        const FxLane lane = FxLane.Scripts;
        float x = left(lane), w = width(lane);
        for (int i = 0; i < vox.Scripted.Count; i++)
        {
            var s = vox.Scripted[i];
            float y = YOfPulse(s.Y);
            if (y < -20 || y > DrawHeight + 20) continue;
            batch.AddRect(x, y - 1.5f, w, 3, Colour4.FromHex("63e6be"));
            text(x + 3, y - 3, $"T{s.Track} #{string.Join(",", s.Ids)}", Colour4.FromHex("63e6be"), 10, Anchor.BottomLeft);
            if (editor.SelectedScripted == i) batch.AddFrame(x - 1, y - 6, w + 2, 12, 2, EditorColours.Osu.Pink);
        }
    }

    private void drawHover()
    {
        if (dragging || editor.Hover is not { } h) return;
        float y = YOfPulse(editor.PulseAt(h.Ms));
        batch.AddRect(left(h.Lane), y - 0.5f, width(h.Lane), 1, Colour4.White.Opacity(0.6f));
        if (EffectLaneEditor.TweenName(h.Lane) != null)
        {
            double v = EffectLaneEditor.ValueOf(h.Lane, h.Fx);
            float hx = XOf(h.Lane, EffectLaneEditor.FxOf(h.Lane, v));
            batch.AddRect(hx - 3, y - 3, 6, 6, Colour4.White.Opacity(0.6f));
            text(hx + 6, y - 2, fmt(v), Colour4.White.Opacity(0.8f), 12, Anchor.BottomLeft);
        }
        else if (h.Lane <= FxLane.PostLane)
        {
            int col = Math.Clamp((int)Math.Floor(h.Fx * 8), 0, 7);
            float colW = width(h.Lane) / 8;
            batch.AddRect(left(h.Lane) + col * colW, y - 3, colW, 6, EffectColours[col].Opacity(0.5f));
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
