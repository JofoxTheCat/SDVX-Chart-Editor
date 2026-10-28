using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Timing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>
/// F6 "Property Transformer": scroll-speed line tools on a time range of the F6 draft — a linear or
/// exponential ramp (N steps or one point per snap tick, smooth or stepped), scaling the values, or
/// shifting the points in time. Works on the draft, so F6's OK/Cancel still decides.
/// </summary>
public partial class PropertyTransformerDialog : DialogWindow
{
    public enum Tool
    {
        Ramp,
        Scale,
        Shift,
    }

    private readonly TimingControlPointsDialog owner;
    private readonly Func<TimingMap> map;
    private readonly Func<double> now;

    public readonly BindableDouble FromMs = new BindableDouble { MinValue = -10_000, MaxValue = 3_600_000, Precision = 1 };
    public readonly BindableDouble ToMs = new BindableDouble { MinValue = -10_000, MaxValue = 3_600_000, Precision = 1 };
    public readonly Bindable<Tool> Mode = new Bindable<Tool>(Tool.Ramp);
    public readonly BindableDouble StartValue = new BindableDouble(1) { MinValue = 0.01, MaxValue = 100, Precision = 0.01 };
    public readonly BindableDouble EndValue = new BindableDouble(2) { MinValue = 0.01, MaxValue = 100, Precision = 0.01 };
    public readonly Bindable<RampShape> Shape = new Bindable<RampShape>(RampShape.Linear);
    public readonly BindableDouble Steps = new BindableDouble(4) { MinValue = 1, MaxValue = 512, Precision = 1 };
    public readonly BindableBool PerSnapTick = new BindableBool();
    public readonly BindableInt Divisor = new BindableInt(4);
    public readonly BindableBool Stepped = new BindableBool();
    public readonly BindableDouble Factor = new BindableDouble(1.5) { MinValue = 0.01, MaxValue = 100, Precision = 0.01 };
    public readonly BindableDouble ShiftMs = new BindableDouble(0) { MinValue = -600_000, MaxValue = 600_000, Precision = 1 };

    public PropertyTransformerDialog(TimingControlPointsDialog owner, Func<TimingMap> map, double fromMs, double toMs, Func<double> now, int divisor)
        : base("Property Transformer", new Vector2(330, 372))
    {
        this.owner = owner;
        this.map = map;
        this.now = now;
        FromMs.Value = Math.Round(fromMs);
        ToMs.Value = Math.Round(toMs);
        Divisor.Value = divisor;

        var m = map();
        StartValue.Value = Math.Max(0.01, Graph.ValueAt(owner.Draft.Beat.ScrollSpeed, m.MsToPulse(fromMs), 1.0));
        EndValue.Value = Math.Max(0.01, Graph.ValueAt(owner.Draft.Beat.ScrollSpeed, m.MsToPulse(toMs), 1.0));
        if (Math.Abs(EndValue.Value - StartValue.Value) < 0.001) EndValue.Value = StartValue.Value * 2;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var divisor = new ComboBox<int>(EditorColours.Divisor.Steps) { Width = 58, Formatter = d => $"1/{d}" };
        divisor.Current.Value = Divisor.Value;
        divisor.Current.BindValueChanged(e => Divisor.Value = e.NewValue);

        Body.Children = new Drawable[]
        {
            new GroupBox("Range (scroll speed points)")
            {
                Position = new Vector2(4, 4),
                Size = new Vector2(322, 70),
                Children = new Drawable[]
                {
                    DialogText.Label("From", bold: true).With(t => t.Position = new Vector2(0, 4)),
                    new NumericUpDown { Position = new Vector2(50, 0), Width = 110, Current = FromMs, Increment = 10 },
                    new DialogButton("Current", () => FromMs.Value = Math.Round(now())) { Position = new Vector2(168, 0), Size = new Vector2(70, 23) },
                    DialogText.Label("To", bold: true).With(t => t.Position = new Vector2(0, 32)),
                    new NumericUpDown { Position = new Vector2(50, 28), Width = 110, Current = ToMs, Increment = 10 },
                    new DialogButton("Current", () => ToMs.Value = Math.Round(now())) { Position = new Vector2(168, 28), Size = new Vector2(70, 23) },
                    DialogText.Label("ms").With(t => t.Position = new Vector2(246, 4)),
                },
            },
            new GroupBox("Tool")
            {
                Position = new Vector2(4, 80),
                Size = new Vector2(322, 240),
                Children = new Drawable[]
                {
                    new DialogRadioButton<Tool>(Mode, Tool.Ramp, "Ramp") { Position = new Vector2(0, 4) },
                    DialogText.Label("from x").With(t => t.Position = new Vector2(20, 30)),
                    new NumericUpDown { Position = new Vector2(64, 26), Width = 70, Decimals = 2, Increment = 0.05, Current = StartValue },
                    DialogText.Label("to x").With(t => t.Position = new Vector2(144, 30)),
                    new NumericUpDown { Position = new Vector2(176, 26), Width = 70, Decimals = 2, Increment = 0.05, Current = EndValue },
                    new DialogRadioButton<RampShape>(Shape, RampShape.Linear, "Linear") { Position = new Vector2(20, 58) },
                    new DialogRadioButton<RampShape>(Shape, RampShape.Exponential, "Exponential") { Position = new Vector2(100, 58) },
                    DialogText.Label("Steps").With(t => t.Position = new Vector2(20, 86)),
                    new NumericUpDown { Position = new Vector2(64, 82), Width = 70, Current = Steps },
                    new DialogCheckBox("or every") { Position = new Vector2(144, 86) }.With(c => c.Current.BindTo(PerSnapTick)),
                    divisor.With(d => d.Position = new Vector2(220, 82)),
                    new DialogCheckBox("Stepped (hold each value, like osu! green lines)") { Position = new Vector2(20, 112) }.With(c => c.Current.BindTo(Stepped)),

                    new DialogRadioButton<Tool>(Mode, Tool.Scale, "Scale values by") { Position = new Vector2(0, 146) },
                    new NumericUpDown { Position = new Vector2(130, 142), Width = 70, Decimals = 2, Increment = 0.05, Current = Factor },

                    new DialogRadioButton<Tool>(Mode, Tool.Shift, "Shift in time by") { Position = new Vector2(0, 178) },
                    new NumericUpDown { Position = new Vector2(130, 174), Width = 90, Current = ShiftMs, Increment = 10 },
                    DialogText.Label("ms").With(t => t.Position = new Vector2(226, 178)),
                },
            },
            new DialogButton("Apply", Accept)
            {
                IsDefault = true,
                Position = new Vector2(4, 330),
                Size = new Vector2(200, 36),
            },
            new DialogButton("Cancel", Cancel)
            {
                Position = new Vector2(212, 330),
                Size = new Vector2(114, 36),
            },
        };
    }

    /// <summary>The draft's new scroll-speed graph for the current settings (null when the range is empty).</summary>
    public List<GraphPoint>? Preview()
    {
        var m = map();
        long from = Math.Max(0, (long)Math.Round(m.MsToPulse(Math.Min(FromMs.Value, ToMs.Value))));
        long to = Math.Max(0, (long)Math.Round(m.MsToPulse(Math.Max(FromMs.Value, ToMs.Value))));
        var graph = owner.Draft.Beat.ScrollSpeed;

        switch (Mode.Value)
        {
            case Tool.Ramp:
                if (to <= from) return null;
                return ScrollSpeedTools.Ramp(graph, from, to, StartValue.Value, EndValue.Value, Shape.Value,
                    (int)Steps.Value, Stepped.Value, PerSnapTick.Value ? m : null, PerSnapTick.Value ? Divisor.Value : 0);

            case Tool.Scale:
                return ScrollSpeedTools.Scale(graph, from, to, Factor.Value);

            default:
                long delta = (long)Math.Round(m.MsToPulse(m.PulseToMs(from) + ShiftMs.Value)) - from;
                return delta == 0 ? null : ScrollSpeedTools.Shift(graph, from, to, delta);
        }
    }

    protected override void OnAccept()
    {
        if (Preview() is { } graph)
            owner.ReplaceScrollSpeed(graph);
    }
}
