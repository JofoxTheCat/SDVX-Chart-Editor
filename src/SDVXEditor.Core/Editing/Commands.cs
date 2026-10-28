using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>A note in a lane. Equality is by note identity, so it stays valid while the note is edited.</summary>
public sealed record NoteRef(NoteKind Kind, int Lane, Note Note);

/// <summary>A laser control point in a section of laser lane 0 (left) or 1 (right).</summary>
public sealed record LaserPointRef(int Lane, LaserSection Section, LaserPoint Point);

/// <summary>
/// One end of a slam point: <see cref="End"/> false = the start end (<see cref="LaserPoint.V"/>), true = the end it lands
/// on (<see cref="LaserPoint.Vf"/>). The editor treats the two ends as two points (hover, select, drag, delete); the chart
/// keeps one point with <c>V</c> / <c>Vf</c>.
/// </summary>
public sealed record LaserSlamEndRef(int Lane, LaserSection Section, LaserPoint Point, bool End)
{
    public LaserPointRef PointRef => new(Lane, Section, Point);
    public double Value => End ? Point.Vf ?? Point.V : Point.V;
}

public sealed class AddNotesCommand : IEditCommand
{
    private readonly List<NoteRef> notes;

    public AddNotesCommand(IEnumerable<NoteRef> notes, string description = "Add notes")
    {
        this.notes = notes.ToList();
        Description = description;
    }

    public string Description { get; }
    public IReadOnlyList<NoteRef> Notes => notes;

    public void Execute(Chart chart)
    {
        foreach (var r in notes) NoteData.InsertSorted(chart.Notes.Lane(r.Kind, r.Lane), r.Note);
    }

    public void Undo(Chart chart)
    {
        foreach (var r in notes) chart.Notes.Lane(r.Kind, r.Lane).Remove(r.Note);
    }
}

public sealed class RemoveNotesCommand : IEditCommand
{
    private readonly List<NoteRef> notes;

    public RemoveNotesCommand(IEnumerable<NoteRef> notes, string description = "Delete notes")
    {
        this.notes = notes.ToList();
        Description = description;
    }

    public string Description { get; }

    public void Execute(Chart chart)
    {
        foreach (var r in notes)
            if (!chart.Notes.Lane(r.Kind, r.Lane).Remove(r.Note))
                throw new InvalidOperationException($"note {r.Note} is not in {r.Kind} lane {r.Lane}");
    }

    public void Undo(Chart chart)
    {
        foreach (var r in notes) NoteData.InsertSorted(chart.Notes.Lane(r.Kind, r.Lane), r.Note);
    }
}

/// <summary>Moves notes in time and/or across lanes of the same kind; FX effect/key sound data moves along.</summary>
public sealed class MoveNotesCommand : IEditCommand
{
    private readonly List<NoteRef> notes;

    public MoveNotesCommand(IEnumerable<NoteRef> notes, long deltaPulse, int deltaLane = 0, string description = "Move notes")
    {
        this.notes = notes.ToList();
        DeltaPulse = deltaPulse;
        DeltaLane = deltaLane;
        Description = description;
        foreach (var r in this.notes)
        {
            int lane = r.Lane + deltaLane;
            if (lane < 0 || lane >= NoteData.LaneCount(r.Kind))
                throw new ArgumentOutOfRangeException(nameof(deltaLane), $"{r.Kind} lane {lane} does not exist");
        }
    }

    public string Description { get; }
    public long DeltaPulse { get; }
    public int DeltaLane { get; }

    /// <summary>The references after the move (lanes updated).</summary>
    public IEnumerable<NoteRef> Moved => notes.Select(r => r with { Lane = r.Lane + DeltaLane });

    public void Execute(Chart chart) => Apply(chart, DeltaPulse, DeltaLane, notes);

    public void Undo(Chart chart) => Apply(chart, -DeltaPulse, -DeltaLane, Moved.ToList());

    private static void Apply(Chart chart, long dy, int dl, List<NoteRef> refs)
    {
        foreach (var r in refs) chart.Notes.Lane(r.Kind, r.Lane).Remove(r.Note);
        foreach (var r in refs)
        {
            r.Note.Y += dy;
            NoteData.InsertSorted(chart.Notes.Lane(r.Kind, r.Lane + dl), r.Note);
        }
    }
}

public sealed class AddLaserSectionCommand : IEditCommand
{
    public AddLaserSectionCommand(int lane, LaserSection section)
    {
        Lane = lane;
        Section = section;
    }

    public int Lane { get; }
    public LaserSection Section { get; }
    public string Description => "Add laser";

    public void Execute(Chart chart) => NoteData.InsertSorted(chart.Notes.Lasers[Lane], Section);
    public void Undo(Chart chart) => chart.Notes.Lasers[Lane].Remove(Section);
}

public sealed class RemoveLaserSectionCommand : IEditCommand
{
    public RemoveLaserSectionCommand(int lane, LaserSection section)
    {
        Lane = lane;
        Section = section;
    }

    public int Lane { get; }
    public LaserSection Section { get; }
    public string Description => "Delete laser";

    public void Execute(Chart chart)
    {
        if (!chart.Notes.Lasers[Lane].Remove(Section)) throw new InvalidOperationException("section is not in the lane");
    }

    public void Undo(Chart chart) => NoteData.InsertSorted(chart.Notes.Lasers[Lane], Section);
}

/// <summary>
/// Adds a control point at an absolute pulse. A point before the section start moves the start
/// (all relative pulses are rebased); a point on an existing pulse is rejected.
/// </summary>
public sealed class AddLaserPointCommand : IEditCommand
{
    private readonly LaserSection section;
    private readonly LaserPoint point;
    private readonly long pulse;
    private long oldY;

    public AddLaserPointCommand(LaserSection section, long pulse, LaserPoint point)
    {
        this.section = section;
        this.pulse = pulse;
        this.point = point;
    }

    public string Description => "Add laser point";

    public void Execute(Chart chart)
    {
        if (section.Points.Any(p => section.Y + p.Ry == pulse))
            throw new InvalidOperationException($"a point already exists at {pulse}");
        oldY = section.Y;
        if (pulse < section.Y) Rebase(section, pulse);
        point.Ry = pulse - section.Y;
        int i = section.Points.FindLastIndex(p => p.Ry < point.Ry) + 1;
        section.Points.Insert(i, point);
    }

    public void Undo(Chart chart)
    {
        section.Points.Remove(point);
        Rebase(section, oldY);
    }

    internal static void Rebase(LaserSection s, long newY)
    {
        long d = s.Y - newY;
        foreach (var p in s.Points) p.Ry += d;
        s.Y = newY;
    }
}

/// <summary>Removes a control point; removing the first point moves the section start to the next one.</summary>
public sealed class RemoveLaserPointCommand : IEditCommand
{
    private readonly LaserSection section;
    private readonly LaserPoint point;
    private int index;
    private long oldY;

    public RemoveLaserPointCommand(LaserSection section, LaserPoint point)
    {
        this.section = section;
        this.point = point;
    }

    public string Description => "Delete laser point";

    public void Execute(Chart chart)
    {
        index = section.Points.IndexOf(point);
        if (index < 0) throw new InvalidOperationException("point is not in the section");
        oldY = section.Y;
        section.Points.RemoveAt(index);
        if (index == 0 && section.Points.Count > 0) AddLaserPointCommand.Rebase(section, section.Y + section.Points[0].Ry);
    }

    public void Undo(Chart chart)
    {
        AddLaserPointCommand.Rebase(section, oldY);
        section.Points.Insert(index, point);
    }
}

/// <summary>Sets any value on the chart (meta fields, BPM, …) and restores the old one on undo.</summary>
public sealed class SetValueCommand<T> : IEditCommand
{
    private readonly Func<Chart, T> get;
    private readonly Action<Chart, T> set;
    private readonly T value;
    private T old = default!;

    public SetValueCommand(string description, Func<Chart, T> get, Action<Chart, T> set, T value)
    {
        Description = description;
        this.get = get;
        this.set = set;
        this.value = value;
    }

    public string Description { get; }

    public void Execute(Chart chart)
    {
        old = get(chart);
        set(chart, value);
    }

    public void Undo(Chart chart) => set(chart, old);
}

public static class MetaCommands
{
    /// <summary>Convenience for meta fields: <c>MetaCommands.Set("Title", m => m.Title, (m, v) => m.Title = v, "New")</c>.</summary>
    public static SetValueCommand<T> Set<T>(string field, Func<ChartMeta, T> get, Action<ChartMeta, T> set, T value)
        => new($"Change {field}", c => get(c.Meta), (c, v) => set(c.Meta, v), value);
}

/// <summary>
/// Changes the length of one note in place (identity kept, so selections stay valid): chip → hold, hold → chip, or a
/// new hold length. The start pulse never changes. What only one form can carry goes: a hold that becomes a chip
/// loses its FX effects, a shortened hold the effects at or after its new end, a chip that becomes a hold its key
/// sound. Undo restores all of it. Check <see cref="OverlapRule.CanResize"/> first.
/// </summary>
public sealed class SetNoteLengthCommand : IEditCommand
{
    private readonly long oldLength;
    private readonly List<FxEffectEvent> oldEffects;
    private readonly KeySoundRef? oldKeySound;

    public SetNoteLengthCommand(NoteRef note, long length, string? description = null)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Note = note;
        Length = length;
        oldLength = note.Note.Length;
        oldEffects = note.Note.Effects;
        oldKeySound = note.Note.KeySound;
        Description = description ?? (length == 0 ? "Hold to chip" : oldLength == 0 ? "Chip to hold" : "Hold length");
    }

    public NoteRef Note { get; }
    public long Length { get; }
    public string Description { get; }

    public void Execute(Chart chart)
    {
        var n = Note.Note;
        n.Length = Length;
        n.Effects = Length == 0 ? new List<FxEffectEvent>() : oldEffects.Where(e => e.RelY < Length).ToList();
        if (Length > 0) n.KeySound = null;
    }

    public void Undo(Chart chart)
    {
        var n = Note.Note;
        n.Length = oldLength;
        n.Effects = oldEffects;
        n.KeySound = oldKeySound;
    }
}
