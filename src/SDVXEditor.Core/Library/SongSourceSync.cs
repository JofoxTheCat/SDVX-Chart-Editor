using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Library;

/// <summary>
/// Song sources are song-level (<see cref="EditorInfo.SongSources"/>), but kson/ksh store them per difficulty file.
/// When the edited chart is saved with a changed list, the editor copies it into the song's other <c>.kson</c>
/// difficulties so every file agrees. A <c>.ksh</c> is never rewritten (the editor never overwrites a ksh); readers
/// take the first non-empty list anyway (<see cref="SongFolder.SongSources"/>).
/// </summary>
public static class SongSourceSync
{
    /// <summary>
    /// Writes <paramref name="sources"/> into every other <c>.kson</c> chart of <paramref name="song"/> whose list differs
    /// (read fresh from disk, written through a temp file). The in-memory chart objects of the song get the list too.
    /// Returns the rewritten files.
    /// </summary>
    public static IReadOnlyList<string> WriteToSiblings(SongFolder song, string activeChartPath, IReadOnlyList<string> sources)
    {
        var written = new List<string>();
        string active = Path.GetFullPath(activeChartPath);

        foreach (var sc in song.Charts)
        {
            if (string.Equals(Path.GetFullPath(sc.FilePath), active, StringComparison.OrdinalIgnoreCase)) continue;
            if (sc.Format != ChartFormat.Kson || !File.Exists(sc.FilePath)) continue;

            var chart = KsonReader.ReadFile(sc.FilePath);
            if (!chart.Editor.SongSources.SequenceEqual(sources, StringComparer.Ordinal))
            {
                chart.Editor.SongSources = sources.ToArray();
                string tmp = sc.FilePath + ".tmp";
                KsonWriter.WriteFile(chart, tmp);
                File.Move(tmp, sc.FilePath, true);
                written.Add(sc.FilePath);
            }

            sc.Chart.Editor.SongSources = sources.ToArray();
        }

        return written;
    }
}
