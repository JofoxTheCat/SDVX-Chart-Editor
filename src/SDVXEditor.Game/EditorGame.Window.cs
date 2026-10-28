using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges;
using osu.Framework.Input.StateChanges.Events;
using osu.Framework.Logging;
using osuTK.Input;
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Window mode and keyboard layout plumbing of the game.
/// <para>
/// Window modes are the framework's three: Windowed, Borderless (a fullscreen-sized window without frame on the
/// display's bounds — on Windows <c>SDL3WindowsWindow.SetBorderless</c>: <c>SDL_SetWindowBordered(false)</c> + display size,
/// +1 px width under OpenGL/Vulkan so drivers never switch it to exclusive fullscreen; Alt+Tab keeps working) and
/// Fullscreen (exclusive). Alt+Enter / F11 toggle Windowed ↔ Borderless like osu! (the framework's own binding cycles
/// through all three, exclusive fullscreen included); from exclusive Fullscreen they go back to Windowed.
/// </para>
/// </summary>
public partial class EditorGame
{
    /// <summary>The next mode of the Alt+Enter toggle: Windowed ↔ Borderless; exclusive Fullscreen → Windowed.</summary>
    public static WindowMode ToggledWindowMode(WindowMode current) =>
        current == WindowMode.Windowed ? WindowMode.Borderless : WindowMode.Windowed;

    /// <summary>Alt+Enter / F11 / View → Borderless ⇄ Windowed.</summary>
    public void ToggleWindowMode()
    {
        var mode = frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode);
        mode.Value = ToggledWindowMode(mode.Value);
        Logger.Log($"window mode: {mode.Value}");
    }

    /// <summary>How many times the root input manager took Alt+Enter / F11 away from the framework (smoke test).</summary>
    public int WindowKeyToggles { get; private set; }

    /// <summary>The root input manager (normal runs and smoke runs alike).</summary>
    public EditorUserInputManager? RootInput { get; private set; }

    protected override UserInputManager CreateUserInputManager()
    {
        var manager = Options.SmokeTest ? new SmokeUserInputManager() : new EditorUserInputManager();
        manager.WindowKey = () =>
        {
            WindowKeyToggles++;
            ToggleWindowMode();
        };
        return RootInput = manager;
    }

    /// <summary>
    /// The game's root input manager. It takes Alt+Enter and F11 before the framework's (prioritised, not overridable)
    /// <c>FrameworkActionContainer</c> sees them, so they toggle Windowed ↔ Borderless instead of cycling into
    /// exclusive fullscreen.
    /// </summary>
    public partial class EditorUserInputManager : UserInputManager
    {
        /// <summary>Called on the update thread for Alt+Enter / F11.</summary>
        public Action? WindowKey { get; set; }

        private readonly HashSet<Key> swallowed = new HashSet<Key>();

        /// <summary>True for the window-mode keys: Alt+Enter (no Ctrl/Shift/Win) or F11 alone.</summary>
        public static bool IsWindowModeKey(Key key, osu.Framework.Input.States.KeyboardState keyboard) => key switch
        {
            Key.Enter or Key.KeypadEnter => keyboard.AltPressed && !keyboard.ControlPressed && !keyboard.ShiftPressed && !keyboard.SuperPressed,
            Key.F11 => !keyboard.AltPressed && !keyboard.ControlPressed && !keyboard.ShiftPressed && !keyboard.SuperPressed,
            _ => false,
        };

        protected override void HandleKeyboardKeyStateChange(ButtonStateChangeEvent<Key> change)
        {
            if (change.Kind == ButtonStateChangeKind.Pressed && WindowKey != null && IsWindowModeKey(change.Button, change.State.Keyboard))
            {
                swallowed.Add(change.Button);
                WindowKey();
                return;
            }

            if (change.Kind == ButtonStateChangeKind.Released && swallowed.Remove(change.Button))
                return;

            base.HandleKeyboardKeyStateChange(change);
        }
    }

    /// <summary>
    /// Smoke test: input injected into the root input manager (mouse moves, buttons, keys) must hover and click even
    /// when the real OS cursor is outside the window. <see cref="UserInputManager"/> computes hover only while the
    /// cursor is in the window, so an unattended run's clicks landed nowhere; the smoke run's root input manager
    /// always handles hover.
    /// <para>
    /// Likewise <see cref="UserInputManager.HandleInputStateChange"/> drops a wheel turn and a mouse-button press
    /// while <c>Host.Window.CursorInWindow</c> is false. A real wheel or press always comes with the cursor inside
    /// the window; an injected one must not depend on where the user's own cursor happens to be (the F6 BPM wheel
    /// check failed with "bpm 100 -> 100" whenever the OS reported the cursor outside the smoke window — reproduced
    /// by setting CursorInWindow to false before the wheel). The flag is claimed only for that one event.
    /// </para>
    /// </summary>
    private partial class SmokeUserInputManager : EditorUserInputManager
    {
        public override bool HandleHoverEvents => true;

        public override void HandleInputStateChange(InputStateChangeEvent inputStateChange)
        {
            bool needsCursor = inputStateChange is MouseScrollChangeEvent
                               || inputStateChange is ButtonStateChangeEvent<MouseButton> { Kind: ButtonStateChangeKind.Pressed };

            if (needsCursor && Host.Window?.CursorInWindow is BindableBool { Value: false } inWindow)
            {
                inWindow.Value = true;

                try
                {
                    base.HandleInputStateChange(inputStateChange);
                }
                finally
                {
                    inWindow.Value = false;
                }

                return;
            }

            base.HandleInputStateChange(inputStateChange);
        }
    }

    // ---- keyboard layout -------------------------------------------------------------

    [Resolved]
    private ReadableKeyCombinationProvider readableKeys { get; set; } = null!;

    private IBindable<bool>? windowActive;

    /// <summary>
    /// Reads the OS keyboard layout once the window exists (SDL's keymap is only real after video init) and again on
    /// SDL_EVENT_KEYMAP_CHANGED (the framework's <c>KeymapChanged</c>) and on regaining focus. A smoke run simulates US
    /// QWERTY so its injected physical keys mean the same on every machine; the system layout is still read and logged.
    /// </summary>
    private void setupKeyboardLayout()
    {
        KeyboardLayoutService.Refresh();
        Logger.Log($"keyboard layout: system Ctrl+Z listens on physical {KeyboardLayoutService.System.PhysicalKeyFor("Z")} "
                   + $"(Ctrl+Y on {KeyboardLayoutService.System.PhysicalKeyFor("Y")})");

        if (Options.SmokeTest)
            KeyboardLayoutService.Simulate(KeyboardLayout.Qwerty);

        readableKeys.KeymapChanged += onKeymapChanged;

        if (Host.Window != null)
        {
            windowActive = Host.Window.IsActive.GetBoundCopy();
            windowActive.BindValueChanged(e =>
            {
                if (e.NewValue) Schedule(KeyboardLayoutService.Refresh);
            });
        }
    }

    /// <summary>Smoke runs are silent unless SDVXEDITOR_SMOKE_SOUND=1; independent of the volume settings the run tests.</summary>
    private readonly osu.Framework.Bindables.BindableDouble smokeMute = new osu.Framework.Bindables.BindableDouble(0);

    /// <summary>
    /// Smoke runs stay out of the user's way: no sound, and the window opens on another display - the rightmost one by
    /// default, or SDVXEDITOR_SMOKE_DISPLAY = right | left | primary | &lt;index&gt;. (Focus stealing is suppressed in
    /// Program.cs through SDL hints.)
    /// </summary>
    private void setupSmokeWindowAndAudio()
    {
        if (Environment.GetEnvironmentVariable("SDVXEDITOR_SMOKE_SOUND") != "1")
            Audio.AddAdjustment(osu.Framework.Audio.AdjustableProperty.Volume, smokeMute);

        if (Host.Window is not { } window) return;

        var displays = window.Displays.ToList();
        if (displays.Count < 2) return;

        string want = (Environment.GetEnvironmentVariable("SDVXEDITOR_SMOKE_DISPLAY") ?? "right").Trim().ToLowerInvariant();
        var target = want switch
        {
            "left" => displays.MinBy(d => d.Bounds.X),
            "primary" => displays.FirstOrDefault(d => d.Bounds.X == 0 && d.Bounds.Y == 0) ?? displays[0],
            _ when int.TryParse(want, out int i) && i >= 0 && i < displays.Count => displays[i],
            _ => displays.MaxBy(d => d.Bounds.X),
        };

        if (target != null)
        {
            window.CurrentDisplayBindable.Value = target;
            Logger.Log($"smoke: window on display {target.Name} at {target.Bounds}");
        }
    }

    // raised on the window thread
    private void onKeymapChanged() => Schedule(KeyboardLayoutService.Refresh);

    private void teardownKeyboardLayout()
    {
        if (readableKeys != null) readableKeys.KeymapChanged -= onKeymapChanged;
        windowActive?.UnbindAll();
        if (Options.SmokeTest) KeyboardLayoutService.Simulate(null);
    }
}
