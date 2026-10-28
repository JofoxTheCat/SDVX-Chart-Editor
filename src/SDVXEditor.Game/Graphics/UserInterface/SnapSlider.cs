using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// osu! slider with a pink ring knob and discrete stops (the beat snap divisor
/// slider, reference 02). Click, drag or scroll; the knob always lands on a stop.
/// </summary>
public partial class SnapSlider : CompositeDrawable
{
    public const float KNOB_SIZE = 22;

    /// <summary>Selected stop value (not index).</summary>
    public readonly Bindable<int> Current = new Bindable<int>();

    private readonly IReadOnlyList<int> stops;

    private Container knob = null!;

    public SnapSlider(IReadOnlyList<int> stops)
    {
        if (stops.Count == 0)
            throw new ArgumentException("needs at least one stop", nameof(stops));

        this.stops = stops;
        Current.Value = stops[0];
        Height = KNOB_SIZE + 4;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Horizontal = KNOB_SIZE / 2 },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 2,
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Colour = EditorColours.Osu.SliderTrack,
                    },
                    knob = new Container
                    {
                        Size = new Vector2(KNOB_SIZE),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        RelativePositionAxes = Axes.X,
                        Masking = true,
                        CornerRadius = KNOB_SIZE / 2,
                        BorderThickness = 2.5f,
                        BorderColour = EditorColours.Osu.SliderTrack,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.35f) },
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
            int index = indexOf(e.NewValue);
            float x = stops.Count == 1 ? 0 : (float)index / (stops.Count - 1);
            knob.MoveToX(x, 120, Easing.OutQuint);
        }, true);
    }

    private int indexOf(int value)
    {
        int best = 0;
        for (int i = 0; i < stops.Count; i++)
        {
            if (Math.Abs(stops[i] - value) < Math.Abs(stops[best] - value))
                best = i;
        }

        return best;
    }

    private void setFromPosition(Vector2 screenSpace)
    {
        float trackWidth = DrawWidth - KNOB_SIZE;
        if (trackWidth <= 0) return;

        float x = (ToLocalSpace(screenSpace).X - KNOB_SIZE / 2) / trackWidth;
        int index = (int)Math.Round(Math.Clamp(x, 0, 1) * (stops.Count - 1));
        Current.Value = stops[index];
    }

    /// <summary>Moves one stop up or down (Ctrl+wheel in the editor).</summary>
    public void Nudge(int direction)
    {
        int index = Math.Clamp(indexOf(Current.Value) + direction, 0, stops.Count - 1);
        Current.Value = stops[index];
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        setFromPosition(e.ScreenSpaceMousePosition);
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e) => true;

    protected override void OnDrag(DragEvent e) => setFromPosition(e.ScreenSpaceMousePosition);

    protected override bool OnScroll(ScrollEvent e)
    {
        Nudge(SDVXEditor.Game.Input.WheelInput.Direction(e));
        return true;
    }
}
