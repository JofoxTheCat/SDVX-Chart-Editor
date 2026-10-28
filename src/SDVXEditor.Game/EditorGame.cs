using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Logging;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osuTK;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Containers;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Play;
using SDVXEditor.Game.Screens.SongSelect;
using SDVXEditor.Resources;
using SixLabors.ImageSharp;

namespace SDVXEditor.Game;

public partial class EditorGame : osu.Framework.Game
{
    public const string WindowTitle = "SDVX Chart Editor";

    /// <summary>
    /// Design resolution of all screens. The reference screenshots are 1920x1080, so every
    /// size in the screens is in those pixels; the scaling container fits it to the window.
    /// </summary>
    public static readonly Vector2 DesignSize = new Vector2(1920, 1080);

    /// <summary>
    /// Environment variable: when set during --smoke-test, a PNG of every visited
    /// screen/dialog is written into that directory (window forced to 1600x900).
    /// </summary>
    public const string ScreenshotDirVariable = "SDVXEDITOR_SCREENSHOT_DIR";

    public StartupOptions Options { get; }

    /// <summary>The confirm-then-open path for web links (<see cref="Overlays.ExternalLinks"/>).</summary>
    public Overlays.ExternalLinks Links { get; private set; } = null!;

    private ScreenStack screenStack = null!;
    private DialogOverlay dialogOverlay = null!;
    private DependencyContainer dependencies = null!;
    private EditorConfigManager config = null!;
    private osu.Framework.Bindables.IBindable<double>? masterVolume; // kept alive: see EditorScreen.musicVolume
    private FrameworkConfigManager frameworkConfig = null!;

    public EditorGame(StartupOptions? options = null)
    {
        Options = options ?? StartupOptions.Default;
    }

    public override void SetHost(GameHost host)
    {
        base.SetHost(host);

        if (host.Window != null)
            host.Window.Title = WindowTitle;
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent) =>
        dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

    [BackgroundDependencyLoader]
    private void load(FrameworkConfigManager frameworkConfig)
    {
        this.frameworkConfig = frameworkConfig;

        // One number format in the whole UI (audit U1): the framework applies its Locale setting to every game thread,
        // and an empty value means the Windows culture — on a German system some widgets showed "327,42" next to
        // "327.42bpm". English culture = decimal dot everywhere; text fields still accept a comma (DialogText.TryParse).
        // Core.Editing.UiFormat documents the formats.
        frameworkConfig.GetBindable<string>(FrameworkSetting.Locale).Value = "en";

        // Own embedded assets; framework resources (Roboto, FontAwesome) are registered by the base Game.
        Resources.AddStore(new DllResourceStore(SDVXEditorResources.ResourceAssembly));
        EditorFont.AddFallbacks(this, Host.Renderer); // Noto Sans / Noto Sans JP behind Roboto (docs/FONTS.md)

        dependencies.CacheAs(Options);
        resetSmokeSettings();
        dependencies.Cache(config = new EditorConfigManager(Host.Storage));
        Audio.AddAdjustment(AdjustableProperty.Volume, masterVolume = config.GetBindable<double>(EditorSetting.VolumeMaster));

        if (Options.FirstRun && !Options.SmokeTest)
            frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode).Value = WindowMode.Borderless;

        // the framework's own default would trap the cursor in Borderless; editor.ini decides (default Never)
        config.BindWith(EditorSetting.ConfineMouse, frameworkConfig.GetBindable<osu.Framework.Input.ConfineMouseMode>(FrameworkSetting.ConfineMouseMode));

        if (Options.SmokeTest && screenshotDir != null)
            frameworkConfig.GetBindable<System.Drawing.Size>(FrameworkSetting.WindowedSize).Value = new System.Drawing.Size(1600, 900);

        if (Options.SmokeTest)
            setupSmokeWindowAndAudio(); // EditorGame.Window.cs: muted, on the rightmost display

        // dialogs live outside the scaling container: native-look windows keep their pixel size
        dialogOverlay = new DialogOverlay();
        dependencies.CacheAs(dialogOverlay);

        // web links (Support the Artist, Thanks): always behind a confirmation; a smoke run records instead of opening
        dependencies.Cache(Links = new Overlays.ExternalLinks(dialogOverlay, Host, () => dependencies.Get<Clipboard>(), Options.SmokeTest));

        loadLibrary(); // EditorGame.Library.cs: song library, texture cache, file drop
        setupOptions(); // EditorGame.Options.cs: options overlay, editor bindings, FPS display, smoke settings snapshot

        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Background },
            new DrawSizePreservingFillContainer
            {
                TargetDrawSize = DesignSize,
                Strategy = DrawSizePreservationStrategy.Minimum,
                // smoke test: a pass-through input manager the smoke parts can drive with mouse/key input (SmokeInput)
                // tooltips (IHasTooltip, e.g. the export badges' VOX pill) for everything in the screen layer
                Child = wrapSmokeInput(new osu.Framework.Graphics.Cursor.TooltipContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new PopupHostContainer
                    {
                        Children = new Drawable[]
                        {
                            new GlobalHotkeys(() => OptionsOverlay.Toggle()), // first = last to see a key: screens win (Ctrl+O in the editor)
                            screenStack = new ScreenStack { RelativeSizeAxes = Axes.Both },
                            OptionsOverlay,
                        },
                    },
                }),
            },
            dialogOverlay,
            Library,
            VoxConverter,
        };
        setupTutorial(); // EditorGame.Tutorial.cs: tutorial manager, its overlay above the dialogs, first-start hint
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        setupKeyboardLayout(); // EditorGame.Window.cs: letter shortcuts follow the OS keyboard layout

        screenStack.Push(new SongSelectScreen());

        if (Options.SmokeTest)
        {
            // only injected input for the whole run: the user's real mouse / keyboard never reach the smoke window
            // (EditorGame.SmokeUiInput.cs setHostInput; put back in exitWhenScreenshotsDone)
            setHostInput(false);
            runSmokeTest();
        }
        else if (Options.OpenSongFolder is string folder)
            openSong(folder, Options.OpenChartSlot);

        // --import <file>: like a drop onto song select (EditorGame.Library.cs)
        if (!Options.SmokeTest && Options.ImportPath is string import)
            importOnStart(import);
    }

    /// <summary>--open: loads a song folder straight into the editor (song select stays underneath).</summary>
    private void openSong(string folder, int? slot)
    {
        try
        {
            screenStack.Push(new EditorScreen(EditorSession.Load(folder, slot)));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Logger.Error(e, $"could not open {folder}");
        }
    }

    /// <summary>
    /// Smoke run: every run starts from default editor settings. The smoke storage (%APPDATA%\SDVXEditor-smoke) is
    /// the run's alone, but a run that is killed never puts its settings back: one left <c>EditorBindings =
    /// tab-timing=F8</c> behind (the options part rebinds that row), a muted master volume, A instead of D for BT-A and
    /// ticked tutorial chapters, and the next run tested against those. Its editor.ini is deleted before the config
    /// manager reads it, so the defaults apply; the per-run snapshot/restore still keeps the file tidy.
    /// framework.ini (window mode / size) stays: the parts that change it restore it themselves.
    /// </summary>
    private void resetSmokeSettings()
    {
        if (!Options.SmokeTest) return;

        string path = Host.Storage.GetFullPath("editor.ini");
        // only ever the dedicated smoke storage, never the user's own settings
        if (!path.Contains("SDVXEditor-smoke", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;

        File.Delete(path);
        Logger.Log($"smoke: removed {path} from the last run; editor settings start at their defaults");
    }

    private string? smokeTempRoot;

    /// <summary>
    /// Where the smoke parts put their song copies and smoke-report.txt: next to the screenshot folder, else a
    /// temp folder of this run (one per process, so two smoke runs at once never share a copy).
    /// </summary>
    private string smokeWorkRoot(string? shots) =>
        shots != null
            ? Path.GetDirectoryName(Path.GetFullPath(shots))!
            : smokeTempRoot ??= Path.Combine(Path.GetTempPath(), "SDVXEditor-smoke", $"{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}");

    private static string? screenshotDir => Environment.GetEnvironmentVariable(ScreenshotDirVariable) is { Length: > 0 } dir ? dir : null;

    // ---- smoke test --------------------------------------------------------

    private readonly List<Task> pendingScreenshots = new List<Task>();

    /// <summary>
    /// Walks every screen, tab and dialog once so construction, loading and the main
    /// interactions are exercised, then exits. A failure surfaces as a <see cref="SmokeTestException"/>
    /// through the host; the entry point prints a one-line result and exits with 1 (0 on success).
    /// Parts: song select, the screen walk, the options overlay, ui-input (real mouse/keys on the UI kit), and with <c>--open</c> collab, editing (+ timing), camera/effects
    /// and playtest; <c>SDVXEDITOR_SMOKE_ONLY=camera,playtest</c> runs a subset.
    /// </summary>
    private void runSmokeTest()
    {
        var only = smokeOnlyParts();
        bool want(string part) => only == null || only.Contains(part);

        // before song select: its screenshots are the first ones (a missing folder failed the run at the very end)
        if (screenshotDir is string shotDir)
            Directory.CreateDirectory(shotDir);

        if (only != null && Options.OpenSongFolder == null && only.Overlaps(smoke_song_parts))
            throw new InvalidOperationException($"smoke test: {SmokeOnlyVariable}={string.Join(",", only)} needs --open <song folder>");

        // song select against the real library first (EditorGame.SmokeSongSelect.cs), then the screen walk below
        if (want("songselect") && !songSelectSmokeDone)
        {
            runSongSelectSmoke(screenshotDir, () =>
            {
                songSelectSmokeDone = true;
                runSmokeTest();
            });
            return;
        }

        string? shots = screenshotDir;
        double interval = shots != null ? 700 : 250;

        if (shots != null)
            Directory.CreateDirectory(shots);

        var parts = new List<(string Part, List<(Action Act, string? Shot)> Steps)>();
        if (want("walk")) parts.Add(("walk", screenWalkSteps()));
        if (want("options")) parts.Add(("options", optionsSteps())); // EditorGame.SmokeOptions.cs (no song needed)
        if (want("ui-input")) parts.Add(("ui-input", uiInputSteps())); // EditorGame.SmokeUiInput.cs (real mouse/keys, no song needed)
        if (want("bottombar")) parts.Add(("bottombar", bottomBarSteps(shots))); // EditorGame.SmokeBottomBar.cs (generated short-audio song, no --open needed)
        if (want("tutorial")) parts.Add(("tutorial", tutorialSteps(shots))); // EditorGame.SmokeTutorial.cs (every chapter with real input, own scratch song)

        if (Options.OpenSongFolder is string song)
        {
            if (want("collab")) parts.Add(("collab", collabSteps(song, Options.OpenChartSlot, shots, interval))); // EditorGame.SmokeCollab.cs
            if (want("editing")) parts.Add(("editing", chartEditingSteps(song, Options.OpenChartSlot, shots, interval))); // EditorGame.SmokeEditing.cs (+ SmokeTiming.cs)
            if (want("camera")) parts.Add(("camera", cameraEffectsSteps(song, Options.OpenChartSlot, shots, interval))); // EditorGame.SmokeCamera.cs
            if (want("effects")) parts.Add(("effects", effectsSteps(song, Options.OpenChartSlot, shots))); // EditorGame.SmokeEffects.cs (VOX game effects, real input)
            if (want("playtest")) parts.Add(("playtest", playtestSteps(song, Options.OpenChartSlot, shots, interval))); // EditorGame.SmokePlaytest.cs
            if (want("menus")) parts.Add(("menus", menuSteps(song, Options.OpenChartSlot, shots))); // EditorGame.SmokeMenus.cs (every menu item by mouse, QWERTZ, Alt+Enter)
            if (want("sources")) parts.Add(("sources", songSourceSteps(song, Options.OpenChartSlot, shots))); // EditorGame.SmokeSongSources.cs (song sources, Support the Artist, Thanks)
            if (want("panels")) parts.Add(("panels", panelSteps(song, Options.OpenChartSlot, shots))); // EditorGame.SmokePanels.cs (laser gap, select FX in view, spin bars, lane highlight, background preview)
            if (want("dsp")) parts.Add(("dsp", effectAudioSteps(song, Options.OpenChartSlot, shots))); // EditorGame.SmokeEffectAudio.cs (audio effect DSP: checkbox, playback, preview, playtest)
        }

        if (want("features")) parts.Add(("features", featureSteps(shots))); // EditorGame.SmokeFeatures.cs (2026-10-27 batch, generated song, real input)

        var flat = parts.SelectMany(p => p.Steps.Select((s, i) => new TimedStep($"{p.Part} {i + 1}/{p.Steps.Count}", s.Act, s.Shot))).ToList();
        smokePartsRun.AddRange(parts.Select(p => p.Part));
        Scheduler.AddDelayed(() => runTimedSteps(flat, 0, interval, shots), 900);
    }

    /// <summary>Environment variable: comma-separated subset of the smoke parts to run (default: all).</summary>
    public const string SmokeOnlyVariable = "SDVXEDITOR_SMOKE_ONLY";

    private static readonly string[] smoke_parts = { "songselect", "walk", "options", "ui-input", "bottombar", "collab", "editing", "camera", "effects", "playtest", "menus", "sources", "panels", "tutorial", "features", "dsp" };
    private static readonly string[] smoke_song_parts = { "collab", "editing", "camera", "effects", "playtest", "menus", "sources", "panels", "dsp" };

    private static HashSet<string>? smokeOnlyParts()
    {
        if (Environment.GetEnvironmentVariable(SmokeOnlyVariable) is not { Length: > 0 } value)
            return null;

        var set = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(p => p.ToLowerInvariant()).ToHashSet();
        var unknown = set.Except(smoke_parts).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"smoke test: unknown {SmokeOnlyVariable} part(s) {string.Join(", ", unknown)} (known: {string.Join(", ", smoke_parts)})");
        return set;
    }

    // ---- smoke runner ------------------------------------------------------

    /// <summary>True once the smoke test ran to its end (the entry point turns this into exit code 0).</summary>
    public bool SmokeTestPassed { get; private set; }

    /// <summary>What the smoke test covered, for the one-line result the entry point prints.</summary>
    public string SmokeTestSummary => $"{(Options.OpenSongFolder is string s ? Path.GetFileName(s.TrimEnd('\\', '/')) : "no --open song")}; "
                                      + $"parts {string.Join(",", (songSelectSmokeDone ? new[] { "songselect" } : Array.Empty<string>()).Concat(smokePartsRun))}; {smokeStepsRun} steps";

    private readonly List<string> smokePartsRun = new List<string>();
    private int smokeStepsRun;

    private sealed record TimedStep(string Name, Action Act, string? Shot);

    /// <summary>A condition the next step waits for (set by <see cref="waitUntil"/> from inside a step).</summary>
    private (Func<bool> Cond, string What, double Deadline)? smokeGate;

    /// <summary>
    /// From inside a smoke step: the next step runs only once <paramref name="cond"/> holds (checked every
    /// 50 ms, after the usual step interval), and the test fails after <paramref name="timeoutMs"/>. Use this
    /// instead of padding with empty steps for anything asynchronous (exports, imports, audio threads).
    /// </summary>
    private void waitUntil(Func<bool> cond, string what, double timeoutMs = 30_000) => smokeGate = (cond, what, Clock.CurrentTime + timeoutMs);

    /// <summary>Pushes a screen; the next step waits until it has loaded and is current (screens load asynchronously).</summary>
    private void pushAndWait(Screen screen)
    {
        screenStack.Push(screen);
        waitUntil(() => screen.IsLoaded && screen.IsCurrentScreen(), $"{screen.GetType().Name} to load", 30_000);
    }

    /// <summary>The next step waits until the playtest screen (pushed by EditorScreen.Playtest) has loaded.</summary>
    private void waitForPlaytest() =>
        waitUntil(() => screenStack.CurrentScreen is PlaytestScreen p && p.IsLoaded, "the playtest screen to load", 30_000);

    /// <summary>Runs one step and wraps a failure with the step name so the one-line result says where it broke.</summary>
    private void runSmokeAction(string name, Action act)
    {
        try
        {
            smokeStepsRun++;
            act();
        }
        catch (SmokeTestException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new SmokeTestException($"[{name}] {e.Message}", e);
        }
    }

    /// <summary>Waits for the gate a step may have set, then calls <paramref name="next"/>.</summary>
    private void afterGate(string name, Action next)
    {
        if (smokeGate is { } gate)
        {
            bool ready;
            try
            {
                ready = gate.Cond();
            }
            catch (Exception e)
            {
                throw new SmokeTestException($"[{name}] while waiting for {gate.What}: {e.Message}", e);
            }

            if (!ready)
            {
                if (Clock.CurrentTime > gate.Deadline)
                    throw new SmokeTestException($"[{name}] timed out waiting for {gate.What}");

                Scheduler.AddDelayed(() => afterGate(name, next), 50);
                return;
            }

            smokeGate = null;
        }

        next();
    }

    /// <summary>
    /// Runs the steps one after another, <paramref name="interval"/> apart (sequential, so a slow step delays the
    /// rest instead of overlapping them), then exits once the screenshots are written.
    /// </summary>
    private void runTimedSteps(List<TimedStep> steps, int index, double interval, string? shots)
    {
        if (index >= steps.Count)
        {
            exitWhenScreenshotsDone();
            return;
        }

        var step = steps[index];
        runSmokeAction(step.Name, step.Act);

        if (step.Shot != null && shots != null)
            Scheduler.AddDelayed(() => pendingScreenshots.Add(saveScreenshot(Path.Combine(shots, step.Shot + ".png"))), interval * 0.8);

        Scheduler.AddDelayed(() => afterGate(step.Name, () => runTimedSteps(steps, index + 1, interval, shots)), interval);
    }

    /// <summary>The screen walk: every screen, tab and dialog once, on a new chart (no song needed).</summary>
    private List<(Action Act, string? Shot)> screenWalkSteps()
    {
        // the central binding table must not bind one key combination twice
        var duplicates = EditorKeyBindings.All.GroupBy(b => b.Keys).Where(g => g.Select(b => b.Action).Distinct().Count() > 1).ToList();
        expect(duplicates.Count == 0, "duplicate key bindings: " + string.Join(", ", duplicates.Select(d => EditorKeyBindings.Describe(d.Key))));

        T current<T>() where T : class, IScreen =>
            screenStack.CurrentScreen as T ?? throw new InvalidOperationException($"smoke test: expected {typeof(T).Name}, got {screenStack.CurrentScreen?.GetType().Name ?? "nothing"}");

        var steps = new List<(Action Act, string? Shot)>
        {
            (() => { }, "01-songselect"),
            (() => current<SongSelectScreen>().Wheel.SelectRelative(3), "01b-songselect-moved"),
            (() => current<SongSelectScreen>().OpenOptionsForSelected(), "06-chart-options"),
            (() => current<SongSelectScreen>().Options.Close(), null),
            (() => pushAndWait(new EditorScreen()), null),
            (() => { }, "02-editor-compose"),
            (() =>
            {
                var editor = current<EditorScreen>();
                editor.SelectTool(EditorTool.BtChip);
                editor.Tools.ShiftHeld.Value = true;
                expect(editor.Tools.EffectiveTool.Value == EditorTool.FxChip, "Shift turns BT chip into FX chip");
            }, "02d-tools-shift-fx"),
            (() =>
            {
                var editor = current<EditorScreen>();
                editor.Tools.ShiftHeld.Value = false;
                expect(editor.Tools.EffectiveTool.Value == EditorTool.BtChip, "releasing Shift restores BT chip");
                editor.TriggerAction(EditorAction.ToolLaser);
                expect(editor.Tools.Tool.Value == EditorTool.Laser, "key 6 = laser");

                double before = editor.Playback.CurrentTime.Value;
                editor.TriggerAction(EditorAction.SeekForwardFar);
                expect(editor.Playback.CurrentTime.Value > before, "Shift+Right seeks forward");
                editor.TriggerAction(EditorAction.Divisor3);
                editor.TriggerAction(EditorAction.Divisor5);
                editor.TriggerAction(EditorAction.PlaybackRateDown);
                expect(Math.Abs(editor.Playback.Rate.Value - 0.75) < 0.001, "Alt+Down = -25 %");
                editor.TriggerAction(EditorAction.AddBookmark);
                expect(editor.Bookmarks.Count == 1, "Ctrl+B adds a bookmark");
                editor.TriggerAction(EditorAction.AddScrollSpeedPoint);
                editor.TriggerAction(EditorAction.NextTimingPoint);
                float zoom = editor.Zoom.Value;
                editor.AdjustAltWheel(AltWheelTarget.Timeline, 1);
                expect(editor.Zoom.Value > zoom && editor.Timeline.Zoom.Value == editor.Zoom.Value, "Alt+wheel over the timeline zooms the shared zoom");
                editor.AdjustAltWheel(AltWheelTarget.Playfield, -1);
                expect(Math.Abs(editor.Zoom.Value - zoom) < 0.001f, "Alt+wheel over the playfield zooms the same value back");
                editor.Timeline.Zoom.Value += Screens.Edit.Components.TimelineArea.ZOOM_STEP;
                expect(editor.Zoom.Value > zoom, "the timeline + button zooms the playfield too");
                editor.Zoom.Value = zoom;
                expect(editor.Session.Chart.Beat.ScrollSpeed.Count == 1, "Ctrl+Shift+P added a scroll speed point to the chart");
                editor.TriggerAction(EditorAction.Undo);
                expect(editor.Session.Chart.Beat.ScrollSpeed.Count == 0, "Ctrl+Z removed the scroll speed point");
                editor.TriggerAction(EditorAction.Undo);
                expect(editor.Bookmarks.Count == 0, "Ctrl+Z removed the Ctrl+B bookmark (bookmarks are undoable)");
                editor.TriggerAction(EditorAction.Undo);
                expect(editor.LastToast.Contains("Nothing to undo"), "Ctrl+Z on an untouched chart says so");
                editor.AdjustAltWheel(AltWheelTarget.Volume, -1);
            }, "02e-volume-and-toast"),
            (() => current<EditorScreen>().MenuBar.OpenMenu(0), "02b-menu-file"),
            (() => current<EditorScreen>().MenuBar.OpenMenu(0), null),
            (() => current<EditorScreen>().SelectTab(EditorTab.Camera), "02c-camera"),
            (() => current<EditorScreen>().SelectTab(EditorTab.Effects), null),
            (() => current<EditorScreen>().SelectTab(EditorTab.Timing), "03-timing"),
            (() => current<EditorScreen>().OpenTimingSetup(), "04-f6-timing-points"),
            (() => ((TimingControlPointsDialog())).ShowListTab(2), "04b-f6-scroll-speed"),
            (() => TimingControlPointsDialog().Cancel(), null),
            (() => current<EditorScreen>().OpenShortcuts(), "07-keyboard-shortcuts"),
            (() => ((Screens.Edit.Dialogs.ShortcutsDialog)dialogOverlay.Current!).ShowPage(4), null),
            (() => dialogOverlay.Current!.Cancel(), null),
            (() => current<EditorScreen>().SelectTab(EditorTab.SongSetup), "05-song-setup"),
            (() => SongSetupDialog().ShowPage(1), "05b-song-setup-difficulty"),
            (() => SongSetupDialog().ShowPage(2), null),
            (() => SongSetupDialog().ShowPage(3), null),
            (() => SongSetupDialog().ShowPage(4), null),
            (() => SongSetupDialog().ShowPage(5), null),
            (() => SongSetupDialog().ShowPage(Screens.Edit.Dialogs.SongSetupDialog.SourcesPage), "05c-song-setup-sources"),
            (() => SongSetupDialog().Cancel(), null),
            (() => current<EditorScreen>().SelectTab(EditorTab.Compose), null),
            (() =>
            {
                current<EditorScreen>().Playtest();
                waitForPlaytest();
            }, null),
            (() => current<PlaytestScreen>().Exit(), null),
            (() => current<EditorScreen>().Exit(), null),
            (() => current<SongSelectScreen>(), null),
        };

        return steps;
    }

    private static void expect(bool condition, string what)
    {
        if (!condition)
            throw new InvalidOperationException("smoke test: " + what);
    }

    /// <summary>A smoke-test failure, with the step it happened in.</summary>
    public sealed class SmokeTestException : Exception
    {
        public SmokeTestException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }

    protected override void Dispose(bool isDisposing)
    {
        restoreSmokeSettings(); // EditorGame.Options.cs: a smoke run leaves the settings as it found them
        teardownOptions();
        teardownKeyboardLayout();

        // writes editor.ini
        config?.Dispose();
        textureCache?.Dispose();
        base.Dispose(isDisposing);
    }

    private Screens.Edit.Dialogs.TimingControlPointsDialog TimingControlPointsDialog() =>
        dialogOverlay.Current as Screens.Edit.Dialogs.TimingControlPointsDialog ?? throw new InvalidOperationException("smoke test: F6 dialog not open");

    private Screens.Edit.Dialogs.SongSetupDialog SongSetupDialog() =>
        dialogOverlay.Current as Screens.Edit.Dialogs.SongSetupDialog ?? throw new InvalidOperationException("smoke test: Song Setup not open");

    private async Task saveScreenshot(string path)
    {
        using var image = await Host.TakeScreenshotAsync().ConfigureAwait(false);
        await image.SaveAsPngAsync(path).ConfigureAwait(false);
    }

    private void exitWhenScreenshotsDone()
    {
        if (pendingScreenshots.Any(s => !s.IsCompleted))
        {
            Scheduler.AddDelayed(exitWhenScreenshotsDone, 100);
            return;
        }

        foreach (var failed in pendingScreenshots.Where(s => s.IsFaulted))
            throw new SmokeTestException("screenshot failed: " + failed.Exception?.GetBaseException().Message, failed.Exception);

        Logger.Log($"smoke: passed ({SmokeTestSummary})", LoggingTarget.Runtime, LogLevel.Important);
        SmokeTestPassed = true;
        // on the update thread: restoring in Dispose re-fires bound drawables (volume meters) off-thread and threw
        restoreSmokeSettings();
        setHostInput(true);
        Exit();
    }
}
