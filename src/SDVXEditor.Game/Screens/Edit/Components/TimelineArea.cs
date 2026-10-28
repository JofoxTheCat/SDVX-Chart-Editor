using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// Translucent band under the menu bar (reference 02/03): zoom ± at the left, the tick
/// ruler in the middle (an empty container the timeline/playfield work fills), and a
/// right-hand block that shows the beat divisor (compose) or the tap/metronome block (timing).
/// </summary>
public partial class TimelineArea : CompositeDrawable
{
    public const float HEIGHT = 140;
    public const float RIGHT_BLOCK_WIDTH = 440;

    /// <summary>One zoom step of the ± buttons and of Alt+wheel.</summary>
    public const float ZOOM_STEP = 0.1f;

    /// <summary>
    /// The editor's one zoom value (1 = default), shared with the playfield: the editor screen binds it to
    /// <c>EditorScreen.Zoom</c>, so the ± buttons and Alt+wheel over the timeline or the playfield zoom both.
    /// </summary>
    public readonly BindableFloat Zoom = CreateZoom();

    public static BindableFloat CreateZoom() => new BindableFloat(1) { MinValue = 0.25f, MaxValue = 4, Precision = 0.05f };

    /// <summary>Tick ruler / waveform area. Empty: filled by the timeline implementation.</summary>
    public Container RulerContent { get; private set; } = null!;

    /// <summary>Right block (beat divisor control or the timing tap block).</summary>
    public Container RightBlock { get; private set; } = null!;

    /// <summary>Things hanging under the ruler, e.g. the "207bpm" tag on the timing tab.</summary>
    public Container UnderRuler { get; private set; } = null!;

    public TimelineArea()
    {
        RelativeSizeAxes = Axes.X;
        Height = HEIGHT;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.5f) },
            new Box
            {
                RelativeSizeAxes = Axes.X,
                Height = 1,
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Colour = Colour4.Black.Opacity(0.6f),
            },
            new RoundIconButton(FontAwesome.Solid.Plus)
            {
                Position = new Vector2(8, 70),
                Action = () => Zoom.Value += ZOOM_STEP,
            },
            new RoundIconButton(FontAwesome.Solid.Minus)
            {
                Position = new Vector2(8, 106),
                Action = () => Zoom.Value -= ZOOM_STEP,
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 42, Right = RIGHT_BLOCK_WIDTH + 4, Top = 56, Bottom = 6 },
                Children = new Drawable[]
                {
                    RulerContent = new Container { RelativeSizeAxes = Axes.Both },
                    // baseline + "now" line; the ticks themselves come from the timeline
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Colour = Colour4.White.Opacity(0.7f),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 2,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Colour = Colour4.White,
                    },
                    UnderRuler = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 30,
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.TopLeft,
                    },
                },
            },
            RightBlock = new Container
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Width = RIGHT_BLOCK_WIDTH,
                RelativeSizeAxes = Axes.Y,
            },
        };
    }
}

/// <summary>"Beat Snap Divisor" label, pink-knob slider and the 1/N value (reference 02, top right).</summary>
public partial class BeatDivisorControl : CompositeDrawable
{
    public readonly BindableInt Divisor = new BindableInt(4);

    private SpriteText valueText = null!;
    private TooltipSpriteText positionText = null!;
    private SnapSlider slider = null!;

    public BeatDivisorControl()
    {
        Tutorial.TutorialAnchors.Register("compose.divisor", this);
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new SpriteText
            {
                Position = new Vector2(60, 4),
                Text = "Beat Snap Divisor:",
                Font = EditorFont.Regular(26),
                Colour = Colour4.White,
            },
            // "12:3" had no label (audit P1): the tooltip says what it is
            positionText = new TooltipSpriteText
            {
                Position = new Vector2(4, 48),
                Font = EditorFont.Regular(20),
                Colour = Colour4.White,
                TooltipText = "Measure : beat at the judgement line",
            },
            slider = new SnapSlider(EditorColours.Divisor.Steps)
            {
                Position = new Vector2(70, 44),
                Width = 300,
            },
            // the value has its own column right of the slider (audit U4: at 1/192 the knob sat on "1/192")
            valueText = new SpriteText
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-10, 36),
                Font = EditorFont.Regular(24),
            },
            new Box
            {
                Position = new Vector2(-1, 56),
                Width = 1,
                Height = 78,
                Colour = Colour4.White.Opacity(0.5f),
            },
        };

        slider.Current.BindTo(Divisor);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Divisor.BindValueChanged(e =>
        {
            valueText.Text = $"1/{e.NewValue}";
            valueText.Colour = EditorColours.Divisor.ForDivisor(e.NewValue);
        }, true);
    }

    /// <summary>Width kept free for "1/192" right of the slider.</summary>
    public const float VALUE_COLUMN = 84;

    /// <summary>The slider and the value text (smoke test: they must not overlap).</summary>
    public Drawable Slider => slider;

    public SpriteText ValueText => valueText;

    protected override void Update()
    {
        base.Update();
        slider.Width = Math.Clamp(DrawWidth - slider.X - VALUE_COLUMN, 120, 300);
    }

    /// <summary>One step along the divisor stops (Ctrl+wheel).</summary>
    public void Nudge(int direction) => slider.Nudge(direction);

    /// <summary>Beat position text left of the slider ("12:3").</summary>
    public void SetPositionText(string text) => positionText.Text = text;
}
