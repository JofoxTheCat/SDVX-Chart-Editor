using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game.Overlays.Options;

public enum CapturedKind
{
    Keyboard,
    MouseButton,
    Wheel,
    Joystick,
}

/// <summary>One input the capture saw: a key (with the modifiers held), a mouse button, a wheel notch or a controller button.</summary>
public readonly record struct CapturedInput(CapturedKind Kind, Key Key = Key.Unknown, InputKey InputKey = InputKey.None, int JoystickButton = -1,
                                            bool Ctrl = false, bool Shift = false, bool Alt = false, bool Super = false)
{
    public bool AnyModifier => Ctrl || Shift || Alt || Super;

    public static CapturedInput FromKey(KeyDownEvent e) =>
        new CapturedInput(CapturedKind.Keyboard, e.Key, Ctrl: e.ControlPressed, Shift: e.ShiftPressed, Alt: e.AltPressed, Super: e.SuperPressed);

    public static bool IsModifierKey(Key key) => key is Key.LControl or Key.RControl or Key.LShift or Key.RShift or Key.LAlt or Key.RAlt or Key.LWin or Key.RWin;

    /// <summary>"Ctrl+Shift+" for the held modifiers.</summary>
    public string ModifierPrefix => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + (Super ? "Super+" : "");
}

/// <summary>
/// A family of bindings the options edit: the editor actions or the playtest controls. Scopes are separate —
/// a key may mean one thing in the editor and another in the playtest — so conflicts are only looked for inside one.
/// </summary>
public abstract class KeybindScope
{
    public abstract string Name { get; }

    /// <summary>
    /// The settings text for a captured input, or an error to show. (null, null) = not a binding by itself, keep
    /// waiting (a modifier key pressed alone in the editor scope).
    /// </summary>
    public abstract (string? Combo, string? Error) Translate(CapturedInput input);

    /// <summary>"Ctrl+Shift+P", "Joy3" for a button.</summary>
    public abstract string Describe(string combo);
}

/// <summary>Editor actions: key combinations with modifiers, the middle and side mouse buttons and the wheel.</summary>
public sealed class EditorKeybindScope : KeybindScope
{
    public override string Name => "editor";

    public override (string? Combo, string? Error) Translate(CapturedInput input)
    {
        switch (input.Kind)
        {
            case CapturedKind.Keyboard:
                if (CapturedInput.IsModifierKey(input.Key)) return (null, null);
                var key = KeyCombination.FromKey(input.Key);
                if (key == InputKey.None) return (null, $"{input.Key} cannot be bound");
                // letters are stored as the layout types them (QWERTZ: the key labelled Z = physical Y gives "Z")
                if (KeyboardLayoutService.ToLogical(key) is not string name)
                    return (null, $"This key ({KeyboardLayoutService.LabelOf(key) ?? key.ToString()}) has no letter of its own on this keyboard layout");
                return (KeyComboText.Normalize(input.ModifierPrefix + name), null);

            case CapturedKind.MouseButton:
            case CapturedKind.Wheel:
                return (KeyComboText.Normalize(input.ModifierPrefix + input.InputKey), null);

            default:
                return (null, "Controller buttons only drive the playtest");
        }
    }

    public override string Describe(string combo) => EditorKeyBindings.Describe(combo);
}

/// <summary>Playtest controls: single keys (osuTK names, modifier keys allowed) and controller buttons (<c>JoyN</c>).</summary>
public sealed class PlaytestKeybindScope : KeybindScope
{
    public override string Name => "playtest";

    public override (string? Combo, string? Error) Translate(CapturedInput input)
    {
        switch (input.Kind)
        {
            case CapturedKind.Keyboard:
                string name = input.Key.ToString();
                if (PlayBindings.ReservedMeaning(name) is string meaning)
                    return (null, $"{name} is fixed in the playtest ({meaning})");
                return (KeyComboText.Normalize(name), null);

            case CapturedKind.Joystick:
                return ($"Joy{input.JoystickButton + 1}", null);

            default:
                return (null, "The playtest reads keys and controller buttons, not the mouse");
        }
    }

    public override string Describe(string combo) =>
        PlayBindings.JoystickButtonOf(combo) is int b ? $"Joy{b + 1}"
        // playtest keys are positions: show what the active layout prints on that position (QWERTZ: physical Z reads "Y")
        : Enum.TryParse<Key>(combo, true, out var key) && KeyCombination.FromKey(key) is var k && k != InputKey.None ? EditorKeyBindings.DescribePhysical(new KeyCombination(k))
        : combo;
}

/// <summary>Receives what the overlay captured while one of the row's binding buttons waits for input.</summary>
public interface IKeyCaptureTarget
{
    /// <summary>Feeds an input; returns false when it was not a binding by itself (keep waiting).</summary>
    bool Capture(CapturedInput input);

    /// <summary>Delete / Backspace: removes the binding of the waiting slot.</summary>
    void ClearSlot();

    /// <summary>Esc or a click elsewhere: stops waiting without a change.</summary>
    void CancelCapture();
}

/// <summary>One binding slot: shows the combination, or "press a key…" while capturing.</summary>
public partial class KeyBindButton : ClickableContainer
{
    public const float WIDTH = 132;

    private readonly Box background;
    private readonly SpriteText text;
    private bool capturing;
    private string combo = string.Empty;
    private bool enabled = true;

    public KeyBindButton()
    {
        Size = new Vector2(WIDTH, 30);
        Masking = true;
        CornerRadius = 4;
        BorderThickness = 1.5f;
        BorderColour = Colour4.White.Opacity(0.15f);
        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.55f) },
            text = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = EditorFont.Regular(OptionsStyle.NOTE_SIZE + 1),
                Colour = Colour4.White,
                Truncate = true,
                MaxWidth = WIDTH - 10,
            },
        };
    }

    /// <summary>The displayed text (smoke test).</summary>
    public string DisplayText => text.Text.ToString();

    public void SetCombo(string display, bool empty)
    {
        combo = empty ? string.Empty : display;
        if (!capturing && !pending) showCombo(empty);
    }

    /// <summary>What an empty slot reads — the same words as the Keyboard Shortcuts list (audit U7).</summary>
    public const string UNBOUND = "not set";

    private bool pending;

    private void showCombo(bool empty)
    {
        text.Text = empty ? UNBOUND : combo;
        text.Colour = empty ? OptionsStyle.Muted : Colour4.White;
    }

    /// <summary>
    /// Shows a captured combination that waits for the conflict answer (audit B5: the slot kept the old key while the
    /// question named the new one); null goes back to the bound combination.
    /// </summary>
    public void ShowPending(string? display)
    {
        pending = display != null;
        BorderColour = pending ? OptionsStyle.Warning : Colour4.White.Opacity(0.15f);
        if (display != null)
        {
            text.Text = display;
            text.Colour = OptionsStyle.Warning;
        }
        else if (!capturing)
            showCombo(combo.Length == 0);
    }

    public bool Capturing
    {
        get => capturing;
        set
        {
            capturing = value;
            BorderColour = value ? OptionsStyle.Accent : Colour4.White.Opacity(0.15f);
            background.Colour = value ? OptionsStyle.Accent.Opacity(0.25f) : Colour4.Black.Opacity(0.55f);
            if (value)
            {
                text.Text = "press a key…";
                text.Colour = OptionsStyle.Accent.Lighten(0.4f);
            }
            else
            {
                showCombo(combo.Length == 0);
            }
        }
    }

    public bool Enabled
    {
        get => enabled;
        set
        {
            enabled = value;
            Alpha = value ? 1 : 0.45f;
        }
    }

    protected override bool OnClick(ClickEvent e) => enabled && base.OnClick(e);

    protected override bool OnHover(HoverEvent e)
    {
        if (enabled && !capturing) background.FadeColour(Colour4.White.Opacity(0.12f), 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        if (!capturing) background.FadeColour(Colour4.Black.Opacity(0.55f), 80);
        base.OnHoverLost(e);
    }
}

/// <summary>
/// osu!-style key binding row: action name, two binding buttons, a reset button (shown when the row differs from
/// its defaults). A captured combination that another action of the same scope already uses is not applied at
/// once: the row asks first — Swap, Clear other, or Cancel.
/// </summary>
public partial class KeybindRow<TAction> : OptionsItem, IKeyCaptureTarget
    where TAction : struct, Enum
{
    public const int SLOTS = 2;

    public TAction Action { get; }
    public string Label { get; }

    private readonly BindingTable<TAction> table;
    private readonly KeybindScope scope;
    private readonly Func<TAction, string> labelOf;
    private readonly bool fixedBinding;
    private readonly string[] keywords;

    private readonly KeyBindButton[] buttons = new KeyBindButton[SLOTS];
    private RoundIconButton reset = null!;
    private Container conflictPanel = null!;
    private TextFlowContainer conflictText = null!;
    private SpriteText errorText = null!;

    private int captureSlot = -1;

    /// <summary>A captured combination waiting for the conflict answer: (slot, combo, conflicting actions).</summary>
    public (int Slot, string Combo, IReadOnlyList<TAction> Conflicts)? Pending { get; private set; }

    /// <summary>The last refusal shown under the row ("Tab is fixed in the playtest"), empty when none.</summary>
    public string LastError => errorText.Text.ToString();

    [Resolved]
    private OptionsOverlay overlay { get; set; } = null!;

    public KeybindRow(TAction action, string label, BindingTable<TAction> table, KeybindScope scope, Func<TAction, string> labelOf, bool fixedBinding = false,
                      params string[] keywords)
    {
        Action = action;
        Label = label;
        this.table = table;
        this.scope = scope;
        this.labelOf = labelOf;
        this.fixedBinding = fixedBinding;
        this.keywords = keywords;
        Padding = new MarginPadding { Vertical = 3 };
    }

    public override IEnumerable<string> FilterTerms =>
        keywords.Prepend(Label).Concat(table[Action].Select(scope.Describe)).Append(BindingTable<TAction>.IdOf(Action)).Append(scope.Name);

    public KeyBindButton Button(int slot) => buttons[slot];

    /// <summary>The row's reset-to-default button (smoke test clicks it).</summary>
    public Drawable ResetButton => reset;

    /// <summary>The Swap / Clear other / Cancel buttons of the conflict question.</summary>
    public IReadOnlyList<Drawable> ConflictButtons { get; private set; } = Array.Empty<Drawable>();

    [BackgroundDependencyLoader]
    private void load()
    {
        var label = new TruncatingText
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Text = fixedBinding ? Label + " (not rebindable)" : Label,
            Font = EditorFont.Regular(OptionsStyle.NOTE_SIZE + 2),
            Colour = fixedBinding ? OptionsStyle.Muted : Colour4.White,
            Truncate = true,
            RelativeSizeAxes = Axes.X,
        };

        var right = new FillFlowContainer
        {
            Anchor = Anchor.CentreRight,
            Origin = Anchor.CentreRight,
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
        };

        for (int i = 0; i < SLOTS; i++)
        {
            int slot = i;
            right.Add(buttons[i] = new KeyBindButton { Action = () => overlay.BeginCapture(this, slot), Enabled = !fixedBinding });
        }

        // always present: a hidden reset button must keep its column, or the key boxes jump when it appears (audit B5)
        right.Add(reset = new RoundIconButton(FontAwesome.Solid.UndoAlt, 24) { Action = ResetToDefault, AlwaysPresent = true });

        float rightWidth = SLOTS * (KeyBindButton.WIDTH + 6) + 30;
        label.Width = 1;
        var top = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 32,
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Right = rightWidth + 8 },
                    Child = label,
                },
                right,
            },
        };

        conflictPanel = new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Alpha = 0,
            Masking = true,
            CornerRadius = 4,
            Margin = new MarginPadding { Top = 4 },
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = OptionsStyle.Warning.Opacity(0.15f) },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding(8),
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        conflictText = new TextFlowContainer(t => t.Font = EditorFont.Regular(OptionsStyle.NOTE_SIZE))
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Colour = OptionsStyle.Warning,
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8, 0),
                            Children = ConflictButtons = new Drawable[]
                            {
                                smallButton("Swap", () => ResolveConflict(ConflictResolution.Swap), Colour4.FromHex("3f6fb5")),
                                smallButton("Clear other", () => ResolveConflict(ConflictResolution.ClearOther), Colour4.FromHex("c0407f")),
                                smallButton("Cancel", CancelConflict, Colour4.FromHex("444444")),
                            },
                        },
                    },
                },
            },
        };

        errorText = new SpriteText
        {
            Font = EditorFont.Regular(OptionsStyle.NOTE_SIZE),
            Colour = OptionsStyle.Error,
            Alpha = 0,
        };

        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children = new Drawable[] { top, errorText, conflictPanel },
        };
    }

    private static FlatButton smallButton(string text, Action action, Colour4 colour) => new FlatButton(text, colour, null, OptionsStyle.NOTE_SIZE)
    {
        Size = new Vector2(120, 28),
        Action = action,
    };

    protected override void LoadComplete()
    {
        base.LoadComplete();
        table.Changed += onTableChanged;
        refresh();
    }

    private void onTableChanged() => Schedule(refresh);

    private void refresh()
    {
        var combos = table[Action];
        for (int i = 0; i < SLOTS; i++)
        {
            string shown = i < combos.Count ? scope.Describe(combos[i]) : string.Empty;
            bool empty = shown.Length == 0 || shown == "None"; // an unbound default reads "None" through Describe
            buttons[i].SetCombo(shown, empty);
        }

        reset.Alpha = table.IsDefault(Action) || fixedBinding ? 0 : 1;
    }

    // ---- capture (driven by the overlay) -------------------------------------

    /// <summary>Called by the overlay when a binding button of this row starts waiting.</summary>
    public void OnCaptureStarted(int slot)
    {
        CancelConflict();
        showError(null);
        captureSlot = slot;
        buttons[slot].Capturing = true;
    }

    public bool Capture(CapturedInput input)
    {
        if (captureSlot < 0) return true;

        var (combo, error) = scope.Translate(input);
        if (combo == null && error == null) return false; // a modifier alone: keep waiting

        int slot = captureSlot;
        endCapture();

        if (error != null)
        {
            showError(error);
            return true;
        }

        // the same combination again in the same slot changes nothing
        var current = table[Action];
        if (slot < current.Count && KeyComboText.Same(current[slot], combo))
            return true;

        var conflicts = table.FindConflicts(combo!, Action);
        if (conflicts.Count > 0)
        {
            Pending = (slot, combo!, conflicts);
            buttons[slot].ShowPending(scope.Describe(combo!));
            conflictText.Text = $"{scope.Describe(combo!)} is already bound to {string.Join(", ", conflicts.Select(labelOf))} ({scope.Name}). "
                                + "Swap gives it this slot's old key, Clear other removes it there.";
            conflictPanel.FadeIn(100);
            return true;
        }

        table.Rebind(Action, slot, combo);
        return true;
    }

    public void ClearSlot()
    {
        if (captureSlot < 0) return;

        int slot = captureSlot;
        endCapture();
        table.Rebind(Action, slot, null);
    }

    public void CancelCapture()
    {
        if (captureSlot >= 0) endCapture();
    }

    private void endCapture()
    {
        if (captureSlot >= 0) buttons[captureSlot].Capturing = false;
        captureSlot = -1;
        overlay.EndCapture(this);
    }

    /// <summary>Answers the conflict question of the pending combination.</summary>
    public void ResolveConflict(ConflictResolution resolution)
    {
        if (Pending is not { } p) return;

        Pending = null;
        buttons[p.Slot].ShowPending(null);
        conflictPanel.FadeOut(100);
        table.Rebind(Action, p.Slot, p.Combo, resolution);
    }

    public void CancelConflict()
    {
        if (Pending is { } p) buttons[p.Slot].ShowPending(null);
        Pending = null;
        conflictPanel?.FadeOut(100);
    }

    public void ResetToDefault()
    {
        CancelConflict();
        table.Reset(Action);
    }

    private void showError(string? error)
    {
        errorText.Text = error ?? string.Empty;
        errorText.Alpha = error == null ? 0 : 1;
    }

    protected override void Dispose(bool isDisposing)
    {
        table.Changed -= onTableChanged;
        base.Dispose(isDisposing);
    }
}
