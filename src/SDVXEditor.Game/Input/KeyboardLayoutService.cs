using osu.Framework;
using osu.Framework.Input.Bindings;
using osu.Framework.Logging;
using osu.Framework.Platform.SDL3;
using osuTK.Input;
using SDL;
using SDVXEditor.Core.Input;

namespace SDVXEditor.Game.Input;

/// <summary>Where the active keyboard layout comes from (an interface so tests and the smoke run can simulate one).</summary>
public interface IKeyboardLayoutSource
{
    /// <summary>Reads the layout the OS has active now.</summary>
    KeyboardLayout Read();
}

/// <summary>
/// The layout through SDL, the framework's own windowing layer: <c>SDL_GetKeyFromScancode</c> per physical key, the
/// same call osu-framework's <c>SDL3ReadableKeyCombinationProvider</c> uses for key labels. Works on Windows (SDL reads
/// the Win32 layout itself), Linux (XKB / Wayland keymap) and macOS. Before SDL's video subsystem is up it answers with
/// the US default map — measured, see docs/ARCHITECTURE.md §12.
/// </summary>
public sealed class SdlKeyboardLayoutSource : IKeyboardLayoutSource
{
    /// <summary>Every physical key whose character matters: letters, punctuation (number row and F-keys stay positional).</summary>
    private static readonly InputKey[] character_keys = Enumerable.Range((int)InputKey.A, 26).Select(i => (InputKey)i)
                                                                  .Concat(new[]
                                                                  {
                                                                      InputKey.Tilde, InputKey.Minus, InputKey.Plus, InputKey.BracketLeft, InputKey.BracketRight,
                                                                      InputKey.Semicolon, InputKey.Quote, InputKey.Comma, InputKey.Period, InputKey.Slash,
                                                                      InputKey.BackSlash, InputKey.NonUSBackSlash,
                                                                  }).ToArray();

    public KeyboardLayout Read()
    {
        var pairs = new List<(string, char)>();

        foreach (var key in character_keys)
        {
            var scancode = key.ToScancode();
            if (scancode == SDL_Scancode.SDL_SCANCODE_UNKNOWN) continue;

            uint keycode = FrameworkEnvironment.UseSDL3
                ? (uint)SDL3.SDL_GetKeyFromScancode(scancode, SDL_Keymod.SDL_KMOD_NONE, false)
                : (uint)global::SDL2.SDL.SDL_GetKeyFromScancode((global::SDL2.SDL.SDL_Scancode)scancode);

            // keycodes below the scancode mask are the Unicode character the key types
            if (keycode is > 0 and < 0x40000000 and <= char.MaxValue)
                pairs.Add((key.ToString(), (char)keycode));
        }

        return KeyboardLayout.FromCharacters("system", pairs);
    }
}

/// <summary>
/// The keyboard layout editor shortcuts follow (Game side of Core <see cref="KeyboardLayout"/>).
/// <para>
/// osu-framework 2026.807.0 key events carry only the physical key (<c>KeyDownEvent.Key</c> = osuTK <c>Key</c> mapped
/// from <c>SDL_KeyboardEvent.scancode</c> in <c>SDL3Extensions.ToKey</c>; there is no keycode/character field). Binding
/// texts name letters logically, so the editor's key binding container listens on <see cref="ToPhysical"/> of every
/// combination, the options capture stores <see cref="ToLogical"/> of what was pressed, and labels come from
/// <see cref="LabelOf"/>. <see cref="Refresh"/> re-reads the layout; the game calls it on start, on the framework's
/// <c>KeymapChanged</c> (SDL_EVENT_KEYMAP_CHANGED, e.g. Win+Space) and when the window regains focus.
/// Playtest keys stay physical (position-based, like osu!'s gameplay keys).
/// </para>
/// </summary>
public static class KeyboardLayoutService
{
    private static IKeyboardLayoutSource source = new SdlKeyboardLayoutSource();
    private static KeyboardLayout? simulated;

    /// <summary>The layout the OS reported at the last <see cref="Refresh"/> (US until the first one).</summary>
    public static KeyboardLayout System { get; private set; } = KeyboardLayout.Qwerty;

    /// <summary>The layout in effect: the simulated one when set, else <see cref="System"/>.</summary>
    public static KeyboardLayout Current => simulated ?? System;

    /// <summary>Raised after <see cref="Current"/> changed (the binding containers reload, menus relabel).</summary>
    public static event Action? Changed;

    /// <summary>Replaces where the layout is read from (tests).</summary>
    public static void SetSource(IKeyboardLayoutSource newSource)
    {
        source = newSource;
        Refresh();
    }

    /// <summary>
    /// Forces a layout regardless of the OS (null = follow the OS again). The smoke test runs on a simulated US layout
    /// so its injected physical keys mean the same on every machine, and on QWERTZ for the Ctrl+Z check.
    /// </summary>
    public static void Simulate(KeyboardLayout? layout)
    {
        if (ReferenceEquals(simulated, layout)) return;

        simulated = layout;
        Logger.Log($"keyboard layout: {(layout != null ? layout.Name + " (simulated)" : "following the system")}");
        Changed?.Invoke();
    }

    /// <summary>Re-reads the OS layout; raises <see cref="Changed"/> when a character moved.</summary>
    public static void Refresh()
    {
        KeyboardLayout next;

        try
        {
            next = source.Read();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            Logger.Log($"keyboard layout: cannot read ({e.Message}); keeping {System.Name}");
            return;
        }

        bool changed = !sameCharacters(System, next);
        System = next;

        if (changed)
        {
            Logger.Log($"keyboard layout: system Z→{next.PhysicalKeyFor("Z")}, Y→{next.PhysicalKeyFor("Y")}, A→{next.PhysicalKeyFor("A")}, M→{next.PhysicalKeyFor("M")}");
            if (simulated == null) Changed?.Invoke();
        }
    }

    private static bool sameCharacters(KeyboardLayout a, KeyboardLayout b) =>
        Enum.GetNames<Key>().All(n => a.CharacterOf(n) == b.CharacterOf(n));

    // ---- framework types ---------------------------------------------------------

    /// <summary>A binding's combination (letters logical) as the physical keys to listen for.</summary>
    public static KeyCombination ToPhysical(KeyCombination keys) =>
        new KeyCombination(keys.Keys.Select(k => physicalOf(k)).ToArray());

    private static InputKey physicalOf(InputKey key)
    {
        if (key is < InputKey.A or > InputKey.Z) return key;

        return Enum.TryParse(Current.PhysicalKeyFor(key.ToString()), out InputKey physical) ? physical : key;
    }

    /// <summary>The binding-text name of a pressed physical key (QWERTZ physical Y → "Z"); null when it cannot be named.</summary>
    public static string? ToLogical(InputKey physical) => Current.LogicalKeyFor(physical.ToString());

    /// <summary>
    /// The letter a physical key types on the active layout as a logical <see cref="Key"/> (QWERTZ physical Y →
    /// <see cref="Key.Z"/>); non-letter keys are returned unchanged. For code that checks <c>e.Key</c> by hand.
    /// </summary>
    public static Key Logical(Key physical) =>
        Current.LogicalKeyFor(physical.ToString()) is { } name && Enum.TryParse(name, out Key logical) ? logical : physical;

    /// <summary>What is printed on a physical key on the active layout ("Ö", "-"), or null for keys without a character.</summary>
    public static string? LabelOf(InputKey physical) => Current.LabelOf(physical.ToString());
}
