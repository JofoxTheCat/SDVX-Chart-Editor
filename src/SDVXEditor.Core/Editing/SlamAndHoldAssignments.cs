using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Per-slam sound (kson <c>audio.key_sound.laser.slam_event</c>: sound name → pulses; a pulse must be a slam's or the
/// event is ignored). "Auto" (null) = no event, the default slam sound plays.
/// </summary>
public static class SlamSounds
{
    /// <summary>The built-in names in toolbox order (kson spec; ksh <c>chokkakuse=up/down/swing/mute</c>).</summary>
    public static readonly IReadOnlyList<string> Builtin = new[] { "slam_down", "slam_up", "slam_swing", "slam_mute" };

    /// <summary>"Down" for slam_down, a custom file name as it is; "Auto" for null.</summary>
    public static string Label(string? sound) => sound switch
    {
        null => "Auto",
        "slam_down" => "Down",
        "slam_up" => "Up",
        "slam_swing" => "Swing",
        "slam_mute" => "Mute",
        _ => sound,
    };

    /// <summary>The kson name of a toolbox label ("Down" → slam_down, "Auto" → null, anything else as it is).</summary>
    public static string? FromLabel(string? label) => label switch
    {
        null or "" or "Auto" => null,
        "Down" => "slam_down",
        "Up" => "slam_up",
        "Swing" => "slam_swing",
        "Mute" => "slam_mute",
        _ => label,
    };

    /// <summary>The sound set on a pulse, or null (default).</summary>
    public static string? At(Chart chart, long pulse) =>
        chart.Audio.KeySound.LaserSlamEvents.FirstOrDefault(kv => kv.Value.Contains(pulse)).Key;

    /// <summary>Sets (or clears, with null) the sound of every given slam pulse; null when nothing would change.</summary>
    public static SetSlamSoundCommand? Set(Chart chart, IEnumerable<long> pulses, string? sound)
    {
        var list = pulses.Distinct().OrderBy(p => p).ToList();
        if (list.Count == 0 || list.All(p => At(chart, p) == sound)) return null;
        return new SetSlamSoundCommand(list, sound);
    }
}

/// <summary>Replaces the slam sound of some pulses; undo restores the whole slam-event table as it was.</summary>
public sealed class SetSlamSoundCommand : IEditCommand
{
    private readonly IReadOnlyList<long> pulses;
    private Dictionary<string, List<long>>? snapshot;

    public SetSlamSoundCommand(IReadOnlyList<long> pulses, string? sound)
    {
        this.pulses = pulses;
        Sound = sound;
    }

    public string? Sound { get; }
    public IReadOnlyList<long> Pulses => pulses;
    public string Description => Sound == null ? "Reset slam sound" : $"Slam sound {SlamSounds.Label(Sound)}";

    public void Execute(Chart chart)
    {
        var events = chart.Audio.KeySound.LaserSlamEvents;
        snapshot = events.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var set = pulses.ToHashSet();

        foreach (var key in events.Keys.ToList())
        {
            events[key].RemoveAll(set.Contains);
            if (events[key].Count == 0) events.Remove(key);
        }

        if (Sound == null) return;
        if (!events.TryGetValue(Sound, out var list)) events[Sound] = list = new List<long>();
        list.AddRange(pulses);
        list.Sort();
    }

    public void Undo(Chart chart) => chart.Audio.KeySound.LaserSlamEvents = snapshot!.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
}

/// <summary>FX hold effects from Compose: the toolbox choice and K-Shoot Editor's Ctrl+click cycle.</summary>
public static class HoldEffects
{
    /// <summary>The effect of a hold (its first event at the hold start), or null.</summary>
    public static string? Of(Note hold) => hold.Effects.FirstOrDefault(e => e.RelY == 0)?.Name ?? hold.Effects.FirstOrDefault()?.Name;

    /// <summary>
    /// The next effect in <see cref="EffectCatalog.AvailableNames"/> (user definitions, then presets) after the hold's
    /// current one, wrapping through "none" (null). <paramref name="direction"/> −1 goes backwards.
    /// </summary>
    public static string? Next(Chart chart, Note hold, int direction = 1)
    {
        var names = new List<string?> { null };
        names.AddRange(EffectCatalog.AvailableNames(chart, laser: false));
        int i = names.IndexOf(Of(hold));
        if (i < 0) i = 0;
        return names[((i + direction) % names.Count + names.Count) % names.Count];
    }

    /// <summary>One undo step: every given FX hold gets <paramref name="effect"/> (null clears); chips are skipped. Null when nothing changes.</summary>
    public static IEditCommand? Assign(IEnumerable<NoteRef> notes, string? effect)
    {
        var holds = notes.Where(n => n.Kind == NoteKind.Fx && n.Note.IsHold).Select(n => n.Note)
                         .Where(n => Of(n) != effect || n.Effects.Count > 1).ToList();
        if (holds.Count == 0) return null;
        return EffectCommands.AssignToHolds(holds, effect);
    }

    /// <summary>The toolbox label for an effect name ("Auto" = keep / none).</summary>
    public static string? FromLabel(string? label) => label is null or "" or "Auto" ? null : label;
}

/// <summary>Swap Laser Colour (K-Shoot Editor): selected laser sections change lane, values and curves kept (unlike Mirror).</summary>
public static class LaserSwap
{
    /// <summary>The command, or null when nothing is selected or a swapped section would overlap one on the other lane.</summary>
    public static IEditCommand? Swap(Chart chart, Selection selection)
    {
        var sections = selection.Sections.ToList();
        if (sections.Count == 0) return null;
        var items = sections.Select(s => (s.Lane, s.Section, 1 - s.Lane, s.Section.Clone())).ToList();
        var moved = sections.Select(s => s.Section).ToHashSet();
        if (!OverlapRule.CanPlace(chart, items.Select(i => (i.Item3, i.Item4)).ToList(), moved)) return null;
        return new ReplaceLaserSectionsCommand("Swap laser colour", items);
    }
}
