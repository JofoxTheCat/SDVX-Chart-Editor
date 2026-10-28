using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Timing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Input;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>
/// F6 "Timing and Control Points" (reference 04), SDVX content: BPM points (red), scroll-speed
/// points (green, x1,00), stops (blue), time signatures (grey). Edits a <see cref="TimingDraft"/>
/// (a working copy of the chart's timing); OK applies every change as one undo step through
/// <see cref="TimingCommands.Apply"/>, Cancel discards. Ctrl+C / Ctrl+V copy scroll-speed points to the
/// current time.
/// </summary>
public partial class TimingControlPointsDialog : DialogWindow
{
    private const int sig_other = 0;

    /// <summary>What "+" adds.</summary>
    public enum NewPointKind
    {
        Bpm,
        ScrollSpeed,
        Stop,
        TimeSignature,
    }

    private readonly TimingViewModel model;
    private readonly EditorClock? playback;

    public TimingDraft Draft { get; }
    private TimingMap map;

    // left side fields edit the selected rows
    public readonly BindableDouble Value = new BindableDouble(120) { MinValue = 0, MaxValue = 100_000, Precision = 0.001 };
    public readonly BindableDouble OffsetField = new BindableDouble { MinValue = -10_000, MaxValue = 3_600_000, Precision = 1 };
    private readonly Bindable<int> signature = new Bindable<int>(4);
    private readonly BindableDouble otherNumerator = new BindableDouble(5) { MinValue = 1, MaxValue = 64, Precision = 1 };
    private readonly BindableDouble otherDenominator = new BindableDouble(4) { MinValue = 1, MaxValue = 64, Precision = 1 };
    public readonly BindableBool Slam = new BindableBool();
    public readonly BindableDouble SlamTo = new BindableDouble(1) { MinValue = 0, MaxValue = 100, Precision = 0.01 };
    private readonly BindableBool curved = new BindableBool();
    private readonly BindableDouble curveA = new BindableDouble(0.25) { MinValue = 0, MaxValue = 1, Precision = 0.01 };
    private readonly BindableDouble curveB = new BindableDouble(0.75) { MinValue = 0, MaxValue = 1, Precision = 0.01 };
    public readonly BindableBool InheritPrevious = new BindableBool(true);
    public readonly Bindable<NewPointKind> AddKind = new Bindable<NewPointKind>(NewPointKind.ScrollSpeed);

    // audio tab
    private readonly BindableDouble volume = new BindableDouble(100) { MinValue = 0, MaxValue = 200, Precision = 1 };
    private readonly BindableDouble previewStart = new BindableDouble { MinValue = 0, MaxValue = 3_600_000, Precision = 1 };
    private readonly BindableDouble previewLength = new BindableDouble(15000) { MinValue = 1000, MaxValue = 60_000, Precision = 1 };

    private DialogTabControl listTabs = null!;
    private ListView<ControlPointRow> list = null!;
    private SpriteText valueLabel = null!;
    private Container statusBox = null!;
    private TextFlowContainer statusText = null!;

    /// <summary>The last notice the dialog showed (e.g. why "+" added nothing); empty until then.</summary>
    public string LastMessage { get; private set; } = string.Empty;
    private Container signatureGroup = null!, scrollGroup = null!, stopGroup = null!;
    private bool updatingFields;
    private static List<GraphPoint>? clipboard;

    public ListView<ControlPointRow> List => list;

    /// <summary>The draft objects (BpmChange / GraphPoint / Stop / TimeSignature) of the selected rows.</summary>
    public IReadOnlyList<object> SelectedSources => list.SelectedItems.Select(r => r.Source!).ToList();

    public TimingControlPointsDialog(TimingViewModel model, EditorClock? playback = null)
        : base("Timing and Control Points", new Vector2(578, 440))
    {
        this.model = model;
        this.playback = playback;
        Draft = model.Session != null ? TimingDraft.From(model.Session.Chart) : new TimingDraft();
        if (Draft.Beat.Bpm.Count == 0) Draft.Beat.Bpm.Add(new BpmChange(0, 120));
        map = Draft.BuildMap();
    }

    private double now => Math.Round(playback?.CurrentTime.Value ?? 0);

    [BackgroundDependencyLoader]
    private void load()
    {
        var leftTabs = new DialogTabControl
        {
            Position = new Vector2(4, 4),
            Size = new Vector2(238, 290),
        };
        leftTabs.AddPage("Timing", createTimingPage());
        leftTabs.AddPage("Audio", createAudioPage());
        leftTabs.AddPage("Style", createStylePage());

        listTabs = new DialogTabControl(0)
        {
            Position = new Vector2(246, 4),
            Size = new Vector2(328, 290),
        };
        listTabs.AddPage("All", new Container());
        listTabs.AddPage("Timing Points", new Container());
        listTabs.AddPage("Scroll Speed", new Container());

        list = new ListView<ControlPointRow>(new[]
        {
            new ListViewColumn<ControlPointRow>("Offset", 82, p => FormatTime(p.TimeMs), dot),
            new ListViewColumn<ControlPointRow>("Value", 78, valueText),
            new ListViewColumn<ControlPointRow>("Meter", 44, p => p.Kind is ControlPointKind.Bpm or ControlPointKind.TimeSignature ? $"{p.Numerator}/{p.Denominator}" : string.Empty),
            new ListViewColumn<ControlPointRow>("Type", 48, kindText),
            new ListViewColumn<ControlPointRow>("Beat", 56, p => position(p.Pulse)),
        })
        {
            Position = new Vector2(246, 4 + DialogTabControl.HEADER_HEIGHT),
            Size = new Vector2(328, 290 - DialogTabControl.HEADER_HEIGHT),
        };

        var addKind = new ComboBox<NewPointKind>(Enum.GetValues<NewPointKind>())
        {
            Position = new Vector2(436, 300),
            Width = 80,
            Formatter = k => k switch
            {
                NewPointKind.Bpm => "BPM",
                NewPointKind.ScrollSpeed => "Speed",
                NewPointKind.Stop => "Stop",
                _ => "Meter",
            },
        };
        addKind.Current.BindTo(AddKind);

        var divisor = new ComboBox<int>(EditorColours.Divisor.Steps)
        {
            Position = new Vector2(488, 0),
            Width = 58,
            Formatter = d => $"1/{d}",
        };
        divisor.Current.Value = model.SnapDivisor.Value;
        divisor.Current.BindValueChanged(e => model.SnapDivisor.Value = e.NewValue);

        Body.Children = new Drawable[]
        {
            leftTabs,
            listTabs,
            list,
            new DialogButton("Property Transformer", () => OpenPropertyTransformer())
            {
                Position = new Vector2(4, 301),
                Size = new Vector2(238, 22),
            },
            new DialogCheckBox("Inherit previous timing settings").With(c =>
            {
                c.Position = new Vector2(250, 305);
                c.Current.BindTo(InheritPrevious);
            }),
            addKind,
            new DialogIconButton(FontAwesome.Solid.Plus, EditorColours.Dialog.AddGreen, () => AddPoint())
            {
                Position = new Vector2(520, 300),
            },
            new DialogIconButton(FontAwesome.Solid.Times, EditorColours.Dialog.RemoveRed, RemoveSelected)
            {
                Position = new Vector2(548, 300),
            },
            new GroupBox("When applying timing changes...", EditorColours.Dialog.Body)
            {
                Position = new Vector2(4, 326),
                Size = new Vector2(570, 64),
                Children = new Drawable[]
                {
                    new DialogCheckBox("Scale notes to new timing") { Position = new Vector2(0, 0) }.With(c => c.Current.BindTo(model.ScaleNotes)),
                    new DialogCheckBox("Snap notes to new timing") { Position = new Vector2(0, 21) }.With(c => c.Current.BindTo(model.SnapNotes)),
                    new DialogCheckBox("Move notes with the grid") { Position = new Vector2(176, 0) }.With(c => c.Current.BindTo(model.MoveNotesWithTiming)),
                    new DialogCheckBox("Adjust bookmarks and preview") { Position = new Vector2(176, 21) }.With(c => c.Current.BindTo(model.MoveExtrasWithOffset)),
                    DialogText.Label("Use beat snap divisor of").With(t => t.Position = new Vector2(352, 4)),
                    divisor,
                },
            },
            new DialogButton("OK", Accept)
            {
                IsDefault = true,
                Position = new Vector2(4, 398),
                Size = new Vector2(392, 36),
            },
            new DialogButton("Cancel", Cancel)
            {
                Position = new Vector2(402, 398),
                Size = new Vector2(172, 36),
            },
            // notices over the bottom of the list ("+" refused, ...), fade out by themselves
            statusBox = new Container
            {
                Position = new Vector2(250, 290),
                Origin = Anchor.BottomLeft,
                Width = 320,
                AutoSizeAxes = Axes.Y,
                Alpha = 0,
                Masking = true,
                CornerRadius = 2,
                BorderThickness = 1,
                BorderColour = EditorColours.Dialog.ListBorder,
                Children = new Drawable[]
                {
                    new osu.Framework.Graphics.Shapes.Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Body },
                    statusText = new TextFlowContainer(t =>
                    {
                        t.Font = EditorFont.Dialog();
                        t.Colour = EditorColours.Dialog.NoteRed;
                    })
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Horizontal = 6, Vertical = 4 },
                    },
                },
            },
        };

        listTabs.SelectedIndex.BindValueChanged(_ => refilter(), true);
        list.SelectedItems.BindCollectionChanged((_, _) => selectionChanged());

        // start on the BPM point active at the current time
        var active = Draft.Beat.Bpm.LastOrDefault(b => b.Y <= map.MsToPulse(now)) ?? Draft.Beat.Bpm.FirstOrDefault();
        list.Select(list.Items.FirstOrDefault(r => r.Source == active));

        foreach (var b in new IBindable[] { Value, OffsetField, signature, otherNumerator, otherDenominator, Slam, SlamTo, curved, curveA, curveB })
            b.BindValueChangedUntyped(writeFields);

        volume.Value = Math.Round(Draft.Volume * 100);
        previewStart.Value = Draft.PreviewOffsetMs;
        previewLength.Value = Draft.PreviewDurationMs;
        volume.BindValueChanged(e => Draft.Volume = e.NewValue / 100);
        previewStart.BindValueChanged(e => Draft.PreviewOffsetMs = (long)e.NewValue);
        previewLength.BindValueChanged(e => Draft.PreviewDurationMs = (long)e.NewValue);
    }

    // ---- pages -------------------------------------------------------------

    private Drawable createTimingPage() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Children = new Drawable[]
        {
            new GroupBox("General")
            {
                RelativeSizeAxes = Axes.X,
                Height = 128,
                Children = new Drawable[]
                {
                    valueLabel = DialogText.Label("BPM", bold: true).With(t => t.Position = new Vector2(0, 4)),
                    new NumericUpDown
                    {
                        Position = new Vector2(56, 0),
                        Width = 150,
                        Decimals = 3,
                        Increment = 1,
                        Current = Value,
                    },
                    DialogText.Label("Offset", bold: true).With(t => t.Position = new Vector2(0, 33)),
                    new NumericUpDown
                    {
                        Position = new Vector2(56, 29),
                        Width = 150,
                        Decimals = 0,
                        Increment = 1,
                        Current = OffsetField,
                    },
                    new DialogButton("Use Current Time", () => OffsetField.Value = now)
                    {
                        Position = new Vector2(0, 62),
                        Size = new Vector2(206, 38),
                    },
                },
            },
            signatureGroup = new GroupBox("Time Signature (metronome)")
            {
                Y = 136,
                RelativeSizeAxes = Axes.X,
                Height = 100,
                Children = new Drawable[]
                {
                    new DialogRadioButton<int>(signature, 3, "3 / 4 (waltz)") { Position = new Vector2(0, 6) },
                    new DialogRadioButton<int>(signature, 4, "4 / 4 (common)") { Position = new Vector2(0, 28) },
                    new DialogRadioButton<int>(signature, sig_other, "Other:") { Position = new Vector2(0, 52) },
                    new NumericUpDown { Position = new Vector2(62, 48), Width = 46, Current = otherNumerator },
                    DialogText.Label("/").With(t => t.Position = new Vector2(114, 52)),
                    new NumericUpDown { Position = new Vector2(126, 48), Width = 46, Current = otherDenominator },
                },
            },
            scrollGroup = new GroupBox("Scroll speed shape")
            {
                Y = 136,
                RelativeSizeAxes = Axes.X,
                Height = 100,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new DialogCheckBox("Jump to") { Position = new Vector2(0, 4) }.With(c => c.Current.BindTo(Slam)),
                    new NumericUpDown { Position = new Vector2(96, 0), Width = 80, Decimals = 2, Increment = 0.05, Current = SlamTo },
                    new DialogCheckBox("Curve") { Position = new Vector2(0, 34) }.With(c => c.Current.BindTo(curved)),
                    DialogText.Label("a").With(t => t.Position = new Vector2(70, 34)),
                    new NumericUpDown { Position = new Vector2(82, 30), Width = 56, Decimals = 2, Increment = 0.05, Current = curveA },
                    DialogText.Label("b").With(t => t.Position = new Vector2(144, 34)),
                    new NumericUpDown { Position = new Vector2(156, 30), Width = 56, Decimals = 2, Increment = 0.05, Current = curveB },
                    DialogText.Label("Jump = instant change at the point").With(t => t.Position = new Vector2(0, 62)),
                },
            },
            stopGroup = new GroupBox("Stop")
            {
                Y = 136,
                RelativeSizeAxes = Axes.X,
                Height = 100,
                Alpha = 0,
                Child = new TextFlowContainer(t =>
                {
                    t.Font = EditorFont.Dialog();
                    t.Colour = EditorColours.Dialog.Text;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Text = "The note stream freezes for the length (ms, field above). The music keeps playing.",
                },
            },
        },
    };

    private Drawable createAudioPage() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Child = new GroupBox("Music")
        {
            RelativeSizeAxes = Axes.X,
            Height = 160,
            Children = new Drawable[]
            {
                DialogText.Label("Volume %", bold: true).With(t => t.Position = new Vector2(0, 4)),
                new NumericUpDown { Position = new Vector2(96, 0), Width = 110, Current = volume, Increment = 5 },
                DialogText.Label("Preview", bold: true).With(t => t.Position = new Vector2(0, 36)),
                new NumericUpDown { Position = new Vector2(96, 32), Width = 110, Current = previewStart, Increment = 100 },
                new DialogButton("Use Current Time", () => previewStart.Value = Math.Max(0, now))
                {
                    Position = new Vector2(96, 60),
                    Size = new Vector2(110, 24),
                },
                DialogText.Label("Length", bold: true).With(t => t.Position = new Vector2(0, 96)),
                new NumericUpDown { Position = new Vector2(96, 92), Width = 110, Current = previewLength, Increment = 1000 },
            },
        },
    };

    private Drawable createStylePage() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Child = new GroupBox("Style")
        {
            RelativeSizeAxes = Axes.X,
            Height = 110,
            Child = new TextFlowContainer(t =>
            {
                t.Font = EditorFont.Dialog();
                t.Colour = EditorColours.Dialog.TextDisabled;
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Text = "Not in the chart model yet: kson has no per-point visual flags (osu! kiai). "
                       + "Background, camera and post effects are edited on the Camera and Effects tabs.",
            },
        },
    };

    // ---- list --------------------------------------------------------------

    private static Colour4? dot(ControlPointRow p) => p.Kind switch
    {
        ControlPointKind.Bpm => EditorColours.Dialog.DotRed,
        ControlPointKind.ScrollSpeed => EditorColours.Dialog.DotGreen,
        ControlPointKind.Stop => EditorColours.Dialog.DotBlue,
        _ => EditorColours.Dialog.DotGrey,
    };

    private static string valueText(ControlPointRow p)
    {
        switch (p.Kind)
        {
            case ControlPointKind.Bpm:
                return p.Value.ToString("F3", CultureInfo.InvariantCulture);

            case ControlPointKind.ScrollSpeed:
                var g = (GraphPoint)p.Source!;
                string v = "x" + g.V.ToString("F2", CultureInfo.InvariantCulture);
                if (g.Vf is double vf) v += ">" + vf.ToString("F2", CultureInfo.InvariantCulture);
                return g.Curve is { IsLinear: false } ? v + "~" : v;

            case ControlPointKind.Stop:
                return $"{p.Value:0} ms";

            default:
                return string.Empty;
        }
    }

    private static string kindText(ControlPointRow p) => p.Kind switch
    {
        ControlPointKind.Bpm => "BPM",
        ControlPointKind.ScrollSpeed => "Speed",
        ControlPointKind.Stop => "Stop",
        _ => "Meter",
    };

    private string position(long pulse)
    {
        var pos = map.PositionOf(Math.Max(0, pulse));
        return pos.PulseInBeat == 0 ? $"{pos.Measure + 1}:{pos.Beat + 1}" : $"{pos.Measure + 1}:{pos.Beat + 1}+{pos.PulseInBeat}";
    }

    /// <summary>mm:ss:mmm — the one time format of the UI (Core.Editing.UiFormat).</summary>
    public static string FormatTime(double ms) => SDVXEditor.Core.Editing.UiFormat.Time(ms);

    /// <summary>Rebuilds the rows from the draft (after any edit), keeping the selection by source object.</summary>
    private void refilter()
    {
        Draft.Sort();
        map = Draft.BuildMap();

        var selected = list.SelectedItems.Select(r => r.Source).ToHashSet(ReferenceEqualityComparer.Instance);
        IEnumerable<ControlPointRow> rows = ControlPointRow.Build(Draft.Beat, map);

        rows = listTabs.SelectedIndex.Value switch
        {
            1 => rows.Where(p => p.Kind != ControlPointKind.ScrollSpeed),
            2 => rows.Where(p => p.Kind == ControlPointKind.ScrollSpeed),
            _ => rows,
        };

        updatingFields = true;
        list.SelectedItems.Clear();
        list.Items.Clear();
        list.Items.AddRange(rows);
        foreach (var r in list.Items.Where(r => selected.Contains(r.Source)))
            list.SelectedItems.Add(r);
        updatingFields = false;
        selectionChanged();
    }

    private ControlPointRow? primary => list.SelectedItems.Count > 0 ? list.SelectedItems[0] : null;

    private void selectionChanged()
    {
        if (updatingFields || primary is not ControlPointRow row)
            return;

        updatingFields = true;

        valueLabel.Text = row.Kind switch
        {
            ControlPointKind.ScrollSpeed => "Speed",
            ControlPointKind.Stop => "ms",
            ControlPointKind.TimeSignature => "BPM",
            _ => "BPM",
        };
        signatureGroup.Alpha = row.Kind is ControlPointKind.Bpm or ControlPointKind.TimeSignature ? 1 : 0;
        scrollGroup.Alpha = row.Kind == ControlPointKind.ScrollSpeed ? 1 : 0;
        stopGroup.Alpha = row.Kind == ControlPointKind.Stop ? 1 : 0;

        switch (row.Source)
        {
            case GraphPoint g:
                Value.Value = g.V;
                Slam.Value = g.Vf.HasValue;
                SlamTo.Value = g.Vf ?? g.V;
                curved.Value = g.Curve is { IsLinear: false };
                curveA.Value = g.Curve?.A ?? 0.25;
                curveB.Value = g.Curve?.B ?? 0.75;
                break;

            case Stop:
                Value.Value = Math.Round(row.Value);
                break;

            default:
                Value.Value = map.BpmAt(row.Pulse);
                break;
        }

        OffsetField.Value = Math.Round(row.TimeMs);
        signature.Value = row.Numerator is 3 or 4 && row.Denominator == 4 ? row.Numerator : sig_other;
        otherNumerator.Value = row.Numerator;
        otherDenominator.Value = row.Denominator;

        updatingFields = false;
    }

    /// <summary>Writes the field values into the selected draft points (value fields to every selected row of the primary's kind, offset only for one row).</summary>
    private void writeFields()
    {
        if (updatingFields || primary is not ControlPointRow first)
            return;

        var rows = list.SelectedItems.Where(r => r.Kind == first.Kind).ToList();
        int numerator = signature.Value == sig_other ? (int)otherNumerator.Value : signature.Value;
        int denominator = signature.Value == sig_other ? (int)otherDenominator.Value : 4;

        foreach (var row in rows)
        {
            switch (row.Source)
            {
                case BpmChange b:
                    if (Value.Value > 0) b.Bpm = Value.Value;
                    setMeterAt(map.MeasureAt(b.Y), numerator, denominator);
                    break;

                case GraphPoint g:
                    g.V = Value.Value;
                    // a ticked "Jump to" is kept even while its value still equals the speed: dropping it here made the
                    // row reload untick the box at once, so a jump could never be set up (tick first, then the value)
                    g.Vf = Slam.Value ? SlamTo.Value : null;
                    g.Curve = curved.Value ? new Curve(curveA.Value, curveB.Value) : null;
                    break;

                case Stop s:
                    s.Length = Math.Max(1, (long)Math.Round(map.MsToPulse(map.PulseToMs(s.Y) + Math.Max(1, Value.Value))) - s.Y);
                    break;

                case TimeSignature t:
                    t.Numerator = Math.Max(1, numerator);
                    t.Denominator = Math.Max(1, denominator);
                    break;
            }
        }

        if (rows.Count == 1 && Math.Abs(OffsetField.Value - Math.Round(first.TimeMs)) >= 1)
            moveTo(first, OffsetField.Value);

        Scheduler.AddOnce(refilter);
    }

    /// <summary>Upserts the time signature starting at <paramref name="measure"/> (a BPM row's meter).</summary>
    private void setMeterAt(int measure, int numerator, int denominator)
    {
        var current = map.SignatureOfMeasure(measure);
        if (current.Numerator == numerator && current.Denominator == denominator) return;

        var existing = Draft.Beat.TimeSignatures.FirstOrDefault(t => t.MeasureIndex == measure);
        if (existing != null)
        {
            existing.Numerator = numerator;
            existing.Denominator = denominator;
        }
        else
        {
            Draft.Beat.TimeSignatures.Add(new TimeSignature(measure, numerator, denominator));
        }
    }

    /// <summary>Moves a row to an audio time. The first BPM point moves the song offset instead (osu!: the first red line is the offset).</summary>
    private void moveTo(ControlPointRow row, double ms)
    {
        switch (row.Source)
        {
            case BpmChange b when b == Draft.Beat.Bpm.OrderBy(x => x.Y).First():
                Draft.OffsetMs += (long)Math.Round(ms - row.TimeMs);
                break;

            case BpmChange b:
            {
                var others = new BeatInfo { Bpm = Draft.Beat.Bpm.Where(x => x != b).ToList(), TimeSignatures = Draft.Beat.TimeSignatures };
                b.Y = Math.Max(1, (long)Math.Round(new TimingMap(others, Draft.OffsetMs).MsToPulse(ms)));
                break;
            }

            case GraphPoint g:
                g.Y = Math.Max(0, (long)Math.Round(map.MsToPulse(ms)));
                break;

            case Stop s:
                s.Y = Math.Max(0, (long)Math.Round(map.MsToPulse(ms)));
                break;

            case TimeSignature t:
            {
                long p = Math.Max(0, (long)Math.Round(map.MsToPulse(ms)));
                int m = map.MeasureAt(p);
                if (p - map.MeasureStart(m) > map.MeasureLength(m) / 2) m++;
                t.MeasureIndex = Math.Max(0, m);
                break;
            }
        }
    }

    // ---- add / remove / copy / paste ---------------------------------------

    /// <summary>"+": a point of <see cref="AddKind"/> at the current time (inheriting the previous point's values when asked).</summary>
    public object? AddPoint(NewPointKind? kind = null, double? atMs = null)
    {
        var k = kind ?? AddKind.Value;
        long pulse = Math.Max(0, (long)Math.Round(map.MsToPulse(atMs ?? now)));
        bool inherit = InheritPrevious.Value;
        object added;

        // one point per kind and position: an existing one is selected instead, and the dialog says so
        // (it used to return silently - "+" seemed to do nothing when the playhead sat on a point, e.g. at 0:00)
        object? existing = k switch
        {
            NewPointKind.Bpm => Draft.Beat.Bpm.FirstOrDefault(b => b.Y == pulse),
            NewPointKind.ScrollSpeed => Draft.Beat.ScrollSpeed.FirstOrDefault(p => p.Y == pulse),
            NewPointKind.Stop => Draft.Beat.Stops.FirstOrDefault(st => st.Y == pulse),
            _ => Draft.Beat.TimeSignatures.FirstOrDefault(t => t.MeasureIndex == map.MeasureAt(pulse)),
        };

        if (existing != null)
        {
            string what = k switch
            {
                NewPointKind.Bpm => "A BPM point",
                NewPointKind.ScrollSpeed => "A scroll speed point",
                NewPointKind.Stop => "A stop",
                _ => $"A time signature for measure {map.MeasureAt(pulse) + 1}",
            };

            if (listTabs.SelectedIndex.Value != 0 && !list.Items.Any(r => r.Source == existing))
                listTabs.SelectedIndex.Value = 0;
            list.Select(list.Items.FirstOrDefault(r => r.Source == existing));
            ShowMessage($"{what} already exists at {FormatTime(map.PulseToMs(pulse))} (selected). Move the playhead to add another.");
            return null;
        }

        switch (k)
        {
            case NewPointKind.Bpm:
                added = new BpmChange(pulse, inherit ? map.BpmAt(pulse) : 120);
                Draft.Beat.Bpm.Add((BpmChange)added);
                break;

            case NewPointKind.ScrollSpeed:
                double v = inherit ? Graph.ValueAt(Draft.Beat.ScrollSpeed, pulse, 1.0) : 1.0;
                added = new GraphPoint(pulse, v);
                Draft.Beat.ScrollSpeed.Add((GraphPoint)added);
                break;

            case NewPointKind.Stop:
                var prevStop = Draft.Beat.Stops.LastOrDefault(s => s.Y <= pulse);
                added = new Stop(pulse, inherit && prevStop != null ? prevStop.Length : map.BeatLength(map.MeasureAt(pulse)));
                Draft.Beat.Stops.Add((Stop)added);
                break;

            default:
                int m = map.MeasureAt(pulse);
                var sig = map.SignatureOfMeasure(m);
                added = inherit ? new TimeSignature(m, sig.Numerator, sig.Denominator) : new TimeSignature(m, 4, 4);
                Draft.Beat.TimeSignatures.Add((TimeSignature)added);
                break;
        }

        refilter();
        list.Select(list.Items.FirstOrDefault(r => r.Source == added));
        return added;
    }

    /// <summary>Shows a notice over the bottom of the list for a few seconds.</summary>
    public void ShowMessage(string text)
    {
        LastMessage = text;
        statusText.Text = text;
        statusBox.ClearTransforms();
        statusBox.FadeIn(100).Then().Delay(5000).FadeOut(400);
    }

    public void RemoveSelected()
    {
        foreach (var r in list.SelectedItems.ToArray())
        {
            switch (r.Source)
            {
                // the chart keeps at least one BPM point
                case BpmChange b when Draft.Beat.Bpm.Count > 1: Draft.Beat.Bpm.Remove(b); break;
                case GraphPoint g: Draft.Beat.ScrollSpeed.Remove(g); break;
                case Stop s: Draft.Beat.Stops.Remove(s); break;
                case TimeSignature t: Draft.Beat.TimeSignatures.Remove(t); break;
            }
        }

        list.SelectedItems.Clear();
        refilter();
    }

    /// <summary>Selects the rows of these draft objects (smoke test, transformer).</summary>
    public void SelectSources(IEnumerable<object> sources)
    {
        var set = sources.ToHashSet(ReferenceEqualityComparer.Instance);
        list.SelectedItems.Clear();
        foreach (var r in list.Items.Where(r => set.Contains(r.Source!)))
            list.SelectedItems.Add(r);
    }

    public void CopySelected()
    {
        var points = list.SelectedItems.Select(r => r.Source).OfType<GraphPoint>().ToList();
        if (points.Count == 0) return;
        long from = points.Min(p => p.Y), to = points.Max(p => p.Y);
        clipboard = ScrollSpeedTools.Copy(points, from, to);
    }

    public void PasteAtCurrentTime()
    {
        if (clipboard == null) return;
        long at = Math.Max(0, (long)Math.Round(map.MsToPulse(now)));
        replaceScrollSpeed(ScrollSpeedTools.Paste(Draft.Beat.ScrollSpeed, clipboard, at));
    }

    /// <summary>Replaces the draft's scroll-speed points (Property Transformer, paste) and selects the new ones.</summary>
    public void ReplaceScrollSpeed(List<GraphPoint> graph) => replaceScrollSpeed(graph);

    private void replaceScrollSpeed(List<GraphPoint> graph)
    {
        var old = Draft.Beat.ScrollSpeed.ToHashSet(ReferenceEqualityComparer.Instance);
        Draft.Beat.ScrollSpeed.Clear();
        Draft.Beat.ScrollSpeed.AddRange(graph);
        refilter();
        SelectSources(graph.Where(p => !old.Contains(p)));
    }

    public PropertyTransformerDialog OpenPropertyTransformer()
    {
        var points = list.SelectedItems.Select(r => r.Source).OfType<GraphPoint>().ToList();
        double from, to;
        if (points.Count >= 2)
        {
            from = map.PulseToMs(points.Min(p => p.Y));
            to = map.PulseToMs(points.Max(p => p.Y));
        }
        else
        {
            from = now;
            long p0 = (long)Math.Round(map.MsToPulse(from));
            to = map.PulseToMs(p0 + map.MeasureLength(map.MeasureAt(Math.Max(0, p0))));
        }

        var dialog = new PropertyTransformerDialog(this, () => map, from, to, () => now, model.SnapDivisor.Value);
        overlay.Push(dialog);
        return dialog;
    }

    [Resolved]
    private DialogOverlay overlay { get; set; } = null!;

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.ControlPressed && !e.Repeat)
        {
            switch (KeyboardLayoutService.Logical(e.Key)) // the keys labelled C / V on the active layout
            {
                case Key.C:
                    CopySelected();
                    return true;

                case Key.V:
                    PasteAtCurrentTime();
                    return true;
            }
        }

        if (e.Key == Key.Delete)
        {
            RemoveSelected();
            return true;
        }

        return base.OnKeyDown(e);
    }

    /// <summary>Switches the right list tab (smoke test).</summary>
    public void ShowListTab(int index) => listTabs.SelectedIndex.Value = index;

    /// <summary>True when OK changed the chart (smoke test).</summary>
    public bool Applied { get; private set; }

    protected override void OnAccept()
    {
        Draft.Sort();
        Applied = model.ApplyDraft(Draft, "Timing and Control Points");
        model.Message?.Invoke(Applied ? "Timing changes applied (one undo step)" : "Timing unchanged");
    }
}

internal static class BindableExtensions
{
    public static void BindValueChangedUntyped(this IBindable bindable, Action action)
    {
        switch (bindable)
        {
            case Bindable<double> d: d.BindValueChanged(_ => action()); break;
            case Bindable<int> i: i.BindValueChanged(_ => action()); break;
            case Bindable<bool> b: b.BindValueChanged(_ => action()); break;
            default: throw new ArgumentException("unsupported bindable");
        }
    }
}
