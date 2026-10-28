namespace SDVXEditor.Core.Charts;

/// <summary>One segment of an evaluated path: a polyline strictly increasing in time (pulses relative to the section).</summary>
public sealed class LaserPiece
{
    public LaserPiece(LaserSegment segment, double[] t, double[] v)
    {
        Segment = segment;
        T = t;
        V = v;
    }

    public LaserSegment Segment { get; }

    /// <summary>Relative pulses, strictly increasing, first = start anchor, last = end anchor.</summary>
    public double[] T { get; }

    public double[] V { get; }

    /// <summary>Value at a relative pulse inside the piece (linear between the dense samples).</summary>
    public double ValueAt(double ry)
    {
        if (ry <= T[0]) return V[0];
        if (ry >= T[^1]) return V[^1];
        int lo = 0, hi = T.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (T[mid] <= ry) lo = mid;
            else hi = mid;
        }

        double f = (ry - T[lo]) / (T[hi] - T[lo]);
        return V[lo] + (V[hi] - V[lo]) * f;
    }
}

/// <summary>How a path is turned into chart points (<see cref="LaserPathBaker.Bake"/>).</summary>
/// <param name="KsonCurves">A Bezier segment of exactly three anchors becomes one point with a kson curve <c>[a, b]</c> (exact).</param>
/// <param name="Grid">Sampled points lie on absolute multiples of this many pulses.</param>
/// <param name="MinGap">No sampled point closer than this to an anchor.</param>
/// <param name="MinGapAfterSlam">No sampled point closer than this after a slam anchor (ksh writes a slam's second line up to 1/32 later).</param>
/// <param name="Tolerance">Maximum value error (0..1 laser range) of the sampled polyline against the path.</param>
public sealed record LaserBakeOptions(bool KsonCurves, long Grid, long MinGap, long MinGapAfterSlam, double Tolerance)
{
    /// <summary>Error budget: a fifth of a ksh value step (1/50).</summary>
    public const double TOLERANCE = 0.004;

    /// <summary>
    /// For the chart / kson / VOX: kson curves where exact; samples on the VOX tick grid (5 pulses = 1/192, so ksh2vox
    /// maps every sample onto its own tick), kept only where needed for <see cref="TOLERANCE"/> — on smooth curves that
    /// is far coarser (official charts bake curves every 3 ticks), near a steep arc end it may go down to one tick.
    /// </summary>
    public static readonly LaserBakeOptions Kson = new(true, 5, 5, 5, TOLERANCE);

    /// <summary>
    /// For ksh: no curves (ksh2vox treats a ksh curve as already baked by K-Shoot), samples on a 1/24 grid and more
    /// than 1/32 from every anchor (more than 2/32 after a slam), because two ksh points within 1/32 read as a slam.
    /// </summary>
    public static readonly LaserBakeOptions Ksh = new(false, 40, Timebase.SlamMaxPulses + 1, 2 * Timebase.SlamMaxPulses + 2, TOLERANCE);
}

/// <summary>
/// Evaluates a <see cref="LaserPath"/> to a value-over-time function and bakes it to <see cref="LaserPoint"/>s.
/// <para>
/// Geometry: curves are built in a plane where one laser width (value 0 → 1) equals <see cref="TIME_SCALE"/> pulses
/// (half a 4/4 measure), so a perfect curve is a true circle there.
/// </para>
/// <para>
/// Monotone rule: a laser is a function of time. Each segment is sampled densely; a sample is kept only when it lies
/// strictly after the previously kept one and strictly before the segment's end anchor, the end anchor is always
/// kept, values are limited by the path's <see cref="PathRange"/> (lasers: clamped to 0..1). Where a curve turns back in time (an arc bulging past its anchors) the kept
/// samples are joined by a straight line from the last sample that moved forward to the next one that is ahead
/// again — the backward loop is cut off, the laser stays continuous and never needs a slam.
/// </para>
/// </summary>
public static class LaserPathBaker
{
    /// <summary>Pulses per laser width in the curve plane.</summary>
    public const double TIME_SCALE = 480;

    // ---- evaluation --------------------------------------------------------

    /// <summary>The time-monotone pieces of the path, one per segment.</summary>
    public static List<LaserPiece> Evaluate(LaserPath path)
    {
        var pieces = new List<LaserPiece>();
        foreach (var s in path.Segments())
            pieces.Add(EvaluateSegment(path, s));
        return pieces;
    }

    /// <summary>Value at a pulse relative to the section start (after a slam on that pulse); null outside the path.</summary>
    public static double? ValueAt(LaserPath path, double ry) => ValueAt(Evaluate(path), path, ry);

    public static double? ValueAt(IReadOnlyList<LaserPiece> pieces, LaserPath path, double ry)
    {
        if (path.Count == 0 || ry < path.Anchors[0].Ry || ry > path.Anchors[^1].Ry) return null;
        if (pieces.Count == 0) return path.Anchors[0].ValueAfter;
        for (int i = pieces.Count - 1; i >= 0; i--)
        {
            if (ry < pieces[i].T[0]) continue;
            if (ry == pieces[i].T[^1] && i == pieces.Count - 1) return path.Anchors[^1].ValueAfter;
            return pieces[i].ValueAt(ry);
        }

        return pieces[0].V[0];
    }

    private static LaserPiece EvaluateSegment(LaserPath path, LaserSegment s)
    {
        var anchors = path.Anchors;
        var range = path.Range;
        int n = s.Count;
        var x = new double[n];
        var y = new double[n];
        for (int k = 0; k < n; k++)
        {
            var a = anchors[s.Start + k];
            x[k] = a.Ry / TIME_SCALE;
            y[k] = range.ToUnit(k == 0 ? a.ValueAfter : a.V);
        }

        List<(double X, double Y)> samples;
        switch (s.Effective)
        {
            case LaserCurveType.PerfectCurve:
                samples = Arc(x, y) ?? Bezier(x, y);
                break;

            case LaserCurveType.Bezier:
                samples = Bezier(x, y);
                break;

            default:
                samples = new List<(double, double)>();
                for (int k = 0; k < n; k++) samples.Add((x[k], y[k]));
                if (n == 2 && anchors[s.Start].Ease is { IsLinear: false } ease)
                {
                    samples.Clear();
                    const int steps = 48;
                    for (int k = 0; k <= steps; k++)
                    {
                        double f = (double)k / steps;
                        samples.Add((x[0] + (x[1] - x[0]) * f, y[0] + (y[1] - y[0]) * ease.Apply(f)));
                    }
                }
                break;
        }

        // monotone in time: keep strictly increasing samples before the end anchor, then the end anchor
        double tStart = anchors[s.Start].Ry, tEnd = anchors[s.End].Ry;
        double value(double u) => range.Limit(range.FromUnit(u));
        var t = new List<double> { tStart };
        var v = new List<double> { value(y[0]) };
        for (int k = 1; k < samples.Count - 1; k++)
        {
            double tk = samples[k].X * TIME_SCALE;
            if (tk > t[^1] + 1e-9 && tk < tEnd - 1e-9)
            {
                t.Add(tk);
                v.Add(value(samples[k].Y));
            }
        }

        t.Add(tEnd);
        v.Add(value(y[^1]));
        return new LaserPiece(s, t.ToArray(), v.ToArray());
    }

    private static List<(double X, double Y)> Bezier(double[] x, double[] y)
    {
        int n = x.Length;
        int steps = Math.Clamp(32 * (n - 1), 32, 512);
        var result = new List<(double, double)>(steps + 1);
        var bx = new double[n];
        var by = new double[n];
        for (int k = 0; k <= steps; k++)
        {
            double u = (double)k / steps;
            Array.Copy(x, bx, n);
            Array.Copy(y, by, n);
            for (int r = n - 1; r > 0; r--)
            {
                for (int i = 0; i < r; i++)
                {
                    bx[i] += (bx[i + 1] - bx[i]) * u;
                    by[i] += (by[i + 1] - by[i]) * u;
                }
            }

            result.Add((bx[0], by[0]));
        }

        result[0] = (x[0], y[0]);
        result[^1] = (x[^1], y[^1]);
        return result;
    }

    /// <summary>Circle arc from the first through the second to the third point; null when they are (nearly) collinear.</summary>
    private static List<(double X, double Y)>? Arc(double[] x, double[] y)
    {
        double ax = x[0], ay = y[0], bx = x[1], by = y[1], cx = x[2], cy = y[2];
        double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
        double cross = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (Math.Abs(d) < 1e-9 || Math.Abs(cross) < 1e-9) return null;

        double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
        double ux = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
        double uy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
        double r = Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));
        if (r > 1e6) return null;

        double ta = Math.Atan2(ay - uy, ax - ux), tc = Math.Atan2(cy - uy, cx - ux);
        double end = tc;
        if (cross > 0)
            while (end < ta) end += 2 * Math.PI;
        else
            while (end > ta) end -= 2 * Math.PI;

        double sweep = end - ta;
        int steps = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / (2 * Math.PI) * 512), 32, 512);
        var result = new List<(double, double)>(steps + 1);
        for (int k = 0; k <= steps; k++)
        {
            double th = ta + sweep * k / steps;
            result.Add((ux + r * Math.Cos(th), uy + r * Math.Sin(th)));
        }

        result[0] = (ax, ay);
        result[^1] = (cx, cy);
        return result;
    }

    // ---- baking ------------------------------------------------------------

    /// <summary>
    /// Chart points for a path whose section starts at <paramref name="sectionY"/>:
    /// <list type="bullet">
    /// <item>straight segments: one point per anchor (exact; a kept ease curve stays on its point);</item>
    /// <item>a three-anchor Bezier with <see cref="LaserBakeOptions.KsonCurves"/>: one point with kson curve
    /// <c>a = (t1−t0)/(t2−t0)</c>, <c>b = (v1−v0)/(v2−v0)</c> — the kson curve IS that quadratic Bezier (exact, needs v0 ≠ v2);</item>
    /// <item>every other curve: the start anchor plus grid samples chosen Douglas–Peucker style until the polyline is
    /// within <see cref="LaserBakeOptions.Tolerance"/> of the path everywhere (limited only by the grid);</item>
    /// <item>slams stay on their anchor (<c>V → Vf</c>) and end/start segments; the last anchor closes the section.</item>
    /// </list>
    /// </summary>
    public static List<LaserPoint> Bake(LaserPath path, long sectionY, LaserBakeOptions options)
    {
        var result = new List<LaserPoint>();
        var anchors = path.Anchors;
        if (anchors.Count == 0) return result;

        foreach (var piece in Evaluate(path))
        {
            var s = piece.Segment;
            var a = anchors[s.Start];
            var b = anchors[s.End];

            switch (s.Effective)
            {
                case LaserCurveType.Linear:
                    for (int k = s.Start; k < s.End; k++)
                    {
                        var p = anchors[k];
                        result.Add(new LaserPoint(p.Ry, p.V, p.IsSlam ? p.Vf : null, s.Count == 2 && p.Ease is { IsLinear: false } e ? e : null));
                    }
                    break;

                default:
                    if (options.KsonCurves && s.Effective == LaserCurveType.Bezier && s.Count == 3 && KsonCurveOf(path, s) is { } curve)
                    {
                        result.Add(new LaserPoint(a.Ry, a.V, a.IsSlam ? a.Vf : null, curve));
                        break;
                    }

                    result.Add(new LaserPoint(a.Ry, a.V, a.IsSlam ? a.Vf : null));
                    foreach (var (ry, v) in Sample(piece, sectionY, a.IsSlam, options))
                        result.Add(new LaserPoint(ry, v));
                    break;
            }
        }

        var last = anchors[^1];
        if (anchors.Count == 1 || result.Count == 0 || result[^1].Ry != last.Ry)
            result.Add(new LaserPoint(last.Ry, last.V, last.IsSlam ? last.Vf : null));
        return result;
    }

    /// <summary>The kson curve equal to a three-anchor Bezier segment, or null when it cannot be expressed (flat ends, no time between).</summary>
    public static Curve? KsonCurveOf(LaserPath path, LaserSegment s)
    {
        if (s.Count != 3) return null;
        var p0 = path.Anchors[s.Start];
        var p1 = path.Anchors[s.Start + 1];
        var p2 = path.Anchors[s.End];
        double t0 = p0.Ry, t1 = p1.Ry, t2 = p2.Ry;
        double v0 = p0.ValueAfter, v1 = p1.V, v2 = p2.V;
        if (!(t0 < t1 && t1 < t2) || Math.Abs(v2 - v0) < 1e-9) return null;
        // a free control anchor outside the value range would take the exact curve out of it: sampled (and clamped) instead
        if (v1 < path.Range.Min || v1 > path.Range.Max) return null;
        double ca = (t1 - t0) / (t2 - t0), cb = (v1 - v0) / (v2 - v0);
        if (ca == cb) return null; // straight: no curve needed, but then it was not a curve either
        return new Curve(ca, cb);
    }

    /// <summary>Douglas–Peucker over grid candidates: the sampled points (relative pulse, value) strictly inside the piece.</summary>
    private static IEnumerable<(long Ry, double V)> Sample(LaserPiece piece, long sectionY, bool afterSlam, LaserBakeOptions o)
    {
        double t0 = piece.T[0], t1 = piece.T[^1];
        long absFrom = sectionY + (long)Math.Ceiling(t0) + (afterSlam ? o.MinGapAfterSlam : o.MinGap);
        long absTo = sectionY + (long)Math.Floor(t1) - o.MinGap;

        var ct = new List<double> { t0 };
        var cv = new List<double> { piece.V[0] };
        for (long g = (long)Math.Ceiling((double)absFrom / o.Grid) * o.Grid; g <= absTo; g += o.Grid)
        {
            double ry = g - sectionY;
            ct.Add(ry);
            cv.Add(piece.ValueAt(ry));
        }

        ct.Add(t1);
        cv.Add(piece.V[^1]);
        if (ct.Count <= 2) yield break;

        var keep = new bool[ct.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, ct.Count - 1));
        while (stack.Count > 0)
        {
            var (i, j) = stack.Pop();
            if (j - i < 2) continue;

            double err = SpanError(piece, ct, cv, i, j, out int worst);
            if (err <= o.Tolerance) continue;
            keep[worst] = true;
            stack.Push((i, worst));
            stack.Push((worst, j));
        }

        for (int k = 1; k < ct.Count - 1; k++)
            if (keep[k]) yield return ((long)Math.Round(ct[k]), cv[k]);
    }

    /// <summary>Largest value error of the chord i → j against the piece; <paramref name="worst"/> = the candidate to split at.</summary>
    private static double SpanError(LaserPiece piece, List<double> ct, List<double> cv, int i, int j, out int worst)
    {
        double ta = ct[i], tb = ct[j], va = cv[i], vb = cv[j];
        double chord(double t) => va + (vb - va) * (t - ta) / (tb - ta);

        double max = 0;
        worst = (i + j) / 2;
        double worstCandidate = -1;
        for (int k = i + 1; k < j; k++)
        {
            double e = Math.Abs(chord(ct[k]) - cv[k]);
            if (e > worstCandidate)
            {
                worstCandidate = e;
                worst = k;
            }
            max = Math.Max(max, e);
        }

        for (int k = 0; k < piece.T.Length; k++)
        {
            double t = piece.T[k];
            if (t <= ta || t >= tb) continue;
            max = Math.Max(max, Math.Abs(chord(t) - piece.V[k]));
        }

        return max;
    }

    /// <summary>
    /// Largest value difference between the path and a point list (laser value interpolation incl. kson curves),
    /// measured at the path's dense samples and every 5 pulses. Used by the tests and the export check.
    /// </summary>
    public static double MaxError(LaserPath path, long sectionY, IReadOnlyList<LaserPoint> points, long pointsY)
    {
        var pieces = Evaluate(path);
        double max = 0;
        void check(double ry)
        {
            if (ValueAt(pieces, path, ry) is not double expected) return;
            if (PointsValueAt(points, sectionY + ry - pointsY) is not double actual) return;
            max = Math.Max(max, Math.Abs(expected - actual));
        }

        foreach (var p in pieces)
        {
            for (int k = 0; k < p.T.Length - 1; k++)
            {
                // inside the piece only (a slam boundary has two values on one pulse)
                check(p.T[k] + (k == 0 ? 0.5 : 0));
                check((p.T[k] + p.T[k + 1]) / 2);
            }
        }

        long end = path.Anchors[^1].Ry;
        for (long ry = path.Anchors[0].Ry + 1; ry < end; ry += 5)
        {
            if (path.Anchors.Any(a => a.Ry == ry)) continue;
            check(ry);
        }

        return max;
    }

    /// <summary>Value of a point list at a relative pulse (same rules as the renderer's <c>LaserMath</c>).</summary>
    public static double? PointsValueAt(IReadOnlyList<LaserPoint> pts, double ry)
    {
        if (pts.Count == 0 || ry < pts[0].Ry || ry > pts[^1].Ry) return null;
        for (int i = pts.Count - 1; i >= 0; i--)
        {
            if (pts[i].Ry > ry) continue;
            if (pts[i].Ry == ry || i == pts.Count - 1) return pts[i].ValueAfter;
            var p = pts[i];
            var q = pts[i + 1];
            double x = (ry - p.Ry) / (q.Ry - p.Ry);
            double f = p.Curve?.Apply(x) ?? x;
            return p.ValueAfter + (q.V - p.ValueAfter) * f;
        }

        return pts[0].V;
    }
}
