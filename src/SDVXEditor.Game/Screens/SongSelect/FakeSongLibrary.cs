namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>Plain record implementation of <see cref="ISongListItem"/>.</summary>
public sealed record SongListItem(
    string Title,
    string Artist,
    string Charter,
    string Illustrator,
    string? JacketPath,
    DifficultySlot Slot,
    int Level,
    double? EstimatedLevel,
    double BpmMin,
    double BpmMax,
    double LengthMs,
    int Chips,
    int Holds,
    int Lasers,
    string Folder,
    SongSource Source,
    DateTime Created,
    DateTime Updated,
    string Tags,
    float Hue) : ISongListItem;

/// <summary>Invented songs for the chrome until the Core library exists. Not real songs.</summary>
public static class FakeSongLibrary
{
    public static IReadOnlyList<ISongListItem> Create()
    {
        var list = new List<ISongListItem>();

        void song(string title, string artist, string charter, double bpm, double bpmMax, int seconds, float hue, SongSource source, params (DifficultySlot slot, int level)[] charts)
        {
            int n = 0;
            foreach (var (slot, level) in charts)
            {
                n++;
                int chips = 180 + level * 48 + n * 7;
                list.Add(new SongListItem(
                    title, artist, charter, "placeholder illustrator", null,
                    slot, level, level - 0.4 + n * 0.1,
                    bpm, bpmMax, seconds * 1000.0,
                    chips, chips / 6, 40 + level * 9,
                    title.ToLowerInvariant().Replace(' ', '_'),
                    source,
                    new DateTime(2026, 6, 27, 15, 47, 0).AddDays(-list.Count * 3),
                    new DateTime(2026, 10, 20, 22, 44, 0).AddHours(-list.Count * 5),
                    "electronic placeholder test chart sdvx nautica",
                    hue));
            }
        }

        song("Neon Cascade", "Placeholder Unit", "JOFOX727", 165, 165, 119, 0.52f, SongSource.Editor,
            (DifficultySlot.Novice, 6), (DifficultySlot.Advanced, 12), (DifficultySlot.Exhaust, 16), (DifficultySlot.Maximum, 18));
        song("Paper Satellite", "Sample Artist", "kshoot_fan", 140, 280, 132, 0.08f, SongSource.Launcher,
            (DifficultySlot.Novice, 5), (DifficultySlot.Advanced, 11), (DifficultySlot.Exhaust, 15), (DifficultySlot.Infinite, 17));
        song("Glass Horizon", "Test Signal", "charter_b", 207, 207, 98, 0.78f, SongSource.Launcher,
            (DifficultySlot.Advanced, 13), (DifficultySlot.Exhaust, 17), (DifficultySlot.Maximum, 19));
        song("Midnight Relay", "Demo Ensemble", "JOFOX727", 174, 174, 145, 0.62f, SongSource.Editor,
            (DifficultySlot.Exhaust, 14), (DifficultySlot.Gravity, 18));
        song("Velvet Static", "Mock Orchestra", "nautica_user", 128, 128, 104, 0.95f, SongSource.Launcher,
            (DifficultySlot.Novice, 4), (DifficultySlot.Advanced, 9), (DifficultySlot.Exhaust, 13), (DifficultySlot.Heavenly, 16));
        song("Lumen Drift", "Synthetic Choir", "charter_c", 190, 190, 126, 0.30f, SongSource.Launcher,
            (DifficultySlot.Exhaust, 16), (DifficultySlot.Vivid, 18), (DifficultySlot.Exceed, 20));
        song("Copper Rain", "Example Band", "JOFOX727", 150, 300, 88, 0.12f, SongSource.Editor,
            (DifficultySlot.Advanced, 10), (DifficultySlot.Exhaust, 15));

        return list;
    }
}
