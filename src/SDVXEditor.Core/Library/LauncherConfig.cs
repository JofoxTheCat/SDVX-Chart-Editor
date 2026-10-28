using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SDVXEditor.Core.Library;

public sealed class LauncherConfigException : Exception
{
    /// <summary><c>config_not_found</c>, <c>config_unreadable</c> (I/O, invalid UTF-8, invalid JSON) or <c>config_not_object</c>.</summary>
    public string Kind { get; }

    public string Path { get; }

    public LauncherConfigException(string kind, string path, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Path = path;
    }
}

/// <summary>Outcome of <see cref="LauncherConfig.Locate"/>.</summary>
public sealed record LauncherConfigLookup(LauncherConfig? Config, LauncherConfigException? Error, IReadOnlyList<string> Searched)
{
    public bool Found => Config != null;
}

/// <summary>
/// Read-only view of the launcher's <c>sdvx_rpc_config.json</c> (shared by launcher V1 and V2).
/// The editor only needs <c>nautica_charts_path</c> — the folder the launcher downloads Nautica songs into.
/// <para>
/// Parsing follows the launcher bridge: UTF-8 with an optional BOM, and a file that exists but does not
/// parse is an error (<c>config_unreadable</c>) — never treated as an empty config. NaN/Infinity, which
/// Python's json would accept, are refused too, exactly as the bridge refuses them.
/// </para>
/// </summary>
public sealed class LauncherConfig
{
    public const string FileName = "sdvx_rpc_config.json";
    public const string ChartsKey = "nautica_charts_path";

    /// <summary>
    /// Developer machines: example configs that exist only there. A list, not logic — add entries here.
    /// Missing paths are skipped silently.
    /// </summary>
    public static readonly List<string> DevLocations = new()
    {
        @"D:\SDVX-DEV\SDVX-Nabla-Launcher-v2-dev\sdvx_rpc_config.json",
    };

    public string Path { get; }
    public JsonObject Root { get; }

    /// <summary><c>nautica_charts_path</c> resolved like V1 resolves it: relative to the config's folder. Null when unset.</summary>
    public string? NauticaChartsPath { get; }

    private LauncherConfig(string path, JsonObject root)
    {
        Path = path;
        Root = root;
        NauticaChartsPath = ResolvePath(GetString(ChartsKey));
    }

    /// <summary>
    /// The game folder (spice64.exe, data/), found like the launcher bridge's <c>_game_dir</c>: the folder of
    /// <c>game_exe_path</c> when that file exists, else the config's own folder when spice64.exe sits there. Null otherwise.
    /// </summary>
    public string? GameDir
    {
        get
        {
            string? exe = ResolvePath(GetString("game_exe_path"));
            if (exe != null && File.Exists(exe)) return System.IO.Path.GetDirectoryName(exe);
            string near = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? "";
            return File.Exists(System.IO.Path.Combine(near, "spice64.exe")) ? near : null;
        }
    }

    /// <summary>
    /// The Asphyxia SDVX plugin folder, found like the bridge's <c>_plugin_dir</c>: under the folder of
    /// <c>asphyxia_exe</c>, the <c>plugins/*</c> folder that carries index.ts and handlers/. Null otherwise.
    /// </summary>
    public string? PluginDir
    {
        get
        {
            string? exe = ResolvePath(GetString("asphyxia_exe"));
            string? root = exe == null ? null : System.IO.Path.GetDirectoryName(exe);
            string plugins = root == null ? "" : System.IO.Path.Combine(root, "plugins");
            if (!Directory.Exists(plugins)) return null;
            return Directory.GetDirectories(plugins).OrderBy(d => d, StringComparer.Ordinal)
                            .FirstOrDefault(d => File.Exists(System.IO.Path.Combine(d, "index.ts")) && Directory.Exists(System.IO.Path.Combine(d, "handlers")));
        }
    }

    /// <summary>
    /// The music dbs whose real songs a custom song id must stay above (ksh2vox ≥ 0.20.1 takes the max over all):
    /// the plugin's music_db.json and the game's data/others/music_db.xml — the json lags behind game updates
    /// (build 2026071400: json 2384, xml 2403). Only files that exist.
    /// </summary>
    public IReadOnlyList<string> BaseMusicDbs
    {
        get
        {
            var list = new List<string>();
            if (PluginDir is { } plugin && System.IO.Path.Combine(plugin, "webui", "asset", "json", "music_db.json") is var json && File.Exists(json))
                list.Add(json);
            if (GameDir is { } game && System.IO.Path.Combine(game, "data", "others", "music_db.xml") is var xml && File.Exists(xml))
                list.Add(xml);
            return list;
        }
    }

    public string? GetString(string key)
        => Root[key] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    /// <summary>A path value from the config: quotes stripped, relative paths taken relative to the config file.</summary>
    public string? ResolvePath(string? value)
    {
        string v = (value ?? "").Trim().Trim('"', '\'');
        if (v.Length == 0) return null;
        if (System.IO.Path.IsPathRooted(v)) return System.IO.Path.GetFullPath(v);
        string dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? "";
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, v));
    }

    public static LauncherConfig Load(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full)) throw new LauncherConfigException("config_not_found", full, $"launcher config not found: {full}");
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(full);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new LauncherConfigException("config_unreadable", full, $"cannot read {full}: {e.Message}", e);
        }
        return Parse(bytes, full);
    }

    public static LauncherConfig Parse(byte[] bytes, string path)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) span = span[3..];
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(span);
        }
        catch (DecoderFallbackException e)
        {
            throw new LauncherConfigException("config_unreadable", path, $"{path} is not valid UTF-8", e);
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException e)
        {
            throw new LauncherConfigException("config_unreadable", path, $"{path} is not valid JSON ({e.Message}); fix or replace it in the launcher", e);
        }
        if (node is not JsonObject obj)
            throw new LauncherConfigException("config_not_object", path, $"{path} does not hold a JSON object");
        return new LauncherConfig(path, obj);
    }

    /// <summary>
    /// Where a launcher config may be, in order: the working directory (the launcher starts tools from its
    /// own folder, and V1 itself resolves the bare file name against the cwd), the editor's folder and its
    /// parents (editor installed inside the launcher folder), then <see cref="DevLocations"/>.
    /// </summary>
    public static IReadOnlyList<string> DefaultCandidates(string? appBaseDir = null, string? workingDir = null)
    {
        var list = new List<string>();
        void add(string? dir)
        {
            if (string.IsNullOrEmpty(dir)) return;
            string p = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, FileName));
            if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
        }
        add(workingDir ?? Environment.CurrentDirectory);
        var d = new DirectoryInfo(appBaseDir ?? AppContext.BaseDirectory);
        for (int i = 0; d != null && i < 4; i++, d = d.Parent) add(d.FullName);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            foreach (var p in DevLocations)
                if (!list.Contains(p, StringComparer.OrdinalIgnoreCase)) list.Add(p);
        return list;
    }

    /// <summary>
    /// An explicit path (<c>--launcher-config</c>) is used as is — missing or broken is an error, never a
    /// silent fallback. Otherwise the first EXISTING candidate decides; if it does not parse, the search
    /// stops with that error instead of guessing at another file.
    /// </summary>
    public static LauncherConfigLookup Locate(string? explicitPath, IEnumerable<string>? candidates = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string p = explicitPath.Trim().Trim('"');
            if (Directory.Exists(p)) p = System.IO.Path.Combine(p, FileName);
            try
            {
                return new LauncherConfigLookup(Load(p), null, new[] { System.IO.Path.GetFullPath(p) });
            }
            catch (LauncherConfigException e)
            {
                return new LauncherConfigLookup(null, e, new[] { System.IO.Path.GetFullPath(p) });
            }
        }
        var searched = new List<string>();
        foreach (string c in candidates ?? DefaultCandidates())
        {
            searched.Add(c);
            if (!File.Exists(c)) continue;
            try
            {
                return new LauncherConfigLookup(Load(c), null, searched);
            }
            catch (LauncherConfigException e)
            {
                return new LauncherConfigLookup(null, e, searched);
            }
        }
        return new LauncherConfigLookup(null, null, searched);
    }
}
