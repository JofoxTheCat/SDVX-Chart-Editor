using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Overlays;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// "♥ Support the Artist": a small, quiet pill that opens the song's sources (<see cref="ExternalLinks.ShowSupport"/>).
/// Hidden while <see cref="Sources"/> holds no openable link. Two looks: <see cref="SupportButtonStyle.Dark"/> for
/// song select (over the background), <see cref="SupportButtonStyle.Light"/> for the editor's menu bar and dialogs.
/// </summary>
public partial class SupportArtistButton : ClickableContainer, IHasTooltip
{
    public const string Label = "Support the Artist";

    /// <summary>The song's sources as stored; only the valid ones count (<see cref="MusicLinks.Openable"/>).</summary>
    public readonly Bindable<IReadOnlyList<string>> Sources = new Bindable<IReadOnlyList<string>>(Array.Empty<string>());

    /// <summary>Song title for the popup's intro line (optional).</summary>
    public Func<string?>? SongTitle { get; init; }

    private readonly SupportButtonStyle style;
    private Box background = null!;
    private Drawable content = null!;
    private SpriteText? subtitle;
    private Container? heart;

    /// <summary>The card's second line, e.g. "Listen to or buy this song - 2 links" (smoke test).</summary>
    public string SubtitleText => subtitle?.Text.ToString() ?? "";

    [Resolved]
    private ExternalLinks links { get; set; } = null!;

    /// <summary>Number of links a click would offer (smoke test).</summary>
    public int LinkCount { get; private set; }

    public bool HasLinks => LinkCount > 0;

    public SupportArtistButton(SupportButtonStyle style = SupportButtonStyle.Dark)
    {
        this.style = style;
        if (style == SupportButtonStyle.Card)
        {
            RelativeSizeAxes = Axes.X;
            Height = CARD_HEIGHT;
        }
        else AutoSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = style switch { SupportButtonStyle.Dark => 13, SupportButtonStyle.Card => 8, _ => 3 };
        Alpha = 0;
        Action = () => links.ShowSupport(Sources.Value, SongTitle?.Invoke());
    }

    public LocalisableString TooltipText => LinkCount switch
    {
        0 => string.Empty,
        1 => $"Open the song's page ({MusicLinks.Openable(Sources.Value)[0].Service.Name})",
        _ => $"Where to listen to or buy this song ({LinkCount} links)",
    };

    public const float CARD_HEIGHT = 64;

    /// <summary>Text size of the light style: dialogs keep 12.5 (unscaled), the menu bar passes its own (audit P17 / U23).</summary>
    public float LightFontSize { get; init; } = 12.5f;

    private static readonly Colour4 pink = Colour4.FromHex("ff66aa");

    [BackgroundDependencyLoader]
    private void load()
    {
        if (style == SupportButtonStyle.Card)
        {
            loadCard();
            return;
        }

        bool dark = style == SupportButtonStyle.Dark;
        float size = dark ? 17 : LightFontSize;
        var ink = dark ? Colour4.White : Colour4.FromHex("4a4a4a");

        Children = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = dark ? Colour4.Black.Opacity(0.45f) : Colour4.Transparent,
            },
            content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(dark ? 7 : 5, 0),
                Padding = dark ? new MarginPadding { Horizontal = 12, Vertical = 4 } : new MarginPadding { Horizontal = 8, Vertical = 4 },
                Alpha = dark ? 0.8f : 0.85f,
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = FontAwesome.Solid.Heart,
                        Size = new Vector2(size * 0.75f),
                        Colour = Colour4.FromHex("ff66aa"),
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = Label,
                        Font = EditorFont.Regular(size),
                        Colour = ink,
                        Shadow = dark,
                    },
                },
            },
        };

        if (dark)
        {
            BorderThickness = 1.5f;
            BorderColour = Colour4.FromHex("ff66aa").Opacity(0.6f);
        }
    }

    /// <summary>
    /// Song select: a card across the details column (the space under the tags was empty) — a heart in a pink ring, the
    /// label and a quiet second line. Calm at rest (translucent, thin pink rim), warmer on hover; hidden without a link.
    /// </summary>
    private void loadCard()
    {
        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.4f) },
            content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0.9f,
                Children = new Drawable[]
                {
                    heart = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        X = 34,
                        Size = new Vector2(40),
                        Masking = true,
                        CornerRadius = 20,
                        BorderThickness = 2,
                        BorderColour = pink.Opacity(0.8f),
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = pink.Opacity(0.15f) },
                            new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Y = 1,
                                Icon = FontAwesome.Solid.Heart,
                                Size = new Vector2(18),
                                Colour = pink,
                            },
                        },
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 68,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 1),
                        Children = new Drawable[]
                        {
                            new SpriteText { Text = Label, Font = EditorFont.Bold(22), Colour = Colour4.White, Shadow = true },
                            subtitle = new SpriteText { Font = EditorFont.Regular(14), Colour = Colour4.White.Opacity(0.7f), Shadow = true },
                        },
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -18,
                        Icon = FontAwesome.Solid.ExternalLinkAlt,
                        Size = new Vector2(14),
                        Colour = Colour4.White.Opacity(0.55f),
                    },
                },
            },
        };

        BorderThickness = 1.5f;
        BorderColour = pink.Opacity(0.45f);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Sources.BindValueChanged(e =>
        {
            LinkCount = MusicLinks.Openable(e.NewValue).Count;
            this.FadeTo(LinkCount > 0 ? 1 : 0, 150);
            if (subtitle != null)
                subtitle.Text = LinkCount == 1
                    ? $"Listen to or buy this song on {MusicLinks.Openable(e.NewValue)[0].Service.Name}"
                    : $"Listen to or buy this song - {LinkCount} links";
        }, true);
    }

    // hidden = not clickable (Alpha 0 still receives input otherwise)
    public override bool PropagatePositionalInputSubTree => LinkCount > 0 && base.PropagatePositionalInputSubTree;

    protected override bool OnHover(HoverEvent e)
    {
        if (style == SupportButtonStyle.Card)
        {
            content.FadeTo(1, 100);
            background.FadeColour(pink.Opacity(0.22f), 100);
            BorderColour = pink.Opacity(0.9f);
            heart?.ScaleTo(1.08f, 160, Easing.OutQuint);
            return base.OnHover(e);
        }

        content.FadeTo(1, 80);
        background.FadeColour(style == SupportButtonStyle.Dark ? Colour4.FromHex("ff66aa").Opacity(0.35f) : Colour4.FromHex("cce8ff"), 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        if (style == SupportButtonStyle.Card)
        {
            content.FadeTo(0.9f, 100);
            background.FadeColour(Colour4.Black.Opacity(0.4f), 100);
            BorderColour = pink.Opacity(0.45f);
            heart?.ScaleTo(1, 160, Easing.OutQuint);
            base.OnHoverLost(e);
            return;
        }

        content.FadeTo(style == SupportButtonStyle.Dark ? 0.8f : 0.85f, 80);
        background.FadeColour(style == SupportButtonStyle.Dark ? Colour4.Black.Opacity(0.45f) : Colour4.Transparent, 80);
        base.OnHoverLost(e);
    }
}

public enum SupportButtonStyle
{
    /// <summary>A translucent dark pill with a pink rim (small; song select used it before <see cref="Card"/>).</summary>
    Dark,

    /// <summary>Song select: a full-width card under the details — heart in a ring, label, a line about the links.</summary>
    Card,

    /// <summary>Editor menu bar / dialogs: plain grey text with a pink heart.</summary>
    Light,
}
