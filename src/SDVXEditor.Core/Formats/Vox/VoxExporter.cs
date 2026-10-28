using System.Text.Json.Nodes;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Formats.Vox;

public enum VoxAudioFormat
{
    /// <summary>MS-ADPCM in a .2dx container — works on Windows and Linux via ffmpeg (converter default).</summary>
    TwoDx,

    /// <summary>ASF/WMA Pro; needs <see cref="VoxExportOptions.WmaEncoder"/> (ffmpeg cannot encode WMA Pro).</summary>
    S3v,
}

/// <summary>Where Python, the converter and the host script come from.</summary>
public sealed class VoxExporterSettings
{
    /// <summary>Python executable chosen by the user; null = bundled runtime, then PATH.</summary>
    public string? PythonPath { get; set; }

    /// <summary>Folder holding ksh2vox.py; null = the <c>external/SDVX-Chart-Converter</c> submodule.</summary>
    public string? ConverterDir { get; set; }

    /// <summary>Host script; null = <c>python/vox_host.py</c> next to the app (or above it in a dev checkout).</summary>
    public string? HostScript { get; set; }

    /// <summary>Base directory for the relative lookups; null = <see cref="AppContext.BaseDirectory"/>.</summary>
    public string? AppBaseDir { get; set; }

    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan InfoTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ExportTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Receives the host's stderr (ksh2vox's console output) line by line.</summary>
    public Action<string>? Log { get; set; }
}

/// <summary>Options of one export. Mirrors the ksh2vox CLI flags (converter CLAUDE.md §3/§7).</summary>
public sealed class VoxExportOptions
{
    /// <summary>Music output root; the song lands in <c>&lt;OutputDir&gt;/&lt;id&gt;_&lt;ascii&gt;/</c>. Required.</summary>
    public required string OutputDir { get; init; }

    /// <summary>music_db.merged.xml to update; null = the converter's default (<c>&lt;OutputDir&gt;/../others/music_db.merged.xml</c>).</summary>
    public string? MergedDbPath { get; init; }

    /// <summary>False = write no music_db entry at all (the song then never appears in game).</summary>
    public bool WriteMergedDb { get; init; } = true;

    /// <summary>Explicit song id (≤ 3071); bypasses the import map.</summary>
    public int? SongId { get; init; }

    /// <summary>music_db.json / .xml of the real songs: ids start one above its highest real id, through the import map.</summary>
    public string? BaseDbPath { get; init; }

    /// <summary>
    /// Several music dbs of real songs (plugin json + the game's music_db.xml); ids start one above the highest
    /// real id over all of them, and over <see cref="BaseDbPath"/>. Needs ksh2vox ≥ 0.20.1 for more than one.
    /// </summary>
    public IReadOnlyList<string>? BaseDbPaths { get; init; }

    /// <summary>Lowest id to hand out; with base dbs as well, the higher of the two floors wins.</summary>
    public int? StartId { get; init; }

    /// <summary>import_map.json; null = next to the merged db (converter default).</summary>
    public string? MapPath { get; init; }

    /// <summary>
    /// Convert even when the import map says "unchanged". On by default: the staging folder is written
    /// fresh for every export, so the converter's mtime fingerprint would never match anyway.
    /// </summary>
    public bool Force { get; init; } = true;

    public VoxAudioFormat AudioFormat { get; init; } = VoxAudioFormat.TwoDx;

    /// <summary>Mono is the verified-working default; the game's .2dx path renders stereo imbalanced.</summary>
    public bool Stereo { get; init; }

    /// <summary>External WMA Pro encoder template with {in} and {out} (only for <see cref="VoxAudioFormat.S3v"/>).</summary>
    public string? WmaEncoder { get; init; }

    /// <summary>Write the Custom Songs tag (ksh2vox 0.15.0+).</summary>
    public bool CustomTag { get; init; } = true;

    /// <summary>Keep the temporary song folder (for debugging); it is deleted otherwise.</summary>
    public bool KeepStaging { get; init; }
}

/// <summary>One chart to export plus the files it refers to.</summary>
public sealed class VoxExportChart
{
    public required Chart Chart { get; init; }
    public string? AudioPath { get; init; }
    public string? JacketPath { get; init; }
}

/// <summary>What to export: the charts of one song.</summary>
public sealed class VoxExportSource
{
    /// <summary>
    /// Name of the temporary song folder. ksh2vox keys its import map on the folder name (CLAUDE.md §3a),
    /// so this must be stable across exports of the same song — use the library folder name.
    /// </summary>
    public required string SongName { get; init; }

    public List<VoxExportChart> Charts { get; init; } = new();

    public static VoxExportSource FromSongFolder(SongFolder folder) => new()
    {
        SongName = folder.Name,
        Charts = folder.Charts.Select(c => new VoxExportChart { Chart = c.Chart, AudioPath = c.AudioPath, JacketPath = c.JacketPath }).ToList(),
    };
}

public sealed record VoxExportProgress(string Event, JsonObject Fields);

public sealed class VoxExportResult
{
    public int SongId { get; init; }
    public bool? IsNew { get; init; }
    public bool Skipped { get; init; }
    public string? SongDir { get; init; }
    public IReadOnlyList<string> Files { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> VoxFiles => Files.Where(f => f.EndsWith(".vox", StringComparison.OrdinalIgnoreCase)).ToList();
    public string? MergedDbPath { get; init; }
    public string? MapPath { get; init; }
    public string? ConverterVersion { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    public string? StagingDir { get; init; }
    public TimeSpan Elapsed { get; init; }
}

/// <summary>Facts from the host's <c>version</c> call — the same checks as <c>ksh2vox.py --self-check</c>.</summary>
public sealed class VoxHostInfo
{
    public required JsonObject Raw { get; init; }
    public string? Python => str("python");
    public string? PythonExecutable => str("python_executable");
    public string? ConverterDir => str("converter_dir");
    public string? ConverterVersion => str("converter_version");
    public string? ConverterError => str("converter_error");
    public bool ConverterLoaded => flag("converter_loaded");
    public bool RadarLoaded => flag("radar_loaded");
    public bool TemplateFound => flag("template_found");
    public string? Ffmpeg => str("ffmpeg");
    public string? Pillow => str("pillow");

    /// <summary>Human-readable problems, empty when everything a full export needs is present.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            var w = new List<string>();
            if (!ConverterLoaded) w.Add($"converter not loaded: {ConverterError ?? "unknown reason"}");
            else
            {
                if (VoxExporter.IsOutdated(ConverterVersion))
                    w.Add($"ksh2vox {ConverterVersion} is older than {VoxExporter.RecommendedConverterVersion}: 6/8 bars and chart audio offsets are converted wrong (see the converter changelog)");
                if (!RadarLoaded) w.Add("radar.py not loaded: skill radar values will be 0");
                if (!TemplateFound) w.Add("template_v13.vox missing next to ksh2vox.py");
            }
            if (Ffmpeg == null) w.Add("ffmpeg not on PATH: no audio will be written");
            if (Pillow == null) w.Add("Pillow not installed: jackets will not be resized");
            return w;
        }
    }

    private string? str(string key) => Raw[key] is JsonValue v && v.TryGetValue(out string? s) ? s : null;
    private bool flag(string key) => Raw[key] is JsonValue v && v.TryGetValue(out bool b) && b;
}

public sealed class VoxExportException : Exception
{
    public string Kind { get; }
    public VoxExportException(string kind, string message, Exception? inner = null) : base(message, inner) => Kind = kind;
}

/// <summary>
/// VOX export through ksh2vox (architecture §5). The editor writes its charts as .kson into a temporary
/// song folder, and a long-lived <c>vox_host.py</c> process imports ksh2vox and calls
/// <c>convert_song</c> on it. The host is started lazily and restarted after a crash or timeout.
/// </summary>
public sealed class VoxExporter : IAsyncDisposable
{
    /// <summary>0.16.0 fixed non-/4 time signatures and the ignored chart audio offset.</summary>
    public const string RecommendedConverterVersion = "0.16.0";

    public const string SubmoduleRelative = "external/SDVX-Chart-Converter";
    public const string HostScriptRelative = "python/vox_host.py";

    private static readonly string[] slotFileNames = { "nov", "adv", "exh", "inf", "mxm" };

    private readonly VoxExporterSettings settings;
    private readonly SemaphoreSlim hostLock = new(1, 1);
    private PythonHost? host;
    private VoxHostInfo? info;

    public VoxExporter(VoxExporterSettings? settings = null) => this.settings = settings ?? new VoxExporterSettings();

    private string appBase => settings.AppBaseDir ?? AppContext.BaseDirectory;

    /// <summary>Converter folder: setting, else the git submodule found from the app directory upwards.</summary>
    public static string? FindConverterDir(string? setting, string? appBaseDir = null)
    {
        if (!string.IsNullOrWhiteSpace(setting))
            return File.Exists(Path.Combine(setting, "ksh2vox.py")) ? Path.GetFullPath(setting) : null;
        string? hit = PythonLocator.FindUpwards(appBaseDir ?? AppContext.BaseDirectory, SubmoduleRelative + "/ksh2vox.py");
        return hit == null ? null : Path.GetDirectoryName(hit);
    }

    public static string? FindHostScript(string? setting, string? appBaseDir = null)
    {
        if (!string.IsNullOrWhiteSpace(setting)) return File.Exists(setting) ? Path.GetFullPath(setting) : null;
        return PythonLocator.FindUpwards(appBaseDir ?? AppContext.BaseDirectory, HostScriptRelative);
    }

    public static bool IsOutdated(string? version) => version == null || ParseVersion(version) < ParseVersion(RecommendedConverterVersion);

    /// <summary>ksh2vox 0.20.1 takes several base dbs and the max real id over all of them (the host passes a list).</summary>
    public const string MultipleBaseDbsVersion = "0.20.1";

    public static bool SupportsMultipleBaseDbs(string? version) => version != null && ParseVersion(version) >= ParseVersion(MultipleBaseDbsVersion);

    /// <summary>"0.17.0" → 0.17.0; missing parts and non-digit suffixes ("0.17.0rc1") count as 0 / are ignored.</summary>
    public static Version ParseVersion(string v)
    {
        var parts = v.Split('.').Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out int n) ? n : 0).ToList();
        while (parts.Count < 3) parts.Add(0);
        return new Version(parts[0], parts[1], parts[2]);
    }

    private async Task<PythonHost> ensureHost(CancellationToken ct)
    {
        await hostLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (host is { IsRunning: true }) return host;
            if (host != null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
                host = null;
                info = null;
            }
            string converter = FindConverterDir(settings.ConverterDir, appBase)
                               ?? throw new VoxExportException("converter_missing", settings.ConverterDir != null
                                   ? $"ksh2vox.py not found in {settings.ConverterDir}"
                                   : $"converter not found: no {SubmoduleRelative}/ksh2vox.py above {appBase} (run 'git submodule update --init' or set the converter folder)");
            string script = FindHostScript(settings.HostScript, appBase)
                            ?? throw new VoxExportException("host_missing", $"{HostScriptRelative} not found above {appBase}");
            var lookup = PythonLocator.Find(settings.PythonPath, appBase);
            if (lookup.Command == null)
                throw new VoxExportException("python_missing",
                    $"no usable Python {PythonLocator.MinimumVersion}+ found. Tried:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", lookup.Tried));
            host = await PythonHost.StartAsync(lookup.Command, script, new[] { "--converter", converter },
                                               settings.StartTimeout, settings.Log, ct).ConfigureAwait(false);
            return host;
        }
        finally
        {
            hostLock.Release();
        }
    }

    /// <summary>Starts the host if needed and returns its self-check.</summary>
    public async Task<VoxHostInfo> GetInfoAsync(CancellationToken ct = default)
    {
        var h = await ensureHost(ct).ConfigureAwait(false);
        if (info != null && ReferenceEquals(h, host)) return info;
        var raw = await h.CallAsync("version", timeout: settings.InfoTimeout, ct: ct).ConfigureAwait(false) as JsonObject
                  ?? throw new PythonHostException("protocol", "version returned no object");
        return info = new VoxHostInfo { Raw = raw };
    }

    /// <summary>Writes <paramref name="source"/> into a temporary song folder and converts it.</summary>
    public async Task<VoxExportResult> ExportAsync(VoxExportSource source, VoxExportOptions options,
                                                   IProgress<VoxExportProgress>? progress = null, CancellationToken ct = default)
    {
        var staging = Stage(source, out var stagingWarnings);
        try
        {
            var result = await ExportFolderAsync(staging, options, progress, ct).ConfigureAwait(false);
            return new VoxExportResult
            {
                SongId = result.SongId,
                IsNew = result.IsNew,
                Skipped = result.Skipped,
                SongDir = result.SongDir,
                Files = result.Files,
                MergedDbPath = result.MergedDbPath,
                MapPath = result.MapPath,
                ConverterVersion = result.ConverterVersion,
                Warnings = stagingWarnings.Concat(result.Warnings).ToList(),
                Errors = result.Errors,
                StagingDir = options.KeepStaging ? staging : null,
                Elapsed = result.Elapsed,
            };
        }
        finally
        {
            if (!options.KeepStaging)
            {
                try { Directory.Delete(Path.GetDirectoryName(staging)!, true); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Converts an existing song folder as it is on disk (no staging).</summary>
    public async Task<VoxExportResult> ExportFolderAsync(string songFolder, VoxExportOptions options,
                                                         IProgress<VoxExportProgress>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.OutputDir)) throw new ArgumentException("OutputDir is required", nameof(options));
        var hostInfo = await GetInfoAsync(ct).ConfigureAwait(false);
        if (!hostInfo.ConverterLoaded)
            throw new VoxExportException("converter_missing", hostInfo.ConverterError ?? "the converter could not be loaded");

        var p = new JsonObject
        {
            ["folder"] = Path.GetFullPath(songFolder),
            ["out"] = Path.GetFullPath(options.OutputDir),
            ["force"] = options.Force,
            ["audio_format"] = options.AudioFormat == VoxAudioFormat.S3v ? "s3v" : "2dx",
            ["audio_channels"] = options.Stereo ? "stereo" : "mono",
            ["custom_tag"] = options.CustomTag,
        };
        if (!options.WriteMergedDb) p["merged_db"] = false;
        else if (options.MergedDbPath != null) p["merged_db"] = Path.GetFullPath(options.MergedDbPath);
        if (options.SongId is { } id) p["id"] = id;
        var bases = new List<string>();
        if (options.BaseDbPath != null) bases.Add(Path.GetFullPath(options.BaseDbPath));
        if (options.BaseDbPaths != null) bases.AddRange(options.BaseDbPaths.Select(Path.GetFullPath));
        if (bases.Count == 1) p["base_db"] = bases[0];
        else if (bases.Count > 1) p["base_db"] = new JsonArray(bases.Distinct(StringComparer.OrdinalIgnoreCase).Select(b => (JsonNode)b).ToArray());
        if (options.StartId is { } sid) p["start_id"] = sid;
        if (options.MapPath != null) p["map"] = Path.GetFullPath(options.MapPath);
        if (options.WmaEncoder != null) p["wma_encoder"] = options.WmaEncoder;

        var h = await ensureHost(ct).ConfigureAwait(false);
        Action<string, JsonObject>? onEvent = progress == null
            ? null
            : (_, msg) =>
            {
                if (msg["name"] is JsonValue n && n.TryGetValue(out string? name))
                    progress.Report(new VoxExportProgress(name, msg["fields"] as JsonObject ?? new JsonObject()));
            };
        var res = await h.CallAsync("export_vox", p, onEvent, settings.ExportTimeout, ct).ConfigureAwait(false) as JsonObject
                  ?? throw new PythonHostException("protocol", "export_vox returned no object");

        var warnings = strings(res["warnings"]);
        if (IsOutdated(hostInfo.ConverterVersion))
            warnings.Insert(0, $"converter ksh2vox {hostInfo.ConverterVersion} is older than {RecommendedConverterVersion}");
        return new VoxExportResult
        {
            SongId = res["song_id"]?.GetValue<int>() ?? 0,
            IsNew = res["is_new"] is JsonValue nv && nv.TryGetValue(out bool isNew) ? isNew : null,
            Skipped = res["skipped"] is JsonValue sv && sv.TryGetValue(out bool sk) && sk,
            SongDir = res["song_dir"]?.GetValue<string>(),
            Files = strings(res["files"]),
            MergedDbPath = res["merged_db"] is JsonValue mv && mv.TryGetValue(out string? m) ? m : null,
            MapPath = res["map"] is JsonValue mp && mp.TryGetValue(out string? map) ? map : null,
            ConverterVersion = res["converter_version"]?.GetValue<string>(),
            Warnings = warnings,
            Errors = strings(res["errors"]),
            Elapsed = TimeSpan.FromSeconds(res["elapsed_s"]?.GetValue<double>() ?? 0),
        };
    }

    private static List<string> strings(JsonNode? n)
        => n is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").ToList() : new List<string>();

    /// <summary>
    /// Writes the charts as <c>nov/adv/exh/inf/mxm.kson</c> (the filename carries the slot: kson cannot
    /// state MXM, and ksh2vox resolves the filename first) into <c>&lt;temp&gt;/&lt;SongName&gt;/</c>, copies audio
    /// and jackets next to them and points each chart at its copies. Returns the song folder.
    /// </summary>
    public static string Stage(VoxExportSource source, out List<string> warnings, string? tempRoot = null)
    {
        warnings = new List<string>();
        if (source.Charts.Count == 0) throw new VoxExportException("no_charts", "nothing to export: the song has no charts");
        var dupes = source.Charts.GroupBy(c => Math.Clamp(c.Chart.Meta.DifficultySlot, 0, 4)).Where(g => g.Count() > 1).ToList();
        if (dupes.Count > 0)
            throw new VoxExportException("duplicate_slot",
                $"two charts use the {Difficulty.ShortName(dupes[0].Key)} slot; the game has one chart per difficulty");

        string root = Path.Combine(tempRoot ?? Path.Combine(Path.GetTempPath(), "SDVXEditor"), "vox-" + Guid.NewGuid().ToString("N")[..12]);
        string folder = Path.Combine(root, SafeFolderName(source.SongName));
        Directory.CreateDirectory(folder);

        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // target name → source full path
        string? copy(string? src, string fallbackPrefix)
        {
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) return null;
            string full = Path.GetFullPath(src);
            string name = Path.GetFileName(full);
            if (copied.TryGetValue(name, out string? existing))
            {
                if (string.Equals(existing, full, StringComparison.OrdinalIgnoreCase)) return name;
                name = fallbackPrefix + "_" + name;
            }
            File.Copy(full, Path.Combine(folder, name), true);
            copied[name] = full;
            return name;
        }

        string? firstAudio = null;
        foreach (var c in source.Charts.OrderBy(c => c.Chart.Meta.DifficultySlot))
        {
            int slot = Math.Clamp(c.Chart.Meta.DifficultySlot, 0, 4);
            string label = slotFileNames[slot];
            JsonObject doc = KsonWriter.ToJson(c.Chart);
            var meta = doc["meta"] as JsonObject ?? new JsonObject();
            doc["meta"] = meta;

            // Levels keep their tenth (17.5): ksh2vox 0.16.0 writes difnum = round(level × 10). Older
            // converters truncate a JSON float with int(), which the version warning already covers.

            string? audio = copy(c.AudioPath, label);
            var audioObj = doc["audio"] as JsonObject ?? new JsonObject();
            var bgm = audioObj["bgm"] as JsonObject ?? new JsonObject();
            if (audio != null)
            {
                bgm["filename"] = audio;
                audioObj["bgm"] = bgm;
                doc["audio"] = audioObj;
                if (firstAudio == null) firstAudio = audio;
                else if (!string.Equals(firstAudio, audio, StringComparison.OrdinalIgnoreCase))
                    warnings.Add($"{Difficulty.ShortName(slot)} uses its own audio ({audio}); a VOX song has one audio file and uses {firstAudio}");
            }
            else
            {
                warnings.Add($"{Difficulty.ShortName(slot)}: no audio file found" + (string.IsNullOrEmpty(c.AudioPath) ? "" : $" ({c.AudioPath})"));
            }

            // The preview window always explicit: ksh2vox's default for a missing duration is 10 s,
            // kson's (and the editor's) is 15 s.
            var preview = new JsonObject
            {
                ["offset"] = c.Chart.Audio.Bgm.PreviewOffsetMs,
                ["duration"] = c.Chart.Audio.Bgm.PreviewDurationMs,
            };
            bgm["preview"] = preview;
            audioObj["bgm"] = bgm;
            doc["audio"] = audioObj;

            AdaptTimeSignatures(doc);

            string? jacket = copy(c.JacketPath, label);
            if (jacket != null) meta["jacket_filename"] = jacket;
            else warnings.Add($"{Difficulty.ShortName(slot)}: no jacket image");

            File.WriteAllText(Path.Combine(folder, label + ".kson"), doc.ToJsonString(), new System.Text.UTF8Encoding(false));
        }
        return folder;
    }

    /// <summary>
    /// Rewrites kson 1.0 <c>beat.time_sig</c> entries <c>[idx, [n, d]]</c> into the object form
    /// <c>{"idx", "n", "d"}</c>. ksh2vox 0.16.0's kson reader only knows <c>[idx, n, d]</c> and the object
    /// form; on the spec's nested form it calls <c>int([n, d])</c> and the whole conversion fails.
    /// None of the Nautica .kson files carries a time_sig, so that converter path had never run.
    /// </summary>
    public static void AdaptTimeSignatures(JsonObject doc)
    {
        if (doc["beat"] is not JsonObject beat || beat["time_sig"] is not JsonArray sigs) return;
        var fixedSigs = new JsonArray();
        foreach (var e in sigs)
        {
            if (e is JsonArray a && a.Count >= 2 && a[1] is JsonArray nd && nd.Count >= 2)
                fixedSigs.Add(new JsonObject { ["idx"] = a[0]?.DeepClone(), ["n"] = nd[0]?.DeepClone(), ["d"] = nd[1]?.DeepClone() });
            else
                fixedSigs.Add(e?.DeepClone());
        }
        beat["time_sig"] = fixedSigs;
    }

    /// <summary>A folder name Windows and Linux both accept; keeps Unicode (ksh2vox derives the ascii id from the title, not the folder).</summary>
    public static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }).ToHashSet();
        var chars = name.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray();
        string s = new string(chars).Trim().TrimEnd('.');
        return s.Length == 0 ? "song" : s;
    }

    public async ValueTask DisposeAsync()
    {
        if (host != null) await host.DisposeAsync().ConfigureAwait(false);
        host = null;
        hostLock.Dispose();
    }
}
