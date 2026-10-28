using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Pure anchor edits on a <see cref="LaserPath"/> (osu!lazer path editing for laser sections). Each returns a NEW path
/// (the input is never modified) or null when the edit is not possible. <see cref="LaserPathCommands"/> wraps them into
/// undoable commands; <see cref="LaserPaths.ToSection"/> bakes the result.
/// </summary>
public static class LaserPathEditing
{
    /// <summary>Placement: what a click on an existing anchor cycles through.</summary>
    /// <remarks>
    /// Inner anchors: inherit → auto segment start (typed by count; two curves meet at Bezier) → straight → perfect curve
    /// → Bezier → inherit. The first anchor and slam anchors always start a segment: auto → straight → perfect → Bezier → auto.
    /// </remarks>
    public static LaserPath CycleType(LaserPath path, int index)
    {
        var p = path.Clone();
        var a = p.Anchors[index];
        bool mustStart = index == 0 || a.IsSlam;

        if (a.Type == null) { a.Type = LaserCurveType.Linear; a.Auto = true; }
        else if (a.Auto) { a.Type = LaserCurveType.Linear; a.Auto = false; }
        else if (a.Type == LaserCurveType.Linear) a.Type = LaserCurveType.PerfectCurve;
        else if (a.Type == LaserCurveType.PerfectCurve) a.Type = LaserCurveType.Bezier;
        else if (mustStart) { a.Type = LaserCurveType.Linear; a.Auto = true; }
        else { a.Type = null; a.Auto = false; }

        p.Normalize();
        return p;
    }

    /// <summary>
    /// Sets an anchor's type explicitly (the context menu); null = inherit, refused for the first anchor and slams.
    /// Setting a type on an inner anchor splits its segment there; inherit merges it back.
    /// </summary>
    public static LaserPath? SetType(LaserPath path, int index, LaserCurveType? type)
    {
        if (index < 0 || index >= path.Count) return null;
        var p = path.Clone();
        var a = p.Anchors[index];
        if (type == null && (index == 0 || a.IsSlam)) return null;
        a.Type = type;
        a.Auto = false;
        p.Normalize();
        return p;
    }

    /// <summary>Sets the type of the segment containing anchor <paramref name="index"/> (on its start anchor).</summary>
    public static LaserPath? SetSegmentType(LaserPath path, int index, LaserCurveType type)
    {
        if (path.SegmentOf(index) is not { } s) return null;
        return SetType(path, s.Start, type);
    }

    /// <summary>
    /// Placement click at (<paramref name="ry"/>, <paramref name="v"/>): a new inner anchor in time order (before the
    /// first one it takes over the first anchor's type). On the last anchor's pulse with another value: that anchor
    /// becomes a slam. Null on any other existing pulse.
    /// </summary>
    public static LaserPath? AddAnchor(LaserPath path, long ry, double v)
    {
        var p = path.Clone();
        int same = p.Anchors.FindIndex(a => a.Ry == ry);
        if (same >= 0)
        {
            var a = p.Anchors[same];
            if (same != p.Count - 1 || a.V == v || a.IsSlam) return null;
            a.Vf = v;
            p.Normalize();
            return p;
        }

        var added = new LaserAnchor(ry, v);
        int i = p.Anchors.FindLastIndex(a => a.Ry < ry) + 1;
        if (i == 0 && p.Count > 0)
        {
            var first = p.Anchors[0];
            added.Type = first.Type;
            added.Auto = first.Auto;
            if (first.Auto && !first.IsSlam) { first.Type = null; first.Auto = false; }
        }

        p.Anchors.Insert(i, added);
        p.Normalize();
        return p;
    }

    /// <summary>
    /// Ctrl+click on the path: an inherit anchor strictly inside the path at a free pulse. A perfect curve that now has
    /// four anchors becomes a Bezier (osu!lazer keeps path types valid the same way).
    /// </summary>
    public static (LaserPath Path, int Index)? InsertAnchor(LaserPath path, long ry, double v)
    {
        if (path.Count < 2 || ry <= path.Anchors[0].Ry || ry >= path.Anchors[^1].Ry || path.Anchors.Any(a => a.Ry == ry)) return null;
        var p = path.Clone();
        int i = p.Anchors.FindLastIndex(a => a.Ry < ry) + 1;
        p.Anchors.Insert(i, new LaserAnchor(ry, v));
        if (p.SegmentOf(i) is { } s && p.Anchors[s.Start] is { Auto: false, Type: LaserCurveType.PerfectCurve } start && s.Count > 3)
            start.Type = LaserCurveType.Bezier;
        p.Normalize();
        return (p, i);
    }

    /// <summary>
    /// True when a placement click at (<paramref name="ry"/>, <paramref name="v"/>) makes a slam: the last anchor's pulse,
    /// another value, and that anchor is not a slam yet. The laser tool then makes the slam even when the pointer is still
    /// over the last anchor's handle (a small slam lies within the handle's hit radius).
    /// </summary>
    public static bool IsSlamClick(LaserPath path, long ry, double v) =>
        path.Count > 0 && path.Anchors[^1] is { IsSlam: false } last && last.Ry == ry && last.V != path.Range.Limit(v);

    /// <summary>
    /// Moves anchor <paramref name="index"/> to (<paramref name="ry"/>, <paramref name="v"/>); the pulse is clamped strictly
    /// between the neighbours (anchors never share a pulse; slams are one anchor with two ends). The value is the start
    /// value <c>V</c>, or with <paramref name="slamEnd"/> on a slam anchor its end value <c>Vf</c> (equal values remove the
    /// slam); either end moves the slam in time, since both ends sit on one pulse. Moving the first anchor moves the section
    /// start (<see cref="LaserPaths.ToSection"/> rebases).
    /// <para>
    /// With <paramref name="controlLimits"/> a Bezier control anchor (<see cref="LaserPath.IsControl"/>) moves freely: its
    /// value within those limits (also outside the laser range — the bake clamps the curve), its pulse anywhere strictly
    /// inside its segment, in any order with the other controls. Without, it is clamped like any anchor (camera lanes).
    /// </para>
    /// </summary>
    public static LaserPath? MoveAnchor(LaserPath path, int index, long ry, double v, bool slamEnd = false, (double Min, double Max)? controlLimits = null)
    {
        if (index < 0 || index >= path.Count) return null;
        var p = path.Clone();
        var a = p.Anchors[index];

        if (controlLimits is { } limits && p.IsControl(index) && p.SegmentOf(index) is { } seg)
        {
            long from = p.Anchors[seg.Start].Ry + 1, to = p.Anchors[seg.End].Ry - 1;
            if (from > to) return null;
            a.Ry = Math.Clamp(ry, from, to);
            a.V = Math.Clamp(v, Math.Min(limits.Min, p.Range.Min), Math.Max(limits.Max, p.Range.Max));
            p.Normalize();
            return p;
        }

        v = p.Range.Limit(v);

        long lo = index > 0 ? p.Anchors[index - 1].Ry + 1 : long.MinValue;
        long hi = index < p.Count - 1 ? p.Anchors[index + 1].Ry - 1 : long.MaxValue;
        // free controls may lie anywhere inside their segment: an end anchor stays beyond all of them and its other neighbour
        if (controlLimits != null)
        {
            for (int k = index - 1; k >= 0; k--)
            {
                lo = Math.Max(lo, p.Anchors[k].Ry + 1);
                if (!p.IsControl(k)) break;
            }

            for (int k = index + 1; k < p.Count; k++)
            {
                hi = Math.Min(hi, p.Anchors[k].Ry - 1);
                if (!p.IsControl(k)) break;
            }
        }

        if (lo > hi) return null;
        a.Ry = Math.Clamp(ry, lo, hi);

        if (slamEnd && a.IsSlam)
        {
            a.Vf = v == a.V ? null : v;
        }
        else
        {
            a.V = v;
            if (a.Vf is double f && f == v) a.Vf = null;
        }

        p.Normalize();
        return p;
    }

    /// <summary>
    /// Removes one end of the slam at anchor <paramref name="index"/>: the start end (<paramref name="end"/> false) leaves a
    /// plain anchor at the end value, the end leaves one at the start value. The anchor keeps its type (a slam anchor is
    /// always a segment start). Null when the anchor is no slam.
    /// </summary>
    public static LaserPath? DeleteSlamEnd(LaserPath path, int index, bool end)
    {
        if (index < 0 || index >= path.Count || !path.Anchors[index].IsSlam) return null;
        var p = path.Clone();
        var a = p.Anchors[index];
        if (!end) a.V = a.Vf!.Value;
        a.Vf = null;
        p.Normalize();
        return p;
    }

    /// <summary>Removes an anchor; null when fewer than two would remain (delete the section instead).</summary>
    public static LaserPath? DeleteAnchor(LaserPath path, int index)
    {
        if (index < 0 || index >= path.Count || path.Count <= 2) return null;
        var p = path.Clone();
        var removed = p.Anchors[index];
        p.Anchors.RemoveAt(index);
        if (index == 0)
        {
            var next = p.Anchors[0];
            if (next.Type == null)
            {
                next.Type = removed.Type;
                next.Auto = removed.Auto;
            }
        }

        p.Normalize();
        return p;
    }
}

/// <summary>
/// Undoable anchor edits on a section in the chart: each replaces the section with a new one baked from the edited path
/// (<see cref="ReplaceLaserSectionsCommand"/>; undo puts the old section object back). Null when the edit is not
/// possible or would overlap another section of the lane.
/// </summary>
public static class LaserPathCommands
{
    public static ReplaceLaserSectionsCommand? Replace(Chart chart, int lane, LaserSection old, LaserPath? edited, string description)
    {
        if (edited == null || !LaserPath.IsSectionShape(edited)) return null;
        var section = LaserPaths.ToSection(edited, old.Y, old.Width);
        if (!OverlapRule.CanPlace(chart, new[] { (lane, section) }, new HashSet<LaserSection> { old })) return null;
        return new ReplaceLaserSectionsCommand(description, new[] { (lane, old, lane, section) });
    }

    public static ReplaceLaserSectionsCommand? SetType(Chart chart, int lane, LaserSection s, int anchor, LaserCurveType? type) =>
        Replace(chart, lane, s, LaserPathEditing.SetType(LaserPaths.AnchorsOf(s), anchor, type), "Change anchor type");

    public static ReplaceLaserSectionsCommand? SetSegmentType(Chart chart, int lane, LaserSection s, int anchor, LaserCurveType type) =>
        Replace(chart, lane, s, LaserPathEditing.SetSegmentType(LaserPaths.AnchorsOf(s), anchor, type), "Change curve type");

    public static ReplaceLaserSectionsCommand? Insert(Chart chart, int lane, LaserSection s, long pulse, double v) =>
        LaserPathEditing.InsertAnchor(LaserPaths.AnchorsOf(s), pulse - s.Y, v) is { } r ? Replace(chart, lane, s, r.Path, "Insert anchor") : null;

    public static ReplaceLaserSectionsCommand? Move(Chart chart, int lane, LaserSection s, int anchor, long pulse, double v, bool slamEnd = false) =>
        Replace(chart, lane, s, LaserPathEditing.MoveAnchor(LaserPaths.AnchorsOf(s), anchor, pulse - s.Y, v, slamEnd), "Move anchor");

    /// <summary>
    /// Deletes one end of a slam anchor (the slam becomes a plain anchor at the other end's value). On a lone-slam section
    /// that would leave a single plain point — no valid laser — so the whole section goes.
    /// </summary>
    public static IEditCommand? DeleteSlamEnd(Chart chart, int lane, LaserSection s, int anchor, bool end)
    {
        var edited = LaserPathEditing.DeleteSlamEnd(LaserPaths.AnchorsOf(s), anchor, end);
        if (edited is { Count: 1 } && !edited.Anchors[0].IsSlam) return new RemoveLaserSectionCommand(lane, s);
        return Replace(chart, lane, s, edited, end ? "Delete slam end" : "Delete slam start");
    }

    /// <summary>Deletes an anchor; with only two anchors the whole section goes.</summary>
    public static IEditCommand? Delete(Chart chart, int lane, LaserSection s, int anchor)
    {
        var anchors = LaserPaths.AnchorsOf(s);
        // two anchors, or a lone slam (one anchor): the whole section goes
        if (anchors.Count <= 2) return new RemoveLaserSectionCommand(lane, s);
        return Replace(chart, lane, s, LaserPathEditing.DeleteAnchor(anchors, anchor), "Delete anchor");
    }
}
