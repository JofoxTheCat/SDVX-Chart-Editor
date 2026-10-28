using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Library;

/// <summary>One chart as listed in a <see cref="SvzManifest"/>.</summary>
public sealed record SvzChartInfo(string File, int Slot, string Difficulty, double Level, string Charter, IReadOnlyList<SvzBookmarkInfo> Bookmarks);

/// <summary>One bookmark of a chart in the manifest summary (the charts themselves carry the real data).</summary>
public sealed record SvzBookmarkInfo(long Pulse, long TimeMs, string Label, string? Colour);

/// <summary>
/// <c>sdvxeditor.json</c> inside a <c>.svz</c> package (docs/SVZ_FORMAT.md). Informational: the package
/// imports without it, and an unreadable manifest is ignored with a warning, never an error.
/// </summary>
public sealed class SvzManifest
{
    public const string FileName = "sdvxeditor.json";
    public const string FormatId = "sdvx-editor-svz";
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public string EditorVersion { get; init; } = "";
    public string Folder { get; init; } = "";
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public DateTimeOffset? ExportedAt { get; init; }
    public IReadOnlyList<SvzChartInfo> Charts { get; init; } = Array.Empty<SvzChartInfo>();
    public string CollabNotes { get; init; } = "";

    /// <summary>Written by a newer editor: fields this version does not know are ignored.</summary>
    public bool IsNewerFormat => FormatVersion > CurrentFormatVersion;

    public string ToJson()
    {
        var o = new JsonObject
        {
            ["format"] = FormatId,
            ["format_version"] = FormatVersion,
            ["editor_version"] = EditorVersion,
            ["folder"] = Folder,
            ["title"] = Title,
            ["artist"] = Artist,
        };
        if (ExportedAt is { } at) o["exported_at"] = at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        o["charts"] = new JsonArray(Charts.Select(c => (JsonNode)new JsonObject
        {
            ["file"] = c.File,
            ["slot"] = c.Slot,
            ["difficulty"] = c.Difficulty,
            ["level"] = c.Level,
            ["charter"] = c.Charter,
            ["bookmarks"] = new JsonArray(c.Bookmarks.Select(b =>
            {
                var bo = new JsonObject { ["y"] = b.Pulse, ["time_ms"] = b.TimeMs, ["label"] = b.Label };
                if (b.Colour != null) bo["color"] = b.Colour;
                return (JsonNode)bo;
            }).ToArray()),
        }).ToArray());
        if (CollabNotes.Length > 0) o["collab_notes"] = CollabNotes;

        return o.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }

    /// <summary>
    /// Tolerant parse: unknown keys are ignored, missing ones default, wrong types fall back. Throws
    /// <see cref="FormatException"/> only when the text is not a JSON object or names another format.
    /// </summary>
    public static SvzManifest Parse(string json)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
                   ?? throw new FormatException("the manifest is not a JSON object");
        }
        catch (JsonException e)
        {
            throw new FormatException("the manifest is not valid JSON: " + e.Message, e);
        }

        if (root["format"] is { } f && str(f) is { Length: > 0 } id && id != FormatId)
            throw new FormatException($"unknown manifest format '{id}'");

        var charts = new List<SvzChartInfo>();
        if (root["charts"] is JsonArray arr)
        {
            foreach (var c in arr.OfType<JsonObject>())
            {
                var bookmarks = new List<SvzBookmarkInfo>();
                if (c["bookmarks"] is JsonArray ba)
                {
                    foreach (var b in ba.OfType<JsonObject>())
                        bookmarks.Add(new SvzBookmarkInfo((long)num(b["y"]), (long)num(b["time_ms"]), Bookmark.CleanLabel(str(b["label"])), Bookmark.NormaliseColour(str(b["color"]))));
                }

                charts.Add(new SvzChartInfo(str(c["file"]), (int)num(c["slot"], -1), str(c["difficulty"]), num(c["level"]), str(c["charter"]), bookmarks));
            }
        }

        DateTimeOffset? exported = DateTimeOffset.TryParse(str(root["exported_at"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

        return new SvzManifest
        {
            FormatVersion = (int)num(root["format_version"], CurrentFormatVersion),
            EditorVersion = str(root["editor_version"]),
            Folder = str(root["folder"]),
            Title = str(root["title"]),
            Artist = str(root["artist"]),
            ExportedAt = exported,
            Charts = charts,
            CollabNotes = str(root["collab_notes"]),
        };

        static string str(JsonNode? n) => n is JsonValue v && v.TryGetValue(out string? s) ? s : "";

        static double num(JsonNode? n, double fallback = 0)
        {
            if (n is not JsonValue v) return fallback;
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out long l)) return l;
            if (v.TryGetValue(out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return fallback;
        }
    }
}

public sealed class SvzExportOptions
{
    /// <summary>Free text for collaborators ("Part B is yours, bookmarks mark the sections").</summary>
    public string CollabNotes { get; init; } = "";

    /// <summary>Export time written into the manifest (tests pin it).</summary>
    public DateTimeOffset? Now { get; init; }

    /// <summary>The importer's caps: a package it would refuse is not written.</summary>
    public ZipLimits Limits { get; init; } = ZipLimits.Default;
}

public sealed record SvzExportResult(string Path, SvzManifest Manifest, IReadOnlyList<string> Warnings);

/// <summary>
/// The editor's share / collab package, like osu!'s <c>.osz</c>: a plain zip of ONE song folder
/// (<c>&lt;folder&gt;/…</c>) with every chart as <c>.kson</c> (a chart read from <c>.ksh</c> also keeps its
/// original), audio, jackets, backgrounds, key sounds and any other files, plus
/// <c>&lt;folder&gt;/sdvxeditor.json</c> (<see cref="SvzManifest"/>). Import goes through
/// <see cref="SongImporter"/> like any zip (same zip-slip and size checks); the manifest is optional.
/// Format description: docs/SVZ_FORMAT.md.
/// </summary>
public static class SvzPackage
{
    public const string Extension = ".svz";

    /// <summary>Editor-private files that never go into a package (the fork marker holds a local path).</summary>
    private static readonly string[] excludedNames = { SongLibrary.ForkMarkerName, SvzManifest.FileName, "Thumbs.db", "desktop.ini", ".DS_Store" };

    public static string EditorVersion =>
        typeof(SvzPackage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "";

    /// <summary>Writes <paramref name="songFolder"/> as a package to <paramref name="destination"/> (replaced if it exists).</summary>
    public static SvzExportResult Export(string songFolder, string destination, SvzExportOptions? options = null)
    {
        options ??= new SvzExportOptions();
        songFolder = Path.GetFullPath(songFolder).TrimEnd('\\', '/');
        var song = SongFolder.Load(songFolder);
        if (song.Charts.Count == 0)
            throw new InvalidOperationException($"{songFolder} has no readable chart to package");

        string name = song.Name;
        var warnings = song.Errors.Select(e => $"{Path.GetFileName(e.File)} not readable, packaged as is: {e.Error}").ToList();

        // files of the folder (recursive, hidden and editor-private ones skipped), keyed by package path
        var entries = new SortedDictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(songFolder, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(songFolder, file).Replace('\\', '/');
            var parts = rel.Split('/');
            if (parts.Any(p => p.StartsWith('.')) || excludedNames.Contains(parts[^1], StringComparer.OrdinalIgnoreCase)) continue;
            if (rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) continue;
            string src = file;
            entries[rel] = () => File.OpenRead(src);
        }

        // every chart as .kson: a ksh chart gets <stem>.kson beside its original (which stays in the package)
        var chartInfos = new List<SvzChartInfo>();
        foreach (var sc in song.Charts.OrderBy(c => c.Slot))
        {
            string file = Path.GetFileName(sc.FilePath);
            if (sc.Format == ChartFormat.Ksh)
            {
                string kson = Path.ChangeExtension(Path.GetRelativePath(songFolder, sc.FilePath), ".kson").Replace('\\', '/');
                byte[] bytes = new UTF8Encoding(false).GetBytes(KsonWriter.Write(sc.Chart));
                entries[kson] = () => new MemoryStream(bytes, false);
                file = Path.GetFileName(kson);
            }

            var timing = TimingMap.FromChart(sc.Chart);
            var m = sc.Chart.Meta;
            chartInfos.Add(new SvzChartInfo(file, sc.Slot, Difficulty.ShortName(sc.Slot), m.Level, m.ChartAuthor,
                sc.Chart.Editor.Bookmarks.Select(b => new SvzBookmarkInfo(b.Y, (long)Math.Round(timing.PulseToMs(b.Y)), Bookmark.CleanLabel(b.Value.Label), Bookmark.NormaliseColour(b.Value.Colour))).ToList()));
        }

        var manifest = new SvzManifest
        {
            EditorVersion = EditorVersion,
            Folder = name,
            Title = song.Title,
            Artist = song.Charts.Select(c => c.Chart.Meta.Artist).FirstOrDefault(a => a.Length > 0) ?? "",
            ExportedAt = options.Now ?? DateTimeOffset.UtcNow,
            Charts = chartInfos,
            CollabNotes = options.CollabNotes.Trim(),
        };
        byte[] manifestBytes = new UTF8Encoding(false).GetBytes(manifest.ToJson());
        entries[SvzManifest.FileName] = () => new MemoryStream(manifestBytes, false);

        // never write what the importer would refuse
        var limits = options.Limits;
        if (entries.Count > limits.MaxEntries)
            throw new InvalidOperationException($"the song folder has {entries.Count} files, more than a package may hold ({limits.MaxEntries})");

        string dest = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        string partial = dest + ".partial";

        try
        {
            long total = 0;
            using (var fs = new FileStream(partial, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (var (rel, open) in entries)
                {
                    var entry = zip.CreateEntry(name + "/" + rel, CompressionLevel.Optimal);
                    using var input = open();
                    using var output = entry.Open();
                    input.CopyTo(output);
                    total += input.Length;
                    if (total > limits.MaxUnpackedBytes)
                        throw new InvalidOperationException($"the song folder is larger than a package may unpack to ({limits.MaxUnpackedBytes / (1024 * 1024)} MiB)");
                }
            }

            if (new FileInfo(partial).Length > limits.MaxArchiveBytes)
                throw new InvalidOperationException($"the package would be larger than {limits.MaxArchiveBytes / (1024 * 1024)} MiB");

            File.Move(partial, dest, true);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }

        return new SvzExportResult(dest, manifest, warnings);
    }

    /// <summary>
    /// Import side: reads and removes <c>sdvxeditor.json</c> from an unpacked song folder. Missing = null;
    /// unreadable = null plus a warning (the songs still import).
    /// </summary>
    public static SvzManifest? TakeManifest(string songDir, ICollection<string> warnings)
    {
        string path = Path.Combine(songDir, SvzManifest.FileName);
        if (!File.Exists(path)) return null;

        try
        {
            if (new FileInfo(path).Length > 1024 * 1024)
            {
                warnings.Add("package manifest ignored: larger than 1 MiB");
                return null;
            }

            var manifest = SvzManifest.Parse(File.ReadAllText(path));
            if (manifest.IsNewerFormat)
                warnings.Add($"package written by a newer editor (format {manifest.FormatVersion}); unknown parts ignored");
            return manifest;
        }
        catch (FormatException e)
        {
            warnings.Add("package manifest ignored: " + e.Message);
            return null;
        }
        finally
        {
            try { File.Delete(path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}
