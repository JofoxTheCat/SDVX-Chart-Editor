using osu.Framework.Graphics;
using osu.Framework.Screens;
using osuTK.Input;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Overlays.Options;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Play;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part for the options overlay (no song needed), driven with input injected into the smoke input manager
/// (<see cref="SmokeInput"/>; mouse moves and clicks, key presses — through hover, the input queues and focus exactly
/// like the window's own input): Ctrl+O opens it, a search narrows the rows, one
/// editor action (Timing tab F3 → F8) and one playtest control (BT-A D → A) are rebound through the capture flow,
/// a conflict is offered and cancelled, a reserved playtest key is refused; then in a new editor F8 opens the Timing tab
/// and F3 no longer does, the Help dialog shows F8, and in the playtest A holds BT-A while D does not. Reset to
/// default (row buttons) brings F3 back. Both binding settings are snapshotted first and put back at the end.
/// Every step checks the effect of its input through <see cref="waitUntil"/> (the next step runs once it holds); a click
/// moves the cursor first and presses a few frames later, like a hand would.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> optionsSteps()
    {
        var o = OptionsOverlay;
        string editorBefore = string.Empty, playBefore = string.Empty;
        bool heldByA = false;

        osu.Framework.Testing.Input.ManualInputManager input() => SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        void moveTo(Drawable d) => input().MoveMouseTo(d);
        void press(Key k) => input().PressKey(k);
        void release(Key k) => input().ReleaseKey(k);
        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException($"smoke test: editor not open (got {screenStack.CurrentScreen?.GetType().Name})");
        PlaytestScreen pt() => screenStack.CurrentScreen as PlaytestScreen ?? throw new InvalidOperationException($"smoke test: playtest not open (got {screenStack.CurrentScreen?.GetType().Name})");

        KeybindRow<T> row<T>(T action) where T : struct, Enum =>
            o.AllItems.OfType<KeybindRow<T>>().FirstOrDefault(r => EqualityComparer<T>.Default.Equals(r.Action, action))
            ?? throw new InvalidOperationException($"smoke test: no options row for {action}");

        // the hover/positional queue follows the cursor on the next frame: press and release a few frames later
        void click(Drawable d)
        {
            moveTo(d);
            Scheduler.AddDelayed(() => input().PressButton(MouseButton.Left), 40);
            Scheduler.AddDelayed(() => input().ReleaseButton(MouseButton.Left), 80);
        }

        void key(Key k, params Key[] modifiers)
        {
            foreach (var m in modifiers) press(m);
            press(k);
            release(k);
            foreach (var m in modifiers.Reverse()) release(m);
        }

        void after(Func<bool> cond, string what) => waitUntil(cond, what, 5000);

        string playSetting() => config.Get<string>(EditorSetting.PlaytestBindings);
        IReadOnlyList<string> timing() => EditorKeyBindings.Table[EditorAction.TabTiming];
        bool timingIs(params string[] combos) => timing().SequenceEqual(combos);

        return new List<(Action, string?)>
        {
            (() =>
            {
                // the run starts from the defaults, whatever the storage held; the values are put back at the end
                input().UseParentInput = false;
                editorBefore = config.Get<string>(EditorSetting.EditorBindings);
                playBefore = playSetting();
                EditorKeyBindings.Table.ResetAll();
                config.SetValue(EditorSetting.PlaytestBindings, PlayBindings.DefaultText);
                expect(screenStack.CurrentScreen is Screens.SongSelect.SongSelectScreen, "options part starts on song select");
                key(Key.O, Key.LControl);
                after(() => o.IsSettled, "Ctrl+O on song select to open the options");
            }, "60-options"),
            (() =>
            {
                click(o.SidebarButtonFor("Input"));
                after(() => o.IsSettled, "the Input section");
            }, "60b-options-input"),
            (() =>
            {
                click(o.SidebarButtonFor("Export"));
                // the converter is detected once per opening run (a Python process): wait for its answer
                waitUntil(() => o.ConverterDetection?.IsCompleted == true && !o.ConverterStatus.Text.Contains("checking"), "the converter detection", 60_000);
            }, null),
            (() =>
            {
                report("options: " + o.ConverterStatus.Text.Replace('\n', '/'));
                // audit P6: the options steppers use the panel's dark style, not the timing tab's bright blue
                foreach (var st in o.AllItems.OfType<OptionsStepper>())
                    expect(st.Stepper.Subdued, $"options: the stepper \"{st.Label}\" uses the dark style");
            }, "60c-options-export"),
            (() =>
            {
                // focus the search box by mouse like a user; the text is set (injected key presses carry no text input)
                click(o.SearchBox);
                o.Search.Value = "timing tab";
                after(() => o.VisibleItems.OfType<KeybindRow<EditorAction>>().Any(r => r.Action == EditorAction.TabTiming)
                            && o.VisibleItems.Count() < o.AllItems.Count() / 4, "the search \"timing tab\" to leave only a few rows, the Timing tab binding among them");
            }, "61-options-search"),
            (() =>
            {
                var r = row(EditorAction.TabTiming);
                click(r.Button(0));
                after(() => o.CaptureTarget == r && r.Button(0).Capturing, "a click on the binding button to wait for a key");
            }, "62-options-capture"),
            (() =>
            {
                key(Key.F8);
                after(() => timingIs("F8") && o.CaptureTarget == null && config.Get<string>(EditorSetting.EditorBindings) == "tab-timing=F8",
                    "F8 to be bound to the Timing tab and written to editor.ini at once");
            }, null),
            (() =>
            {
                // second slot: F1 belongs to the Compose tab → the row asks first and applies nothing yet
                click(row(EditorAction.TabTiming).Button(1));
                after(() => o.CaptureTarget != null, "the second slot to wait");
            }, null),
            (() =>
            {
                var r = row(EditorAction.TabTiming);
                key(Key.F1);
                after(() => r.Pending is { Conflicts: var c } && c.Contains(EditorAction.TabCompose) && timingIs("F8"), "F1 to be reported as taken by the Compose tab, not applied");
                // audit B5: the slot shows the key that was pressed, and the row's key boxes stay in line with a row
                // without a reset icon (the hidden icon keeps its column)
                after(() => r.Button(1).DisplayText == "F1", $"the waiting slot to show the pressed F1 (shows '{r.Button(1).DisplayText}')");
                if (o.VisibleItems.OfType<KeybindRow<EditorAction>>().FirstOrDefault(x => x != r && EditorKeyBindings.Table.IsDefault(x.Action)) is { } other)
                {
                    float dx = r.Button(0).ScreenSpaceDrawQuad.TopLeft.X - other.Button(0).ScreenSpaceDrawQuad.TopLeft.X;
                    expect(Math.Abs(dx) < 1, $"the key boxes of a changed row line up with an unchanged row's (off by {dx:0.#} px)");
                }
            }, "63-options-conflict"),
            (() =>
            {
                var r = row(EditorAction.TabTiming);
                click(r.ConflictButtons[2]); // Cancel
                after(() => r.Pending == null && EditorKeyBindings.Table.IsDefault(EditorAction.TabCompose) && timingIs("F8"), "Cancel to keep both bindings as they were");
            }, null),
            (() =>
            {
                click(row(EditorAction.TabTiming).Button(1));
                after(() => o.CaptureTarget != null, "the second slot to wait again");
            }, null),
            (() =>
            {
                key(Key.K, Key.LControl, Key.LShift);
                after(() => timingIs("F8", "Ctrl+Shift+K"), "Ctrl+Shift+K to be captured with its modifiers");
            }, null),
            (() =>
            {
                click(row(EditorAction.TabTiming).Button(1));
                after(() => o.CaptureTarget != null, "the second slot to wait once more");
            }, null),
            (() =>
            {
                key(Key.BackSpace);
                after(() => timingIs("F8"), "Backspace to clear the slot");
            }, null),
            (() =>
            {
                o.Search.Value = "bt-a";
                after(() => o.VisibleItems.Contains(row(PlayAction.BtA)), "the search \"bt-a\" to show the BT-A row");
            }, null),
            (() =>
            {
                click(row(PlayAction.BtA).Button(0));
                after(() => o.CaptureTarget == row(PlayAction.BtA), "the BT-A button to wait");
            }, null),
            (() =>
            {
                key(Key.Tab);
                after(() => row(PlayAction.BtA).LastError.Contains("fixed") && o.PlayBindings[PlayAction.BtA].SequenceEqual(new[] { "D" }),
                    "Tab to be refused for the playtest (it toggles autoplay there)");
            }, null),
            (() =>
            {
                click(row(PlayAction.BtA).Button(0));
                after(() => o.CaptureTarget != null, "the BT-A button to wait again");
            }, null),
            (() =>
            {
                key(Key.A);
                after(() => o.PlayBindings[PlayAction.BtA].SequenceEqual(new[] { "A" }) && playSetting().Contains("bt-a=A,"),
                    "A to be bound to BT-A and written to editor.ini at once");
            }, "64-options-rebind-playtest"),
            (() =>
            {
                key(Key.Escape);
                after(() => !o.IsOpen.Value, "Esc to close the options");
            }, null),
            (() => pushAndWait(new EditorScreen()), null),
            (() =>
            {
                moveTo(ed().BottomBar);
                key(Key.F8);
                after(() => ed().CurrentTab == EditorTab.Timing, "F8 to open the Timing tab in the editor");
            }, null),
            (() =>
            {
                ed().SelectTab(EditorTab.Compose);
                key(Key.F3);
                key(Key.F2);
                // F2 (untouched) must switch to Camera, and F3 before it must not have gone to Timing in between
                after(() => ed().CurrentTab == EditorTab.Camera, "the untouched F2 to open the Camera tab");
            }, null),
            (() =>
            {
                ed().SelectTab(EditorTab.Compose);
                key(Key.F3);
            }, null),
            (() =>
            {
                expect(ed().CurrentTab == EditorTab.Compose, $"F3 no longer opens the Timing tab (tab {ed().CurrentTab})");
                var dialog = ed().OpenShortcuts();
                after(() => dialog.IsLoaded, "the shortcuts dialog to load");
            }, "65-options-help-dialog"),
            (() =>
            {
                var dialog = (ShortcutsDialog)dialogOverlay.Current!;
                expect(dialog.Rows.Any(r => r.Description == "Timing tab" && r.Keys == "F8"), "Help → Keyboard Shortcuts shows F8 for the Timing tab");
                dialog.Cancel();
                ed().Playtest();
                waitForPlaytest();
            }, null),
            (() =>
            {
                press(Key.A);
                after(() => pt().IsHeld(PlayAction.BtA), "A to hold BT-A in the playtest");
            }, "66-options-playtest"),
            (() =>
            {
                release(Key.A);
                after(() => !pt().IsHeld(PlayAction.BtA), "releasing A to release BT-A");
            }, null),
            (() =>
            {
                press(Key.D);
                heldByA = true;
            }, null),
            (() =>
            {
                expect(heldByA && !pt().IsHeld(PlayAction.BtA), "D no longer holds BT-A");
                release(Key.D);
                pt().Exit();
            }, null),
            (() =>
            {
                // back in the editor: its own Options binding (Ctrl+Shift+O) opens the overlay
                expect(screenStack.CurrentScreen is EditorScreen, "back in the editor");
                key(Key.O, Key.LControl, Key.LShift);
                after(() => o.IsSettled, "Ctrl+Shift+O in the editor to open the options");
            }, null),
            (() =>
            {
                o.Search.Value = "timing tab";
                after(() => o.VisibleItems.Contains(row(EditorAction.TabTiming)), "the Timing tab row to show");
            }, null),
            (() =>
            {
                click(row(EditorAction.TabTiming).ResetButton);
                after(() => EditorKeyBindings.Table.AllDefault, "the row's reset button to restore F3");
            }, null),
            (() =>
            {
                o.Search.Value = "bt-a";
                after(() => o.VisibleItems.Contains(row(PlayAction.BtA)), "the BT-A row to show");
            }, null),
            (() =>
            {
                click(row(PlayAction.BtA).ResetButton);
                after(() => playSetting() == PlayBindings.DefaultText, "the playtest row's reset button to restore D");
            }, "67-options-reset"),
            (() =>
            {
                key(Key.Escape);
                after(() => !o.IsOpen.Value, "Esc to close the options");
            }, null),
            (() =>
            {
                key(Key.F3);
                after(() => ed().CurrentTab == EditorTab.Timing, "F3 to open the Timing tab again after the reset");
            }, null),
            (() =>
            {
                ed().SelectTab(EditorTab.Compose);
                ed().Exit();
                config.SetValue(EditorSetting.EditorBindings, editorBefore);
                config.SetValue(EditorSetting.PlaytestBindings, playBefore);
                expect(config.Get<string>(EditorSetting.EditorBindings) == editorBefore && EditorKeyBindings.OverrideText == EditorKeyBindings.Table.ToOverrideText(),
                    "the binding settings are back to what the run found");
                input().UseParentInput = true;
            }, null),
        };
    }
}
