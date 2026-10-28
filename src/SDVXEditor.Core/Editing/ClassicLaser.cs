using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Classic laser placement, the K-Shoot / kson editor way (Drewol's kson-editor, <c>editor/src/tools/laser.rs</c>): every
/// click puts a plain point on the snapped beat, segments are straight lines, the value lies on a coarse grid (tenths,
/// twentieths for a wide laser), a second click on the last point's beat with another value is a slam, and a click on the
/// last point again (same beat and value, or the same beat after a slam) finishes the section. The editor uses it while
/// the classic-laser key is held (compose tab, laser tool).
/// <para>
/// Classic points are anchors like any other: plain straight anchors (<see cref="LaserCurveType.Linear"/>), so classic
/// and osu!-style anchors mix in one laser, and an all-classic laser is a trivial path = plain points (export identical
/// to a laser drawn point by point). The last classic anchor of a draft stays an auto segment start, so clicks that
/// follow without the key are typed by count from there (2 straight, 3 perfect curve, 4+ Bezier).
/// </para>
/// </summary>
public static class ClassicLaser
{
    /// <summary>Value steps of a normal laser (kson-editor: <c>resolution = 10 * wide</c>).</summary>
    public const int VALUE_STEPS = 10;

    /// <summary>
    /// The value on the classic grid: 0.1 steps, 0.05 for a wide laser. kson-editor floors the cursor position onto the
    /// grid; this rounds to the nearest step, so the point lands where the cursor ghost shows it.
    /// </summary>
    public static double Snap(double v, bool wide)
    {
        int steps = wide ? 2 * VALUE_STEPS : VALUE_STEPS;
        return Math.Clamp(Math.Round(v * steps) / steps, 0, 1);
    }

    /// <summary>A click on the draft's last point again: same beat and value, or the same beat after a slam. Finishes the section.</summary>
    public static bool IsFinishClick(LaserPath path, long ry, double v) =>
        path.Count > 0 && path.Anchors[^1] is var last && last.Ry == ry && (last.IsSlam || last.V == path.Range.Limit(v));

    /// <summary>
    /// A classic click while placing: a slam of the last anchor (its beat, another value), else a plain straight point in
    /// time order. The point before it becomes an explicit straight segment start, so the segment between them is a
    /// straight line whatever was placed before; the point after an inserted one too. The new point is an auto segment
    /// start when it is the last one (anchor clicks may follow). Null when another anchor already sits on that pulse.
    /// </summary>
    public static LaserPath? Add(LaserPath path, long ry, double v)
    {
        if (LaserPathEditing.AddAnchor(path, ry, v) is not { } p) return null;
        if (LaserPathEditing.IsSlamClick(path, ry, v)) return p;

        int i = p.Anchors.FindIndex(a => a.Ry == ry);
        var added = p.Anchors[i];
        bool last = i == p.Count - 1;
        // before the first point AddAnchor handed the first point's type to the new one: give it back
        if (i == 0 && !last && p.Anchors[1].Type == null)
        {
            p.Anchors[1].Type = added.Type;
            p.Anchors[1].Auto = added.Auto;
        }

        added.Type = LaserCurveType.Linear;
        added.Auto = last;
        if (i > 0) straight(p.Anchors[i - 1]);
        if (!last) straight(p.Anchors[i + 1], inheritOnly: true);
        p.Normalize();
        return p;

        static void straight(LaserAnchor a, bool inheritOnly = false)
        {
            if (a.Type != null && (inheritOnly || !a.Auto)) return;
            a.Type = LaserCurveType.Linear;
            a.Auto = false;
        }
    }
}
