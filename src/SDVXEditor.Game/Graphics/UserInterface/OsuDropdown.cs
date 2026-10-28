using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Graphics.Containers;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// osu! dropdown: "value ⌄" header, list opens in the nearest <see cref="PopupLayer"/>.
/// <see cref="Bordered"/> gives the song-select look (black box, white 1 px border);
/// otherwise it is the editor toolbox look (dark translucent, rounded).
/// </summary>
public partial class OsuDropdown<T> : CompositeDrawable, IHasCurrentValue<T>
    where T : notnull
{
    private readonly BindableWithCurrent<T> current = new BindableWithCurrent<T>();

    public Bindable<T> Current
    {
        get => current.Current;
        set => current.Current = value;
    }

    public IReadOnlyList<T> Items { get; }

    /// <summary>When set, the list offered on open (items that change with the chart, e.g. effect names); else <see cref="Items"/>.</summary>
    public Func<IReadOnlyList<T>>? ItemSource { get; init; }

    public Func<T, string> Formatter { get; init; } = v => v?.ToString() ?? string.Empty;

    public bool Bordered { get; init; }

    public float FontSize { get; init; } = 24;

    [Resolved]
    private PopupLayer popupLayer { get; set; } = null!;

    private SpriteText label = null!;
    private Box hover = null!;
    private bool open;

    public OsuDropdown(IEnumerable<T> items)
    {
        Items = items.ToList();
        Height = 32;

        if (Items.Count > 0)
            current.Value = Items[0];
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Masking = true;
        CornerRadius = Bordered ? 1 : 4; // radius 0 draws no border
        BorderThickness = Bordered ? 1.5f : 0;
        BorderColour = Colour4.White;

        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Bordered ? Colour4.Black : Colour4.Black.Opacity(0.55f) },
            hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 },
            label = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 4,
                Font = EditorFont.Regular(FontSize),
                Colour = Colour4.White,
                Truncate = true,
                RelativeSizeAxes = Axes.X,
                Width = 0.85f,
            },
            new SpriteIcon
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -8,
                Size = new Vector2(FontSize * 0.7f),
                Icon = FontAwesome.Solid.ChevronDown,
                Colour = Colour4.White,
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        current.BindValueChanged(e => label.Text = Formatter(e.NewValue), true);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (open)
        {
            popupLayer.Close();
            return true;
        }

        var list = new PopupMenuList();

        foreach (var item in ItemSource?.Invoke() ?? Items)
        {
            list.Add(new Item(Formatter(item), FontSize * 0.85f, EqualityComparer<T>.Default.Equals(item, current.Value), () =>
            {
                current.Value = item;
                popupLayer.Close();
            }));
        }

        var popup = new Container
        {
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 1, // radius 0 draws no border
            BorderThickness = 1,
            BorderColour = Colour4.White.Opacity(0.6f),
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("111111").Opacity(0.95f) },
                list,
            },
        };

        open = true;
        popupLayer.Open(this, popup, closed: () => open = false);
        return true;
    }

    protected override bool OnHover(HoverEvent e)
    {
        hover.FadeTo(0.1f, 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        hover.FadeOut(80);
        base.OnHoverLost(e);
    }

    private partial class Item : PopupMenuItem
    {
        private readonly Box hover;
        private readonly bool selected;

        public Item(string text, float size, bool selected, Action action)
        {
            this.selected = selected;
            RelativeSizeAxes = Axes.X;
            Height = size + 10;
            Action = action;
            Highlighted = selected;
            Children = new Drawable[]
            {
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.TabActive, Alpha = 0 },
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 8,
                    Text = text,
                    Font = EditorFont.Regular(size),
                    Colour = Colour4.White,
                },
            };
        }

        // highlighted (mouse or keyboard) = full blue; the current value keeps a half-tone otherwise
        protected override void UpdateHighlight(bool highlighted) => hover.FadeTo(highlighted ? 1 : selected ? 0.5f : 0, 60);
    }
}

/// <summary>osu!-style single line text box: dark translucent field, white text, pink caret.</summary>
public partial class OsuTextBox : BasicTextBox
{
    public OsuTextBox()
    {
        Height = 32;
        Masking = true;
        CornerRadius = 4;
        BackgroundUnfocused = Colour4.Black.Opacity(0.5f);
        BackgroundFocused = Colour4.Black.Opacity(0.7f);
        BackgroundCommit = EditorColours.Osu.Pink.Opacity(0.4f);
        BorderThickness = 1;
        BorderColour = Colour4.White.Opacity(0.3f);
    }

    protected override osuTK.Graphics.Color4 SelectionColour => EditorColours.Osu.Pink.Opacity(0.5f);

    protected override void LoadComplete()
    {
        base.LoadComplete();
        // created by the base constructor, before FontSize can be read
        Placeholder.Font = EditorFont.Regular(FontSize);
    }

    protected override SpriteText CreatePlaceholder() => new SpriteText
    {
        Font = EditorFont.Regular(20),
        Colour = Colour4.White.Opacity(0.45f),
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        X = 2,
    };

    protected override Drawable GetDrawableCharacter(char c) => new SpriteText
    {
        Text = c.ToString(),
        Font = EditorFont.Regular(FontSize),
        Colour = Colour4.White,
    };
}
