using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// The inspector's laser part: the anchors behind the selected laser points / slam ends (a curved path is edited on its
/// anchors, never on baked samples) with exact position, start value (V), landing value (Vf, the slam's second end),
/// curve type and the section width; multi-selections show mixed values and an edit applies to every selected anchor.
/// Each edit is one command of section replacements (<see cref="LaserPathCommands.Replace"/>), so curve types stay.
/// </summary>
public static class LaserInspector
{
    /// <summary>One selected anchor: its section, lane and index into <see cref="LaserPaths.AnchorsOf"/>.</summary>
    public sealed record Target(int Lane, LaserSection Section, int Index, LaserAnchor Anchor)
    {
        public long Pulse => Section.Y + Anchor.Ry;
    }

    public sealed record Summary(
        int Anchors,
        int Sections,
        int Samples,
        InspectorValue<long> Position,
        InspectorValue<double> Start,
        InspectorValue<double> Landing,
        bool AnySlam,
        bool AllSlams,
        InspectorValue<int> Type,
        InspectorValue<int> Width);

    /// <summary>
    /// The anchors behind the selection: each touched point that is an anchor of its section (points of a curved path
    /// that are only baked samples are counted in <see cref="Summary.Samples"/> and left alone).
    /// </summary>
    public static IReadOnlyList<Target> Targets(Selection selection, out int samples)
    {
        var list = new List<Target>();
        samples = 0;
        foreach (var g in selection.TouchedLaserPoints.GroupBy(p => (p.Lane, p.Section)))
        {
            var anchors = LaserPaths.AnchorsOf(g.Key.Section);
            foreach (var p in g.OrderBy(p => p.Point.Ry))
            {
                int i = anchors.Anchors.FindIndex(a => a.Ry == p.Point.Ry);
                if (i < 0) samples++;
                else list.Add(new Target(g.Key.Lane, g.Key.Section, i, anchors.Anchors[i]));
            }
        }

        return list;
    }

    public static Summary Of(Selection selection)
    {
        var t = Targets(selection, out int samples);
        return new Summary(
            t.Count,
            t.Select(x => x.Section).Distinct().Count(),
            samples,
            InspectorValue<long>.Of(t.Select(x => x.Pulse)),
            InspectorValue<double>.Of(t.Select(x => x.Anchor.V)),
            InspectorValue<double>.Of(t.Where(x => x.Anchor.IsSlam).Select(x => x.Anchor.Vf!.Value)),
            t.Any(x => x.Anchor.IsSlam),
            t.Count > 0 && t.All(x => x.Anchor.IsSlam),
            InspectorValue<int>.Of(t.Select(x => (int?)x.Anchor.Type ?? -1)),
            InspectorValue<int>.Of(t.Select(x => x.Section.Width).Distinct()));
    }

    /// <summary>Applies <paramref name="edit"/> to each target's anchor (per section, on one copy of its anchors); null when refused or unchanged.</summary>
    private static IEditCommand? edit(Chart chart, IReadOnlyList<Target> targets, string name, Func<LaserPath, int, LaserPath?> edit)
    {
        var cmds = new List<IEditCommand>();
        foreach (var g in targets.GroupBy(t => (t.Lane, t.Section)))
        {
            var path = LaserPaths.AnchorsOf(g.Key.Section);
            // later anchors first, so moving one never shifts the index of another
            foreach (var t in g.OrderByDescending(t => t.Index))
            {
                if (edit(path, t.Index) is not { } next) return null;
                path = next;
            }

            if (LaserPaths.SamePoints(LaserPaths.ToSection(path, g.Key.Section.Y, g.Key.Section.Width).Points, g.Key.Section.Points, 1e-12)
                && LaserPaths.ToSection(path, g.Key.Section.Y, g.Key.Section.Width).Y == g.Key.Section.Y
                && sameTypes(path, LaserPaths.AnchorsOf(g.Key.Section)))
                continue;
            if (LaserPathCommands.Replace(chart, g.Key.Lane, g.Key.Section, path, name) is not { } cmd) return null;
            cmds.Add(cmd);
        }

        return cmds.Count == 0 ? null : cmds.Count == 1 ? cmds[0] : new CompositeCommand(name, cmds);
    }

    private static bool sameTypes(LaserPath a, LaserPath b) =>
        a.Count == b.Count && a.Anchors.Zip(b.Anchors).All(x => x.First.Type == x.Second.Type && x.First.Auto == x.Second.Auto);

    /// <summary>Start value V of every target (a slam keeps its landing value; equal values remove the slam).</summary>
    public static IEditCommand? SetStart(Chart chart, Selection selection, double v)
    {
        if (!(v >= 0 && v <= 1)) return null;
        return edit(chart, Targets(selection, out _), "Set laser value", (path, i) =>
        {
            var p = path.Clone();
            var a = p.Anchors[i];
            a.V = v;
            if (a.Vf is { } vf && vf == v) a.Vf = null;
            return p;
        });
    }

    /// <summary>
    /// Landing value Vf: makes every target a slam to <paramref name="vf"/> (null removes the slam, keeping V). A lone
    /// slam cannot lose its slam (a single plain point is no laser).
    /// </summary>
    public static IEditCommand? SetLanding(Chart chart, Selection selection, double? vf)
    {
        if (vf is { } x && !(x >= 0 && x <= 1)) return null;
        return edit(chart, Targets(selection, out _), vf == null ? "Remove slam" : "Set slam end", (path, i) =>
        {
            var p = path.Clone();
            var a = p.Anchors[i];
            a.Vf = vf == null || vf == a.V ? null : vf;
            return LaserPath.IsSectionShape(p) ? p : null;
        });
    }

    /// <summary>Position: one anchor moves to the pulse (strictly between its neighbours); several move by the same delta as the earliest.</summary>
    public static IEditCommand? SetPosition(Chart chart, Selection selection, long pulse)
    {
        var t = Targets(selection, out _);
        if (t.Count == 0 || pulse < 0) return null;
        long delta = pulse - t.Min(x => x.Pulse);
        if (delta == 0) return null;
        return edit(chart, t, "Move anchor", (path, i) =>
        {
            var a = path.Anchors[i];
            var moved = LaserPathEditing.MoveAnchor(path, i, a.Ry + delta, a.V);
            if (moved == null) return null;
            // MoveAnchor clamps between the neighbours; refuse instead of landing somewhere else
            return moved.Anchors.Any(x => x.Ry == a.Ry + delta) || i == 0 ? moved : null;
        });
    }

    /// <summary>Curve type of every target anchor (null = inherit / auto).</summary>
    public static IEditCommand? SetType(Chart chart, Selection selection, LaserCurveType? type) =>
        edit(chart, Targets(selection, out _), "Change anchor type", (path, i) => LaserPathEditing.SetType(path, i, type));

    /// <summary>Width (1 or 2) of the touched sections, values kept (K-Shoot "Keep Values").</summary>
    public static IEditCommand? SetWidth(Chart chart, Selection selection, int width)
    {
        if (width is not (1 or 2)) return null;
        var items = selection.Sections.Where(s => s.Section.Width != width)
                             .Select(s => (s.Lane, s.Section, s.Lane, LaserEditing.WithWidth(s.Section, width, keepPosition: false).Section)).ToList();
        return items.Count == 0 ? null : new ReplaceLaserSectionsCommand("Set laser width", items);
    }

    public static string TypeName(int type) => type switch
    {
        0 => "straight",
        1 => "perfect",
        2 => "bezier",
        _ => "auto",
    };

    /// <summary>"straight" / "perfect" / "bezier" / "auto" (also linear, arc, curve, inherit); false for anything else.</summary>
    public static bool TryParseType(string text, out LaserCurveType? type)
    {
        type = null;
        switch (text.Trim().ToLowerInvariant())
        {
            case "straight" or "linear" or "s": type = LaserCurveType.Linear; return true;
            case "perfect" or "arc" or "circle" or "p": type = LaserCurveType.PerfectCurve; return true;
            case "bezier" or "curve" or "b": type = LaserCurveType.Bezier; return true;
            case "auto" or "inherit" or "" or "a": return true;
            default: return false;
        }
    }
}
