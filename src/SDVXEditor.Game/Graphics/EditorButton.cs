using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;

namespace SDVXEditor.Game.Graphics;

/// <summary>Flat rectangular button in the osu!stable editor style.</summary>
public partial class EditorButton : ClickableContainer
{
    private readonly string text;
    private Box background = null!;

    public EditorButton(string text)
    {
        this.text = text;
        AutoSizeAxes = Axes.Both;
        Masking = true;
        CornerRadius = 4;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.PanelLight },
            new SpriteText
            {
                Text = text,
                Font = EditorFont.Bold(16),
                Colour = EditorColours.Text,
                Margin = new MarginPadding { Horizontal = 18, Vertical = 9 },
            },
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        background.FadeColour(EditorColours.Highlight, 100);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        background.FadeColour(EditorColours.PanelLight, 100);
        base.OnHoverLost(e);
    }
}
