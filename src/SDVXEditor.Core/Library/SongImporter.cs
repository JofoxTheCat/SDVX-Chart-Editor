using System.IO.Compression;
using System.Reflection;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Formats.Vox;

namespace SDVXEditor.Core.Library;

public enum SongImportKind
{
    Folder,
    Zip,
    ChartFile,
    Audio,

    /// <summary>The editor's own package (<see cref="SvzPackage"/>): a zip of one song folder plus an optional manifest.</summary>
    Svz,
}

/// <summary>What to do when the target folder name is taken in the editor root.</summary>
public enum ExistingSongPolicy
{
    /// <summary>Import as "name 2", "name 3", … (default — a drop never destroys anything).</summary>
    Rename,

    /// <summary>Delete the existing editor song folder and import in its place.</summary>
    Replace,

    /// <summary>Refuse with <c>song_exists</c>.</summary>
    Fail,
}

/// <summary>
/// Caps for untrusted archives — the launcher bridge's values (converter CLAUDE.md §14.4a): 1 GiB unpacked,
/// 5000 entries, 200× per-entry ratio above 1 MiB. Configurable so tests can use tiny limits.
/// </summary>
public sealed record ZipLimits
{
    public long MaxArchiveBytes { get; init; } = 256L * 1024 * 1024;
    public long MaxUnpackedBytes { get; init; } = 1024L * 1024 * 1024;
    public int MaxEntries { get; init; } = 5000;
    public long MaxRatio { get; init; } = 200;

    /// <summary>Entries up to this size are exempt from the ratio check (a tiny, highly compressible text is normal).</summary>
    public long RatioFloorBytes { get; init; } = 1024 * 1024;

    public static readonly ZipLimits Default = new();
}

public sealed class SongImportOptions
{
    public ExistingSongPolicy Existing { get; init; } = ExistingSongPolicy.Rename;
    public ZipLimits Zip { get; init; } = ZipLimits.Default;

    /// <summary>Slot of the empty chart created for an audio drop (default EXH).</summary>
    public int NewChartSlot { get; init; } = Difficulty.Exhaust;
}

public sealed record SongImportResult(LibrarySong Song, SongImportKind Kind, bool AlreadyInLibrary, IReadOnlyList<string> Warnings)
{
    /// <summary>The package manifest of an imported <c>.svz</c> (or of a zip that carried one); null otherwise.</summary>
    public SvzManifest? Manifest { get; init; }
}

public sealed class SongImportException : Exception
{
    /// <summary>
    /// <c>not_found</c>, <c>unsupported</c>, <c>no_charts</c>, <c>song_exists</c>, <c>unsafe_path</c>,
    /// <c>archive_too_large</c>, <c>archive_layout</c>, <c>archive_invalid</c>.
    /// </summary>
    public string Kind { get; }

    public string? Arg { get; }

    public SongImportException(string kind, string message, string? arg = null, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
        Arg = arg;
    }
}

/// <summary>
/// Drag &amp; drop import into the editor's songs folder: a song folder, a .zip, a single .ksh/.kson
/// (imports the folder it lives in) or an audio file (creates a new song with an empty chart).
/// Everything is unpacked/copied into a <c>.import-*</c> staging folder first and moved into place only when complete.
/// </summary>
public sealed class SongImporter
{
    private static readonly string[] ignoredZipEntries = { "__MACOSX/", ".DS_Store", "Thumbs.db", "desktop.ini" };

    private readonly SongLibrary library;

    public SongImporter(SongLibrary library) => this.library = library;

    public static SongImportKind? Classify(string path)
    {
        if (Directory.Exists(path)) return SongImportKind.Folder;
        if (!File.Exists(path)) return null;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".zip") return SongImportKind.Zip;
        if (ext == SvzPackage.Extension) return SongImportKind.Svz;
        if (SongFolder.IsChartFile(path)) return SongImportKind.ChartFile;
        if (SongFolder.AudioExtensions.Contains(ext)) return SongImportKind.Audio;
        return null;
    }

    public SongImportResult Import(string path, SongImportOptions? options = null)
    {
        options ??= new SongImportOptions();
        string full = Path.GetFullPath(path.Trim().Trim('"'));
        if (!File.Exists(full) && !Directory.Exists(full)) throw new SongImportException("not_found", $"not found: {full}", full);
        var kind = Classify(full) ?? throw new SongImportException("unsupported",
            $"cannot import {Path.GetFileName(full)}: drop a song folder, a .zip or .svz, a .ksh/.kson or an audio file", Path.GetExtension(full));
        return kind switch
        {
            SongImportKind.Folder => importFolder(full, kind, options),
            SongImportKind.ChartFile => importFolder(Path.GetDirectoryName(full)!, kind, options),
            SongImportKind.Zip or SongImportKind.Svz => importZip(full, kind, options),
            SongImportKind.Audio => importAudio(full, options),
            _ => throw new SongImportException("unsupported", $"cannot import {full}"),
        };
    }

    /// <summary>
    /// The editor-root folder name an import of <paramref name="path"/> would use, without importing — so the UI
    /// can ask Replace / Keep both first. Null when the path would not create a new folder (a song already in the
    /// library, an unsupported or unreadable file); audio drops are named from tags, so they report null too.
    /// </summary>
    public string? PlannedFolderName(string path)
    {
        char[] seps = { '\\', '/' };
        string full = Path.GetFullPath(path.Trim().Trim('"'));
        string? name = null;
        switch (Classify(full))
        {
            case SongImportKind.Folder:
            case SongImportKind.ChartFile:
                string folder = Directory.Exists(full) ? full : Path.GetDirectoryName(full)!;
                string? parent = Path.GetDirectoryName(folder.TrimEnd(seps));
                if (parent != null && library.Roots.Any(r => string.Equals(r.Path.TrimEnd(seps), parent.TrimEnd(seps), StringComparison.OrdinalIgnoreCase)))
                    return null;
                name = Path.GetFileName(folder.TrimEnd(seps));
                break;

            case SongImportKind.Zip:
            case SongImportKind.Svz:
                try
                {
                    using var zip = ZipFile.OpenRead(full);
                    var names = zip.Entries.Select(e => e.FullName.Replace(seps[0], '/'))
                        .Where(n => !ignoredZipEntries.Any(i => n.StartsWith(i, StringComparison.OrdinalIgnoreCase) || n.EndsWith("/" + i, StringComparison.OrdinalIgnoreCase) || n == i))
                        .ToList();
                    var top = names.Select(n => n.Split('/')[0]).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    bool flat = names.Any(n => !n.Contains('/') && SongFolder.IsChartFile(n));
                    if (flat) name = Path.GetFileNameWithoutExtension(full);
                    else if (top.Count == 1 && names.All(n => n.Contains('/'))) name = top[0];
                }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    return null;
                }

                break;
        }

        if (string.IsNullOrEmpty(name)) return null;
        name = VoxExporter.SafeFolderName(name);
        return name.StartsWith('.') ? "_" + name[1..] : name;
    }

    /// <summary>True when importing <paramref name="path"/> would meet an existing editor song of the same folder name.</summary>
    public bool WouldReplaceExisting(string path) =>
        PlannedFolderName(path) is { } name && Directory.Exists(Path.Combine(library.EditorRoot, name));

    // ------------------------------------------------------------------ folder

    private SongImportResult importFolder(string folder, SongImportKind kind, SongImportOptions options)
    {
        if (!Directory.EnumerateFiles(folder).Any(SongFolder.IsChartFile))
            throw new SongImportException("no_charts", $"{folder} contains no .ksh or .kson chart", folder);

        // Already a song of the library (editor or launcher root): open it where it is.
        string? parent = Path.GetDirectoryName(folder.TrimEnd('\\', '/'));
        if (parent != null && library.Roots.Any(r => string.Equals(r.Path.TrimEnd('\\', '/'), parent.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
        {
            var song = library.Find(folder) ?? library.Refresh(folder)
                       ?? throw new SongImportException("no_charts", $"{folder} has no readable chart", folder);
            return new SongImportResult(song, kind, true, Array.Empty<string>());
        }
        if (isInside(library.EditorRoot, folder) || isInside(folder, library.EditorRoot))
            throw new SongImportException("unsafe_path", "cannot import a folder that contains, or lies inside, the editor's songs folder", folder);

        string staging = newStaging();
        try
        {
            string src = Path.Combine(staging, "song");
            SongLibrary.CopyDirectory(folder, src);
            return place(src, Path.GetFileName(folder.TrimEnd('\\', '/')), kind, options, new List<string>());
        }
        finally
        {
            deleteQuietly(staging);
        }
    }

    // ------------------------------------------------------------------ zip

    private SongImportResult importZip(string zipPath, SongImportKind kind, SongImportOptions options)
    {
        var limits = options.Zip;
        var warnings = new List<string>();
        if (new FileInfo(zipPath).Length > limits.MaxArchiveBytes)
            throw new SongImportException("archive_too_large", $"the archive is larger than {limits.MaxArchiveBytes / (1024 * 1024)} MiB", (limits.MaxArchiveBytes / (1024 * 1024)).ToString());
        string staging = newStaging();
        try
        {
            string unpacked = Path.Combine(staging, "zip");
            Directory.CreateDirectory(unpacked);
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                SafeExtract(zip, unpacked, limits);
            }
            catch (InvalidDataException e)
            {
                throw new SongImportException("archive_invalid", $"{Path.GetFileName(zipPath)} is not a readable zip archive: {e.Message}", null, e);
            }

            // Layout: exactly one top-level folder (Nautica packs), or flat with the charts at the top.
            // The folder name matters: ksh2vox keys its import map on it (converter CLAUDE.md §3a).
            var dirs = Directory.GetDirectories(unpacked);
            var files = Directory.GetFiles(unpacked);
            string songDir, name;
            if (files.Any(SongFolder.IsChartFile))
            {
                songDir = unpacked;
                name = Path.GetFileNameWithoutExtension(zipPath);
                if (dirs.Length > 0) warnings.Add($"flat archive: {dirs.Length} sub-folder(s) imported as part of the song");
            }
            else if (dirs.Length == 1 && files.Length == 0)
            {
                songDir = dirs[0];
                name = Path.GetFileName(songDir);
            }
            else
            {
                throw new SongImportException("archive_layout",
                    "the archive must contain exactly one song folder (or the chart files directly) — " +
                    $"found {dirs.Length} folder(s) and {files.Length} file(s) at the top", null);
            }
            if (!Directory.EnumerateFiles(songDir).Any(SongFolder.IsChartFile))
                throw new SongImportException("no_charts", $"the archive's song folder '{name}' contains no .ksh or .kson chart", name);
            var manifest = SvzPackage.TakeManifest(songDir, warnings);
            return place(songDir, name, kind, options, warnings) with { Manifest = manifest };
        }
        finally
        {
            deleteQuietly(staging);
        }
    }

    /// <summary>
    /// Extracts an untrusted archive into <paramref name="dest"/>. Refuses zip-slip (absolute paths, drive
    /// letters, <c>..</c> segments, anything resolving outside <paramref name="dest"/>), too many entries,
    /// a declared unpacked total above the cap, and entries expanding beyond the ratio. While copying the
    /// real byte count is enforced too — the declared sizes are only trusted as an upper bound.
    /// </summary>
    public static void SafeExtract(ZipArchive zip, string dest, ZipLimits limits)
    {
        dest = Path.GetFullPath(dest);
        string destPrefix = dest.EndsWith(Path.DirectorySeparatorChar) ? dest : dest + Path.DirectorySeparatorChar;
        var entries = zip.Entries;
        if (entries.Count > limits.MaxEntries)
            throw new SongImportException("archive_too_large", $"the archive has {entries.Count} entries, more than {limits.MaxEntries}", limits.MaxEntries.ToString());

        long declared = 0;
        var plan = new List<(ZipArchiveEntry Entry, string Target, bool IsDir)>();
        foreach (var e in entries)
        {
            string name = e.FullName.Replace('\\', '/');
            if (ignoredZipEntries.Any(i => name.StartsWith(i, StringComparison.OrdinalIgnoreCase) || name.EndsWith("/" + i, StringComparison.OrdinalIgnoreCase) || name == i))
                continue;
            var segments = name.Split('/');
            if (name.StartsWith('/') || name.Contains(':') || segments.Any(s => s == ".."))
                throw new SongImportException("unsafe_path", $"refusing unsafe path in archive: {e.FullName}", e.FullName);
            if (segments.Any(s => s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new SongImportException("unsafe_path", $"refusing invalid file name in archive: {e.FullName}", e.FullName);
            string target = Path.GetFullPath(Path.Combine(dest, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destPrefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(target.TrimEnd(Path.DirectorySeparatorChar), dest, StringComparison.OrdinalIgnoreCase))
                throw new SongImportException("unsafe_path", $"refusing path outside the song folder: {e.FullName}", e.FullName);
            bool isDir = name.EndsWith('/');
            if (!isDir && e.Length > limits.RatioFloorBytes && e.Length > Math.Max(e.CompressedLength, 1) * limits.MaxRatio)
                throw new SongImportException("archive_too_large", $"{e.FullName} would expand more than {limits.MaxRatio}-fold", limits.MaxRatio.ToString());
            declared += e.Length;
            if (declared > limits.MaxUnpackedBytes)
                throw new SongImportException("archive_too_large", $"the unpacked archive is larger than {limits.MaxUnpackedBytes / (1024 * 1024)} MiB", (limits.MaxUnpackedBytes / (1024 * 1024)).ToString());
            plan.Add((e, target, isDir));
        }

        long written = 0;
        byte[] buffer = new byte[81920];
        foreach (var (e, target, isDir) in plan)
        {
            if (isDir)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = e.Open();
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write);
            long entryBytes = 0;
            int n;
            while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                entryBytes += n;
                written += n;
                if (entryBytes > e.Length || written > limits.MaxUnpackedBytes)
                    throw new SongImportException("archive_too_large", $"{e.FullName} unpacks to more than it declares", e.FullName);
                output.Write(buffer, 0, n);
            }
        }
    }

    // ------------------------------------------------------------------ audio → new song

    private SongImportResult importAudio(string audioPath, SongImportOptions options)
    {
        var warnings = new List<string>();
        var (tagTitle, tagArtist) = AudioTags.Read(audioPath);
        string stem = Path.GetFileNameWithoutExtension(audioPath);
        string title = tagTitle ?? stem, artist = tagArtist ?? "";
        if (tagTitle == null && stem.Contains(" - "))
        {
            // "Artist - Title.mp3", the usual file naming
            int i = stem.IndexOf(" - ", StringComparison.Ordinal);
            artist = tagArtist ?? stem[..i].Trim();
            title = stem[(i + 3)..].Trim();
        }
        if (tagTitle == null) warnings.Add("no title tag in the audio file; title taken from the file name");

        string staging = newStaging();
        try
        {
            string dir = Path.Combine(staging, "song");
            Directory.CreateDirectory(dir);
            string audioName = Path.GetFileName(audioPath);
            File.Copy(audioPath, Path.Combine(dir, audioName));

            var chart = NewChart(title, artist, audioName, options.NewChartSlot);
            string chartName = Difficulty.ShortName(chart.Meta.DifficultySlot).ToLowerInvariant() + ".kson";
            KsonWriter.WriteFile(chart, Path.Combine(dir, chartName));
            string name = VoxExporter.SafeFolderName(artist.Length > 0 ? $"{artist} - {title}" : title);
            return place(dir, name, SongImportKind.Audio, options, warnings);
        }
        finally
        {
            deleteQuietly(staging);
        }
    }

    /// <summary>An empty chart: 120 BPM from pulse 0, 4/4, level 1, the given audio, 15 s preview from the start.</summary>
    public static Chart NewChart(string title, string artist, string audioFileName, int slot = Difficulty.Exhaust)
    {
        var chart = new Chart();
        chart.Meta.Title = title;
        chart.Meta.Artist = artist;
        chart.Meta.DifficultySlot = Math.Clamp(slot, 0, 4);
        chart.Meta.Level = 1;
        chart.Beat.Bpm.Add(new BpmChange(0, 120));
        chart.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        chart.Audio.Bgm.Filename = audioFileName;
        chart.Audio.Bgm.PreviewOffsetMs = 0;
        chart.Audio.Bgm.PreviewDurationMs = 15000;
        chart.Editor.AppName = "SDVX Chart Editor";
        chart.Editor.AppVersion = typeof(SongImporter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "";
        return chart;
    }

    // ------------------------------------------------------------------ helpers

    private SongImportResult place(string sourceDir, string name, SongImportKind kind, SongImportOptions options, List<string> warnings)
    {
        Directory.CreateDirectory(library.EditorRoot);
        name = VoxExporter.SafeFolderName(name);
        if (name.StartsWith('.')) name = "_" + name[1..];
        string target = Path.Combine(library.EditorRoot, name);
        if (Directory.Exists(target) || File.Exists(target))
        {
            switch (options.Existing)
            {
                case ExistingSongPolicy.Fail:
                    throw new SongImportException("song_exists", $"a song folder named '{name}' already exists", name);
                case ExistingSongPolicy.Replace:
                    Directory.Delete(target, true);
                    break;
                default:
                    string renamed = SongLibrary.UniqueFolderName(library.EditorRoot, name);
                    warnings.Add($"'{name}' already exists; imported as '{renamed}'");
                    target = Path.Combine(library.EditorRoot, renamed);
                    break;
            }
        }
        Directory.Move(sourceDir, target);
        var song = library.Refresh(target) ?? throw new SongImportException("no_charts", $"{target} has no readable chart", target);
        foreach (var err in song.Errors) warnings.Add("chart not readable: " + err);
        return new SongImportResult(song, kind, false, warnings);
    }

    private string newStaging()
    {
        Directory.CreateDirectory(library.EditorRoot);
        string s = Path.Combine(library.EditorRoot, ".import-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(s);
        return s;
    }

    private static void deleteQuietly(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static bool isInside(string parent, string child)
    {
        string p = Path.GetFullPath(parent).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string c = Path.GetFullPath(child).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }
}
