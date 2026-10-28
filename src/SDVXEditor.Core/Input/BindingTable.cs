using System.Text;

namespace SDVXEditor.Core.Input;

/// <summary>What a rebind does with other actions that already use the new combination.</summary>
public enum ConflictResolution
{
    /// <summary>Leave them alone: both actions keep the combination (the caller warned the user).</summary>
    Keep,

    /// <summary>The other action takes over the combination this slot had before (or loses it when the slot was empty).</summary>
    Swap,

    /// <summary>The combination is removed from the other action.</summary>
    ClearOther,
}

/// <summary>
/// User-editable key bindings of one scope (the editor, the playtest): a fixed default table plus per-action
/// overrides. Every action has an ordered list of combinations (<see cref="KeyComboText"/>); an override may be
/// empty (deliberately unbound). Conflicts are combinations bound to more than one action of the same scope.
/// <para>
/// Settings text (<see cref="ToOverrideText"/>): only actions that differ from their defaults, as
/// <c>action-id=Combo|Combo</c> joined by <c>, </c>; <see cref="KeyComboText.Unbound"/> stands for "no binding".
/// Action ids are the kebab-case enum names (<c>PlaybackRateUp</c> → <c>playback-rate-up</c>). Unknown ids and
/// invalid combinations are ignored, an empty value keeps the default.
/// </para>
/// </summary>
public sealed class BindingTable<TAction>
    where TAction : struct, Enum
{
    private readonly Dictionary<TAction, string[]> defaults;
    private readonly Dictionary<TAction, string[]> overrides = new Dictionary<TAction, string[]>();

    /// <summary>Raised after any change (set, reset, load).</summary>
    public event Action? Changed;

    /// <param name="defaults">The default table; one action may appear several times (several combinations).</param>
    public BindingTable(IEnumerable<(TAction Action, string Combo)> defaults)
    {
        this.defaults = defaults
                        .Select(d => (d.Action, Combo: KeyComboText.Normalize(d.Combo)))
                        .Where(d => d.Combo != null)
                        .GroupBy(d => d.Action)
                        .ToDictionary(g => g.Key, g => g.Select(d => d.Combo!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>Actions in enum order.</summary>
    public static IReadOnlyList<TAction> Actions { get; } = Enum.GetValues<TAction>();

    public IReadOnlyList<string> DefaultsOf(TAction action) => defaults.TryGetValue(action, out var d) ? d : Array.Empty<string>();

    /// <summary>Effective combinations of an action (override, else default).</summary>
    public IReadOnlyList<string> this[TAction action] => overrides.TryGetValue(action, out var o) ? o : DefaultsOf(action);

    public bool IsDefault(TAction action) => !overrides.ContainsKey(action);

    public bool AllDefault => overrides.Count == 0;

    /// <summary>Every (action, combination) pair in effect.</summary>
    public IEnumerable<(TAction Action, string Combo)> Pairs => Actions.SelectMany(a => this[a].Select(c => (a, c)));

    /// <summary>Replaces the combinations of an action. Equal to the defaults = no override.</summary>
    public void Set(TAction action, IEnumerable<string> combos)
    {
        setQuiet(action, combos);
        Changed?.Invoke();
    }

    private void setQuiet(TAction action, IEnumerable<string> combos)
    {
        var list = combos.Select(KeyComboText.Normalize).Where(c => c != null).Select(c => c!)
                         .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        if (sameList(list, DefaultsOf(action)))
            overrides.Remove(action);
        else
            overrides[action] = list;
    }

    private static bool sameList(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.Zip(b).All(p => string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));

    public void Reset(TAction action)
    {
        if (overrides.Remove(action))
            Changed?.Invoke();
    }

    public void ResetAll()
    {
        if (overrides.Count == 0) return;

        overrides.Clear();
        Changed?.Invoke();
    }

    /// <summary>Resets several actions at once (one <see cref="Changed"/>).</summary>
    public void Reset(IEnumerable<TAction> actions)
    {
        bool any = false;
        foreach (var a in actions) any |= overrides.Remove(a);
        if (any) Changed?.Invoke();
    }

    /// <summary>Actions (other than <paramref name="except"/>) that use <paramref name="combo"/>.</summary>
    public IReadOnlyList<TAction> FindConflicts(string combo, TAction? except = null) =>
        Actions.Where(a => (except == null || !EqualityComparer<TAction>.Default.Equals(a, except.Value))
                           && this[a].Any(c => KeyComboText.Same(c, combo))).ToList();

    /// <summary>Every combination that is bound to more than one action.</summary>
    public IReadOnlyList<(string Combo, IReadOnlyList<TAction> Actions)> AllConflicts() =>
        Pairs.GroupBy(p => p.Combo, StringComparer.OrdinalIgnoreCase)
             .Select(g => (Combo: g.Key, Actions: (IReadOnlyList<TAction>)g.Select(p => p.Action).Distinct().ToList()))
             .Where(g => g.Actions.Count > 1)
             .ToList();

    /// <summary>
    /// Puts <paramref name="combo"/> into slot <paramref name="slot"/> of <paramref name="action"/> (appended when the slot
    /// does not exist yet; null clears the slot) and resolves conflicts as asked.
    /// </summary>
    /// <returns>The actions that used the combination before (the conflicts that were resolved or kept).</returns>
    public IReadOnlyList<TAction> Rebind(TAction action, int slot, string? combo, ConflictResolution resolution = ConflictResolution.Keep)
    {
        if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));

        var current = this[action].ToList();
        string? previous = slot < current.Count ? current[slot] : null;
        string? normalized = KeyComboText.Normalize(combo);

        if (normalized == null)
        {
            if (previous != null) current.RemoveAt(slot);
            setQuiet(action, current);
            Changed?.Invoke();
            return Array.Empty<TAction>();
        }

        var conflicts = FindConflicts(normalized, action);

        // the same combination in another slot of this action moves instead of duplicating
        int existing = current.FindIndex(c => KeyComboText.Same(c, normalized));
        if (existing >= 0 && existing != slot)
        {
            current.RemoveAt(existing);
            if (existing < slot) slot--;
        }

        if (slot < current.Count) current[slot] = normalized;
        else current.Add(normalized);
        setQuiet(action, current);

        if (resolution != ConflictResolution.Keep)
        {
            foreach (var other in conflicts)
            {
                var list = this[other].ToList();
                int at = list.FindIndex(c => KeyComboText.Same(c, normalized));
                if (at < 0) continue;

                // swapping in a combination the other action already has would only duplicate it: then it is just cleared
                if (resolution == ConflictResolution.Swap && previous != null && !list.Any(c => KeyComboText.Same(c, previous)))
                    list[at] = previous;
                else
                    list.RemoveAt(at);

                setQuiet(other, list);
            }
        }

        Changed?.Invoke();
        return conflicts;
    }

    // ---- settings text ------------------------------------------------------

    /// <summary><c>PlaybackRateUp</c> → <c>playback-rate-up</c>, <c>Divisor1</c> → <c>divisor1</c>.</summary>
    public static string IdOf(TAction action)
    {
        string name = action.ToString();
        var sb = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    public static TAction? ActionOf(string id)
    {
        string key = id.Trim().ToLowerInvariant();
        foreach (var a in Actions)
        {
            if (IdOf(a) == key) return a;
        }

        return null;
    }

    /// <summary>Only the overrides, in enum order: <c>tab-timing=F8, open-difficulty=none</c>. Empty when all are default.</summary>
    public string ToOverrideText() =>
        string.Join(", ", Actions.Where(a => overrides.ContainsKey(a))
                                 .Select(a => $"{IdOf(a)}={(overrides[a].Length == 0 ? KeyComboText.Unbound : string.Join("|", overrides[a]))}"));

    /// <summary>Replaces all overrides with the ones in <paramref name="text"/> (see the class remarks).</summary>
    public void LoadOverrides(string? text)
    {
        overrides.Clear();

        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (string part in text.Split(',', ';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                if (ActionOf(part[..eq]) is not TAction action) continue;

                string value = part[(eq + 1)..].Trim();
                if (value.Length == 0) continue; // empty keeps the default

                if (value.Equals(KeyComboText.Unbound, StringComparison.OrdinalIgnoreCase))
                {
                    setQuiet(action, Array.Empty<string>());
                    continue;
                }

                var combos = value.Split('|').Select(KeyComboText.Normalize).Where(c => c != null).Select(c => c!).ToList();
                if (combos.Count > 0) setQuiet(action, combos);
            }
        }

        Changed?.Invoke();
    }
}
