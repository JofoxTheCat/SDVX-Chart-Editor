using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Graphics.Containers;

namespace SDVXEditor.Game.Graphics.UserInterface.Dialog;

/// <summary>
/// Windows combo box. Drop-down list style by default; <see cref="Editable"/> turns the
/// face into a text box whose text is the value (Song Setup "Difficulty" is editable).
/// <see cref="Text"/> always holds the shown text; <see cref="Current"/> the picked item.
/// </summary>
public partial class ComboBox<T> : CompositeDrawable
    where T : notnull
{
    public readonly Bindable<T> Current = new Bindable<T>();

    /// <summary>The visible text: the formatted item, or free text when editable.</summary>
    public readonly Bindable<string> Text = new Bindable<string>(string.Empty);

    public IReadOnlyList<T> Items { get; }

    public Func<T, string> Formatter { get; init; } = v => v?.ToString() ?? string.Empty;

    public bool Editable { get; init; }

    [Resolved]
    private PopupLayer popupLayer { get; set; } = null!;

    private Box hover = null!;
    private bool open;

    public ComboBox(IEnumerable<T> items)
    {
        Items = items.ToList();
        Height = 23;

        if (Items.Count > 0)
            Current.Value = Items[0];
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = 2;
        BorderThickness = 1;
        BorderColour = EditorColours.Dialog.ControlBorder;

        Drawable face;

        if (Editable)
        {
            var textBox = new DialogTextBox
            {
                RelativeSizeAxes = Axes.X,
                Width = 0.999f,
                Height = 21,
                FrameThickness = 0,
                Margin = new MarginPadding(1),
                Padding = new MarginPadding { Right = 20 },
            };
            textBox.Current.BindTo(Text);
            face = textBox;
        }
        else
        {
            var label = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 4,
                Font = EditorFont.Dialog(),
                Colour = EditorColours.Dialog.Text,
                Truncate = true,
                RelativeSizeAxes = Axes.X,
                Width = 0.8f,
            };
            label.Current.BindTo(Text);
            face = label;
        }

        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window },
            hover = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.ButtonHover, Alpha = 0 },
            face,
            new DropButton(togglePopup)
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                RelativeSizeAxes = Axes.Y,
                Width = 20,
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // an editable box keeps the text it was bound to; a list box shows the picked item
        Current.BindValueChanged(e => Text.Value = Formatter(e.NewValue), !Editable);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (!Editable)
            togglePopup();

        return true;
    }

    private void togglePopup()
    {
        if (open)
        {
            popupLayer.Close();
            return;
        }

        var list = new PopupMenuList { Padding = new MarginPadding(1) };

        foreach (var item in Items)
        {
            list.Add(new Item(Formatter(item), EqualityComparer<T>.Default.Equals(item, Current.Value), () =>
            {
                Current.Value = item;
                Text.Value = Formatter(item);
                popupLayer.Close();
            }));
        }

        var popup = new Container
        {
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 1, // radius 0 draws no border
            BorderThickness = 1,
            BorderColour = EditorColours.Dialog.ListBorder,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window },
                list,
            },
        };

        open = true;
        popupLayer.Open(this, popup, closed: () => open = false);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (!Editable) hover.FadeIn(60);
        BorderColour = EditorColours.Dialog.ControlBorderHover;
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        hover.FadeOut(60);
        BorderColour = EditorColours.Dialog.ControlBorder;
    }

    private partial class DropButton : ClickableContainer
    {
        public DropButton(Action action)
        {
            Action = action;
            Child = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(9),
                Icon = FontAwesome.Solid.ChevronDown,
                Colour = EditorColours.Dialog.Glyph,
            };
        }
    }

    private partial class Item : PopupMenuItem
    {
        private readonly Box highlight;
        private readonly SpriteText text;

        public Item(string label, bool selected, Action action)
        {
            RelativeSizeAxes = Axes.X;
            Height = 19;
            Action = action;
            Highlighted = selected;
            Children = new Drawable[]
            {
                highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.TextSelection, Alpha = 0 },
                text = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 4,
                    Text = label,
                    Font = EditorFont.Dialog(),
                    Colour = EditorColours.Dialog.Text,
                },
            };
        }

        protected override void UpdateHighlight(bool highlighted)
        {
            highlight.Alpha = highlighted ? 1 : 0;
            text.Colour = highlighted ? Colour4.White : EditorColours.Dialog.Text;
        }
    }
}
