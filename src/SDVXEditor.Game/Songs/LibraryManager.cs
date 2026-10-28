using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.InteropServices;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.LevelEstimation;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Game.Songs;

public enum LibraryLoadState
{
    Loading,
    Ready,
}

public enum SongExportFormat
{
    Kson,
    Ksh,
    Vox,
    Zip,

    /// <summary>The editor's share / collab package (Core <c>SvzPackage</c>, docs/SVZ_FORMAT.md).</summary>
    Svz,
}

/// <summary>
/// Game-side owner of the <see cref="SongLibrary"/> (architecture §4). The Core library is not thread-safe,
/// so every call into it runs on the thread pool behind one gate; results are published to the update thread
/// (<see cref="Songs"/>, <see cref="SongsChanged"/>, <see cref="State"/>, <see cref="Status"/>).
/// <para>
/// Roots: the editor's own songs folder, <c>--songs</c> and the launcher's <c>nautica_charts_path</c>.
/// Launcher songs are read-only; the library forks them on the first save (<see cref="EnsureWritable"/>).
/// </para>
/// </summary>
public partial class LibraryManager : Component
{
    public SongLibrary Library { get; }

    /// <summary>How the launcher config was found (or why not). Null when the launcher was not looked up.</summary>
    public LauncherConfigLookup? Launcher { get; }

    /// <summary>Default export folder: <c>Exports</c> beside the editor's songs folder (<c>%APPDATA%\SDVXEditor\Exports</c>).</summary>
    public string DefaultExportRoot { get; }

    /// <summary>
    /// Where exports go: the options' <c>ExportFolder</c> when set, else <see cref="DefaultExportRoot"/>. A smoke test
    /// ignores the setting (<see cref="IgnoreConfiguredExportFolder"/>) so it never writes into the user's folder.
    /// </summary>
    public string ExportRoot =>
        !IgnoreConfiguredExportFolder && config?.Get<string>(SDVXEditor.Game.Configuration.EditorSetting.ExportFolder) is { Length: > 0 } custom
            ? custom.Trim()
            : DefaultExportRoot;

    public bool IgnoreConfiguredExportFolder { get; init; }

    public readonly Bindable<LibraryLoadState> State = new Bindable<LibraryLoadState>();

    /// <summary>One line for the UI: scan progress, then a summary.</summary>
    public readonly Bindable<string> Status = new Bindable<string>(string.Empty);

    /// <summary>Snapshot of the library, replaced (never mutated) on the update thread.</summary>
    public IReadOnlyList<LibrarySong> Songs { get; private set; } = Array.Empty<LibrarySong>();

    /// <summary>Raised on the update thread whenever <see cref="Songs"/> was replaced.</summary>
    public event Action? SongsChanged;

    /// <summary>Problems of the last scan (unreadable folders), plus a launcher-config error.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly ConcurrentDictionary<string, Task<DifficultyResult?>> estimates = new ConcurrentDictionary<string, Task<DifficultyResult?>>();
    private int scanning;

    public LibraryManager(SongLibraryOptions options, LauncherConfigLookup? launcher)
    {
        Library = new SongLibrary(options);
        Launcher = launcher;
        DefaultExportRoot = Path.Combine(Path.GetDirectoryName(Library.EditorRoot.TrimEnd('\\', '/')) ?? Library.EditorRoot, "Exports");
    }

    /// <summary>
    /// The library for the command line: launcher config (<c>--launcher-config</c> or the usual lookup) and
    /// <c>--songs</c>. In a smoke test <c>--songs</c> (or a temp folder) becomes the editor root, so the test
    /// never writes into the user's real songs folder.
    /// </summary>
    public static LibraryManager FromStartup(StartupOptions options)
    {
        var lookup = LauncherConfig.Locate(options.LauncherConfigPath);
        var libOptions = new SongLibraryOptions { LauncherChartsPath = lookup.Config?.NauticaChartsPath };

        if (options.SmokeTest)
        {
            libOptions.EditorRoot = options.SongsDirectory
                                    ?? Path.Combine(Path.GetTempPath(), "SDVXEditor-smoke", Guid.NewGuid().ToString("N")[..8], "Songs");
        }
        else if (options.SongsDirectory != null)
        {
            libOptions.ExtraRoots.Add(options.SongsDirectory);
        }

        return new LibraryManager(libOptions, lookup) { IgnoreConfiguredExportFolder = options.SmokeTest };
    }

    /// <summary>The launcher config problem in readable form, or null.</summary>
    public string? LauncherProblem => Launcher?.Error is { } e
        ? $"Launcher config {(e.Kind == "config_not_found" ? "not found" : "unreadable")}: {e.Message}"
        : null;

    // ------------------------------------------------------------------ scanning

    /// <summary>Full scan (cache-backed). Concurrent calls collapse into the running one.</summary>
    public Task ScanAsync()
    {
        if (Interlocked.Exchange(ref scanning, 1) == 1)
            return Task.CompletedTask;

        Schedule(() => State.Value = LibraryLoadState.Loading);

        return run(() =>
        {
            try
            {
                var result = Library.Scan(new SyncProgress(p =>
                {
                    if (p.Total > 0)
                        setStatus($"Scanning songs {p.Done}/{p.Total}");
                }));

                var problems = result.Problems.ToList();
                if (LauncherProblem is string lp) problems.Insert(0, lp);

                Logger.Log($"library: {result.Songs} songs ({result.FromCache} cached, {result.Loaded} parsed, {result.Problems.Count} problems) from {string.Join(", ", Library.Roots.Select(r => r.Path))}");
                publish(problems, $"{result.Songs} songs");
                return 0;
            }
            finally
            {
                Interlocked.Exchange(ref scanning, 0);
                Schedule(() => State.Value = LibraryLoadState.Ready);
            }
        });
    }

    /// <summary>Cheap disk comparison (fingerprints only); rescans when anything changed. Call on window focus.</summary>
    public async Task<LibraryChanges> CheckForChangesAsync()
    {
        if (Volatile.Read(ref scanning) == 1)
            return new LibraryChanges(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        var changes = await run(() => Library.DetectChanges()).ConfigureAwait(false);
        if (changes.Any)
        {
            Logger.Log($"library: {changes.Added.Count} added, {changes.Changed.Count} changed, {changes.Removed.Count} removed on disk — rescanning");
            await ScanAsync().ConfigureAwait(false);
        }

        return changes;
    }

    /// <summary>Re-reads one folder (after an edit) and publishes the result.</summary>
    public Task<LibrarySong?> RefreshAsync(string folder) => run(() =>
    {
        var song = Library.Refresh(folder);
        publish(Problems, null);
        return song;
    });

    // ------------------------------------------------------------------ import / delete / fork

    public Task<SongImportResult> ImportAsync(string path, SongImportOptions? options = null) => run(() =>
    {
        var result = new SongImporter(Library).Import(path, options);
        publish(Problems, null);
        return result;
    });

    /// <summary>The editor song an import of <paramref name="path"/> would collide with (same folder name), or null.</summary>
    public Task<string?> FindImportConflictAsync(string path) => run(() =>
    {
        var importer = new SongImporter(Library);
        return importer.WouldReplaceExisting(path) ? importer.PlannedFolderName(path) : null;
    });

    /// <summary>
    /// Moves an editor song into the recycle bin (Windows) or a <c>.trash</c> folder beside the songs. Launcher
    /// songs are refused: they belong to the launcher, whose import map is keyed on their folder.
    /// </summary>
    public Task DeleteAsync(LibrarySong song) => run(() =>
    {
        if (song.Source == SongSource.Launcher || song.IsReadOnly)
            throw new InvalidOperationException("This song belongs to the launcher library. Remove it in the launcher instead.");

        var root = Library.Roots.FirstOrDefault(r => isDirectChild(r.Path, song.FolderPath));
        if (root == null)
            throw new InvalidOperationException("The song is not inside a library folder; it was not deleted.");

        recycle(song.FolderPath, root.Path);
        Library.Refresh(song.FolderPath);
        publish(Problems, null);
        return 0;
    });

    /// <summary>
    /// The folder to write into for <paramref name="song"/>: itself for an editor song, the fork (created on
    /// first use) for a launcher song. Blocks until the library is free — call it from a save, not per frame.
    /// </summary>
    public string EnsureWritable(LibrarySong song)
    {
        gate.Wait();

        try
        {
            var current = Library.Find(song.FolderPath) ?? song;
            var target = Library.EnsureWritable(current);
            publish(Problems, null);
            return target.FolderPath;
        }
        finally
        {
            gate.Release();
        }
    }

    // ------------------------------------------------------------------ level estimate

    /// <summary>Estimated level of one chart, computed once per chart file version and cached.</summary>
    public Task<DifficultyResult?> EstimateAsync(LibrarySong song, LibraryChart chart)
    {
        string key = $"{song.Fingerprint}|{song.FolderPath}|{chart.FileName}";

        return estimates.GetOrAdd(key, _ => Task.Run(() =>
        {
            try
            {
                string path = Path.Combine(song.FolderPath, chart.FileName);
                var c = chart.Format == ChartFormat.Kson ? KsonReader.ReadFile(path) : KshReader.ReadFile(path);
                c.Meta.DifficultySlot = chart.Slot;
                return DifficultyEstimator.Default.Estimate(c);
            }
            catch (Exception e) when (e is IOException or FormatException or System.Text.Json.JsonException or UnauthorizedAccessException
                                          or ArgumentException or InvalidOperationException)
            {
                Logger.Log($"level estimate failed for {chart.FileName}: {e.Message}");
                return (DifficultyResult?)null;
            }
        }));
    }

    // ------------------------------------------------------------------ export

    /// <summary>
    /// Exports a song into <see cref="ExportRoot"/> (never into the game's data_mods). Returns the written folder or
    /// file. kson/ksh: every chart converted, audio and images copied. zip: the folder as is. VOX: ksh2vox through
    /// the Python host into <c>Exports\VOX</c>.
    /// </summary>
    public async Task<string> ExportAsync(LibrarySong song, SongExportFormat format, Action<string>? progress = null)
    {
        Directory.CreateDirectory(ExportRoot);
        guardExportTarget(ExportRoot);

        switch (format)
        {
            case SongExportFormat.Kson:
            case SongExportFormat.Ksh:
                return await Task.Run(() => exportCharts(song, format)).ConfigureAwait(false);

            case SongExportFormat.Zip:
                return await Task.Run(() =>
                {
                    string zip = uniqueFile(ExportRoot, song.FolderName, ".zip");
                    ZipFile.CreateFromDirectory(song.FolderPath, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
                    return zip;
                }).ConfigureAwait(false);

            case SongExportFormat.Vox:
                return await exportVox(song, progress).ConfigureAwait(false);

            case SongExportFormat.Svz:
                return (await ExportSvzAsync(song.FolderPath).ConfigureAwait(false)).Path;

            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    /// <summary>Packages a song folder as <c>Exports\&lt;folder&gt;.svz</c> (a free name: " 2", " 3" … when taken).</summary>
    public Task<SvzExportResult> ExportSvzAsync(string songFolder, string collabNotes = "") => Task.Run(() =>
    {
        Directory.CreateDirectory(ExportRoot);
        guardExportTarget(ExportRoot);
        string name = Path.GetFileName(songFolder.TrimEnd('\\', '/'));
        return SvzPackage.Export(songFolder, uniqueFile(ExportRoot, name, SvzPackage.Extension), new SvzExportOptions { CollabNotes = collabNotes });
    });

    private string exportCharts(LibrarySong song, SongExportFormat format)
    {
        string ext = format == SongExportFormat.Kson ? ".kson" : ".ksh";
        var folder = SongFolder.Load(song.FolderPath);
        if (folder.Charts.Count == 0)
            throw new InvalidOperationException("the song has no readable chart");

        string dest = Path.Combine(ExportRoot, SongLibrary.UniqueFolderName(ExportRoot, $"{song.FolderName} ({ext.TrimStart('.')})"));
        string staging = dest + ".partial";
        Directory.CreateDirectory(staging);

        try
        {
            foreach (string f in Directory.EnumerateFiles(song.FolderPath))
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (SongFolder.ImageExtensions.Contains(e) || SongFolder.AudioExtensions.Contains(e) || e == ".gif")
                    File.Copy(f, Path.Combine(staging, Path.GetFileName(f)));
            }

            foreach (var sc in folder.Charts)
            {
                string target = Path.Combine(staging, Path.GetFileNameWithoutExtension(sc.FilePath) + ext);
                if (format == SongExportFormat.Kson) KsonWriter.WriteFile(sc.Chart, target);
                else KshWriter.WriteFile(sc.Chart, target);
            }

            Directory.Move(staging, dest);
            return dest;
        }
        catch
        {
            try { Directory.Delete(staging, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }

            throw;
        }
    }

    [osu.Framework.Allocation.Resolved(CanBeNull = true)]
    private SDVXEditor.Game.Configuration.EditorConfigManager? config { get; set; }

    private static string? nullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private async Task<string> exportVox(LibrarySong song, Action<string>? progress)
    {
        string voxRoot = Path.Combine(ExportRoot, "VOX");
        string music = Path.Combine(voxRoot, "music");
        guardExportTarget(music);

        await using var exporter = new VoxExporter(new VoxExporterSettings
        {
            ConverterDir = nullIfEmpty(config?.Get<string>(SDVXEditor.Game.Configuration.EditorSetting.VoxConverterDir)),
            PythonPath = nullIfEmpty(config?.Get<string>(SDVXEditor.Game.Configuration.EditorSetting.VoxPythonPath)),
            Log = l => Logger.Log("vox: " + l),
        });

        progress?.Invoke("Starting the converter…");
        var info = await exporter.GetInfoAsync().ConfigureAwait(false);
        if (!info.ConverterLoaded)
            throw new VoxExportException("converter_missing", info.ConverterError ?? "the ksh2vox converter could not be loaded");

        // An outdated converter (the v0.12.0 submodule) has no import map: only an explicit id works there.
        bool outdated = VoxExporter.IsOutdated(info.ConverterVersion);
        // ids from 2900, and always above every real song the launcher's plugin json or the game's music_db.xml knows
        // (ksh2vox >= 0.20.1 takes the max over all; the json alone lagged behind game build 2026071400)
        var bases = outdated ? Array.Empty<string>() : Launcher?.Config?.BaseMusicDbs ?? Array.Empty<string>();
        if (!outdated && bases.Count > 1 && !VoxExporter.SupportsMultipleBaseDbs(info.ConverterVersion))
            bases = bases.TakeLast(1).ToList(); // an older converter takes one base: the game's xml is the complete one
        var options = new VoxExportOptions
        {
            OutputDir = music,
            SongId = outdated ? 2900 : null,
            StartId = outdated ? null : 2900,
            BaseDbPaths = bases.Count > 0 ? bases : null,
        };

        var result = await exporter.ExportFolderAsync(song.FolderPath, options,
            new SyncProgress<VoxExportProgress>(p => progress?.Invoke(describe(p)))).ConfigureAwait(false);

        if (result.Errors.Count > 0)
            throw new VoxExportException("export_failed", string.Join("; ", result.Errors));

        return result.SongDir ?? music;
    }

    private static string describe(VoxExportProgress p) => p.Event switch
    {
        "chart_done" => "Converted a chart…",
        "audio_done" => "Encoded the audio…",
        "jacket_done" => "Wrote the jackets…",
        "song_done" => "Finishing…",
        _ => p.Event.Replace('_', ' ') + "…",
    };

    /// <summary>Exports go to the editor's own folder; a path inside a game's data_mods is refused outright.</summary>
    private static void guardExportTarget(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("data_mods", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"refusing to export into a game data_mods folder: {full}");
    }

    private static string uniqueFile(string dir, string stem, string ext)
    {
        string p = Path.Combine(dir, stem + ext);
        for (int n = 2; File.Exists(p); n++)
            p = Path.Combine(dir, $"{stem} {n}{ext}");
        return p;
    }

    // ------------------------------------------------------------------ plumbing

    private async Task<T> run<T>(Func<T> work)
    {
        await gate.WaitAsync().ConfigureAwait(false);

        try
        {
            return await Task.Run(work).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Snapshots the library (caller holds the gate) and hands it to the update thread.</summary>
    private void publish(IReadOnlyList<string> problems, string? status)
    {
        var snapshot = Library.Songs.ToList();

        Schedule(() =>
        {
            Songs = snapshot;
            Problems = problems;
            if (status != null) Status.Value = status;
            SongsChanged?.Invoke();
        });
    }

    private void setStatus(string s) => Schedule(() => Status.Value = s);

    private static bool isDirectChild(string root, string folder)
        => string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('\\', '/'))?.TrimEnd('\\', '/'),
            Path.GetFullPath(root).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static void recycle(string folder, string root)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var op = new ShFileOpStruct
            {
                wFunc = fo_delete,
                pFrom = Path.GetFullPath(folder) + '\0',
                fFlags = fof_allowundo | fof_noconfirmation | fof_silent | fof_noerrorui,
            };

            int rc = SHFileOperationW(ref op);
            if (rc == 0 && op.fAnyOperationsAborted == 0 && !Directory.Exists(folder))
                return;

            Logger.Log($"recycle bin refused {folder} (code {rc}); moving it to .trash instead");
        }

        string trash = Path.Combine(root, ".trash");
        Directory.CreateDirectory(trash);
        Directory.Move(folder, Path.Combine(trash, SongLibrary.UniqueFolderName(trash, Path.GetFileName(folder.TrimEnd('\\', '/')))));
    }

    private const uint fo_delete = 3;
    private const ushort fof_silent = 0x4, fof_noconfirmation = 0x10, fof_allowundo = 0x40, fof_noerrorui = 0x400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOpStruct op);

    /// <summary>IProgress that reports synchronously on the reporting thread (Progress&lt;T&gt; would post to a context).</summary>
    private sealed class SyncProgress : IProgress<(int Done, int Total, string Folder)>
    {
        private readonly Action<(int Done, int Total, string Folder)> action;
        public SyncProgress(Action<(int Done, int Total, string Folder)> action) => this.action = action;
        public void Report((int Done, int Total, string Folder) value) => action(value);
    }

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> action;
        public SyncProgress(Action<T> action) => this.action = action;
        public void Report(T value) => action(value);
    }

    protected override void Dispose(bool isDisposing)
    {
        base.Dispose(isDisposing);
        gate.Dispose();
    }
}
