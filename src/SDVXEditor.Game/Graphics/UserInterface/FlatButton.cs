using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// Flat coloured button with centred text, optionally with a vertical gradient and an icon
/// (timing tab: orange Reset, light-blue "Tap here!", yellow "Timing Setup Panel").
/// Size is set by the owner.
/// </summary>
public partial class FlatButton : ClickableContainer, IHasTooltip
{
    /// <summary>Hover text (empty = none).</summary>
    public LocalisableString TooltipText { get; set; }

    private readonly string text;
    private readonly Colour4 top;
    private readonly Colour4 bottom;
    private readonly float fontSize;

    public IconUsage? Icon { get; init; }
    public Colour4 TextColour { get; init; } = Colour4.White;
    public Colour4? BorderTint { get; init; }

    private Box hover = null!;
    private Box flash = null!;

    public FlatButton(string text, Colour4 colour, Colour4? bottomColour = null, float fontSize = 24)
    {
        this.text = text;
        top = colour;
        bottom = bottomColour ?? colour;
        this.fontSize = fontSize;
        Masking = true;
        CornerRadius = 4;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        if (BorderTint is Colour4 border)
        {
            BorderThickness = 1.5f;
            BorderColour = border;
        }

        hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 };
        flash = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 };

        var children = new List<Drawable>
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = ColourInfo.GradientVertical(top, bottom) },
            hover,
            flash,
        };

        // dark text on a light button gets no shadow (it turned "Tap here!" / the yellow button muddy, audit U8)
        bool lightText = TextColour.R + TextColour.G + TextColour.B > 1.5f;

        if (Icon is IconUsage icon)
        {
            // a small mark in the bottom-right corner, not a watermark through the words (audit U8)
            children.Add(new SpriteIcon
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-8),
                Icon = icon,
                Size = new Vector2(fontSize * 0.7f),
                Colour = TextColour.Opacity(0.7f),
            });
        }

        children.Add(new TextFlowContainer(t =>
        {
            t.Font = EditorFont.Regular(fontSize);
            t.Colour = TextColour;
            t.Shadow = lightText;
        })
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            TextAnchor = Anchor.Centre,
            Text = text,
        });

        Children = children;
    }

    protected override bool OnClick(ClickEvent e)
    {
        flash.FadeTo(0.35f).FadeOut(250);
        return base.OnClick(e);
    }

    protected override bool OnHover(HoverEvent e)
    {
        hover.FadeTo(0.12f, 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        hover.FadeOut(80);
        base.OnHoverLost(e);
    }
}

/// <summary>
/// Wide flat menu button of the "what do you want to do with this chart?" overlay
/// (reference 06): solid colour, 3 px lighter border, "N. Label" in large text.
/// </summary>
public partial class NumberedMenuButton : ClickableContainer
{
    public const float WIDTH = 1035;
    public const float HEIGHT = 85;

    public int Number { get; }

    private readonly string label;
    private readonly Colour4 colour;
    private Box background = null!;

    public NumberedMenuButton(int number, string label, Colour4 colour, Action action)
    {
        Number = number;
        this.label = label;
        this.colour = colour;
        Action = action;
        Size = new Vector2(WIDTH, HEIGHT);
        Masking = true;
        CornerRadius = 1; // radius 0 draws no border
        BorderThickness = 3;
        BorderColour = colour.Lighten(0.55f);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
            new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = $"{Number}. {label}",
                Font = EditorFont.Regular(54),
                Colour = Colour4.White,
                Shadow = true,
            },
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        background.FadeColour(colour.Lighten(0.25f), 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        background.FadeColour(colour, 80);
        base.OnHoverLost(e);
    }
}

/// <summary>Round white button with a black glyph (timeline zoom + / −, reference 02).</summary>
public partial class RoundIconButton : ClickableContainer
{
    private readonly IconUsage icon;
    private Circle disc = null!;

    public RoundIconButton(IconUsage icon, float size = 26)
    {
        this.icon = icon;
        Size = new Vector2(size);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            disc = new Circle { RelativeSizeAxes = Axes.Both, Colour = Colour4.White },
            new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.Both,
                Size = new Vector2(0.55f),
                Icon = icon,
                Colour = Colour4.Black,
            },
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        disc.FadeColour(EditorColours.Osu.Pink.Lighten(0.5f), 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        disc.FadeColour(Colour4.White, 80);
        base.OnHoverLost(e);
    }
}
