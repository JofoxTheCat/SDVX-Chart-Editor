using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>Menu entry with an optional shortcut hint shown right-aligned ("Ctrl+S").</summary>
public class EditorMenuItem : MenuItem
{
    public string Shortcut { get; }

    public EditorMenuItem(string text, string shortcut = "", Action? action = null)
        : base(text, action ?? (() => { }))
    {
        Shortcut = shortcut;
    }

    public EditorMenuItem(string text, IReadOnlyList<MenuItem> items)
        : base(text)
    {
        Shortcut = string.Empty;
        Items = items;
    }
}

/// <summary>Thin separator line between menu groups.</summary>
public class EditorMenuSeparator : EditorMenuItem
{
    public EditorMenuSeparator()
        : base(string.Empty)
    {
    }
}

/// <summary>
/// Light Windows-like menu bar of the editor (reference 02: ~22 px, black text on
/// near-white). Built on osu-framework's <see cref="Menu"/>; only the look is custom.
/// </summary>
public partial class EditorMenuBar : EditorMenu
{
    public const float HEIGHT = 26;

    /// <summary>Menu text size: 12.5 read as ~8 px in the scaled UI, much smaller than everything else (audit U23).</summary>
    public const float FONT_SIZE = 15.5f;

    public EditorMenuBar()
        : base(Direction.Horizontal, true)
    {
        RelativeSizeAxes = Axes.X;
        Height = HEIGHT;
        BackgroundColour = Colour4.FromHex("f5f5f5");
    }

    /// <summary>Opens the top-level menu at <paramref name="index"/> (smoke test / keyboard access).</summary>
    public void OpenMenu(int index)
    {
        if (index < Children.Count)
            Children[index].TriggerClick();
    }
}

public partial class EditorMenu : Menu
{
    private readonly bool topLevel;

    public EditorMenu(Direction direction, bool topLevel = false)
        : base(direction, topLevel)
    {
        this.topLevel = topLevel;
        BackgroundColour = Colour4.FromHex("f2f2f2");

        if (!topLevel)
        {
            MaskingContainer.CornerRadius = 1; // radius 0 draws no border
            MaskingContainer.BorderThickness = 1;
            MaskingContainer.BorderColour = Colour4.FromHex("cccccc");
            MaskingContainer.EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
            {
                Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                Colour = Colour4.Black.Opacity(0.25f),
                Radius = 4,
                Offset = new osuTK.Vector2(2),
            };
        }
    }

    protected override Menu CreateSubMenu() => new EditorMenu(Direction.Vertical);

    protected override DrawableMenuItem CreateDrawableMenuItem(MenuItem item) =>
        topLevel ? new DrawableTopLevelItem(item) : new DrawableEditorMenuItem(item);

    /// <summary>
    /// Menu bar entry. A separate class rather than a flag: <see cref="DrawableMenuItem"/>
    /// calls CreateContent from its constructor, before any field of ours is assigned.
    /// </summary>
    private partial class DrawableTopLevelItem : DrawableEditorMenuItem
    {
        public DrawableTopLevelItem(MenuItem item)
            : base(item)
        {
            BackgroundColourHover = Colour4.FromHex("cce8ff");
        }

        protected override Drawable CreateContent()
        {
            var label = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Font = EditorFont.Regular(EditorMenuBar.FONT_SIZE),
                Colour = Colour4.Black,
            };
            Item.Text.BindValueChanged(e => label.Text = e.NewValue, true);

            return new Container
            {
                AutoSizeAxes = Axes.X,
                Height = EditorMenuBar.HEIGHT,
                Padding = new MarginPadding { Horizontal = 8 },
                Child = label,
            };
        }
    }

    protected override ScrollContainer<Drawable> CreateScrollContainer(Direction direction) => new BasicScrollContainer(direction);

    private partial class DrawableEditorMenuItem : DrawableMenuItem
    {
        public DrawableEditorMenuItem(MenuItem item)
            : base(item)
        {
            BackgroundColour = Colour4.Transparent;
            BackgroundColourHover = Colour4.FromHex("91c9f7");
            ForegroundColour = Colour4.Black;
            ForegroundColourHover = Colour4.Black;

            if (item is EditorMenuSeparator)
                BackgroundColourHover = Colour4.Transparent;
        }

        protected override Drawable CreateContent()
        {
            if (Item is EditorMenuSeparator)
            {
                return new Container
                {
                    Width = 200,
                    Height = 7,
                    Child = new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.X,
                        Width = 0.94f,
                        Height = 1,
                        Colour = Colour4.FromHex("d7d7d7"),
                    },
                };
            }

            var label = new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Font = EditorFont.Regular(EditorMenuBar.FONT_SIZE),
                Colour = Colour4.Black,
            };
            Item.Text.BindValueChanged(e => label.Text = e.NewValue, true);

            string shortcut = (Item as EditorMenuItem)?.Shortcut ?? string.Empty;
            bool hasSub = Item.Items.Count > 0;

            return new Container
            {
                AutoSizeAxes = Axes.X,
                Height = 26,
                Padding = new MarginPadding { Left = 28, Right = 14 },
                Children = new Drawable[]
                {
                    label,
                    new Container
                    {
                        // fixed label column so shortcuts line up
                        Width = 300,
                        RelativeSizeAxes = Axes.Y,
                        Child = new SpriteText
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Text = hasSub ? "›" : shortcut,
                            Font = EditorFont.Regular(EditorMenuBar.FONT_SIZE),
                            Colour = Colour4.FromHex("6d6d6d"),
                        },
                    },
                },
            };
        }
    }
}
