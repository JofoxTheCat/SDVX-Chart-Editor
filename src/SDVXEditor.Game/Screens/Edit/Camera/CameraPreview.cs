using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>
/// Live camera preview: a perspective SDVX lane with the upcoming notes and lasers, drawn through
/// <see cref="CameraProjection"/> (USC's track and camera geometry) with the <see cref="CameraState"/> at the clock's
/// time — the same projection as the playtest.
/// </summary>
public partial class CameraPreview : CompositeDrawable
{
    /// <summary>
    /// How far ahead the lane shows notes, in ms: the playtest's default view (mod speed 600 → 8 / hi-speed beats =
    /// 480 000 / 600 ms at the basis tempo, USC's view range). With USC's long lane and strong perspective a longer
    /// window crams the far half.
    /// </summary>
    public const double WINDOW_MS = 800;

    // lengths along the lane as world lengths (lane block width = 2), independent of the lane length
    private static double w(double world) => CameraProjection.Along(world);

    private const int slices = 20;
    private const double laser_half = 0.075;

    private readonly EditorSession session;
    private readonly EditorClock clock;

    private QuadBatch batch = null!;

    // VOX game effects (the playtest's PlayfieldEffects): ColorConversion, lane colours and LaneY, cached per chart version
    private static readonly Colour4 bg_top = Colour4.FromHex("1b1030");
    private static readonly Colour4 bg_bottom = Colour4.FromHex("05040a");
    private Box background = null!;
    private VoxEffectEvaluator? voxEffects;
    private int voxEffectsVersion = -1;

    /// <summary>The game effects of the last frame.</summary>
    public SDVXEditor.Game.Screens.Play.PlayfieldEffects Effects { get; private set; } = SDVXEditor.Game.Screens.Play.PlayfieldEffects.None;
    private SpriteText readout = null!;

    /// <summary>The state drawn last frame (smoke test).</summary>
    public CameraState LastState { get; private set; }

    public int LastQuadCount { get; private set; }

    public CameraPreview(EditorSession session, EditorClock clock)
    {
        this.session = session;
        this.clock = clock;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 6;
        BorderThickness = 1.5f;
        BorderColour = Colour4.White.Opacity(0.3f);
        InternalChildren = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = ColourInfo.GradientVertical(bg_top, bg_bottom) },
            batch = new QuadBatch(),
            readout = new SpriteText
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(8, -6),
                Font = EditorFont.Regular(14),
                Colour = Colour4.White.Opacity(0.85f),
                Shadow = true,
            },
            new SpriteText
            {
                Position = new Vector2(8, 6),
                Text = "camera preview (approximation)",
                Font = EditorFont.Regular(13),
                Colour = EditorColours.TextMuted,
            },
        };
    }

    private Vector2 screen(CameraProjection.ScreenPoint p)
    {
        float h = DrawHeight;
        return new Vector2(DrawWidth / 2 + (float)p.X * h, h / 2 + (float)p.Y * h);
    }

    /// <summary>Projected quad over lane-space x0..x1 × along a0..a1; null if any corner is behind the camera.</summary>
    private Quad? quad(CameraState s, double x0, double x1, double a0, double a1, int side = 0)
    {
        var bl = CameraProjection.Project(s, x0, a0, side);
        var br = CameraProjection.Project(s, x1, a0, side);
        var tl = CameraProjection.Project(s, x0, a1, side);
        var tr = CameraProjection.Project(s, x1, a1, side);
        if (!bl.Visible || !br.Visible || !tl.Visible || !tr.Visible) return null;
        return new Quad(screen(tl), screen(tr), screen(bl), screen(br));
    }

    private void add(Quad? q, ColourInfo c)
    {
        if (q is { } v) batch.Add(v, c);
    }

    private static int sideOf(double x0, double x1) => x1 <= 0 ? -1 : x0 >= 0 ? 1 : 0;

    protected override void Update()
    {
        base.Update();
        batch.Clear();
        if (DrawWidth < 10 || DrawHeight < 10) return;

        double now = clock.CurrentTime.Value;
        double pulseNow = Math.Max(0, session.MsToPulse(now));
        var state = new CameraEvaluator(session.Chart).Evaluate(pulseNow);
        LastState = state;

        // lane block: laser L, BT A..D, laser R
        double bt = CameraProjection.BtLaneWidth;
        var lanes = new (double X0, double X1, Colour4 C)[]
        {
            (-1, -2 * bt, EditorColours.Lanes.LaserLeft.Opacity(0.18f)),
            (-2 * bt, -bt, Colour4.FromHex("101018")),
            (-bt, 0, Colour4.FromHex("14141e")),
            (0, bt, Colour4.FromHex("14141e")),
            (bt, 2 * bt, Colour4.FromHex("101018")),
            (2 * bt, 1, EditorColours.Lanes.LaserRight.Opacity(0.18f)),
        };

        for (int k = 0; k < slices; k++)
        {
            double a0 = (double)k / slices, a1 = (double)(k + 1) / slices;
            float fade = 1 - 0.6f * (float)a0;
            foreach (var (x0, x1, c) in lanes)
                add(quad(state, x0, x1, a0, a1, sideOf(x0, x1)), c.Opacity(c.A * fade + 0.15f * fade));
            // separators
            foreach (double x in new[] { -2 * bt, -bt, bt, 2 * bt })
                add(quad(state, x - 0.006, x + 0.006, a0, a1, Math.Sign(x)), Colour4.White.Opacity(0.35f * fade));
            add(quad(state, -0.008, 0, a0, a1, -1), Colour4.White.Opacity(0.5f * fade));
            add(quad(state, 0, 0.008, a0, a1, 1), Colour4.White.Opacity(0.5f * fade));
        }

        // beat lines
        var timing = session.Timing;
        long pEnd = (long)Math.Ceiling(session.MsToPulse(now + WINDOW_MS));
        for (int m = timing.MeasureAt((long)pulseNow); ; m++)
        {
            long start = timing.MeasureStart(m);
            if (start > pEnd) break;
            double a = along(start, now);
            if (a >= 0 && a <= 1)
            {
                add(quad(state, -2 * bt, 0, a, a + w(0.036), -1), Colour4.White.Opacity(0.4f));
                add(quad(state, 0, 2 * bt, a, a + w(0.036), 1), Colour4.White.Opacity(0.4f));
            }
        }

        int trackQuads = batch.Count;
        drawNotes(state, now);
        drawLasers(state, now);

        // judgement line
        add(quad(state, -1, 0, 0, w(0.108), -1), EditorColours.Osu.JudgementGreen);
        add(quad(state, 0, 1, 0, w(0.108), 1), EditorColours.Osu.JudgementGreen);

        applyGameEffects(now, trackQuads);
        LastQuadCount = batch.Count;
        string tilt = state.TiltManual ? "manual " + state.TiltInput.ToString("0.##", CultureInfo.InvariantCulture) : TiltValue.NameOf(state.TiltMode ?? TiltMode.Normal);
        readout.Text = string.Create(CultureInfo.InvariantCulture,
            $"top {state.ZoomTop:0} · bottom {state.ZoomBottom:0} · side {state.ZoomSide:0} · split {state.CenterSplit:0} · roll {state.RollDeg:0.#}° (tilt {tilt})");
    }

    private void applyGameEffects(double now, int trackQuads)
    {
        if (voxEffects == null || voxEffectsVersion != session.Version)
        {
            voxEffects = new VoxEffectEvaluator(session.Chart, p => session.PulseToMs(p));
            voxEffectsVersion = session.Version;
        }

        var fx = Effects = SDVXEditor.Game.Screens.Play.PlayfieldEffects.At(voxEffects, now);
        if (fx.LaneColour is { } lc)
            batch.MapColours(c => ColourInfo.SingleColour(SDVXEditor.Game.Screens.Play.PlayfieldEffects.Tint((Colour4)c.AverageColour.SRGB, lc)), 0, trackQuads);
        if (fx.Lane.Conversions.Count > 0) batch.MapColours(c => SDVXEditor.Game.Screens.Play.PlayfieldEffects.Convert(fx.Lane, c));
        background.Colour = fx.Background.Conversions.Count == 0
            ? ColourInfo.GradientVertical(bg_top, bg_bottom)
            : ColourInfo.GradientVertical(SDVXEditor.Game.Screens.Play.PlayfieldEffects.Convert(fx.Background, bg_top),
                SDVXEditor.Game.Screens.Play.PlayfieldEffects.Convert(fx.Background, bg_bottom));
        batch.Y = (float)(fx.LaneY / 1024 * DrawHeight);
    }

    private double along(double pulse, double now) => (session.PulseToMs(pulse) - now) / WINDOW_MS;

    private void drawNotes(CameraState s, double now)
    {
        var notes = session.Chart.Notes;
        long pFrom = (long)Math.Floor(session.MsToPulse(now - 200));
        long pTo = (long)Math.Ceiling(session.MsToPulse(now + WINDOW_MS));
        double bt = CameraProjection.BtLaneWidth;

        for (int lane = 0; lane < 2; lane++)
        {
            double x0 = lane == 0 ? -2 * bt : 0, x1 = x0 + 2 * bt;
            foreach (var n in notes.Fx[lane])
            {
                if (n.Y > pTo) break;
                if (n.EndY < pFrom) continue;
                drawNote(s, now, n, x0 + 0.01, x1 - 0.01, EditorColours.Lanes.Fx, n.IsHold ? 0.55f : 1, lane == 0 ? -1 : 1);
            }
        }

        for (int lane = 0; lane < 4; lane++)
        {
            double x0 = -2 * bt + lane * bt;
            foreach (var n in notes.Bt[lane])
            {
                if (n.Y > pTo) break;
                if (n.EndY < pFrom) continue;
                drawNote(s, now, n, x0 + 0.02, x0 + bt - 0.02, Colour4.White, n.IsHold ? 0.8f : 1, lane < 2 ? -1 : 1);
            }
        }
    }

    private void drawNote(CameraState s, double now, Note n, double x0, double x1, Colour4 c, float alpha, int side)
    {
        double a0 = Math.Max(0, along(n.Y, now));
        if (n.IsHold)
        {
            double a1 = Math.Min(1, along(n.EndY, now));
            if (a1 > a0) add(quad(s, x0 + 0.02, x1 - 0.02, a0, a1, side), c.Opacity(alpha * 0.7f));
        }

        double a = along(n.Y, now);
        if (a >= 0 && a <= 1) add(quad(s, x0, x1, a, a + w(0.162), side), c.Opacity(alpha));
    }

    private void drawLasers(CameraState s, double now)
    {
        long pFrom = (long)Math.Floor(session.MsToPulse(now));
        long pTo = (long)Math.Ceiling(session.MsToPulse(now + WINDOW_MS));
        for (int lane = 0; lane < 2; lane++)
        {
            var colour = lane == 0 ? EditorColours.Lanes.LaserLeft : EditorColours.Lanes.LaserRight;
            foreach (var sec in session.Chart.Notes.Lasers[lane])
            {
                if (sec.Y > pTo) break;
                if (sec.EndY < pFrom || sec.Points.Count < 2) continue;

                double xOf(double v) => sec.Width == 2 ? (v - 0.5) * 2 * CameraProjection.LaserX * 2 : -CameraProjection.LaserX + 2 * CameraProjection.LaserX * v;

                long from = Math.Max(sec.Y, pFrom), to = Math.Min(sec.EndY, pTo);
                const int steps = 40;
                double prevA = along(from, now), prevX = xOf(CameraEvaluator.ValueAt(sec, from));
                for (int k = 1; k <= steps; k++)
                {
                    double pulse = from + (to - from) * (double)k / steps;
                    double a = along(pulse, now), x = xOf(CameraEvaluator.ValueAt(sec, pulse));
                    if (a > prevA) laserSegment(s, prevX, prevA, x, a, colour.Opacity(0.75f));
                    prevA = a;
                    prevX = x;
                }

                // slams as horizontal bars
                foreach (var p in sec.Points.Where(p => p.IsSlam))
                {
                    double a = along(sec.Y + p.Ry, now);
                    if (a < 0 || a > 1) continue;
                    double x0 = xOf(p.V), x1 = xOf(p.Vf!.Value);
                    add(quad(s, Math.Min(x0, x1) - laser_half, Math.Max(x0, x1) + laser_half, a, a + w(0.18)), colour);
                }
            }
        }
    }

    private void laserSegment(CameraState s, double x0, double a0, double x1, double a1, Colour4 c)
    {
        var bl = CameraProjection.Project(s, x0 - laser_half, a0);
        var br = CameraProjection.Project(s, x0 + laser_half, a0);
        var tl = CameraProjection.Project(s, x1 - laser_half, a1);
        var tr = CameraProjection.Project(s, x1 + laser_half, a1);
        if (!bl.Visible || !br.Visible || !tl.Visible || !tr.Visible) return;
        batch.Add(new Quad(screen(tl), screen(tr), screen(bl), screen(br)), c);
    }
}
