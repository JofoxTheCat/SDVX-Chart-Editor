namespace SDVXEditor.Core.Charts;

/// <summary>
/// The link between a <see cref="LaserSection"/> and its editor-side <see cref="LaserPath"/>.
/// <list type="bullet">
/// <item>A section carries a path only when the path adds something over plain points (a curve type or an auto
/// flag, see <see cref="LaserPath.IsTrivial"/>); its points are then the bake (<see cref="LaserBakeOptions.Kson"/>).</item>
/// <item>The path is trusted only while the points are still exactly what it was baked to (a fingerprint). Anything
/// that edits points directly (the old point tools, timing remaps, slam toggles, reverse) therefore makes the path
/// stale; the section then falls back to "every point is a straight anchor" — the shape is kept, the curve types
/// are not.</item>
/// <item>A section without a (valid) path has derived anchors: one straight anchor per point, kson curves kept.</item>
/// </list>
/// </summary>
public static class LaserPaths
{
    /// <summary>The section's stored path if it still matches the points, else null.</summary>
    public static LaserPath? ValidPath(LaserSection s)
    {
        var path = s.Path;
        if (path == null || path.Count < 2) return null;
        path.Signature ??= Fingerprint(LaserPathBaker.Bake(path, s.Y, LaserBakeOptions.Kson));
        return path.Signature == Fingerprint(s.Points) ? path : null;
    }

    /// <summary>True when the section has a valid path with curve types (anchors differ from points).</summary>
    public static bool HasCurvedPath(LaserSection s) => ValidPath(s) is { IsTrivial: false };

    /// <summary>A copy of the anchors to edit: the valid path, else one straight anchor per point.</summary>
    public static LaserPath AnchorsOf(LaserSection s) => ValidPath(s)?.Clone() ?? LaserPath.FromPoints(s.Points);

    /// <summary>
    /// A new section from a path: normalised (sorted, typed, auto types), rebased so the first anchor is at the
    /// section start, baked, and carrying the path unless it is trivial.
    /// </summary>
    public static LaserSection ToSection(LaserPath path, long y, int width)
    {
        var p = path.Clone();
        p.Normalize();
        if (p.Count > 0 && p.Anchors[0].Ry != 0)
        {
            long d = p.Anchors[0].Ry;
            y += d;
            foreach (var a in p.Anchors) a.Ry -= d;
        }

        var section = new LaserSection(y, LaserPathBaker.Bake(p, y, LaserBakeOptions.Kson), width);
        if (!p.IsTrivial)
        {
            p.Signature = Fingerprint(section.Points);
            section.Path = p;
        }

        return section;
    }

    /// <summary>
    /// Points to write for a format: a curved path baked with <paramref name="options"/> (e.g. no kson curves for ksh),
    /// otherwise the section's own points.
    /// </summary>
    public static List<LaserPoint> PointsFor(LaserSection s, LaserBakeOptions options) =>
        ValidPath(s) is { IsTrivial: false } path && !ReferenceEquals(options, LaserBakeOptions.Kson)
            ? LaserPathBaker.Bake(path, s.Y, options)
            : s.Points;

    /// <summary>
    /// Attaches a path read from a file when its bake matches the section's points (values within 1e-6, same pulses,
    /// same curves); returns false (and attaches nothing) otherwise — the points were edited elsewhere.
    /// </summary>
    public static bool TryAttach(LaserSection s, LaserPath path)
    {
        if (path.Count < 2) return false;
        var p = path.Clone();
        p.Normalize();
        var baked = LaserPathBaker.Bake(p, s.Y, LaserBakeOptions.Kson);
        if (!SamePoints(baked, s.Points, 1e-6)) return false;
        p.Signature = Fingerprint(s.Points);
        s.Path = p;
        return true;
    }

    public static bool SamePoints(IReadOnlyList<LaserPoint> a, IReadOnlyList<LaserPoint> b, double eps)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            var p = a[i];
            var q = b[i];
            if (p.Ry != q.Ry || Math.Abs(p.V - q.V) > eps) return false;
            if (p.IsSlam != q.IsSlam || (p.IsSlam && Math.Abs(p.Vf!.Value - q.Vf!.Value) > eps)) return false;
            bool pc = p.Curve is { IsLinear: false }, qc = q.Curve is { IsLinear: false };
            if (pc != qc) return false;
            if (pc && (Math.Abs(p.Curve!.Value.A - q.Curve!.Value.A) > eps || Math.Abs(p.Curve.Value.B - q.Curve.Value.B) > eps)) return false;
        }

        return true;
    }

    /// <summary>In-process fingerprint of a point list (values rounded to 1e-7).</summary>
    public static int Fingerprint(IReadOnlyList<LaserPoint> points)
    {
        var h = new HashCode();
        h.Add(points.Count);
        foreach (var p in points)
        {
            h.Add(p.Ry);
            h.Add(Math.Round(p.V * 1e7));
            h.Add(p.IsSlam ? Math.Round(p.Vf!.Value * 1e7) : -1);
            if (p.Curve is { IsLinear: false } c)
            {
                h.Add(Math.Round(c.A * 1e7));
                h.Add(Math.Round(c.B * 1e7));
            }
        }

        return h.ToHashCode();
    }
}
