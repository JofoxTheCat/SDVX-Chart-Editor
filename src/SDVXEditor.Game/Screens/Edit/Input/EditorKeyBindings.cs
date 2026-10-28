using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Input;

namespace SDVXEditor.Game.Screens.Edit.Input;

/// <summary>Every editor command that has a key. Bound in <see cref="EditorKeyBindings.All"/>.</summary>
public enum EditorAction
{
    // tabs / windows
    TabCompose,
    TabCamera,
    TabTiming,
    TabEffects,
    SongSetup,
    Playtest,
    TimingSetup,
    Exit,
    ShowShortcuts,

    /// <summary>The options overlay (outside the editor also Ctrl+O, see <see cref="EditorKeyBindings"/>).</summary>
    ShowOptions,

    // transport
    PlayPause,
    PlayFromStart,
    SeekStart,
    SeekEnd,
    SeekBackward,
    SeekForward,
    SeekBackwardFar,
    SeekForwardFar,
    PreviousTimingPoint,
    NextTimingPoint,
    PlaybackRateUp,
    PlaybackRateDown,
    PlaybackRateUpFine,
    PlaybackRateDownFine,

    // view
    DivisorNext,
    DivisorPrevious,
    Divisor1,
    Divisor2,
    Divisor3,
    Divisor4,
    Divisor5,
    Divisor6,
    Divisor7,
    Divisor8,
    Divisor9,

    /// <summary>Alt+wheel up, routed by hover: timeline or playfield = the one shared zoom, elsewhere = volume.</summary>
    AltWheelUp,

    /// <summary>Alt+wheel down, routed like <see cref="AltWheelUp"/>.</summary>
    AltWheelDown,

    // bookmarks
    AddBookmark,
    RemoveBookmark,
    PreviousBookmark,
    NextBookmark,

    // tools / toggles
    ToolSelect,
    ToolBtChip,
    ToolBtHold,
    ToolFxChip,
    ToolFxHold,
    ToolLaser,

    /// <summary>Laser tool draws laser L (Q).</summary>
    LaserSideLeft,

    /// <summary>Laser tool draws laser R (E).</summary>
    LaserSideRight,

    ToolNext,
    ToolPrevious,
    ToggleGridSnap,
    ToggleWideLaser,
    ToggleSlamAssist,
    ToggleLockNotes,
    ToggleMirrorPreview,

    /// <summary>
    /// Held (not pressed): classic K-Shoot / kson-editor laser placement and editing while down (Core <c>ClassicLaser</c>).
    /// Polled by the editor screen like the hold modifier; once it is part of another combination it does nothing until
    /// released.
    /// </summary>
    ClassicLaserHold,

    // chart edits (need the chart model; dispatched to IEditorChartActions)
    Save,
    OpenDifficulty,

    /// <summary>File → Create a New Difficulty (copy or empty, then a free slot, then Song Setup). Unbound by default.</summary>
    NewDifficulty,

    ClearAllNotes,
    RevertToSaved,
    RevertToSavedFull,
    ChartCheck,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    Delete,
    SelectAll,
    Clone,
    MirrorHorizontal,
    ReverseInTime,
    ReverseSelection,
    NudgeBackward,
    NudgeForward,
    MoveSelectionLaneLeft,
    MoveSelectionLaneRight,
    MoveSelectionEarlier,
    MoveSelectionLater,
    ToggleSelectionWide,
    ToggleSelectionSlam,
    LiveMapping,

    // timing
    AddBpmPoint,
    AddScrollSpeedPoint,
    DeleteTimingPoint,

    // ---- feature batch 2026-10-27 (handled by EditorScreen.Features.cs, outside the chart-action range) ----

    /// <summary>Insert one snap tick of blank time at the cursor (everything after moves later).</summary>
    InsertBlankTick,

    /// <summary>Insert one beat of blank time at the cursor.</summary>
    InsertBlankBeat,

    /// <summary>Insert blank time of a chosen length (dialog).</summary>
    InsertBlankCustom,

    /// <summary>Delete the selection's time range (or the range in the dialog) and pull everything after it back.</summary>
    DeleteRange,

    /// <summary>Selected lasers change colour (L ↔ R), values kept.</summary>
    SwapLaserColour,

    /// <summary>Loop playback over the selection / the bookmarks around the cursor / the current measure.</summary>
    ToggleLoop,

    ToggleInspector,
    ToggleLayers,

    /// <summary>Ctrl+click on an FX hold cycles its effect (K-Shoot Editor); this key does the same for the selected holds.</summary>
    CycleHoldEffect,

    DivisorSet12,
    DivisorSet16,
    DivisorSet24,
    DivisorSet32,
    DivisorSet48,
    DivisorSet64,
    DivisorSet96,
    DivisorSet192,
}

/// <summary>One row of the central binding table.</summary>
public sealed record EditorKeyBinding(KeyCombination Keys, EditorAction Action, string Category, string Description);

/// <summary>
/// The one place that lists every editor binding (osu!stable editor layout, osu-editor-spec.md
/// 2.3/2.4) with SDVX meanings. The key binding container, the Help → Keyboard Shortcuts window and the
/// options overlay all read this table.
/// Deviations from osu!stable: Ctrl+arrows move the selection (lane / snap tick), so the
/// stable bookmark jump moves to Alt+Left/Right and playback speed to Alt+Up/Down. Ctrl+O stays
/// "open difficulty" inside the editor (stable); the options are Ctrl+Shift+O there and Ctrl+O everywhere else.
/// <para>
/// <see cref="Defaults"/> is the fixed default table; <see cref="Table"/> holds the user's overrides (editor.ini
/// <c>EditorBindings</c>, Core <see cref="BindingTable{TAction}"/>) and <see cref="All"/> is the table in effect.
/// <see cref="Changed"/> fires on every change, so the binding containers reload live.
/// Alt+wheel (<see cref="EditorAction.AltWheelUp"/>/<see cref="EditorAction.AltWheelDown"/>) is routed by what the
/// cursor is over and is not rebindable (<see cref="IsFixed"/>).
/// </para>
/// </summary>
public static class EditorKeyBindings
{
    private const InputKey ctrl = InputKey.Control;
    private const InputKey shift = InputKey.Shift;
    private const InputKey alt = InputKey.Alt;

    /// <summary>The default table (one row per default combination; an action may have several).</summary>
    public static readonly IReadOnlyList<EditorKeyBinding> Defaults = build();

    /// <summary>The user-editable bindings (defaults + overrides).</summary>
    public static readonly BindingTable<EditorAction> Table =
        new BindingTable<EditorAction>(Defaults.Select(d => (d.Action, Format(d.Keys))));

    /// <summary>Actions in table order (the order of their first default row; actions without one last).</summary>
    private static readonly EditorAction[] ordered = Enum.GetValues<EditorAction>()
        .OrderBy(a => Defaults.Select((d, i) => (d, i)).FirstOrDefault(x => x.d.Action == a) is { d: not null } x ? x.i : int.MaxValue)
        .ToArray();

    private static IReadOnlyList<EditorKeyBinding> all = Defaults;

    /// <summary>The bindings in effect, one row per combination, grouped like <see cref="Defaults"/>.</summary>
    public static IReadOnlyList<EditorKeyBinding> All => all;

    /// <summary>Raised after the bindings changed (rebind, reset, settings reload).</summary>
    public static event Action? Changed;

    static EditorKeyBindings()
    {
        Table.Changed += () =>
        {
            all = buildEffective();
            Changed?.Invoke();
        };

        // letters follow the keyboard layout: the containers re-map them, menus and Help relabel punctuation
        KeyboardLayoutService.Changed += () => Changed?.Invoke();
    }

    /// <summary>Actions whose key is context-dependent and cannot be rebound.</summary>
    public static bool IsFixed(EditorAction action) => action is EditorAction.AltWheelUp or EditorAction.AltWheelDown;

    /// <summary>Loads the overrides from the editor.ini text; fixed actions always keep their defaults.</summary>
    public static void Apply(string? overrides)
    {
        Table.LoadOverrides(overrides);
        Table.Reset(ordered.Where(IsFixed));
    }

    /// <summary>The overrides as editor.ini text.</summary>
    public static string OverrideText => Table.ToOverrideText();

    /// <summary>Category and description of an action (from its first default row).</summary>
    public static (string Category, string Description) InfoOf(EditorAction action)
    {
        var row = Defaults.FirstOrDefault(d => d.Action == action);
        return row != null ? (row.Category, row.Description) : ("General", action.ToString());
    }

    /// <summary>Categories in table order (the Help dialog's tabs, the options' keybind groups).</summary>
    public static IReadOnlyList<string> Categories => Defaults.Select(d => d.Category).Distinct().ToList();

    /// <summary>Actions of a category in table order.</summary>
    public static IReadOnlyList<EditorAction> ActionsIn(string category) => ordered.Where(a => InfoOf(a).Category == category).ToList();

    private static List<EditorKeyBinding> buildEffective()
    {
        var list = new List<EditorKeyBinding>();

        foreach (var action in ordered)
        {
            var (category, description) = InfoOf(action);
            var rows = Defaults.Where(d => d.Action == action).ToList();

            foreach (string combo in Table[action])
            {
                if (Parse(combo) is not KeyCombination keys) continue;

                // a default combination keeps its own row's description
                var same = rows.FirstOrDefault(r => KeyComboText.Same(Format(r.Keys), combo));
                list.Add(new EditorKeyBinding(keys, action, category, same?.Description ?? description));
            }
        }

        return list;
    }

    /// <summary>Settings text of a combination: <c>Ctrl+Shift+P</c>, <c>Alt+MouseWheelUp</c>.</summary>
    public static string Format(KeyCombination keys) =>
        KeyComboText.Normalize(string.Join("+", keys.Keys.Select(k => k switch
        {
            InputKey.Control or InputKey.LControl or InputKey.RControl => "Ctrl",
            InputKey.Shift or InputKey.LShift or InputKey.RShift => "Shift",
            InputKey.Alt or InputKey.LAlt or InputKey.RAlt => "Alt",
            InputKey.Super or InputKey.LSuper or InputKey.RSuper => "Super",
            _ => k.ToString(),
        }))) ?? string.Empty;

    /// <summary>The combination of a settings text, or null when a token is no <see cref="InputKey"/>.</summary>
    public static KeyCombination? Parse(string combo)
    {
        if (KeyComboText.Normalize(combo) is not string normalized) return null;

        var keys = new List<InputKey>();
        foreach (string token in normalized.Split('+'))
        {
            InputKey key;
            switch (KeyComboText.ModifierOf(token))
            {
                case "Ctrl": key = InputKey.Control; break;
                case "Shift": key = InputKey.Shift; break;
                case "Alt": key = InputKey.Alt; break;
                case "Super": key = InputKey.Super; break;
                default:
                    if (!Enum.TryParse(token, true, out key) || key == InputKey.None || int.TryParse(token, out _)) return null;
                    break;
            }

            keys.Add(key);
        }

        return new KeyCombination(keys.ToArray());
    }

    /// <summary>"Ctrl+Shift+P" for a settings text (shown on buttons); the raw text when it does not parse.</summary>
    public static string Describe(string combo) => Parse(combo) is KeyCombination k ? Describe(k) : combo;

    /// <summary>The current first combination of an action for menus and toasts ("Ctrl+S"), empty when unbound.</summary>
    public static string ShortcutOf(EditorAction action) =>
        All.FirstOrDefault(b => b.Action == action) is { } binding ? Describe(binding.Keys) : string.Empty;

    private static EditorKeyBinding b(EditorAction action, string category, string description, params InputKey[] keys) =>
        new EditorKeyBinding(new KeyCombination(keys), action, category, description);

    private static List<EditorKeyBinding> build()
    {
        var list = new List<EditorKeyBinding>
        {
            b(EditorAction.TabCompose, "General", "Compose tab", InputKey.F1),
            b(EditorAction.TabCamera, "General", "Camera tab", InputKey.F2),
            b(EditorAction.TabEffects, "General", "Effects tab", shift, InputKey.F2),
            b(EditorAction.TabTiming, "General", "Timing tab", InputKey.F3),
            b(EditorAction.SongSetup, "General", "Song Setup", InputKey.F4),
            b(EditorAction.Playtest, "General", "Test chart from the current position", InputKey.F5),
            b(EditorAction.TimingSetup, "General", "Timing and Control Points window", InputKey.F6),
            b(EditorAction.Exit, "General", "Back to song select", InputKey.Escape),
            b(EditorAction.Save, "General", "Save", ctrl, InputKey.S),
            b(EditorAction.OpenDifficulty, "General", "Open difficulty", ctrl, InputKey.O),
            b(EditorAction.NewDifficulty, "General", "Create a new difficulty (copy or empty)"), // no default key, bindable in the options
            b(EditorAction.ClearAllNotes, "General", "Clear all notes", ctrl, InputKey.N),
            b(EditorAction.RevertToSaved, "General", "Revert to saved", ctrl, InputKey.L),
            b(EditorAction.RevertToSavedFull, "General", "Revert to saved (full)", ctrl, shift, InputKey.L),
            b(EditorAction.ChartCheck, "General", "Check chart for problems", ctrl, shift, InputKey.A),
            b(EditorAction.ShowShortcuts, "General", "Keyboard shortcuts", ctrl, InputKey.Slash),
            b(EditorAction.ShowOptions, "General", "Options", ctrl, shift, InputKey.O),

            b(EditorAction.PlayPause, "Playback", "Pause / resume", InputKey.Space),
            b(EditorAction.PlayPause, "Playback", "Pause / resume", InputKey.C),
            b(EditorAction.PlayFromStart, "Playback", "Play from the start", InputKey.X),
            b(EditorAction.SeekStart, "Playback", "Jump to start", InputKey.Z),
            b(EditorAction.SeekEnd, "Playback", "Jump to end", InputKey.V),
            b(EditorAction.SeekBackward, "Playback", "Seek one snap tick back", InputKey.Left),
            b(EditorAction.SeekForward, "Playback", "Seek one snap tick forward", InputKey.Right),
            b(EditorAction.SeekBackwardFar, "Playback", "Seek four ticks back", shift, InputKey.Left),
            b(EditorAction.SeekForwardFar, "Playback", "Seek four ticks forward", shift, InputKey.Right),
            b(EditorAction.SeekBackward, "Playback", "Seek one snap tick back", InputKey.MouseWheelUp),
            b(EditorAction.SeekForward, "Playback", "Seek one snap tick forward", InputKey.MouseWheelDown),
            b(EditorAction.SeekBackwardFar, "Playback", "Seek four ticks back", shift, InputKey.MouseWheelUp),
            b(EditorAction.SeekForwardFar, "Playback", "Seek four ticks forward", shift, InputKey.MouseWheelDown),
            b(EditorAction.PreviousTimingPoint, "Playback", "Previous timing point", InputKey.Up),
            b(EditorAction.NextTimingPoint, "Playback", "Next timing point", InputKey.Down),
            b(EditorAction.PlaybackRateUp, "Playback", "Playback speed +25 %", alt, InputKey.Up),
            b(EditorAction.PlaybackRateDown, "Playback", "Playback speed -25 %", alt, InputKey.Down),
            b(EditorAction.PlaybackRateUpFine, "Playback", "Playback speed +5 %", alt, shift, InputKey.Up),
            b(EditorAction.PlaybackRateDownFine, "Playback", "Playback speed -5 %", alt, shift, InputKey.Down),

            b(EditorAction.DivisorNext, "View", "Finer beat snap divisor", ctrl, InputKey.MouseWheelUp),
            b(EditorAction.DivisorPrevious, "View", "Coarser beat snap divisor", ctrl, InputKey.MouseWheelDown),
            b(EditorAction.AltWheelUp, "View", "Timeline/playfield: zoom in (one shared zoom) · side areas: volume up", alt, InputKey.MouseWheelUp),
            b(EditorAction.AltWheelDown, "View", "Timeline/playfield: zoom out (one shared zoom) · side areas: volume down", alt, InputKey.MouseWheelDown),

            b(EditorAction.AddBookmark, "Bookmarks", "Add bookmark", ctrl, InputKey.B),
            b(EditorAction.RemoveBookmark, "Bookmarks", "Remove nearest bookmark (within 2 s)", ctrl, shift, InputKey.B),
            b(EditorAction.PreviousBookmark, "Bookmarks", "Previous bookmark", alt, InputKey.Left),
            b(EditorAction.NextBookmark, "Bookmarks", "Next bookmark", alt, InputKey.Right),

            b(EditorAction.ToolSelect, "Compose", "Select tool", InputKey.Number1),
            b(EditorAction.ToolBtChip, "Compose", "BT chip tool (Shift: FX chip · hold Ctrl/Alt: hold)", InputKey.Number2),
            b(EditorAction.ToolBtHold, "Compose", "BT hold tool (Shift: FX hold)", InputKey.Number3),
            b(EditorAction.ToolFxChip, "Compose", "FX chip tool (hold Ctrl/Alt: FX hold)", InputKey.Number4),
            b(EditorAction.ToolFxHold, "Compose", "FX hold tool", InputKey.Number5),
            b(EditorAction.ToolLaser, "Compose", "Laser tool; again: switch laser L/R (Shift while drawing = wide)", InputKey.Number6),
            b(EditorAction.LaserSideLeft, "Compose", "Laser tool on laser L", InputKey.Q),
            b(EditorAction.LaserSideRight, "Compose", "Laser tool on laser R", InputKey.E),
            b(EditorAction.ToolNext, "Compose", "Next tool", ctrl, alt, InputKey.MouseWheelDown),
            b(EditorAction.ToolPrevious, "Compose", "Previous tool", ctrl, alt, InputKey.MouseWheelUp),
            b(EditorAction.ToggleGridSnap, "Compose", "Grid snap (Timing tab: tap tempo)", InputKey.T),
            b(EditorAction.ToggleWideLaser, "Compose", "Wide laser", InputKey.W),
            b(EditorAction.ToggleSlamAssist, "Compose", "Slam assist", InputKey.S),
            b(EditorAction.ToggleLockNotes, "Compose", "Lock notes", InputKey.L),
            b(EditorAction.ToggleMirrorPreview, "Compose", "Mirror preview", InputKey.M),
            // F: no editor binding uses F with or without Ctrl / Shift / Alt, so holding it beside the hold modifier, Shift
            // (FX / wide) or a playback key can never fire another shortcut; home row of the left hand, the mouse hand stays free
            b(EditorAction.ClassicLaserHold, "Compose", "Hold: classic lasers (K-Shoot style: plain points, 1/10 value grid, click the last point again to finish)", InputKey.F),

            b(EditorAction.Undo, "Edit", "Undo", ctrl, InputKey.Z),
            b(EditorAction.Redo, "Edit", "Redo", ctrl, InputKey.Y),
            b(EditorAction.Cut, "Edit", "Cut", ctrl, InputKey.X),
            b(EditorAction.Copy, "Edit", "Copy", ctrl, InputKey.C),
            b(EditorAction.Paste, "Edit", "Paste at the current time", ctrl, InputKey.V),
            b(EditorAction.Delete, "Edit", "Delete selection", InputKey.Delete),
            b(EditorAction.SelectAll, "Edit", "Select all", ctrl, InputKey.A),
            b(EditorAction.Clone, "Edit", "Clone selection after itself", ctrl, InputKey.D),
            b(EditorAction.MirrorHorizontal, "Edit", "Mirror: BT A-D, B-C, FX L-R, laser L-R (v to 1-v)", ctrl, InputKey.H),
            b(EditorAction.ReverseInTime, "Edit", "Reverse selection in time", ctrl, InputKey.J),
            b(EditorAction.ReverseSelection, "Edit", "Invert selection", ctrl, InputKey.G),
            b(EditorAction.NudgeBackward, "Edit", "Nudge selection one tick back", InputKey.J),
            b(EditorAction.NudgeForward, "Edit", "Nudge selection one tick forward", InputKey.K),
            b(EditorAction.MoveSelectionLaneLeft, "Edit", "Move selection one lane left", ctrl, InputKey.Left),
            b(EditorAction.MoveSelectionLaneRight, "Edit", "Move selection one lane right", ctrl, InputKey.Right),
            b(EditorAction.MoveSelectionEarlier, "Edit", "Move selection one snap tick earlier", ctrl, InputKey.Down),
            b(EditorAction.MoveSelectionLater, "Edit", "Move selection one snap tick later", ctrl, InputKey.Up),
            b(EditorAction.ToggleSelectionWide, "Edit", "Toggle wide on selected lasers", ctrl, InputKey.W),
            b(EditorAction.ToggleSelectionSlam, "Edit", "Toggle slam on selected laser points", ctrl, InputKey.E),
            b(EditorAction.LiveMapping, "Edit", "Live mapping mode", ctrl, InputKey.Tab),

            b(EditorAction.AddBpmPoint, "Timing", "Add BPM point at the current time", ctrl, InputKey.P),
            b(EditorAction.AddScrollSpeedPoint, "Timing", "Add scroll speed point", ctrl, shift, InputKey.P),
            b(EditorAction.DeleteTimingPoint, "Timing", "Delete the current timing point", ctrl, InputKey.I),
            b(EditorAction.InsertBlankTick, "Timing", "Insert one snap tick of blank time at the cursor", InputKey.Insert),
            b(EditorAction.InsertBlankBeat, "Timing", "Insert one beat of blank time at the cursor", shift, InputKey.Insert),
            b(EditorAction.InsertBlankCustom, "Timing", "Insert blank time / delete a range... (dialog)", alt, InputKey.Insert),
            b(EditorAction.DeleteRange, "Timing", "Delete the selected time range (everything after moves back)", ctrl, shift, InputKey.Delete),

            b(EditorAction.SwapLaserColour, "Edit", "Swap laser colour L-R (values kept)", ctrl, shift, InputKey.H),
            b(EditorAction.CycleHoldEffect, "Edit", "Cycle the effect of the selected FX holds (also Ctrl+click a hold)", ctrl, shift, InputKey.E),
            b(EditorAction.ToggleLoop, "Playback", "Loop playback (selection / bookmarks around the cursor / measure)", shift, InputKey.Space),
            b(EditorAction.ToggleInspector, "View", "Inspector (exact values of the selection)", ctrl, shift, InputKey.I),
            b(EditorAction.ToggleLayers, "View", "Layers (show / hide object types)", ctrl, shift, InputKey.U),
        };

        // Shift+1..9: divisor 1/1..1/9 (stable). 1/5, 1/7, 1/9 are no snap stop (VOX has 48 ticks per beat) and snap to the nearest stop.
        for (int i = 1; i <= 9; i++)
            list.Add(b(EditorAction.Divisor1 + (i - 1), "View", $"Beat snap divisor 1/{i}", shift, InputKey.Number1 + (i - 1)));

        // "Set quantize to 1/N" for the finer divisors (unbound by default; Shift+1..9 cover 1/1..1/9)
        foreach (var (action, d) in new[] { (EditorAction.DivisorSet12, 12), (EditorAction.DivisorSet16, 16), (EditorAction.DivisorSet24, 24), (EditorAction.DivisorSet32, 32),
                     (EditorAction.DivisorSet48, 48), (EditorAction.DivisorSet64, 64), (EditorAction.DivisorSet96, 96), (EditorAction.DivisorSet192, 192) })
            list.Add(b(action, "View", $"Beat snap divisor 1/{d}"));

        return list;
    }

    /// <summary>
    /// "Ctrl+Shift+P", "Wheel up" … for the shortcuts window. Letters are logical (the key labelled Z), other character
    /// keys show what the active layout prints on that position (QWERTZ: Ctrl+Slash reads "Ctrl+-").
    /// </summary>
    public static string Describe(KeyCombination keys) =>
        string.Join("+", keys.Keys.OrderBy(order).Select(k => describe(k, false)));

    /// <summary>Like <see cref="Describe(KeyCombination)"/> for a physical key (playtest bindings): letters are labelled by the layout too.</summary>
    public static string DescribePhysical(KeyCombination keys) =>
        string.Join("+", keys.Keys.OrderBy(order).Select(k => describe(k, true)));

    private static int order(InputKey key) => key switch
    {
        InputKey.Control => 0,
        InputKey.Shift => 1,
        InputKey.Alt => 2,
        InputKey.Super => 3,
        _ => 4,
    };

    private static string describe(InputKey key, bool physical) => key switch
    {
        >= InputKey.A and <= InputKey.Z when !physical => key.ToString(),
        InputKey.Control => "Ctrl",
        InputKey.Super => "Win",
        InputKey.MouseMiddle => "Middle click",
        InputKey.ExtraMouseButton1 => "Mouse 4",
        InputKey.ExtraMouseButton2 => "Mouse 5",
        >= InputKey.Joystick1 and <= InputKey.Joystick64 => $"Joy{key - InputKey.Joystick1 + 1}",
        InputKey.BackSpace => "Backspace",
        InputKey.PageUp => "Page up",
        InputKey.PageDown => "Page down",
        InputKey.MouseWheelUp => "Wheel up",
        InputKey.MouseWheelDown => "Wheel down",
        InputKey.Escape => "Esc",
        InputKey.Delete => "Del",
        >= InputKey.Number0 and <= InputKey.Number9 => ((int)(key - InputKey.Number0)).ToString(),
        _ => KeyboardLayoutService.LabelOf(key) ?? key.ToString(),
    };
}

/// <summary>
/// Key binding container fed from <see cref="EditorKeyBindings.All"/> (user overrides included), reloaded live when
/// they change. Exact matching: Ctrl+Z never also fires Z.
/// </summary>
public partial class EditorKeyBindingContainer : KeyBindingContainer<EditorAction>
{
    public EditorKeyBindingContainer()
        : base(SimultaneousBindingMode.None, KeyCombinationMatchingMode.Exact)
    {
        RelativeSizeAxes = Axes.Both;
    }

    /// <summary>
    /// The table's letters are logical: listen on the physical keys that type them on the active layout. A Shift+wheel
    /// binding also listens on the horizontal wheel key the framework turns Shift+wheel into (<see cref="WheelInput"/>).
    /// </summary>
    public override IEnumerable<IKeyBinding> DefaultKeyBindings =>
        EditorKeyBindings.All.SelectMany(k => WheelInput.WithShiftWheelAlias(KeyboardLayoutService.ToPhysical(k.Keys))
                                                        .Select(c => (IKeyBinding)new KeyBinding(c, k.Action)));

    protected override bool HandleRepeats => true;

    /// <summary>How many times the mappings were reloaded after a change (smoke test).</summary>
    public int Reloads { get; private set; }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        EditorKeyBindings.Changed += onChanged;
    }

    // the reload happens on the update thread, whoever changed the table
    private void onChanged() => Schedule(() =>
    {
        ReloadMappings();
        Reloads++;
    });

    protected override void Dispose(bool isDisposing)
    {
        EditorKeyBindings.Changed -= onChanged;
        base.Dispose(isDisposing);
    }
}

/// <summary>Forwards actions from the binding container to a callback (the editor screen sits above the container).</summary>
public partial class EditorActionReceiver : Component, IKeyBindingHandler<EditorAction>
{
    private readonly Func<EditorAction, bool, bool> pressed;

    /// <param name="pressed">(action, isRepeat) → handled.</param>
    public EditorActionReceiver(Func<EditorAction, bool, bool> pressed)
    {
        this.pressed = pressed;
    }

    public bool OnPressed(KeyBindingPressEvent<EditorAction> e) => pressed(e.Action, e.Repeat);

    public void OnReleased(KeyBindingReleaseEvent<EditorAction> e)
    {
    }
}

/// <summary>
/// Chart-model actions (undo, copy, mirror, nudge …), implemented against Core commands by
/// <c>EditorChartActions</c>; <see cref="StubChartActions"/> remains for screens without a chart.
/// </summary>
public interface IEditorChartActions
{
    /// <returns>true when the action was carried out.</returns>
    bool Perform(EditorAction action);
}

/// <summary>Placeholder: reports every chart action as "not wired yet".</summary>
public sealed class StubChartActions : IEditorChartActions
{
    public bool Perform(EditorAction action) => false;
}
