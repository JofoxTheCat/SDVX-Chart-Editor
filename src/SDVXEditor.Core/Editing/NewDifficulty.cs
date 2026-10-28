using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// File → Create a New Difficulty (osu!: "copy of the current difficulty" or "empty, keep timing and metadata"):
/// which slots are free, the new chart and the file it is saved as. The file name decides the slot (kson/ksh cannot
/// state MXM; <see cref="Difficulty.FromFileName"/> reads the name first), so a new MXM is <c>mxm.kson</c>.
/// </summary>
public static class NewDifficulty
{
    private static readonly string[] stems = { "nov", "adv", "exh", "inf", "mxm" };

    /// <summary>Slots 0..4 no chart of the song uses yet, in order.</summary>
    public static IReadOnlyList<int> FreeSlots(IEnumerable<int> usedSlots)
    {
        var used = usedSlots.ToHashSet();
        return Enumerable.Range(0, 5).Where(s => !used.Contains(s)).ToList();
    }

    /// <summary>
    /// The chart for <paramref name="slot"/>: a deep copy of <paramref name="source"/> (kson round trip) with its notes, or —
    /// <paramref name="copyNotes"/> false — the same song with timing (BPM, time signatures, scroll speed, stops),
    /// metadata, audio, background and bookmarks but no notes, lasers, camera work or note-bound effects.
    /// </summary>
    public static Chart Create(Chart source, int slot, bool copyNotes)
    {
        if (slot is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(slot));

        var chart = KsonReader.Read(KsonWriter.Write(source));
        chart.Meta.DifficultySlot = slot;
        chart.Meta.DifficultyName = null;

        if (!copyNotes)
        {
            chart.Notes = new NoteData();
            chart.Camera = new CameraInfo();
            chart.EndPulse = null;
            chart.Audio.KeySound = new KeySoundInfo();
            chart.Audio.FxEffects.OrphanLongEvents.Clear();
            chart.Audio.FxEffects.ParamChanges.Clear();
            chart.Audio.LaserEffects.ParamChanges.Clear();
            chart.Audio.LaserEffects.PulseEvents.Clear();
            chart.Meta.Level = 1;
        }

        return chart;
    }

    /// <summary>
    /// A file name for a new chart of <paramref name="slot"/> that no file of the folder uses (any extension):
    /// <c>exh.kson</c>, else <c>exh_2.kson</c> … — every one resolves back to the slot by name.
    /// </summary>
    public static string FileName(int slot, IEnumerable<string> existingFileNames)
    {
        var taken = existingFileNames.Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).ToHashSet();
        string stem = stems[slot];
        string name = stem;
        for (int i = 2; taken.Contains(name); i++)
            name = $"{stem}_{i}";
        return name + ".kson";
    }
}
