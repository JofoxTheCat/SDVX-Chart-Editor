using osu.Framework.Graphics;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>SDVX difficulty slots in music_db order (slot 3 is the per-version variant).</summary>
public enum DifficultySlot
{
    Novice,
    Advanced,
    Exhaust,
    Infinite,
    Gravity,
    Heavenly,
    Vivid,
    Exceed,
    Nabla,
    Maximum,
}

public static class DifficultySlotExtensions
{
    public static string ShortName(this DifficultySlot slot) => slot switch
    {
        DifficultySlot.Novice => "NOV",
        DifficultySlot.Advanced => "ADV",
        DifficultySlot.Exhaust => "EXH",
        DifficultySlot.Infinite => "INF",
        DifficultySlot.Gravity => "GRV",
        DifficultySlot.Heavenly => "HVN",
        DifficultySlot.Vivid => "VVD",
        DifficultySlot.Exceed => "XCD",
        DifficultySlot.Nabla => "NBL",
        DifficultySlot.Maximum => "MXM",
        _ => slot.ToString(),
    };

    private static readonly Colour4 nabla = Colour4.FromHex("8f7cff");

    /// <summary>Core slot (0 NOV … 3 INF-class, 4 MXM) plus the variant name ("GRV", "HVN" …) to the display slot.</summary>
    public static DifficultySlot FromCore(int slot, string? name) => slot switch
    {
        0 => DifficultySlot.Novice,
        1 => DifficultySlot.Advanced,
        2 => DifficultySlot.Exhaust,
        4 => DifficultySlot.Maximum,
        _ => (name ?? "").Trim().ToUpperInvariant() switch
        {
            "GRV" => DifficultySlot.Gravity,
            "HVN" => DifficultySlot.Heavenly,
            "VVD" => DifficultySlot.Vivid,
            "XCD" => DifficultySlot.Exceed,
            "NBL" => DifficultySlot.Nabla,
            _ => DifficultySlot.Infinite,
        },
    };

    /// <summary>Display slot back to the Core slot 0-4.</summary>
    public static int ToCore(this DifficultySlot slot) => slot switch
    {
        DifficultySlot.Novice => 0,
        DifficultySlot.Advanced => 1,
        DifficultySlot.Exhaust => 2,
        DifficultySlot.Maximum => 4,
        _ => 3,
    };

    public static Colour4 Colour(this DifficultySlot slot) => slot switch
    {
        DifficultySlot.Novice => EditorColours.Difficulty.Novice,
        DifficultySlot.Advanced => EditorColours.Difficulty.Advanced,
        DifficultySlot.Exhaust => EditorColours.Difficulty.Exhaust,
        DifficultySlot.Infinite => EditorColours.Difficulty.Infinite,
        DifficultySlot.Gravity => EditorColours.Difficulty.Gravity,
        DifficultySlot.Heavenly => EditorColours.Difficulty.Heavenly,
        DifficultySlot.Vivid => EditorColours.Difficulty.Vivid,
        DifficultySlot.Exceed => EditorColours.Difficulty.Exceed,
        DifficultySlot.Nabla => nabla, // assumption, like the other variant colours
        DifficultySlot.Maximum => EditorColours.Difficulty.Maximum,
        _ => Colour4.White,
    };
}

/// <summary>Where a chart in the library comes from.</summary>
public enum SongSource
{
    /// <summary>Downloaded by the launcher (nautica_charts_path); opened read-only by default.</summary>
    Launcher,

    /// <summary>The editor's own songs folder.</summary>
    Editor,
}

/// <summary>
/// One wheel panel = one chart (difficulty) of a song. View-model only: <see cref="LibraryChartItem"/>
/// wraps the real library, <see cref="FakeSongLibrary"/> provides invented data for previews.
/// </summary>
public interface ISongListItem
{
    string Title { get; }
    string Artist { get; }
    string Charter { get; }
    string Illustrator { get; }

    /// <summary>Jacket image on disk, or null (the panel then draws a placeholder).</summary>
    string? JacketPath { get; }

    DifficultySlot Slot { get; }

    /// <summary>Level 1-20.</summary>
    int Level { get; }

    /// <summary>Estimated level from the difficulty model, null when not computed.</summary>
    double? EstimatedLevel { get; }

    double BpmMin { get; }
    double BpmMax { get; }
    double LengthMs { get; }
    int Chips { get; }
    int Holds { get; }
    int Lasers { get; }

    /// <summary>Song folder name; groups the charts of one song.</summary>
    string Folder { get; }

    SongSource Source { get; }
    DateTime Created { get; }
    DateTime Updated { get; }
    /// <summary>Search terms (difficulty name, folder, file name). Not shown: these are not real tags (audit U13).</summary>
    string Tags { get; }

    /// <summary>The chart's file name inside the song folder ("" for invented data).</summary>
    string ChartFile => "";

    /// <summary>Placeholder colour seed (0-1) for jacket/background until images load.</summary>
    float Hue { get; }

    /// <summary>Identity of the song: charts with the same key form one set on the wheel.</summary>
    string SongKey => Folder;

    /// <summary>Identity of the chart across library refreshes (items are rebuilt, keys stay).</summary>
    string ChartKey => SongKey + "|" + Slot;

    /// <summary>Absolute song folder, null for invented data.</summary>
    string? FolderPath => null;

    /// <summary>The library folder the song lives in (for "group by song folder").</summary>
    string RootName => Source == SongSource.Launcher ? "Launcher library" : "Editor songs";

    /// <summary>Background image from the chart's <c>bg</c>, or null (the jacket is then shown blurred).</summary>
    string? BackgroundPath => null;

    string? AudioPath => null;
    double PreviewOffsetMs => 0;
    double PreviewDurationMs => 0;

    /// <summary>The level as declared by the chart (may be fractional in kson).</summary>
    double DeclaredLevel => Level;

    /// <summary>Launcher songs are read-only; editing one saves into a copy.</summary>
    bool IsReadOnly => Source == SongSource.Launcher;

    /// <summary>The song's sources as stored (links to music sites; "Support the Artist" shows the valid ones).</summary>
    IReadOnlyList<string> SongSources => Array.Empty<string>();
}
