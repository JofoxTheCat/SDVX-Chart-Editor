using osu.Framework.Extensions.Color4Extensions;
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

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>osu! checkbox: a pink dot (filled = on, ring = off) followed by the label (reference 03).</summary>
public partial class PinkCheckbox : ClickableContainer, IHasTooltip
{
    /// <summary>Hover text (empty = none).</summary>
    public LocalisableString TooltipText { get; set; }

    public readonly BindableBool Current = new BindableBool();

    private readonly string label;
    private readonly float fontSize;

    private Circle fill = null!;

    /// <summary>The dot (ring + fill); smoke checks that it is drawn in both states.</summary>
    public Drawable Dot { get; private set; } = null!;
    private SpriteText text = null!;

    public PinkCheckbox(string label, float fontSize = 26)
    {
        this.label = label;
        this.fontSize = fontSize;
        AutoSizeAxes = Axes.Both;
        Action = () => Current.Toggle();
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        float dot = fontSize * 0.8f;

        Children = new Drawable[]
        {
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(fontSize * 0.6f, 0),
                Children = new Drawable[]
                {
                    Dot = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(dot),
                        Masking = true,
                        CornerRadius = dot / 2,
                        BorderThickness = 2.5f,
                        BorderColour = EditorColours.Osu.PinkDot,
                        Children = new Drawable[]
                        {
                            // Always drawn: a masked container paints its border only over drawn child pixels, so
                            // without this backing the ring vanished when the fill faded out (audit B1: "off" looked
                            // like a plain label).
                            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.35f) },
                            fill = new Circle { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.PinkDot },
                        },
                    },
                    text = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = label,
                        Font = EditorFont.Regular(fontSize),
                        Colour = Colour4.White,
                    },
                },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Current.BindValueChanged(e =>
        {
            // a setting restored off the update thread (smoke settings restore while the game disposes) must not start
            // a transform there ("Cannot mutate the Transforms ... not on the update thread")
            if (osu.Framework.Development.ThreadSafety.IsUpdateThread) fill.FadeTo(e.NewValue ? 1 : 0, 100);
            else Schedule(() => fill.FadeTo(e.NewValue ? 1 : 0, 100));
        }, true);
    }

    protected override bool OnHover(HoverEvent e)
    {
        text.FadeColour(EditorColours.Osu.Pink.Lighten(0.4f), 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        text.FadeColour(Colour4.White, 80);
        base.OnHoverLost(e);
    }
}
