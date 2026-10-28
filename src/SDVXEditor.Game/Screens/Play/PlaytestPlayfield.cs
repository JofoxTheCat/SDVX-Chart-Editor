using System.Diagnostics;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Play;

/// <summary>
/// The SDVX track in perspective: lanes, measure lines, BT/FX chips and holds, lasers with slams and wide range,
/// laser cursors, judgement line and hit flashes. Every frame the camera comes from <see cref="CameraEvaluator"/>
/// and every vertex goes through <see cref="CameraProjection.Project"/> — the same math as the camera tab's
/// preview. Distance along the lane is the chart's scroll integral (<see cref="Core.Timing.TimingMap.VisualPosition"/>,
/// scroll speed and stops included) times the hi-speed, so a beat always covers the same distance like in SDVX.
/// <para>
/// Everything is one <see cref="QuadBatch"/>; only objects inside the visible pulse range are visited (binary
/// search per lane), so a dense chart costs a few thousand projected vertices per frame.
/// </para>
/// </summary>
public partial class PlaytestPlayfield : CompositeDrawable
{
    /// <summary>Lane length in beats at hi-speed 1 (USC <c>SetViewRange(8 / hispeed)</c>: BPM × hi-speed 600 at 120 BPM
    /// shows 1.6 beats = 0.8 s over <see cref="CameraProjection.LaneLength"/>).</summary>
    public const double BASE_BEATS = 8;

    // Lengths along the lane are world lengths (lane block width = 2) turned into lane fractions, so objects keep their
    // size whatever the lane length is. At the judgement line one world unit is about as large on screen as before the
    // USC camera (the camera is nearer but the field of view wider).
    private static double w(double world) => CameraProjection.Along(world);

    private static readonly double chip_len = w(0.216);
    private static readonly double slam_len = w(0.288);
    private const double laser_half = 0.07;
    private static readonly double behind = w(-1.08);

    private static readonly double bt = CameraProjection.BtLaneWidth;

    private readonly PlayableChart chart;
    private readonly GameplayJudge judge;
    private readonly CameraEvaluator camera;
    private readonly QuadBatch batch;

    // VOX game effects (PlayfieldEffects): background-stage overlay behind the track, additive ghosts and an overlay above it
    private readonly VoxEffectEvaluator voxEffects;
    private readonly QuadBatch backgroundOverlay;
    private readonly QuadBatch ghost;
    private readonly QuadBatch overlay;
    private readonly Random effectRandom = new Random(7);
    private readonly Dictionary<(int Lane, long Pulse), List<VoxScript>> scriptedChips = new();
    private double scriptPulseNow;

    /// <summary>The game effects of the last frame (the screen colours its background with <see cref="PlayfieldEffects.Background"/>).</summary>
    public PlayfieldEffects Effects { get; private set; } = PlayfieldEffects.None;

    private readonly List<(int Lane, double X0, double X1, Colour4 Colour, double Start)> effects = new List<(int, double, double, Colour4, double)>();

    /// <summary>Song time drawn (ms).</summary>
    public double SongTime { get; set; }

    /// <summary>Hi-speed multiplier (BPM × hi-speed = the "mod speed").</summary>
    public double HiSpeed { get; set; } = 5;

    public CameraState LastState { get; private set; }
    public int LastQuadCount { get; private set; }
    public int LastVisibleObjects { get; private set; }

    /// <summary>Time to build the frame's quads (ms, CPU), for the smoke report.</summary>
    public double LastBuildMs { get; private set; }

    public double MaxBuildMs { get; private set; }

    /// <summary>Mean build time and the most objects / quads in one frame since <see cref="ResetStats"/>.</summary>
    public double MeanBuildMs => builds == 0 ? 0 : buildSum / builds;

    public int MaxVisibleObjects { get; private set; }
    public int MaxQuads { get; private set; }

    private double buildSum;
    private int builds;

    private double laneBeats => BASE_BEATS / Math.Max(0.05, HiSpeed);
    private double visNow;
    private CameraState state;
    private int visible;

    public PlaytestPlayfield(PlayableChart chart, GameplayJudge judge)
    {
        this.chart = chart;
        this.judge = judge;
        camera = new CameraEvaluator(chart.Chart);
        voxEffects = new VoxEffectEvaluator(chart.Chart, p => chart.Timing.PulseToMs(p));
        // VOX note scripts on chips: (play lane, pulse) → scripts (BT A..D = TRACK3..6, FX-L = 2, FX-R = 7)
        var vx = chart.Chart.VoxExtras;
        foreach (var sn in vx.Scripted)
        {
            int lane = sn.Track switch { >= 3 and <= 6 => sn.Track - 3, 2 => PlayLanes.FxL, 7 => PlayLanes.FxR, _ => -1 };
            var scripts = vx.Scripts.Where(x => sn.Ids.Contains(x.Id)).ToList();
            if (lane >= 0 && scripts.Count > 0) scriptedChips[(lane, sn.Y)] = scripts;
        }
        RelativeSizeAxes = Axes.Both;
        InternalChildren = new Drawable[]
        {
            backgroundOverlay = new QuadBatch(),
            batch = new QuadBatch(),
            ghost = new QuadBatch { Blending = BlendingParameters.Additive },
            overlay = new QuadBatch(),
        };
    }

    /// <summary>Flash at the judgement line of a lane (0-7); slams flash at <paramref name="laserValue"/>.</summary>
    public void Flash(int lane, HitResult result, double laserValue = 0.5, int laserWidth = 1)
    {
        var colour = result switch
        {
            HitResult.SCritical or HitResult.Critical => Colour4.FromHex("fff7a8"),
            HitResult.Near => Colour4.FromHex("4fc3ff"),
            _ => Colour4.FromHex("ff4040"),
        };

        double x0, x1;
        if (lane < 4) (x0, x1) = (-2 * bt + lane * bt, -bt + lane * bt);
        else if (lane < 6) (x0, x1) = lane == PlayLanes.FxL ? (-2 * bt, 0.0) : (0.0, 2 * bt);
        else
        {
            double x = laserX(laserValue, laserWidth);
            (x0, x1) = (x - 0.12, x + 0.12);
        }

        effects.Add((lane, x0, x1, colour, Clock.CurrentTime));
    }

    protected override void Update()
    {
        base.Update();

        var sw = Stopwatch.StartNew();
        batch.Clear();
        ghost.Clear();
        overlay.Clear();
        backgroundOverlay.Clear();
        visible = 0;
        if (DrawWidth < 10 || DrawHeight < 10) return;
        Effects = PlayfieldEffects.At(voxEffects, SongTime);

        var timing = chart.Timing;
        double pulseNow = timing.MsToPulse(SongTime);
        scriptPulseNow = pulseNow;
        visNow = timing.VisualPosition(pulseNow);
        state = camera.Evaluate(Math.Max(0, pulseNow));
        LastState = state;

        // visible pulse range: walk out in beats until the lane end (scroll speed can be anything, stops are 0)
        long pFrom = (long)Math.Floor(pulseNow), pTo = (long)Math.Ceiling(pulseNow);
        for (int k = 0; k < 16 && along(pFrom) > behind; k++) pFrom -= Timebase.PulsesPerQuarter;
        for (int k = 0; k < 1024 && along(pTo) < 1; k++) pTo += Timebase.PulsesPerQuarter;

        drawTrack(timing, pFrom, pTo);
        int trackQuads = batch.Count;
        drawLaneClear();
        drawLanePress();

        for (int lane = PlayLanes.FxL; lane <= PlayLanes.FxR; lane++)
            drawButtonLane(lane, pFrom, pTo);
        for (int lane = 0; lane < 4; lane++)
            drawButtonLane(lane, pFrom, pTo);

        for (int side = 0; side < 2; side++)
            drawLasers(side, pFrom, pTo);

        drawJudgementLine();
        drawCursors();
        drawEffects();
        applyGameEffects(trackQuads);

        LastQuadCount = batch.Count;
        LastVisibleObjects = visible;
        LastBuildMs = sw.Elapsed.TotalMilliseconds;
        MaxBuildMs = Math.Max(MaxBuildMs, LastBuildMs);
        buildSum += LastBuildMs;
        builds++;
        MaxVisibleObjects = Math.Max(MaxVisibleObjects, visible);
        MaxQuads = Math.Max(MaxQuads, LastQuadCount);
    }

    /// <summary>
    /// The VOX game effects on this frame's quads (preview; see <see cref="PlayfieldEffects"/>): LANECOL tints the track,
    /// LANECLEARCOL fills it, the lane stage's ColorConversion maps every colour, LaneY slides the track (+1024 = one
    /// screen height down; the game's direction is not verified), shake moves it, the rest are ghost / overlay passes.
    /// </summary>
    private void applyGameEffects(int trackQuads)
    {
        var fx = Effects;
        if (fx.LaneColour is { } lc)
            batch.MapColours(c => new ColourInfo
            {
                TopLeft = PlayfieldEffects.Tint((Colour4)c.TopLeft.SRGB, lc),
                TopRight = PlayfieldEffects.Tint((Colour4)c.TopRight.SRGB, lc),
                BottomLeft = PlayfieldEffects.Tint((Colour4)c.BottomLeft.SRGB, lc),
                BottomRight = PlayfieldEffects.Tint((Colour4)c.BottomRight.SRGB, lc),
            }, 0, trackQuads);

        if (fx.Lane.Conversions.Count > 0) batch.MapColours(c => PlayfieldEffects.Convert(fx.Lane, c));
        if (fx.Lane.Any) PlayfieldEffects.DrawGhosts(fx.Lane, batch, ghost, overlay, DrawSize, effectRandom, batch.Count);
        if (fx.Background.Any)
        {
            var bgOnly = new QuadBatch();
            PlayfieldEffects.DrawGhosts(fx.Background, bgOnly, backgroundOverlay, backgroundOverlay, DrawSize, effectRandom, 0);
        }

        var shake = PlayfieldEffects.ShakeOffset(fx.Lane.Shake * DrawHeight / 1080, effectRandom);
        var offset = new Vector2(0, (float)(fx.LaneY / 1024 * DrawHeight)) + shake;
        batch.Position = ghost.Position = offset;
        LastTrackQuads = trackQuads;
    }

    /// <summary>LANECLEARCOL: a fill over the lane block, behind the notes.</summary>
    private void drawLaneClear()
    {
        if (Effects.LaneClearColour is not { } clear || (clear >> 24) == 0) return;
        var fill = PlayfieldEffects.Argb(clear);
        for (int k = 0; k < 8; k++)
        {
            double a0 = behind + (1 - behind) * k / 8, a1 = behind + (1 - behind) * (k + 1) / 8;
            rect(-1, 0, a0, a1, fill, -1);
            rect(0, 1, a0, a1, fill, 1);
        }
    }

    /// <summary>Quads of the track (lanes and bar lines) in the last frame.</summary>
    public int LastTrackQuads { get; private set; }

    /// <summary>Resets the per-run maximum of <see cref="LastBuildMs"/>.</summary>
    public void ResetStats()
    {
        MaxBuildMs = buildSum = 0;
        builds = MaxVisibleObjects = MaxQuads = 0;
    }

    // ---- geometry -------------------------------------------------------

    /// <summary>Screen measurements of the current frame (smoke probe): lane width at the judgement line, the distance
    /// of a 1/16 and of a beat ahead of <paramref name="pulseNow"/> along the centre, and the lane width at the far end.</summary>
    public (double LaneWidthPx, double SixteenthPx, double BeatPx, double FarWidthPx) MeasureGeometry(double pulseNow)
    {
        Vector2 at(double x, double a) => screen(CameraProjection.Project(state, x, a));
        double width(double a) => (at(1, a) - at(-1, a)).Length;
        double dist(double pulses) => (at(0, along(pulseNow + pulses)) - at(0, along(pulseNow))).Length;
        return (width(along(pulseNow)), dist(Timebase.PulsesPerQuarter / 4.0), dist(Timebase.PulsesPerQuarter), width(1));
    }

    private double along(double pulse) => (chart.Timing.VisualPosition(pulse) - visNow) / (Timebase.PulsesPerQuarter * laneBeats);

    private static double laserX(double v, int width) =>
        width == 2 ? (v - 0.5) * 2 * CameraProjection.LaserX * 2 : -CameraProjection.LaserX + 2 * CameraProjection.LaserX * v;

    private Vector2 screen(CameraProjection.ScreenPoint p)
    {
        float h = DrawHeight;
        return new Vector2(DrawWidth / 2 + (float)p.X * h, h / 2 + (float)p.Y * h);
    }

    private static int sideOf(double x0, double x1) => x1 <= 0 ? -1 : x0 >= 0 ? 1 : 0;

    /// <summary>Lane-space rectangle x0..x1 × along a0..a1 (a0 near, a1 far), skipped when a corner is behind the camera.</summary>
    private void rect(double x0, double x1, double a0, double a1, ColourInfo colour, int side = 0)
    {
        var bl = CameraProjection.Project(state, x0, a0, side);
        var br = CameraProjection.Project(state, x1, a0, side);
        var tl = CameraProjection.Project(state, x0, a1, side);
        var tr = CameraProjection.Project(state, x1, a1, side);
        if (!bl.Visible || !br.Visible || !tl.Visible || !tr.Visible) return;
        batch.Add(new Quad(screen(tl), screen(tr), screen(bl), screen(br)), colour);
    }

    /// <summary>A band from (x0, a0) to (x1, a1) with a horizontal half width (laser segment).</summary>
    private void band(double x0, double a0, double x1, double a1, double half, ColourInfo colour)
    {
        var bl = CameraProjection.Project(state, x0 - half, a0);
        var br = CameraProjection.Project(state, x0 + half, a0);
        var tl = CameraProjection.Project(state, x1 - half, a1);
        var tr = CameraProjection.Project(state, x1 + half, a1);
        if (!bl.Visible || !br.Visible || !tl.Visible || !tr.Visible) return;
        batch.Add(new Quad(screen(tl), screen(tr), screen(bl), screen(br)), colour);
    }

    /// <summary>Fades objects out at the far end of the lane.</summary>
    private static float farFade(double a) => (float)Math.Clamp((1 - a) / 0.15, 0, 1);

    // ---- track ------------------------------------------------------------

    private static readonly Colour4 lane_dark = Colour4.FromHex("0d0d16");
    private static readonly Colour4 lane_mid = Colour4.FromHex("12121d");

    private void drawTrack(Core.Timing.TimingMap timing, long pFrom, long pTo)
    {
        const int slices = 8;
        var laserL = EditorColours.Lanes.LaserLeft;
        var laserR = EditorColours.Lanes.LaserRight;

        for (int k = 0; k < slices; k++)
        {
            double a0 = behind + (1 - behind) * k / slices, a1 = behind + (1 - behind) * (k + 1) / slices;
            float f0 = farFade(a0), f1 = farFade(a1);
            ColourInfo shade(Colour4 c, float alpha) => ColourInfo.GradientVertical(c.Opacity(alpha * f1), c.Opacity(alpha * f0));

            rect(-1, -2 * bt, a0, a1, shade(laserL, 0.16f), -1);
            rect(2 * bt, 1, a0, a1, shade(laserR, 0.16f), 1);
            for (int lane = 0; lane < 4; lane++)
            {
                double x0 = -2 * bt + lane * bt;
                rect(x0, x0 + bt, a0, a1, shade(lane is 1 or 2 ? lane_mid : lane_dark, 0.97f), lane < 2 ? -1 : 1);
            }

            // dividers: thin between BT lanes, brighter at the laser lanes and the centre
            foreach (double x in new[] { -bt, bt })
                rect(x - 0.004, x + 0.004, a0, a1, shade(Colour4.White, 0.16f), Math.Sign(x));
            rect(-0.006, 0, a0, a1, shade(Colour4.White, 0.3f), -1);
            rect(0, 0.006, a0, a1, shade(Colour4.White, 0.3f), 1);
            rect(-2 * bt - 0.008, -2 * bt + 0.004, a0, a1, shade(laserL, 0.7f), -1);
            rect(2 * bt - 0.004, 2 * bt + 0.008, a0, a1, shade(laserR, 0.7f), 1);
            rect(-1 - 0.012, -1, a0, a1, shade(laserL, 0.45f), -1);
            rect(1, 1 + 0.012, a0, a1, shade(laserR, 0.45f), 1);
        }

        // measure lines
        for (int m = Math.Max(0, timing.MeasureAt(pFrom)); ; m++)
        {
            long start = timing.MeasureStart(m);
            if (start > pTo) break;
            double a = along(start);
            if (a < behind || a > 1) continue;
            if (voxEffects.BarHidden(start, p => timing.MeasureStart(timing.MeasureAt(p)))) continue; // VOX BAROFF
            var c = Colour4.White.Opacity(0.42f * farFade(a));
            rect(-2 * bt, 0, a, a + w(0.045), c, -1);
            rect(0, 2 * bt, a, a + w(0.045), c, 1);
        }

        // VOX BAR: forced bar lines
        foreach (long bar in voxEffects.ForcedBars)
        {
            if (bar < pFrom || bar > pTo) continue;
            double a = along(bar);
            if (a < behind || a > 1) continue;
            var c = Colour4.White.Opacity(0.42f * farFade(a));
            rect(-2 * bt, 0, a, a + w(0.045), c, -1);
            rect(0, 2 * bt, a, a + w(0.045), c, 1);
        }
    }

    private void drawLanePress()
    {
        for (int lane = 0; lane < PlayLanes.Buttons; lane++)
        {
            if (!judge.Held[lane]) continue;
            bool fx = lane >= 4;
            double x0 = fx ? (lane == PlayLanes.FxL ? -2 * bt : 0) : -2 * bt + lane * bt;
            double x1 = fx ? x0 + 2 * bt : x0 + bt;
            var c = fx ? EditorColours.Lanes.Fx : Colour4.FromHex("cfe6ff");
            rect(x0, x1, 0, w(1.6), ColourInfo.GradientVertical(c.Opacity(0), c.Opacity(fx ? 0.22f : 0.28f)), sideOf(x0, x1));
        }
    }

    private void drawJudgementLine()
    {
        var c = Colour4.FromHex("f4f4ff");
        rect(-1, 0, w(-0.036), w(0.036), c, -1);
        rect(0, 1, w(-0.036), w(0.036), c, 1);
        rect(-1, -2 * bt, w(-0.108), w(0.108), EditorColours.Lanes.LaserLeft, -1);
        rect(2 * bt, 1, w(-0.108), w(0.108), EditorColours.Lanes.LaserRight, 1);
    }

    // ---- notes ------------------------------------------------------------

    private void drawButtonLane(int lane, long pFrom, long pTo)
    {
        bool fx = lane >= 4;
        double x0 = fx ? (lane == PlayLanes.FxL ? -2 * bt : 0) : -2 * bt + lane * bt;
        double x1 = fx ? x0 + 2 * bt : x0 + bt;
        int side = sideOf(x0, x1);
        var colour = fx ? EditorColours.Lanes.Fx : Colour4.White;

        // holds (sorted, non-overlapping per lane → binary search on the end)
        var holds = chart.Holds[lane];
        int lo = 0, hi = holds.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (holds[mid].Note.EndY < pFrom) lo = mid + 1;
            else hi = mid;
        }

        for (int i = lo; i < holds.Count; i++)
        {
            var h = holds[i];
            if (h.Note.Y > pTo) break;
            double a0 = along(h.Note.Y), a1 = Math.Min(1, along(h.Note.EndY));
            bool active = judge.Held[lane] && h.Ms <= SongTime && SongTime <= h.EndMs;
            if (active || (h.Ms <= SongTime && judge.Held[lane])) a0 = Math.Max(a0, 0);
            a0 = Math.Max(a0, behind);
            if (a1 <= a0) continue;
            visible++;
            float alpha = active ? 0.95f : 0.62f;
            var body = fx ? colour.Opacity(alpha * 0.85f) : Colour4.FromHex("e8f2ff").Opacity(alpha);
            double inset = fx ? 0.03 : 0.035;
            rect(x0 + inset, x1 - inset, a0, a1, ColourInfo.GradientVertical(body.Opacity(body.A * farFade(a1)), body), side);
            if (active)
                rect(x0 + inset, x1 - inset, 0, w(0.108), Colour4.White, side);
        }

        // chips
        var chips = chart.Chips[lane];
        lo = 0;
        hi = chips.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (chips[mid].Pulse < pFrom) lo = mid + 1;
            else hi = mid;
        }

        for (int i = lo; i < chips.Count; i++)
        {
            var o = chips[i];
            if (o.Pulse > pTo) break;
            if (o.IsHit) continue;
            double a = along(o.Pulse);
            double cx0 = x0, cx1 = x1;
            int cside = side;
            if (scriptedChips.Count > 0 && scriptedChips.TryGetValue((lane, o.Pulse), out var scripts))
            {
                // VOX note script preview: drawn at $targetStep, shifted by $offsetX (0.178 ≈ one BT lane)
                var r = VoxScriptEval.Run(scripts, o.Pulse * VoxScriptEval.StepsPerPulse, scriptPulseNow * VoxScriptEval.StepsPerPulse);
                a = along(r.TargetStep / VoxScriptEval.StepsPerPulse);
                double dx = r.OffsetX / 0.178 * bt;
                cx0 += dx;
                cx1 += dx;
                cside = sideOf(cx0, cx1);
            }
            if (a < behind || a > 1) continue;
            visible++;
            float f = farFade(a) * (o.Judged ? 0.35f : 1);
            double pad = fx ? 0.012 : 0.018;
            if (fx)
            {
                rect(cx0 + pad, cx1 - pad, a - chip_len / 2, a + chip_len / 2, colour.Opacity(f), cside);
                rect(cx0 + pad + 0.03, cx1 - pad - 0.03, a - chip_len / 4, a + chip_len / 4, Colour4.FromHex("ffd9a6").Opacity(f), cside);
            }
            else
            {
                rect(cx0 + pad, cx1 - pad, a - chip_len / 2, a + chip_len / 2, Colour4.White.Opacity(f), cside);
                rect(cx0 + pad + 0.02, cx1 - pad - 0.02, a - chip_len / 4, a + chip_len / 4, Colour4.FromHex("b9d4f0").Opacity(f), cside);
            }
        }
    }

    // ---- lasers ----------------------------------------------------------

    private void drawLasers(int side, long pFrom, long pTo)
    {
        var colour = side == 0 ? EditorColours.Lanes.LaserLeft : EditorColours.Lanes.LaserRight;
        var sections = chart.Lasers[side];

        int lo = 0, hi = sections.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (sections[mid].Section.EndY < pFrom) lo = mid + 1;
            else hi = mid;
        }

        for (int i = lo; i < sections.Count; i++)
        {
            var sec = sections[i].Section;
            if (sec.Y > pTo) break;
            visible++;
            drawSection(sec, colour, pFrom, pTo);
        }
    }

    private void drawSection(LaserSection sec, Colour4 colour, long pFrom, long pTo)
    {
        var pts = sec.Points;
        int width = sec.Width;
        double half = width == 2 ? laser_half * 1.15 : laser_half;

        ColourInfo tint(double a) => colour.Opacity((a < 0 ? 0.35f : 0.82f) * farFade(a));

        // entry before every section start and exit after a closing slam, like USC (Track.cpp / LaserTrackBuilder:
        // entry 2.56, exit 2 band widths long from its default skin's 50x256 / 50x200 textures; same rule as the compose
        // playfield, ComposePlayfield.ENTRY_LENGTH), fading away from the laser
        double ha = along(sec.Y);
        if (ha >= behind && ha <= 1 && pts.Count > 0)
        {
            double hx = laserX(pts[0].V, width);
            var c = colour.Opacity(0.95f * farFade(ha));
            rect(hx - half, hx + half, ha - w(Edit.Compose.ComposePlayfield.ENTRY_LENGTH * 2 * half), ha, ColourInfo.GradientVertical(c, c.Opacity(0)));
        }

        if (pts.Count > 0 && pts[^1].IsSlam)
        {
            double ea = along(sec.Y + pts[^1].Ry) + slam_len;
            if (ea >= behind && ea <= 1)
            {
                double ex = laserX(pts[^1].Vf!.Value, width);
                var c = colour.Opacity(0.95f * farFade(ea));
                rect(ex - half, ex + half, ea, ea + w(Edit.Compose.ComposePlayfield.EXIT_LENGTH * 2 * half), ColourInfo.GradientVertical(c.Opacity(0), c));
            }
        }

        for (int k = 0; k < pts.Count; k++)
        {
            var p = pts[k];
            long y = sec.Y + p.Ry;
            if (y > pTo) break;

            if (p.IsSlam)
            {
                double a = along(y);
                if (a >= behind && a <= 1)
                {
                    double xa = laserX(p.V, width), xb = laserX(p.Vf!.Value, width);
                    rect(Math.Min(xa, xb) - half, Math.Max(xa, xb) + half, a, a + slam_len, colour.Opacity(0.95f * farFade(a)));
                }
            }

            if (k + 1 >= pts.Count) break;
            var q = pts[k + 1];
            long y1 = sec.Y + q.Ry;
            if (y1 < pFrom || y1 <= y) continue;

            // segment y..y1 clipped to the visible range, subdivided where the projection or a curve bends it
            double from = Math.Max(y, pFrom), to = Math.Min(y1, pTo);
            double af = along(from), at = along(to);
            int steps = Math.Clamp((int)Math.Ceiling(Math.Abs(at - af) * 64), 1, 96);
            if (p.Curve is { IsLinear: false }) steps = Math.Max(steps, 16);

            double v0 = p.ValueAfter, v1 = q.V, len = y1 - y;
            double valueAt(double pulse)
            {
                double x = (pulse - y) / len;
                double f = p.Curve?.Apply(x) ?? x;
                return v0 + (v1 - v0) * f;
            }

            double prevA = af, prevX = laserX(valueAt(from), width);
            for (int s = 1; s <= steps; s++)
            {
                double pulse = from + (to - from) * s / steps;
                double a = s == steps ? at : along(pulse);
                double x = laserX(valueAt(pulse), width);
                double a0 = prevA, a1 = a;
                if (a1 > behind && a0 < 1 && a1 > a0)
                    band(prevX, Math.Max(a0, behind), x, Math.Min(a1, 1), half, tint((a0 + a1) / 2));
                prevA = a;
                prevX = x;
            }
        }
    }

    private void drawCursors()
    {
        for (int side = 0; side < 2; side++)
        {
            var colour = side == 0 ? EditorColours.Lanes.LaserLeft : EditorColours.Lanes.LaserRight;
            bool active = judge.CursorActive[side];
            int width = judge.ActiveLaser[side]?.Section.Width ?? 1;
            double x = laserX(judge.Cursor[side], width);
            float alpha = active ? 1 : 0.3f;

            var tip = CameraProjection.Project(state, x, w(0.5));
            var l = CameraProjection.Project(state, x - 0.075, w(-0.135));
            var r = CameraProjection.Project(state, x + 0.075, w(-0.135));
            if (!tip.Visible || !l.Visible || !r.Visible) continue;
            var t = screen(tip);
            batch.Add(new Quad(t, t, screen(l), screen(r)), colour.Opacity(alpha));
            if (active && judge.Locked[side])
            {
                var t2 = CameraProjection.Project(state, x, w(0.27));
                var l2 = CameraProjection.Project(state, x - 0.04, w(-0.045));
                var r2 = CameraProjection.Project(state, x + 0.04, w(-0.045));
                var tt = screen(t2);
                batch.Add(new Quad(tt, tt, screen(l2), screen(r2)), Colour4.White.Opacity(0.9f));
            }
        }
    }

    private void drawEffects()
    {
        const double duration = 220;
        double now = Clock.CurrentTime;
        effects.RemoveAll(e => now - e.Start > duration);
        foreach (var (lane, x0, x1, c, start) in effects)
        {
            double t = (now - start) / duration;
            float alpha = (float)(1 - t);
            double grow = 0.03 * t;
            int side = lane >= 6 ? 0 : sideOf(x0, x1);
            rect(x0 - grow, x1 + grow, w(-0.18), w(0.45 + 0.72 * t), ColourInfo.GradientVertical(c.Opacity(0), c.Opacity(0.85f * alpha)), side);
        }
    }
}
