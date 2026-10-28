namespace SDVXEditor.Core.Charts;

/// <summary>
/// Segment shaping (kson <c>[a, b]</c>): a quadratic Bezier from (0,0) to (1,1) with control
/// point (a, b). Linear iff <c>a == b</c>.
/// </summary>
public readonly record struct Curve(double A, double B)
{
    public bool IsLinear => A == B;

    /// <summary>Maps a segment fraction x in [0,1] to an eased fraction y in [0,1].</summary>
    public double Apply(double x)
    {
        if (IsLinear) return x;
        x = Math.Clamp(x, 0, 1);
        // x(t) = 2a·t(1-t) + t²  →  t = x / (a + sqrt(a² + (1-2a)x)), the numerically stable root.
        double disc = A * A + (1 - 2 * A) * x;
        double den = A + Math.Sqrt(Math.Max(0, disc));
        double t = den > 1e-12 ? x / den : Math.Sqrt(x);
        return 2 * (1 - t) * t * B + t * t;
    }
}

/// <summary>
/// One point of a kson graph (<c>[y, v]</c>, <c>[y, [v, vf]]</c>, optional <c>[a, b]</c>).
/// <see cref="Vf"/> set = instant jump at <see cref="Y"/> from V to Vf. <see cref="Curve"/> shapes the
/// segment towards the next point.
/// </summary>
public sealed class GraphPoint
{
    public long Y { get; set; }
    public double V { get; set; }
    public double? Vf { get; set; }
    public Curve? Curve { get; set; }

    public GraphPoint() { }

    public GraphPoint(long y, double v, double? vf = null, Curve? curve = null)
    {
        Y = y;
        V = v;
        Vf = vf;
        Curve = curve;
    }

    /// <summary>Value right after <see cref="Y"/> (after an instant jump).</summary>
    public double ValueAfter => Vf ?? V;

    public GraphPoint Clone() => new(Y, V, Vf, Curve);
}

public static class Graph
{
    /// <summary>
    /// Evaluates a sorted graph at <paramref name="pulse"/>. Before the first point the first V
    /// applies, after the last its value-after; an empty graph yields <paramref name="defaultValue"/>.
    /// </summary>
    public static double ValueAt(IReadOnlyList<GraphPoint> points, double pulse, double defaultValue)
    {
        if (points.Count == 0) return defaultValue;
        if (pulse < points[0].Y) return points[0].V;

        int i = LastIndexAtOrBefore(points, pulse);
        var p = points[i];
        if (i == points.Count - 1 || pulse == p.Y) return p.ValueAfter;
        var q = points[i + 1];
        return Interpolate(p, q, pulse);
    }

    /// <summary>Value inside the segment p → q (p.Y &lt;= pulse &lt;= q.Y).</summary>
    public static double Interpolate(GraphPoint p, GraphPoint q, double pulse)
    {
        double len = q.Y - p.Y;
        if (len <= 0) return q.V;
        double x = (pulse - p.Y) / len;
        double f = p.Curve?.Apply(x) ?? x;
        return p.ValueAfter + (q.V - p.ValueAfter) * f;
    }

    internal static int LastIndexAtOrBefore(IReadOnlyList<GraphPoint> points, double pulse)
    {
        int lo = 0, hi = points.Count - 1, ans = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].Y <= pulse) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ans;
    }

    public static List<GraphPoint> CloneAll(IEnumerable<GraphPoint> points) => points.Select(p => p.Clone()).ToList();
}

/// <summary>A value at a pulse (kson <c>ByPulse&lt;T&gt;</c>).</summary>
public sealed record PulseValue<T>(long Y, T Value);
