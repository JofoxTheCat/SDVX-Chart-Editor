using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Pure laser-shape edits used by the compose tools. Each returns a new section (the input is never
/// modified), to be put into the chart with a <see cref="ReplaceLaserSectionsCommand"/> or
/// <see cref="AddLaserSectionCommand"/>.
/// </summary>
public static class LaserEditing
{
    /// <summary>
    /// A new section from (<paramref name="y0"/>, <paramref name="v0"/>) to (<paramref name="y1"/>, <paramref name="v1"/>),
    /// in either order. Same pulse with different values = a lone slam: ONE point <c>V → Vf</c>, no tail — official charts
    /// end sections on a slam and have 118,940 sections that are nothing but one slam (all 8,602 official .vox,
    /// 2026-10-27; 0.1.0 gave such a slam a one-snap-tick tail). Same pulse and value = null.
    /// </summary>
    public static LaserSection? NewSection(long y0, double v0, long y1, double v1, int width)
    {
        if (y1 < y0) (y0, v0, y1, v1) = (y1, v1, y0, v0);

        if (y1 == y0)
        {
            if (v0 == v1) return null;
            return new LaserSection(y0, new[] { new LaserPoint(0, v0, v1) }, width);
        }

        return new LaserSection(y0, new[] { new LaserPoint(0, v0), new LaserPoint(y1 - y0, v1) }, width);
    }

    /// <summary>
    /// A copy of <paramref name="s"/> with point <paramref name="index"/> moved to (<paramref name="pulse"/>,
    /// <paramref name="value"/>). The pulse is clamped between the neighbouring points (and to ≥ 0); landing on a
    /// neighbour's pulse merges the two into one slam that jumps from the earlier point's V to the later point's
    /// value-after (the later point's curve is kept). A slam whose two values become equal loses its jump.
    /// With <paramref name="slamEnd"/> on a slam point only the end value (Vf) changes. Moving the first point
    /// moves the section start; relative pulses are rebased.
    /// </summary>
    public static LaserSection? MovePoint(LaserSection s, int index, long pulse, double value, bool slamEnd = false)
    {
        if (index < 0 || index >= s.Points.Count) return null;

        var abs = s.Points.Select(pt => s.Y + pt.Ry).ToList();
        var pts = s.Points.Select(pt => pt.Clone()).ToList();
        var point = pts[index];

        if (slamEnd && point.IsSlam)
        {
            point.Vf = value == point.V ? null : value;
        }
        else
        {
            long lo = index > 0 ? abs[index - 1] : 0;
            long hi = index < pts.Count - 1 ? abs[index + 1] : long.MaxValue;
            pulse = Math.Clamp(pulse, lo, hi);
            abs[index] = pulse;
            point.V = value;
            if (point.Vf is double f && f == value) point.Vf = null;

            int merge = index > 0 && pulse == abs[index - 1] ? index - 1
                : index < pts.Count - 1 && pulse == abs[index + 1] ? index : -1;

            if (merge >= 0)
            {
                var a = pts[merge];
                var b = pts[merge + 1];
                double after = b.ValueAfter;
                pts[merge] = new LaserPoint(0, a.V, after == a.V ? null : after, b.Curve);
                pts.RemoveAt(merge + 1);
                abs.RemoveAt(merge + 1);
            }
        }

        long y = abs[0];
        for (int i = 0; i < pts.Count; i++) pts[i].Ry = abs[i] - y;
        return new LaserSection(y, pts, s.Width);
    }

    /// <summary>
    /// The section at another width (1 = normal, 2 = wide), every point / anchor keeping its place on screen: a normal
    /// value v sits where the wide value 0.25 + v / 2 does. Widening never clamps; narrowing clamps values that lie
    /// outside the normal range (beyond the lanes) to 0 / 1 and counts them in <c>Clamped</c> (a slam whose ends then
    /// meet loses its jump). Curved paths re-bake their remapped anchors (Bezier controls are moved, not clamped); kson
    /// ease curves stay (they are relative to their segment). The same section when the width is already right.
    /// <para>
    /// <paramref name="keepPosition"/> false = keep the values (the laser stretches / shrinks around the centre, the path
    /// and its types stay untouched) — K-Shoot Editor 2.2's two variants "Toggle Wide (Keep Position)" / "(Keep Values)".
    /// </para>
    /// </summary>
    public static (LaserSection Section, int Clamped) WithWidth(LaserSection s, int width, bool keepPosition = true)
    {
        if (s.Width == width) return (s, 0);
        if (!keepPosition)
        {
            var same = s.Clone();
            same.Width = width;
            return (same, 0);
        }

        int clamped = 0;
        double map(double v, bool clamp = true)
        {
            double r = width == 2 ? 0.25 + v / 2 : (v - 0.25) * 2;
            if (!clamp) return Math.Round(r, 12);
            double c = Math.Clamp(r, 0, 1);
            if (Math.Abs(c - r) > 1e-9) clamped++;
            return Math.Round(c, 12);
        }

        if (LaserPaths.ValidPath(s) is { IsTrivial: false } path)
        {
            var p = path.Clone();
            for (int i = 0; i < p.Count; i++)
            {
                var a = p.Anchors[i];
                bool control = p.IsControl(i);
                a.V = map(a.V, !control);
                if (a.Vf is double f) a.Vf = map(f);
                if (a.Vf == a.V) a.Vf = null;
            }

            return (LaserPaths.ToSection(p, s.Y, width), clamped);
        }

        var pts = s.Points.Select(pt =>
        {
            double v = map(pt.V);
            double? vf = pt.Vf is double f ? map(f) : null;
            return new LaserPoint(pt.Ry, v, vf == v ? null : vf, pt.Curve);
        });
        return (new LaserSection(s.Y, pts, width), clamped);
    }

    /// <summary>Inserts a point at an absolute pulse inside the section; null if a point already sits there or the pulse is outside.</summary>
    public static (LaserSection Section, int Index)? InsertPoint(LaserSection s, long pulse, double value)
    {
        if (pulse < s.Y || pulse > s.EndY || s.Points.Any(p => s.Y + p.Ry == pulse)) return null;

        var copy = s.Clone();
        long ry = pulse - copy.Y;
        int index = copy.Points.FindLastIndex(p => p.Ry < ry) + 1;
        copy.Points.Insert(index, new LaserPoint(ry, value));
        return (copy, index);
    }
}
