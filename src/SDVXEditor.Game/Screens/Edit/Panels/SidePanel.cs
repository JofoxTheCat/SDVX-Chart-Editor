using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Panels;

/// <summary>
/// A dark, non-modal panel of the compose tab's right column (inspector, layers, chart check): title, a ✕ that hides it,
/// and <see cref="Content"/>. <see cref="Shown"/> is the toggle (menu View, keys); hidden panels take no input.
/// </summary>
public abstract partial class SidePanel : CompositeDrawable
{
    public const float WIDTH = 340;

    public readonly BindableBool Shown = new BindableBool();

    protected FillFlowContainer Content { get; private set; } = null!;

    public string Title { get; }

    /// <summary>The ✕ button (smoke test).</summary>
    public Drawable CloseButton { get; private set; } = null!;

    protected SidePanel(string title)
    {
        Title = title;
        Width = WIDTH;
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 6;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.PanelDark },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding(8),
                Spacing = new Vector2(0, 6),
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 22,
                        Children = new Drawable[]
                        {
                            new SpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Text = Title, Font = EditorFont.Bold(18), Colour = EditorColours.Osu.Pink },
                            CloseButton = new CloseCross { Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight, Action = () => Shown.Value = false },
                        },
                    },
                    Content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 5),
                    },
                },
            },
        };

        Shown.BindValueChanged(e =>
        {
            this.FadeTo(e.NewValue ? 1 : 0, 100);
            if (e.NewValue) OnShown();
        }, true);
    }

    /// <summary>Hidden panels are not hit-tested (they would swallow clicks meant for the playfield).</summary>
    public override bool PropagatePositionalInputSubTree => Shown.Value && base.PropagatePositionalInputSubTree;

    public override bool PropagateNonPositionalInputSubTree => Shown.Value && base.PropagateNonPositionalInputSubTree;

    /// <summary>Clicks on the panel background stay in the panel.</summary>
    protected override bool OnMouseDown(MouseDownEvent e) => true;

    protected override bool OnClick(ClickEvent e) => true;

    protected override bool OnScroll(ScrollEvent e) => true;

    /// <summary>Called whenever the panel is shown (refresh its content).</summary>
    protected virtual void OnShown()
    {
    }

    private partial class CloseCross : ClickableContainer
    {
        public CloseCross()
        {
            Size = new Vector2(20);
            Child = new SpriteIcon { RelativeSizeAxes = Axes.Both, Icon = FontAwesome.Solid.Times, Colour = Colour4.White.Opacity(0.8f) };
        }
    }
}
