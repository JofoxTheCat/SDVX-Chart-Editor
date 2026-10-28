using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Performance;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Input;
using osu.Framework.Testing.Input;
using osuTK.Input;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Overlays.Options;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Options wiring: the overlay (Ctrl+O), the editor key binding overrides (editor.ini <c>EditorBindings</c> ↔
/// <see cref="EditorKeyBindings.Table"/>, both ways, live), the FPS display, the start-up settings that feed the
/// library, and the smoke test's settings snapshot.
/// </summary>
public partial class EditorGame
{
    /// <summary>The options overlay (also cached for screens: the song select button, the editor menu).</summary>
    public OptionsOverlay OptionsOverlay { get; private set; } = null!;

    private Bindable<string> editorBindingSetting = null!;
    private Bindable<bool> showFps = null!;
    private bool syncingEditorBindings;
    private IReadOnlyDictionary<EditorSetting, object?>? smokeSettings;
    private readonly Dictionary<FrameworkSetting, (object? Value, Action<object?> Set)> smokeFrameworkSettings = new Dictionary<FrameworkSetting, (object?, Action<object?>)>();

    private void setupOptions()
    {
        if (Options.SmokeTest)
            snapshotSmokeSettings();

        // editor bindings: settings → table on load and on outside changes, table → settings on every rebind
        editorBindingSetting = config.GetBindable<string>(EditorSetting.EditorBindings);
        EditorKeyBindings.Apply(editorBindingSetting.Value);
        EditorKeyBindings.Changed += onEditorBindingsChanged;
        editorBindingSetting.BindValueChanged(e =>
        {
            if (syncingEditorBindings || e.NewValue == EditorKeyBindings.OverrideText) return;

            syncingEditorBindings = true;
            EditorKeyBindings.Apply(e.NewValue);
            syncingEditorBindings = false;
        });

        showFps = config.GetBindable<bool>(EditorSetting.ShowFps);
        showFps.BindValueChanged(e => FrameStatistics.Value = e.NewValue ? FrameStatisticsMode.Minimal : FrameStatisticsMode.None, true);

        // the ksh2vox version in use, detected once per session (options Export section, export badges)
        dependencies.Cache(VoxConverter = new VoxConverterDetector());
        dependencies.Cache(OptionsOverlay = new OptionsOverlay());
    }

    /// <summary>The converter detection shared by the options and the export badges.</summary>
    public VoxConverterDetector VoxConverter { get; private set; } = null!;

    private void onEditorBindingsChanged()
    {
        if (syncingEditorBindings) return;

        string text = EditorKeyBindings.OverrideText;
        Schedule(() =>
        {
            syncingEditorBindings = true;
            editorBindingSetting.Value = text;
            syncingEditorBindings = false;
        });
    }

    // the root input manager (Alt+Enter / F11, smoke hover) is in EditorGame.Window.cs

    /// <summary>
    /// Smoke test only: a pass-through input manager around the screen layer (null otherwise). It passes the window's
    /// input through untouched until a part sets <c>UseParentInput = false</c> and injects its own (the options part
    /// does, and switches back at its end).
    /// </summary>
    public ManualInputManager? SmokeInput { get; private set; }

    private Drawable wrapSmokeInput(Drawable content)
    {
        if (!Options.SmokeTest) return content;

        SmokeInput = new ManualInputManager { RelativeSizeAxes = Axes.Both, ShowVisualCursorGuide = false, Child = content };
        return SmokeInput;
    }

    private void teardownOptions() => EditorKeyBindings.Changed -= onEditorBindingsChanged;

    /// <summary>
    /// The start-up options for the library: the options' songs folder and launcher config fill in what the command
    /// line left out. A smoke test uses the command line only.
    /// </summary>
    private StartupOptions libraryStartupOptions()
    {
        if (Options.SmokeTest) return Options;

        static string? set(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        return Options with
        {
            SongsDirectory = Options.SongsDirectory ?? set(config.Get<string>(EditorSetting.SongsFolder)),
            LauncherConfigPath = Options.LauncherConfigPath ?? set(config.Get<string>(EditorSetting.LauncherConfigPath)),
        };
    }

    // ---- smoke settings snapshot ----------------------------------------------------

    /// <summary>
    /// A smoke run has its own storage (Program.cs), and still leaves editor.ini / framework.ini exactly as it found
    /// them: every value is snapshotted here and put back in <see cref="restoreSmokeSettings"/> before the files are written.
    /// </summary>
    private void snapshotSmokeSettings()
    {
        smokeSettings = config.Snapshot();
        rememberFramework<WindowMode>(FrameworkSetting.WindowMode);
        rememberFramework<FrameSync>(FrameworkSetting.FrameSync);
        rememberFramework<System.Drawing.Size>(FrameworkSetting.WindowedSize);
    }

    private void rememberFramework<T>(FrameworkSetting setting)
    {
        var b = frameworkConfig.GetBindable<T>(setting);
        smokeFrameworkSettings[setting] = (b.Value, v => b.Value = (T)v!);
    }

    private void restoreSmokeSettings()
    {
        if (smokeSettings == null) return;

        var changed = config.Restore(smokeSettings);
        foreach (var (_, (value, set)) in smokeFrameworkSettings) set(value);
        if (changed.Count > 0)
            Logger.Log($"smoke: restored {changed.Count} setting(s) the run had changed: {string.Join(", ", changed)}");
        smokeSettings = null;
    }

    /// <summary>
    /// Lowest-priority keys of the whole game: Ctrl+O toggles the options when no screen used the key (inside the
    /// editor Ctrl+O is "open difficulty"; the editor's own Options binding is Ctrl+Shift+O).
    /// </summary>
    private partial class GlobalHotkeys : Component
    {
        private readonly Action toggleOptions;

        public GlobalHotkeys(Action toggleOptions)
        {
            this.toggleOptions = toggleOptions;
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (KeyboardLayoutService.Logical(e.Key) == Key.O && e.ControlPressed && !e.ShiftPressed && !e.AltPressed && !e.Repeat)
            {
                toggleOptions();
                return true;
            }

            return base.OnKeyDown(e);
        }
    }
}
