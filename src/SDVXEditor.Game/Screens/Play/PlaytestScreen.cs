using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Audio;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.LevelEstimation;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Screens.Edit;

namespace SDVXEditor.Game.Screens.Play;

/// <summary>
/// osu!-style test play (F5) of the chart being edited. Plays the in-memory chart — nothing is saved — from a
/// short lead-in before the editor's position, at the editor's playback rate and with the universal audio offset.
/// The song track is borrowed from the editor's transport and handed back on exit, and the editor resumes where the
/// test stopped. Rules live in Core (<see cref="GameplayJudge"/>); this screen wires devices, sound, renderer and HUD.
/// <para>
/// Controls (editor.ini, <see cref="EditorSetting.PlaytestBindings"/>): BT D F J K, FX C M, lasers W/E and O/P,
/// Enter pause, Esc results/back; Tab autoplay, F3/F4 or -/+ scroll speed, ` retry. Controllers: buttons as
/// <c>JoyN</c> bindings, knobs as joystick axes (<see cref="EditorSetting.PlaytestKnobAxes"/>) or, in mouse mode,
/// as mouse X/Y (the cursor is hidden and confined, which puts the framework into relative mouse mode).
/// </para>
/// </summary>
public partial class PlaytestScreen : Screen
{
    /// <summary>Real-time lead-in before the editor position.</summary>
    public const double LEAD_IN_MS = 2000;

    /// <summary>Real-time wait after entering before the clock starts (fade-in).</summary>
    public const double START_DELAY_MS = 600;

    private readonly EditorSession? session;
    private readonly EditorClock? editorClock;

    [Resolved]
    private EditorConfigManager config { get; set; } = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    private readonly Bindable<double> modSpeed = new Bindable<double>();
    private readonly Bindable<bool> autoplaySetting = new Bindable<bool>();
    private readonly Bindable<bool> hitsoundSetting = new Bindable<bool>();
    private readonly Bindable<bool> assistTick = new Bindable<bool>();
    private readonly Bindable<double> effectsVolume = new Bindable<double>();

    /// <summary>Gauge of the run (editor.ini <see cref="EditorSetting.PlaytestGauge"/>, F2 cycles and restarts).</summary>
    public readonly Bindable<GaugeType> Gauge = new Bindable<GaugeType>();

    private PlayBindings bindings = new PlayBindings();
    private readonly Dictionary<Key, List<PlayAction>> keyActions = new Dictionary<Key, List<PlayAction>>();
    private readonly Dictionary<int, List<PlayAction>> joyActions = new Dictionary<int, List<PlayAction>>();
    private readonly HashSet<PlayAction> heldActions = new HashSet<PlayAction>();

    /// <summary>True while a bound input of <paramref name="action"/> is held (smoke test: a rebind reaches the playtest).</summary>
    public bool IsHeld(PlayAction action) => heldActions.Contains(action);
    private JudgementWindows windows = JudgementWindows.Default;
    private double knobSensitivity = 1, keyLaserSpeed = 4;
    private int knobAxisL, knobAxisR = 1;
    private bool mouseKnobs;

    private Chart chart = null!;
    private Track? track;
    private double rate = 1, audioOffset, editorStartMs, startMs, countFromMs;
    private HiSpeedBasis hiSpeedBasis = new HiSpeedBasis(120, HiSpeedBasisSource.ModeBpm);
    private bool unsaved;

    private PlaytestClock clock = null!;
    private PlayableChart playable = null!;
    private GameplayJudge judge = null!;
    private PlaytestPlayfield playfield = null!;

    // the background takes the VOX post effects of stage 0 (ColorConversion), the playfield those of stage 1
    private static readonly Colour4 background_top = Colour4.FromHex("1d1236");
    private static readonly Colour4 background_bottom = Colour4.FromHex("04030a");
    private Box background = null!;
    private readonly osu.Framework.Graphics.Containers.Container playfieldLayer = new osu.Framework.Graphics.Containers.Container { RelativeSizeAxes = Axes.Both };
    private PlaytestHud hud = null!;
    private PlaytestResults? results;
    private HitsoundPlayer? hitsounds;
    private readonly EditorClock soundClock = new EditorClock();

    private double enteredAt = double.NaN;
    private double lastClockFrame;
    private bool paused, autoplayUsed, exiting;
    private double? estimatedLevel;
    private double stoppedAtMs;

    // mouse-mode knobs: with the cursor hidden and confined the framework switches to relative (raw) mouse input,
    // whose position is not clamped to the window, so the deltas keep coming at any knob angle
    private Vector2 mouseDelta;
    private CursorState? previousCursor;

    /// <summary>The judge of the current run (smoke test, HUD).</summary>
    public GameplayJudge Judge => judge;

    public PlaytestPlayfield Playfield => playfield;
    public PlaytestHud Hud => hud;
    public PlaytestClock PlayClock => clock;
    public bool ShowingResults => results != null;
    public double StartMs => startMs;
    public double EditorStartMs => editorStartMs;

    /// <summary>Hi-speed forced for this screen only, not stored (smoke test stress run).</summary>
    public double? HiSpeedOverride { get; set; }

    /// <summary>The BPM the mod speed is divided by (std BPM, else the chart's most common tempo).</summary>
    public HiSpeedBasis HiSpeedBasis => hiSpeedBasis;

    /// <summary>Hi-speed in effect: the override, else mod speed ÷ <see cref="HiSpeedBasis"/>.</summary>
    public double HiSpeed => HiSpeedOverride ?? hiSpeedBasis.HiSpeedFor(modSpeed.Value);

    /// <summary>Real-time ms the clock has been running (smoke test).</summary>
    public double RunningMs { get; private set; }

    /// <summary>Frames while running and their longest / mean real-time length (smoke report).</summary>
    public int RunningFrames { get; private set; }

    public double MaxFrameMs { get; private set; }
    public double MeanFrameMs => RunningFrames == 0 ? 0 : RunningMs / RunningFrames;

    /// <param name="session">The editor's session (its chart is tested in memory). Null shows an empty track.</param>
    /// <param name="editorClock">The editor's transport: start position, rate, offset and the song track.</param>
    /// <summary>
    /// The editor's audio effects on the borrowed song (null from song select). While the test runs it follows the
    /// player's FX buttons and laser cursors; autoplay plays the chart's effects.
    /// </summary>
    public SDVXEditor.Game.Audio.EffectAudioHost? EffectAudio { get; init; }

    public PlaytestScreen(EditorSession? session = null, EditorClock? editorClock = null)
    {
        this.session = session;
        this.editorClock = editorClock;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        config.BindWith(EditorSetting.PlaytestModSpeed, modSpeed);
        config.BindWith(EditorSetting.PlaytestAutoplay, autoplaySetting);
        config.BindWith(EditorSetting.PlaytestHitsounds, hitsoundSetting);
        config.BindWith(EditorSetting.PlaytestAssistTick, assistTick);
        config.BindWith(EditorSetting.VolumeEffects, effectsVolume);
        config.BindWith(EditorSetting.PlaytestGauge, Gauge);

        readInputSettings();

        chart = session?.Chart ?? emptyChart();
        unsaved = session?.IsDirty ?? false;
        rate = editorClock?.Rate.Value ?? 1;
        audioOffset = editorClock?.AudioOffset.Value ?? 0;
        editorStartMs = editorClock?.CurrentTime.Value ?? 0;

        var timing = session?.Timing ?? Core.Timing.TimingMap.FromChart(chart);
        hiSpeedBasis = HiSpeedBasis.Of(chart, timing);

        // borrow the song track from the editor; it is handed back in OnExiting
        if (editorClock != null)
        {
            track = editorClock.Track;
            editorClock.IsPlaying.Value = false;
            editorClock.Track = null;
        }

        startMs = editorStartMs - LEAD_IN_MS * rate;
        // the first second of the lead-in is free: objects there are drawn but not counted
        countFromMs = startMs + 1000 * rate;

        string title = string.IsNullOrEmpty(chart.Meta.Title) ? "Untitled" : chart.Meta.Title;
        string lvl = chart.Meta.Level.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        hud = new PlaytestHud($"{title}  [{chart.Meta.DifficultyDisplayName} {lvl}]");

        if (session != null)
        {
            hitsounds = new HitsoundPlayer(session, soundClock, effectsVolume);
            soundClock.Rate.Value = Math.Clamp(rate, soundClock.Rate.MinValue, soundClock.Rate.MaxValue);
        }

        InternalChildren = new Drawable[]
        {
            background = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientVertical(background_top, background_bottom),
            },
        };

        if (hitsounds != null) AddInternal(hitsounds);
        AddInternal(playfieldLayer);
        AddInternal(hud);

        // level estimate off the update thread (a dense chart takes a moment)
        var snapshot = chart;
        Task.Run(() =>
        {
            try
            {
                var r = DifficultyEstimator.Default.Estimate(snapshot);
                Schedule(() => estimatedLevel = r.EstimatedLevel);
            }
            catch (Exception e)
            {
                Logger.Log($"playtest: level estimate failed: {e.Message}");
            }
        });
    }

    private static Chart emptyChart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        return c;
    }

    private void readInputSettings()
    {
        bindings = PlayBindings.Parse(config.Get<string>(EditorSetting.PlaytestBindings));
        keyActions.Clear();
        joyActions.Clear();
        foreach (var (input, action) in bindings.Pairs)
        {
            if (PlayBindings.JoystickButtonOf(input) is int button)
                add(joyActions, button, action);
            else if (Enum.TryParse<Key>(input, true, out var key))
                add(keyActions, key, action);
            else
                Logger.Log($"playtest: unknown input \"{input}\" for {PlayBindings.KeyOf(action)}");
        }

        windows = JudgementWindows.Parse(config.Get<string>(EditorSetting.PlaytestWindows));
        knobSensitivity = config.Get<double>(EditorSetting.PlaytestKnobSensitivity);
        keyLaserSpeed = config.Get<double>(EditorSetting.PlaytestKeyLaserSpeed);
        mouseKnobs = config.Get<bool>(EditorSetting.PlaytestMouseKnobs);
        var axes = config.Get<string>(EditorSetting.PlaytestKnobAxes).Split(',');
        if (axes.Length > 0 && int.TryParse(axes[0].Trim(), out int a0)) knobAxisL = a0;
        if (axes.Length > 1 && int.TryParse(axes[1].Trim(), out int a1)) knobAxisR = a1;

        static void add<T>(Dictionary<T, List<PlayAction>> d, T k, PlayAction a) where T : notnull
        {
            if (!d.TryGetValue(k, out var list)) d[k] = list = new List<PlayAction>();
            list.Add(a);
        }
    }

    /// <summary>(Re)starts the test from the lead-in: fresh judgement state, clock at the start.</summary>
    protected override void LoadComplete()
    {
        base.LoadComplete();

        // not in load(): the run seeks the borrowed track, and TrackBass.Seek waits for the audio thread. On the async
        // load thread that wait held the screen's load lock, so a game stopping meanwhile (the audio thread gone) left
        // the seek waiting forever and disposing the screen deadlocked on the lock (EditorScreen had the same problem).
        startRun();
    }

    private void startRun()
    {
        playfieldLayer.Clear();
        results?.Expire();
        results = null;

        playable = new PlayableChart(chart, session?.Timing, countFromMs);
        judge = new GameplayJudge(playable, windows.ForRate(rate), autoplaySetting.Value, Gauge.Value);
        judge.Judged += onJudged;
        autoplayUsed = judge.Autoplay;

        clock?.Stop();
        clock = new PlaytestClock(track, rate, audioOffset, startMs);
        if (track != null)
            clock.Seek(startMs);

        playfield = new PlaytestPlayfield(playable, judge) { SongTime = startMs };
        playfieldLayer.Add(playfield);

        heldActions.Clear();
        paused = false;
        RunningMs = 0;
        RunningFrames = 0;
        MaxFrameMs = 0;
        enteredAt = double.NaN;
        stoppedAtMs = startMs;
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        this.FadeInFromZero(150);

        if (mouseKnobs && host.Window != null)
        {
            previousCursor = host.Window.CursorState;
            host.Window.CursorState = CursorState.HiddenAndConfined;
        }

        if (unsaved)
            Logger.Log("playtest: the chart has unsaved changes; testing the in-memory chart");
    }

    public override bool OnExiting(ScreenExitEvent e)
    {
        if (!exiting)
        {
            exiting = true;
            double stop = results != null ? stoppedAtMs : clock.CurrentTime;
            clock.Stop();
            soundClock.IsPlaying.Value = false;
            if (EffectAudio != null) EffectAudio.Input.ChartDriven = true;

            if (previousCursor is { } c && host.Window != null)
                host.Window.CursorState = c;

            // hand the track back and resume the editor where the test stopped
            if (editorClock != null)
            {
                editorClock.CurrentTime.Value = Math.Clamp(stop, 0, editorClock.LengthMs);
                editorClock.Track = track;
            }
        }

        this.FadeOut(150);
        return base.OnExiting(e);
    }

    // ---- frame ------------------------------------------------------------

    /// <summary>Song time now, extrapolated within the frame for input that arrives between clock updates.</summary>
    private double inputTime => clock.IsRunning ? clock.CurrentTime + (Time.Current - lastClockFrame) * rate : clock.CurrentTime;

    protected override void Update()
    {
        base.Update();

        if (double.IsNaN(enteredAt)) enteredAt = Time.Current;
        if (!clock.IsRunning && !paused && results == null && Time.Current - enteredAt >= START_DELAY_MS)
            clock.Start();

        double elapsed = Time.Elapsed;
        clock.Update(elapsed);
        lastClockFrame = Time.Current;
        if (clock.IsRunning)
        {
            RunningMs += elapsed;
            RunningFrames++;
            if (RunningFrames > 5) MaxFrameMs = Math.Max(MaxFrameMs, elapsed);
        }

        double t = clock.CurrentTime;

        if (clock.IsRunning)
        {
            feedLaserInput(t, elapsed);
            judge.Update(t);
        }

        double hiSpeed = HiSpeed;
        playfield.SongTime = t;
        playfield.HiSpeed = hiSpeed;
        var bgStage = playfield.Effects.Background;
        background.Colour = bgStage.Conversions.Count == 0
            ? ColourInfo.GradientVertical(background_top, background_bottom)
            : ColourInfo.GradientVertical(PlayfieldEffects.Convert(bgStage, background_top), PlayfieldEffects.Convert(bgStage, background_bottom));

        // sounds: autoplay and the assist tick use the clock-driven player (it starts samples ahead of time)
        soundClock.CurrentTime.Value = Math.Max(0, t);
        soundClock.IsPlaying.Value = clock.IsRunning && t >= 0;
        if (hitsounds != null) hitsounds.Enabled.Value = hitsoundSetting.Value && (judge.Autoplay || assistTick.Value);
        feedEffects();

        if (results == null && clock.IsRunning && judge.Finished && t > playable.LastObjectMs + 1500 * rate)
            showResults(true);

        // hard gauge at 0 % = TRACK CRASH: the run stops and the summary says where (autoplay never fails)
        if (results == null && judge.Score.Failed)
            showResults(false);

        long pulse = (long)Math.Max(0, playable.Timing.MsToPulse(t));
        double end = Math.Max(playable.LastObjectMs, startMs + 1);
        string? banner = !clock.IsRunning && results == null && !paused ? "READY" : paused ? "PAUSED — Enter to resume" : null;
        hud.Refresh(new PlaytestHudState(judge.Score, playable.Timing.BpmAt(pulse), hiSpeed, modSpeed.Value, hiSpeedBasis, HiSpeedOverride != null, rate, audioOffset,
            estimatedLevel, (t - startMs) / (end - startMs), judge.Autoplay, paused, unsaved, banner));
    }

    /// <summary>The player's FX buttons and laser cursors → the effect DSP (autoplay = the chart decides).</summary>
    private void feedEffects()
    {
        if (EffectAudio == null) return;
        if (hitsounds != null) hitsounds.ApplySlamVolume.Value = EffectAudio.Enabled.Value;
        var input = EffectAudio.Input;
        input.FxDown[0] = judge.Held[PlayLanes.FxL];
        input.FxDown[1] = judge.Held[PlayLanes.FxR];
        input.LaserOn[0] = judge.Locked[0];
        input.LaserOn[1] = judge.Locked[1];
        // an on-time press is heard from the hold start: the audio thread renders this far ahead
        input.GraceMs = 30 + EffectAudio.LeadMs;
        input.ChartDriven = judge.Autoplay;
    }

    private void feedLaserInput(double t, double elapsedReal)
    {
        // keys: constant cursor speed while held
        double step = keyLaserSpeed * elapsedReal / 1000;
        for (int side = 0; side < 2; side++)
        {
            int dir = (heldActions.Contains(side == 0 ? PlayAction.LaserLRight : PlayAction.LaserRRight) ? 1 : 0)
                      - (heldActions.Contains(side == 0 ? PlayAction.LaserLLeft : PlayAction.LaserRLeft) ? 1 : 0);
            if (dir != 0) judge.MoveLaser(side, dir * step, t);
        }

        // mouse mode: X = left knob, Y = right knob
        var m = mouseDelta;
        mouseDelta = Vector2.Zero;
        if (mouseKnobs && m != Vector2.Zero)
        {
            const double pixels_per_width = 300;
            if (m.X != 0) judge.MoveLaser(0, m.X / pixels_per_width * knobSensitivity, t);
            if (m.Y != 0) judge.MoveLaser(1, m.Y / pixels_per_width * knobSensitivity, t);
        }
    }

    // ---- judgements: sound + feedback ---------------------------------

    private void onJudged(JudgementEvent e)
    {
        var o = e.Object;
        bool sounding = hitsoundSetting.Value && hitsounds != null && !judge.Autoplay && !assistTick.Value;

        switch (o.Kind)
        {
            case ScoreObjectKind.Chip:
                playfield.Flash(o.Lane, e.Result);
                hud.ShowJudgement(e);
                if (sounding && e.Result != HitResult.Error)
                {
                    if (o.Note?.KeySound is { } ks && PlayLanes.IsFx(o.Lane)) hitsounds!.PlayKeySound(ks.Name, ks.Volume);
                    else hitsounds!.PlaySlot(PlayLanes.IsFx(o.Lane) ? HitsoundSlot.FxChip : HitsoundSlot.BtChip);
                }
                break;

            case ScoreObjectKind.Slam:
                playfield.Flash(o.Lane, e.Result, e.Result == HitResult.Error ? o.Slam.From : o.Slam.To, o.Laser?.Section.Width ?? 1);
                hud.ShowJudgement(e);
                if (sounding && e.Result != HitResult.Error) hitsounds!.PlaySlot(HitsoundSlot.Slam, o.Pulse);
                break;

            default:
                if (e.Result == HitResult.Error) hud.ShowJudgement(e);
                break;
        }
    }

    private void showResults(bool reachedEnd)
    {
        if (results != null) return;
        stoppedAtMs = clock.CurrentTime;
        clock.Stop();
        soundClock.IsPlaying.Value = false;
        AddInternal(results = new PlaytestResults(judge.Score, chart.Meta.Title, autoplayUsed, reachedEnd, Math.Max(0, stoppedAtMs), judge.Score.MeanOffsetMs));
        results.FadeInFromZero(200);
        Logger.Log($"playtest: score {judge.Score.Score} ex {judge.Score.ExScore}/{judge.Score.MaxExScore} chain {judge.Score.MaxChain}/{judge.Score.TotalUnits} errors {judge.Score.Errors} (autoplay {autoplayUsed})");
    }

    // ---- public controls (keys and the smoke test) ------------------------

    /// <param name="persist">Also store it as the default for the next test (editor.ini); the smoke test does not.</param>
    public void SetAutoplay(bool on, bool persist = true)
    {
        judge.Autoplay = on;
        if (persist) autoplaySetting.Value = on;
        autoplayUsed |= on;
    }

    public void AdjustSpeed(int direction) => modSpeed.Value = Math.Clamp(modSpeed.Value + 25 * direction, 50, 2000);

    public void TogglePause()
    {
        if (results != null) return;
        paused = !paused;
        if (paused) clock.Stop();
        else clock.Start();
    }

    /// <summary>Esc: first the result summary, then back to the editor.</summary>
    public void Back()
    {
        if (results == null) showResults(false);
        else this.Exit();
    }

    public void Retry() => startRun();

    /// <summary>F2: normal → easy → hard → normal (stored for the next test), and the run restarts with it.</summary>
    public void CycleGauge()
    {
        Gauge.Value = (GaugeType)(((int)Gauge.Value + 1) % Enum.GetValues<GaugeType>().Length);
        startRun();
    }

    /// <summary>One line on the scroll geometry at the current song time (smoke probe).</summary>
    public string DescribeScroll()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var timing = playable.Timing;
        double t = clock.CurrentTime;
        double pulse = timing.MsToPulse(t);
        double hiSpeed = HiSpeed;
        var st = playfield.LastState;
        var g = playfield.MeasureGeometry(pulse);
        return string.Create(inv,
            $"playtest probe at {t:0} ms: pulse {pulse:0}, BPM {timing.BpmAt(pulse):0.##}, basis {hiSpeedBasis.Label} {hiSpeedBasis.Bpm:0.##}, mod speed {modSpeed.Value:0}, hi-speed {hiSpeed:0.###}, "
            + $"lane {PlaytestPlayfield.BASE_BEATS / Math.Max(0.05, hiSpeed):0.##} beats, scroll speed {timing.ScrollSpeedAt(pulse):0.###}, "
            + $"camera zoom_top {st.ZoomTop:0.#} (pitch {st.PitchDeg:0.##} deg) zoom_bottom {st.ZoomBottom:0.#} (distance x{st.DistanceFactor:0.###}) roll {st.RollDeg:0.#} deg; "
            + $"on screen ({playfield.DrawWidth:0}x{playfield.DrawHeight:0} px): lane width at the judgement line {g.LaneWidthPx:0} px, "
            + $"1/16 {g.SixteenthPx:0.#} px ({g.SixteenthPx / Math.Max(1, g.LaneWidthPx):0.###} lane widths), 1 beat {g.BeatPx:0} px, lane end {g.FarWidthPx:0} px wide");
    }

    // ---- input ----------------------------------------------------------

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Repeat) return keyActions.ContainsKey(e.Key) || base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Tab:
                SetAutoplay(!judge.Autoplay);
                return true;

            case Key.F3:
            case Key.Minus:
            case Key.KeypadMinus:
                AdjustSpeed(-1);
                return true;

            case Key.F4:
            case Key.Plus:
            case Key.KeypadPlus:
                AdjustSpeed(1);
                return true;

            case Key.Tilde:
                Retry();
                return true;

            case Key.F2:
                CycleGauge();
                return true;
        }

        if (keyActions.TryGetValue(e.Key, out var actions))
        {
            foreach (var a in actions) press(a);
            return true;
        }

        // Esc always leaves, even when it was rebound
        if (e.Key == Key.Escape)
        {
            Back();
            return true;
        }

        return base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyUpEvent e)
    {
        if (keyActions.TryGetValue(e.Key, out var actions))
            foreach (var a in actions) release(a);
        base.OnKeyUp(e);
    }

    protected override bool OnJoystickPress(JoystickPressEvent e)
    {
        int button = (int)e.Button - (int)JoystickButton.FirstButton;
        if (!joyActions.TryGetValue(button, out var actions)) return base.OnJoystickPress(e);
        foreach (var a in actions) press(a);
        return true;
    }

    protected override void OnJoystickRelease(JoystickReleaseEvent e)
    {
        int button = (int)e.Button - (int)JoystickButton.FirstButton;
        if (joyActions.TryGetValue(button, out var actions))
            foreach (var a in actions) release(a);
        base.OnJoystickRelease(e);
    }

    protected override bool OnJoystickAxisMove(JoystickAxisMoveEvent e)
    {
        int axis = (int)e.Axis.Source;
        int side = axis == knobAxisL ? 0 : axis == knobAxisR ? 1 : -1;
        if (side < 0 || !clock.IsRunning) return base.OnJoystickAxisMove(e);

        // knobs report an absolute angle that wraps around (−1 … 1 per turn)
        double d = e.Axis.Value - e.LastValue;
        if (d > 1) d -= 2;
        else if (d < -1) d += 2;
        judge.MoveLaser(side, d * knobSensitivity, inputTime);
        return true;
    }

    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        if (mouseKnobs) mouseDelta += e.Delta;
        return base.OnMouseMove(e);
    }

    // relative mouse positions drift outside the screen in mouse mode; keep receiving them
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

    private void press(PlayAction a)
    {
        if (!heldActions.Add(a)) return;

        switch (a)
        {
            case PlayAction.Start:
                if (results != null) this.Exit();
                else TogglePause();
                return;

            case PlayAction.Back:
                Back();
                return;
        }

        if (results != null || paused) return;
        double t = inputTime;

        if (PlayBindings.LaneOf(a) is int lane)
        {
            judge.Press(lane, t);
            if (hitsoundSetting.Value && hitsounds != null && !judge.Autoplay && !assistTick.Value && holdStartsNear(lane, t))
                hitsounds.PlaySlot(PlayLanes.IsFx(lane) ? HitsoundSlot.FxHold : HitsoundSlot.BtHold);
            return;
        }

        // a laser key: register the turn at the press time (slams), the held key keeps moving in Update
        int side = a is PlayAction.LaserLLeft or PlayAction.LaserLRight ? 0 : 1;
        int dir = a is PlayAction.LaserLRight or PlayAction.LaserRRight ? 1 : -1;
        judge.MoveLaser(side, dir * 0.02, t);
    }

    private void release(PlayAction a)
    {
        if (!heldActions.Remove(a)) return;
        if (PlayBindings.LaneOf(a) is int lane)
            judge.Release(lane, inputTime);
    }

    private bool holdStartsNear(int lane, double t)
    {
        foreach (var h in playable.Holds[lane])
        {
            if (h.Ms > t + windows.NearMs * rate) break;
            if (Math.Abs(h.Ms - t) <= windows.NearMs * rate) return true;
        }

        return false;
    }
}
