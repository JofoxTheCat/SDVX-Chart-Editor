using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;

namespace SDVXEditor.Game.Overlays.Options;

/// <summary>Look of the options panel (osu!stable: near-black panel, pink accents, white labels).</summary>
public static class OptionsStyle
{
    public const float LABEL_SIZE = 21;
    public const float NOTE_SIZE = 17;
    public static readonly Colour4 Panel = Colour4.FromHex("161616");
    public static readonly Colour4 Sidebar = Colour4.FromHex("0b0b0b");
    public static readonly Colour4 Accent = EditorColours.Osu.Pink;
    public static readonly Colour4 SubsectionTitle = Colour4.FromHex("ff8fc6");
    public static readonly Colour4 Muted = Colour4.FromHex("9a9a9a");
    public static readonly Colour4 Warning = Colour4.FromHex("ffb347");
    public static readonly Colour4 Error = Colour4.FromHex("ff6b6b");
    public static readonly Colour4 Ok = Colour4.FromHex("9cff2e");
}

/// <summary>
/// One searchable entry of the options (a setting row, a note, a key binding row). The overlay hides entries whose
/// <see cref="FilterTerms"/> (plus the titles of their section and subsection) do not contain every search word.
/// </summary>
public abstract partial class OptionsItem : CompositeDrawable
{
    /// <summary>Label and keywords, lower-case; matched against the search words.</summary>
    public abstract IEnumerable<string> FilterTerms { get; }

    protected OptionsItem()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
    }

    public bool Matches(IReadOnlyList<string> words, string context)
    {
        if (words.Count == 0) return true;

        string haystack = (string.Join(" ", FilterTerms) + " " + context).ToLowerInvariant();
        return words.All(haystack.Contains);
    }
}

/// <summary>A labelled row: label left, control right (osu!stable options row).</summary>
public abstract partial class OptionsRow : OptionsItem
{
    public string Label { get; }
    private readonly string[] keywords;

    protected OptionsRow(string label, params string[] keywords)
    {
        Label = label;
        this.keywords = keywords;
        Padding = new MarginPadding { Vertical = 5 };
    }

    public override IEnumerable<string> FilterTerms => keywords.Prepend(Label);

    protected static SpriteText CreateLabel(string text) => new TruncatingText
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Text = text,
        Font = EditorFont.Regular(OptionsStyle.LABEL_SIZE),
        Colour = Colour4.White,
        Truncate = true,
        RelativeSizeAxes = Axes.X,
    };

    /// <summary>Label in the left 42 %, <paramref name="control"/> filling the rest.</summary>
    protected void SetContent(Drawable control, float height = 34)
    {
        var label = CreateLabel(Label);
        label.Width = 0.4f;
        control.Anchor = Anchor.CentreRight;
        control.Origin = Anchor.CentreRight;
        control.RelativeSizeAxes = Axes.X;
        control.Width = 0.58f;

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = height,
            Children = new[] { label, control },
        };
    }
}

/// <summary>Pink checkbox bound to a setting.</summary>
public partial class OptionsCheckbox : OptionsRow
{
    public PinkCheckbox Checkbox { get; }

    public OptionsCheckbox(string label, Bindable<bool> current, params string[] keywords)
        : base(label, keywords)
    {
        Checkbox = new PinkCheckbox(label, OptionsStyle.LABEL_SIZE);
        Checkbox.Current.BindTo(current);
        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 30,
            Child = Checkbox,
        };
        Checkbox.Anchor = Anchor.CentreLeft;
        Checkbox.Origin = Anchor.CentreLeft;
    }
}

/// <summary>osu!stable slider: pink track and knob, value right of it. Click or drag; scroll steps.</summary>
public partial class OptionsSlider : OptionsRow
{
    private readonly BindableNumber<double> current;
    private readonly Func<double, string> format;
    private Container track = null!;
    private Box fill = null!;
    private Circle knob = null!;
    private SpriteText value = null!;

    public OptionsSlider(string label, BindableNumber<double> current, Func<double, string>? format = null, params string[] keywords)
        : base(label, keywords)
    {
        this.current = current;
        this.format = format ?? (v => v.ToString("0.##", CultureInfo.InvariantCulture));
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var control = new Container
        {
            Height = 30,
            Children = new Drawable[]
            {
                track = new SliderTrack(this)
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = 8, Right = 92 }, // was { Right = 92, Horizontal = 8 }: Horizontal overwrote Right and the value sat on the track (audit U5)
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = 3,
                            Colour = OptionsStyle.Accent.Opacity(0.35f),
                        },
                        fill = new Box
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            RelativeSizeAxes = Axes.X,
                            Height = 3,
                            Colour = OptionsStyle.Accent,
                        },
                        knob = new Circle
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.Centre,
                            RelativePositionAxes = Axes.X,
                            Size = new Vector2(16),
                            Colour = OptionsStyle.Accent,
                        },
                    },
                },
                value = new SpriteText
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Font = EditorFont.Regular(OptionsStyle.NOTE_SIZE + 1),
                    Colour = Colour4.White,
                },
            },
        };
        SetContent(control);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        current.BindValueChanged(_ => updateDisplay(), true);
    }

    private double normalized
    {
        get
        {
            double min = current.MinValue, max = current.MaxValue;
            return max > min ? Math.Clamp((current.Value - min) / (max - min), 0, 1) : 0;
        }
    }

    private void updateDisplay()
    {
        float n = (float)normalized;
        fill.Width = n;
        knob.X = n;
        value.Text = format(current.Value);
    }

    private void setFromScreen(Vector2 screenSpace)
    {
        float local = track.ToLocalSpace(screenSpace).X - track.Padding.Left;
        float width = track.DrawWidth - track.Padding.TotalHorizontal;
        if (width <= 0) return;

        double n = Math.Clamp(local / width, 0, 1);
        double raw = current.MinValue + n * (current.MaxValue - current.MinValue);
        double step = current.Precision > 0 ? current.Precision : 0.01;
        current.Value = Math.Round(raw / step) * step;
    }

    private void stepBy(int direction)
    {
        double step = current.Precision > 0 ? current.Precision : (current.MaxValue - current.MinValue) / 100;
        current.Value += direction * step;
    }

    private partial class SliderTrack : Container
    {
        private readonly OptionsSlider slider;

        public SliderTrack(OptionsSlider slider)
        {
            this.slider = slider;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            slider.setFromScreen(e.ScreenSpaceMousePosition);
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e) => true;

        protected override void OnDrag(DragEvent e) => slider.setFromScreen(e.ScreenSpaceMousePosition);

        protected override bool OnScroll(ScrollEvent e)
        {
            slider.stepBy(SDVXEditor.Game.Input.WheelInput.Direction(e));
            return true;
        }
    }
}

/// <summary>osu! dropdown bound to a setting.</summary>
public partial class OptionsDropdown<T> : OptionsRow
    where T : notnull
{
    public OsuDropdown<T> Dropdown { get; }

    public OptionsDropdown(string label, Bindable<T> current, IEnumerable<T> items, Func<T, string>? format = null, params string[] keywords)
        : base(label, keywords.Concat(items.Select(i => format?.Invoke(i) ?? i.ToString() ?? string.Empty)).ToArray())
    {
        Dropdown = new OsuDropdown<T>(items) { FontSize = OptionsStyle.LABEL_SIZE - 1, Formatter = format ?? (v => v.ToString() ?? string.Empty) };
        Dropdown.Current.BindTo(current);
        SetContent(Dropdown);
    }
}

/// <summary>Text field for a path or a value; the setting is written on Enter or when the field loses focus.</summary>
public partial class OptionsTextBox : OptionsRow
{
    public OsuTextBox TextBox { get; }

    private readonly Bindable<string> current;

    public OptionsTextBox(string label, Bindable<string> current, string placeholder = "", params string[] keywords)
        : base(label, keywords)
    {
        this.current = current;
        TextBox = new OsuTextBox
        {
            PlaceholderText = placeholder,
            FontSize = OptionsStyle.NOTE_SIZE + 1,
            CommitOnFocusLost = true,
        };
        SetContent(TextBox);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // settings can change off the update thread (smoke restore, detector): set the text there
        current.BindValueChanged(e => Schedule(() => TextBox.Text = e.NewValue ?? string.Empty), true);
        TextBox.OnCommit += (_, _) => current.Value = TextBox.Text.Trim();
    }
}

/// <summary>osu! stepper (blue box with arrows) bound to a number.</summary>
public partial class OptionsStepper : OptionsRow
{
    public OsuStepper Stepper { get; }

    public OptionsStepper(string label, BindableNumber<double> current, double step, Func<double, string>? format = null, params string[] keywords)
        : base(label, keywords)
    {
        Stepper = new OsuStepper { Step = step, DragStepPerPixel = step / 4, Height = 32, Subdued = true };
        Stepper.Current = current;
        if (format != null) Stepper.Formatter = format;
        SetContent(Stepper);
    }
}

/// <summary>Full-width flat button (osu!stable options buttons: pink or blue bar).</summary>
public partial class OptionsButton : OptionsItem
{
    private readonly string text;
    private readonly string[] keywords;

    public FlatButton Button { get; }

    public OptionsButton(string text, Action action, Colour4? colour = null, params string[] keywords)
    {
        this.text = text;
        this.keywords = keywords;
        Padding = new MarginPadding { Vertical = 4 };
        InternalChild = Button = new FlatButton(text, colour ?? Colour4.FromHex("c0407f"), null, OptionsStyle.LABEL_SIZE - 2)
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Action = action,
        };
    }

    public override IEnumerable<string> FilterTerms => keywords.Prepend(text);
}

/// <summary>Muted explanatory text (or a warning) under a setting.</summary>
public partial class OptionsNote : OptionsItem
{
    private readonly TextFlowContainer flow;
    private readonly string[] keywords;
    private string text;

    public OptionsNote(string text, Colour4? colour = null, params string[] keywords)
    {
        this.text = text;
        this.keywords = keywords;
        Padding = new MarginPadding { Vertical = 2, Left = 2 };
        InternalChild = flow = new TextFlowContainer(t =>
        {
            t.Font = EditorFont.Regular(OptionsStyle.NOTE_SIZE);
        })
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Colour = colour ?? OptionsStyle.Muted,
            Text = text,
        };
    }

    public string Text
    {
        get => text;
        set
        {
            text = value;
            flow.Text = value;
        }
    }

    public Colour4 TextColour
    {
        set => flow.Colour = value;
    }

    public override IEnumerable<string> FilterTerms => keywords.Prepend(text);
}

/// <summary>Pink capitals over a group of rows ("KEYBOARD", "VOLUME").</summary>
public partial class OptionsSubsection : FillFlowContainer
{
    public string Title { get; }

    public OptionsSection? Section { get; internal set; }

    private readonly FillFlowContainer<OptionsItem> items;
    private readonly Container header;

    public IEnumerable<OptionsItem> Items => items.Children;

    public OptionsSubsection(string title, Drawable? headerExtra = null)
    {
        Title = title;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        Padding = new MarginPadding { Bottom = 18 };

        var headerChildren = new List<Drawable>
        {
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = title.ToUpperInvariant(),
                Font = EditorFont.Bold(19),
                Colour = OptionsStyle.SubsectionTitle,
            },
        };

        if (headerExtra != null)
        {
            headerExtra.Anchor = Anchor.CentreRight;
            headerExtra.Origin = Anchor.CentreRight;
            headerChildren.Add(headerExtra);
        }

        Children = new Drawable[]
        {
            header = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 32,
                Children = headerChildren,
            },
            items = new FillFlowContainer<OptionsItem>
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Left = 6 },
            },
        };
    }

    public OptionsSubsection Add(params OptionsItem[] rows)
    {
        items.AddRange(rows);
        return this;
    }

    /// <summary>Applies the search; returns true when anything stays visible.</summary>
    public bool ApplyFilter(IReadOnlyList<string> words, string sectionTitle)
    {
        bool any = false;
        foreach (var item in items.Children)
        {
            bool show = item.Matches(words, sectionTitle + " " + Title);
            item.Alpha = show ? 1 : 0;
            item.AlwaysPresent = false;
            any |= show;
        }

        Alpha = any ? 1 : 0;
        return any;
    }
}

/// <summary>Top-level section ("General", "Audio" …): icon + big title, then its subsections.</summary>
public partial class OptionsSection : FillFlowContainer
{
    public string Title { get; }
    public IconUsage Icon { get; }

    private readonly FillFlowContainer<OptionsSubsection> subsections;

    public IEnumerable<OptionsSubsection> Subsections => subsections.Children;

    public OptionsSection(string title, IconUsage icon, params OptionsSubsection[] subs)
    {
        Title = title;
        Icon = icon;
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        Padding = new MarginPadding { Top = 18, Bottom = 10, Horizontal = 28 };

        Children = new Drawable[]
        {
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(14, 0),
                Margin = new MarginPadding { Bottom = 10 },
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = icon,
                        Size = new Vector2(30),
                        Colour = Colour4.White,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = title,
                        Font = EditorFont.Regular(40),
                        Colour = Colour4.White,
                    },
                },
            },
            subsections = new FillFlowContainer<OptionsSubsection>
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
            },
        };

        foreach (var s in subs) AddSubsection(s);
    }

    public void AddSubsection(OptionsSubsection sub)
    {
        sub.Section = this;
        subsections.Add(sub);
    }

    public bool ApplyFilter(IReadOnlyList<string> words)
    {
        bool any = false;
        foreach (var sub in subsections.Children)
            any |= sub.ApplyFilter(words, Title);

        Alpha = any ? 1 : 0;
        return any;
    }
}
