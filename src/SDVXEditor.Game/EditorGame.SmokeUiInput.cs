using System.Drawing;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input;
using osu.Framework.Input.StateChanges;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Containers;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using F6Dialog = SDVXEditor.Game.Screens.Edit.Dialogs.TimingControlPointsDialog;
using SetupDialog = SDVXEditor.Game.Screens.Edit.Dialogs.SongSetupDialog;
using SDVXEditor.Game.Screens.SongSelect;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part <c>ui-input</c> (no song needed): the UI kit driven with real mouse and keyboard input, injected
/// into the game's root input manager exactly where the window's input arrives (mouse position, button down / up a
/// few frames apart, wheel, keys). Nothing is set programmatically except to restore state. Run once borderless at
/// the screen size and once windowed 1600x900: song select Group dropdown (click + keyboard), Song Setup slot and
/// editable display-name combo boxes, tab headers, the Level spinner, F6 spinners (click, hold-to-repeat, wheel,
/// Up/Down keys), check box, radio button, list selection (click, Ctrl+click), the add-kind combo box, buttons.
/// </summary>
public partial class EditorGame
{
    /// <summary>Environment variable: when set, a failing ui-input check is reported and the run goes on (diagnosis).</summary>
    public const string UiInputSoftVariable = "SDVXEDITOR_UIINPUT_SOFT";

    private bool uiInputBusy;
    private int uiInputFailures;

    private InputManager rootInput => GetContainingInputManager() ?? throw new InvalidOperationException("smoke test: no root input manager");

    private void uiCheck(bool condition, string what, Func<string>? detail = null)
    {
        if (condition)
        {
            report("ui-input: ok - " + what);
            return;
        }

        string message = what + (detail != null ? " [" + detail() + "]" : string.Empty);

        if (Environment.GetEnvironmentVariable(UiInputSoftVariable) is { Length: > 0 })
        {
            uiInputFailures++;
            report("ui-input: FAIL - " + message);
            return;
        }

        expect(false, message);
    }

    // ---- raw input, as the window delivers it -------------------------------------

    private void inputMoveTo(Vector2 screenSpace) => new MousePositionAbsoluteInput { Position = screenSpace }.Apply(rootInput.CurrentState, rootInput);
    private void inputButton(bool down)
    {
        // The root input manager drops a press while the OS cursor is outside the window (a windowed run with the
        // user's cursor elsewhere). A real click always has the cursor inside, so the injected one claims that too.
        if (down && Host.Window?.CursorInWindow is osu.Framework.Bindables.BindableBool inWindow && !inWindow.Value)
        {
            cursorInWindowBefore ??= false;
            inWindow.Value = true;
        }

        new MouseButtonInput(MouseButton.Left, down).Apply(rootInput.CurrentState, rootInput);
    }

    private bool? cursorInWindowBefore;
    private void inputWheel(float notches) => inputScroll(new Vector2(0, notches));

    /// <summary>A wheel turn with both axes: some systems and mouse drivers turn Shift+wheel into a horizontal scroll (X).</summary>
    private void inputScroll(Vector2 delta) => new MouseScrollRelativeInput { Delta = delta }.Apply(rootInput.CurrentState, rootInput);

    /// <summary>Shift held around one scroll event: <c>(Shift down, scroll, Shift up)</c>.</summary>
    private (Action, double)[] shiftScroll(Func<Vector2> where, Vector2 delta) => new (Action, double)[]
    {
        (() => inputMoveTo(where()), 40),
        (() => inputKey(Key.ShiftLeft, true), 40),
        (() => inputScroll(delta), 40),
        (() => inputKey(Key.ShiftLeft, false), 60),
    };
    private void inputKey(Key key, bool down) => new KeyboardKeyInput(key, down).Apply(rootInput.CurrentState, rootInput);

    /// <summary>
    /// Runs <paramref name="actions"/> one per ~2 frames (a real click is never one frame); the next smoke step waits
    /// until the last one ran.
    /// </summary>
    private void inputSequence(string what, params (Action Act, double DelayAfter)[] actions)
    {
        uiInputBusy = true;

        void next(int i)
        {
            if (i >= actions.Length)
            {
                uiInputBusy = false;
                return;
            }

            runSmokeAction(what, actions[i].Act);
            Scheduler.AddDelayed(() => next(i + 1), actions[i].DelayAfter);
        }

        next(0);
        waitUntil(() => !uiInputBusy, what, 20_000);
    }

    private static Vector2 centreOf(Drawable d) => d.ScreenSpaceDrawQuad.Centre;

    /// <summary>A point inside <paramref name="d"/>, in its own draw space (x, y relative 0..1).</summary>
    private static Vector2 pointIn(Drawable d, float rx, float ry) => d.ToScreenSpace(new Vector2(d.DrawWidth * rx, d.DrawHeight * ry));

    /// <summary>The positional input queue under the cursor, front first (what a click there would reach).</summary>
    private string queueAt(Drawable target)
    {
        InputManager im = rootInput;
        for (Drawable? p = target; p != null; p = p.Parent)
        {
            if (p is InputManager inner)
            {
                im = inner;
                break;
            }
        }

        Vector2 pos = im.CurrentState.Mouse.Position;
        var chain = new List<string>();
        for (Drawable? p = target; p != null && p != im; p = p.Parent)
        {
            if (!p.PropagatePositionalInputSubTree || (p is CompositeDrawable { Masking: true } && !p.ReceivePositionalInputAt(pos)) || p == target)
                chain.Add($"{p.GetType().Name}(present {p.IsPresent}, propagate {p.PropagatePositionalInputSubTree}, receives {p.ReceivePositionalInputAt(pos)}, quad {p.ScreenSpaceDrawQuad.AABBFloat})");
        }

        return $"mouse {pos.X:0},{pos.Y:0}; queue " + string.Join(" > ", im.PositionalInputQueue.Take(10).Select(d => d.GetType().Name)) + "; blockers " + string.Join(" / ", chain);
    }

    private (Action, double)[] clickAt(Func<Vector2> where, Action? then = null, double hold = 50)
    {
        var list = new List<(Action, double)>
        {
            (() => inputMoveTo(where()), 40),
            (() => inputButton(true), hold),
            (() => inputButton(false), 60),
        };
        if (then != null) list.Add((then, 0));
        return list.ToArray();
    }

    private (Action, double)[] keys(Action? then, params Key[] seq)
    {
        var list = new List<(Action, double)>();
        foreach (var k in seq)
        {
            list.Add((() => inputKey(k, true), 40));
            list.Add((() => inputKey(k, false), 40));
        }

        if (then != null) list.Add((then, 0));
        return list.ToArray();
    }

    private static bool shown(Drawable d)
    {
        for (Drawable? p = d; p != null; p = p.Parent)
        {
            if (!p.IsPresent) return false;
        }

        return true;
    }

    private static IEnumerable<T> visible<T>(Drawable root) where T : Drawable => root.ChildrenOfType<T>().Where(shown);

    private static SpriteText textIn(Drawable root, string text) =>
        visible<SpriteText>(root).FirstOrDefault(t => t.Text.ToString() == text)
        ?? throw new InvalidOperationException($"smoke test: no visible text \"{text}\" in {root.GetType().Name}");

    private PopupLayer? openPopupLayer => this.ChildrenOfType<PopupLayer>().FirstOrDefault(p => p.IsOpen);

    private SpriteText popupItem(string text) =>
        openPopupLayer is PopupLayer layer
            ? textIn(layer, text)
            : throw new InvalidOperationException($"smoke test: no popup open to pick \"{text}\" from");

    private readonly List<osu.Framework.Input.Handlers.InputHandler> disabledHostInput = new List<osu.Framework.Input.Handlers.InputHandler>();

    /// <summary>Nesting depth of <see cref="setHostInput"/>(false): the whole smoke run holds one, ui-input / tutorial another.</summary>
    private int hostInputBlocks;

    /// <summary>
    /// Turns the host's mouse / keyboard / touch handlers off (false) or back on (true; nested — only the outermost
    /// call re-enables them). Injected input must be the only input: while the OS cursor is outside the window,
    /// SDL3Window.pollMouse feeds every move of the user's real cursor (global position, e.g. -1646,-66 on another
    /// display) into the root input manager. A move arriving between an injected move and its press put the press
    /// beside its target ("[menus 5/40] timed out waiting for the slot choice": the dialog button never got the
    /// click), so the whole smoke run blocks them (<see cref="LoadComplete"/>), not only single parts.
    /// </summary>
    private void setHostInput(bool enabled)
    {
        if (enabled)
        {
            if (cursorInWindowBefore is bool was && Host.Window?.CursorInWindow is osu.Framework.Bindables.BindableBool inWindow)
                inWindow.Value = was;
            cursorInWindowBefore = null;

            if (hostInputBlocks == 0 || --hostInputBlocks > 0) return;

            foreach (var h in disabledHostInput) h.Enabled.Value = true;
            disabledHostInput.Clear();
            return;
        }

        if (hostInputBlocks++ > 0) return;

        foreach (var h in Host.AvailableInputHandlers.Where(h => h.Enabled.Value
                                                                && h is osu.Framework.Input.Handlers.Mouse.MouseHandler or osu.Framework.Input.Handlers.Keyboard.KeyboardHandler
                                                                    or osu.Framework.Input.Handlers.Touch.TouchHandler))
        {
            h.Enabled.Value = false;
            disabledHostInput.Add(h);
        }
    }

    // ---- the part --------------------------------------------------------------------

    private List<(Action Act, string? Shot)> uiInputSteps()
    {
        var steps = new List<(Action, string?)>();
        var mode = frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode);
        var size = frameworkConfig.GetBindable<Size>(FrameworkSetting.WindowedSize);
        WindowMode modeBefore = mode.Value;
        Size sizeBefore = size.Value;

        steps.Add((() =>
        {
            modeBefore = mode.Value;
            sizeBefore = size.Value;
            uiInputFailures = 0;
            // the real mouse / keyboard would move the cursor under the injected input (a window that moves or resizes
            // under a resting cursor reports it): only the injected input counts during this part
            setHostInput(false);
        }, null));

        steps.AddRange(uiInputPass("borderless", () => mode.Value = WindowMode.Borderless));
        steps.AddRange(uiInputPass("windowed 1600x900", () =>
        {
            mode.Value = WindowMode.Windowed;
            size.Value = new Size(1600, 900);
        }));

        steps.Add((() =>
        {
            mode.Value = modeBefore;
            size.Value = sizeBefore;
            setHostInput(true);
            expect(uiInputFailures == 0, $"ui-input: {uiInputFailures} check(s) failed (see smoke-report / runtime log)");
        }, null));

        return steps;
    }

    /// <summary>The editor for the dialogs: a scratch copy of the <c>--open</c> song (real timing rows), else a new chart.</summary>
    private EditorScreen uiInputEditor()
    {
        if (Options.OpenSongFolder is not string song)
            return new EditorScreen();

        string work = Path.Combine(smokeWorkRoot(screenshotDir), "smoke-song-ui", Path.GetFileName(song.TrimEnd('\\', '/')));
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);
        foreach (string f in Directory.EnumerateFiles(song))
            File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
        return new EditorScreen(EditorSession.Load(work, Options.OpenChartSlot));
    }

    private List<(Action Act, string? Shot)> uiInputPass(string label, Action resize)
    {
        T current<T>() where T : class, IScreen =>
            screenStack.CurrentScreen as T ?? throw new InvalidOperationException($"smoke test: expected {typeof(T).Name}, got {screenStack.CurrentScreen?.GetType().Name ?? "nothing"}");

        Vector2 sizeBefore = Vector2.Zero;
        string tag = $"[{label}] ";

        // song select
        OsuDropdown<SongGrouping> groupDropdown() => visible<OsuDropdown<SongGrouping>>(current<SongSelectScreen>()).Single();
        SongGrouping grouping0 = default, picked = default;

        // song setup
        SetupDialog setup() => dialogOverlay.Current as SetupDialog ?? throw new InvalidOperationException("smoke test: Song Setup not open");
        ComboBox<int> slotBox() => visible<ComboBox<int>>(setup()).First();
        ComboBox<string> nameBox() => visible<ComboBox<string>>(setup()).First(c => c.Editable);
        int slot0 = 0;
        string name0 = string.Empty, namePick = string.Empty;
        double level0 = 0;

        // F6
        F6Dialog f6() => dialogOverlay.Current as F6Dialog ?? throw new InvalidOperationException("smoke test: F6 not open");
        NumericUpDown bpmBox() => visible<NumericUpDown>(f6()).First();
        double bpm0 = 0;
        bool inherit0 = false;

        Vector2 spinUp(NumericUpDown n) => n.ToScreenSpace(new Vector2(n.DrawWidth - 9, n.DrawHeight * 0.27f));
        Vector2 spinDown(NumericUpDown n) => n.ToScreenSpace(new Vector2(n.DrawWidth - 9, n.DrawHeight * 0.73f));

        return new List<(Action, string?)>
        {
            (() =>
            {
                sizeBefore = DrawSize;
                double t0 = Clock.CurrentTime;
                resize();
                // a mode switch takes a few frames; when the size stays (already that size) go on after 2.5 s
                waitUntil(() => Clock.CurrentTime - t0 > 1200 && (DrawSize != sizeBefore || Clock.CurrentTime - t0 > 2500), "the window to resize", 5000);
            }, null),
            (() => report($"ui-input: {tag}window draw size {DrawSize.X:0}x{DrawSize.Y:0}"), null),

            // ---- song select: osu! dropdown
            (() =>
            {
                expect(screenStack.CurrentScreen is SongSelectScreen, "ui-input starts on song select");
                grouping0 = current<SongSelectScreen>().Grouping.Value;
                inputSequence("open the Group dropdown", clickAt(() => centreOf(groupDropdown()),
                    () => uiCheck(openPopupLayer != null, tag + "click on the Group dropdown opens its list", () => queueAt(groupDropdown()))));
            }, null),
            (() =>
            {
                if (openPopupLayer == null) return;

                var dd = groupDropdown();
                picked = dd.Items.First(g => !EqualityComparer<SongGrouping>.Default.Equals(g, grouping0));
                inputSequence("pick a grouping", clickAt(() => centreOf(popupItem(dd.Formatter(picked))), () =>
                {
                    uiCheck(current<SongSelectScreen>().Grouping.Value.Equals(picked), tag + $"clicking \"{dd.Formatter(picked)}\" changes the grouping",
                        () => $"grouping {current<SongSelectScreen>().Grouping.Value}");
                    uiCheck(openPopupLayer == null, tag + "the list closes after the pick");
                }));
            }, null),
            (() =>
            {
                var before = current<SongSelectScreen>().Grouping.Value;
                var dd = groupDropdown();
                int index = dd.Items.ToList().IndexOf(before);
                var expected = dd.Items[Math.Min(index + 1, dd.Items.Count - 1)];
                inputSequence("Group dropdown by keyboard", clickAt(() => centreOf(groupDropdown())).Concat(keys(() =>
                {
                    uiCheck(current<SongSelectScreen>().Grouping.Value.Equals(expected), tag + "Down + Enter in the open Group list picks the next entry",
                        () => $"grouping {current<SongSelectScreen>().Grouping.Value}, expected {expected}");
                    uiCheck(openPopupLayer == null, tag + "Enter closes the list");
                }, Key.Down, Key.Enter)).ToArray());
            }, null),
            (() =>
            {
                var before = current<SongSelectScreen>().Grouping.Value;
                inputSequence("Group dropdown Esc", clickAt(() => centreOf(groupDropdown())).Concat(keys(() =>
                {
                    uiCheck(openPopupLayer == null, tag + "Esc closes the open list");
                    uiCheck(current<SongSelectScreen>().Grouping.Value.Equals(before), tag + "Esc keeps the value");
                    uiCheck(screenStack.CurrentScreen is SongSelectScreen, tag + "Esc in the list does not leave song select");
                }, Key.Down, Key.Escape)).ToArray());
            }, null),
            (() =>
            {
                openPopupLayer?.Close();
                current<SongSelectScreen>().Grouping.Value = grouping0;
                pushAndWait(uiInputEditor());
            }, null),

            // ---- Song Setup (opened like a user: click the "song setup" tab)
            (() => inputSequence("click the song setup tab", clickAt(() => centreOf(textIn(current<EditorScreen>(), EditorTab.SongSetup.DisplayName().ToLowerInvariant())),
                () => uiCheck(dialogOverlay.Current is SetupDialog, tag + "clicking the song setup tab opens Song Setup"))), null),
            (() =>
            {
                if (dialogOverlay.Current is not SetupDialog) current<EditorScreen>().OpenSongSetup();
                setup().ShowPage(1); // the slot lives on the Difficulty page only (audit U11)
            }, null),
            (() =>
            {
                slot0 = setup().Edit.Slot.Value;
                inputSequence("open the slot combo box", clickAt(() => centreOf(slotBox()),
                    () => uiCheck(openPopupLayer != null, tag + "click on the Song Setup slot combo box opens its list", () => queueAt(slotBox()))));
            }, null),
            (() =>
            {
                if (openPopupLayer == null) return;

                var box = slotBox();
                int target = slot0 == 1 ? 2 : 1;
                inputSequence("pick a slot", clickAt(() => centreOf(popupItem(box.Formatter(target))), () =>
                {
                    uiCheck(setup().Edit.Slot.Value == target, tag + $"clicking \"{box.Formatter(target)}\" sets the slot", () => $"slot {setup().Edit.Slot.Value}");
                    uiCheck(openPopupLayer == null, tag + "the combo list closes after the pick");
                    uiCheck(dialogOverlay.Current is SetupDialog, tag + "picking does not close the dialog");
                }));
            }, null),
            (() =>
            {
                int before = setup().Edit.Slot.Value;
                inputSequence("slot combo box by keyboard", clickAt(() => centreOf(slotBox())).Concat(keys(() =>
                {
                    uiCheck(setup().Edit.Slot.Value == Math.Min(before + 1, 4), tag + "Down + Enter in the open slot list picks the next slot", () => $"slot {setup().Edit.Slot.Value}");
                    uiCheck(openPopupLayer == null, tag + "Enter closes the combo list");
                    uiCheck(dialogOverlay.Current is SetupDialog, tag + "Enter in the list does not press OK");
                }, Key.Down, Key.Enter)).ToArray());
            }, null),
            (() =>
            {
                inputSequence("slot combo box Esc", clickAt(() => centreOf(slotBox())).Concat(keys(() =>
                {
                    uiCheck(openPopupLayer == null, tag + "Esc closes the combo list");
                    uiCheck(dialogOverlay.Current is SetupDialog, tag + "Esc in the list does not cancel the dialog");
                }, Key.Escape)).ToArray());
            }, null),
            (() => setup().ShowPage(0), null), // the display name stays on the General page
            (() =>
            {
                name0 = setup().Edit.DifficultyName.Value;
                var box = nameBox();
                namePick = box.Items.First(i => box.Formatter(i) != name0);
                // the editable box opens from its arrow button (the face is a text box)
                inputSequence("open the display-name combo box", clickAt(() => pointIn(nameBox(), 1, 0.5f) - new Vector2(10, 0),
                    () => uiCheck(openPopupLayer != null, tag + "the editable combo box arrow opens its list", () => queueAt(nameBox()))));
            }, null),
            (() =>
            {
                if (openPopupLayer == null) return;

                inputSequence("pick a display name", clickAt(() => centreOf(popupItem(nameBox().Formatter(namePick))), () =>
                    uiCheck(setup().Edit.DifficultyName.Value == namePick, tag + $"clicking \"{namePick}\" fills the editable combo box", () => $"text \"{setup().Edit.DifficultyName.Value}\"")));
            }, null),
            (() => inputSequence("click the Difficulty tab", clickAt(() => centreOf(textIn(setup(), "Difficulty")),
                () => uiCheck(visible<NumericUpDown>(setup()).Any(), tag + "clicking the Difficulty tab header shows that page"))), null),
            (() =>
            {
                level0 = setup().Edit.Level.Value;
                var n = visible<NumericUpDown>(setup()).First();
                bool up = level0 < setup().Edit.Level.MaxValue - 1e-9; // a level-20 chart can only go down
                inputSequence("Level spinner", clickAt(() => up ? spinUp(n) : spinDown(n), () =>
                    uiCheck(Math.Abs(setup().Edit.Level.Value - (level0 + (up ? 0.1 : -0.1))) < 1e-6, tag + $"Level {(up ? "▲" : "▼")} moves the level by its step",
                        () => $"level {level0} -> {setup().Edit.Level.Value}; queue {queueAt(n)}")));
            }, null),
            (() => inputSequence("Song Setup Cancel button", clickAt(() => centreOf(textIn(setup(), "Cancel")),
                () => uiCheck(dialogOverlay.Current == null, tag + "the Cancel button closes Song Setup"))), null),

            // ---- F6 (opened with the real F6 key)
            (() =>
            {
                dialogOverlay.CloseAll();
                inputSequence("F6", keys(() => uiCheck(dialogOverlay.Current is F6Dialog, tag + "the F6 key opens Timing and Control Points"), Key.F6));
            }, null),
            (() =>
            {
                if (dialogOverlay.Current is not F6Dialog) current<EditorScreen>().OpenTimingSetup();
                bpm0 = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM ▲", clickAt(() => spinUp(n), () =>
                    uiCheck(Math.Abs(f6().Value.Value - (bpm0 + 1)) < 1e-6, tag + "F6 BPM ▲ adds 1", () => $"bpm {bpm0} -> {f6().Value.Value}; queue {queueAt(n)}")));
            }, null),
            (() =>
            {
                double before = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM ▼", clickAt(() => spinDown(n), () =>
                    uiCheck(Math.Abs(f6().Value.Value - (before - 1)) < 1e-6, tag + "F6 BPM ▼ subtracts 1", () => $"bpm {before} -> {f6().Value.Value}")));
            }, null),
            (() =>
            {
                double before = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM ▲ held", clickAt(() => spinUp(n), () =>
                    uiCheck(f6().Value.Value >= before + 3, tag + "holding F6 BPM ▲ repeats", () => $"bpm {before} -> {f6().Value.Value}"), hold: 900));
            }, null),
            (() =>
            {
                double before = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM wheel", (() => inputMoveTo(pointIn(n, 0.4f, 0.5f)), 40), (() => inputWheel(1), 60), (() =>
                    uiCheck(Math.Abs(f6().Value.Value - (before + 1)) < 1e-6, tag + "wheel up over the BPM field adds 1", () => $"bpm {before} -> {f6().Value.Value}; queue {queueAt(n)}"), 0));
            }, null),
            (() =>
            {
                double before = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM Shift+wheel", shiftScroll(() => pointIn(n, 0.4f, 0.5f), new Vector2(0, 1)).Append((() =>
                    uiCheck(Math.Abs(f6().Value.Value - (before + 10)) < 1e-6, tag + "Shift+wheel up over the BPM field adds 10", () => $"bpm {before} -> {f6().Value.Value}"), 0)).ToArray());
            }, null),
            (() =>
            {
                // Shift+wheel arriving as a horizontal scroll (macOS, some mouse drivers): the same ten steps
                double before = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM Shift+horizontal wheel", shiftScroll(() => pointIn(n, 0.4f, 0.5f), new Vector2(-1, 0)).Append((() =>
                    uiCheck(Math.Abs(f6().Value.Value - (before - 10)) < 1e-6, tag + "Shift+wheel down as a horizontal scroll subtracts 10", () => $"bpm {before} -> {f6().Value.Value}"), 0)).ToArray());
            }, null),
            (() =>
            {
                double before = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM keys", clickAt(() => pointIn(n, 0.3f, 0.5f)).Concat(keys(() =>
                    uiCheck(Math.Abs(f6().Value.Value - (before + 1)) < 1e-6, tag + "Up, Up, Down in the focused BPM field = +1", () => $"bpm {before} -> {f6().Value.Value}"),
                    Key.Up, Key.Up, Key.Down)).ToArray());
            }, null),
            (() =>
            {
                // click into the field first (focus, caret), then ▲: the value steps and the text follows
                double before = f6().Value.Value;
                var n = bpmBox();
                inputSequence("BPM focused then ▲", clickAt(() => pointIn(n, 0.3f, 0.5f)).Concat(clickAt(() => spinUp(n), () =>
                    uiCheck(Math.Abs(f6().Value.Value - (before + 1)) < 1e-6, tag + "▲ after clicking into the BPM field adds 1", () => $"bpm {before} -> {f6().Value.Value}"))).ToArray());
            }, null),
            (() =>
            {
                double before = f6().Value.Value;
                var n = bpmBox();
                // two quick clicks (a double click for the framework) = two steps
                inputSequence("BPM ▲ double click", clickAt(() => spinUp(n), hold: 30).Concat(clickAt(() => spinUp(n), () =>
                    uiCheck(Math.Abs(f6().Value.Value - (before + 2)) < 1e-6, tag + "two quick clicks on ▲ add 2", () => $"bpm {before} -> {f6().Value.Value}"), hold: 30)).ToArray());
            }, null),
            (() =>
            {
                double before = f6().Value.Value;
                var n = bpmBox();
                // a press that wanders a few pixels (hand on a mouse) still steps once
                inputSequence("BPM ▼ wobbly",
                    (() => inputMoveTo(spinDown(n)), 40),
                    (() => inputButton(true), 30),
                    (() => inputMoveTo(spinDown(n) + new Vector2(-3, 2)), 30),
                    (() => inputButton(false), 60),
                    (() => uiCheck(Math.Abs(f6().Value.Value - (before - 1)) < 1e-6, tag + "a slightly moving press on ▼ subtracts 1", () => $"bpm {before} -> {f6().Value.Value}"), 0));
            }, null),
            (() =>
            {
                inherit0 = f6().InheritPrevious.Value;
                inputSequence("check box", clickAt(() => centreOf(textIn(f6(), "Inherit previous timing settings")), () =>
                    uiCheck(f6().InheritPrevious.Value != inherit0, tag + "clicking a dialog check box toggles it")));
            }, null),
            (() =>
            {
                var radio = visible<DialogRadioButton<int>>(f6()).First();
                inputSequence("radio button", clickAt(() => centreOf(textIn(f6(), "3 / 4 (waltz)")), () =>
                    uiCheck(radio.Selected, tag + "clicking a dialog radio button selects it")));
            }, null),
            (() =>
            {
                // "+" adds a point (the add-kind combo box is set first, by mouse)
                inputSequence("add-kind combo box", clickAt(() => centreOf(visible<ComboBox<F6Dialog.NewPointKind>>(f6()).First())));
            }, null),
            (() =>
            {
                if (openPopupLayer == null)
                {
                    uiCheck(false, tag + "the add-kind combo box opens its list");
                    return;
                }

                inputSequence("pick Speed", clickAt(() => centreOf(popupItem("Speed")), () =>
                    uiCheck(f6().AddKind.Value == F6Dialog.NewPointKind.ScrollSpeed, tag + "picking \"Speed\" in the add-kind combo box")));
            }, null),
            (() =>
            {
                // "+" on a position that already has a scroll speed point (charts often start with one at 0:00):
                // nothing is added, the existing row is selected and the dialog says why
                var speedRow = f6().List.Items.FirstOrDefault(r => r.Kind == ControlPointKind.ScrollSpeed);
                if (speedRow == null) return;

                current<EditorScreen>().Playback.CurrentTime.Value = speedRow.TimeMs;
                int rows = f6().List.Items.Count;
                var plus = visible<DialogIconButton>(f6()).First();
                inputSequence("+ button refused", clickAt(() => centreOf(plus), () =>
                {
                    uiCheck(f6().List.Items.Count == rows, tag + "+ on an existing scroll speed point adds no duplicate", () => $"rows {rows} -> {f6().List.Items.Count}");
                    uiCheck(f6().LastMessage.Contains("already exists"), tag + "the dialog says why + added nothing", () => $"message \"{f6().LastMessage}\"");
                    uiCheck(f6().List.SelectedItems.Count == 1 && f6().List.SelectedItems[0].Kind == ControlPointKind.ScrollSpeed
                                                               && Math.Abs(f6().List.SelectedItems[0].TimeMs - speedRow.TimeMs) < 1,
                        tag + "the existing point is selected instead");
                }));
            }, null),
            (() =>
            {
                // a position without a scroll speed point (the playhead is set up directly; the click is real)
                var taken = f6().List.Items.Where(r => r.Kind == ControlPointKind.ScrollSpeed).Select(r => r.TimeMs).ToList();
                double free = Enumerable.Range(1, 60).Select(i => i * 1000.0 + 137).First(t => taken.All(x => Math.Abs(x - t) > 50));
                current<EditorScreen>().Playback.CurrentTime.Value = free;
                int rows = f6().List.Items.Count;
                var plus = visible<DialogIconButton>(f6()).First();
                inputSequence("+ button", clickAt(() => centreOf(plus), () =>
                    uiCheck(f6().List.Items.Count == rows + 1, tag + "the + button adds a row", () => $"rows {rows} -> {f6().List.Items.Count} at {current<EditorScreen>().Playback.CurrentTime.Value:0} ms; message \"{f6().LastMessage}\"")));
            }, null),
            (() =>
            {
                var list = f6().List;
                inputSequence("list click", clickAt(() => list.ToScreenSpace(new Vector2(40, ListView<ControlPointRow>.HEADER_HEIGHT + ListView<ControlPointRow>.ROW_HEIGHT * 0.5f + 1)), () =>
                    uiCheck(list.SelectedItems.Count == 1 && list.SelectedItems[0] == list.Items[0], tag + "clicking a list row selects only it", () => $"{list.SelectedItems.Count} selected")));
            }, null),
            (() =>
            {
                var list = f6().List;
                inputSequence("list ctrl+click",
                    (() => inputKey(Key.ControlLeft, true), 40),
                    (() => inputMoveTo(list.ToScreenSpace(new Vector2(40, ListView<ControlPointRow>.HEADER_HEIGHT + ListView<ControlPointRow>.ROW_HEIGHT * 1.5f + 1))), 40),
                    (() => inputButton(true), 50),
                    (() => inputButton(false), 40),
                    (() => inputKey(Key.ControlLeft, false), 40),
                    (() => uiCheck(list.SelectedItems.Count == 2, tag + "Ctrl+click adds a second row to the selection", () => $"{list.SelectedItems.Count} selected"), 0));
            }, null),
            (() =>
            {
                var list = f6().List;
                inputSequence("list keys", clickAt(() => list.ToScreenSpace(new Vector2(40, ListView<ControlPointRow>.HEADER_HEIGHT + ListView<ControlPointRow>.ROW_HEIGHT * 0.5f + 1))).Concat(keys(() =>
                    uiCheck(list.SelectedItems.Count == 1 && list.SelectedItems[0] == list.Items[1], tag + "Down in the focused list selects the next row",
                        () => $"{list.SelectedItems.Count} selected, first at {(list.SelectedItems.Count > 0 ? list.Items.IndexOf(list.SelectedItems[0]) : -1)}"), Key.Down)).ToArray());
            }, null),
            (() => inputSequence("F6 Cancel button", clickAt(() => centreOf(textIn(f6(), "Cancel")),
                () => uiCheck(dialogOverlay.Current == null, tag + "the Cancel button closes F6"))), null),

            // ---- osu!-style kit inside the scaled editor (compose toolbar, timeline, timing tab)
            (() =>
            {
                dialogOverlay.CloseAll();
                current<EditorScreen>().SelectTab(EditorTab.Compose);
            }, null),
            (() =>
            {
                var e = current<EditorScreen>();
                var tiles = visible<ToolTile>(e).ToList();
                var before = e.Tools.Tool.Value;
                var tile = tiles[before == EditorTool.BtChip ? 2 : 1];
                inputSequence("tool tile", clickAt(() => centreOf(tile), () =>
                    uiCheck(e.Tools.Tool.Value != before, tag + "clicking a tool tile selects that tool", () => $"tool {e.Tools.Tool.Value}; queue {queueAt(tile)}")));
            }, null),
            (() =>
            {
                var dd = (OsuDropdown<string>)current<EditorScreen>().Toolbox.FxEffectDropdown;
                inputSequence("toolbar dropdown", clickAt(() => centreOf(dd), () =>
                    uiCheck(openPopupLayer != null, tag + "click on the FX Effect dropdown opens its list", () => queueAt(dd))));
            }, null),
            (() =>
            {
                var dd = (OsuDropdown<string>)current<EditorScreen>().Toolbox.FxEffectDropdown;
                if (openPopupLayer == null) return;

                // the list shows effect labels, the value is the kson name ("Gate" → gate)
                string target = dd.Current.Value == "gate" ? "Echo" : "Gate";
                inputSequence("toolbar dropdown pick", clickAt(() => centreOf(popupItem(target)), () =>
                    uiCheck(dd.Current.Value == target.ToLowerInvariant(), tag + $"picking \"{target}\" in the FX Effect dropdown", () => dd.Current.Value)));
            }, null),
            (() =>
            {
                var slider = visible<SnapSlider>(current<EditorScreen>()).First();
                int before = slider.Current.Value;
                inputSequence("snap slider", clickAt(() => pointIn(slider, before == EditorColours.Divisor.Steps[^1] ? 0.05f : 0.97f, 0.5f), () =>
                    uiCheck(slider.Current.Value != before, tag + "clicking the snap slider track moves it", () => $"1/{before} -> 1/{slider.Current.Value}; queue {queueAt(slider)}")));
            }, null),
            (() =>
            {
                var slider = visible<SnapSlider>(current<EditorScreen>()).First();
                int before = slider.Current.Value;
                inputSequence("snap slider wheel", (() => inputMoveTo(centreOf(slider)), 40), (() => inputWheel(before == EditorColours.Divisor.Steps[0] ? 1 : -1), 60), (() =>
                    uiCheck(slider.Current.Value != before, tag + "the wheel over the snap slider steps it", () => $"1/{before} -> 1/{slider.Current.Value}"), 0));
            }, null),
            (() =>
            {
                // the seek bindings: wheel = one snap tick, Shift+wheel = four (the framework delivers Shift+wheel as a
                // horizontal scroll, WheelInput): two plain notches put the cursor on the grid, then one with Shift
                var e = current<EditorScreen>();
                var field = visible<SDVXEditor.Game.Screens.Edit.Compose.ComposePlayfield>(e).First();
                e.Playback.IsPlaying.Value = false;
                // the snap-slider steps above may leave 1/192, whose ticks are 1 or 2 pulses (1.25 rounded): use 1/4
                e.TriggerAction(SDVXEditor.Game.Screens.Edit.Input.EditorAction.Divisor4);
                e.Playback.CurrentTime.Value = Math.Min(20_000, e.Playback.LengthMs / 2);
                double t0 = 0, t1 = 0, t2 = 0;
                inputSequence("Shift+wheel seek", new (Action, double)[]
                {
                    (() => inputMoveTo(centreOf(field)), 40),
                    (() => inputWheel(-1), 60),
                    (() => t0 = e.Playback.CurrentTime.Value, 0),
                    (() => inputWheel(-1), 60),
                    (() => t1 = e.Playback.CurrentTime.Value, 0),
                }.Concat(shiftScroll(() => centreOf(field), new Vector2(0, -1))).Append((() =>
                {
                    t2 = e.Playback.CurrentTime.Value;
                    double tick = t1 - t0, far = t2 - t1;
                    uiCheck(tick > 0 && far > 3.5 * tick && far < 4.5 * tick, tag + "Shift+wheel over the playfield seeks four snap ticks (wheel: one)",
                        () => $"wheel {t0:0.#} -> {t1:0.#} ms, Shift+wheel -> {t2:0.#} ms");
                }, 0)).ToArray());
            }, null),
            (() => inputSequence("timing tab", clickAt(() => centreOf(textIn(current<EditorScreen>(), EditorTab.Timing.DisplayName().ToLowerInvariant())),
                () => uiCheck(current<EditorScreen>().CurrentTab == EditorTab.Timing, tag + "clicking the timing tab switches to it"))), null),
            (() =>
            {
                var st = visible<OsuStepper>(current<EditorScreen>()).First();
                double before = st.Current.Value;
                inputSequence("stepper ›", clickAt(() => st.ToScreenSpace(new Vector2(st.DrawWidth - 21, st.DrawHeight / 2)), () =>
                    uiCheck(Math.Abs(st.Current.Value - (before + st.Step)) < 1e-6, tag + "the BPM stepper › adds a step", () => $"{before} -> {st.Current.Value}; queue {queueAt(st)}")));
            }, null),
            (() =>
            {
                var st = visible<OsuStepper>(current<EditorScreen>()).First();
                double before = st.Current.Value;
                inputSequence("stepper ‹", clickAt(() => st.ToScreenSpace(new Vector2(21, st.DrawHeight / 2)), () =>
                    uiCheck(Math.Abs(st.Current.Value - (before - st.Step)) < 1e-6, tag + "the BPM stepper ‹ subtracts a step", () => $"{before} -> {st.Current.Value}")));
            }, null),
            (() =>
            {
                var st = visible<OsuStepper>(current<EditorScreen>()).First();
                double before = st.Current.Value;
                inputSequence("stepper wheel", (() => inputMoveTo(centreOf(st)), 40), (() => inputWheel(1), 60), (() =>
                    uiCheck(Math.Abs(st.Current.Value - (before + st.Step)) < 1e-6, tag + "the wheel over the BPM stepper adds a step", () => $"{before} -> {st.Current.Value}; queue {queueAt(st)}"), 0));
            }, null),
            (() =>
            {
                var st = visible<OsuStepper>(current<EditorScreen>()).First();
                double before = st.Current.Value;
                inputSequence("stepper Shift+wheel", shiftScroll(() => centreOf(st), new Vector2(0, 1)).Append((() =>
                    uiCheck(Math.Abs(st.Current.Value - (before + 10 * st.Step)) < 1e-6, tag + "Shift+wheel over the BPM stepper adds ten steps", () => $"{before} -> {st.Current.Value}; queue {queueAt(st)}"), 0)).ToArray());
            }, null),
            (() =>
            {
                var st = visible<OsuStepper>(current<EditorScreen>()).First();
                double before = st.Current.Value;
                inputSequence("stepper Shift+horizontal wheel", shiftScroll(() => centreOf(st), new Vector2(-1, 0)).Append((() =>
                    uiCheck(Math.Abs(st.Current.Value - (before - 10 * st.Step)) < 1e-6, tag + "Shift+wheel down as a horizontal scroll over the BPM stepper subtracts ten steps", () => $"{before} -> {st.Current.Value}"), 0)).ToArray());
            }, null),
            (() =>
            {
                var box = visible<PinkCheckbox>(current<EditorScreen>()).First();
                bool before = box.Current.Value;
                inputSequence("pink checkbox", clickAt(() => centreOf(box), () =>
                    uiCheck(box.Current.Value != before, tag + "clicking an osu! checkbox toggles it", () => queueAt(box))));
            }, null),
            (() =>
            {
                var box = visible<PinkCheckbox>(current<EditorScreen>()).First();
                inputSequence("pink checkbox back", clickAt(() => centreOf(box)));
            }, null),
            (() => inputSequence("Timing Setup Panel button", clickAt(() => centreOf(visible<FlatButton>(current<EditorScreen>()).First(b => visible<SpriteText>(b).Any(t => t.Text.ToString().Contains("Setup")))),
                () => uiCheck(dialogOverlay.Current is F6Dialog, tag + "the Timing Setup Panel button opens F6"))), null),
            (() => inputSequence("Esc closes F6", keys(() => uiCheck(dialogOverlay.Current == null, tag + "Esc closes the dialog"), Key.Escape)), null),
            (() =>
            {
                dialogOverlay.CloseAll();
                current<EditorScreen>().Exit();
            }, null),
            (() => current<SongSelectScreen>(), null),
        };
    }
}
