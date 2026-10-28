using SDVXEditor.Core.Input;

namespace SDVXEditor.Core.Gameplay;

/// <summary>Playtest controls.</summary>
public enum PlayAction
{
    BtA,
    BtB,
    BtC,
    BtD,
    FxL,
    FxR,
    LaserLLeft,
    LaserLRight,
    LaserRLeft,
    LaserRRight,
    Start,
    Back,
}

/// <summary>
/// Playtest key bindings as one settings line, e.g.
/// <c>bt-a=D, bt-b=F, bt-c=J, bt-d=K, fx-l=C, fx-r=M, laser-l-left=W, laser-l-right=E, laser-r-left=O, laser-r-right=P, start=Enter, back=Escape</c>
/// (the K-Shoot Mania / USC keyboard layout). A value is one or more input names joined by <c>|</c>: an osuTK key
/// name (<c>D</c>, <c>Enter</c>, <c>Space</c> …) or <c>Joy1</c>…<c>Joy64</c> for controller buttons, or
/// <see cref="KeyComboText.Unbound"/> (<c>none</c>) for no binding. Unknown keys and empty values are ignored; an
/// action that is not named keeps its default. Names are resolved by the game. The bindings are a
/// <see cref="BindingTable{TAction}"/> (<see cref="Table"/>), which the options overlay edits.
/// </summary>
public sealed class PlayBindings
{
    private static readonly (PlayAction Action, string Default)[] defaults =
    {
        (PlayAction.BtA, "D"),
        (PlayAction.BtB, "F"),
        (PlayAction.BtC, "J"),
        (PlayAction.BtD, "K"),
        (PlayAction.FxL, "C"),
        (PlayAction.FxR, "M"),
        (PlayAction.LaserLLeft, "W"),
        (PlayAction.LaserLRight, "E"),
        (PlayAction.LaserRLeft, "O"),
        (PlayAction.LaserRRight, "P"),
        (PlayAction.Start, "Enter"),
        (PlayAction.Back, "Escape"),
    };

    public static readonly string DefaultText = new PlayBindings().ToString();

    /// <summary>Keys the playtest screen handles itself before any binding (autoplay, speed, retry); binding them has no effect.</summary>
    public static readonly IReadOnlyList<(string Key, string Meaning)> Reserved = new[]
    {
        ("Tab", "autoplay"),
        ("F3", "speed down"),
        ("F4", "speed up"),
        ("Minus", "speed down"),
        ("KeypadMinus", "speed down"),
        ("Plus", "speed up"),
        ("KeypadPlus", "speed up"),
        ("Tilde", "retry"),
        ("F2", "gauge (normal / easy / hard, restarts)"),
    };

    /// <summary>The reserved meaning of an input name, or null.</summary>
    public static string? ReservedMeaning(string input) =>
        Reserved.FirstOrDefault(r => string.Equals(r.Key, input, StringComparison.OrdinalIgnoreCase)).Meaning;

    /// <summary>The editable bindings (one input name per combination).</summary>
    public BindingTable<PlayAction> Table { get; } = new BindingTable<PlayAction>(defaults.Select(d => (d.Action, d.Default)));

    /// <summary>Input names bound to an action.</summary>
    public IReadOnlyList<string> this[PlayAction action] => Table[action];

    public static string KeyOf(PlayAction action) => BindingTable<PlayAction>.IdOf(action);

    public static PlayBindings Parse(string? text)
    {
        var b = new PlayBindings();
        b.Table.LoadOverrides(text);
        return b;
    }

    /// <summary>All (input name, action) pairs; one input may drive several actions.</summary>
    public IEnumerable<(string Input, PlayAction Action)> Pairs => Table.Pairs.Select(p => (p.Combo, p.Action));

    /// <summary>The 0-based controller button of a <c>JoyN</c> name, or null.</summary>
    public static int? JoystickButtonOf(string name) =>
        name.Length > 3 && name.StartsWith("joy", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(3), out int n) && n is >= 1 and <= 64 ? n - 1 : null;

    /// <summary>The BT/FX lane (0-5) of a button action, or null.</summary>
    public static int? LaneOf(PlayAction action) => action <= PlayAction.FxR ? (int)action : null;

    /// <summary>Every action, defaults included (the settings line is always complete).</summary>
    public override string ToString() =>
        string.Join(", ", BindingTable<PlayAction>.Actions.Select(a => $"{KeyOf(a)}={(Table[a].Count == 0 ? KeyComboText.Unbound : string.Join("|", Table[a]))}"));
}
