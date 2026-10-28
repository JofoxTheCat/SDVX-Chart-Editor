using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Charts;

public enum ChartFormat
{
    Ksh,
    Kson,
}

/// <summary>One difficulty of a song folder.</summary>
public sealed class SongChart
{
    public required string FilePath { get; init; }
    public required ChartFormat Format { get; init; }
    public required Chart Chart { get; init; }

    /// <summary>Resolved slot 0..4 (filename first, then the chart's own difficulty). Also written to Chart.Meta.</summary>
    public int Slot => Chart.Meta.DifficultySlot;

    /// <summary>Jacket file for this chart, from its own declaration; null if the folder has no image.</summary>
    public string? JacketPath { get; init; }

    /// <summary>True when the declared jacket was missing and another image was taken.</summary>
    public bool JacketIsFallback { get; init; }

    public string? AudioPath { get; init; }
}

/// <summary>
/// A song = a folder with one chart per difficulty plus audio, jacket(s) and background files.
/// Rules ported from ksh2vox: <c>pick_charts</c> (.kson beats a .ksh with the same stem),
/// <c>_resolve_diff</c> (filename first) and <c>_resolve_jacket</c> (the chart's declared jacket,
/// never "the first image"; converter CLAUDE.md §8a/§8b/§9).
/// </summary>
public sealed class SongFolder
{
    public static readonly string[] ChartExtensions = { ".ksh", ".kson" };
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };
    public static readonly string[] AudioExtensions = { ".ogg", ".mp3", ".wav", ".flac", ".m4a", ".aac", ".opus", ".wma", ".s3v" };

    public string FolderPath { get; }
    public string Name => Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public IReadOnlyList<SongChart> Charts { get; }

    /// <summary>Chart files that could not be read, with the reason.</summary>
    public IReadOnlyList<(string File, string Error)> Errors { get; }

    private SongFolder(string folder, List<SongChart> charts, List<(string, string)> errors)
    {
        FolderPath = folder;
        Charts = charts;
        Errors = errors;
    }

    public SongChart? this[int slot] => Charts.FirstOrDefault(c => c.Slot == slot);

    /// <summary>Title of the song (from the first chart that states one).</summary>
    public string Title => Charts.Select(c => c.Chart.Meta.Title).FirstOrDefault(t => t.Length > 0) ?? Name;

    /// <summary>
    /// The song's sources (<see cref="EditorInfo.SongSources"/>): the first chart, in <see cref="Charts"/> order, whose
    /// list is not empty; empty when none has one. Song Setup writes the same list into every difficulty it saves.
    /// </summary>
    public IReadOnlyList<string> SongSources => Charts.Select(c => c.Chart.Editor.SongSources).FirstOrDefault(s => s.Count > 0) ?? Array.Empty<string>();

    public static bool IsChartFile(string path) => ChartExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// One chart file per difficulty, preferring .kson over a .ksh with the same stem
    /// (Nautica packs increasingly ship both). Sorted ordinally like the converter.
    /// </summary>
    public static IReadOnlyList<string> PickChartFiles(IEnumerable<string> fileNames)
    {
        var charts = fileNames.Where(IsChartFile).ToList();
        var ksonStems = charts.Where(f => f.EndsWith(".kson", StringComparison.OrdinalIgnoreCase))
                              .Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).ToHashSet();
        return charts.Where(f => f.EndsWith(".kson", StringComparison.OrdinalIgnoreCase)
                                 || !ksonStems.Contains(Path.GetFileNameWithoutExtension(f).ToLowerInvariant()))
                     .OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    public static SongFolder Load(string folder)
    {
        var files = Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().ToList();
        var charts = new List<SongChart>();
        var errors = new List<(string, string)>();
        foreach (string name in PickChartFiles(files))
        {
            string path = Path.Combine(folder, name);
            try
            {
                bool kson = name.EndsWith(".kson", StringComparison.OrdinalIgnoreCase);
                var chart = kson ? KsonReader.ReadFile(path) : KshReader.ReadFile(path);
                // Filename wins (he4ven's mxm.ksh says "infinite"); kson/ksh cannot state MXM.
                if (Difficulty.FromFileName(name, out string? variant) is { } slot)
                {
                    chart.Meta.DifficultySlot = slot;
                    chart.Meta.DifficultyName ??= variant;
                }
                string? jacket = ResolveImage(folder, chart.Meta.JacketFilename, out bool fallback);
                charts.Add(new SongChart
                {
                    FilePath = path,
                    Format = kson ? ChartFormat.Kson : ChartFormat.Ksh,
                    Chart = chart,
                    JacketPath = jacket,
                    JacketIsFallback = fallback,
                    AudioPath = ResolveAudio(folder, chart.Audio.Bgm.Filename),
                });
            }
            catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                errors.Add((path, e.Message));
            }
        }
        charts.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : string.CompareOrdinal(a.FilePath, b.FilePath));
        return new SongFolder(folder, charts, errors);
    }

    /// <summary>
    /// The image a chart declares (exact name, then case-insensitive). If it is missing, falls back to
    /// the first image (ordinal order) and reports that via <paramref name="isFallback"/>.
    /// </summary>
    public static string? ResolveImage(string folder, string? declared, out bool isFallback)
    {
        isFallback = false;
        var images = Directory.EnumerateFiles(folder)
                              .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                              .OrderBy(f => f, StringComparer.Ordinal).ToList();
        if (images.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(declared))
        {
            string name = Path.GetFileName(declared.Trim().Replace('\\', '/'));
            var hit = images.FirstOrDefault(f => Path.GetFileName(f) == name)
                      ?? images.FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit;
        }
        isFallback = true;
        return images[0];
    }

    /// <summary>The audio file named by the chart (first existing of <c>a;b</c>, tolerating a wrong extension), else the first audio file.</summary>
    public static string? ResolveAudio(string folder, string? declared)
    {
        var files = Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.Ordinal).ToList();
        foreach (string raw in (declared ?? "").Split(';', ','))
        {
            string name = raw.Trim().Trim('"');
            if (name.Length == 0) continue;
            string p = Path.Combine(folder, name);
            if (File.Exists(p)) return p;
            string stem = Path.GetFileNameWithoutExtension(name);
            var byStem = files.FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), stem, StringComparison.OrdinalIgnoreCase)
                                                   && AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            if (byStem != null) return byStem;
        }
        foreach (string ext in AudioExtensions)
        {
            var f = files.FirstOrDefault(x => x.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            if (f != null) return f;
        }
        return null;
    }
}
