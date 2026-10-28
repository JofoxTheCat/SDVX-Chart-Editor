using SDVXEditor.Core.Charts;

namespace SDVXEditor.Game.Screens.Edit.Compose;

/// <summary>
/// Horizontal geometry of the compose playfield, in its local pixels:
/// <c>[markers | LASER-L | BT-A | BT-B | BT-C | BT-D | LASER-R | markers]</c>. FX-L lies under
/// BT-A+B, FX-R under BT-C+D. Laser values 0..1 run from the centre of the left laser lane to the
/// centre of the right one; a wide (2x) laser covers twice that range around the middle, like in game, so
/// its outer quarter on each side lies past the lanes (<see cref="WIDE_REACH"/>, drawn over the side margin
/// and the <see cref="OVERHANG"/> space beside the playfield).
/// </summary>
public static class PlayfieldLayout
{
    public const float LASER_LANE = 40;
    public const float BT_LANE = 80;

    /// <summary>
    /// How far outside the lane block (left and right) the laser tool still places, drags and hit-tests
    /// laser points for normal lasers: one BT lane, where they clamp to 0 / 1 (convenience). Wide lasers reach
    /// further, at true scale (<see cref="WIDE_REACH"/>).
    /// </summary>
    public const float LASER_REACH = BT_LANE;

    /// <summary>
    /// Side margin for BPM / scroll speed markers. Equal to <see cref="LASER_REACH"/>, so the laser margin is
    /// part of the playfield drawable and never reaches into the density column or the toolbars.
    /// </summary>
    public const float SIDE = LASER_REACH;

    /// <summary>Width of the lane block (two laser lanes + four BT lanes).</summary>
    public const float LANES = 2 * LASER_LANE + 4 * BT_LANE;

    public const float TOTAL_WIDTH = LANES + 2 * SIDE;

    public const float BT_LEFT = SIDE + LASER_LANE;
    public const float BT_RIGHT = BT_LEFT + 4 * BT_LANE;

    /// <summary>Distance of the judgement line from the bottom edge.</summary>
    public const float JUDGEMENT_FROM_BOTTOM = 170;

    /// <summary>Pixels per millisecond at playfield zoom 1.</summary>
    public const float PX_PER_MS = 0.32f;

    /// <summary>BT chip height. Kept slimmer than <see cref="FX_CHIP_HEIGHT"/> so an FX chip under two BT chips stays visible (as in the game, FX chips are the fatter ones).</summary>
    public const float CHIP_HEIGHT = 10;

    /// <summary>FX chip height; it sticks out above and below BT chips on the same pulse.</summary>
    public const float FX_CHIP_HEIGHT = 20;

    /// <summary>Half width of a drawn laser band.</summary>
    public const float LASER_HALF_WIDTH = 15;

    public const float LASER_LEFT_X = SIDE + LASER_LANE / 2;
    public const float LASER_RIGHT_X = SIDE + LANES - LASER_LANE / 2;
    public const float LASER_CENTRE_X = (LASER_LEFT_X + LASER_RIGHT_X) / 2;

    /// <summary>Default laser value snap: ksh stores 51 steps (0..50).</summary>
    public const double LASER_VALUE_STEPS = 50;

    public static float LaneLeft(NoteKind kind, int lane) => kind == NoteKind.Bt ? BT_LEFT + lane * BT_LANE : BT_LEFT + lane * 2 * BT_LANE;

    public static float LaneWidth(NoteKind kind) => kind == NoteKind.Bt ? BT_LANE : 2 * BT_LANE;

    /// <summary>BT lane under x, or null outside the BT block.</summary>
    public static int? BtLaneAt(float x) => x >= BT_LEFT && x < BT_RIGHT ? (int)((x - BT_LEFT) / BT_LANE) : null;

    /// <summary>FX lane under x (left half of the BT block = FX-L), or null outside the BT block.</summary>
    public static int? FxLaneAt(float x) => x >= BT_LEFT && x < BT_RIGHT ? (x < BT_LEFT + 2 * BT_LANE ? 0 : 1) : null;

    /// <summary>Nearest lane of a kind, clamped (used while dragging).</summary>
    public static int NearestLane(NoteKind kind, float x)
    {
        int count = NoteData.LaneCount(kind);
        int lane = (int)Math.Floor((x - BT_LEFT) / LaneWidth(kind));
        return Math.Clamp(lane, 0, count - 1);
    }

    /// <summary>Left / right edge of the lane block.</summary>
    public const float LANES_LEFT = SIDE;

    public const float LANES_RIGHT = SIDE + LANES;

    /// <summary>
    /// How far a wide laser (true 2x scale, like the game) reaches past each edge of the lane block: values
    /// 0..0.25 / 0.75..1 lie out there. 160 px with the current lanes = two BT lanes.
    /// </summary>
    public const float WIDE_REACH = (LASER_RIGHT_X - LASER_LEFT_X) - LANES / 2;

    /// <summary>
    /// Space a playfield needs beyond its own drawable on each side so a wide laser band at 0 / 1 is not clipped and
    /// can be clicked there: <see cref="WIDE_REACH"/> + the band's half width + a little, minus the side margin.
    /// The compose tab keeps this free next to every playfield (main and reference).
    /// </summary>
    public const float OVERHANG = WIDE_REACH + LASER_HALF_WIDTH + 5 - SIDE;

    /// <summary>Normal lasers: centre of laser lane L (0) to centre of laser lane R (1). Wide: twice that around the centre, past the lanes.</summary>
    public static float LaserX(double value, bool wide)
    {
        float span = LASER_RIGHT_X - LASER_LEFT_X;
        return wide
            ? LASER_CENTRE_X + (float)(value - 0.5) * 2 * span
            : LASER_LEFT_X + (float)value * span;
    }

    /// <summary>Inverse of <see cref="LaserX"/>, clamped to 0..1 (normal lasers clamp inside the <see cref="LASER_REACH"/> margin).</summary>
    public static double LaserValue(float x, bool wide)
    {
        float span = LASER_RIGHT_X - LASER_LEFT_X;
        double v = wide ? 0.5 + (x - LASER_CENTRE_X) / (2 * span) : (x - LASER_LEFT_X) / span;
        return Math.Clamp(v, 0, 1);
    }

    /// <summary>Inverse of <see cref="LaserX"/> without clamping (drags keep the grab offset; Bezier controls may leave 0..1).</summary>
    public static double LaserValueRaw(float x, bool wide)
    {
        float span = LASER_RIGHT_X - LASER_LEFT_X;
        return wide ? 0.5 + (x - LASER_CENTRE_X) / (2 * span) : (x - LASER_LEFT_X) / span;
    }

    /// <summary>
    /// Values a Bezier control anchor may take: what lies inside the playfield's input / drawing area (the field plus
    /// <see cref="OVERHANG"/> on both sides, less half a handle), so a control out there stays visible and grabbable.
    /// Normal lasers about −0.53..1.53, wide ones −0.02..1.02 (the overhang is sized for the wide range).
    /// </summary>
    public static (double Min, double Max) ControlLimits(bool wide) =>
        (Math.Min(0, LaserValueRaw(-OVERHANG + 8, wide)), Math.Max(1, LaserValueRaw(TOTAL_WIDTH + OVERHANG - 8, wide)));

    /// <summary>True when x lies where the laser tool acts: the lanes plus <see cref="LASER_REACH"/>, or for wide lasers plus <see cref="WIDE_REACH"/>.</summary>
    public static bool InLaserReach(float x, bool wide = false)
    {
        float reach = wide ? WIDE_REACH + LASER_HALF_WIDTH : LASER_REACH;
        return x >= LANES_LEFT - reach && x <= LANES_RIGHT + reach;
    }

    public static double SnapLaserValue(double v) => Math.Round(v * LASER_VALUE_STEPS) / LASER_VALUE_STEPS;
}

/// <summary>Laser path helpers shared by renderer and editing.</summary>
public static class LaserMath
{
    /// <summary>Value of a section at an absolute pulse (after a slam on that pulse). Null outside the section.</summary>
    public static double? ValueAt(LaserSection s, double pulse)
    {
        var pts = s.Points;
        if (pts.Count == 0 || pulse < s.Y || pulse > s.EndY) return null;

        for (int i = pts.Count - 1; i >= 0; i--)
        {
            double y = s.Y + pts[i].Ry;
            if (y > pulse) continue;
            if (y == pulse || i == pts.Count - 1) return pts[i].ValueAfter;

            var q = pts[i + 1];
            return Interpolate(pts[i], q, (pulse - y) / (q.Ry - pts[i].Ry));
        }

        return pts[0].V;
    }

    /// <summary>Value inside segment p → q at fraction x (0..1), with p's curve.</summary>
    public static double Interpolate(LaserPoint p, LaserPoint q, double x)
    {
        double f = p.Curve?.Apply(x) ?? x;
        return p.ValueAfter + (q.V - p.ValueAfter) * f;
    }
}
