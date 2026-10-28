using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>A value shown for a multi-selection: the common value, or <see cref="Mixed"/> when the objects differ.</summary>
public readonly record struct InspectorValue<T>(T? Value, bool Mixed, bool Present) where T : struct
{
    public static readonly InspectorValue<T> None = new(null, false, false);

    /// <summary>The common value of the items, mixed when they differ, none when there are no items.</summary>
    public static InspectorValue<T> Of(IEnumerable<T> values)
    {
        T? first = null;
        foreach (var v in values)
        {
            if (first == null) first = v;
            else if (!EqualityComparer<T>.Default.Equals(first.Value, v)) return new InspectorValue<T>(null, true, true);
        }

        return first == null ? None : new InspectorValue<T>(first, false, true);
    }
}

/// <summary>
/// The inspector's note part (Core, so the rules are unit-tested): what the selected BT / FX notes have in common and the
/// edits that set an exact value. Every edit is one command; null = refused (overlap, nothing to change).
/// </summary>
public static class NoteInspector
{
    public sealed record Summary(int Count, int Bt, int Fx, InspectorValue<long> Start, InspectorValue<long> Length, InspectorValue<int> Lane, string? Effect, bool EffectMixed);

    public static Summary Of(IReadOnlyCollection<NoteRef> notes)
    {
        var effects = notes.Where(n => n.Kind == NoteKind.Fx && n.Note.IsHold).Select(n => HoldEffects.Of(n.Note)).Distinct().ToList();
        return new Summary(
            notes.Count,
            notes.Count(n => n.Kind == NoteKind.Bt),
            notes.Count(n => n.Kind == NoteKind.Fx),
            InspectorValue<long>.Of(notes.Select(n => n.Note.Y)),
            InspectorValue<long>.Of(notes.Select(n => n.Note.Length)),
            notes.Select(n => n.Kind).Distinct().Count() == 1 ? InspectorValue<int>.Of(notes.Select(n => n.Lane)) : new InspectorValue<int>(null, true, notes.Count > 0),
            effects.Count == 1 ? effects[0] : null,
            effects.Count > 1);
    }

    /// <summary>
    /// Start: one note lands exactly on <paramref name="pulse"/>; several move together so the earliest lands there (their
    /// spacing is kept). Null when it would overlap or leave the chart.
    /// </summary>
    public static IEditCommand? SetStart(Chart chart, IReadOnlyCollection<NoteRef> notes, long pulse)
    {
        if (notes.Count == 0 || pulse < 0) return null;
        long delta = pulse - notes.Min(n => n.Note.Y);
        if (delta == 0) return null;
        var moves = notes.Select(n => (From: n, n.Lane, Y: n.Note.Y + delta)).ToList();
        var moved = notes.Select(n => n.Note).ToHashSet();
        if (!OverlapRule.CanPlace(chart, moves.Select(m => (m.From.Kind, m.Lane, m.Y, m.From.Note.Length)).ToList(), moved)) return null;
        return new TransformNotesCommand("Set note position", moves);
    }

    /// <summary>Length for every selected note (0 = chip); null when one of them cannot take it or nothing changes.</summary>
    public static IEditCommand? SetLength(Chart chart, IReadOnlyCollection<NoteRef> notes, long length)
    {
        if (length < 0) return null;
        var change = notes.Where(n => n.Note.Length != length).ToList();
        if (change.Count == 0) return null;
        // checked as a group: two selected notes of one lane must not grow into each other either
        var placed = notes.Select(n => (n.Kind, n.Lane, n.Note.Y, Length: change.Contains(n) ? length : n.Note.Length)).ToList();
        if (!OverlapRule.CanPlace(chart, placed, notes.Select(n => n.Note).ToHashSet())) return null;
        var cmds = change.Select(n => (IEditCommand)new SetNoteLengthCommand(n, length, length == 0 ? "Set chip" : "Set hold length")).ToList();
        return cmds.Count == 1 ? cmds[0] : new CompositeCommand("Set note length", cmds);
    }

    /// <summary>Lane for every selected note of one kind (BT 0..3, FX 0..1); null on overlap / mixed kinds / no change.</summary>
    public static IEditCommand? SetLane(Chart chart, IReadOnlyCollection<NoteRef> notes, int lane)
    {
        if (notes.Count == 0 || notes.Select(n => n.Kind).Distinct().Count() != 1) return null;
        var kind = notes.First().Kind;
        if (lane < 0 || lane >= NoteData.LaneCount(kind) || notes.All(n => n.Lane == lane)) return null;
        var moves = notes.Select(n => (From: n, Lane: lane, n.Note.Y)).ToList();
        if (!OverlapRule.CanPlace(chart, moves.Select(m => (kind, m.Lane, m.Y, m.From.Note.Length)).ToList(), notes.Select(n => n.Note).ToHashSet())) return null;
        return new TransformNotesCommand("Set note lane", moves);
    }
}

/// <summary>
/// Text forms of the inspector's position and length fields. Position: a pulse ("1440") or a musical position
/// "measure:beat" / "measure:beat+pulses" (1-based, as the editor shows it). Length: pulses ("120"), beats ("1.5b") or a
/// fraction of a beat ("1/4" = a quarter of a beat).
/// </summary>
public static class InspectorText
{
    private static readonly System.Globalization.CultureInfo inv = System.Globalization.CultureInfo.InvariantCulture;

    public static string Position(Timing.TimingMap timing, long pulse)
    {
        var p = timing.PositionOf(Math.Max(0, pulse));
        return p.PulseInBeat == 0 ? $"{p.Measure + 1}:{p.Beat + 1}" : $"{p.Measure + 1}:{p.Beat + 1}+{p.PulseInBeat}";
    }

    public static long? ParsePosition(string text, Timing.TimingMap timing)
    {
        text = text.Trim();
        if (long.TryParse(text, System.Globalization.NumberStyles.Integer, inv, out long pulse)) return pulse >= 0 ? pulse : null;

        var plus = text.Split('+');
        if (plus.Length > 2) return null;
        var mb = plus[0].Split(':');
        if (mb.Length != 2 || !int.TryParse(mb[0], out int m) || !int.TryParse(mb[1], out int b) || m < 1 || b < 1) return null;
        long extra = 0;
        if (plus.Length == 2 && !long.TryParse(plus[1], out extra)) return null;
        if (b > timing.MeasureLength(m - 1) / timing.BeatLength(m - 1)) return null;
        return timing.ToPulse(m - 1, b - 1, extra);
    }

    public static string Length(long pulses)
    {
        if (pulses == 0) return "0";
        double beats = (double)pulses / Charts.Timebase.PulsesPerQuarter;
        return $"{pulses} ({beats.ToString("0.###", inv)} beat{(beats == 1 ? "" : "s")})";
    }

    public static long? ParseLength(string text)
    {
        text = text.Trim().ToLowerInvariant();
        int paren = text.IndexOf('(');
        if (paren > 0) text = text[..paren].Trim();
        if (long.TryParse(text, System.Globalization.NumberStyles.Integer, inv, out long p)) return p >= 0 ? p : null;
        if (text.EndsWith('b') && double.TryParse(text[..^1], System.Globalization.NumberStyles.Float, inv, out double beats) && beats >= 0)
            return (long)Math.Round(beats * Charts.Timebase.PulsesPerQuarter);
        var f = text.Split('/');
        if (f.Length == 2 && double.TryParse(f[0], System.Globalization.NumberStyles.Float, inv, out double n) && double.TryParse(f[1], System.Globalization.NumberStyles.Float, inv, out double d) && d > 0 && n >= 0)
            return (long)Math.Round(n / d * Charts.Timebase.PulsesPerQuarter);
        return null;
    }
}
