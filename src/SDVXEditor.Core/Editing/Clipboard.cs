using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Copied chart content, stored relative to its first pulse so it can be pasted anywhere.
/// Laser points copy their whole section (a section cannot be split on paste).
/// </summary>
public sealed class ClipboardData
{
    public List<(NoteKind Kind, int Lane, Note Note)> Notes { get; } = new();
    public List<(int Lane, LaserSection Section)> Lasers { get; } = new();

    public bool IsEmpty => Notes.Count == 0 && Lasers.Count == 0;

    /// <summary>Length from the first copied pulse to the last (hold ends included).</summary>
    public long Length => Notes.Select(n => n.Note.EndY).Concat(Lasers.Select(l => l.Section.EndY)).DefaultIfEmpty(0).Max();

    public static ClipboardData FromSelection(Selection selection)
    {
        var data = new ClipboardData();
        var sections = selection.Sections.ToList();
        long origin = selection.Notes.Select(n => n.Note.Y).Concat(sections.Select(s => s.Section.Y)).DefaultIfEmpty(0).Min();
        foreach (var r in selection.Notes.OrderBy(n => n.Note.Y))
        {
            var copy = r.Note.Clone();
            copy.Y -= origin;
            data.Notes.Add((r.Kind, r.Lane, copy));
        }
        foreach (var (lane, s) in sections.OrderBy(s => s.Section.Y))
        {
            var copy = s.Clone();
            copy.Y -= origin;
            data.Lasers.Add((lane, copy));
        }
        return data;
    }

    /// <summary>A command that pastes a fresh copy at <paramref name="pulse"/>; <paramref name="mirror"/> flips lanes and laser values.</summary>
    public IEditCommand PasteAt(long pulse, bool mirror = false)
    {
        var cmds = new List<IEditCommand>();
        var notes = Notes.Select(n =>
        {
            var copy = n.Note.Clone();
            copy.Y += pulse;
            int lane = mirror ? NoteData.LaneCount(n.Kind) - 1 - n.Lane : n.Lane;
            return new NoteRef(n.Kind, lane, copy);
        }).ToList();
        if (notes.Count > 0) cmds.Add(new AddNotesCommand(notes, "Paste notes"));
        foreach (var (lane, s) in Lasers)
        {
            var copy = s.Clone();
            copy.Y += pulse;
            if (mirror)
                foreach (var p in copy.Points)
                {
                    p.V = 1 - p.V;
                    if (p.Vf is { } vf) p.Vf = 1 - vf;
                }
            cmds.Add(new AddLaserSectionCommand(mirror ? 1 - lane : lane, copy));
        }
        return new CompositeCommand("Paste", cmds);
    }
}
