using System.Globalization;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.StateChanges;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Input;
using SDVXEditor.Game.Overlays.Options;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Play;
using SDVXEditor.Game.Screens.SongSelect;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part <c>menus</c> (with <c>--open</c>, on a scratch copy of the song), all through input injected into
/// <see cref="SmokeInput"/>: every leaf of the editor's menu bar is clicked with the mouse (top-level item, submenus,
/// the item) and must change something visible — a dialog, the tab, a tool, a value, the window mode, the chart, at
/// least a toast — with stronger checks where the effect is known; dialogs, the playtest and the options are closed
/// again and toggles put back. Then File → Create a New Difficulty end to end twice (copy, empty: choice dialog →
/// slot dialog → Song Setup, chart file saved in the scratch song), Ctrl+O → Open Difficulty, Revert with its
/// confirmation, Ctrl+Z / Ctrl+Y on a simulated German QWERTZ layout (the key labelled Z is physical Y), Alt+Enter
/// through the root input manager (Windowed ⇄ Borderless, never exclusive fullscreen), and File → Exit with unsaved
/// changes (Cancel keeps the editor, Discard leaves).
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> menuSteps(string songFolder, int? slot, string? shots)
    {
        string work = Path.Combine(smokeWorkRoot(shots), "smoke-menus", Path.GetFileName(songFolder.TrimEnd('\\', '/')));

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException($"smoke test (menus): editor not open (got {screenStack.CurrentScreen?.GetType().Name})");
        osu.Framework.Testing.Input.ManualInputManager input() => SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");

        Exception? failure = null;
        bool sweepDone = false;
        int clicked = 0;
        var freeSlots = new List<int>();
        int notesBefore = 0;
        string? sourceChart = null;
        var windowMode = frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode);

        int notes(Chart c) => c.Notes.Bt.Concat(c.Notes.Fx).Sum(l => l.Count) + c.Notes.Lasers.Sum(l => l.Count);

        void click(Drawable d)
        {
            input().MoveMouseTo(d);
            Scheduler.AddDelayed(() => input().PressButton(MouseButton.Left), 40);
            Scheduler.AddDelayed(() => input().ReleaseButton(MouseButton.Left), 80);
        }

        // dialogs live outside the smoke input manager (DialogOverlay sits beside the scaling container), so a click on a
        // dialog button goes through the game's root input manager: the same path the window's own mouse input takes
        void clickDialog(Drawable d)
        {
            // (EditorGame.SmokeUiInput.cs: inputButton also claims the OS cursor is in the window, or the press is dropped)
            inputMoveTo(d.ScreenSpaceDrawQuad.Centre);
            Scheduler.AddDelayed(() => inputButton(true), 40);
            Scheduler.AddDelayed(() => inputButton(false), 80);
        }

        void key(Key k, params Key[] modifiers)
        {
            foreach (var m in modifiers) input().PressKey(m);
            input().PressKey(k);
            input().ReleaseKey(k);
            foreach (var m in modifiers.Reverse()) input().ReleaseKey(m);
        }

        // ---- the menu sweep: a self-driving chain on the scheduler (one leaf after the other) ----

        void fail(string what) => failure ??= new SmokeTestException("[menus sweep] " + what);

        void waitFor(Func<bool> cond, string what, double timeoutMs, Action then)
        {
            double deadline = Clock.CurrentTime + timeoutMs;

            void poll()
            {
                if (failure != null) return;

                bool ok;
                try
                {
                    ok = cond();
                }
                catch (Exception e)
                {
                    fail($"{what}: {e.Message}");
                    return;
                }

                if (ok) then();
                else if (Clock.CurrentTime > deadline) fail($"timed out: {what}");
                else Scheduler.AddDelayed(poll, 50);
            }

            Scheduler.AddDelayed(poll, 50);
        }

        // only items of menus that are open now: a closing submenu still fades out for a while and ignores clicks
        static Menu? owner(Drawable d)
        {
            var p = d.Parent;
            while (p != null && p is not Menu) p = p.Parent;
            return p as Menu;
        }

        Menu.DrawableMenuItem? drawableOf(MenuItem item) =>
            ed().MenuBar.ChildrenOfType<Menu.DrawableMenuItem>()
                .FirstOrDefault(d => d.Item == item && d.IsPresent && d.IsAlive && owner(d) is { State: MenuState.Open });

        // clicks the chain of items (top-level item, submenus, the leaf), waiting for each to be on screen
        void clickChain(IReadOnlyList<MenuItem> chain, int index, string path, Action done)
        {
            if (index >= chain.Count)
            {
                done();
                return;
            }

            var item = chain[index];

            // a submenu that is already open (hover opens them too) needs no click on its parent
            if (index < chain.Count - 1 && drawableOf(chain[index + 1]) != null)
            {
                clickChain(chain, index + 1, path, done);
                return;
            }

            waitFor(() => drawableOf(item) != null, $"menu item \"{item.Text.Value}\" of {path} on screen", 3000, () =>
            {
                click(drawableOf(item)!);
                Scheduler.AddDelayed(() => clickChain(chain, index + 1, path, done), 160);
            });
        }

        string fingerprint()
        {
            if (screenStack.CurrentScreen is not EditorScreen e) return "screen:" + screenStack.CurrentScreen?.GetType().Name;

            return string.Join("|",
                e.ToastCount, e.MenuActionsRun, dialogOverlay.OpenDialogs.Count, dialogOverlay.Current?.GetType().Name, e.CurrentTab, e.Tools.Tool.Value,
                e.Tools.LaserSide.Value, e.Playback.Rate.Value, e.Background.Dim.Value, windowMode.Value, e.Session.Version, e.Session.Selection.Count,
                e.Bookmarks.Count, e.ReferenceSession != null, OptionsOverlay.IsOpen.Value, e.LastOpenedFolder, e.Timing.MetronomeClicks.Value,
                e.Playback.CurrentTime.Value.ToString(CultureInfo.InvariantCulture), screenStack.CurrentScreen?.GetType().Name);
        }

        // known effects (stronger than "something changed") and the clean-up after an item
        (Func<bool>? Check, Action? Cleanup, double Timeout) expectation(string path, EditorScreen e)
        {
            var mode = windowMode.Value;
            int chartNotes = notes(e.Session.Chart);
            bool metronome = e.Timing.MetronomeClicks.Value;

            void cancelDialogs() => dialogOverlay.CloseAll();

            if (path.StartsWith("File > Export Song > ", StringComparison.Ordinal))
            {
                bool svz = path.Contains(".svz", StringComparison.Ordinal);
                int toasts = e.ToastCount;
                return (() => (svz || e.ExportsRunning == 0) && e.ToastCount > toasts
                                                           && (e.LastToast.StartsWith("Exported", StringComparison.Ordinal) || e.LastToast.Contains("failed", StringComparison.Ordinal)),
                    () => report($"menus: {path} → {e.LastToast}"), 180_000);
            }

            switch (path)
            {
                case "File > Create a New Difficulty...":
                    return (() => dialogOverlay.Current is ChoiceDialog { Title: "Create a New Difficulty" or "Unsaved changes" }, cancelDialogs, 5000);

                case "File > Open Difficulty...":
                    // a one-chart song (ABYSS MOTION: ksh + kson of the same chart) has nothing to choose from: a toast says so
                    return (() => dialogOverlay.Current is ChoiceDialog { Title: "Open Difficulty" or "Unsaved changes" }
                                  || e.Session.Charts.Count < 2 && e.LastToast.StartsWith("This song has no other difficulty", StringComparison.Ordinal), cancelDialogs, 5000);

                case "File > Save":
                    return (() => !e.Session.IsDirty, null, 5000);

                case "File > Clear All Notes":
                    return (() => notes(e.Session.Chart) == 0, () => e.TriggerAction(EditorAction.Undo), 5000);

                case "File > Test Chart":
                    return (() => screenStack.CurrentScreen is PlaytestScreen { IsLoaded: true }, () => ((PlaytestScreen)screenStack.CurrentScreen).Exit(), 30_000);

                case "File > Open Song Folder":
                    return (() => e.LastOpenedFolder == work, null, 5000);

                case "File > Options...":
                    return (() => OptionsOverlay.IsOpen.Value, () => OptionsOverlay.Close(), 5000);

                case "File > Revert to Saved":
                    return (null, cancelDialogs, 5000);

                case "View > Compose": return (() => e.CurrentTab == EditorTab.Compose, null, 5000);
                case "View > Camera": case "Camera > Camera Tab": return (() => e.CurrentTab == EditorTab.Camera, null, 5000);
                case "View > Timing": return (() => e.CurrentTab == EditorTab.Timing, null, 5000);
                case "View > Effects": return (() => e.CurrentTab == EditorTab.Effects, null, 5000);
                // audit U10 / U9: the "song setup" tab lights up while its dialog is open, and the dialog dims what is behind
                case "View > Song Setup...": return (() => dialogOverlay.Current is Screens.Edit.Dialogs.SongSetupDialog && e.LitTab == EditorTab.SongSetup
                                                            && dialogOverlay.CurrentBackdrop?.Alpha > 0.3f, cancelDialogs, 5000);
                case "View > Timing Setup...": case "Timing > Timing Setup...": case "Timing > Time Signature...":
                    return (() => dialogOverlay.Current is TimingControlPointsDialog, cancelDialogs, 5000);
                case "View > Audio Offset & Hitsounds...": case "Timing > Audio Offset & Hitsounds...":
                    return (() => dialogOverlay.Current is AudioOffsetDialog, cancelDialogs, 5000);
                case "View > Keyboard Shortcuts...": case "Help > Keyboard Shortcuts":
                    return (() => dialogOverlay.Current is ShortcutsDialog, cancelDialogs, 5000);
                case "Help > Tutorial...": return (() => dialogOverlay.Current is Tutorial.TutorialChapterListDialog, cancelDialogs, 5000); // the chapter list (EditorGame.SmokeTutorial.cs runs the tutorial)
                case "Help > About SDVX Chart Editor":
                    return (() => dialogOverlay.Current is AboutDialog && AboutDialog.Lines.Any(l => l.Contains("osu-framework") && l.Contains("MIT"))
                                                                   && AboutDialog.Lines.Any(l => l.Contains("Noto") && l.Contains("Open Font License")), cancelDialogs, 5000);
                case "Edit > Bookmarks...": return (() => dialogOverlay.Current is BookmarkListDialog, cancelDialogs, 5000);
                case "Timing > Insert / Delete Time...": return (() => dialogOverlay.Current is TimeShiftDialog, cancelDialogs, 5000);
                // without a selection Delete Selected Range opens the same dialog
                case "Timing > Delete Selected Range": return (null, cancelDialogs, 5000);
                case "Thanks": // top-level item without a submenu
                    return (() => dialogOverlay.Current is CreditsDialog && CreditsDialog.Credits.Sections.SelectMany(s => s.Entries).Any(x => x.Name == "Fax"), cancelDialogs, 5000);

                case "View > Borderless / Windowed":
                    return (() => windowMode.Value == EditorGame.ToggledWindowMode(mode), () => windowMode.Value = mode, 5000);

                case "Timing > Metronome Clicks":
                    return (() => e.Timing.MetronomeClicks.Value != metronome, () => e.Timing.MetronomeClicks.Value = metronome, 5000);
            }

            if (path.StartsWith("View > Background Dim > ", StringComparison.Ordinal))
            {
                float v = float.Parse(path["View > Background Dim > ".Length..].TrimEnd('%', ' '), CultureInfo.InvariantCulture) / 100;
                return (() => Math.Abs(e.Background.Dim.Value - v) < 0.001f, null, 5000);
            }

            if (path.StartsWith("Compose > Audio Rate > ", StringComparison.Ordinal))
            {
                double r = double.Parse(path["Compose > Audio Rate > ".Length..].TrimEnd('%', ' '), CultureInfo.InvariantCulture) / 100;
                return (() => Math.Abs(e.Playback.Rate.Value - r) < 0.001, () => e.Playback.Rate.Value = 1, 5000);
            }

            if (path.StartsWith("Compose > Tool > ", StringComparison.Ordinal))
            {
                string tool = path["Compose > Tool > ".Length..];
                return (() => e.CurrentTab == EditorTab.Compose && tool switch
                {
                    "Select" => e.Tools.Tool.Value == EditorTool.Select,
                    "BT Chip" => e.Tools.Tool.Value == EditorTool.BtChip,
                    "BT Hold" => e.Tools.Tool.Value == EditorTool.BtHold,
                    "FX Chip" => e.Tools.Tool.Value == EditorTool.FxChip,
                    "FX Hold" => e.Tools.Tool.Value == EditorTool.FxHold,
                    "Laser L" => e.Tools.Tool.Value == EditorTool.Laser && e.Tools.LaserSide.Value == 0,
                    _ => e.Tools.Tool.Value == EditorTool.Laser && e.Tools.LaserSide.Value == 1,
                }, null, 5000);
            }

            if (path.StartsWith("File > Open Difficulty for Reference > ", StringComparison.Ordinal) && !path.EndsWith("Close Reference", StringComparison.Ordinal)
                                                                                                    && !path.Contains("(no other", StringComparison.Ordinal))
                return (() => e.ReferenceSession != null, () => e.CloseReference(), 5000);

            if (path.StartsWith("Compose > ", StringComparison.Ordinal) && !path.Contains(" > Snap Divisor > ", StringComparison.Ordinal))
            {
                // the five compose toggles: flipped, then flipped back
                string text = path["Compose > ".Length..];
                return (() => e.LastToast.StartsWith(text, StringComparison.Ordinal), () =>
                {
                    e.SelectTab(EditorTab.Compose); // compose toggles act on the Compose tab only (T is tap tempo on Timing)
                    e.TriggerAction(text switch
                    {
                    "Grid Snapping" => EditorAction.ToggleGridSnap,
                    "Wide Laser" => EditorAction.ToggleWideLaser,
                    "Slam Assist" => EditorAction.ToggleSlamAssist,
                    "Lock Notes" => EditorAction.ToggleLockNotes,
                        _ => EditorAction.ToggleMirrorPreview,
                    });
                }, 5000);
            }

            return (null, null, 5000);
        }

        void sweep(List<string> paths, int i)
        {
            if (failure != null) return;

            if (i >= paths.Count)
            {
                sweepDone = true;
                return;
            }

            string path = paths[i];
            var e = ed();
            var leaf = e.MenuLeaves().FirstOrDefault(l => l.Path == path);
            if (leaf.Item == null)
            {
                fail($"menu item {path} disappeared");
                return;
            }

            // a known state for each item: compose tab at the chart's middle, nothing open
            if (dialogOverlay.HasOpenDialog) dialogOverlay.CloseAll();
            e.Playback.IsPlaying.Value = false;
            e.Playback.Loop.Value = null; // View > Loop Playback
            e.Inspector.Shown.Value = e.LayersPanel.Shown.Value = e.ChartCheck.Shown.Value = false; // the side panels
            e.Layers.ShowAll();

            string before = fingerprint();
            var (check, cleanup, timeout) = expectation(path, e);

            clickChain(leaf.Parents.Append(leaf.Item).ToList(), 0, path, () =>
            {
                clicked++;
                waitFor(() => fingerprint() != before && (check?.Invoke() ?? true), $"a visible effect of {path} (before {before}, now {fingerprint()})", timeout, () =>
                {
                    cleanup?.Invoke();
                    waitFor(() => screenStack.CurrentScreen is EditorScreen { IsLoaded: true } && !dialogOverlay.HasOpenDialog && !OptionsOverlay.IsOpen.Value,
                        $"the editor back in front after {path}", 30_000, () => Scheduler.AddDelayed(() => sweep(paths, i + 1), 120));
                });
            });
        }

        // ---- steps ----

        ChoiceDialog choice(string title) =>
            dialogOverlay.Current is ChoiceDialog d && d.Title == title ? d : throw new InvalidOperationException($"smoke test (menus): dialog \"{title}\" not open (got {dialogOverlay.Current?.Title ?? "none"})");

        bool choiceOpen(string title) => dialogOverlay.Current is ChoiceDialog d && d.Title == title && d.IsLoaded;

        void clickMenu(string path)
        {
            var leaf = ed().MenuLeaves().FirstOrDefault(l => l.Path == path);
            if (leaf.Item == null) throw new InvalidOperationException($"smoke test (menus): no menu item {path}");
            clickChain(leaf.Parents.Append(leaf.Item).ToList(), 0, path, () => { });
        }

        bool newDifficultyDone(int slotWanted, bool copy, out string why)
        {
            var e = ed();
            why = "";
            if (dialogOverlay.Current is not Screens.Edit.Dialogs.SongSetupDialog) { why = "Song Setup not open"; return false; }
            if (e.Session.ActiveChart?.Slot != slotWanted) { why = $"active slot {e.Session.ActiveChart?.Slot}"; return false; }
            string? file = e.Session.ChartPath;
            if (file == null || !File.Exists(file) || !file.StartsWith(work, StringComparison.OrdinalIgnoreCase)) { why = $"chart file {file}"; return false; }
            int saved = notes(KsonReader.ReadFile(file));
            if (copy ? saved != notesBefore : saved != 0) { why = $"saved chart has {saved} objects"; return false; }
            return true;
        }

        // after an Esc that closed something: it is gone and the editor is still open, with nothing else asked
        void escStill(Func<bool> closed, string what)
        {
            expect(closed(), $"Esc closes {what}");
            expect(screenStack.CurrentScreen is EditorScreen && !choiceOpen("Unsaved changes"), $"Esc on {what} stays in the editor (no exit prompt)");
            report($"menus: Esc closed {what}, editor still open - ok");
        }

        int exitSlot = 0;

        long scrollPointAt = 0;

        void addScrollSpeedPoint(EditorScreen e)
        {
            // a fresh spot each time: a second point at the same time is refused
            scrollPointAt += 4;
            e.Playback.CurrentTime.Value = e.Session.PulseToMs(e.Session.Timing.MeasureStart((int)(10 + scrollPointAt)));
            e.TriggerAction(EditorAction.AddScrollSpeedPoint);
        }

        int scrollPoints() => ed().Session.Chart.Beat.ScrollSpeed.Count;
        int scrollCount = 0;

        return new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder))
                    File.Copy(f, Path.Combine(work, Path.GetFileName(f)));

                var song = SongFolder.Load(work);
                var opened = (slot is int s ? song[s] : null) ?? song.Charts[^1];

                // the new-difficulty flows need two free slots: drop other charts from the copy if the song has all five
                foreach (var c in song.Charts.Where(c => c != opened).OrderByDescending(c => c.Slot))
                {
                    if (Core.Editing.NewDifficulty.FreeSlots(SongFolder.Load(work).Charts.Select(x => x.Slot)).Count >= 2) break;
                    File.Delete(c.FilePath);
                }

                var session = EditorSession.Load(work, opened.Slot);
                expect(session.Charts.Count >= 2 || Core.Editing.NewDifficulty.FreeSlots(session.Charts.Select(c => c.Slot)).Count >= 2, "menus smoke: song copy prepared");
                freeSlots.AddRange(Core.Editing.NewDifficulty.FreeSlots(session.Charts.Select(c => c.Slot)));
                pushAndWait(new EditorScreen(session));
                input().UseParentInput = false;
                report($"menus: working on a copy in {work}; keyboard layout system={KeyboardLayoutService.System.Name} "
                       + $"(Ctrl+Z → physical {KeyboardLayoutService.System.PhysicalKeyFor("Z")}), simulated={KeyboardLayoutService.Current.Name}");
            }, null),

            // ---- every menu leaf by mouse
            (() =>
            {
                var e = ed();
                var paths = e.MenuLeaves().Select(l => l.Path).Where(p => p != "File > Exit...").ToList();
                expect(paths.Count >= 60, $"the menu has every item ({paths.Count})");
                // a selection and a bookmark give the edit items something to act on
                e.TriggerAction(EditorAction.SelectAll);
                report($"menus: sweeping {paths.Count} menu items");
                Scheduler.AddDelayed(() => sweep(paths, 0), 100);
                waitUntil(() =>
                {
                    if (failure != null) throw failure;
                    return sweepDone;
                }, "the menu sweep", 900_000);
            }, null),
            (() =>
            {
                report($"menus: {clicked} items clicked, each with a visible effect");
                var e = ed();
                if (dialogOverlay.HasOpenDialog) dialogOverlay.CloseAll();
                e.SelectTab(EditorTab.Compose);
                // the edit items cut and deleted the selection: back to the file (saved by File > Save before them)
                e.TriggerAction(EditorAction.RevertToSaved);
                expect(!e.Session.IsDirty && notes(e.Session.Chart) > 0, "reverted to the saved chart before the new-difficulty flows");
                notesBefore = notes(e.Session.Chart);
                sourceChart = e.Session.ChartPath;
            }, null),

            // ---- File → Create a New Difficulty: copy
            (() =>
            {
                clickMenu("File > Create a New Difficulty...");
                waitUntil(() => choiceOpen("Create a New Difficulty"), "the new-difficulty choice", 5000);
            }, null),
            (() =>
            {
                clickDialog(choice("Create a New Difficulty").Button(choice("Create a New Difficulty").Labels.First(l => l.StartsWith("Copy", StringComparison.Ordinal))));
                waitUntil(() => choiceOpen("New Difficulty: Slot"), "the slot choice", 5000);
            }, "70-new-difficulty-slot"),
            (() =>
            {
                var d = choice("New Difficulty: Slot");
                expect(d.Labels.Contains(Difficulty.ShortName(freeSlots[0])) && !d.Labels.Contains(Difficulty.ShortName(ed().Session.ActiveChart!.Slot)), "only free slots are offered");
                clickDialog(d.Button(Difficulty.ShortName(freeSlots[0])));
                waitUntil(() => newDifficultyDone(freeSlots[0], true, out _), $"Song Setup on a saved copy in slot {Difficulty.ShortName(freeSlots[0])}", 10_000);
            }, "71-new-difficulty-song-setup"),
            (() =>
            {
                SongSetupDialog().Cancel();
                report($"menus: new difficulty (copy) → {Path.GetFileName(ed().Session.ChartPath)} with {notesBefore} objects, Song Setup opened");
            }, null),

            // ---- File → Create a New Difficulty: empty
            (() =>
            {
                clickMenu("File > Create a New Difficulty...");
                waitUntil(() => choiceOpen("Create a New Difficulty"), "the new-difficulty choice", 5000);
            }, null),
            (() =>
            {
                clickDialog(choice("Create a New Difficulty").Button("Empty chart (map from zero)"));
                waitUntil(() => choiceOpen("New Difficulty: Slot"), "the slot choice", 5000);
            }, null),
            (() =>
            {
                var d = choice("New Difficulty: Slot");
                expect(!d.Labels.Contains(Difficulty.ShortName(freeSlots[0])), "the slot taken by the copy is no longer offered");
                clickDialog(d.Button(Difficulty.ShortName(freeSlots[1])));
                waitUntil(() => newDifficultyDone(freeSlots[1], false, out _), $"Song Setup on a saved empty chart in slot {Difficulty.ShortName(freeSlots[1])}", 10_000);
            }, null),
            (() =>
            {
                SongSetupDialog().Cancel();
                var e = ed();
                expect(e.Session.Chart.Beat.Bpm.Count > 0 && e.Session.Chart.Meta.Title.Length > 0, "the empty chart keeps timing and metadata");
                report($"menus: new difficulty (empty) → {Path.GetFileName(e.Session.ChartPath)}, {e.Session.Charts.Count} charts in the song now");
            }, null),

            // ---- Ctrl+O → Open Difficulty (real key) → the original chart
            (() =>
            {
                key(Key.O, Key.LControl);
                waitUntil(() => choiceOpen("Open Difficulty"), "Ctrl+O to open the difficulty chooser", 5000);
            }, "72-open-difficulty"),
            (() =>
            {
                var d = choice("Open Difficulty");
                string label = d.Labels.First(l => l.Contains(Path.GetFileName(sourceChart!), StringComparison.Ordinal));
                clickDialog(d.Button(label));
                waitUntil(() => ed().Session.ChartPath == sourceChart, "switching back to the original difficulty", 5000);
            }, null),

            // ---- Revert to Saved with a change: confirmation → Revert
            (() =>
            {
                var e = ed();
                scrollCount = scrollPoints();
                addScrollSpeedPoint(e);
                expect(e.Session.IsDirty && scrollPoints() == scrollCount + 1, "a scroll speed point makes the chart dirty");
                clickMenu("File > Revert to Saved");
                waitUntil(() => choiceOpen("Revert to Saved"), "the revert confirmation", 5000);
            }, null),
            (() =>
            {
                clickDialog(choice("Revert to Saved").Button("Revert"));
                waitUntil(() => !ed().Session.IsDirty && scrollPoints() == scrollCount, "the revert", 5000);
            }, null),

            // ---- QWERTZ: the key labelled Z (physical Y) undoes, physical Z redoes
            (() =>
            {
                int reloads = ed().KeyBindings.Reloads;
                KeyboardLayoutService.Simulate(KeyboardLayout.Qwertz);
                waitUntil(() => ed().KeyBindings.Reloads > reloads, "the key bindings to follow the QWERTZ layout", 5000);
            }, null),
            (() =>
            {
                var e = ed();
                expect(EditorKeyBindings.ShortcutOf(EditorAction.Undo) == "Ctrl+Z", $"menus/help still say Ctrl+Z ({EditorKeyBindings.ShortcutOf(EditorAction.Undo)})");
                expect(EditorKeyBindings.ShortcutOf(EditorAction.ShowShortcuts) == "Ctrl+-", $"Ctrl+Slash is labelled with the QWERTZ key ({EditorKeyBindings.ShortcutOf(EditorAction.ShowShortcuts)})");
                var capture = new EditorKeybindScope().Translate(new CapturedInput(CapturedKind.Keyboard, Key.Y, Ctrl: true));
                expect(capture.Combo == "Ctrl+Z", $"the options capture stores physical Y as Ctrl+Z ({capture.Combo ?? capture.Error})");
                expect(new PlaytestKeybindScope().Describe("Z") == "Y", $"a physical playtest key shows its QWERTZ label ({new PlaytestKeybindScope().Describe("Z")})");
                scrollCount = scrollPoints();
                addScrollSpeedPoint(e);
                expect(scrollPoints() == scrollCount + 1, "scroll speed point added");
                key(Key.Y, Key.LControl); // physical Y = the key labelled Z on QWERTZ
                waitUntil(() => scrollPoints() == scrollCount, "Ctrl + (key labelled Z) to undo on QWERTZ", 5000);
            }, null),
            (() =>
            {
                key(Key.Z, Key.LControl); // physical Z = the key labelled Y on QWERTZ
                waitUntil(() => scrollPoints() == scrollCount + 1, "Ctrl + (key labelled Y) to redo on QWERTZ", 5000);
            }, null),
            (() =>
            {
                int reloads = ed().KeyBindings.Reloads;
                KeyboardLayoutService.Simulate(KeyboardLayout.Qwerty);
                waitUntil(() => ed().KeyBindings.Reloads > reloads, "the key bindings back on QWERTY", 5000);
                report("menus: QWERTZ simulation - Ctrl+(physical Y) undid, Ctrl+(physical Z) redid, menus show Ctrl+Z, Ctrl+Slash reads Ctrl+-, capture stores Ctrl+Z - ok");
            }, null),
            (() =>
            {
                key(Key.Z, Key.LControl); // back on QWERTY: physical Z undoes again
                waitUntil(() => scrollPoints() == scrollCount, "Ctrl+Z on QWERTY again", 5000);
            }, null),

            // ---- Alt+Enter through the root input manager: Windowed ⇄ Borderless
            (() =>
            {
                windowMode.Value = WindowMode.Windowed;
                int toggles = WindowKeyToggles;
                var root = RootInput ?? throw new InvalidOperationException("smoke test: no root input manager");
                new KeyboardKeyInput(Key.LAlt, true).Apply(root.CurrentState, root);
                new KeyboardKeyInput(Key.Enter, true).Apply(root.CurrentState, root);
                new KeyboardKeyInput(Key.Enter, false).Apply(root.CurrentState, root);
                new KeyboardKeyInput(Key.LAlt, false).Apply(root.CurrentState, root);
                expect(WindowKeyToggles == toggles + 1 && windowMode.Value == WindowMode.Borderless, $"Alt+Enter: Windowed → Borderless (got {windowMode.Value})");
                waitUntil(() => Host.Window?.WindowState != osu.Framework.Platform.WindowState.Fullscreen, "no exclusive fullscreen", 3000);
            }, "73-borderless"),
            (() =>
            {
                var root = RootInput!;
                new KeyboardKeyInput(Key.F11, true).Apply(root.CurrentState, root);
                new KeyboardKeyInput(Key.F11, false).Apply(root.CurrentState, root);
                expect(windowMode.Value == WindowMode.Windowed, $"F11: Borderless → Windowed (got {windowMode.Value})");
                report("menus: Alt+Enter Windowed → Borderless, F11 back to Windowed (framework cycle into exclusive fullscreen intercepted) - ok");
            }, null),

            // ---- File → Exit with unsaved changes: Cancel stays, Discard leaves
            (() =>
            {
                addScrollSpeedPoint(ed());
                expect(ed().Session.IsDirty, "dirty before Exit");
                clickMenu("File > Exit...");
                waitUntil(() => choiceOpen("Unsaved changes"), "the unsaved-changes prompt on Exit", 5000);
            }, "74-exit-prompt"),
            (() =>
            {
                clickDialog(choice("Unsaved changes").Button("Cancel"));
                waitUntil(() => !dialogOverlay.HasOpenDialog && screenStack.CurrentScreen is EditorScreen { Session.IsDirty: true }, "Cancel keeps the editor and the change", 5000);
            }, null),

            // ---- Esc like osu!: the topmost thing closes first, leaving comes last and asks about unsaved changes.
            // Real Esc presses through the root input manager (the screen layer passes the window input through again)
            (() =>
            {
                input().UseParentInput = true;
                ed().SelectTab(EditorTab.Compose);
                exitSlot = ed().Session.ActiveChart!.Slot;
                var file = ed().MenuLeaves().First(l => l.Path == "File > Save");
                inputSequence("Esc: open the File menu", clickAt(() => centreOf(drawableOf(file.Parents[0])!), () =>
                    expect(drawableOf(file.Item) != null, "a click on File opens the menu")));
            }, null),
            (() =>
            {
                var file = ed().MenuLeaves().First(l => l.Path == "File > Save");
                inputSequence("Esc closes the menu", keys(() => escStill(() => drawableOf(file.Item) == null, "an open menu"), Key.Escape));
            }, null),
            (() =>
            {
                var dd = (Graphics.UserInterface.OsuDropdown<string>)ed().Toolbox.FxEffectDropdown;
                inputSequence("Esc: open a dropdown", clickAt(() => centreOf(dd), () => expect(openPopupLayer != null, "a click on the FX Effect dropdown opens its list")));
            }, null),
            (() => inputSequence("Esc closes the dropdown", keys(() => escStill(() => openPopupLayer == null, "an open dropdown list"), Key.Escape)), null),
            (() =>
            {
                ed().OpenBookmarkList();
                waitUntil(() => dialogOverlay.Current is BookmarkListDialog { IsLoaded: true }, "the bookmark list", 5000);
            }, null),
            (() => inputSequence("Esc closes the dialog", keys(() => escStill(() => !dialogOverlay.HasOpenDialog, "an open dialog"), Key.Escape)), null),
            (() =>
            {
                var e = ed();
                var other = e.Session.Charts.FirstOrDefault(c => c != e.Session.ActiveChart);
                if (other == null) return;
                expect(e.OpenReference(other), "reference opened for the Esc check");
                inputSequence("Esc closes the reference", keys(() => escStill(() => ed().ReferenceSession == null, "the reference view"), Key.Escape));
            }, null),
            (() =>
            {
                ed().OpenOptions();
                waitUntil(() => OptionsOverlay.IsOpen.Value, "the options", 5000);
            }, null),
            (() => inputSequence("Esc closes the options", keys(() => escStill(() => !OptionsOverlay.IsOpen.Value, "the options panel"), Key.Escape)), null),
            (() => inputSequence("Esc with unsaved changes", keys(() =>
                expect(choiceOpen("Unsaved changes") && screenStack.CurrentScreen is EditorScreen, "Esc with nothing open and unsaved changes asks Save / Discard / Cancel"), Key.Escape)), "74b-esc-prompt"),
            (() => inputSequence("Esc on the prompt", keys(() =>
                expect(!dialogOverlay.HasOpenDialog && screenStack.CurrentScreen is EditorScreen { Session.IsDirty: true }, "Esc on the prompt cancels it: still in the editor, change kept"), Key.Escape)), null),
            (() =>
            {
                input().UseParentInput = false;
                report("menus: Esc closes menu, dropdown, dialog, reference and options one at a time, then asks about unsaved changes (Esc on the prompt = Cancel) - ok");
            }, null),
            (() =>
            {
                clickMenu("File > Exit...");
                waitUntil(() => choiceOpen("Unsaved changes"), "the unsaved-changes prompt on Exit", 5000);
            }, null),
            (() =>
            {
                clickDialog(choice("Unsaved changes").Button("Discard"));
                waitUntil(() => screenStack.CurrentScreen is SongSelectScreen, "Discard leaves the editor", 10_000);
            }, null),
            (() =>
            {
                input().UseParentInput = true;
                report("menus: File → Exit asks about unsaved changes (Cancel stays, Discard leaves) - ok");
                // a saved chart: Esc leaves at once, no prompt
                pushAndWait(new EditorScreen(EditorSession.Load(work, exitSlot)));
            }, null),
            (() =>
            {
                expect(screenStack.CurrentScreen is EditorScreen { Session.IsDirty: false }, "the saved copy reopened");
                inputSequence("Esc on a saved chart", keys(() =>
                    expect(screenStack.CurrentScreen is SongSelectScreen && !dialogOverlay.HasOpenDialog, "Esc on a saved chart leaves the editor without asking"), Key.Escape));
            }, null),
        };
    }
}
