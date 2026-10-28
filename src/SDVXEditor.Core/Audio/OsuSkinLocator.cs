namespace SDVXEditor.Core.Audio;

/// <summary>
/// Finds the user's own osu! skin so the editor can play its hitsounds. Nothing from osu! is ever
/// copied into this repository: samples are read at runtime from the user's install.
/// <para>
/// The active skin is the <c>Skin = …</c> line of <c>%LOCALAPPDATA%\osu!\osu!.&lt;user&gt;.cfg</c>
/// (or <c>osu!.cfg</c>). Those files also hold credentials, so <see cref="ReadSkinLine"/> reads line by
/// line, keeps only the value of that one key and discards everything else unseen.
/// </para>
/// </summary>
public static class OsuSkinLocator
{
    public static readonly string[] SampleExtensions = { ".wav", ".ogg", ".mp3" };

    /// <summary><c>%LOCALAPPDATA%\osu!</c> (osu!stable's default install).</summary>
    public static string DefaultOsuFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "osu!");

    /// <summary>The value of the first <c>Skin = …</c> line, or null. Every other line is skipped without being kept.</summary>
    public static string? ReadSkinLine(string cfgPath)
    {
        try
        {
            foreach (string line in File.ReadLines(cfgPath))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || !line.AsSpan(0, eq).Trim().Equals("Skin", StringComparison.Ordinal)) continue;

                string value = line[(eq + 1)..].Trim();
                return value.Length > 0 ? value : null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>The cfg files to look at, most specific first: osu!.&lt;user&gt;.cfg, other osu!.*.cfg (newest first), osu!.cfg.</summary>
    public static IEnumerable<string> ConfigCandidates(string osuFolder, string? userName = null)
    {
        if (!Directory.Exists(osuFolder)) yield break;

        userName ??= Environment.UserName;
        string own = Path.Combine(osuFolder, $"osu!.{userName}.cfg");
        if (File.Exists(own)) yield return own;

        string global = Path.Combine(osuFolder, "osu!.cfg");
        var others = Directory.EnumerateFiles(osuFolder, "osu!.*.cfg")
                              .Where(f => !string.Equals(f, own, StringComparison.OrdinalIgnoreCase) && !string.Equals(f, global, StringComparison.OrdinalIgnoreCase))
                              .OrderByDescending(File.GetLastWriteTimeUtc);
        foreach (string f in others) yield return f;

        if (File.Exists(global)) yield return global;
    }

    /// <summary>The active skin's folder (<c>&lt;osu&gt;\Skins\&lt;skin&gt;</c>), or null when osu! or the skin is not found.</summary>
    public static string? FindSkinFolder(string? osuFolder = null, string? userName = null)
    {
        osuFolder ??= DefaultOsuFolder;
        foreach (string cfg in ConfigCandidates(osuFolder, userName))
        {
            string? skin = ReadSkinLine(cfg);
            // a folder name only: anything that would leave Skins\ is ignored
            if (skin == null || skin != Path.GetFileName(skin) || skin is "." or "..") continue;

            string folder = Path.Combine(osuFolder, "Skins", skin);
            if (Directory.Exists(folder)) return folder;
        }

        return null;
    }

    /// <summary>Setting value: the active skin of the osu! install (default).</summary>
    public const string AutoSetting = "";

    /// <summary>Setting value: the editor's own synthesised sounds only.</summary>
    public const string BuiltInSetting = "built-in";

    /// <summary>Skin folder names under <c>&lt;osu&gt;\Skins</c>, sorted (empty when osu! is not installed).</summary>
    public static IReadOnlyList<string> ListSkins(string? osuFolder = null)
    {
        string skins = Path.Combine(osuFolder ?? DefaultOsuFolder, "Skins");
        try
        {
            return Directory.Exists(skins)
                ? Directory.EnumerateDirectories(skins).Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList()
                : Array.Empty<string>();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// The folder a hitsound-skin setting stands for: <see cref="AutoSetting"/> = the active osu! skin,
    /// <see cref="BuiltInSetting"/> = none, an absolute path = that folder, anything else = a skin name
    /// under <c>&lt;osu&gt;\Skins</c>. Null when there is no such folder.
    /// </summary>
    public static string? ResolveSkinSetting(string? setting, string? osuFolder = null, string? userName = null)
    {
        setting = setting?.Trim() ?? AutoSetting;
        if (setting.Length == 0) return FindSkinFolder(osuFolder, userName);
        if (string.Equals(setting, BuiltInSetting, StringComparison.OrdinalIgnoreCase)) return null;
        if (Path.IsPathFullyQualified(setting)) return Directory.Exists(setting) ? setting : null;
        if (setting != Path.GetFileName(setting) || setting is "." or "..") return null;

        string folder = Path.Combine(osuFolder ?? DefaultOsuFolder, "Skins", setting);
        return Directory.Exists(folder) ? folder : null;
    }

    /// <summary>The file for a sample stem in a folder (.wav, .ogg or .mp3), or null.</summary>
    public static string? ResolveSample(string? folder, string stem)
    {
        if (folder == null || !HitsoundMap.IsValidValue(stem)) return null;

        foreach (string ext in SampleExtensions)
        {
            string path = Path.Combine(folder, stem + ext);
            if (File.Exists(path)) return path;
        }

        return null;
    }
}
