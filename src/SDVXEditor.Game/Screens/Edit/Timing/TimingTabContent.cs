using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.IO.Stores;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;

namespace SDVXEditor.Game.Screens.Edit.Timing;

/// <summary>
/// Tap-tempo: BPM from the average interval of consecutive taps. A pause longer than
/// <see cref="RESET_AFTER_MS"/> starts a new measurement.
/// </summary>
public sealed class TapTempo
{
    public const double RESET_AFTER_MS = 2000;
    public const int MAX_TAPS = 16;

    /// <summary>Taps needed before the result is offered for applying.</summary>
    public const int CONFIRM_AFTER = 4;

    private readonly List<double> taps = new List<double>();

    public int TapCount => taps.Count;

    /// <summary>Records a tap at <paramref name="timeMs"/> (any monotonic clock) and returns the BPM, or null after the first tap.</summary>
    public double? Tap(double timeMs)
    {
        if (taps.Count > 0 && timeMs - taps[^1] > RESET_AFTER_MS)
            taps.Clear();

        taps.Add(timeMs);
        if (taps.Count > MAX_TAPS)
            taps.RemoveAt(0);

        if (taps.Count < 2)
            return null;

        double avg = (taps[^1] - taps[0]) / (taps.Count - 1);
        return avg > 0 ? 60_000 / avg : null;
    }

    public void Reset() => taps.Clear();
}

/// <summary>Tap tempo result shared by the tap block (top right) and the panel's confirm button.</summary>
public sealed class TapState
{
    public readonly TapTempo Tempo = new TapTempo();
    public readonly Bindable<double?> Bpm = new Bindable<double?>();
    public readonly BindableInt Count = new BindableInt();

    public bool CanApply => Bpm.Value != null && Count.Value >= TapTempo.CONFIRM_AFTER;

    public void Tap(double timeMs)
    {
        double? bpm = Tempo.Tap(timeMs);
        Count.Value = Tempo.TapCount;
        Bpm.Value = bpm is double b ? Math.Round(b, 2) : null;
    }

    public void Reset()
    {
        Tempo.Reset();
        Count.Value = 0;
        Bpm.Value = null;
    }
}

/// <summary>
/// The dark centre panel of the timing tab (reference 03): BPM and offset steppers (they edit the
/// chart through the undo stack), the yellow "Timing Setup Panel" button (F6), the two move
/// options, base scroll speed, metronome clicks and the swinging metronome pill.
/// </summary>
public partial class TimingTabContent : CompositeDrawable
{
    public const float PANEL_WIDTH = 900;
    public const float PANEL_HEIGHT = 695;

    private readonly TimingViewModel model;
    private readonly EditorClock playback;
    private readonly Action openTimingSetup;
    private readonly TapState tap;

    private Container metronomeBall = null!;
    private TextFlowContainer note = null!;
    private Container tapConfirm = null!;
    private SpriteText tapConfirmText = null!;

    public TimingTabContent(TimingViewModel model, EditorClock playback, Action openTimingSetup, TapState tap)
    {
        this.model = model;
        this.playback = playback;
        this.openTimingSetup = openTimingSetup;
        this.tap = tap;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        OsuStepper bpmStepper, offsetStepper, baseStepper;

        InternalChild = new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Y = 53,
            Size = new Vector2(PANEL_WIDTH, PANEL_HEIGHT),
            Masking = true,
            CornerRadius = 12,
            EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
            {
                Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                Colour = Colour4.Black.Opacity(0.4f),
                Radius = 12,
            },
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.PanelDark },
                label("BPM:", 96),
                bpmStepper = new OsuStepper
                {
                    Position = new Vector2(298, 75),
                    Current = model.Bpm,
                    Step = 1,
                    DragStepPerPixel = 0.05,
                    Formatter = v => v.ToString("0.###", CultureInfo.InvariantCulture),
                },
                note = new TextFlowContainer(t =>
                {
                    t.Font = EditorFont.Regular(22);
                    t.Colour = Colour4.White;
                })
                {
                    Position = new Vector2(52, 132),
                    Width = 570,
                    AutoSizeAxes = Axes.Y,
                },
                new FlatButton("Timing Setup Panel", EditorColours.Osu.TimingSetupTop, EditorColours.Osu.TimingSetupBottom, 28)
                {
                    Position = new Vector2(637, 80),
                    Size = new Vector2(187, 99),
                    Icon = FontAwesome.Solid.Tools,
                    TextColour = Colour4.FromHex("3a2600"), // dark on yellow (white was barely readable, audit U8)
                    TooltipText = "All BPM and scroll speed points of the chart (F6)",
                    Action = openTimingSetup,
                },
                tapConfirm = new Container
                {
                    Position = new Vector2(637, 196),
                    Size = new Vector2(187, 70),
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new FlatButton("Use tapped BPM", EditorColours.Osu.TapBlue, EditorColours.Osu.TapBlue.Darken(0.15f), 20)
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 40,
                            Action = () => ApplyTap(),
                        },
                        tapConfirmText = new SpriteText
                        {
                            Y = 44,
                            Font = EditorFont.Regular(19),
                            Colour = Colour4.White,
                        },
                    },
                },
                label("Offset:", 241),
                offsetStepper = new OsuStepper
                {
                    Position = new Vector2(298, 220),
                    Current = model.Offset,
                    Step = 1,
                    DragStepPerPixel = 1,
                    Formatter = v => v.ToString("0", CultureInfo.InvariantCulture) + " ms",
                },
                new PinkCheckbox("Move placed notes when the offset/BPM changes")
                {
                    Position = new Vector2(55, 286),
                }.With(c => c.Current.BindTo(model.MoveNotesWithTiming)),
                new PinkCheckbox("Move scroll-speed points, preview and bookmarks with the offset/BPM")
                {
                    Position = new Vector2(55, 334),
                }.With(c => c.Current.BindTo(model.MoveExtrasWithOffset)),
                label("Base scroll speed:", 440),
                baseStepper = new OsuStepper
                {
                    Position = new Vector2(298, 419),
                    Current = model.BaseScrollSpeed,
                    Step = 0.05,
                    DragStepPerPixel = 0.005,
                    Formatter = v => "x" + v.ToString("0.00", CultureInfo.InvariantCulture),
                },
                new PinkCheckbox("Metronome clicks", 22)
                {
                    Position = new Vector2(620, 430),
                }.With(c => c.Current.BindTo(model.MetronomeClicks)),
                createMetronome(),
            },
        };

        gestures(bpmStepper, "Change BPM");
        gestures(offsetStepper, "Change offset");
        gestures(baseStepper, "Change base scroll speed");
        Tutorial.TutorialAnchors.Register("timing.bpm", bpmStepper); Tutorial.TutorialAnchors.Register("timing.offset", offsetStepper); Tutorial.TutorialAnchors.Register("timing.base-speed", baseStepper);
    }

    private void gestures(OsuStepper stepper, string description)
    {
        stepper.GestureStarted += () => model.BeginGesture(description);
        stepper.GestureEnded += model.EndGesture;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        model.MoveNotesWithTiming.BindValueChanged(_ => updateNote());
        model.ControlPoints.BindCollectionChanged((_, _) => updateNote(), true);
        tap.Bpm.BindValueChanged(_ => updateTap());
        tap.Count.BindValueChanged(_ => updateTap(), true);
    }

    /// <summary>Applies the tapped BPM to the BPM point at the current time (Enter / confirm button).</summary>
    public bool ApplyTap()
    {
        if (tap.Bpm.Value is not double bpm) return false;
        bool done = model.ApplyBpm(bpm);
        model.Message?.Invoke(done ? $"BPM set to {bpm:0.##} (tap tempo)" : "Tap tempo: BPM unchanged");
        tap.Reset();
        return done;
    }

    private void updateTap()
    {
        tapConfirm.FadeTo(tap.CanApply ? 1 : 0, 100);
        if (tap.Bpm.Value is double b)
            tapConfirmText.Text = $"{b:0.##} BPM, {tap.Count.Value} taps (Enter)";
    }

    private void updateNote()
    {
        int count = model.ControlPoints.Count(p => p.Kind == ControlPointKind.Bpm);
        var active = model.ActiveBpmPoint();
        int index = active == null ? 0 : model.ControlPoints.Where(p => p.Kind == ControlPointKind.Bpm).TakeWhile(p => p.Source != active).Count() + 1;
        string where = count > 1 ? $"BPM point {index} of {count} (the one at the current time)" : "the song's BPM";
        note.Text = model.MoveNotesWithTiming.Value
            ? $"Edits {where}. Notes keep their beat and move with the grid."
            : $"Edits {where}. Notes keep their time and are re-snapped to the new grid (F6 sets the divisor).";
    }

    private double lastNoteUpdate;

    private static Drawable label(string text, float centreY) => new SpriteText
    {
        Anchor = Anchor.TopLeft,
        Origin = Anchor.CentreRight,
        Position = new Vector2(286, centreY),
        Text = text,
        Font = EditorFont.Regular(27),
        Colour = Colour4.White,
    };

    private Drawable createMetronome()
    {
        const float width = 716, height = 100;

        var dots = new Container { RelativeSizeAxes = Axes.Both };
        foreach (float x in new[] { 50f, width / 2, width - 50 })
        {
            dots.Add(new Circle
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.Centre,
                X = x,
                Size = new Vector2(18),
                Colour = Colour4.White,
            });
        }

        return new HintContainer
        {
            Position = new Vector2(97, 552),
            Size = new Vector2(width, height),
            Masking = true,
            CornerRadius = height / 2,
            TooltipText = "Metronome: the knob swings once per beat of the chart's tempo while the song plays",
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(Colour4.FromHex("8a8a8a"), Colour4.FromHex("cfcfcf")),
                },
                dots,
                metronomeBall = new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.Centre,
                    X = 50,
                    Size = new Vector2(height),
                    Masking = true,
                    CornerRadius = height / 2,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("555555").Opacity(0.75f) },
                },
            },
        };
    }

    protected override void Update()
    {
        base.Update();

        // swing left/right once per beat of the chart's own tempo map
        if (model.Session is EditorSession s)
        {
            double pulse = s.MsToPulse(playback.CurrentTime.Value);
            double beats = pulse / Core.Charts.Timebase.PulsesPerQuarter;
            float t = (float)(0.5 - 0.5 * Math.Cos(Math.PI * beats));
            metronomeBall.X = 50 + t * (716 - 100);
        }

        // the active BPM point changes while seeking
        if (Time.Current - lastNoteUpdate > 250)
        {
            lastNoteUpdate = Time.Current;
            updateNote();
        }
    }
}

/// <summary>
/// Top-right block of the timing tab: orange Reset (re-tap), four metronome lamps that follow the
/// beat while playing (downbeat green, other beats white) and flash on taps, and "Tap here!".
/// Plays synthesised metronome clicks when enabled. Enter applies a tapped BPM.
/// </summary>
public partial class TapTempoBlock : CompositeDrawable
{
    private static readonly Colour4 downbeat = Colour4.FromHex("66ff66");
    private static readonly Colour4 offbeat = Colour4.White;

    private readonly TimingViewModel model;
    private readonly EditorClock playback;
    private readonly TapState tap;
    private readonly Box[] lamps = new Box[4];

    private ISampleStore? samples;
    private Sample? tick, tock;
    private (int Measure, int Beat)? lastBeat;

    /// <summary>Metronome clicks played (smoke test).</summary>
    public int Clicks { get; private set; }

    /// <summary>Applies the tapped BPM (set by the timing tab, Enter).</summary>
    public Func<bool>? ApplyTap { get; set; }

    public TapTempoBlock(TimingViewModel model, EditorClock playback, TapState tap)
    {
        this.model = model;
        this.playback = playback;
        this.tap = tap;
        RelativeSizeAxes = Axes.Both;
        Padding = new MarginPadding { Vertical = 6, Right = 4 };
    }

    private IBindable<double>? effectsVolume; // kept alive: see EditorScreen.musicVolume

    [BackgroundDependencyLoader]
    private void load(AudioManager audio, EditorConfigManager config)
    {
        samples = audio.GetSampleStore(new ResourceStore<byte[]>(new MetronomeSamples()));
        samples.AddAdjustment(AdjustableProperty.Volume, effectsVolume = config.GetBindable<double>(EditorSetting.VolumeEffects));
        tick = samples.Get("tick");
        tock = samples.Get("tock");

        var lampFlow = new FillFlowContainer<Container>
        {
            Position = new Vector2(132, 12),
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(10, 0),
        };

        for (int i = 0; i < lamps.Length; i++)
        {
            lampFlow.Add(new Container
            {
                Size = new Vector2(64, 46),
                Masking = true,
                CornerRadius = 3,
                Child = lamps[i] = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
            });
        }

        InternalChild = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 8,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("3a3a3a").Opacity(0.85f) },
                new FlatButton("Reset", EditorColours.Osu.ResetOrange, EditorColours.Osu.ResetOrange.Darken(0.15f))
                {
                    Position = new Vector2(14, 12),
                    Size = new Vector2(104, 112),
                    TooltipText = "Forget the taps and start counting again",
                    Action = Reset,
                },
                // the four black boxes had no explanation (audit U8)
                new HintContainer
                {
                    Position = lampFlow.Position,
                    AutoSizeAxes = Axes.Both,
                    TooltipText = "Beat lamps: light up on each beat while playing (green = first beat of a measure) and flash on every tap",
                    Child = lampFlow.With(f => f.Position = Vector2.Zero),
                },
                new FlatButton("Tap here!", EditorColours.Osu.TapBlue, EditorColours.Osu.TapBlue.Darken(0.1f))
                {
                    Position = new Vector2(132, 78),
                    Size = new Vector2(286, 46),
                    TextColour = Colour4.FromHex("06263a"), // dark on light blue (audit U8)
                    TooltipText = "Tap in time with the music: after 4 taps the BPM can be applied (Enter)",
                    Action = () => Tap(Time.Current),
                },
            },
        };
    }

    /// <summary>Registers a tap at <paramref name="timeMs"/>; from the fourth tap on the BPM can be applied.</summary>
    public void Tap(double timeMs)
    {
        tap.Tap(timeMs);

        int lamp = (tap.Count.Value - 1) % lamps.Length;
        lamps[lamp].FlashColour(Colour4.FromHex("ffe066"), 250);
    }

    /// <summary>Re-tap: forgets the taps so far.</summary>
    public void Reset()
    {
        tap.Reset();
        foreach (var l in lamps)
            l.FadeColour(Colour4.Black, 100);
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Key.Enter or Key.KeypadEnter && !e.Repeat && tap.Bpm.Value != null)
            return ApplyTap?.Invoke() ?? false;

        return base.OnKeyDown(e);
    }

    protected override void Update()
    {
        base.Update();

        if (model.Session is not EditorSession s || !playback.IsPlaying.Value)
        {
            if (lastBeat != null)
            {
                lastBeat = null;
                foreach (var l in lamps) l.FadeColour(Colour4.Black, 150);
            }

            return;
        }

        long pulse = (long)Math.Floor(s.MsToPulse(playback.CurrentTime.Value));
        if (pulse < 0) return;

        var pos = s.Timing.PositionOf(pulse);
        var beat = (pos.Measure, pos.Beat);
        if (beat == lastBeat) return;

        bool first = lastBeat == null;
        lastBeat = beat;

        for (int i = 0; i < lamps.Length; i++)
        {
            bool lit = i == pos.Beat % lamps.Length;
            lamps[i].FadeColour(lit ? (pos.Beat == 0 ? downbeat : offbeat) : Colour4.Black, lit ? 0 : 120);
        }

        // click only on beat crossings during playback, not when starting in the middle of one
        if (!first && model.MetronomeClicks.Value)
        {
            (pos.Beat == 0 ? tick : tock)?.Play();
            Clicks++;
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        samples?.Dispose();
        base.Dispose(isDisposing);
    }
}

/// <summary>Synthesised metronome clicks: "tick" (downbeat, 1.6 kHz) and "tock" (1 kHz), 30 ms each. Own work.</summary>
public sealed class MetronomeSamples : IResourceStore<byte[]>
{
    private const int rate = 44100;

    private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>
    {
        ["tick"] = wav(click(1600)),
        ["tock"] = wav(click(1000)),
    };

    private static float[] click(double freq)
    {
        int n = rate * 30 / 1000;
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            s[i] = (float)(Math.Exp(-t * 180) * Math.Sin(2 * Math.PI * freq * t));
        }

        return s;
    }

    private static byte[] wav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (float f in samples)
            w.Write((short)Math.Clamp(f * 0.7f * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    private byte[]? find(string name) =>
        files.TryGetValue(Path.GetFileNameWithoutExtension(name), out var b) ? b : null;

    public byte[] Get(string name) => find(name)!;

    public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

    public Stream? GetStream(string name) => find(name) is { } b ? new MemoryStream(b) : null;

    public IEnumerable<string> GetAvailableResources() => files.Keys.Select(k => k + ".wav");

    public void Dispose()
    {
    }
}
