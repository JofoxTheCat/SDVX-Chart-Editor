namespace SDVXEditor.Core.Input;

/// <summary>
/// What a keyboard layout types on each physical key, and the two translations editor shortcuts need.
/// <para>
/// osu-framework delivers keys as <b>physical</b> positions (SDL scancodes: <c>KeyDownEvent.Key</c> is the US-QWERTY
/// name of the position, whatever the layout). Editor letter shortcuts are <b>logical</b> instead — Ctrl+Z is the key
/// <i>labelled</i> Z, which on a German QWERTZ keyboard is the physical Y position. Binding texts
/// (<see cref="KeyComboText"/>) therefore store letters A–Z logically; everything else (digits, F-keys, arrows,
/// punctuation, mouse) stays physical, as osu! keeps it.
/// </para>
/// Key names are the framework's <c>InputKey</c> / osuTK <c>Key</c> names ("Y", "Semicolon", "Slash"); Core never
/// resolves them. A layout is built from (physical name, unshifted character) pairs — the game asks SDL
/// (<c>SDL_GetKeyFromScancode</c>), tests use <see cref="Qwerty"/>, <see cref="Qwertz"/>, <see cref="Azerty"/>.
/// </summary>
public sealed class KeyboardLayout
{
    /// <summary>A short name for logs and the smoke report ("de-QWERTZ (simulated)", "system").</summary>
    public string Name { get; }

    private readonly Dictionary<string, char> charOf = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<char, string> keyOfLetter = new Dictionary<char, string>();

    private KeyboardLayout(string name, IEnumerable<(string Physical, char Char)> keys)
    {
        Name = name;

        foreach (var (physical, c) in keys)
        {
            if (string.IsNullOrEmpty(physical) || c == '\0') continue;

            char lower = char.ToLowerInvariant(c);
            charOf[physical] = lower;

            if (lower is >= 'a' and <= 'z')
            {
                // two positions typing the same letter (unusual): the one that carries the letter's own name wins
                if (!keyOfLetter.ContainsKey(lower) || string.Equals(physical, lower.ToString(), StringComparison.OrdinalIgnoreCase))
                    keyOfLetter[lower] = physical;
            }
        }
    }

    /// <summary>A layout from (physical key name, character typed without modifiers) pairs.</summary>
    public static KeyboardLayout FromCharacters(string name, IEnumerable<(string Physical, char Char)> keys) => new KeyboardLayout(name, keys);

    /// <summary>True for a single ASCII letter token ("Z", "y").</summary>
    public static bool IsLetterKey(string token) => token.Length == 1 && char.IsAsciiLetter(token[0]);

    /// <summary>The unshifted character of a physical key on this layout, or null when unknown / not a character key.</summary>
    public char? CharacterOf(string physical) => charOf.TryGetValue(physical, out char c) ? c : null;

    /// <summary>
    /// The physical key to listen for when a binding names <paramref name="key"/>: a letter goes to the position that
    /// types it on this layout (QWERTZ: Z → Y); a letter no position types (a Cyrillic layout) falls back to the
    /// same-named position, like osu!. Every other key name is returned unchanged.
    /// </summary>
    public string PhysicalKeyFor(string key)
    {
        if (!IsLetterKey(key)) return key;

        char letter = char.ToLowerInvariant(key[0]);
        return keyOfLetter.TryGetValue(letter, out string? physical) ? physical : char.ToUpperInvariant(letter).ToString();
    }

    /// <summary>
    /// The binding text for a pressed physical key: the letter it types when that is A–Z (QWERTZ: physical Y → "Z",
    /// AZERTY: Semicolon → "M"), else the physical name. The inverse of <see cref="PhysicalKeyFor"/>.
    /// Null for the one position a binding text cannot name: a letter-named position that types no letter while its
    /// letter sits elsewhere (AZERTY physical M types ','; "M" already means the semicolon position).
    /// </summary>
    public string? LogicalKeyFor(string physical)
    {
        if (charOf.TryGetValue(physical, out char c) && c is >= 'a' and <= 'z' && keyOfLetter.TryGetValue(c, out string? owner)
            && string.Equals(owner, physical, StringComparison.OrdinalIgnoreCase))
            return char.ToUpperInvariant(c).ToString();

        if (!IsLetterKey(physical)) return physical;

        // a letter position typing something else: only nameable while no other position types that letter
        string upper = physical.ToUpperInvariant();
        return PhysicalKeyFor(upper).Equals(upper, StringComparison.OrdinalIgnoreCase) ? upper : null;
    }

    /// <summary>What is printed on a physical key: its character upper-cased ("Ö", "-", "Z"), or null when the layout has none.</summary>
    public string? LabelOf(string physical) =>
        charOf.TryGetValue(physical, out char c) && !char.IsControl(c) && c != ' ' ? char.ToUpperInvariant(c).ToString() : null;

    /// <summary>Every token of a binding text through <see cref="PhysicalKeyFor"/> (modifiers stay).</summary>
    public string ToPhysicalCombo(string combo) => mapTokens(combo, PhysicalKeyFor);

    /// <summary>Every token of a physical combination through <see cref="LogicalKeyFor"/> (modifiers stay); null when a key cannot be named.</summary>
    public string? ToLogicalCombo(string combo)
    {
        var tokens = combo.Split('+').Select(t => KeyComboText.ModifierOf(t) != null ? t : LogicalKeyFor(t.Trim())).ToList();
        return tokens.Any(t => t == null) ? null : string.Join("+", tokens);
    }

    private static string mapTokens(string combo, Func<string, string> map) =>
        string.Join("+", combo.Split('+').Select(t => KeyComboText.ModifierOf(t) != null ? t : map(t.Trim())));

    public override string ToString() => Name;

    // ---- fixtures (tests, smoke simulation) ------------------------------------

    // physical rows of a 105-key ISO board, US names
    private static readonly string[] row1 = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "BracketLeft", "BracketRight" };
    private static readonly string[] row2 = { "A", "S", "D", "F", "G", "H", "J", "K", "L", "Semicolon", "Quote", "BackSlash" };
    private static readonly string[] row3 = { "NonUSBackSlash", "Z", "X", "C", "V", "B", "N", "M", "Comma", "Period", "Slash" };
    private static readonly string[] numberRow = { "Tilde", "Number1", "Number2", "Number3", "Number4", "Number5", "Number6", "Number7", "Number8", "Number9", "Number0", "Minus", "Plus" };

    private static IEnumerable<(string, char)> rows(string r1, string r2, string r3, string numbers) =>
        row1.Zip(r1).Concat(row2.Zip(r2)).Concat(row3.Zip(r3)).Concat(numberRow.Zip(numbers));

    /// <summary>US QWERTY (the framework's own key names).</summary>
    public static readonly KeyboardLayout Qwerty = new KeyboardLayout("en-US QWERTY",
        rows("qwertyuiop[]", "asdfghjkl;'\\", "\\zxcvbnm,./", "`1234567890-="));

    /// <summary>German QWERTZ (Y and Z swapped, umlauts on the punctuation positions; what SDL reports for layout 00000407).</summary>
    public static readonly KeyboardLayout Qwertz = new KeyboardLayout("de-DE QWERTZ",
        rows("qwertzuiopü+", "asdfghjklöä#", "<yxcvbnm,.-", "^1234567890ß´"));

    /// <summary>French AZERTY (A/Q and Z/W swapped, M on the semicolon position, symbols on the number row).</summary>
    public static readonly KeyboardLayout Azerty = new KeyboardLayout("fr-FR AZERTY",
        rows("azertyuiop^$", "qsdfghjklmù*", "<wxcvbn,;:!", "²&é\"'(-è_çà)="));
}
