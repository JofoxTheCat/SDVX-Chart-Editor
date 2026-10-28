using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.SongSelect;
using SDVXEditor.Game.Tutorial;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part <c>tutorial</c> (no <c>--open</c> needed; the tutorials make their own song folders in the smoke song
/// root): a scripted beginner runs BOTH tutorials — the main one (Fax's song) and the classic one — every chapter from
/// the chapter list to the end of chapter 7 with REAL input: screen-layer clicks, drags, keys and wheel through the smoke
/// <c>ManualInputManager</c>, the tutorial card and dialogs through the root input manager, doing exactly what each
/// step's <see cref="TutorialStep.Demo"/> says and clicking Next on information steps. Along the way: the migration of
/// the old per-chapter folders, the off-track hint + "Take me back", Back/review mode, the progress ticks per tutorial
/// in editor.ini, ONE song folder per tutorial after every chapter, a single chapter (4) started from the editor's
/// Help → Tutorial…, Reset on chapter 1 (with its "Replace?" question, since chapter 4's saved work would go) and
/// Exit tutorial. Screenshots of representative steps of both tutorials.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> tutorialSteps(string? shots)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        var progressBefore = new Dictionary<TutorialCourse, string>();
        bool playEffectsBefore = config.Get<bool>(EditorSetting.PlayEffects); // the effects chapter ticks "play effects during playback"
        bool chainDone = false;
        SmokeTestException? chainFailure = null;
        int stepsRun = 0, demos = 0, confirms = 0;
        var started = DateTime.Now;
        var main = TutorialCourses.Main;
        var classic = TutorialCourses.Classic;
        string root = Library.Library.EditorRoot;

        SongSelectScreen songSelect() => screenStack.CurrentScreen as SongSelectScreen ?? throw new InvalidOperationException($"smoke test (tutorial): song select expected, got {screenStack.CurrentScreen?.GetType().Name}");
        TutorialChapterListDialog list() => dialogOverlay.Current as TutorialChapterListDialog ?? throw new InvalidOperationException("smoke test (tutorial): chapter list not open");
        string state() => Tutorial.IsRunning
            ? $"{Tutorial.Course.Id} chapter {Tutorial.Chapter?.Number} step {Tutorial.StepIndex + 1}/{Tutorial.Steps.Count} \"{Tutorial.Step?.Title}\"{(Tutorial.ChapterComplete ? " (complete)" : "")}; "
              + $"off-track '{Tutorial.OffTrack}'; progress '{Tutorial.ProgressText}'; screen {screenStack.CurrentScreen?.GetType().Name}; dialog {dialogOverlay.Current?.GetType().Name}"
            : "tutorial not running";

        // every folder in the songs root that a tutorial could have made (one per tutorial, never one per chapter)
        string[] tutorialFolders() => Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Select(Path.GetFileName).OfType<string>().Where(n => n.StartsWith("Tutorial", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

        bool allShots = shots != null && Environment.GetEnvironmentVariable("SDVXEDITOR_TUTORIAL_SHOTS") == "all";
        static string slug(string t) => new string(t.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        int lastTaskShot = -1;
        Tutorial.StateChanged += () =>
        {
            // every task of a step gets its own screenshot (the spotlight moves with it)
            if (!allShots || !Tutorial.IsRunning || Tutorial.CurrentTask == null || Tutorial.TaskIndex == 0) return;
            int k = Tutorial.Chapter!.Number * 10000 + Tutorial.StepIndex * 100 + Tutorial.TaskIndex;
            if (k == lastTaskShot) return;
            lastTaskShot = k;
            string n = FormattableString.Invariant($"all-{Tutorial.Course.Id}-c{Tutorial.Chapter.Number}-{Tutorial.StepIndex + 1:00}-{slug(Tutorial.Step!.Title)}-t{Tutorial.TaskIndex + 1}");
            Scheduler.AddDelayed(() => shot(n), 450); // after the spotlight has glided there
        };

        void shot(string name)
        {
            if (shots != null) pendingScreenshots.Add(saveScreenshot(Path.Combine(shots, "tutorial-" + name + ".png")));
        }

        // ---- the self-driving chain: one chapter after the other ------------------------------------

        void fail(string what)
        {
            if (chainFailure != null) return;
            string focus = $"; tab {(screenStack.CurrentScreen as EditorScreen)?.CurrentTab}; toast '{(screenStack.CurrentScreen as EditorScreen)?.LastToast}'; "
                           + $"focus root {rootInput.FocusedDrawable?.GetType().Name}, smoke {im.FocusedDrawable?.GetType().Name}";
            chainFailure = new SmokeTestException("[tutorial chain] " + what + " — " + state() + focus);
            shot("zz-failure");
        }

        void waitFor(Func<bool> cond, string what, double timeoutMs, Action then)
        {
            double deadline = Clock.CurrentTime + timeoutMs;

            void poll()
            {
                if (chainFailure != null) return;
                bool ok;
                try
                {
                    ok = cond();
                }
                catch (Exception e)
                {
                    fail($"while waiting for {what}: {e.Message}");
                    return;
                }

                if (ok)
                {
                    then();
                    return;
                }

                if (Clock.CurrentTime > deadline)
                {
                    fail("timed out waiting for " + what);
                    return;
                }

                Scheduler.AddDelayed(poll, 40);
            }

            poll();
        }

        // which screenshots, by tutorial, chapter and step title (taken when the step starts, before the demo)
        var wanted = new Dictionary<(string, int, string), string>
        {
            [("main", 0, "Set the BPM")] = "main-01-spotlight-bpm",
            [("main", 1, "Place chips")] = "main-03-ghost-chips",
            [("main", 1, "Resize a hold")] = "main-04-resize-hold",
            [("main", 2, "A slam")] = "main-05-ghost-slam",
            [("main", 2, "Anchor types")] = "main-06-anchor-types",
            [("main", 3, "Modding timestamps")] = "main-07-modding-timestamp",
            [("main", 4, "Camera anchor types")] = "main-08-camera-anchor-types",
            [("main", 6, "Your own section")] = "main-09-free-mapping-drop",
            [("main", 0, "Put it at the drop")] = "main-02-f6-speed-jump",
            [("main", 1, "Beat snap")] = "main-12-beat-snap-colours",
            [("main", 2, "Wide lasers")] = "main-13-wide-laser-tasks",
            [("main", 4, "Zoom in at the drop")] = "main-14-camera-ghosts",
            [("main", 5, "Select the FX hold")] = "main-15-select-fx",
            [("main", 6, "The skill radar")] = "main-16-skill-radar",
            [("main", 7, "Options")] = "main-17-options-in-song-select",
            [("classic", 1, "Place chips")] = "classic-01-ghost-chips",
            [("classic", 2, "Curves")] = "classic-02-ghost-curve",
        };

        void runChapters(int last, Action done)
        {
            void loop()
            {
                if (chainFailure != null) return;

                if (!Tutorial.IsRunning)
                {
                    fail("the tutorial stopped");
                    return;
                }

                var course = Tutorial.Course;
                int chapter = Tutorial.Chapter!.Number;

                if (Tutorial.ChapterComplete)
                {
                    if (chapter == 0 && course == main) shot("main-00-chapter-complete");
                    // the chapter has exactly the steps its time budget was planned for (Core TutorialPlan / MainTutorialPlan, docs/TUTORIAL.md)
                    int planned = course.Plan[chapter].Steps;
                    if (Tutorial.Steps.Count != planned)
                    {
                        fail($"{course.Id} chapter {chapter} has {Tutorial.Steps.Count} steps, its plan says {planned}");
                        return;
                    }

                    // one song folder per tutorial: every chapter so far worked in the same one
                    var folders = tutorialFolders();
                    if (!folders.Contains(course.FolderName) || folders.Any(f => f.StartsWith("Tutorial (Chapter", StringComparison.Ordinal)))
                    {
                        fail($"after {course.Id} chapter {chapter} the songs root holds the tutorial folders [{string.Join(", ", folders)}]");
                        return;
                    }

                    var panel = TutorialOverlay.Panel;
                    if (chapter < last)
                    {
                        clickRoot(() => panel.Buttons.GetValueOrDefault("Next chapter"));
                        waitFor(() => Tutorial.IsRunning && Tutorial.Chapter?.Number == chapter + 1 && !Tutorial.ChapterComplete, $"chapter {chapter + 1} to start", 15_000, loop);
                    }
                    else
                    {
                        clickRoot(() => panel.Buttons.GetValueOrDefault("Close"));
                        waitFor(() => !Tutorial.IsRunning, "the tutorial to close", 5000, done);
                    }

                    return;
                }

                var step = Tutorial.Step!;
                int index = Tutorial.StepIndex;
                var ctx = Tutorial.Context!;

                // the step may need the editor that the chapter just opened: wait until the user would be "on track"
                waitFor(() => Tutorial.OffTrack == null && TutorialOverlay.Panel.Buttons.Count > 0, $"step \"{step.Title}\" to be on track", 20_000, () =>
                    Scheduler.AddDelayed(() =>
                    {
                        if (wanted.TryGetValue((course.Id, chapter, step.Title), out string? name)) shot(name);
                        if (allShots) shot(FormattableString.Invariant($"all-{course.Id}-c{chapter}-{index + 1:00}-{slug(step.Title)}"));

                        // a step that names something to highlight must show it (before its user does anything)
                        if (!Tutorial.StepSolved && Tutorial.OffTrack == null && TutorialOverlay.TargetMissing)
                        {
                            fail($"spotlight: {course.Id} chapter {chapter} step \"{step.Title}\" names a target that is not on screen");
                            return;
                        }

                        beforeDemo(chapter, step, () =>
                        {
                            var demo = new SmokeTutorialDemo(this, im, s => fail(s));
                            if (!Tutorial.StepSolved && Tutorial.StepIndex == index) step.Demo?.Invoke(demo, ctx); // a step already solved on entry needs nobody
                            if (step.Demo != null) demos++;

                            demo.Run(() =>
                            {
                                if (chainFailure != null) return;
                                // information steps (and steps whose demo leaves them open) end with Next
                                if (step.IsInformation && Tutorial.StepIndex == index && Tutorial.Chapter?.Number == chapter && !Tutorial.ChapterComplete)
                                    clickRoot(() => TutorialOverlay.Panel.Buttons.GetValueOrDefault("Next"));

                                waitFor(() => !Tutorial.IsRunning || Tutorial.ChapterComplete || Tutorial.StepIndex != index || Tutorial.Chapter?.Number != chapter,
                                    $"{course.Id} chapter {chapter} step {index + 1} \"{step.Title}\" to finish", 20_000, () =>
                                    {
                                        stepsRun++;
                                        loop();
                                    });
                            });
                        });
                    }, 250));
            }

            loop();
        }

        // robustness checks woven into the run (all with real input; once, in the first tutorial run)
        void beforeDemo(int chapter, TutorialStep step, Action then)
        {
            if (chapter == 1 && step.Title == "Place chips" && !checkedOffTrack)
            {
                checkedOffTrack = true;
                // leave the compose tab on purpose: the card says how to get back, "Take me back" does it
                var leave = new SmokeTutorialDemo(this, im, s => fail(s));
                leave.Press(Screens.Edit.Input.EditorAction.TabTiming);
                leave.Run(() => { });
                waitFor(() => Tutorial.OffTrack != null && TutorialOverlay.Panel.Buttons.ContainsKey("Take me back"), "the off-track hint after F3", 5000, () =>
                {
                    report($"tutorial: off-track hint shown: \"{Tutorial.OffTrack}\"");
                    shot("main-10-off-track-hint");
                    clickRoot(() => TutorialOverlay.Panel.Buttons["Take me back"]);
                    waitFor(() => Tutorial.OffTrack == null && screenStack.CurrentScreen is EditorScreen { CurrentTab: EditorTab.Compose }, "\"Take me back\" to return to compose", 5000, then);
                });
                return;
            }

            if (chapter == 1 && step.Title == "Holds" && !checkedBack)
            {
                checkedBack = true;
                int index = Tutorial.StepIndex;
                clickRoot(() => TutorialOverlay.Panel.Buttons.GetValueOrDefault("Back"));
                waitFor(() => Tutorial.StepIndex == index - 1 && Tutorial.ReviewMode && TutorialOverlay.Panel.Buttons.ContainsKey("Next"), "Back to the previous step in review mode", 5000, () =>
                {
                    // its condition holds already (the chips are there): review mode must not jump forward by itself
                    Scheduler.AddDelayed(() =>
                    {
                        if (Tutorial.StepIndex != index - 1) fail("a step entered with Back advanced by itself");
                        clickRoot(() => TutorialOverlay.Panel.Buttons.GetValueOrDefault("Next"));
                        waitFor(() => Tutorial.StepIndex == index && !Tutorial.ReviewMode, "Next back to the Holds step", 5000, then);
                    }, 900);
                });
                return;
            }

            then();
        }

        void clickRoot(Func<Drawable?> target)
        {
            var d = target();
            if (d == null)
            {
                fail("a tutorial button to click is missing");
                return;
            }

            inputMoveTo(d.ScreenSpaceDrawQuad.Centre);
            Scheduler.AddDelayed(() =>
            {
                inputMoveTo(d.ScreenSpaceDrawQuad.Centre); // where it is now (a card that just grew may have moved)
                inputButton(true);
            }, 60);
            Scheduler.AddDelayed(() => inputButton(false), 120);
        }

        Drawable? helpTutorialItem()
        {
            if (screenStack.CurrentScreen is not EditorScreen e) return null;
            var leaf = e.MenuLeaves().FirstOrDefault(l => l.Path == "Help > Tutorial...");
            if (leaf.Item == null) return null;
            var help = leaf.Parents[0];
            var helpDrawable = e.MenuBar.ChildrenOfType<Menu.DrawableMenuItem>().FirstOrDefault(d => d.Item == help && d.IsPresent);
            var item = e.MenuBar.ChildrenOfType<Menu.DrawableMenuItem>().FirstOrDefault(d => d.Item == leaf.Item && d.IsPresent && TutorialAnchors.IsShown(d));
            return item ?? helpDrawable;
        }

        bool helpOpen() => screenStack.CurrentScreen is EditorScreen e && e.MenuLeaves().FirstOrDefault(l => l.Path == "Help > Tutorial...") is { Item: { } it }
                                                                      && e.MenuBar.ChildrenOfType<Menu.DrawableMenuItem>().Any(d => d.Item == it && TutorialAnchors.IsShown(d));

        var asked = new HashSet<ChoiceDialog>();

        // a start from the chapter list may first ask "Replace?" (the folder holds the user's work): click Replace
        bool runningOrConfirm(int chapter)
        {
            if (dialogOverlay.Current is ChoiceDialog { Title: "Tutorial" } ask && ask.Labels.Contains("Replace") && asked.Add(ask))
            {
                confirms++;
                report($"tutorial: \"Replace?\" asked before chapter {chapter}");
                inputSequence("tutorial: Replace", clickAt(() => ask.Button("Replace").ScreenSpaceDrawQuad.Centre));
                return false;
            }

            // the chart too: under load the chapter's editor can be loaded a frame before its chart is set
            return Tutorial.IsRunning && Tutorial.Chapter?.Number == chapter && Tutorial.Context?.Editor?.IsLoaded == true
                   && Tutorial.Context.Chart != null;
        }

        // one full run of a tutorial, from Start on chapter 0 in its list to the end of chapter 7
        List<(Action, string?)> fullRun(TutorialCourse course) => new List<(Action, string?)>
        {
            (() =>
            {
                if (list().Course != course)
                    inputSequence($"tutorial: tab \"{course.Name}\"", clickAt(() => list().CourseTabs[course.Id].ScreenSpaceDrawQuad.Centre));
            }, null),
            (() => waitUntil(() => dialogOverlay.Current is TutorialChapterListDialog { IsLoaded: true } l && l.Course == course, $"the chapter list of {course.Id}", 5000), null),
            (() =>
            {
                expect(list().StartButtons.Count == course.Count && list().Ticks.Count == course.Count && list().CourseTabs.Count == TutorialCourses.All.Count,
                    $"the {course.Id} chapter list shows every chapter with a tick and a tab per tutorial");
            }, $"tutorial-{course.Id}-chapter-list"),
            (() => inputSequence($"tutorial: Start {course.Id} chapter 0", clickAt(() => list().StartButtons[0].ScreenSpaceDrawQuad.Centre)), null),
            (() => waitUntil(() => Tutorial.IsRunning && Tutorial.Course == course && Tutorial.Chapter?.Number == 0, $"{course.Id} chapter 0 to start", 5000), null),
            (() =>
            {
                chainDone = false;
                stepsRun = 0;
                demos = 0;
                started = DateTime.Now;
                runChapters(course.Count - 1, () => chainDone = true);
                waitUntil(() => chainDone || chainFailure != null, $"every {course.Id} tutorial chapter to be completed with real input", 1_200_000);
            }, null),
            (() =>
            {
                if (chainFailure != null) throw chainFailure;
                var done = Tutorial.CompletedOf(course);
                int total = course.Plan.Sum(p => p.Steps);
                expect(done.Count == course.Count, $"all {course.Id} chapters ticked (got {string.Join(",", done.OrderBy(n => n))})");
                expect(config.Get<string>(course.ProgressSetting) == string.Join(",", Enumerable.Range(0, course.Count)), $"{course.Id} progress is stored in editor.ini under its own key");
                expect(Tutorial.StepsSkipped == 0, $"no step was skipped ({Tutorial.StepsSkipped})");
                expect(stepsRun == total, $"every planned {course.Id} step ran once ({stepsRun} of {total})");
                expect(Math.Abs(course.Chapters.Sum(c => c.Minutes) - course.Plan.Sum(p => p.Minutes)) < 1e-9, "the chapter list shows the planned minutes");
                var marker = Tutorial.WorkspaceFor(course).State().Marker;
                expect(marker is { Chapter: 7 } m && m.Course == course.Id, $"the {course.Id} folder's marker says chapter 7 ({marker})");
                report(FormattableString.Invariant($"tutorial: {course.Id}: all {course.Count} chapters completed with real input: {stepsRun} steps ({demos} with a scripted user action), {(DateTime.Now - started).TotalSeconds:0} s; one song folder \"{course.FolderName}\" ([{string.Join(", ", tutorialFolders())}]); {SmokeTutorialDemo.SpotChecks} clicks/drags checked inside the spotlight"));
            }, null),
        };

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                expect(screenStack.CurrentScreen is SongSelectScreen, "tutorial part starts on song select");
                foreach (var c in TutorialCourses.All)
                {
                    progressBefore[c] = config.Get<string>(c.ProgressSetting);
                    Tutorial.ResetProgress(c);
                    Tutorial.WorkspaceFor(c).DeleteAll();
                }

                // migration: the per-chapter folders of the first version go, anything not recognisably the tutorial's stays
                var classicWs = Tutorial.WorkspaceFor(classic);
                string legacy = Path.Combine(root, "Tutorial (Chapter 3)"), foreign = Path.Combine(root, "Tutorial (Chapter 9)");
                Directory.CreateDirectory(legacy);
                Directory.CreateDirectory(foreign);
                using (var audio = classicWs.OpenAudio()!)
                {
                    using (var f = File.Create(Path.Combine(legacy, "tutorial.ogg"))) audio.CopyTo(f);
                    audio.Position = 0;
                    using (var f = File.Create(Path.Combine(foreign, "tutorial.ogg"))) audio.CopyTo(f);
                    audio.Position = 0;
                    Directory.CreateDirectory(classicWs.StagingFolder);
                    using (var f = File.Create(Path.Combine(classicWs.StagingFolder, "Tutorial (Chapter 0).ogg"))) audio.CopyTo(f);
                }

                File.WriteAllText(Path.Combine(legacy, "exh.kson"), "{}");
                File.WriteAllText(Path.Combine(foreign, "exh.kson"), "{}");
                File.WriteAllBytes(Path.Combine(foreign, "jacket.png"), new byte[] { 1, 2, 3 });
                int removed = TutorialWorkspace.MigrateLegacy(root);
                expect(removed == 1 && !Directory.Exists(legacy) && Directory.Exists(foreign) && !File.Exists(Path.Combine(classicWs.StagingFolder, "Tutorial (Chapter 0).ogg")),
                    $"migration removed the old tutorial folder and staged audio, kept the folder with a foreign file (removed {removed})");
                Directory.Delete(foreign, true);
                report("tutorial: migration of the per-chapter folders - ok (own folder + staged audio removed, a folder with a foreign file kept)");

                setHostInput(false);
                im.UseParentInput = false;
                waitUntil(() => songSelect().LibraryLoaded && TutorialAnchors.Resolve("songselect.tutorial") != null, "the song select Tutorial button", 20_000);
            }, null),

            // the footer button (screen layer, smoke input) opens the chapter list: the new tutorial first
            (() =>
            {
                var button = TutorialAnchors.Resolve("songselect.tutorial")!;
                inputSequence("tutorial: footer button", (() => im.MoveMouseTo(button.ScreenSpaceDrawQuad.Centre), 50), (() => im.PressButton(osuTK.Input.MouseButton.Left), 50),
                    (() => im.ReleaseButton(osuTK.Input.MouseButton.Left), 80));
                waitUntil(() => dialogOverlay.Current is TutorialChapterListDialog { IsLoaded: true }, "the chapter list", 5000);
            }, null),
            (() => expect(list().Course == main && TutorialCourses.All[0] == main, "the chapter list opens on the new tutorial, listed first"), null),
        };

        steps.AddRange(fullRun(main));
        steps.AddRange(new List<(Action, string?)>
        {
            // ---- a single chapter, started directly from the editor's Help → Tutorial…
            (() =>
            {
                // the main tutorial ends in song select (its options steps go back there: Ctrl+O = options only there)
                expect(screenStack.CurrentScreen is SongSelectScreen, $"main chapter 7 ends in song select ({screenStack.CurrentScreen?.GetType().Name})");
                screenStack.Push(new EditorScreen(EditorSession.Load(Tutorial.WorkspaceFor(main).Folder, Difficulty.Exhaust)));
                waitUntil(() => screenStack.CurrentScreen is EditorScreen { IsLoaded: true }, "the tutorial song in the editor", 10_000);
            }, null),
            (() =>
            {
                inputSequence("tutorial: Help menu", (() => im.MoveMouseTo(((EditorScreen)screenStack.CurrentScreen!).MenuBar.ChildrenOfType<Menu.DrawableMenuItem>().First(d => d.Item.Text.Value.ToString() == "Help").ScreenSpaceDrawQuad.Centre), 60),
                    (() => im.PressButton(osuTK.Input.MouseButton.Left), 50), (() => im.ReleaseButton(osuTK.Input.MouseButton.Left), 120));
                waitUntil(helpOpen, "the Help menu", 3000);
            }, null),
            (() =>
            {
                inputSequence("tutorial: Help → Tutorial…", (() => im.MoveMouseTo(helpTutorialItem()!.ScreenSpaceDrawQuad.Centre), 80),
                    (() => im.PressButton(osuTK.Input.MouseButton.Left), 50), (() => im.ReleaseButton(osuTK.Input.MouseButton.Left), 120));
                waitUntil(() => dialogOverlay.Current is TutorialChapterListDialog { IsLoaded: true }, "the chapter list from the Help menu", 5000);
            }, null),
            (() =>
            {
                expect(list().Course == main && list().Ticks.All(t => t.Icon.Equals(osu.Framework.Graphics.Sprites.FontAwesome.Solid.CheckCircle)), "every chapter of the new tutorial shows its tick");
                inputSequence("tutorial: Again on chapter 4", clickAt(() => list().StartButtons[4].ScreenSpaceDrawQuad.Centre));
            }, "tutorial-main-11-chapter-list-done"),
            (() => waitUntil(() => runningOrConfirm(4), "chapter 4 on its own, in the tutorial's song folder", 15_000), null),
            (() =>
            {
                chainDone = false;
                var chart = Tutorial.Context!.Chart!;
                var slam = Tutorial.Context.Targets.Slam!;
                expect(chart.Notes.Lasers[slam.Lane].Any(s => s.SlamCount > 0) && chart.Camera.Body.ZoomBottom.Count == 0 && chart.Camera.Tilt.Count == 0,
                    "\"Again\" starts chapter 4 from its starting state (chapter 2's slam, no camera points)");
                expect(tutorialFolders().SequenceEqual(new[] { main.FolderName }), $"chapter 4 reused the one folder ([{string.Join(", ", tutorialFolders())}])");
                runChapters(4, () => chainDone = true);
                waitUntil(() => chainDone || chainFailure != null, "chapter 4 alone", 300_000);
            }, null),
            (() =>
            {
                if (chainFailure != null) throw chainFailure;
                expect(!Tutorial.IsRunning, "Close ends the tutorial");
            }, null),

            // ---- Reset restores a chapter's starting state (asking first: chapter 4's saved camera work would go); Exit tutorial stops it where the user is
            (() =>
            {
                Tutorial.ShowChapterList(main);
                waitUntil(() => dialogOverlay.Current is TutorialChapterListDialog { IsLoaded: true }, "the chapter list", 5000);
            }, null),
            (() =>
            {
                confirms = 0;
                inputSequence("tutorial: Reset chapter 1", clickAt(() => list().ResetButtons[1].ScreenSpaceDrawQuad.Centre));
            }, null),
            (() => waitUntil(() => runningOrConfirm(1), "chapter 1 after Reset", 15_000), null),
            (() =>
            {
                expect(confirms == 1, $"Reset asked before replacing chapter 4's saved work ({confirms} questions)");
                var chart = Tutorial.Context!.Chart!;
                var t = Tutorial.Context.Targets;
                expect(chart.Notes.Bt[t.StrayChip.Lane].Any(n => n.Y == t.StrayChip.Y) && TutorialChecks.CountPlaced(chart, t.Chips) == 0 && TutorialChecks.IsPlaced(chart, t.ResizeStart!)
                       && chart.Camera.Body.ZoomBottom.Count == 0,
                    "Reset wrote chapter 1's starting chart again (stray chip, no chips in bar 1, the short hold, no camera)");
                expect(TutorialOverlay.Spotlight.Width > 0 || Tutorial.Step!.Anchor != null, "the first step spotlights the playfield");
                inputSequence("tutorial: Exit tutorial", clickAt(() => TutorialOverlay.Panel.Buttons["Exit tutorial"].ScreenSpaceDrawQuad.Centre));
            }, null),
            (() => waitUntil(() => !Tutorial.IsRunning && screenStack.CurrentScreen is EditorScreen, "Exit tutorial to stop and leave the user in the editor", 5000), null),
            (() =>
            {
                ((EditorScreen)screenStack.CurrentScreen!).Exit();
                waitUntil(() => screenStack.CurrentScreen is SongSelectScreen, "song select", 5000);
            }, null),
            (() =>
            {
                report("tutorial: main: single chapter (4) from Help → Tutorial…, Reset (chapter 1 back to its start, after a \"Replace?\"), Exit tutorial - ok");
                // the classic tutorial: its tab in the same list
                Tutorial.ShowChapterList(main);
                waitUntil(() => dialogOverlay.Current is TutorialChapterListDialog { IsLoaded: true }, "the chapter list", 5000);
            }, null),
        });
        steps.AddRange(fullRun(classic));
        steps.Add((() =>
        {
            var folders = tutorialFolders();
            expect(folders.SequenceEqual(new[] { main.FolderName, classic.FolderName }.OrderBy(n => n, StringComparer.Ordinal)),
                $"after both tutorials the songs root holds exactly one folder per tutorial ([{string.Join(", ", folders)}])");
            string staging = Tutorial.WorkspaceFor(main).StagingFolder;
            var staged = Directory.Exists(staging) ? Directory.EnumerateFiles(staging).Select(Path.GetFileName).OfType<string>().OrderBy(n => n).ToArray() : Array.Empty<string>();
            expect(staged.All(n => n == Path.GetFileName(Tutorial.WorkspaceFor(main).StagedAudio) || n == Path.GetFileName(Tutorial.WorkspaceFor(classic).StagedAudio)),
                $"\"Tutorial files\" holds one staged audio per tutorial at most ([{string.Join(", ", staged)}])");
            expect(screenStack.CurrentScreen is EditorScreen, "the classic chapter 7 ends in the editor");
            ((EditorScreen)screenStack.CurrentScreen!).Exit();
            waitUntil(() => screenStack.CurrentScreen is SongSelectScreen, "song select", 5000);
        }, null));
        steps.Add((() =>
        {
            im.UseParentInput = true;
            setHostInput(true);
            config.SetValue(EditorSetting.PlayEffects, playEffectsBefore); // later parts (dsp) expect the user's setting
            foreach (var c in TutorialCourses.All)
            {
                Tutorial.WorkspaceFor(c).DeleteAll();
                config.SetValue(c.ProgressSetting, progressBefore.GetValueOrDefault(c) ?? string.Empty);
            }

            _ = Library.ScanAsync();
            report($"tutorial: both tutorials replayed with real input, one song folder each; {Tutorial.StepsAdvanced} steps advanced in all");
        }, null));
        return steps;
    }

    private bool checkedOffTrack, checkedBack;

    /// <summary>The scripted "user" of a tutorial step: queues real input and runs it one action after another.</summary>
    private sealed class SmokeTutorialDemo : ITutorialDemo
    {
        private readonly EditorGame game;
        private readonly osu.Framework.Testing.Input.ManualInputManager im;
        private readonly Action<string> fail;
        private readonly List<(Action Act, double After, Func<bool>? Wait, string What)> queue = new List<(Action, double, Func<bool>?, string)>();

        public SmokeTutorialDemo(EditorGame game, osu.Framework.Testing.Input.ManualInputManager im, Action<string> fail)
        {
            this.game = game;
            this.im = im;
            this.fail = fail;
        }

        private void add(Action a, double after = 60, string what = "") => queue.Add((a, after, null, what));

        /// <summary>Clicks, drags and wheel turns a step needs must land in its spotlight (when it names a target).</summary>
        private void spot(Vector2 p, string what)
        {
            var t = game.Tutorial;
            if (t.Step is not { HasTarget: true } step || t.StepSolved || t.OffTrack != null) return;
            if (step.Tasks.Count > 0 && t.CurrentTask is { HasTarget: false } && step.Area == null && step.Target == null && step.Anchor == null) return;

            var o = game.TutorialOverlay;
            if (o.TargetMissing || o.SpotlightGoal is not { } goal)
            {
                fail($"spotlight: \"{step.Title}\"{task()} names a target that is not on screen when the user has to {what}");
                return;
            }

            if (!goal.Inflate(3).Contains(p))
                fail(FormattableString.Invariant($"spotlight: \"{step.Title}\"{task()}: the user has to {what} at ({p.X:0},{p.Y:0}), outside the spotlight {goal}"));
            else
                SpotChecks++;
        }

        private string task() => game.Tutorial.CurrentTask is { } c ? $" task {game.Tutorial.TaskIndex + 1} \"{TutorialRichText.Plain(c.Text).Split('.')[0]}\"" : "";

        /// <summary>How many demo pointer actions were checked against the spotlight (smoke report).</summary>
        public static int SpotChecks;

        private bool onPanel(Drawable d)
        {
            for (Drawable? p = d; p != null; p = p.Parent)
            {
                if (p is TutorialPanel) return true;
            }

            return false;
        }

        private bool insideSmokeInput(Drawable d)
        {
            for (Drawable? p = d; p != null; p = p.Parent)
            {
                if (p == im) return true;
            }

            return false;
        }

        public void Click(Func<Drawable?> target, bool right = false)
        {
            Drawable? d = null;
            add(() =>
            {
                d = target();
                if (d == null || !TutorialAnchors.IsShown(d))
                {
                    fail("demo: the thing to click is not on screen");
                    return;
                }

                var at = d.ScreenSpaceDrawQuad.Centre;
                if (!onPanel(d)) spot(at, right ? "right-click" : "click");
                if (insideSmokeInput(d)) im.MoveMouseTo(at);
                else game.inputMoveTo(at);
            }, 50, "click");
            add(() =>
            {
                if (d == null) return;
                if (insideSmokeInput(d)) im.PressButton(right ? MouseButton.Right : MouseButton.Left);
                else game.inputButton(true);
            }, 50);
            add(() =>
            {
                if (d == null) return;
                if (insideSmokeInput(d)) im.ReleaseButton(right ? MouseButton.Right : MouseButton.Left);
                else game.inputButton(false);
            }, 90);
        }

        public void ClickAt(Func<Vector2> screenSpace, bool right = false)
        {
            add(() =>
            {
                var p = screenSpace();
                spot(p, right ? "right-click" : "click");
                im.MoveMouseTo(p);
            }, 50);
            add(() => im.PressButton(right ? MouseButton.Right : MouseButton.Left), 50);
            add(() => im.ReleaseButton(right ? MouseButton.Right : MouseButton.Left), 80);
        }

        public void ClickAtIf(Func<bool> condition, Func<Vector2> screenSpace)
        {
            bool go = false;
            add(() =>
            {
                go = condition();
                if (!go) return;
                var p = screenSpace();
                spot(p, "click");
                im.MoveMouseTo(p);
            }, 50);
            add(() =>
            {
                if (go) im.PressButton(MouseButton.Left);
            }, 50);
            add(() =>
            {
                if (go) im.ReleaseButton(MouseButton.Left);
            }, 80);
        }

        public void Drag(Func<Vector2> from, Func<Vector2> to)
        {
            add(() =>
            {
                spot(from(), "start a drag");
                spot(to(), "end a drag");
                im.MoveMouseTo(from());
            }, 50);
            add(() => im.PressButton(MouseButton.Left), 50);
            add(() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.04f)), 50); // a short first move: drags start where they are detected
            add(() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.5f)), 50);
            add(() => im.MoveMouseTo(to()), 50);
            add(() => im.ReleaseButton(MouseButton.Left), 90);
        }

        public void DragPath(params Func<Vector2>[] points)
        {
            add(() => im.MoveMouseTo(points[0]()), 50);
            add(() => im.PressButton(MouseButton.Left), 50);
            foreach (var p in points.Skip(1)) add(() => im.MoveMouseTo(p()), 50);
            add(() => im.ReleaseButton(MouseButton.Left), 90);
        }

        public void MoveTo(Func<Vector2> screenSpace)
        {
            add(() => im.MoveMouseTo(screenSpace() + new Vector2(1, 0)), 50);
            add(() => im.MoveMouseTo(screenSpace()), 120);
        }

        public void Key(Key key, params Key[] modifiers)
        {
            add(() =>
            {
                foreach (var m in modifiers) im.PressKey(m);
                im.PressKey(key);
            }, 40);
            add(() =>
            {
                im.ReleaseKey(key);
                foreach (var m in modifiers.Reverse()) im.ReleaseKey(m);
            }, 90);
        }

        public void Press(Screens.Edit.Input.EditorAction action)
        {
            var binding = Screens.Edit.Input.EditorKeyBindings.All.FirstOrDefault(b => b.Action == action);
            if (binding == null)
            {
                add(() => fail($"demo: {action} has no key binding"));
                return;
            }

            var mods = new List<Key>();
            Key? main = null;
            foreach (var k in Input.KeyboardLayoutService.ToPhysical(binding.Keys).Keys)
            {
                switch (k)
                {
                    case osu.Framework.Input.Bindings.InputKey.Control or osu.Framework.Input.Bindings.InputKey.LControl: mods.Add(osuTK.Input.Key.LControl); break;
                    case osu.Framework.Input.Bindings.InputKey.Shift or osu.Framework.Input.Bindings.InputKey.LShift: mods.Add(osuTK.Input.Key.LShift); break;
                    case osu.Framework.Input.Bindings.InputKey.Alt or osu.Framework.Input.Bindings.InputKey.LAlt: mods.Add(osuTK.Input.Key.LAlt); break;
                    case osu.Framework.Input.Bindings.InputKey.Super or osu.Framework.Input.Bindings.InputKey.LSuper: mods.Add(osuTK.Input.Key.LWin); break;
                    default: main = (Key)(int)k; break;
                }
            }

            if (main is Key m) Key(m, mods.ToArray());
            else add(() => fail($"demo: {action} is bound to {binding.Keys} (no keyboard key)"));
        }

        public void HoldKey(Key key, bool down) => add(() =>
        {
            if (down) im.PressKey(key);
            else im.ReleaseKey(key);
        }, 80);

        public void KeyIf(Func<bool> condition, Key key, params Key[] modifiers)
        {
            bool go = false;
            add(() =>
            {
                go = condition();
                if (!go) return;
                foreach (var m in modifiers) im.PressKey(m);
                im.PressKey(key);
            }, 40);
            add(() =>
            {
                if (!go) return;
                im.ReleaseKey(key);
                foreach (var m in modifiers.Reverse()) im.ReleaseKey(m);
            }, 90);
        }

        public void DialogKey(Key key, params Key[] modifiers)
        {
            add(() =>
            {
                foreach (var m in modifiers) game.inputKey(m, true);
                game.inputKey(key, true);
            }, 40);
            add(() =>
            {
                game.inputKey(key, false);
                foreach (var m in modifiers.Reverse()) game.inputKey(m, false);
            }, 90);
        }

        public void Wheel(Func<Vector2> at, float notches, params Key[] modifiers)
        {
            add(() =>
            {
                spot(at(), "turn the wheel");
                im.MoveMouseTo(at());
                foreach (var m in modifiers) im.PressKey(m);
            }, 50);
            for (int i = 0; i < Math.Abs((int)notches); i++)
                add(() => im.ScrollVerticalBy(Math.Sign(notches)), 50);
            add(() =>
            {
                foreach (var m in modifiers.Reverse()) im.ReleaseKey(m);
            }, 60);
        }

        public void Do(Action action) => add(action, 60, "do");

        public void WaitFor(Func<bool> condition, string what) => queue.Add((() => { }, 0, condition, what));

        /// <summary>Runs the queue on the game's scheduler, then <paramref name="done"/>.</summary>
        public void Run(Action done)
        {
            int i = 0;
            double deadline = 0;

            void next()
            {
                if (i >= queue.Count)
                {
                    done();
                    return;
                }

                var (act, after, wait, what) = queue[i];

                if (wait != null)
                {
                    if (deadline == 0) deadline = game.Clock.CurrentTime + 20_000;
                    bool ok;
                    try
                    {
                        ok = wait();
                    }
                    catch (Exception e)
                    {
                        fail($"demo wait \"{what}\": {e.Message}");
                        return;
                    }

                    if (!ok)
                    {
                        if (game.Clock.CurrentTime > deadline)
                        {
                            fail($"demo: timed out waiting for {what}");
                            return;
                        }

                        game.Scheduler.AddDelayed(next, 40);
                        return;
                    }

                    deadline = 0;
                    i++;
                    game.Scheduler.AddDelayed(next, 30);
                    return;
                }

                try
                {
                    act();
                }
                catch (Exception e)
                {
                    fail($"demo action {i} ({what}): {e.Message}");
                    return;
                }

                i++;
                game.Scheduler.AddDelayed(next, after);
            }

            next();
        }
    }
}
