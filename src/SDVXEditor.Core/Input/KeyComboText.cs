namespace SDVXEditor.Core.Input;

/// <summary>
/// Text form of one key combination as stored in the settings: modifiers first in a fixed order, then the key(s),
/// joined by <c>+</c> — <c>Ctrl+Shift+P</c>, <c>Alt+MouseWheelUp</c>, <c>F5</c>, <c>Joy3</c>. Key names are whatever the
/// game resolves (osu-framework <c>InputKey</c> / osuTK <c>Key</c> names, <c>JoyN</c>); Core only canonicalises
/// the modifiers and compares case-insensitively, so <c>shift+ctrl+p</c> and <c>Ctrl+Shift+P</c> are the same binding.
/// </summary>
public static class KeyComboText
{
    /// <summary>The value that marks an action as deliberately unbound (an empty value keeps the default).</summary>
    public const string Unbound = "none";

    private static readonly (string Canonical, string[] Aliases)[] modifiers =
    {
        ("Ctrl", new[] { "ctrl", "control" }),
        ("Shift", new[] { "shift" }),
        ("Alt", new[] { "alt" }),
        ("Super", new[] { "super", "win", "meta", "cmd" }),
    };

    /// <summary>The canonical modifier name of a token ("control" → "Ctrl"), or null for a normal key.</summary>
    public static string? ModifierOf(string token)
    {
        string t = token.Trim().ToLowerInvariant();
        foreach (var (canonical, aliases) in modifiers)
        {
            if (aliases.Contains(t))
                return canonical;
        }

        return null;
    }

    /// <summary>
    /// Canonical form: modifiers de-duplicated in Ctrl, Shift, Alt, Super order, then the other keys in their given
    /// order (duplicates removed). Returns null for an empty or modifier-only combination, or one with an invalid token.
    /// </summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var mods = new HashSet<string>();
        var keys = new List<string>();

        foreach (string raw in text.Split('+'))
        {
            string token = raw.Trim();
            if (token.Length == 0 || token.Length > 32 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
                return null;

            if (ModifierOf(token) is string m)
                mods.Add(m);
            else if (!keys.Contains(token, StringComparer.OrdinalIgnoreCase))
                keys.Add(token);
        }

        if (keys.Count == 0) return null;

        return string.Join("+", modifiers.Select(m => m.Canonical).Where(mods.Contains).Concat(keys));
    }

    /// <summary>True when both name the same combination (canonical, case-insensitive).</summary>
    public static bool Same(string? a, string? b)
    {
        string? na = Normalize(a), nb = Normalize(b);
        return na != null && nb != null && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }
}
