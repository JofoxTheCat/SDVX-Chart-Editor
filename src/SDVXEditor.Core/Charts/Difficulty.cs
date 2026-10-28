namespace SDVXEditor.Core.Charts;

/// <summary>
/// Difficulty slot resolution, ported from ksh2vox <c>_resolve_diff</c> (converter CLAUDE.md §9):
/// filename stem first (exact, then substring), then the chart's own difficulty value.
/// Slots: 0 NOV, 1 ADV, 2 EXH, 3 INF-class, 4 MXM.
/// </summary>
public static class Difficulty
{
    public const int Novice = 0, Advanced = 1, Exhaust = 2, Infinite = 3, Maximum = 4;

    private static readonly Dictionary<string, int> aliases = new()
    {
        ["nov"] = 0, ["novice"] = 0,
        ["adv"] = 1, ["advanced"] = 1,
        ["exh"] = 2, ["exhaust"] = 2,
        ["inf"] = 3, ["infinite"] = 3,
        ["grv"] = 3, ["gravity"] = 3,
        ["hvn"] = 3, ["heaven"] = 3, ["heavenly"] = 3,
        ["vvd"] = 3, ["vivid"] = 3,
        ["xcd"] = 3, ["exceed"] = 3,
        ["nbl"] = 3, ["nabla"] = 3,
        ["mxm"] = 4, ["maximum"] = 4,
        ["light"] = 0, ["challenge"] = 1, ["extended"] = 2,
        ["n"] = 0, ["l"] = 0,
        ["a"] = 1, ["c"] = 1,
        ["e"] = 2,
        ["i"] = 3, ["g"] = 3, ["h"] = 3, ["v"] = 3, ["x"] = 3, ["b"] = 3,
        ["m"] = 4,
    };

    // Checked as substrings of a filename stem, in this order (longest names first).
    private static readonly string[] substrings =
    {
        "novice", "advanced", "exhaust", "infinite", "gravity", "heavenly", "heaven", "vivid", "exceed",
        "nabla", "maximum", "challenge", "extended", "light",
        "nov", "adv", "exh", "inf", "grv", "hvn", "vvd", "xcd", "nbl", "mxm",
    };

    // Infinite-class variant names, used as the slot's display name.
    private static readonly Dictionary<string, string> variantNames = new()
    {
        ["grv"] = "GRV", ["gravity"] = "GRV", ["g"] = "GRV",
        ["hvn"] = "HVN", ["heaven"] = "HVN", ["heavenly"] = "HVN", ["h"] = "HVN",
        ["vvd"] = "VVD", ["vivid"] = "VVD", ["v"] = "VVD",
        ["xcd"] = "XCD", ["exceed"] = "XCD", ["x"] = "XCD",
        ["nbl"] = "NBL", ["nabla"] = "NBL", ["b"] = "NBL",
    };

    private static readonly string[] shortNames = { "NOV", "ADV", "EXH", "INF", "MXM" };
    private static readonly string[] kshNames = { "light", "challenge", "extended", "infinite", "infinite" };

    public static string ShortName(int slot) => shortNames[Math.Clamp(slot, 0, 4)];

    /// <summary>ksh <c>difficulty=</c> value for a slot (MXM is written as infinite).</summary>
    public static string KshName(int slot) => kshNames[Math.Clamp(slot, 0, 4)];

    /// <summary>Lowest level the game shows with a tenth digit (17.5, 18.0, 18.1 …).</summary>
    public const double FirstDecimalLevel = 17;

    /// <summary>
    /// A level as the game displays it: one decimal from <see cref="FirstDecimalLevel"/> up ("18.0"),
    /// whole numbers below ("12"; a stray tenth below 17 is still shown, "16.5").
    /// </summary>
    public static string FormatLevel(double level)
    {
        double rounded = Math.Round(level, 1, MidpointRounding.AwayFromZero);
        string format = rounded >= FirstDecimalLevel ? "0.0" : "0.#";
        return rounded.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Slot from a name alias (case-insensitive) or a numeric 0–4; null if unknown.</summary>
    public static int? FromName(string? value)
    {
        string v = (value ?? "").Trim().ToLowerInvariant();
        if (aliases.TryGetValue(v, out int slot)) return slot;
        if (v.Length == 1 && v[0] is >= '0' and <= '4') return v[0] - '0';
        return null;
    }

    /// <summary>Slot implied by a chart filename alone (exact stem, then substring), or null.</summary>
    public static int? FromFileName(string fileName, out string? variantName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName).Trim().ToLowerInvariant();
        variantName = null;
        if (aliases.TryGetValue(stem, out int slot))
        {
            variantNames.TryGetValue(stem, out variantName);
            return slot;
        }
        foreach (string key in substrings)
        {
            if (stem.Contains(key, StringComparison.Ordinal))
            {
                variantNames.TryGetValue(key, out variantName);
                return aliases[key];
            }
        }
        return null;
    }

    /// <summary>Full <c>_resolve_diff</c>: filename first, then the chart's declared difficulty.</summary>
    public static int? Resolve(string fileName, string? declared)
        => FromFileName(fileName, out _) ?? FromName(declared);
}
