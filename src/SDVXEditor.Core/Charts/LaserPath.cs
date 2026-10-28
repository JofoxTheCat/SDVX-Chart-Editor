using System.Text.Json.Serialization;

namespace SDVXEditor.Core.Charts;

/// <summary>
/// How the anchors of one path segment are interpolated (osu!lazer slider path types, minus Catmull).
/// </summary>
public enum LaserCurveType
{
    /// <summary>Straight lines through every anchor of the segment.</summary>
    Linear,

    /// <summary>Circular arc through exactly three anchors; any other count falls back to <see cref="Bezier"/> (osu! rule).</summary>
    PerfectCurve,

    /// <summary>One Bezier curve with the segment's anchors as control points (only the end anchors lie on it).</summary>
    Bezier,
}

/// <summary>
/// One editor-side anchor of a <see cref="LaserPath"/>. <see cref="Type"/> set = the anchor starts a new segment of
/// that type (a "red" anchor in osu! terms); null = an inner control point of the current segment ("inherit").
/// <see cref="Vf"/> set = a slam at this anchor: the segment before ends at <see cref="V"/>, the next starts at
/// <see cref="Vf"/>. A slam anchor always starts a segment.
/// </summary>
public sealed class LaserAnchor
{
    /// <summary>Pulse relative to the section start (the first anchor is at 0).</summary>
    public long Ry { get; set; }

    public double V { get; set; }
    public double? Vf { get; set; }
    public LaserCurveType? Type { get; set; }

    /// <summary>The type is chosen by <see cref="LaserPath.ApplyAutoTypes"/> (anchor count, curve joins); an explicit choice clears it.</summary>
    public bool Auto { get; set; }

    /// <summary>kson ease curve <c>[a, b]</c> on a two-anchor straight segment (kept from charts made before anchors existed).</summary>
    public Curve? Ease { get; set; }

    public LaserAnchor() { }

    public LaserAnchor(long ry, double v, double? vf = null, LaserCurveType? type = null, bool auto = false, Curve? ease = null)
    {
        Ry = ry;
        V = v;
        Vf = vf;
        Type = type;
        Auto = auto;
        Ease = ease;
    }

    public bool IsSlam => Vf.HasValue && Vf.Value != V;
    public double ValueAfter => Vf ?? V;

    public LaserAnchor Clone() => new(Ry, V, Vf, Type, Auto, Ease);

    public override string ToString() => $"{Ry}: {V}{(IsSlam ? $"->{Vf}" : "")} {(Type?.ToString() ?? "inherit")}{(Auto ? " (auto)" : "")}";
}

/// <summary>
/// The value axis of an anchor path. Lasers use 0..1 clamped (<see cref="Laser"/>); a camera lane uses its view range,
/// unclamped (<see cref="CameraPaths.Range"/>). Curves are built in a plane where the span <see cref="Min"/>..<see cref="Max"/>
/// is one unit and <see cref="LaserPathBaker.TIME_SCALE"/> pulses are one unit, so a perfect curve is a circle as drawn
/// in a lane of that width.
/// </summary>
public sealed record PathRange(double Min, double Max, bool Clamp)
{
    public static readonly PathRange Laser = new(0, 1, true);

    public double Span => Max - Min;

    /// <summary>Value → curve-plane unit (identity for <see cref="Laser"/>).</summary>
    public double ToUnit(double v) => Min == 0 && Max == 1 ? v : (v - Min) / Span;

    /// <summary>Curve-plane unit → value (identity for <see cref="Laser"/>).</summary>
    public double FromUnit(double u) => Min == 0 && Max == 1 ? u : Min + u * Span;

    /// <summary>The value as stored: clamped to the range when <see cref="Clamp"/>, else unchanged.</summary>
    public double Limit(double v) => Clamp ? Math.Clamp(v, Min, Max) : v;
}

/// <summary>A run of anchors <see cref="Start"/>..<see cref="End"/> (inclusive, shared with the neighbours) of one type.</summary>
public readonly record struct LaserSegment(int Start, int End, LaserCurveType Type)
{
    public int Count => End - Start + 1;

    /// <summary>
    /// The interpolation actually used: two anchors are always a straight line, a perfect curve needs exactly three
    /// anchors (otherwise Bezier, as in osu!).
    /// </summary>
    public LaserCurveType Effective => Count <= 2 ? LaserCurveType.Linear
        : Type == LaserCurveType.PerfectCurve && Count != 3 ? LaserCurveType.Bezier
        : Type;

    public bool IsCurve => Effective != LaserCurveType.Linear;
}

/// <summary>
/// The osu!lazer-style editing model of one laser section — and of one camera curve (<see cref="CameraPaths"/>, with a
/// camera <see cref="Range"/>): anchors with segment types. The chart itself only knows
/// <see cref="LaserSection.Points"/>; those are always a bake of the path (<see cref="LaserPathBaker"/>), so every
/// reader, the renderer, the playtest and the exporters keep working on points. See <see cref="LaserPaths"/> for how a
/// section's path is found, validated and stored.
/// </summary>
public sealed class LaserPath
{
    public List<LaserAnchor> Anchors { get; set; } = new();

    /// <summary>Fingerprint of the points this path was baked to (or validated against); not serialised.</summary>
    [JsonIgnore]
    internal int? Signature { get; set; }

    /// <summary>Value axis (not serialised; set by the owner: lasers 0..1, a camera lane its view range).</summary>
    [JsonIgnore]
    public PathRange Range { get; set; } = PathRange.Laser;

    public LaserPath() { }
    public LaserPath(IEnumerable<LaserAnchor> anchors) => Anchors = anchors.ToList();

    public int Count => Anchors.Count;

    public LaserPath Clone() => new(Anchors.Select(a => a.Clone())) { Signature = Signature, Range = Range };

    /// <summary>True when anchor <paramref name="i"/> starts a segment (the first anchor, a typed anchor, a slam).</summary>
    public bool StartsSegment(int i) => i == 0 || (i < Anchors.Count - 1 && (Anchors[i].Type != null || Anchors[i].IsSlam));

    /// <summary>The segments in order. A path of one anchor has none.</summary>
    public List<LaserSegment> Segments()
    {
        var list = new List<LaserSegment>();
        int start = 0;
        for (int i = 1; i < Anchors.Count; i++)
        {
            if (i == Anchors.Count - 1 || StartsSegment(i))
            {
                list.Add(new LaserSegment(start, i, Anchors[start].Type ?? LaserCurveType.Linear));
                start = i;
            }
        }

        return list;
    }

    /// <summary>The segment anchor <paramref name="i"/> belongs to (as start or inner anchor; the last anchor belongs to the last segment).</summary>
    public LaserSegment? SegmentOf(int i)
    {
        LaserSegment? found = null;
        foreach (var s in Segments())
        {
            if (i >= s.Start && i < s.End) return s;
            if (i == s.End) found = s;
        }

        return found;
    }

    /// <summary>
    /// The type an anchor shows: its own type, or for an inner anchor the type of its segment. The last anchor shows
    /// its segment's type.
    /// </summary>
    public LaserCurveType? DisplayType(int i) => Anchors[i].Type;

    /// <summary>
    /// True when every typed anchor is straight: the path bakes to exactly its anchors and adds nothing over plain points,
    /// so it is not stored (auto flags are dropped with it — like osu!lazer, types are fixed once placed).
    /// </summary>
    /// <summary>
    /// True when the anchors make a valid laser section: two or more anchors, or one slam anchor (a lone slam, as official
    /// charts have it — no tail). A single plain anchor is not a laser.
    /// </summary>
    public static bool IsSectionShape(LaserPath path) => path.Count >= 2 || path.Count == 1 && path.Anchors[0].IsSlam;

    public bool IsTrivial => Anchors.All(a => a.Type is null or LaserCurveType.Linear);

    /// <summary>
    /// True when anchor <paramref name="i"/> is a Bezier control anchor: an inherit anchor strictly inside a segment
    /// whose interpolation is Bezier (it is not on the curve). Control anchors may lie outside <see cref="Range"/> and
    /// out of time order between their neighbours (strictly inside their segment's span); the bake clamps the curve.
    /// </summary>
    public bool IsControl(int i)
    {
        if (i <= 0 || i >= Anchors.Count - 1 || Anchors[i].Type != null || Anchors[i].IsSlam) return false;
        return SegmentOf(i) is { } s && i > s.Start && i < s.End && s.Effective == LaserCurveType.Bezier;
    }

    /// <summary>
    /// Keeps the invariants: anchors sorted by pulse, the first anchor and every slam anchor typed (auto when they had
    /// none), values limited by <see cref="Range"/> (lasers: clamped to 0..1), then <see cref="ApplyAutoTypes"/>.
    /// Bezier control anchors (<see cref="IsControl"/>) keep their value and their place in the anchor order while the
    /// other anchors are in time order and they lie strictly inside their segment's span; otherwise (an anchor stopped
    /// being a control) everything is sorted and limited as before. Paths without such controls normalise exactly as
    /// they always did.
    /// </summary>
    public void Normalize()
    {
        // unsorted input is only kept in its order when a trial on a copy shows that just controls are out of order
        // (typing the first anchor of an unsorted list would otherwise type the wrong anchor before the sort)
        bool sorted = true;
        for (int i = 1; i < Anchors.Count && sorted; i++) sorted = Anchors[i - 1].Ry <= Anchors[i].Ry;
        if (!sorted && !Clone().normalizeOnce(sort: false))
        {
            normalizeOnce(sort: true);
            return;
        }

        if (!normalizeOnce(sort: false)) normalizeOnce(sort: true);
    }

    private bool normalizeOnce(bool sort)
    {
        if (sort) Anchors.Sort((a, b) => a.Ry.CompareTo(b.Ry));
        for (int i = 0; i < Anchors.Count; i++)
        {
            var a = Anchors[i];
            if ((i == 0 || a.IsSlam) && a.Type == null)
            {
                a.Type = LaserCurveType.Linear;
                a.Auto = true;
            }
            if (a.Type == null) a.Auto = false;
        }

        ApplyAutoTypes();

        // controls are recognised on the typed path; everything else must be in time order and inside the range
        var controls = Enumerable.Range(0, Anchors.Count).Where(IsControl).ToHashSet();
        long last = long.MinValue;
        bool ordered = true;
        for (int i = 0; i < Anchors.Count; i++)
        {
            if (controls.Contains(i)) continue;
            if (Anchors[i].Ry < last) ordered = false;
            last = Anchors[i].Ry;
        }

        if (ordered)
        {
            foreach (var s in Segments())
                for (int i = s.Start + 1; i < s.End; i++)
                    if (controls.Contains(i) && (Anchors[i].Ry <= Anchors[s.Start].Ry || Anchors[i].Ry >= Anchors[s.End].Ry))
                        ordered = false;
        }

        if (!ordered && !sort) return false;

        for (int i = 0; i < Anchors.Count; i++)
        {
            var a = Anchors[i];
            if (controls.Contains(i) && ordered) continue;
            a.V = Range.Limit(a.V);
            if (a.Vf is double f) a.Vf = f == a.V ? null : Range.Limit(f);
        }

        return true;
    }

    /// <summary>
    /// Types every auto segment start as osu!lazer does while placing a slider: 2 anchors → straight, 3 → perfect curve,
    /// 4 or more → Bezier. Two curves in a row meet at a Bezier anchor: an auto segment start (not the first anchor,
    /// not a slam) whose segment has 3+ anchors becomes Bezier when the segment before it is a curve.
    /// </summary>
    public void ApplyAutoTypes()
    {
        var segments = Segments();
        for (int k = 0; k < segments.Count; k++)
        {
            var s = segments[k];
            var start = Anchors[s.Start];
            if (!start.Auto) continue;

            var type = s.Count <= 2 ? LaserCurveType.Linear : s.Count == 3 ? LaserCurveType.PerfectCurve : LaserCurveType.Bezier;
            if (k > 0 && s.Count >= 3 && !start.IsSlam && Anchors[segments[k - 1].Start].Type is { } prevType
                && new LaserSegment(segments[k - 1].Start, segments[k - 1].End, prevType).IsCurve)
                type = LaserCurveType.Bezier;

            start.Type = type;
            segments[k] = s with { Type = type };
        }
    }

    /// <summary>Each point of a section becomes a straight-typed anchor; slams and kson curves are kept as they are.</summary>
    public static LaserPath FromPoints(IEnumerable<LaserPoint> points, PathRange? range = null) =>
        new(points.Select(p => new LaserAnchor(p.Ry, p.V, p.Vf, LaserCurveType.Linear, ease: p.Curve is { IsLinear: false } c ? c : null)))
        {
            Range = range ?? PathRange.Laser,
        };
}
