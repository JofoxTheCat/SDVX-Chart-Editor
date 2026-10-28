using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Placement rule shared by every editing operation.
/// <list type="bullet">
/// <item>A chip occupies its pulse, a hold the half-open range [Y, EndY). Two notes on one lane conflict when
/// these ranges intersect: two chips on one pulse, a chip inside a hold, or overlapping holds. A chip on a
/// hold's end pulse and holds that touch (end == start) are allowed — both are valid kson (ksh writes
/// touching holds as one, see <c>KshWriter.KnownLosses</c>).</item>
/// <item>Laser sections on one lane conflict when their closed ranges [Y, EndY] intersect (sharing even one
/// pulse would make two lasers at once) or when the gap between them is shorter than <see cref="MinLaserGap"/>.</item>
/// <item>Nothing may start before pulse 0.</item>
/// </list>
/// </summary>
public static class OverlapRule
{
    /// <summary>
    /// Smallest gap between two laser sections of one lane: 10 pulses = 1/96 note = 2 VOX ticks = two lines of a ksh at
    /// 192 lines per measure (end point, the '-' line, the next start) — the closest K-Shoot itself writes two sections;
    /// the local library has 25 such gaps and none smaller. Official .vox (all 8,602, 2026-10-27, 369,479 section gaps):
    /// 0 and 1 tick 28 times each (an old-chart trick to switch the laser filter mid-slam — in kson that is one section
    /// with a slam), 2 ticks never, 3 ticks 392 times. Before, only a shared pulse was refused, so a free drag (5-pulse
    /// steps) stopped 5 pulses = one VOX tick short of the next section: two lasers that look and play as if they
    /// started at once (the editor bug of 2026-10-27).
    /// </summary>
    public const long MinLaserGap = 10;

    /// <summary>The status text when a laser edit is refused for <see cref="SectionsTooClose"/>.</summary>
    public const string LaserGapMessage = "Overlaps or touches another laser of that side (sections need a 1/96 gap)";

    public static bool Conflicts(long y1, long len1, long y2, long len2)
    {
        if (len1 == 0 && len2 == 0) return y1 == y2;
        if (len1 == 0) return y2 <= y1 && y1 < y2 + len2;
        if (len2 == 0) return y1 <= y2 && y2 < y1 + len1;
        return y1 < y2 + len2 && y2 < y1 + len1;
    }

    /// <summary>The closed ranges share at least one pulse: two lasers on one lane at once (chart check error).</summary>
    public static bool SectionsConflict(long y1, long end1, long y2, long end2) => y1 <= end2 && y2 <= end1;

    /// <summary>
    /// The placement rule for laser sections of one lane: they share a pulse or lie less than <see cref="MinLaserGap"/>
    /// apart (end of the earlier to start of the later).
    /// </summary>
    public static bool SectionsTooClose(long y1, long end1, long y2, long end2) =>
        y1 < end2 + MinLaserGap && y2 < end1 + MinLaserGap;

    /// <summary>Pulses between two sections (later start − earlier end); ≤ 0 when they share a pulse.</summary>
    public static long SectionGap(LaserSection a, LaserSection b) => Math.Max(a.Y - b.EndY, b.Y - a.EndY);

    /// <summary>
    /// True if notes placed at the given positions are valid among the lane's other notes
    /// (<paramref name="ignore"/> = notes that are being moved away) and among themselves.
    /// </summary>
    public static bool CanPlace(Chart chart, IReadOnlyList<(NoteKind Kind, int Lane, long Y, long Length)> placed, ISet<Note> ignore)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            var p = placed[i];
            if (p.Y < 0 || p.Lane < 0 || p.Lane >= NoteData.LaneCount(p.Kind)) return false;
            foreach (var n in chart.Notes.Lane(p.Kind, p.Lane))
                if (!ignore.Contains(n) && Conflicts(p.Y, p.Length, n.Y, n.Length)) return false;
            for (int j = i + 1; j < placed.Count; j++)
            {
                var q = placed[j];
                if (q.Kind == p.Kind && q.Lane == p.Lane && Conflicts(p.Y, p.Length, q.Y, q.Length)) return false;
            }
        }
        return true;
    }

    /// <summary>True if <paramref name="note"/> may take <paramref name="length"/> (same start) among the other notes of its lane.</summary>
    public static bool CanResize(Chart chart, NoteRef note, long length) =>
        length >= 0 && CanPlace(chart, new[] { (note.Kind, note.Lane, note.Note.Y, length) }, new HashSet<Note> { note.Note });

    /// <summary>
    /// True if the sections may go to these lanes (<paramref name="ignore"/> = sections being replaced) among the lanes'
    /// other sections and among themselves: no shared pulse, and a gap of at least <see cref="MinLaserGap"/>. A file can
    /// hold sections closer than that (official charts do, rarely): an edit of such a section is allowed as long as it
    /// shares no pulse and comes no closer than a replaced section of that lane already was, so it can still be edited
    /// and moved apart.
    /// </summary>
    public static bool CanPlace(Chart chart, IReadOnlyList<(int Lane, LaserSection Section)> placed, ISet<LaserSection> ignore)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            var (lane, s) = placed[i];
            if (s.Y < 0 || s.Points.Count == 0) return false;
            var replaced = ignore.Count == 0 ? null : chart.Notes.Lasers[lane].Where(ignore.Contains).ToList();
            foreach (var other in chart.Notes.Lasers[lane])
            {
                if (ignore.Contains(other) || !SectionsTooClose(s.Y, s.EndY, other.Y, other.EndY)) continue;
                long gap = SectionGap(s, other);
                if (gap <= 0) return false;
                // grandfathered: as close as (or farther than) a replaced section already was
                if (replaced == null || !replaced.Any(r => SectionGap(r, other) <= gap)) return false;
            }

            for (int j = i + 1; j < placed.Count; j++)
                if (placed[j].Lane == lane && SectionsTooClose(s.Y, s.EndY, placed[j].Section.Y, placed[j].Section.EndY)) return false;
        }
        return true;
    }
}

/// <summary>Moves notes to individual new lanes/pulses (same kind). The Note objects stay the same.</summary>
public sealed class TransformNotesCommand : IEditCommand
{
    private readonly List<(NoteRef From, int Lane, long Y)> moves;

    public TransformNotesCommand(string description, IEnumerable<(NoteRef From, int Lane, long Y)> moves)
    {
        Description = description;
        this.moves = moves.ToList();
        OldY = this.moves.Select(m => m.From.Note.Y).ToList();
    }

    private List<long> OldY { get; }
    public string Description { get; }

    /// <summary>References to the notes after the command ran.</summary>
    public IEnumerable<NoteRef> Result => moves.Select(m => m.From with { Lane = m.Lane });

    public void Execute(Chart chart)
    {
        foreach (var m in moves) chart.Notes.Lane(m.From.Kind, m.From.Lane).Remove(m.From.Note);
        foreach (var m in moves)
        {
            m.From.Note.Y = m.Y;
            NoteData.InsertSorted(chart.Notes.Lane(m.From.Kind, m.Lane), m.From.Note);
        }
    }

    public void Undo(Chart chart)
    {
        foreach (var m in moves) chart.Notes.Lane(m.From.Kind, m.Lane).Remove(m.From.Note);
        for (int i = 0; i < moves.Count; i++)
        {
            moves[i].From.Note.Y = OldY[i];
            NoteData.InsertSorted(chart.Notes.Lane(moves[i].From.Kind, moves[i].From.Lane), moves[i].From.Note);
        }
    }
}

/// <summary>Replaces laser sections with new ones (possibly on the other lane). Used by move, mirror and reverse.</summary>
public sealed class ReplaceLaserSectionsCommand : IEditCommand
{
    private readonly List<(int OldLane, LaserSection Old, int NewLane, LaserSection New)> items;

    public ReplaceLaserSectionsCommand(string description, IEnumerable<(int OldLane, LaserSection Old, int NewLane, LaserSection New)> items)
    {
        Description = description;
        this.items = items.ToList();
    }

    public string Description { get; }
    public IEnumerable<(int Lane, LaserSection Section)> Result => items.Select(i => (i.NewLane, i.New));
    public IReadOnlyList<(int OldLane, LaserSection Old, int NewLane, LaserSection New)> Items => items;

    public void Execute(Chart chart)
    {
        foreach (var i in items) chart.Notes.Lasers[i.OldLane].Remove(i.Old);
        foreach (var i in items) NoteData.InsertSorted(chart.Notes.Lasers[i.NewLane], i.New);
    }

    public void Undo(Chart chart)
    {
        foreach (var i in items) chart.Notes.Lasers[i.NewLane].Remove(i.New);
        foreach (var i in items) NoteData.InsertSorted(chart.Notes.Lasers[i.OldLane], i.Old);
    }
}

/// <summary>
/// The osu!-style selection operations. Each returns an undoable command for a <see cref="CommandStack"/>,
/// or null when the result would break the <see cref="OverlapRule"/> (the UI then refuses the action).
/// Laser points act on their whole section.
/// </summary>
public static class SelectionOps
{
    private static List<(int Lane, LaserSection Section)> Sections(Selection s) => s.Sections.OrderBy(x => x.Section.Y).ToList();

    /// <summary>Span of the selection including hold ends and the full extent of touched laser sections.</summary>
    public static (long Start, long End)? Span(Selection selection)
    {
        var sections = Sections(selection);
        var starts = selection.Notes.Select(n => n.Note.Y).Concat(sections.Select(s => s.Section.Y)).ToList();
        if (starts.Count == 0) return null;
        long end = selection.Notes.Select(n => n.Note.EndY).Concat(sections.Select(s => s.Section.EndY)).Max();
        return (starts.Min(), end);
    }

    /// <summary>
    /// Deletes the selected notes and laser points (a section whose points are all selected goes entirely). A selected
    /// slam end removes just that end — the slam becomes a plain anchor at the other end's value; both ends selected
    /// remove the anchor. Sections with selected slam ends are edited through their anchors
    /// (<see cref="LaserPathEditing"/>), so a curved path keeps its curve types; a section left with fewer than two
    /// anchors goes.
    /// </summary>
    public static IEditCommand? Delete(Selection selection)
    {
        if (selection.IsEmpty) return null;
        var cmds = new List<IEditCommand>();
        if (selection.Notes.Count > 0) cmds.Add(new RemoveNotesCommand(selection.Notes, "Delete notes"));
        var ends = selection.SlamEnds.GroupBy(e => (e.Lane, e.Section)).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var group in selection.LaserPoints.GroupBy(p => (p.Lane, p.Section)))
        {
            var (lane, section) = group.Key;
            if (group.Count() >= section.Points.Count) cmds.Add(new RemoveLaserSectionCommand(lane, section));
            else if (ends.Remove(group.Key, out var withEnds)) cmds.Add(DeleteThroughAnchors(lane, section, group.Select(p => p.Point), withEnds));
            else cmds.AddRange(group.Select(p => new RemoveLaserPointCommand(section, p.Point)));
        }

        foreach (var ((lane, section), list) in ends)
            cmds.Add(DeleteThroughAnchors(lane, section, Array.Empty<LaserPoint>(), list));
        return new CompositeCommand("Delete", cmds);
    }

    /// <summary>
    /// One section's delete through its anchors: whole points (and slams with both ends selected) remove their anchor,
    /// a single slam end removes that end. Points that are no anchor (samples of a curved path) are left alone.
    /// </summary>
    private static IEditCommand DeleteThroughAnchors(int lane, LaserSection section, IEnumerable<LaserPoint> points, IReadOnlyList<LaserSlamEndRef> ends)
    {
        var path = LaserPaths.AnchorsOf(section);
        var remove = points.Select(p => p.Ry).ToHashSet();
        var single = new Dictionary<long, bool>();
        foreach (var g in ends.GroupBy(e => e.Point))
        {
            if (g.Select(e => e.End).Distinct().Count() == 2) remove.Add(g.Key.Ry);
            else single[g.Key.Ry] = g.First().End;
        }

        foreach (var (ry, end) in single)
        {
            if (remove.Contains(ry)) continue;
            int i = path.Anchors.FindIndex(a => a.Ry == ry);
            if (i >= 0 && LaserPathEditing.DeleteSlamEnd(path, i, end) is { } edited) path = edited;
        }

        foreach (long ry in remove.OrderByDescending(r => r))
        {
            int i = path.Anchors.FindIndex(a => a.Ry == ry);
            if (i < 0) continue;
            if (LaserPathEditing.DeleteAnchor(path, i) is not { } edited) return new RemoveLaserSectionCommand(lane, section);
            path = edited;
        }

        // one end of a lone slam deleted: a single plain point is no laser, the section goes
        if (!LaserPath.IsSectionShape(path)) return new RemoveLaserSectionCommand(lane, section);
        return new ReplaceLaserSectionsCommand("Delete laser points", new[] { (lane, section, lane, LaserPaths.ToSection(path, section.Y, section.Width)) });
    }

    /// <summary>
    /// Moves the selection by <paramref name="deltaPulse"/> and <paramref name="deltaLane"/> (notes only;
    /// lasers keep their side). Both deltas are clamped so everything stays at pulse ≥ 0 and inside its
    /// lanes; null if the clamped move is a no-op or would overlap unselected objects.
    /// </summary>
    public static IEditCommand? Move(Chart chart, Selection selection, long deltaPulse, int deltaLane = 0)
    {
        if (Span(selection) is not { } span) return null;
        deltaPulse = Math.Max(deltaPulse, -span.Start);
        foreach (var n in selection.Notes)
            deltaLane = Math.Clamp(deltaLane, -n.Lane, NoteData.LaneCount(n.Kind) - 1 - n.Lane);
        if (deltaPulse == 0 && deltaLane == 0) return null;
        return Transform(chart, selection, $"Move", n => (n.Lane + deltaLane, n.Note.Y + deltaPulse),
            (lane, s) => (lane, Shifted(s, deltaPulse)));
    }

    /// <summary>
    /// Mirrors horizontally: BT A↔D, B↔C, FX L↔R, laser L↔R with v → 1−v (slam vf too). Curve parameters
    /// stay as they are: the eased fraction f(x) of a segment is unchanged when both end values are mirrored.
    /// </summary>
    public static IEditCommand? Mirror(Chart chart, Selection selection)
    {
        if (selection.IsEmpty) return null;
        return Transform(chart, selection, "Mirror", n => (NoteData.LaneCount(n.Kind) - 1 - n.Lane, n.Note.Y), (lane, s) =>
        {
            // a curved anchor path mirrors its anchors and is baked again, so it keeps its curve types
            if (LaserPaths.ValidPath(s) is { IsTrivial: false } path)
            {
                var flipped = path.Clone();
                foreach (var a in flipped.Anchors)
                {
                    a.V = Flip(a.V);
                    if (a.Vf is { } avf) a.Vf = Flip(avf);
                }
                return (1 - lane, LaserPaths.ToSection(flipped, s.Y, s.Width));
            }

            var m = s.Clone();
            foreach (var p in m.Points)
            {
                p.V = Flip(p.V);
                if (p.Vf is { } vf) p.Vf = Flip(vf);
            }
            return (1 - lane, m);
        });
    }

    /// <summary>
    /// Reverses time inside the selection span: an object occupying [y, end] goes to [S+E−end, S+E−y], so holds keep
    /// their length. Laser sections reverse their point order; a slam v→vf becomes vf→v, and a segment curve
    /// (a, b) becomes (1−a, 1−b) on the reversed segment (the point reflection of the Bezier).
    /// </summary>
    public static IEditCommand? Reverse(Chart chart, Selection selection)
    {
        if (Span(selection) is not { } span) return null;
        long sum = span.Start + span.End;
        return Transform(chart, selection, "Reverse", n => (n.Lane, sum - n.Note.EndY), (lane, s) => (lane, Reversed(s, sum)));
    }

    /// <summary>Copies the selection so that its first pulse lands on <paramref name="targetPulse"/>; null on overlap.</summary>
    public static IEditCommand? Duplicate(Chart chart, Selection selection, long targetPulse)
        => selection.IsEmpty ? null : Paste(chart, ClipboardData.FromSelection(selection), targetPulse);

    /// <summary>Pastes clipboard content at a pulse; null if it would overlap existing objects.</summary>
    public static IEditCommand? Paste(Chart chart, ClipboardData clip, long pulse, bool mirror = false)
    {
        if (clip.IsEmpty) return null;
        var cmd = (CompositeCommand)clip.PasteAt(pulse, mirror);
        var notes = cmd.Commands.OfType<AddNotesCommand>().SelectMany(a => a.Notes).ToList();
        var sections = cmd.Commands.OfType<AddLaserSectionCommand>().Select(a => (a.Lane, a.Section)).ToList();
        bool ok = OverlapRule.CanPlace(chart, notes.Select(n => (n.Kind, n.Lane, n.Note.Y, n.Note.Length)).ToList(), new HashSet<Note>())
                  && OverlapRule.CanPlace(chart, sections, new HashSet<LaserSection>());
        return ok ? cmd : null;
    }

    private static IEditCommand? Transform(Chart chart, Selection selection, string name,
        Func<NoteRef, (int Lane, long Y)> note, Func<int, LaserSection, (int Lane, LaserSection Section)> laser)
    {
        var moves = selection.Notes.Select(n => { var (l, y) = note(n); return (From: n, Lane: l, Y: y); }).ToList();
        var sections = Sections(selection).Select(s => { var r = laser(s.Lane, s.Section); return (OldLane: s.Lane, Old: s.Section, NewLane: r.Lane, New: r.Section); }).ToList();

        var movedNotes = selection.Notes.Select(n => n.Note).ToHashSet();
        var movedSections = sections.Select(s => s.Old).ToHashSet();
        if (!OverlapRule.CanPlace(chart, moves.Select(m => (m.From.Kind, m.Lane, m.Y, m.From.Note.Length)).ToList(), movedNotes)) return null;
        if (!OverlapRule.CanPlace(chart, sections.Select(s => (s.NewLane, s.New)).ToList(), movedSections)) return null;

        var cmds = new List<IEditCommand>();
        if (moves.Count > 0) cmds.Add(new TransformNotesCommand(name + " notes", moves));
        if (sections.Count > 0) cmds.Add(new ReplaceLaserSectionsCommand(name + " lasers", sections));
        return new CompositeCommand(name, cmds);
    }

    /// <summary>1 − x, rounded to 12 decimals so that flipping twice returns the original value.</summary>
    private static double Flip(double x) => Math.Round(1 - x, 12);

    private static LaserSection Shifted(LaserSection s, long delta)
    {
        var c = s.Clone();
        c.Y += delta;
        return c;
    }

    /// <summary>A time-reversed copy of a section: pulse y maps to <paramref name="sum"/> − y.</summary>
    public static LaserSection Reversed(LaserSection s, long sum)
    {
        var pts = s.Points;
        long newY = sum - s.EndY;
        var result = new LaserSection(newY, width: s.Width);
        for (int k = pts.Count - 1; k >= 0; k--)
        {
            var p = pts[k];
            long abs = sum - (s.Y + p.Ry);
            // the reversed segment starting here is the old segment (k-1 → k); its curve was on point k-1
            Curve? curve = k > 0 && pts[k - 1].Curve is { } c ? new Curve(Flip(c.A), Flip(c.B)) : null;
            result.Points.Add(p.Vf is { } vf
                ? new LaserPoint(abs - newY, vf, p.V, curve)
                : new LaserPoint(abs - newY, p.V, null, curve));
        }
        return result;
    }
}
