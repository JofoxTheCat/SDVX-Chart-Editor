using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// Square tile with a big icon and a label (left toolbox, reference 02: Select / Circle / Hold).
/// The active tile is bright, the others are dimmed. <see cref="Active"/> is driven by the owner;
/// <see cref="Hint"/> is a small note in the top-right corner (e.g. the Shift alternative).
/// </summary>
public partial class ToolTile : ClickableContainer
{
    public const float SIZE = 110;

    public readonly BindableBool Active = new BindableBool();

    /// <summary>Small text top-right ("Shift: FX"); empty hides it.</summary>
    public readonly Bindable<string> Hint = new Bindable<string>(string.Empty);

    /// <summary>Second small line under <see cref="Hint"/> ("Ctrl: Hold"); empty hides it.</summary>
    public readonly Bindable<string> Hint2 = new Bindable<string>(string.Empty);

    /// <summary>Colour of the big icon; the laser tile switches it between laser L and R.</summary>
    public readonly Bindable<Colour4> IconColour = new Bindable<Colour4>(Colour4.White);

    /// <summary>Big letter in the top-right corner in <see cref="IconColour"/> (the laser tile: "L" / "R"); empty hides it.</summary>
    public readonly Bindable<string> Badge = new Bindable<string>(string.Empty);

    private readonly IconUsage icon;
    private readonly string label;
    private readonly string? hotkey;
    private readonly Colour4 iconColour;

    private Box background = null!;
    private Container content = null!;
    private SpriteText hintText = null!;
    private SpriteText hint2Text = null!;
    private SpriteIcon iconSprite = null!;
    private Container badge = null!;
    private Box badgeBox = null!;
    private SpriteText badgeText = null!;

    public ToolTile(IconUsage icon, string label, string? hotkey = null, Colour4? iconColour = null, float size = SIZE)
    {
        this.icon = icon;
        this.label = label;
        this.hotkey = hotkey;
        this.iconColour = iconColour ?? Colour4.White;
        IconColour.Value = this.iconColour;
        Size = new Vector2(size);
        Masking = true;
        CornerRadius = 8;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        float s = Width;

        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.TileBackground },
            content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    iconSprite = new SpriteIcon
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.Centre,
                        // lower and smaller than centred, so the two hint lines ("Shift: FX" / "Ctrl: Hold") keep clear of it
                        Y = s * 0.5f,
                        Size = new Vector2(s * 0.33f),
                        Icon = icon,
                        Colour = iconColour,
                        Shadow = true,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Y = -s * 0.06f,
                        Text = label,
                        Font = EditorFont.Regular(s * 0.2f),
                        Colour = Colour4.White,
                    },
                    new SpriteText
                    {
                        Position = new Vector2(6, 3),
                        Text = hotkey ?? string.Empty,
                        Font = EditorFont.Bold(14),
                        Colour = EditorColours.TextMuted,
                    },
                    // right of the icon, below the two hint lines: the top-right corner belongs to the hints
                    // ("F: classic" on the laser tile used to sit on top of this badge)
                    badge = new Container
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Position = new Vector2(-4, s * 0.37f), // centred on the icon
                        Size = new Vector2(s * 0.26f),
                        Masking = true,
                        CornerRadius = 5,
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            badgeBox = new Box { RelativeSizeAxes = Axes.Both },
                            badgeText = new SpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Font = EditorFont.Bold(s * 0.23f),
                                Colour = Colour4.Black,
                            },
                        },
                    },
                    // hints keep clear of the tile edge and of the hotkey digit; a longer text is cut with "…"
                    hintText = new SpriteText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Position = new Vector2(-6, 2),
                        Font = EditorFont.Bold(12.5f), // was 11 (~6 px on screen, audit P4)
                        Colour = EditorColours.Lanes.Fx,
                        MaxWidth = s - 30,
                        Truncate = true,
                    },
                    hint2Text = new SpriteText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Position = new Vector2(-6, 15),
                        Font = EditorFont.Bold(12.5f),
                        Colour = Colour4.White,
                        // below the hotkey digit, so this line may use more of the width ("classic lasers")
                        MaxWidth = s - 20,
                        Truncate = true,
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Active.BindValueChanged(_ => updateState(), true);
        Hint.BindValueChanged(e => hintText.Text = e.NewValue, true);
        Hint2.BindValueChanged(e => hint2Text.Text = e.NewValue, true);
        IconColour.BindValueChanged(e =>
        {
            iconSprite.FadeColour(e.NewValue, 100);
            badgeBox.FadeColour(e.NewValue, 100);
        }, true);
        Badge.BindValueChanged(e =>
        {
            badgeText.Text = e.NewValue;
            bool shown = !string.IsNullOrEmpty(e.NewValue);
            badge.Alpha = shown ? 1 : 0;
            // the icon makes room for the badge on its right
            iconSprite.X = shown ? -Width * 0.1f : 0;
        }, true);
    }

    private void updateState()
    {
        background.FadeColour(Active.Value ? EditorColours.Osu.TileActive : EditorColours.Osu.TileBackground, 100);
        content.FadeTo(Active.Value || IsHovered ? 1 : 0.45f, 100);
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateState();
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        base.OnHoverLost(e);
        updateState();
    }
}

/// <summary>
/// Right-toolbar toggle (reference 02: New Combo, Whistle, Grid Snap, Lock Notes …):
/// icon with a label above/below it, dimmed while off, bright while on.
/// </summary>
public partial class ToggleTile : ClickableContainer
{
    public const float WIDTH = 100;
    public const float HEIGHT = 112;

    public readonly BindableBool Current = new BindableBool();

    private readonly IconUsage icon;
    private readonly string top;
    private readonly string bottom;
    private readonly string? hotkey;

    private Box background = null!;
    private Container content = null!;

    /// <param name="top">Upper label line ("Grid").</param>
    /// <param name="bottom">Lower label line ("Snap"), may be empty.</param>
    public ToggleTile(IconUsage icon, string top, string bottom, string? hotkey = null)
    {
        this.icon = icon;
        this.top = top;
        this.bottom = bottom;
        this.hotkey = hotkey;
        Size = new Vector2(WIDTH, HEIGHT);
        Masking = true;
        CornerRadius = 8;
        Action = () => Current.Toggle();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.TileBackground, Alpha = 0.5f },
            content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Y = 4,
                        Text = top,
                        Font = EditorFont.Regular(24),
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(34),
                        Icon = icon,
                        Shadow = true,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Y = -4,
                        Text = bottom,
                        Font = EditorFont.Regular(24),
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Position = new Vector2(-5, 3),
                        Text = hotkey ?? string.Empty,
                        Font = EditorFont.Bold(13),
                        Colour = EditorColours.TextMuted,
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Current.BindValueChanged(_ => updateState(), true);
    }

    private void updateState()
    {
        background.FadeTo(Current.Value ? 1 : 0.5f, 100);
        background.FadeColour(Current.Value ? EditorColours.Osu.TileActive : EditorColours.Osu.TileBackground, 100);
        content.FadeColour(Current.Value ? Colour4.White : Colour4.FromHex("c8c8c8"), 100);
        content.FadeTo(Current.Value ? 1 : IsHovered ? 0.7f : 0.38f, 100);
    }

    protected override bool OnHover(HoverEvent e)
    {
        updateState();
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        base.OnHoverLost(e);
        updateState();
    }
}
