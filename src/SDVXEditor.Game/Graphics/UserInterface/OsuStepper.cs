using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// Blue pill with "‹ value ›" (timing tab BPM / Offset / Slider velocity, reference 03).
/// Click an arrow for one step (Shift = ten steps), scroll over it, or drag the value
/// horizontally for continuous changes.
/// </summary>
public partial class OsuStepper : CompositeDrawable
{
    public const float DEFAULT_WIDTH = 300;
    public const float DEFAULT_HEIGHT = 42;

    private readonly BindableNumber<double> current = new BindableDouble();

    /// <summary>The edited value; bind it to a view-model property.</summary>
    public BindableNumber<double> Current
    {
        get => current;
        set
        {
            current.UnbindBindings();
            current.BindTo(value);
        }
    }

    /// <summary>
    /// The options overlay's look (dark box, light rim, like its dropdowns) instead of the timing tab's bright blue
    /// (audit P6: the joystick steppers were the only bright blue controls in the options).
    /// </summary>
    public bool Subdued { get; init; }

    /// <summary>Amount added per arrow click / scroll notch.</summary>
    public double Step { get; set; } = 1;

    /// <summary>Value change per dragged pixel.</summary>
    public double DragStepPerPixel { get; set; } = 0.1;

    public Func<double, string> Formatter { get; set; } = v => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A drag of the value starts / ends (an editor groups the changes in between into one undo step).</summary>
    public event Action? GestureStarted;

    public event Action? GestureEnded;

    private SpriteText valueText = null!;

    public OsuStepper()
    {
        Size = new Vector2(DEFAULT_WIDTH, DEFAULT_HEIGHT);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 4,
                BorderThickness = 1.5f,
                BorderColour = Subdued ? Colour4.White.Opacity(0.6f) : EditorColours.Osu.StepperBorder,
                Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Subdued ? ColourInfo.SingleColour(Colour4.Black.Opacity(0.55f))
                        : ColourInfo.GradientVertical(EditorColours.Osu.StepperTop, EditorColours.Osu.StepperBottom),
                },
            },
            new DragArea(this) { RelativeSizeAxes = Axes.Both },
            valueText = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Font = EditorFont.Regular(21),
                Colour = Colour4.White,
            },
            new ArrowButton(FontAwesome.Solid.ChevronLeft, big => change(-1, big))
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 4,
            },
            new ArrowButton(FontAwesome.Solid.ChevronRight, big => change(1, big))
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -4,
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        current.BindValueChanged(e => valueText.Text = Formatter(e.NewValue), true);
    }

    private void change(int direction, bool big) => current.Value += direction * Step * (big ? 10 : 1);

    protected override bool OnScroll(ScrollEvent e)
    {
        current.Value += SDVXEditor.Game.Input.WheelInput.Direction(e) * Step * (e.ShiftPressed ? 10 : 1);
        return true;
    }

    private partial class DragArea : Container
    {
        private readonly OsuStepper stepper;
        private double dragStartValue;
        private float dragStartX;

        public DragArea(OsuStepper stepper)
        {
            this.stepper = stepper;
        }

        protected override bool OnDragStart(DragStartEvent e)
        {
            dragStartValue = stepper.current.Value;
            dragStartX = e.MousePosition.X;
            stepper.GestureStarted?.Invoke();
            return true;
        }

        protected override void OnDragEnd(DragEndEvent e)
        {
            stepper.GestureEnded?.Invoke();
            base.OnDragEnd(e);
        }

        protected override void OnDrag(DragEvent e)
        {
            float dx = e.MousePosition.X - dragStartX;
            stepper.current.Value = dragStartValue + dx * stepper.DragStepPerPixel * (e.ShiftPressed ? 0.1 : 1);
        }
    }

    private partial class ArrowButton : ClickableContainer
    {
        private readonly IconUsage icon;
        private readonly Action<bool> clicked;
        private Box hover = null!;

        public ArrowButton(IconUsage icon, Action<bool> clicked)
        {
            this.icon = icon;
            this.clicked = clicked;
            Size = new Vector2(34);
            Masking = true;
            CornerRadius = 3;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("123f86").Opacity(0.55f) },
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = icon,
                    Size = new Vector2(18),
                    Colour = Colour4.White,
                    Shadow = true,
                },
            };
        }

        protected override bool OnClick(ClickEvent e)
        {
            clicked(e.ShiftPressed);
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeTo(0.2f, 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(80);
            base.OnHoverLost(e);
        }
    }
}
