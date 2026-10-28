using System.Collections.Concurrent;
using osu.Framework.Logging;
using SDVXEditor.Core.Radar;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// Skill radar per chart file, computed in the background on first selection and cached for the session
/// (like the level estimate). The key includes size and write time, so an edited chart is computed again.
/// Values come from the fitted radar model (Core <see cref="SkillRadarModel"/>); without an embedded model it
/// falls back to the classic radar.py port.
/// </summary>
public static class RadarCache
{
    private static readonly ConcurrentDictionary<string, Task<SkillRadar?>> cache = new ConcurrentDictionary<string, Task<SkillRadar?>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The chart file behind a list item, or null (placeholder items have none).</summary>
    public static string? ChartPath(ISongListItem item)
        => item is LibraryChartItem c ? Path.Combine(c.Song.FolderPath, c.Chart.FileName) : null;

    /// <summary>The finished radar of <paramref name="path"/>, if it was computed already.</summary>
    public static SkillRadar? Peek(string path)
        => cache.TryGetValue(key(path), out var t) && t.IsCompletedSuccessfully ? t.Result : null;

    public static Task<SkillRadar?> GetAsync(string path) => cache.GetOrAdd(key(path), _ => Task.Run(() =>
    {
        try
        {
            return (SkillRadar?)Compute(path);
        }
        catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException
                                      or ArgumentException or InvalidOperationException)
        {
            Logger.Log($"skill radar failed for {path}: {e.Message}");
            return null;
        }
    }));

    /// <summary>A fresh (uncached) radar of <paramref name="path"/>, exactly what the cache stores.</summary>
    public static SkillRadar Compute(string path) => SkillRadarModel.Default.EstimateFile(path).Radar;

    private static string key(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return $"{info.FullName}|{(info.Exists ? info.Length : -1)}|{(info.Exists ? info.LastWriteTimeUtc.Ticks : 0)}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return path;
        }
    }
}
