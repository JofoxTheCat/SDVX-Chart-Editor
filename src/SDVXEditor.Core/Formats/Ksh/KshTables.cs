using System.Globalization;
using System.Text;

namespace SDVXEditor.Core.Formats.Ksh;

/// <summary>Name and value tables shared by the ksh reader and writer.</summary>
internal static class KshTables
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ------------------------------------------------------------ encoding

    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    /// <summary>
    /// UTF-8 BOM → UTF-8 (what KSM v2 and all Nautica charts write); otherwise strict UTF-8 if it decodes,
    /// else Shift_JIS (cp932), the encoding of KSM v1 charts.
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        try
        {
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932).GetString(bytes);
        }
    }

    // ------------------------------------------------------------ laser values

    /// <summary>ksh laser char → 0..50, or -1 if not a position char.</summary>
    public static int LaserChar(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c - 'A' + 10,
        >= 'a' and <= 'o' => c - 'a' + 36,
        _ => -1,
    };

    public static char LaserCharOf(double v)
    {
        int i = (int)Math.Round(Math.Clamp(v, 0, 1) * 50, MidpointRounding.AwayFromZero);
        return i < 10 ? (char)('0' + i) : i < 36 ? (char)('A' + i - 10) : (char)('a' + i - 36);
    }

    // ------------------------------------------------------------ effects

    private static readonly (string Ksh, string Kson)[] effectTypes =
    {
        ("Retrigger", "retrigger"), ("Gate", "gate"), ("Flanger", "flanger"), ("PitchShift", "pitch_shift"),
        ("BitCrusher", "bitcrusher"), ("Phaser", "phaser"), ("Wobble", "wobble"), ("TapeStop", "tapestop"),
        ("Echo", "echo"), ("SideChain", "sidechain"), ("SwitchAudio", "switch_audio"),
        ("PeakingFilter", "peaking_filter"), ("HighPassFilter", "high_pass_filter"), ("LowPassFilter", "low_pass_filter"),
    };

    private static readonly (string Ksh, string Kson)[] legacyFilters =
    {
        ("peak", "peaking_filter"), ("hpf1", "high_pass_filter"), ("lpf1", "low_pass_filter"), ("bitc", "bitcrusher"),
    };

    /// <summary>ksh built-in effect/type name (PascalCase) → kson name; unknown names pass through.</summary>
    public static string EffectToKson(string name)
    {
        foreach (var (k, j) in effectTypes)
            if (k == name) return j;
        return name;
    }

    public static string EffectToKsh(string name)
    {
        foreach (var (k, j) in effectTypes)
            if (j == name) return k;
        return name;
    }

    public static bool IsBuiltinKsonEffect(string name) => effectTypes.Any(e => e.Kson == name);

    public static string FilterToKson(string name)
    {
        foreach (var (k, j) in legacyFilters)
            if (k == name) return j;
        return EffectToKson(name);
    }

    public static string FilterToKsh(string name)
    {
        foreach (var (k, j) in legacyFilters)
            if (j == name) return k;
        return EffectToKsh(name);
    }

    private static readonly (string Ksh, string Kson)[] specialParams =
    {
        ("loFreq", "freq_1"), ("hiFreq", "freq_2"), ("Q", "q"), ("overWrap", "overlap"), ("volume", "vol"),
    };

    /// <summary>ksh camelCase parameter → kson snake_case (with the spec's renames).</summary>
    public static string ParamToKson(string name)
    {
        foreach (var (k, j) in specialParams)
            if (k == name) return j;
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string ParamToKsh(string name)
    {
        foreach (var (k, j) in specialParams)
            if (j == name) return k;
        var sb = new StringBuilder();
        bool up = false;
        foreach (char c in name)
        {
            if (c == '_') { up = true; continue; }
            sb.Append(up ? char.ToUpperInvariant(c) : c);
            up = false;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parameters of an <c>fx-l=Name;p1;p2</c> invocation, interpreted by the effect type
    /// (verified on ABYSS MOTION: <c>ga;8</c> on a gate definition = <c>{"wave_length": "1/8"}</c>).
    /// </summary>
    public static Dictionary<string, string> InvocationParams(string type, IReadOnlyList<string> args)
    {
        var p = new Dictionary<string, string>();
        if (args.Count == 0 || args[0].Length == 0) return p;
        switch (type)
        {
            case "retrigger":
            case "gate":
            case "wobble":
                p["wave_length"] = "1/" + args[0];
                break;
            case "echo":
                p["wave_length"] = "1/" + args[0];
                if (args.Count > 1 && args[1].Length > 0) p["feedback_level"] = args[1] + "%";
                break;
            case "pitch_shift":
                p["pitch"] = args[0];
                break;
            case "bitcrusher":
                p["reduction"] = args[0] + "samples";
                break;
            case "tapestop":
                p["speed"] = args[0] + "%";
                break;
        }
        return p;
    }

    /// <summary>Inverse of <see cref="InvocationParams"/>; null when the parameters have no ksh short form.</summary>
    public static string? InvocationArgs(string type, IReadOnlyDictionary<string, string> p)
    {
        if (p.Count == 0) return "";
        static string? Strip(string v, string prefix, string suffix)
            => v.StartsWith(prefix, StringComparison.Ordinal) && v.EndsWith(suffix, StringComparison.Ordinal) && v.Length > prefix.Length + suffix.Length
                ? v[prefix.Length..^suffix.Length] : null;
        switch (type)
        {
            case "retrigger":
            case "gate":
            case "wobble":
                return p.Count == 1 && p.TryGetValue("wave_length", out var wl) ? Strip(wl, "1/", "") : null;
            case "echo":
            {
                if (!p.TryGetValue("wave_length", out var w) || Strip(w, "1/", "") is not { } n) return null;
                if (p.Count == 1) return n;
                return p.Count == 2 && p.TryGetValue("feedback_level", out var fb) && Strip(fb, "", "%") is { } f ? n + ";" + f : null;
            }
            case "pitch_shift":
                return p.Count == 1 && p.TryGetValue("pitch", out var pi) ? pi : null;
            case "bitcrusher":
                return p.Count == 1 && p.TryGetValue("reduction", out var r) ? Strip(r, "", "samples") : null;
            case "tapestop":
                return p.Count == 1 && p.TryGetValue("speed", out var s) ? Strip(s, "", "%") : null;
        }
        return null;
    }

    /// <summary>KSM v1 FX hold letters (ksh_format.md, legacy) → (effect, args). Not present in the sample charts.</summary>
    public static readonly IReadOnlyDictionary<char, (string Effect, string[] Args)> LegacyFxChars = new Dictionary<char, (string, string[])>
    {
        ['S'] = ("Retrigger", new[] { "8" }), ['V'] = ("Retrigger", new[] { "12" }), ['T'] = ("Retrigger", new[] { "16" }),
        ['W'] = ("Retrigger", new[] { "24" }), ['U'] = ("Retrigger", new[] { "32" }),
        ['G'] = ("Gate", new[] { "4" }), ['H'] = ("Gate", new[] { "8" }), ['K'] = ("Gate", new[] { "12" }),
        ['I'] = ("Gate", new[] { "16" }), ['L'] = ("Gate", new[] { "24" }), ['J'] = ("Gate", new[] { "32" }),
        ['F'] = ("Flanger", Array.Empty<string>()), ['P'] = ("PitchShift", Array.Empty<string>()),
        ['B'] = ("BitCrusher", Array.Empty<string>()), ['Q'] = ("Phaser", Array.Empty<string>()),
        ['X'] = ("Wobble", new[] { "12" }), ['A'] = ("TapeStop", Array.Empty<string>()), ['D'] = ("SideChain", Array.Empty<string>()),
    };

    public static string SlamSoundToKson(string v) => v switch
    {
        "up" => "slam_up",
        "down" => "slam_down",
        "swing" => "slam_swing",
        "mute" => "slam_mute",
        _ => v,
    };

    public static string SlamSoundToKsh(string v) => v.StartsWith("slam_", StringComparison.Ordinal) ? v[5..] : v;

    public static string Fmt(double d) => d.ToString("0.############", Inv);

    public static bool TryNum(string s, out double d) => double.TryParse(s.Trim(), NumberStyles.Float, Inv, out d);
}
