using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osuTK;

namespace SDVXEditor.Game.Graphics.UserInterface.Dialog;

/// <summary>Factory helpers for plain dialog text.</summary>
public static class DialogText
{
    public static SpriteText Label(string text, bool bold = false, float size = 12.5f) => new SpriteText
    {
        Text = text,
        Font = EditorFont.Dialog(size, bold),
        Colour = EditorColours.Dialog.Text,
    };

    /// <summary>Number formatting of the dialogs: invariant, decimal dot (one format in the whole UI, Core.Editing.UiFormat).</summary>
    public static string Format(double value, int decimals) =>
        value.ToString("F" + decimals, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses user input that may use either decimal comma or dot ("207,5", "207.5").
    /// The last ',' or '.' is the decimal separator; any earlier one is a group separator.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string s = text.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty);
        int last = Math.Max(s.LastIndexOf(','), s.LastIndexOf('.'));

        if (last >= 0)
        {
            string whole = s[..last].Replace(",", string.Empty).Replace(".", string.Empty);
            s = whole + "." + s[(last + 1)..];
        }

        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>Push button. <see cref="IsDefault"/> draws the dark default/OK frame.</summary>
public partial class DialogButton : ClickableContainer
{
    private readonly string text;
    public bool IsDefault { get; init; }

    private Box background = null!;
    private bool pressed;

    public DialogButton(string text, Action? action = null)
    {
        this.text = text;
        Action = action;
        Height = 23;
        Masking = true;
        CornerRadius = 4;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        BorderThickness = IsDefault ? 2 : 1;
        BorderColour = IsDefault ? Colour4.FromHex("1e1e1e") : Colour4.FromHex("c4c4c4");

        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("fdfdfd") },
            new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = text,
                Font = EditorFont.Dialog(),
                Colour = EditorColours.Dialog.Text,
            },
        };
    }

    private void updateState() =>
        background.FadeColour(pressed ? EditorColours.Dialog.ButtonPressed : IsHovered ? EditorColours.Dialog.ButtonHover : Colour4.FromHex("fdfdfd"), 60);

    protected override bool OnHover(HoverEvent e)
    {
        updateState();
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => updateState();

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        pressed = true;
        updateState();
        return true;
    }

    protected override void OnMouseUp(MouseUpEvent e)
    {
        pressed = false;
        updateState();
    }
}

/// <summary>Single line text field: white, 1 px grey border, blue border when focused.</summary>
public partial class DialogTextBox : BasicTextBox
{
    public DialogTextBox()
    {
        Height = 23;
        FontSize = 12.5f;
        LengthLimit = 512;
        BackgroundUnfocused = EditorColours.Dialog.Window;
        BackgroundFocused = EditorColours.Dialog.Window;
        BackgroundCommit = EditorColours.Dialog.Window;
        Masking = true;
        CornerRadius = 2;
        BorderThickness = 1;
        BorderColour = EditorColours.Dialog.ControlBorder;
    }

    protected override float LeftRightPadding => 4;

    protected override osuTK.Graphics.Color4 SelectionColour => EditorColours.Dialog.TextSelection.Opacity(0.35f);

    protected override osuTK.Graphics.Color4 InputErrorColour => EditorColours.Dialog.NoteRed.Opacity(0.3f);

    private float borderThickness = 1;

    /// <summary>Frame width; 0 for a box embedded in another control (editable combo box).</summary>
    public float FrameThickness
    {
        get => borderThickness;
        set
        {
            borderThickness = value;
            BorderThickness = value;
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        BorderThickness = borderThickness;
        BorderColour = HasFocus ? EditorColours.Dialog.FocusBorder : EditorColours.Dialog.ControlBorder;
    }

    protected override void OnFocus(FocusEvent e)
    {
        base.OnFocus(e);
        BorderColour = EditorColours.Dialog.FocusBorder;
    }

    protected override void OnFocusLost(FocusLostEvent e)
    {
        base.OnFocusLost(e);
        BorderColour = EditorColours.Dialog.ControlBorder;
    }

    protected override SpriteText CreatePlaceholder() => new SpriteText
    {
        Font = EditorFont.Dialog(),
        Colour = EditorColours.Dialog.TextDisabled,
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
    };

    protected override Drawable GetDrawableCharacter(char c) => new SpriteText
    {
        Text = c.ToString(),
        Font = EditorFont.Dialog(),
        Colour = EditorColours.Dialog.Text,
    };

    protected override Caret CreateCaret() => new DialogCaret();

    private partial class DialogCaret : Caret
    {
        private const float caret_width = 1;

        public DialogCaret()
        {
            RelativeSizeAxes = Axes.Y;
            Size = new Vector2(caret_width, 0.75f);
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            Colour = Colour4.Transparent;
            InternalChild = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White };
        }

        public override void Hide() => this.FadeOut(100);

        public override void DisplayAt(Vector2 position, float? selectionWidth)
        {
            ClearTransforms();
            Position = new Vector2(position.X, position.Y);

            if (selectionWidth != null)
            {
                Alpha = 1;
                Width = selectionWidth.Value + caret_width;
                Colour = EditorColours.Dialog.TextSelection.Opacity(0.35f);
            }
            else
            {
                Width = caret_width;
                Colour = EditorColours.Dialog.Text;
                this.FadeIn().Then().Delay(530).FadeOut().Then().Delay(530).Loop();
            }
        }
    }
}

/// <summary>
/// Numeric up/down: text field with ▲▼ spin buttons. Accepts decimal comma and dot,
/// clamps to the bindable's range, Enter / focus loss commits. Like the Windows control:
/// a spin button steps on press and repeats while held, the wheel steps over the whole
/// control (Shift+wheel = ten steps), and Up/Down (PageUp/PageDown = ten steps) step while
/// the field has focus.
/// </summary>
public partial class NumericUpDown : CompositeDrawable
{
    /// <summary>Hold time before a held spin button starts repeating, and the repeat interval (ms).</summary>
    public const double REPEAT_DELAY = 400, REPEAT_INTERVAL = 60;

    private readonly BindableNumber<double> current = new BindableDouble();

    public BindableNumber<double> Current
    {
        get => current;
        set
        {
            current.UnbindBindings();
            current.BindTo(value);
        }
    }

    public int Decimals { get; init; }

    public double Increment { get; init; } = 1;

    /// <summary>Optional display format (default: <see cref="Decimals"/> fixed decimals in the current culture).</summary>
    public Func<double, string>? Formatter { get; init; }

    private SpinTextBox textBox = null!;

    public NumericUpDown()
    {
        Height = 23;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            textBox = new SpinTextBox(this)
            {
                RelativeSizeAxes = Axes.X,
                CommitOnFocusLost = true,
            },
            new FillFlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -1,
                Width = 16,
                Height = 21,
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    new SpinButton(FontAwesome.Solid.CaretUp, () => step(1)),
                    new SpinButton(FontAwesome.Solid.CaretDown, () => step(-1)),
                },
            },
        };

        textBox.OnCommit += (_, _) => commitText();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        current.BindValueChanged(_ => updateText(), true);
    }

    /// <summary>Sets the text as if the user had typed it and committed (used by tests and tools).</summary>
    public void SetText(string text)
    {
        textBox.Text = text;
        commitText();
    }

    private void commitText()
    {
        if (DialogText.TryParse(textBox.Text, out double v))
            current.Value = v;

        updateText();
    }

    private void updateText() => textBox.Text = Formatter?.Invoke(current.Value) ?? DialogText.Format(current.Value, Decimals);

    private void step(int direction)
    {
        commitText();
        current.Value += direction * Increment;
    }

    /// <summary>The wheel steps once per notch, ten steps with Shift (like PageUp / PageDown in the field).</summary>
    protected override bool OnScroll(ScrollEvent e)
    {
        int direction = SDVXEditor.Game.Input.WheelInput.Direction(e);
        if (direction != 0)
            step(direction * (e.ShiftPressed ? 10 : 1));
        return true;
    }

    /// <summary>The field: Up/Down step instead of reaching the dialog (which swallowed them, so the keys did nothing).</summary>
    private partial class SpinTextBox : DialogTextBox
    {
        private readonly NumericUpDown owner;

        public SpinTextBox(NumericUpDown owner)
        {
            this.owner = owner;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (!ReadOnly && !e.ControlPressed && !e.AltPressed)
            {
                switch (e.Key)
                {
                    case osuTK.Input.Key.Up:
                        owner.step(1);
                        return true;

                    case osuTK.Input.Key.Down:
                        owner.step(-1);
                        return true;

                    case osuTK.Input.Key.PageUp:
                        owner.step(10);
                        return true;

                    case osuTK.Input.Key.PageDown:
                        owner.step(-10);
                        return true;
                }
            }

            return base.OnKeyDown(e);
        }
    }

    /// <summary>
    /// ▲ / ▼: steps on press and repeats while held (Windows spin buttons). Handling the press also keeps it from the
    /// text box behind, whose drag selection could otherwise swallow the click.
    /// </summary>
    private partial class SpinButton : CompositeDrawable
    {
        private readonly Action action;
        private readonly Box hover;
        private ScheduledDelegate? repeat;

        public SpinButton(IconUsage icon, Action action)
        {
            this.action = action;
            RelativeSizeAxes = Axes.Both;
            Height = 0.5f;
            InternalChildren = new Drawable[]
            {
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.ButtonHover, Alpha = 0 },
                new Box { RelativeSizeAxes = Axes.Y, Width = 1, Colour = EditorColours.Dialog.GroupBorder },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(7),
                    Icon = icon,
                    Colour = EditorColours.Dialog.Glyph,
                },
            };
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button != osuTK.Input.MouseButton.Left)
                return false;

            action();
            hover.Colour = EditorColours.Dialog.ButtonPressed;
            repeat?.Cancel();
            repeat = Scheduler.AddDelayed(() =>
            {
                repeat = Scheduler.AddDelayed(() =>
                {
                    if (IsHovered) action();
                }, REPEAT_INTERVAL, true);
            }, REPEAT_DELAY);
            return true;
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            stopRepeat();
            base.OnMouseUp(e);
        }

        // the press already stepped
        protected override bool OnClick(ClickEvent e) => true;

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeIn(60);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => hover.FadeOut(60);

        private void stopRepeat()
        {
            repeat?.Cancel();
            repeat = null;
            hover.Colour = EditorColours.Dialog.ButtonHover;
        }

        protected override void Dispose(bool isDisposing)
        {
            repeat?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}

/// <summary>Classic 13 px check box with a label.</summary>
public partial class DialogCheckBox : ClickableContainer
{
    public readonly BindableBool Current = new BindableBool();

    private readonly string label;
    private SpriteIcon check = null!;
    private Container box = null!;

    public DialogCheckBox(string label)
    {
        this.label = label;
        AutoSizeAxes = Axes.Both;
        Action = () => Current.Toggle();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Child = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                box = new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(13),
                    Masking = true,
                    CornerRadius = 2,
                    BorderThickness = 1,
                    BorderColour = EditorColours.Dialog.Glyph,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window },
                        check = new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(9),
                            Icon = FontAwesome.Solid.Check,
                            Colour = EditorColours.Dialog.Glyph,
                        },
                    },
                },
                DialogText.Label(label).With(t =>
                {
                    t.Anchor = Anchor.CentreLeft;
                    t.Origin = Anchor.CentreLeft;
                }),
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Current.BindValueChanged(e => check.Alpha = e.NewValue ? 1 : 0, true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        box.BorderColour = EditorColours.Dialog.FocusBorder;
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => box.BorderColour = EditorColours.Dialog.Glyph;
}

/// <summary>A radio button bound to a shared value: selected when <c>Current.Value == value</c>.</summary>
public partial class DialogRadioButton<T> : ClickableContainer
    where T : notnull
{
    private readonly Bindable<T> current = new Bindable<T>();
    private readonly T value;
    private readonly string label;

    private Circle ring = null!;
    private Circle outer = null!;
    private Circle inner = null!;

    public DialogRadioButton(Bindable<T> group, T value, string label)
    {
        current.BindTo(group);
        this.value = value;
        this.label = label;
        AutoSizeAxes = Axes.Both;
        Action = () => current.Value = this.value;
    }

    /// <summary>True while the shared value equals this button's value.</summary>
    public bool Selected => EqualityComparer<T>.Default.Equals(current.Value, value);

    [BackgroundDependencyLoader]
    private void load()
    {
        Child = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(14),
                    Children = new Drawable[]
                    {
                        ring = new Circle { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Glyph },
                        outer = new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(12),
                            Colour = EditorColours.Dialog.Window,
                        },
                        inner = new Circle
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Size = new Vector2(6),
                            Colour = EditorColours.Dialog.Window,
                        },
                    },
                },
                DialogText.Label(label).With(t =>
                {
                    t.Anchor = Anchor.CentreLeft;
                    t.Origin = Anchor.CentreLeft;
                }),
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        current.BindValueChanged(e =>
        {
            bool selected = EqualityComparer<T>.Default.Equals(e.NewValue, value);
            outer.Colour = selected ? EditorColours.Dialog.RadioDot : EditorColours.Dialog.Window;
            ring.Colour = selected ? EditorColours.Dialog.RadioDot : EditorColours.Dialog.Glyph;
            inner.Alpha = selected ? 1 : 0;
        }, true);
    }
}

/// <summary>Frame with a caption breaking the top line ("General", "Time Signature (metronome)").</summary>
public partial class GroupBox : Container
{
    private readonly string caption;
    private readonly Colour4 background;
    private readonly Container content;

    protected override Container<Drawable> Content => content;

    /// <param name="background">Colour behind the caption; must match what the box sits on.</param>
    public GroupBox(string caption, Colour4? background = null)
    {
        this.caption = caption;
        this.background = background ?? EditorColours.Dialog.Window;

        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = 7 },
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 3,
                    BorderThickness = 1,
                    BorderColour = EditorColours.Dialog.GroupBorder,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
            },
            new Container
            {
                AutoSizeAxes = Axes.Both,
                X = 6,
                Padding = new MarginPadding { Horizontal = 2 },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = this.background },
                    DialogText.Label(caption),
                },
            },
            content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = 20, Left = 9, Right = 9, Bottom = 6 },
            },
        };
    }
}

/// <summary>Small square button showing only a coloured glyph (F6 green + / red ✕).</summary>
public partial class DialogIconButton : ClickableContainer
{
    private readonly Box background;

    public DialogIconButton(IconUsage icon, Colour4 colour, Action? action = null)
    {
        Action = action;
        Size = new Vector2(24);
        Masking = true;
        CornerRadius = 3;
        BorderThickness = 1;
        BorderColour = Colour4.FromHex("c4c4c4");
        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("fdfdfd") },
            new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(14),
                Icon = icon,
                Colour = colour,
                Shadow = true,
                ShadowColour = Colour4.Black.Opacity(0.25f),
                ShadowOffset = new Vector2(0, 0.06f),
            },
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        background.FadeColour(EditorColours.Dialog.ButtonHover, 60);
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => background.FadeColour(Colour4.FromHex("fdfdfd"), 60);
}

/// <summary>Blue underlined link text.</summary>
public partial class DialogLink : ClickableContainer
{
    public DialogLink(string text, Action? action = null)
    {
        AutoSizeAxes = Axes.Both;
        Action = action;
        SpriteText label;
        Children = new Drawable[]
        {
            label = new SpriteText { Text = text, Font = EditorFont.Dialog(), Colour = EditorColours.Dialog.Link },
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Colour = EditorColours.Dialog.Link,
            },
        };
    }
}
