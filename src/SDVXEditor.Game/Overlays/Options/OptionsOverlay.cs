using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Input;

namespace SDVXEditor.Game.Overlays.Options;

/// <summary>
/// The options (osu!stable style): a panel that slides in from the left with a section sidebar, a title, a search
/// box that filters every row, and the sections General, Graphics, Audio, Input, Playtest and Export
/// (<see cref="OptionsOverlay"/>.Sections.cs). Every value is written the moment it changes (editor.ini /
/// framework.ini). Opened with Ctrl+O (outside the editor), the song select "Options" button and the editor's
/// File → Options… (Ctrl+Shift+O); Esc or a click beside the panel closes it.
/// <para>
/// While open the overlay takes the keyboard: the screens below get no keys. A key binding button that waits for
/// input receives the next key / mouse button / wheel notch / controller button through
/// <see cref="BeginCapture"/>; Esc cancels, Delete/Backspace clears the slot.
/// </para>
/// </summary>
public partial class OptionsOverlay : CompositeDrawable
{
    public const float SIDEBAR_WIDTH = 76;
    public const float CONTENT_WIDTH = 640;
    public const float PANEL_WIDTH = SIDEBAR_WIDTH + CONTENT_WIDTH;

    public readonly BindableBool IsOpen = new BindableBool();

    /// <summary>The search text (bound to the search box).</summary>
    public readonly Bindable<string> Search = new Bindable<string>(string.Empty);

    private Container panel = null!;
    private Box dim = null!;
    private BasicScrollContainer scroll = null!;
    private FillFlowContainer<OptionsSection> sectionFlow = null!;
    private FillFlowContainer sidebarFlow = null!;
    private SpriteText noResults = null!;

    public OsuTextBox SearchBox { get; private set; } = null!;

    public IReadOnlyList<OptionsSection> Sections => sectionFlow.Children;

    private readonly Dictionary<string, Drawable> sidebarButtons = new Dictionary<string, Drawable>();

    /// <summary>The sidebar icon that jumps to a section ("Export").</summary>
    public Drawable SidebarButtonFor(string section) => sidebarButtons[section];

    /// <summary>The row waiting for input, or null.</summary>
    public IKeyCaptureTarget? CaptureTarget { get; private set; }

    public OptionsOverlay()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            dim = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black, Alpha = 0 },
            panel = new PanelContainer
            {
                RelativeSizeAxes = Axes.Y,
                Width = PANEL_WIDTH,
                X = -PANEL_WIDTH,
                Masking = true,
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = Colour4.Black.Opacity(0.5f),
                    Radius = 20,
                },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = OptionsStyle.Panel },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = SIDEBAR_WIDTH,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = OptionsStyle.Sidebar },
                            sidebarFlow = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Padding = new MarginPadding { Top = 170 },
                                Spacing = new Vector2(0, 6),
                            },
                        },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = SIDEBAR_WIDTH },
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Padding = new MarginPadding { Horizontal = 28, Top = 26 },
                                Spacing = new Vector2(0, 4),
                                Children = new Drawable[]
                                {
                                    new SpriteText { Text = "options", Font = EditorFont.Regular(58), Colour = Colour4.White },
                                    new SpriteText { Text = "Change the way SDVX Chart Editor behaves", Font = EditorFont.Regular(20), Colour = OptionsStyle.Accent },
                                    SearchBox = new SearchTextBox(() => Close())
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 38,
                                        FontSize = 22,
                                        PlaceholderText = "type to search",
                                        Margin = new MarginPadding { Top = 12 },
                                    },
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Top = 176 },
                                Children = new Drawable[]
                                {
                                    scroll = new BasicScrollContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        ScrollbarVisible = true,
                                        Child = sectionFlow = new FillFlowContainer<OptionsSection>
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Padding = new MarginPadding { Bottom = 200 },
                                        },
                                    },
                                    noResults = new SpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Y = 60,
                                        Text = "No matching options",
                                        Font = EditorFont.Regular(24),
                                        Colour = OptionsStyle.Muted,
                                        Alpha = 0,
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };

        foreach (var section in createSections()) // OptionsOverlay.Sections.cs
        {
            sectionFlow.Add(section);
            var s = section;
            var button = new SidebarButton(section.Title, section.Icon, () => ScrollTo(s));
            sidebarFlow.Add(button);
            sidebarButtons[section.Title] = button;
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        SearchBox.Current.BindTo(Search);
        Search.BindValueChanged(e => applyFilter(e.NewValue), true);

        IsOpen.BindValueChanged(e =>
        {
            if (e.NewValue)
            {
                this.FadeIn(150);
                dim.FadeTo(0.4f, 200);
                panel.MoveToX(0, 350, Easing.OutQuint);
                Schedule(() => GetContainingFocusManager()?.ChangeFocus(SearchBox));
                onOpened();
            }
            else
            {
                CaptureTarget?.CancelCapture();
                dim.FadeOut(200);
                panel.MoveToX(-PANEL_WIDTH, 300, Easing.OutQuint);
                this.Delay(300).FadeOut();
                GetContainingFocusManager()?.ChangeFocus(null);
            }
        });
    }

    public void Open() => IsOpen.Value = true;

    /// <summary>Open and the slide-in finished (smoke test waits for it before clicking).</summary>
    public bool IsSettled => IsOpen.Value && Math.Abs(panel.X) < 0.5f && Alpha > 0.99f;

    public void Close() => IsOpen.Value = false;

    public void Toggle() => IsOpen.Toggle();

    public override bool PropagatePositionalInputSubTree => IsOpen.Value;

    public override bool PropagateNonPositionalInputSubTree => IsOpen.Value;

    public override bool AcceptsFocus => IsOpen.Value;

    /// <summary>Scrolls a section to the top of the list.</summary>
    public void ScrollTo(OptionsSection section)
    {
        if (Search.Value.Length > 0) Search.Value = string.Empty;
        scroll.ScrollTo(section);
    }

    private void applyFilter(string text)
    {
        var words = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool any = false;
        foreach (var section in sectionFlow.Children)
            any |= section.ApplyFilter(words);

        noResults.Alpha = any ? 0 : 1;
        scroll.ScrollToStart(false);
    }

    /// <summary>The visible rows (smoke test).</summary>
    public IEnumerable<OptionsItem> VisibleItems =>
        sectionFlow.Children.Where(s => s.Alpha > 0).SelectMany(s => s.Subsections).Where(s => s.Alpha > 0).SelectMany(s => s.Items).Where(i => i.Alpha > 0);

    public IEnumerable<OptionsItem> AllItems => sectionFlow.Children.SelectMany(s => s.Subsections).SelectMany(s => s.Items);

    // ---- key capture ---------------------------------------------------------

    /// <summary>A binding button was clicked: its row waits for the next input.</summary>
    public void BeginCapture<TAction>(KeybindRow<TAction> row, int slot)
        where TAction : struct, Enum
    {
        CaptureTarget?.CancelCapture();
        CaptureTarget = row;
        row.OnCaptureStarted(slot);
        // keys must reach the overlay, not the search box
        GetContainingFocusManager()?.ChangeFocus(this);
    }

    /// <summary>Called by the row when it stopped waiting.</summary>
    public void EndCapture(IKeyCaptureTarget row)
    {
        if (CaptureTarget == row) CaptureTarget = null;
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (CaptureTarget is { } target)
        {
            if (e.Repeat) return true;

            bool plain = !e.ControlPressed && !e.ShiftPressed && !e.AltPressed && !e.SuperPressed;
            if (e.Key == Key.Escape && plain) target.CancelCapture();
            else if (e.Key is Key.Delete or Key.BackSpace && plain) target.ClearSlot();
            else target.Capture(CapturedInput.FromKey(e));
            return true;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            return true;
        }

        if (KeyboardLayoutService.Logical(e.Key) == Key.O && e.ControlPressed && !e.ShiftPressed && !e.AltPressed)
        {
            Close();
            return true;
        }

        // the screens below get no keys while the options are open
        return true;
    }

    protected override bool OnJoystickPress(JoystickPressEvent e)
    {
        if (CaptureTarget is not { } target) return base.OnJoystickPress(e);

        target.Capture(new CapturedInput(CapturedKind.Joystick, JoystickButton: (int)e.Button - (int)osu.Framework.Input.JoystickButton.FirstButton));
        return true;
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (CaptureTarget is { } target)
        {
            var key = e.Button switch
            {
                MouseButton.Middle => InputKey.MouseMiddle,
                MouseButton.Button1 => InputKey.ExtraMouseButton1,
                MouseButton.Button2 => InputKey.ExtraMouseButton2,
                _ => InputKey.None,
            };

            if (key == InputKey.None) target.CancelCapture(); // left/right click: stop waiting
            else target.Capture(mouseInput(CapturedKind.MouseButton, key, e));
            return true;
        }

        // a click beside the panel closes the options
        if (!panel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
        {
            Close();
            return true;
        }

        return base.OnMouseDown(e);
    }

    protected override bool OnScroll(ScrollEvent e)
    {
        // Shift+wheel arrives horizontally (WheelInput); it is still the wheel the user turned and is stored as such
        if (CaptureTarget is { } target && WheelInput.Direction(e) != 0)
        {
            target.Capture(mouseInput(CapturedKind.Wheel, WheelInput.Direction(e) > 0 ? InputKey.MouseWheelUp : InputKey.MouseWheelDown, e));
            return true;
        }

        return true; // never scroll the screen below
    }

    private static CapturedInput mouseInput(CapturedKind kind, InputKey key, UIEvent e) =>
        new CapturedInput(kind, InputKey: key, Ctrl: e.ControlPressed, Shift: e.ShiftPressed, Alt: e.AltPressed, Super: e.SuperPressed);

    protected override bool OnHover(HoverEvent e) => true;

    protected override bool OnClick(ClickEvent e) => true;

    /// <summary>The panel takes every click inside it (rows, empty space) so nothing falls through.</summary>
    private partial class PanelContainer : Container
    {
        protected override bool OnMouseDown(MouseDownEvent e) => e.Button == MouseButton.Left || base.OnMouseDown(e);

        protected override bool OnClick(ClickEvent e) => true;
    }

    /// <summary>The search box: Esc closes the options right away (a plain text box would only drop its focus).</summary>
    private partial class SearchTextBox : OsuTextBox
    {
        private readonly Action escape;

        public SearchTextBox(Action escape)
        {
            this.escape = escape;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                escape();
                return true;
            }

            return base.OnKeyDown(e);
        }
    }

    private partial class SidebarButton : ClickableContainer
    {
        private readonly SpriteIcon icon;

        public SidebarButton(string title, IconUsage iconUsage, Action action)
        {
            RelativeSizeAxes = Axes.X;
            Height = 56;
            Action = action;
            Child = icon = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(26),
                Icon = iconUsage,
                Colour = OptionsStyle.Muted,
            };
            TooltipText = title;
        }

        public string TooltipText { get; }

        protected override bool OnHover(HoverEvent e)
        {
            icon.FadeColour(Colour4.White, 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            icon.FadeColour(OptionsStyle.Muted, 80);
            base.OnHoverLost(e);
        }
    }
}
