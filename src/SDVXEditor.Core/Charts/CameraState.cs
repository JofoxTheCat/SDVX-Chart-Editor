namespace SDVXEditor.Core.Charts;

/// <summary>
/// The camera at one moment, evaluated from <see cref="CameraInfo"/> (and the lasers, for auto tilt).
/// Raw values keep the kson units; the derived angles/offsets are what a renderer applies. Shared by
/// the editor's camera preview and the playtest renderer.
/// </summary>
public readonly record struct CameraState
{
    /// <summary>kson units (2400 = 360°, ver ≥ 167). Rotates the far end of the lane around the judgement line.</summary>
    public double ZoomTop { get; init; }

    /// <summary>kson units. Positive brings the lane closer to the camera.</summary>
    public double ZoomBottom { get; init; }

    /// <summary>kson units. Shifts the lane sideways (100 = 0.72 of half the lane block, as USC).</summary>
    public double ZoomSide { get; init; }

    /// <summary>kson units; 100 = one BT lane of gap at the centre.</summary>
    public double CenterSplit { get; init; }

    /// <summary>Roll of lane and judgement line in degrees.</summary>
    public double RotationDeg { get; init; }

    /// <summary>Tilt in laser units (±1 = one laser fully at the edge), auto or manual, before the mode's scale.</summary>
    public double TiltInput { get; init; }

    /// <summary>Scale of the active tilt mode (normal 1, bigger 1.75, biggest 2.5, zero 0; manual 1).</summary>
    public double TiltScale { get; init; }

    /// <summary>True while a manual tilt value is in effect.</summary>
    public bool TiltManual { get; init; }

    /// <summary>The auto mode in effect (null while manual).</summary>
    public TiltMode? TiltMode { get; init; }

    /// <summary>Roll added by a spin / half spin / swing pattern, in degrees.</summary>
    public double PatternRollDeg { get; init; }

    /// <summary>Tilt roll in degrees (<see cref="TiltInput"/> × scale × <see cref="CameraEvaluator.TiltDegreesPerUnit"/>).</summary>
    public double TiltDeg => TiltInput * TiltScale * CameraEvaluator.TiltDegreesPerUnit;

    /// <summary>Total roll around the judgement line: tilt + rotation_deg + pattern.</summary>
    public double RollDeg => TiltDeg + RotationDeg + PatternRollDeg;

    /// <summary>Pitch of the lane plane added by zoom_top, in degrees (USC: <c>PitchScaleFunc(zoom_top / 100) × 15°</c>;
    /// 35 → 4.46°, 100 → 12.7°, 2400 → 360°).</summary>
    public double PitchDeg => CameraProjection.PitchScale(ZoomTop / 100.0) * 15;

    /// <summary>Camera distance to the judgement line relative to the default (USC zoom; zoom_bottom −95 → ×1.49,
    /// 100 → ×0.61, 300 → ×0.14).</summary>
    public double DistanceFactor => CameraProjection.ZoomFactor(ZoomBottom);

    /// <summary>Sideways shift of the lane in lane-block half widths (USC: zoom_side 100 → 0.36 track widths = 0.72).</summary>
    public double ShiftX => ZoomSide / 100.0 * 500.0 / 696.0;

    /// <summary>Gap at the centre in BT lane widths.</summary>
    public double SplitLanes => CenterSplit / 100.0;

    public static readonly CameraState Default = new() { TiltScale = 1, TiltMode = Charts.TiltMode.Normal };
}

/// <summary>
/// Evaluates <see cref="CameraInfo"/> at a pulse. Graph values follow <see cref="Graph.ValueAt"/>
/// (curves, instant jumps). Tilt: a manual point holds or interpolates towards the next manual
/// point; an auto point switches the mode, and the auto input comes from the laser positions.
/// <para>
/// Approximations (no official formula is documented): auto tilt input = left laser value + (right
/// laser value − 1), lasers absent count as neutral; <c>keep_*</c> behaves like its plain mode;
/// spin = one eased full turn, half spin = out to 180° and back, swing = a decaying oscillation of
/// ±<c>scale</c>/250 × <see cref="SwingDegrees"/>.
/// </para>
/// </summary>
public sealed class CameraEvaluator
{
    /// <summary>Degrees of roll per tilt unit at scale 1.</summary>
    public const double TiltDegreesPerUnit = 10;

    /// <summary>Swing amplitude in degrees at the default scale 250.</summary>
    public const double SwingDegrees = 12;

    private readonly Chart chart;

    public CameraEvaluator(Chart chart) => this.chart = chart;

    public static double ScaleOf(TiltMode mode) => mode switch
    {
        Charts.TiltMode.Normal or Charts.TiltMode.KeepNormal => 1.0,
        Charts.TiltMode.Bigger or Charts.TiltMode.KeepBigger => 1.75,
        Charts.TiltMode.Biggest or Charts.TiltMode.KeepBiggest => 2.5,
        _ => 0.0,
    };

    public CameraState Evaluate(double pulse)
    {
        var body = chart.Camera.Body;
        var (input, scale, manual, mode) = EvaluateTilt(pulse);
        return new CameraState
        {
            ZoomTop = Graph.ValueAt(body.ZoomTop, pulse, 0),
            ZoomBottom = Graph.ValueAt(body.ZoomBottom, pulse, 0),
            ZoomSide = Graph.ValueAt(body.ZoomSide, pulse, 0),
            CenterSplit = Graph.ValueAt(body.CenterSplit, pulse, 0),
            RotationDeg = Graph.ValueAt(body.RotationDeg, pulse, 0),
            TiltInput = input,
            TiltScale = scale,
            TiltManual = manual,
            TiltMode = mode,
            PatternRollDeg = PatternRoll(pulse),
        };
    }

    /// <summary>Tilt at a pulse: (input, scale, manual?, auto mode).</summary>
    public (double Input, double Scale, bool Manual, TiltMode? Mode) EvaluateTilt(double pulse)
    {
        var tilt = chart.Camera.Tilt;
        int i = -1;
        for (int k = tilt.Count - 1; k >= 0; k--)
        {
            if (tilt[k].Y <= pulse)
            {
                i = k;
                break;
            }
        }

        if (i < 0)
            return (AutoInput(pulse), 1.0, false, Charts.TiltMode.Normal);

        var p = tilt[i];
        var after = p.Final ?? p.Value;

        if (after.Auto is { } m)
            return (m == Charts.TiltMode.Zero ? 0 : AutoInput(pulse), ScaleOf(m), false, m);

        double v = after.Manual;
        if (i + 1 < tilt.Count && tilt[i + 1].Value.Auto == null && tilt[i + 1].Y > p.Y)
        {
            var q = tilt[i + 1];
            double x = (pulse - p.Y) / (q.Y - p.Y);
            double f = p.Curve?.Apply(x) ?? x;
            v = after.Manual + (q.Value.Manual - after.Manual) * f;
        }

        return (v, 1.0, true, null);
    }

    /// <summary>Auto tilt input from the lasers active at <paramref name="pulse"/>.</summary>
    public double AutoInput(double pulse)
    {
        double left = LaserValue(0, pulse) ?? 0;
        double right = LaserValue(1, pulse) ?? 1;
        return Math.Clamp(left + (right - 1), -1, 1);
    }

    private double? LaserValue(int lane, double pulse)
    {
        foreach (var s in chart.Notes.Lasers[lane])
        {
            if (s.Y > pulse) break;
            if (s.EndY < pulse || s.Points.Count == 0) continue;
            double v = ValueAt(s, pulse);
            // a wide laser moves twice as far
            return s.Width == 2 ? 0.5 + (v - 0.5) * 2 : v;
        }

        return null;
    }

    /// <summary>Laser value of a section at an absolute pulse (value after a slam on that pulse).</summary>
    public static double ValueAt(LaserSection s, double pulse)
    {
        var pts = s.Points;
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            double y = s.Y + pts[i].Ry;
            if (y > pulse) continue;
            if (y == pulse || i == pts.Count - 1) return pts[i].ValueAfter;
            var q = pts[i + 1];
            double x = (pulse - y) / (q.Ry - pts[i].Ry);
            double f = pts[i].Curve?.Apply(x) ?? x;
            return pts[i].ValueAfter + (q.V - pts[i].ValueAfter) * f;
        }

        return pts.Count > 0 ? pts[0].V : 0;
    }

    /// <summary>Roll from the pattern active at <paramref name="pulse"/> (the latest one that started).</summary>
    public double PatternRoll(double pulse)
    {
        double roll = 0;
        foreach (var (kind, p) in Patterns(chart.Camera))
        {
            if (p.Length <= 0 || pulse < p.Y || pulse > p.Y + p.Length) continue;
            double t = (pulse - p.Y) / p.Length;
            roll = PatternRollAt(kind, p, t);
        }

        return roll;
    }

    /// <summary>Roll of one pattern at progress <paramref name="t"/> in [0, 1].</summary>
    public static double PatternRollAt(CamPatternKind kind, CamPattern p, double t)
    {
        t = Math.Clamp(t, 0, 1);
        double dir = p.Direction < 0 ? -1 : 1;
        switch (kind)
        {
            case CamPatternKind.Spin:
                return dir * 360 * (1 - Math.Pow(1 - t, 2)) % 360;

            case CamPatternKind.HalfSpin:
                return dir * 180 * Math.Sin(Math.PI * t);

            default:
                double amp = (p.SwingScale ?? 250) / 250 * SwingDegrees;
                int repeat = Math.Max(1, p.SwingRepeat ?? 3);
                int decay = Math.Max(0, p.SwingDecayOrder ?? 2);
                return dir * amp * Math.Pow(1 - t, decay) * Math.Sin(2 * Math.PI * repeat * t);
        }
    }

    /// <summary>All patterns with their kind, in start order.</summary>
    public static IEnumerable<(CamPatternKind Kind, CamPattern Pattern)> Patterns(CameraInfo cam) =>
        cam.Spin.Select(p => (CamPatternKind.Spin, p))
           .Concat(cam.HalfSpin.Select(p => (CamPatternKind.HalfSpin, p)))
           .Concat(cam.Swing.Select(p => (CamPatternKind.Swing, p)))
           .OrderBy(x => x.p.Y);
}

public enum CamPatternKind
{
    Spin,
    HalfSpin,
    Swing,
}

/// <summary>
/// Pinhole projection of the lane for a <see cref="CameraState"/>, with the track geometry and camera of
/// unnamed-sdvx-clone (USC, MIT; <c>Main/src/Track.cpp</c>, <c>Main/src/Camera.cpp</c>, landscape values, read
/// 2026-10-26 — numbers only, the code is ours). Lane space: <c>x</c> across the lane block in [-1, 1] (laser lane
/// centres at ±<see cref="LaserX"/>), <c>along</c> from the judgement line (0) to the far end (1). Screen space:
/// x right, y down, both in units of the viewport height, judgement line centre at (0, <see cref="JudgeScreenY"/>)
/// before roll.
/// <list type="bullet">
/// <item><b>Track</b>: USC's track is 1 wide and <c>trackLength</c> = 12 long; ours is 2 wide, so
/// <see cref="LaneLength"/> = 24. The lane shows 8 / hi-speed beats over that length (USC
/// <c>SetViewRange(8 / hispeed)</c>), which puts a 1/16 at hi-speed 1.87 about 0.35 lane widths ahead of the
/// judgement line and the far end at 9 % of the near width.</item>
/// <item><b>Camera</b>: USC places the judgement line 0.9 below and 0.9 in front of the camera with the track tilted
/// up by 1.5° (<c>GetOriginTransform</c>), a 60° vertical field of view, and pitches the camera so the judgement line
/// sits 5 % above the bottom edge. We keep that view and only move the image up so the line lands at
/// <see cref="JudgeScreenY"/> (a shift of the principal point: the perspective is unchanged, the HUD footer stays
/// clear).</item>
/// <item><b>zoom_top</b>: USC <c>PitchScaleFunc(zoom_top / 100) × 15°</c> rotates the lane around the judgement line
/// (<see cref="CameraState.PitchDeg"/>). <b>zoom_bottom</b>: the track slides along the camera→judgement-line ray, by
/// <c>1.65^−z − 1</c> track widths away for z = zoom_bottom / 100 ≤ 0 and to <c>1.65^−(z^1.35)</c> of the distance
/// for z &gt; 0 (<see cref="CameraState.DistanceFactor"/>). <b>zoom_side</b>: USC <c>(z / 100 × 500 / 696) / 2</c>
/// track widths sideways (<see cref="CameraState.ShiftX"/>).</item>
/// <item>Kept from before (not USC): roll (tilt, rotation_deg, spins) turns the image around the judgement line centre
/// — USC rolls around the camera axis — and the centre split opens one BT lane at 100.</item>
/// </list>
/// </summary>
public static class CameraProjection
{
    /// <summary>World length of the visible lane (lane block width = 2; USC: 12 track widths).</summary>
    public const double LaneLength = 24;

    /// <summary>Lane-space units per USC track width (ours is 2 wide).</summary>
    public const double TrackWidth = 2;

    /// <summary>USC: the judgement line lies 0.9 track widths below and in front of the camera (before the anchor tilt).</summary>
    public const double CritDrop = 0.9 * TrackWidth;

    public const double CritDepth = 0.9 * TrackWidth;

    /// <summary>USC: the track's base tilt (anchor rotation), degrees.</summary>
    public const double BasePitchDeg = 1.5;

    /// <summary>USC landscape: vertical field of view (degrees).</summary>
    public const double FieldOfViewDeg = 60;

    /// <summary>USC landscape: the judgement line's height above the bottom edge (fraction of the viewport) the camera pitch aims for.</summary>
    public const double CritFromBottom = 0.05;

    /// <summary>Focal length in viewport heights (vertical field of view <see cref="FieldOfViewDeg"/>).</summary>
    public static readonly double FocalLength = 0.5 / Math.Tan(FieldOfViewDeg / 2 * Math.PI / 180);

    /// <summary>Where the judgement line centre lands (fraction of the viewport height below the centre).</summary>
    public const double JudgeScreenY = 0.32;

    /// <summary>Width of one BT lane in lane-space units (4 BT + 2 half-width laser lanes = 2).</summary>
    public const double BtLaneWidth = 2.0 / 5;

    /// <summary>Laser value 0..1 → lane x (laser lane centres).</summary>
    public const double LaserX = 0.9;

    /// <summary>A world length along the lane as a fraction of the lane (object sizes that must not scale with the lane length).</summary>
    public static double Along(double worldLength) => worldLength / LaneLength;

    public readonly record struct ScreenPoint(double X, double Y, double Depth)
    {
        public bool Visible => Depth > 0.05;
    }

    // the judgement line centre relative to the camera at zoom 0 (y up, z forward), its distance, the camera pitch
    // (radians, looking down) and the vertical image shift that puts the line at JudgeScreenY
    private static readonly double critY, critZ, critDistance, cameraPitch, imageShift;

    static CameraProjection()
    {
        (critY, critZ) = Anchor(0, 0);
        critDistance = Math.Sqrt(critY * critY + critZ * critZ);
        double fov = FieldOfViewDeg * Math.PI / 180;
        // USC: rotToCrit − (fov/2 − fov·pitchOffset): look down by the angle to the judgement line less the angle that
        // puts it CritFromBottom above the bottom edge
        cameraPitch = Math.Atan2(-critY, critZ) - (fov / 2 - fov * CritFromBottom);
        double c = Math.Cos(cameraPitch), s = Math.Sin(cameraPitch);
        double depth = critZ * c - critY * s;
        imageShift = JudgeScreenY + FocalLength * (critY * c + critZ * s) / depth;
    }

    /// <summary>A point d along the lane (pitched by zoom_top around the judgement line, then the base tilt), relative to the camera.</summary>
    private static (double Y, double Z) Anchor(double d, double pitchDeg)
    {
        double p = pitchDeg * Math.PI / 180;
        double y = d * Math.Sin(p), z = CritDepth + d * Math.Cos(p);
        double b = BasePitchDeg * Math.PI / 180;
        return (y * Math.Cos(b) + z * Math.Sin(b) - CritDrop, -y * Math.Sin(b) + z * Math.Cos(b));
    }

    /// <summary>Applies the centre split: each half moves outwards by half the gap.</summary>
    /// <param name="side">-1 / 1: which half a point exactly on the centre belongs to (0 = it stays).</param>
    public static double SplitX(CameraState s, double x, int side = 0)
    {
        double half = s.SplitLanes * BtLaneWidth / 2;
        int sign = x == 0 ? side : Math.Sign(x);
        return x + sign * half;
    }

    public static ScreenPoint Project(CameraState s, double x, double along, int side = 0)
    {
        double lx = SplitX(s, x, side) + s.ShiftX;
        var (wy, wz) = Anchor(along * LaneLength, s.PitchDeg);

        // zoom_bottom: the track slides along the camera→judgement-line ray, so the line keeps its screen spot
        double slide = (s.DistanceFactor - 1) * critDistance;
        wy += critY / critDistance * slide;
        wz += critZ / critDistance * slide;

        double c = Math.Cos(cameraPitch), sn = Math.Sin(cameraPitch);
        double depth = wz * c - wy * sn;
        if (depth <= 1e-6) return new ScreenPoint(0, 0, depth);
        double sx = FocalLength * lx / depth;
        double sy = -FocalLength * (wy * c + wz * sn) / depth + imageShift;

        // roll around the judgement line centre
        double jy = JudgeScreenY;
        double r = s.RollDeg * Math.PI / 180;
        double rx = sx, ry = sy - jy;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        return new ScreenPoint(rx * cos - ry * sin, jy + rx * sin + ry * cos, depth);
    }

    /// <summary>
    /// USC's <c>PitchScaleFunc</c> (landscape): zoom_top / 100 → lane pitch in units of 15°. Piecewise linear
    /// (−4 → −3.05, 5.59 → 4.75, ±12 → ±12), repeating every 24 units (360°).
    /// </summary>
    public static double PitchScale(double input)
    {
        const double kLower = -4, kUpper = 5.59, uLower = -3.05, uUpper = 4.75;
        int rot = 0, dir = input < 0 ? -1 : 1;
        if (dir == -1)
        {
            while (input < -12)
            {
                input += 24;
                rot++;
            }
        }
        else
        {
            while (input > 12)
            {
                input -= 24;
                rot++;
            }
        }

        double scaled = input < kLower ? (input - kLower) / (12 + kLower) * (12 + uLower) + uLower
            : input < 0 ? input / kLower * uLower
            : input < kUpper ? input / kUpper * uUpper
            : (input - kUpper) / (12 - kUpper) * (12 - uUpper) + uUpper;
        return rot * dir * 24 + scaled;
    }

    /// <summary>USC zoom: the camera's distance to the judgement line relative to the default, for a zoom_bottom value.</summary>
    public static double ZoomFactor(double zoomBottom)
    {
        double z = zoomBottom / 100;
        return z <= 0
            ? 1 + (Math.Pow(1.65, -z) - 1) / (critDistance / TrackWidth)
            : Math.Pow(1.65, -Math.Pow(z, 1.35));
    }
}
