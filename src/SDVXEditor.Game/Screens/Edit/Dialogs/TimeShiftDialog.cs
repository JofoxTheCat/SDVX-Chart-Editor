using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>Units of the insert / delete time dialog.</summary>
public enum TimeShiftUnit
{
    SnapTicks,
    Beats,
    Measures,
    Pulses,
}

/// <summary>
/// Timing → Insert Blank Time / Delete Range… (Alt+Insert): a length in snap ticks, beats, measures or pulses, then
/// "Insert at cursor" or "Delete from cursor" (Core <see cref="Core.Editing.TimeShift"/>, one undo step each).
/// </summary>
public partial class TimeShiftDialog : DialogWindow
{
    private readonly string cursorText;
    private readonly Func<TimeShiftUnit, double, long> toPulses;
    private readonly Func<long, bool, string?> apply;

    public readonly BindableDouble Amount = new BindableDouble(1) { MinValue = 0, MaxValue = 10_000, Precision = 0.001 };
    public readonly Bindable<TimeShiftUnit> Unit = new Bindable<TimeShiftUnit>(TimeShiftUnit.Beats);

    public SpriteText ResultText { get; private set; } = null!;

    /// <param name="cursorText">"12:3 (01:23:456)" of the cursor.</param>
    /// <param name="toPulses">The length in pulses of an amount in a unit (at the cursor).</param>
    /// <param name="apply">(pulses, delete) → null on success, else why it was refused.</param>
    public TimeShiftDialog(string cursorText, Func<TimeShiftUnit, double, long> toPulses, Func<long, bool, string?> apply)
        : base("Insert / Delete Time", new Vector2(420, 170))
    {
        this.cursorText = cursorText;
        this.toPulses = toPulses;
        this.apply = apply;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var unit = new ComboBox<TimeShiftUnit>(Enum.GetValues<TimeShiftUnit>())
        {
            Position = new Vector2(150, 34),
            Width = 150,
            Formatter = u => u switch
            {
                TimeShiftUnit.SnapTicks => "snap ticks",
                TimeShiftUnit.Beats => "beats",
                TimeShiftUnit.Measures => "measures",
                _ => "pulses (240 / beat)",
            },
        };
        unit.Current.BindTo(Unit);

        Body.Children = new Drawable[]
        {
            DialogText.Label($"At the cursor: {cursorText}. Everything from there on moves; the audio stays.").With(t => t.Position = new Vector2(10, 10)),
            new NumericUpDown { Position = new Vector2(10, 34), Width = 130, Current = Amount, Decimals = 3, Increment = 1 },
            unit,
            ResultText = DialogText.Label("").With(t =>
            {
                t.Position = new Vector2(10, 66);
                t.Colour = EditorColours.Dialog.NoteRed;
            }),
            new DialogButton("Insert at cursor", () => run(false)) { IsDefault = true, Position = new Vector2(10, 128), Size = new Vector2(130, 30) },
            new DialogButton("Delete from cursor", () => run(true)) { Position = new Vector2(148, 128), Size = new Vector2(140, 30) },
            new DialogButton("Cancel", Cancel) { Position = new Vector2(296, 128), Size = new Vector2(114, 30) },
        };

        Amount.BindValueChanged(_ => preview());
        Unit.BindValueChanged(_ => preview(), true);
    }

    private long pulses => toPulses(Unit.Value, Amount.Value);

    private void preview()
    {
        long p = pulses;
        ResultText.Colour = EditorColours.Dialog.Text;
        ResultText.Text = p <= 0 ? "Length 0: nothing to do." : $"= {p} pulses ({(double)p / Timebase.PulsesPerQuarter:0.###} beats)";
    }

    /// <summary>Inserts (or deletes) the length; closes on success, shows the reason otherwise.</summary>
    public void Run(bool delete) => run(delete);

    private void run(bool delete)
    {
        if (apply(pulses, delete) is { } refused)
        {
            ResultText.Colour = EditorColours.Dialog.NoteRed;
            ResultText.Text = refused;
            return;
        }

        Accept();
    }
}
