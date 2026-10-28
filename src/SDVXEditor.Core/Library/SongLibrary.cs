using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Library;

public sealed class SongLibraryOptions
{
    /// <summary>The editor's own songs folder (created on demand). Default <c>%APPDATA%\SDVXEditor\Songs</c>.</summary>
    public string EditorRoot { get; set; } = SongLibrary.DefaultEditorRoot;

    /// <summary>The launcher's <c>nautica_charts_path</c>; null = no launcher root.</summary>
    public string? LauncherChartsPath { get; set; }

    /// <summary>Launcher songs are read-only unless this is switched off; editing one forks it.</summary>
    public bool LauncherReadOnly { get; set; } = true;

    /// <summary>Further roots (e.g. <c>--songs</c>), treated like the editor root but never used as import target.</summary>
    public List<string> ExtraRoots { get; set; } = new();

    /// <summary>Metadata cache file; null = <c>library-cache.json</c> next to the editor root. Empty string = no cache.</summary>
    public string? CachePath { get; set; }
}

/// <summary>What a scan did.</summary>
public sealed record LibraryScanResult(int Songs, int FromCache, int Loaded, IReadOnlyList<string> Added,
                                       IReadOnlyList<string> Removed, IReadOnlyList<string> Changed, IReadOnlyList<string> Problems);

/// <summary>Folders whose inputs differ from the last scan (cheap: fingerprints only, nothing parsed).</summary>
public sealed record LibraryChanges(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, IReadOnlyList<string> Changed)
{
    public bool Any => Added.Count + Removed.Count + Changed.Count > 0;
}

/// <summary>
/// The song library (architecture §4): the editor's own folder plus the launcher's chart folder.
/// Scans are cached by folder fingerprint (the same name+size+mtime fingerprint ksh2vox uses for its
/// import map, converter CLAUDE.md §3a), so an unchanged library costs one directory listing per song.
/// <para>
/// Launcher songs are read-only: <see cref="EnsureWritable"/> (and <see cref="SaveChart"/>, which calls
/// it) copies such a song into the editor folder under a NEW folder name before the first write. The
/// launcher's import map is keyed on the folder name, so neither the launcher folder nor that key is
/// ever touched by the editor.
/// </para>
/// Not thread-safe: call from one thread (or lock around it).
/// </summary>
public sealed class SongLibrary
{
    public const string ForkMarkerName = ".sdvxeditor-fork.json";
    private const int cacheVersion = 3; // 2: BackgroundPath, ModifiedUtc; 3: SongSources

    /// <summary>Extensions that feed ksh2vox (its <c>_FINGERPRINT_EXTS</c>): charts, audio, images.</summary>
    public static readonly string[] FingerprintExtensions =
        { ".ksh", ".kson", ".ogg", ".mp3", ".wav", ".flac", ".m4a", ".aac", ".opus", ".wma", ".s3v", ".png", ".jpg", ".jpeg" };

    public static string DefaultEditorRoot
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SDVXEditor", "Songs");

    private readonly SongLibraryOptions options;
    private readonly List<LibrarySong> songs = new();
    private Dictionary<string, LibrarySong>? cache;

    public SongLibrary(SongLibraryOptions? options = null)
    {
        this.options = options ?? new SongLibraryOptions();
        var roots = new List<LibraryRoot> { new(Path.GetFullPath(this.options.EditorRoot), SongSource.Editor, false) };
        foreach (var extra in this.options.ExtraRoots)
            roots.Add(new LibraryRoot(Path.GetFullPath(extra), SongSource.Editor, false));
        if (!string.IsNullOrWhiteSpace(this.options.LauncherChartsPath))
            roots.Add(new LibraryRoot(Path.GetFullPath(this.options.LauncherChartsPath), SongSource.Launcher, this.options.LauncherReadOnly));
        // The same folder configured twice would list every song twice.
        Roots = roots.GroupBy(r => r.Path.TrimEnd('\\', '/'), StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
    }

    /// <summary>Convenience: editor root + the launcher's charts folder from its config.</summary>
    public static SongLibrary WithLauncher(LauncherConfig? config, string? editorRoot = null)
        => new(new SongLibraryOptions
        {
            EditorRoot = editorRoot ?? DefaultEditorRoot,
            LauncherChartsPath = config?.NauticaChartsPath,
        });

    public IReadOnlyList<LibraryRoot> Roots { get; }
    public string EditorRoot => Roots[0].Path;
    public IReadOnlyList<LibrarySong> Songs => songs;

    private string? cachePath => options.CachePath switch
    {
        "" => null,
        null => Path.Combine(Path.GetDirectoryName(EditorRoot.TrimEnd('\\', '/')) ?? EditorRoot, "library-cache.json"),
        var p => p,
    };

    public LibrarySong? Find(string folderPath)
    {
        string full = Path.GetFullPath(folderPath);
        return songs.FirstOrDefault(s => string.Equals(s.FolderPath, full, StringComparison.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ scanning

    /// <summary>Song folders of all roots: direct sub-folders holding a chart file (dot-folders are staging and skipped).</summary>
    public IEnumerable<(string Folder, LibraryRoot Root)> EnumerateSongFolders()
    {
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root.Path)) continue;
            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(root.Path).OrderBy(d => d, StringComparer.Ordinal).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string d in dirs)
            {
                if (Path.GetFileName(d).StartsWith('.')) continue;
                bool hasChart;
                try
                {
                    hasChart = Directory.EnumerateFiles(d).Any(SongFolder.IsChartFile);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (hasChart) yield return (Path.GetFullPath(d), root);
            }
        }
    }

    /// <summary>Full scan. Unchanged folders come from the cache; changed and new ones are parsed.</summary>
    public LibraryScanResult Scan(IProgress<(int Done, int Total, string Folder)>? progress = null)
    {
        loadCache();
        var before = songs.ToDictionary(s => s.FolderPath, s => s.Fingerprint, StringComparer.OrdinalIgnoreCase);
        var folders = EnumerateSongFolders().ToList();
        var result = new List<LibrarySong>();
        var problems = new List<string>();
        int fromCache = 0, loaded = 0;
        for (int i = 0; i < folders.Count; i++)
        {
            var (folder, root) = folders[i];
            progress?.Report((i, folders.Count, folder));
            try
            {
                string fp = Fingerprint(folder);
                if (cache!.TryGetValue(folder, out var hit) && hit.Fingerprint == fp)
                {
                    result.Add(hit with { Source = root.Source, IsReadOnly = root.ReadOnly, FolderPath = folder });
                    fromCache++;
                    continue;
                }
                result.Add(load(folder, root, fp));
                loaded++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{folder}: {e.Message}");
            }
        }
        progress?.Report((folders.Count, folders.Count, ""));

        songs.Clear();
        songs.AddRange(result.OrderBy(s => s.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.FolderPath, StringComparer.Ordinal));
        cache = songs.ToDictionary(s => s.FolderPath, StringComparer.OrdinalIgnoreCase);
        saveCache();

        var now = songs.ToDictionary(s => s.FolderPath, s => s.Fingerprint, StringComparer.OrdinalIgnoreCase);
        var added = now.Keys.Where(k => !before.ContainsKey(k)).ToList();
        var removed = before.Keys.Where(k => !now.ContainsKey(k)).ToList();
        var changed = now.Where(kv => before.TryGetValue(kv.Key, out var old) && old != kv.Value).Select(kv => kv.Key).ToList();
        return new LibraryScanResult(songs.Count, fromCache, loaded, added, removed, changed, problems);
    }

    /// <summary>Compares the disk with the last scan without parsing anything. Call e.g. on window focus.</summary>
    public LibraryChanges DetectChanges()
    {
        var known = songs.ToDictionary(s => s.FolderPath, s => s.Fingerprint, StringComparer.OrdinalIgnoreCase);
        var added = new List<string>();
        var changed = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, _) in EnumerateSongFolders())
        {
            seen.Add(folder);
            string fp;
            try
            {
                fp = Fingerprint(folder);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            if (!known.TryGetValue(folder, out var old)) added.Add(folder);
            else if (old != fp) changed.Add(folder);
        }
        var removed = known.Keys.Where(k => !seen.Contains(k)).ToList();
        return new LibraryChanges(added, removed, changed);
    }

    /// <summary>Re-reads one folder (after an import or a save) and updates <see cref="Songs"/>. Null if it is not a song folder.</summary>
    public LibrarySong? Refresh(string folder)
    {
        string full = Path.GetFullPath(folder);
        songs.RemoveAll(s => string.Equals(s.FolderPath, full, StringComparison.OrdinalIgnoreCase));
        var root = rootOf(full);
        if (root == null || !Directory.Exists(full) || !Directory.EnumerateFiles(full).Any(SongFolder.IsChartFile))
        {
            cache?.Remove(full);
            saveCache();
            return null;
        }
        var song = load(full, root, Fingerprint(full));
        int i = songs.FindIndex(s => StringComparer.CurrentCultureIgnoreCase.Compare(s.Title, song.Title) > 0);
        songs.Insert(i < 0 ? songs.Count : i, song);
        loadCache();
        cache![full] = song;
        saveCache();
        return song;
    }

    private LibraryRoot? rootOf(string folder)
    {
        string? parent = Path.GetDirectoryName(folder.TrimEnd('\\', '/'));
        return parent == null ? null : Roots.FirstOrDefault(r => string.Equals(r.Path.TrimEnd('\\', '/'), parent.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
    }

    private static LibrarySong load(string folder, LibraryRoot root, string fingerprint)
    {
        var sf = SongFolder.Load(folder);
        return LibrarySong.FromFolder(sf, fingerprint, root.Source, root.ReadOnly, readForkMarker(folder));
    }

    /// <summary>
    /// ksh2vox's <c>folder_fingerprint</c>: sha1 over "name\0size\0mtime_ns" of every chart/audio/image file
    /// directly in the folder, sorted by name, joined with \x01. Metadata, not content — like a build cache.
    /// </summary>
    public static string Fingerprint(string folder)
    {
        var parts = new List<string>();
        foreach (string path in Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(path);
            if (!FingerprintExtensions.Any(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
            FileInfo fi;
            try
            {
                fi = new FileInfo(path);
                long mtimeNs = (fi.LastWriteTimeUtc.Ticks - DateTime.UnixEpoch.Ticks) * 100;
                parts.Add($"{name}\0{fi.Length}\0{mtimeNs}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(string.Join("\u0001", parts)));
        return Convert.ToHexStringLower(hash);
    }

    // ------------------------------------------------------------------ fork on edit

    /// <summary>
    /// The song to write to. An editor song is returned as is. A read-only (launcher) song is copied into
    /// the editor root under a new name first — or the fork made earlier is reused — so the launcher's
    /// folder and its import-map key are never written.
    /// </summary>
    public LibrarySong EnsureWritable(LibrarySong song)
    {
        if (!song.IsReadOnly) return song;
        string source = Path.GetFullPath(song.FolderPath);
        Directory.CreateDirectory(EditorRoot);

        foreach (string dir in Directory.EnumerateDirectories(EditorRoot))
        {
            if (string.Equals(readForkMarker(dir), source, StringComparison.OrdinalIgnoreCase))
                return Find(dir) ?? Refresh(dir) ?? throw new IOException($"existing fork {dir} has no charts");
        }

        string name = UniqueFolderName(EditorRoot, song.FolderName + " (edit)");
        string staging = Path.Combine(EditorRoot, ".fork-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            CopyDirectory(source, staging);
            File.WriteAllText(Path.Combine(staging, ForkMarkerName), JsonSerializer.Serialize(new ForkMarker
            {
                ForkedFrom = source,
                SourceFingerprint = song.Fingerprint,
                ForkedAt = DateTime.UtcNow,
            }, jsonOptions));
            Directory.Move(staging, Path.Combine(EditorRoot, name));
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return Refresh(Path.Combine(EditorRoot, name)) ?? throw new IOException("the fork has no charts");
    }

    /// <summary>
    /// Saves one chart into a song, forking it first when it is read-only. <paramref name="fileName"/>
    /// defaults to the existing file of the chart's slot, else <c>&lt;slot&gt;.kson</c>; the extension picks the
    /// writer (.kson or .ksh). Returns the song as it is now (the fork, if one was made).
    /// </summary>
    public LibrarySong SaveChart(LibrarySong song, Chart chart, string? fileName = null)
    {
        var target = EnsureWritable(song);
        fileName ??= target[chart.Meta.DifficultySlot]?.FileName
                     ?? Difficulty.ShortName(chart.Meta.DifficultySlot).ToLowerInvariant() + ".kson";
        if (fileName != Path.GetFileName(fileName)) throw new ArgumentException("fileName must be a plain file name", nameof(fileName));
        string path = Path.Combine(target.FolderPath, fileName);
        string tmp = path + ".tmp";
        if (fileName.EndsWith(".ksh", StringComparison.OrdinalIgnoreCase)) KshWriter.WriteFile(chart, tmp);
        else if (fileName.EndsWith(".kson", StringComparison.OrdinalIgnoreCase)) KsonWriter.WriteFile(chart, tmp);
        else throw new ArgumentException("chart files end in .kson or .ksh", nameof(fileName));
        File.Move(tmp, path, true);
        return Refresh(target.FolderPath) ?? target;
    }

    /// <summary><paramref name="name"/>, or "name 2", "name 3", … — the first that does not exist in <paramref name="parent"/>.</summary>
    public static string UniqueFolderName(string parent, string name)
    {
        string candidate = name;
        for (int n = 2; Directory.Exists(Path.Combine(parent, candidate)) || File.Exists(Path.Combine(parent, candidate)); n++)
            candidate = $"{name} {n}";
        return candidate;
    }

    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string f in Directory.EnumerateFiles(source))
            File.Copy(f, Path.Combine(target, Path.GetFileName(f)));
        foreach (string d in Directory.EnumerateDirectories(source))
        {
            // Never follow a junction/symlink out of the song folder.
            if (new DirectoryInfo(d).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            CopyDirectory(d, Path.Combine(target, Path.GetFileName(d)));
        }
    }

    private sealed class ForkMarker
    {
        public string ForkedFrom { get; set; } = "";
        public string SourceFingerprint { get; set; } = "";
        public DateTime ForkedAt { get; set; }
    }

    private static string? readForkMarker(string folder)
    {
        string p = Path.Combine(folder, ForkMarkerName);
        if (!File.Exists(p)) return null;
        try
        {
            return JsonSerializer.Deserialize<ForkMarker>(File.ReadAllText(p), jsonOptions)?.ForkedFrom;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ cache

    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class CacheFile
    {
        public int Version { get; set; }
        public List<LibrarySong> Songs { get; set; } = new();
    }

    private void loadCache()
    {
        if (cache != null) return;
        cache = new Dictionary<string, LibrarySong>(StringComparer.OrdinalIgnoreCase);
        if (cachePath is not { } p || !File.Exists(p)) return;
        try
        {
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(p), jsonOptions);
            if (file?.Version != cacheVersion) return;
            foreach (var s in file.Songs.Where(s => s.FolderPath.Length > 0)) cache[s.FolderPath] = s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // A broken cache is only a slower scan.
        }
    }

    private void saveCache()
    {
        if (cachePath is not { } p || cache == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(p))!);
            string tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new CacheFile { Version = cacheVersion, Songs = cache.Values.ToList() }, jsonOptions));
            File.Move(tmp, p, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
