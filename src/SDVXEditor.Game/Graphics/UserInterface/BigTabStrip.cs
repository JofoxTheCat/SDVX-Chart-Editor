using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// The large lower-case tab strip of the osu!stable editor ("compose design timing …",
/// reference 02/03): dark translucent strip with a rounded right end, the active tab
/// is a solid blue block.
/// </summary>
public partial class BigTabStrip<T> : CompositeDrawable
    where T : notnull
{
    public const float HEIGHT = 42;

    public readonly Bindable<T> Current = new Bindable<T>();

    /// <summary>
    /// A tab shown as active over <see cref="Current"/> while it is set (a tab that opens a dialog instead of a page:
    /// "song setup" lights up while its dialog is open, the page tab behind dims; audit U10).
    /// </summary>
    public readonly Bindable<object?> Lit = new Bindable<object?>();

    /// <summary>Fired for every click, also on the already active tab (Song Setup opens a dialog each time).</summary>
    public event Action<T>? TabClicked;

    private readonly IReadOnlyList<(T Value, string Label)> items;

    public BigTabStrip(IEnumerable<(T Value, string Label)> items)
    {
        this.items = items.ToList();
        AutoSizeAxes = Axes.X;
        Height = HEIGHT;
        Masking = true;
        CornerRadius = 6;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        FillFlowContainer flow;

        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.TabStripBackground },
            flow = new FillFlowContainer
            {
                AutoSizeAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Padding = new MarginPadding { Right = 8 },
            },
        };

        foreach (var (value, label) in items)
            flow.Add(new TabItem(value, label, this));
    }

    private partial class TabItem : ClickableContainer
    {
        private readonly T value;
        private readonly string label;
        private readonly BigTabStrip<T> strip;
        private readonly Bindable<T> current = new Bindable<T>();
        private readonly Bindable<object?> lit = new Bindable<object?>();

        private Box active = null!;
        private Box hover = null!;

        public TabItem(T value, string label, BigTabStrip<T> strip)
        {
            this.value = value;
            this.label = label;
            this.strip = strip;
            RelativeSizeAxes = Axes.Y;
            AutoSizeAxes = Axes.X;
            current.BindTo(strip.Current);
            lit.BindTo(strip.Lit);

            Action = () =>
            {
                strip.TabClicked?.Invoke(this.value);
                current.Value = this.value;
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                active = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.TabActive, Alpha = 0 },
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 },
                new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = label.ToLowerInvariant(),
                    Font = EditorFont.Regular(36),
                    Colour = Colour4.White,
                    Margin = new MarginPadding { Horizontal = 18, Bottom = 3 },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            current.BindValueChanged(_ => updateActive(), true);
            lit.BindValueChanged(_ => updateActive());
        }

        private void updateActive()
        {
            var eq = EqualityComparer<T>.Default;
            bool isLit = lit.Value is T l && eq.Equals(l, value);
            bool isCurrent = eq.Equals(current.Value, value);
            active.FadeTo(isLit ? 1 : isCurrent ? (lit.Value is null ? 1 : 0.35f) : 0, 80);
        }

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeTo(0.08f, 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(80);
            base.OnHoverLost(e);
        }
    }
}
