using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Backgrounds;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Input;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Effects;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Timing;
using SDVXEditor.Game.Screens.Play;
using SDVXEditor.Game.Screens.SongSelect;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// Editor chrome after the osu!stable editor (reference 02/03), laid out in the
/// 1920x1080 design space of the UI scaling container: menu bar, tab strip over the
/// timeline band, per-tab centre content, toolboxes, bottom bar, background layer.
/// </summary>
public partial class EditorScreen : Screen
{
    public const float CONTENT_TOP = EditorMenuBar.HEIGHT + TimelineArea.HEIGHT;

    /// <summary>Transport (audio clock) of the editor.</summary>
    public readonly EditorClock Playback = new EditorClock();

    /// <summary>The loaded song, active chart, undo stack and selection.</summary>
    public EditorSession Session { get; }

    /// <summary>Compose tools (also driven by the smoke test).</summary>
    public ComposeEditor Compose { get; private set; } = null!;

    public ComposePlayfield Playfield { get; private set; } = null!;
    public DensityColumn Density { get; private set; } = null!;
    public TimelineRuler Ruler { get; private set; } = null!;
    public SummaryMarkers Summary { get; private set; } = null!;
    public HitsoundPlayer Hitsounds { get; private set; } = null!;
    public readonly TimingViewModel Timing = new TimingViewModel();
    public readonly EditorToolState Tools = new EditorToolState();

    /// <summary>Bookmark times in ms, a read-only view of the chart's bookmarks (every change goes through the undo stack).</summary>
    public readonly BindableList<double> Bookmarks = new BindableList<double>();

    /// <summary>Receiver of chart-model actions (undo, copy, mirror …).</summary>
    public IEditorChartActions ChartActions { get; set; } = new StubChartActions();

    public EditorScreen(EditorSession? session = null)
    {
        Session = session ?? EditorSession.CreateEmpty();
    }

    private EditorToast toast = null!;
    private VolumeOverlay volume = null!;

    [Resolved]
    private DialogOverlay dialogs { get; set; } = null!;

    private EditorTabStrip tabStrip = null!;
    private TimelineArea timeline = null!;
    private BeatDivisorControl divisorControl = null!;
    private TapTempoBlock tapBlock = null!;
    private ComposeToolbox toolbox = null!;

    /// <summary>The compose toolbox (tool tiles, laser L | R switch).</summary>
    public ComposeToolbox Toolbox => toolbox;
    private ComposeToggleBar toggles = null!;

    private readonly Dictionary<EditorTab, Drawable> pages = new Dictionary<EditorTab, Drawable>();

    public EditorMenuBar MenuBar { get; private set; } = null!;

    /// <summary>"Support the Artist" at the right end of the menu bar (hidden without song sources).</summary>
    public SupportArtistButton SupportButton { get; private set; } = null!;

    /// <summary>The editor's key binding container (smoke test: feeds it key events to check rebinds).</summary>
    public EditorKeyBindingContainer KeyBindings { get; private set; } = null!;

    [Resolved(canBeNull: true)]
    private Overlays.Options.OptionsOverlay? optionsOverlay { get; set; }
    public BackgroundLayer Background { get; private set; } = null!;
    public ComposeTabContent ComposeContent { get; private set; } = null!;
    public CameraTabContent CameraContent { get; private set; } = null!;
    public EffectsTabContent EffectsContent { get; private set; } = null!;

    /// <summary>The chart's audio effects on the song (effects tab checkbox, editor.ini <c>PlayEffects</c>).</summary>
    public SDVXEditor.Game.Audio.EffectAudioHost EffectAudio { get; private set; } = null!;
    public TimelineArea Timeline => timeline;
    public EditorBottomBar BottomBar { get; private set; } = null!;

    public EditorTab CurrentTab => tabStrip.Current.Value;

    /// <summary>The beat snap divisor block of the timeline (smoke test: slider and value must not overlap).</summary>
    public BeatDivisorControl DivisorControl => divisorControl;

    /// <summary>The tab lit over the current one (song setup while its dialog is open), null otherwise (smoke test).</summary>
    public EditorTab? LitTab => tabStrip.Lit.Value as EditorTab?;

    /// <summary>The volume overlay keeps clear of the right tool column (Hit Sounds sits at its bottom).</summary>
    public const float VOLUME_OVERLAY_INSET = 110;

    [Resolved]
    private AudioManager audio { get; set; } = null!;

    [Resolved]
    private EditorConfigManager config { get; set; } = null!;

    /// <summary>Held in a field: an adjustment source that only the audio side references weakly can be collected.</summary>
    private IBindable<double>? musicVolume;

    [Resolved]
    private osu.Framework.Configuration.FrameworkConfigManager frameworkConfig { get; set; } = null!;

    [BackgroundDependencyLoader]
    private void load()
    {
        divisorControl = new BeatDivisorControl();
        tapBlock = new TapTempoBlock(Timing, Playback, tapState) { Alpha = 0 };
        Compose = new ComposeEditor(Session, Tools) { Message = t => showToast(t) };

        pages[EditorTab.Compose] = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                ComposeContent = new ComposeTabContent(),
                toolbox = new ComposeToolbox(Tools) { Y = 30 },
                toggles = new ComposeToggleBar
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Position = new Vector2(-8, 43),
                    WideLaserClicked = () => ToggleWideLaser(),
                },
            },
        };
        Hitsounds = new HitsoundPlayer(Session, Playback, config.GetBindable<double>(EditorSetting.VolumeEffects));
        EffectAudio = new SDVXEditor.Game.Audio.EffectAudioHost(Session, Playback, () => loadedTrack);
        pages[EditorTab.Camera] = CameraContent = new CameraTabContent(Session, Playback, Compose, Tools, Zoom) { Message = t => showToast(t) };
        pages[EditorTab.Timing] = timingContent = new TimingTabContent(Timing, Playback, () => OpenTimingSetup(), tapState);
        pages[EditorTab.Effects] = EffectsContent = new EffectsTabContent(Session, Playback, Compose, Tools, Zoom, Hitsounds) { Message = t => showToast(t) };

        var contentArea = new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding { Top = CONTENT_TOP, Bottom = EditorBottomBar.HEIGHT },
        };

        foreach (var page in pages.Values)
        {
            page.Alpha = 0;
            contentArea.Add(page);
        }

        InternalChild = KeyBindings = new EditorKeyBindingContainer
        {
            Children = new Drawable[]
            {
                new EditorActionReceiver(handleAction),
                Background = new BackgroundLayer(),
                contentArea,
                timeline = new TimelineArea { Y = EditorMenuBar.HEIGHT },
                tabStrip = new EditorTabStrip { Y = EditorMenuBar.HEIGHT },
                BottomBar = new EditorBottomBar(Playback, Playtest),
                toast = new EditorToast { Y = -EditorBottomBar.HEIGHT - 20 },
                volume = new VolumeOverlay { Y = -EditorBottomBar.HEIGHT, X = -VOLUME_OVERLAY_INSET }, // left of the tool column: it covered Hit Sounds (audit U16)
                MenuBar = new EditorMenuBar { Items = createMenu() },
                // right end of the menu bar: quiet, only there when the song has sources (Song Setup → Sources)
                SupportButton = new SupportArtistButton(SupportButtonStyle.Light)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    X = -6,
                    LightFontSize = EditorMenuBar.FONT_SIZE,
                    SongTitle = () => Session.Chart.Meta.Title,
                },
            },
        };

        timeline.RightBlock.AddRange(new Drawable[] { divisorControl, tapBlock });

        // ---- chart views
        Compose.Divisor.BindTo(divisorControl.Divisor);
        Compose.GridSnap.BindTo(toggles.GridSnap);
        Compose.WideLaser.BindTo(toggles.WideLaser);
        Compose.LockNotes.BindTo(toggles.LockNotes);

        timeline.Zoom.BindTo(Zoom);
        ComposeContent.PlayfieldContainer.Add(Playfield = new ComposePlayfield(Session, Playback, Compose, Tools, Zoom));
        ComposeContent.DensityContainer.Add(Density = new DensityColumn(Session, Playback, visibleRange));
        timeline.RulerContent.Add(Ruler = new TimelineRuler(Session, Playback, Zoom, divisorControl.Divisor, timeline.UnderRuler, snapMs));
        BottomBar.SummaryContent.Add(Summary = new SummaryMarkers(Session, Playback, Bookmarks));
        AddInternal(Hitsounds);
        AddInternal(EffectAudio);
        config.BindWith(EditorSetting.PlayEffects, EffectsContent.PlayEffects);
        EffectAudio.Enabled.BindTo(EffectsContent.PlayEffects);
        Hitsounds.ApplySlamVolume.BindTo(EffectsContent.PlayEffects);
        EffectsContent.PreviewEffect = (name, laser) => EffectAudio.Preview(name, laser);
        config.BindWith(EditorSetting.Hitsounds, toggles.Hitsounds);
        config.BindWith(EditorSetting.HoldModifier, Tools.HoldModifier);
        config.BindWith(EditorSetting.DragSnapModifier, Tools.DragSnapModifier);
        Hitsounds.Enabled.BindTo(toggles.Hitsounds);

        ChartActions = new EditorChartActions(Session, Compose, () => Playback.CurrentTime.Value, showToast)
        {
            ChartReplaced = onChartReplaced,
        };
        setupCollab(); // EditorScreen.Collab.cs
        setupBottomBar(); // EditorScreen.BottomBar.cs
        setupMenu(); // EditorScreen.Menu.cs (Ctrl+O / new difficulty need dialogs)
        setupFeatures(); // EditorScreen.Features.cs (side panels, chart check, toolbox drop-downs, loop)

        // ---- audio
        // BindWith binds to the stored bindable. GetBindable() returns a bound copy that only weak
        // references keep alive: after a GC the binding was gone (offset ignored, smoke test flaky).
        config.BindWith(EditorSetting.AudioOffset, Playback.AudioOffset);
        loadedTrack = Session.LoadTrack(audio);
        loadedTrack?.AddAdjustment(AdjustableProperty.Volume, musicVolume = config.GetBindable<double>(EditorSetting.VolumeMusic));
        // the track is handed to the clock in LoadComplete, not here: the clock seeks it, and TrackBass.Seek waits for the
        // audio thread. On this async load thread that wait held the screen's load lock; when the game stopped meanwhile
        // (a failed smoke test, closing the window during a load) the audio thread was gone, the seek never returned and
        // disposing the screen blocked on the lock forever - the process stayed alive with a frozen window.

        setupTiming();
        onChartReplaced();

        if (Session.StartTrackTimeMs is double startAt)
            Playback.CurrentTime.Value = Math.Max(0, startAt - Playback.AudioOffset.Value);

        Background.Source.Value = GradientBackgroundSource.FromHue(0.86f);
        Background.Dim.Value = 0.15f;
    }

    private osu.Framework.Audio.Track.Track? loadedTrack;

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (loadedTrack != null)
            Playback.Track = loadedTrack;

        EditorTab previous = EditorTab.Compose;

        tabStrip.Current.BindValueChanged(e =>
        {
            if (e.NewValue == EditorTab.SongSetup)
            {
                // a dialog, not a page: open it and stay where we were
                Schedule(() => tabStrip.Current.Value = previous);
                OpenSongSetup();
                return;
            }

            previous = e.NewValue;

            foreach (var (tab, page) in pages)
                page.FadeTo(tab == e.NewValue ? 1 : 0, 120);

            bool timing = e.NewValue == EditorTab.Timing;
            divisorControl.FadeTo(timing ? 0 : 1, 120);
            tapBlock.FadeTo(timing ? 1 : 0, 120);
        }, true);

        // the menu's shortcut column follows rebinds made in the options
        EditorKeyBindings.Changed += onBindingsChanged;
        updateClassicKey();

        // the support button follows the song sources (Song Setup, undo / redo, chart switch)
        Session.ChartChanged += onSourcesMaybeChanged;
        onSourcesMaybeChanged();
    }

    private void onSourcesMaybeChanged()
    {
        if (!ReferenceEquals(SupportButton.Sources.Value, Session.Chart.Editor.SongSources))
            SupportButton.Sources.Value = Session.Chart.Editor.SongSources;
    }

    private void onBindingsChanged() => Schedule(() =>
    {
        updateClassicKey();
        MenuBar.Items = createMenu();
        refreshReferenceMenu();
    });

    /// <summary>Opens the options overlay (File → Options…, Ctrl+Shift+O by default).</summary>
    public void OpenOptions() => optionsOverlay?.Open();

    protected override void Dispose(bool isDisposing)
    {
        EditorKeyBindings.Changed -= onBindingsChanged;
        Session.ChartChanged -= onSourcesMaybeChanged;
        teardownFeatures();
        base.Dispose(isDisposing);
    }

    protected override void Update()
    {
        base.Update();

        Playback.Update(Clock.ElapsedFrameTime);
        pollModifierKeys();

        // the waveform needs the audio length, known once BASS has opened the file
        if (Playback.Track is { IsLoaded: true } t && Math.Abs(trackLengthShown - t.Length) > 1)
        {
            trackLengthShown = t.Length;
            Ruler.SetAudio(Session.AudioPath, Session.LoadWaveform, t.Length);
        }

        long pulse = (long)Math.Floor(Session.MsToPulse(Playback.CurrentTime.Value));
        var pos = Session.Timing.PositionOf(Math.Max(0, pulse));
        divisorControl.SetPositionText($"{pos.Measure + 1}:{pos.Beat + 1}");
    }

    private double trackLengthShown;

    /// <summary>Time range the playfield shows (for the density column's viewport frame).</summary>
    private (double, double) visibleRange()
    {
        float px = Compose.PxPerMs;
        float h = Playfield.DrawHeight;
        double now = Playback.CurrentTime.Value;
        const float below = PlayfieldLayout.JUDGEMENT_FROM_BOTTOM;
        return (now - below / px, now + (h - below) / px);
    }

    /// <summary>Snaps a time to the current beat divisor (seeks while paused land on ticks, like osu!).</summary>
    private double snapMs(double ms)
    {
        long pulse = Math.Max(0, (long)Math.Round(Session.MsToPulse(ms)));
        return Math.Max(0, Session.PulseToMs(Session.Timing.Snap(pulse, divisorControl.Divisor.Value)));
    }

    /// <summary>After loading, switching or reverting the chart: refresh the view models built from it.</summary>
    private void onChartReplaced()
    {
        updateChartLength(); // EditorScreen.BottomBar.cs
        Timing.Refresh();
        reloadBookmarks();
        onCollabChartReplaced();
    }

    // ---- actions -----------------------------------------------------------

    public void SelectTab(EditorTab tab) => tabStrip.Current.Value = tab;

    public void SelectTool(EditorTool tool) => Tools.Tool.Value = tool;

    public TimingControlPointsDialog OpenTimingSetup()
    {
        if (dialogs.Current is TimingControlPointsDialog open)
            return open;

        var dialog = new TimingControlPointsDialog(Timing, Playback);
        dialogs.Push(dialog);
        return dialog;
    }

    public SongSetupDialog OpenSongSetup()
    {
        if (dialogs.Current is SongSetupDialog open)
            return open;

        var dialog = new SongSetupDialog(Session, Playback, showToast);
        dialog.Closed += () => tabStrip.Lit.Value = null;
        dialogs.Push(dialog);
        tabStrip.Lit.Value = EditorTab.SongSetup;
        return dialog;
    }

    public AudioOffsetDialog OpenAudioOffset()
    {
        if (dialogs.Current is AudioOffsetDialog open)
            return open;

        var dialog = new AudioOffsetDialog(Hitsounds);
        dialogs.Push(dialog);
        return dialog;
    }

    public void Playtest()
    {
        Playback.IsPlaying.Value = false;
        // like osu!: nothing is saved; the in-memory chart is tested, with a warning when it differs from the file
        if (Session.IsDirty)
            showToast("Unsaved changes: testing the chart in memory (not saved)");
        this.Push(new PlaytestScreen(Session, Playback) { EffectAudio = EffectAudio });
    }

    /// <summary>Seeks <paramref name="direction"/> snap ticks of the current divisor, on the chart's tempo map.</summary>
    private void seekBySnap(int direction)
    {
        var timing = Session.Timing;
        int divisor = divisorControl.Divisor.Value;
        long pulse = timing.Snap(Math.Max(0, (long)Math.Round(Session.MsToPulse(Playback.CurrentTime.Value))), divisor);

        for (int i = 0; i < Math.Abs(direction); i++)
            pulse = direction > 0 ? timing.NextSnap(pulse, divisor) : pulse > 0 ? timing.PreviousSnap(pulse, divisor) : 0;

        Playback.CurrentTime.Value = Math.Clamp(Session.PulseToMs(Math.Max(0, pulse)), 0, Playback.LengthMs);
    }

    // ---- input -------------------------------------------------------------

    /// <summary>The one editor zoom (1 = default): timeline ruler/waveform and playfield spacing both follow it, each with its own px/ms factor. ± buttons and Alt+wheel over timeline or playfield change it.</summary>
    public readonly BindableFloat Zoom = TimelineArea.CreateZoom();

    /// <summary>Runs an action as if its key had been pressed (menu items, smoke test).</summary>
    public bool TriggerAction(EditorAction action) => handleAction(action, false);

    /// <summary>Last toast text (smoke test checks that stubbed actions answer).</summary>
    public string LastToast => toast.LastMessage;

    public ShortcutsDialog OpenShortcuts()
    {
        if (dialogs.Current is ShortcutsDialog open)
            return open;

        var dialog = new ShortcutsDialog();
        dialogs.Push(dialog);
        return dialog;
    }

    /// <summary>What Alt+wheel changes at a screen position: over the timeline or the playfield the shared zoom, anything else the volume.</summary>
    public AltWheelTarget AltWheelTargetAt(Vector2 screenSpacePosition)
    {
        if (timeline.ScreenSpaceDrawQuad.Contains(screenSpacePosition))
            return AltWheelTarget.Timeline;

        if (CurrentTab == EditorTab.Compose && ComposeContent.PlayfieldContainer.ScreenSpaceDrawQuad.Contains(screenSpacePosition))
            return AltWheelTarget.Playfield;

        return AltWheelTarget.Volume;
    }

    public void AdjustAltWheel(AltWheelTarget target, int direction)
    {
        switch (target)
        {
            case AltWheelTarget.Timeline:
            case AltWheelTarget.Playfield:
                // one shared zoom: timeline ruler/waveform and playfield spacing zoom together
                Zoom.Value += direction * TimelineArea.ZOOM_STEP;
                break;

            default:
                volume.Adjust(direction);
                break;
        }
    }

    /// <summary>
    /// The Wide Laser toggle (W, the toggle bar tile, Compose menu): flips the width for new lasers and, with lasers
    /// selected, makes them wide / normal too (one undo step, on-screen positions kept; narrowing clamps what lies
    /// beyond the lanes and says so).
    /// </summary>
    public void ToggleWideLaser()
    {
        toggles.WideLaser.Toggle();
        if (ChartActions is EditorChartActions actions && actions.SetSelectionWidth(toggles.WideLaser.Value)) return;
        showToast($"Wide Laser {(toggles.WideLaser.Value ? "on" : "off")}");
    }

    private bool handleAction(EditorAction action, bool repeat)
    {
        // only movement-like actions auto-repeat
        bool repeatable = action is EditorAction.SeekBackward or EditorAction.SeekForward
            or EditorAction.SeekBackwardFar or EditorAction.SeekForwardFar
            or EditorAction.DivisorNext or EditorAction.DivisorPrevious
            or EditorAction.AltWheelUp or EditorAction.AltWheelDown
            or EditorAction.NudgeBackward or EditorAction.NudgeForward
            or EditorAction.MoveSelectionLaneLeft or EditorAction.MoveSelectionLaneRight
            or EditorAction.MoveSelectionEarlier or EditorAction.MoveSelectionLater;

        // an action while the hold modifier is down (Ctrl+Z, Ctrl+wheel, Alt+wheel …) uses it up until it is released;
        // the classic-laser key is held, not pressed: its own action does nothing (pollModifierKeys reads it)
        Tools.OtherInput(classicKey: action == EditorAction.ClassicLaserHold);
        if (action == EditorAction.ClassicLaserHold) return true;

        if (repeat && !repeatable)
            return true;

        if (handleFeatureAction(action)) // EditorScreen.Features.cs
            return true;

        switch (action)
        {
            case EditorAction.TabCompose: SelectTab(EditorTab.Compose); return true;
            case EditorAction.TabCamera: SelectTab(EditorTab.Camera); return true;
            case EditorAction.TabTiming: SelectTab(EditorTab.Timing); return true;
            case EditorAction.TabEffects: SelectTab(EditorTab.Effects); return true;
            case EditorAction.SongSetup: OpenSongSetup(); return true;
            case EditorAction.Playtest: Playtest(); return true;
            case EditorAction.TimingSetup: OpenTimingSetup(); return true;
            case EditorAction.ShowShortcuts: OpenShortcuts(); return true;
            case EditorAction.ShowOptions: OpenOptions(); return true;
            case EditorAction.Exit when Compose.Placement != null: Compose.CancelPlacement(); return true; // Esc cancels a laser draft first
            case EditorAction.Exit when ReferenceSession != null: CloseReference(); return true;
            // like osu!: Esc closes what is open first (dialogs, menus, popups and the options take the key before it
            // gets here), and leaving asks Save / Discard / Cancel when the chart has unsaved changes
            case EditorAction.Exit: ExitWithPrompt(); return true;

            case EditorAction.PlayPause: Playback.IsPlaying.Toggle(); return true;

            case EditorAction.PlayFromStart:
                Playback.CurrentTime.Value = 0;
                Playback.IsPlaying.Value = true;
                return true;

            case EditorAction.SeekStart: Playback.CurrentTime.Value = 0; return true;
            case EditorAction.SeekEnd: Playback.CurrentTime.Value = Playback.LengthMs; return true;
            case EditorAction.SeekBackward: seekBySnap(-1); return true;
            case EditorAction.SeekForward: seekBySnap(1); return true;
            case EditorAction.SeekBackwardFar: seekBySnap(-4); return true;
            case EditorAction.SeekForwardFar: seekBySnap(4); return true;
            case EditorAction.PreviousTimingPoint: seekToPoint(Timing.ControlPoints.Select(p => p.TimeMs), -1); return true;
            case EditorAction.NextTimingPoint: seekToPoint(Timing.ControlPoints.Select(p => p.TimeMs), 1); return true;

            case EditorAction.PlaybackRateUp: changeRate(0.25); return true;
            case EditorAction.PlaybackRateDown: changeRate(-0.25); return true;
            case EditorAction.PlaybackRateUpFine: changeRate(0.05); return true;
            case EditorAction.PlaybackRateDownFine: changeRate(-0.05); return true;

            case EditorAction.DivisorNext: divisorControl.Nudge(1); return true;
            case EditorAction.DivisorPrevious: divisorControl.Nudge(-1); return true;
            case >= EditorAction.Divisor1 and <= EditorAction.Divisor9: setDivisor(action - EditorAction.Divisor1 + 1); return true;

            case EditorAction.AltWheelUp:
            case EditorAction.AltWheelDown:
                var mouse = GetContainingInputManager()?.CurrentState.Mouse.Position ?? Vector2.Zero;
                AdjustAltWheel(AltWheelTargetAt(mouse), action == EditorAction.AltWheelUp ? 1 : -1);
                return true;

            case EditorAction.AddBookmark: AddBookmarkAtCursor(); return true; // EditorScreen.BottomBar.cs (undoable)
            case EditorAction.RemoveBookmark: RemoveBookmarkNearCursor(); return true;

            case EditorAction.PreviousBookmark: seekToPoint(Bookmarks, -1); return true;
            case EditorAction.NextBookmark: seekToPoint(Bookmarks, 1); return true;

            case EditorAction.AddBpmPoint:
                addTimingPoint(ControlPointKind.Bpm);
                return true;

            case EditorAction.AddScrollSpeedPoint:
                addTimingPoint(ControlPointKind.ScrollSpeed);
                return true;

            case EditorAction.DeleteTimingPoint:
                deleteTimingPoint();
                return true;

            case EditorAction.ToggleGridSnap when CurrentTab == EditorTab.Timing:
                tapBlock.Tap(Time.Current);
                return true;
        }

        if (CurrentTab == EditorTab.Compose)
        {
            switch (action)
            {
                case EditorAction.ToolSelect: SelectTool(EditorTool.Select); return true;
                case EditorAction.ToolBtChip: SelectTool(EditorTool.BtChip); return true;
                case EditorAction.ToolBtHold: SelectTool(EditorTool.BtHold); return true;
                case EditorAction.ToolFxChip: SelectTool(EditorTool.FxChip); return true;
                case EditorAction.ToolFxHold: SelectTool(EditorTool.FxHold); return true;
                case EditorAction.ToolLaser:
                    // 6 again switches the laser side
                    if (Tools.Tool.Value == EditorTool.Laser) Tools.LaserSide.Value = 1 - Tools.LaserSide.Value;
                    SelectTool(EditorTool.Laser);
                    return true;

                case EditorAction.LaserSideLeft:
                case EditorAction.LaserSideRight:
                    Tools.LaserSide.Value = action == EditorAction.LaserSideLeft ? 0 : 1;
                    SelectTool(EditorTool.Laser);
                    return true;

                case EditorAction.ToolNext: cycleTool(1); return true;
                case EditorAction.ToolPrevious: cycleTool(-1); return true;
                case EditorAction.ToggleGridSnap: toggles.GridSnap.Toggle(); return true;
                case EditorAction.ToggleWideLaser: ToggleWideLaser(); return true;
                case EditorAction.ToggleSlamAssist: toggles.SlamAssist.Toggle(); return true;
                case EditorAction.ToggleLockNotes: toggles.LockNotes.Toggle(); return true;
                case EditorAction.ToggleMirrorPreview: toggles.MirrorPreview.Toggle(); return true;
            }
        }

        // everything from Save to LiveMapping needs the chart model
        if (action >= EditorAction.Save && action <= EditorAction.LiveMapping)
        {
            if (!ChartActions.Perform(action))
            {
                Logger.Log($"editor action {action}: not wired yet");
                showToast($"{describe(action)}: not wired yet");
            }

            return true;
        }

        return false;
    }

    private static string describe(EditorAction action) => EditorKeyBindings.InfoOf(action).Description;

    private void showToast(string text) => toast.Show(text);

    private void changeRate(double delta) =>
        Playback.Rate.Value = Math.Round(Math.Clamp(Playback.Rate.Value + delta, Playback.Rate.MinValue, Playback.Rate.MaxValue), 2);

    private void setDivisor(int divisor)
    {
        int nearest = EditorColours.Divisor.Steps.OrderBy(s => Math.Abs(s - divisor)).First();
        divisorControl.Divisor.Value = nearest;

        if (nearest != divisor)
            showToast($"1/{divisor} is no snap stop (VOX has 48 ticks per beat), using 1/{nearest}");
    }

    private void cycleTool(int direction)
    {
        var tools = Enum.GetValues<EditorTool>();
        int i = Array.IndexOf(tools, Tools.Tool.Value);
        SelectTool(tools[(i + direction + tools.Length) % tools.Length]);
    }

    private void seekToPoint(IEnumerable<double> times, int direction)
    {
        double t = Playback.CurrentTime.Value;
        var sorted = times.Distinct().OrderBy(x => x).ToList();
        double? target = direction > 0
            ? sorted.Where(x => x > t + 1).Cast<double?>().FirstOrDefault()
            : sorted.Where(x => x < t - 1).Cast<double?>().LastOrDefault();

        if (target is double v)
            Playback.CurrentTime.Value = Math.Clamp(v, 0, Playback.LengthMs);
    }

    private void addTimingPoint(ControlPointKind kind)
    {
        string? refused = Timing.AddPointAtCurrentTime(kind);
        showToast(refused ?? $"{(kind == ControlPointKind.Bpm ? "BPM" : "Scroll speed")} point at {TimingControlPointsDialog.FormatTime(Playback.CurrentTime.Value)}");
    }

    private void deleteTimingPoint()
    {
        if (Timing.DeletePointAtCurrentTime() is { } refused)
            showToast(refused);
    }

    // Shift alone is not a binding: it swaps BT tools to FX while held. Ctrl / Alt (whichever is the hold modifier) turn
    // the chip tools into hold tools while held on their own; any other key uses the modifier up (Core HoldModifierLatch).
    // Keys bound to editor actions never get here (the binding container handles them first): handleAction covers those,
    // and pollModifierKeys catches keys some other drawable consumed.
    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Key.LShift or Key.RShift)
            Tools.ShiftHeld.Value = true;
        else if (isModifierKey(e.Key))
            setModifierKeys(e.ControlPressed, e.AltPressed);
        else
            Tools.OtherInput(classicKey: isClassicKey(e.Key));

        return base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyUpEvent e)
    {
        if (e.Key is Key.LShift or Key.RShift)
            Tools.ShiftHeld.Value = e.ShiftPressed;
        else if (isModifierKey(e.Key))
            setModifierKeys(e.ControlPressed, e.AltPressed);

        base.OnKeyUp(e);
    }

    private static bool isModifierKey(Key key) =>
        key is Key.LShift or Key.RShift or Key.LControl or Key.RControl or Key.LAlt or Key.RAlt or Key.LWin or Key.RWin;

    /// <summary>The hold modifier only exists on the compose tab (the camera tab's Alt-drag on curve handles stays its own).</summary>
    private void setModifierKeys(bool ctrl, bool alt)
    {
        bool compose = CurrentTab == EditorTab.Compose;
        Tools.SetModifierKeys(compose && ctrl, compose && alt);
    }

    /// <summary>
    /// Per frame: the modifier state from the input manager (a key release swallowed elsewhere, focus changes - SDL releases
    /// every key when the window loses focus), and any non-modifier key that is down uses the hold modifier up.
    /// </summary>
    private void pollModifierKeys()
    {
        if (GetContainingInputManager()?.CurrentState.Keyboard is not { } keyboard) return;

        setModifierKeys(keyboard.ControlPressed, keyboard.AltPressed);
        Tools.SetClassicKey(CurrentTab == EditorTab.Compose && classicCombos.Any(c => comboDown(c, keyboard)));
        foreach (var key in keyboard.Keys)
        {
            if (isModifierKey(key)) continue;
            Tools.OtherInput(classicKey: isClassicKey(key));
        }
    }

    // ---- classic-laser key (held like the hold modifier, rebindable: EditorAction.ClassicLaserHold) ----------------

    /// <summary>The classic-laser bindings as physical keys (letters follow the keyboard layout).</summary>
    private List<KeyCombination> classicCombos = new List<KeyCombination>();

    private void updateClassicKey()
    {
        var bindings = EditorKeyBindings.All.Where(b => b.Action == EditorAction.ClassicLaserHold).ToList();
        classicCombos = bindings.Select(b => KeyboardLayoutService.ToPhysical(b.Keys)).ToList();
        Tools.ClassicKey.Value = bindings.Count > 0 ? EditorKeyBindings.Describe(bindings[0].Keys) : string.Empty;
    }

    /// <summary>A non-modifier key of a classic-laser binding (it does not use the classic latch up).</summary>
    private bool isClassicKey(Key key)
    {
        var input = KeyCombination.FromKey(key);
        return classicCombos.Any(c => c.Keys.Contains(input));
    }

    /// <summary>Every key of the combination is down (extra modifiers allowed: Shift = wide, Ctrl = free value).</summary>
    private static bool comboDown(KeyCombination combo, osu.Framework.Input.States.KeyboardState keyboard) =>
        combo.Keys.Length > 0 && combo.Keys.All(k => k switch
        {
            InputKey.Control => keyboard.ControlPressed,
            InputKey.Shift => keyboard.ShiftPressed,
            InputKey.Alt => keyboard.AltPressed,
            InputKey.Super => keyboard.SuperPressed,
            _ => keyboard.Keys.Any(x => KeyCombination.FromKey(x) == k),
        });

    // ---- screen lifecycle --------------------------------------------------

    public override void OnEntering(ScreenTransitionEvent e) => this.FadeInFromZero(200);

    public override void OnResuming(ScreenTransitionEvent e) => this.FadeIn(150);

    public override void OnSuspending(ScreenTransitionEvent e) => this.FadeOut(150);

    public override bool OnExiting(ScreenExitEvent e)
    {
        Playback.IsPlaying.Value = false;
        Playback.Track = null;
        CloseReference();
        teardownTiming();
        Session.Dispose();
        dialogs.CloseAll();
        this.FadeOut(200);
        return base.OnExiting(e);
    }

    // ---- menu: EditorScreen.Menu.cs (createMenu, File/Edit/View/Compose/Camera/Timing/Help) -----------
}

/// <summary>Where Alt+wheel acts, decided by what the cursor is over.</summary>
public enum AltWheelTarget
{
    Timeline,
    Playfield,
    Volume,
}
