using osu.Framework.Graphics;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Effects;

namespace SDVXEditor.Game.Tutorial;

public static partial class TutorialChapters
{
    // ---- 3: navigating -------------------------------------------------------------------

    private static TutorialChapter navigating() => new TutorialChapter
    {
        Number = 3,
        Title = "Navigating",
        Summary = "Timeline, zoom, playback speed, bookmarks and the collab list, jump to a time, (extra) reference difficulty",
        Minutes = TutorialPlan.MinutesOf(3),
        Requirement = compose,
        Restore = restoreCompose,
        Steps = c =>
        {
            double speedMs = c.Song.MsAt(at(c.Song.SpeedUp.Measure));
            string speedText = clock(speedMs);

            return new List<TutorialStep>
            {
                new TutorialStep("The timeline",
                    "The band at the top is the timeline: the song's waveform with a ruler of bars and beats. Click or drag in it to move through the song. "
                    + "Coloured marks are timing points and bookmarks.\n\nThe column left of the playfield is a mini map of all notes.")
                {
                    Anchor = "editor.timeline",
                },

                new TutorialStep("Zoom",
                    "Hold Alt and turn the mouse wheel over the playfield or the timeline: both zoom together. Zoom in to spread dense notes apart, out to see more.")
                {
                    Anchor = "editor.playfield",
                    Enter = c => c.Manager.WhenEditorReady(() => c.Bag["zoom"] = c.Editor!.Zoom.Value),
                    Progress = c => c.Editor is { } e ? $"Zoom: {e.Zoom.Value:0.00}×" : null,
                    Done = c => c.Bag.TryGetValue("zoom", out var z) && z is float z0 && c.Editor is { } e && Math.Abs(e.Zoom.Value - z0) >= 0.09f,
                    Demo = (d, c) => d.Wheel(() => c.Editor!.Playfield.ScreenSpaceDrawQuad.Centre, 2, Key.LAlt),
                },

                new TutorialStep("Slow it down",
                    "Fast part? Listen at half speed: click 50 % at the bottom (the pitch stays the same), press {key:PlayPause} to play and again to pause. "
                    + "Then click 100 % to get back to full speed.")
                {
                    Target = c => c.Editor?.BottomBar.RateButtons.FirstOrDefault(r => Math.Abs(r.Rate - (c.Bag.ContainsKey("halfPlayed") ? 1 : 0.5)) < 0.01),
                    Progress = c =>
                    {
                        if (c.Editor is not { } e) return null;
                        bool half = Math.Abs(e.Playback.Rate.Value - 0.5) < 0.01;
                        if (half && e.Playback.IsPlaying.Value) c.Bag["halfPlayed"] = true;
                        return c.Bag.ContainsKey("halfPlayed") ? "Played at 50 % ✓ — pause, then 100 %"
                            : half ? "50 % ✓ — now press " + TutorialContext.KeyOf(EditorAction.PlayPause) : "Click 50 %";
                    },
                    Done = c => c.Bag.ContainsKey("halfPlayed") && c.Editor is { } e && !e.Playback.IsPlaying.Value && Math.Abs(e.Playback.Rate.Value - 1) < 0.01,
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Editor?.BottomBar.RateButtons.FirstOrDefault(r => Math.Abs(r.Rate - 0.5) < 0.01));
                        d.Press(EditorAction.PlayPause);
                        d.WaitFor(() => c.Bag.ContainsKey("halfPlayed"), "playback at 50 %");
                        d.Press(EditorAction.PlayPause);
                        d.Click(() => c.Editor?.BottomBar.RateButtons.FirstOrDefault(r => Math.Abs(r.Rate - 1) < 0.01));
                    },
                },

                new TutorialStep("Bookmarks",
                    "Bookmarks mark places to come back to. This song already has four (intro, main, breakdown, speed-up — the flags in the timeline). "
                    + "Add your own: press {key:AddBookmark} or click + at the bottom. Then press {key:NextBookmark} (or >) to jump to the next one; "
                    + "{key:PreviousBookmark} (the left bookmark arrow) goes back, - removes the nearest.")
                {
                    Target = c => c.Bag.ContainsKey("bookmarkAt") ? c.Editor?.BottomBar.NextBookmarkButton : c.Editor?.BottomBar.AddBookmarkButton,
                    Enter = c =>
                    {
                        c.Bag["bookmarks"] = c.Chart?.Editor.Bookmarks.Count ?? 0;
                        SeekTo(c, at(4)); // somewhere without a bookmark yet
                    },
                    Progress = c =>
                    {
                        if (!c.Bag.ContainsKey("bookmarkAt") && c.Chart is { } ch && c.Bag["bookmarks"] is int n && ch.Editor.Bookmarks.Count > n && c.Editor is { } e)
                            c.Bag["bookmarkAt"] = e.Playback.CurrentTime.Value;
                        return c.Bag.ContainsKey("bookmarkAt") ? "Added ✓ — now jump to the next one" : null;
                    },
                    Done = c => c.Bag.TryGetValue("bookmarkAt", out var t) && t is double added && c.Editor is { } e
                                && Math.Abs(e.Playback.CurrentTime.Value - added) > 50 && e.Bookmarks.Any(b => Math.Abs(b - e.Playback.CurrentTime.Value) < 2),
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Editor?.BottomBar.AddBookmarkButton);
                        d.WaitFor(() => c.Bag.ContainsKey("bookmarkAt"), "the new bookmark");
                        d.Press(EditorAction.NextBookmark);
                    },
                },

                new TutorialStep("The collab list",
                    "Mapping with a friend? \"Collab\" lists every bookmark with a label and a colour — notes for each other like \"your part starts here\". Click it.\n\n"
                    + "Double-click a row to jump there; rename a bookmark, pick a colour, delete it. Bookmarks are saved in the chart (and survive in .ksh too). "
                    + "Then close the window: Esc or x.")
                {
                    Requirement = TutorialNeed.Editor(),
                    Target = c => (Drawable?)c.Dialog<BookmarkListDialog>() ?? c.Editor?.BottomBar.CollabButton,
                    Progress = c =>
                    {
                        if (c.Dialog<BookmarkListDialog>() != null) c.Bag["collabOpened"] = true;
                        return c.Bag.ContainsKey("collabOpened") ? "Opened ✓ — close it again" : null;
                    },
                    Done = c => c.Bag.ContainsKey("collabOpened") && !c.Dialogs.HasOpenDialog,
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Editor?.BottomBar.CollabButton);
                        d.WaitFor(() => c.Bag.ContainsKey("collabOpened"), "the collab list");
                        d.DialogKey(Key.Escape);
                    },
                },

                new TutorialStep("Jump to a time",
                    "Click the time at the bottom left: a small window asks where to go.")
                {
                    Target = c => c.Editor?.BottomBar.TimeDisplay,
                    Done = c => c.Dialog<JumpToTimeDialog>() != null,
                    Demo = (d, c) => d.Click(() => c.Editor?.BottomBar.TimeDisplay),
                },

                new TutorialStep("Type a time",
                    $"Type {speedText} and press Enter: you land on the speed-up.\n\n"
                    + "For modding: select notes and press {key:Copy} — besides copying, the editor puts a timestamp like \"" + speedText + " (" + speedText + "|BT-A) - \" "
                    + "on the clipboard. Paste one into this box and the editor jumps there and selects exactly those notes.")
                {
                    Target = c => c.Dialog<JumpToTimeDialog>(),
                    Requirement = c => c.Dialog<JumpToTimeDialog>() != null || c.Editor is { } e && Math.Abs(e.Playback.CurrentTime.Value - speedMs) < 60
                        ? TutorialNeed.Editor()(c) : "The window was closed. Click the time at the bottom left again.",
                    Restore = c => c.Editor?.OpenTimeDialog(),
                    Done = c => c.Dialog<JumpToTimeDialog>() == null && c.Editor is { } e && Math.Abs(e.Playback.CurrentTime.Value - speedMs) < 60,
                    Demo = (d, c) =>
                    {
                        d.Do(() => c.Dialog<JumpToTimeDialog>()!.TimeBox.Text = speedText); // typing stand-in
                        d.DialogKey(Key.Enter);
                    },
                },

                new TutorialStep("Extra: a reference difficulty",
                    "Optional — \"Skip step\" if you chart one difficulty only. Charting several? Open another one beside yours, read-only: "
                    + "File → Open Difficulty for Reference → ADV. It sits to the right, greyed out, scrolling with yours; its notes can be copied with "
                    + "{key:Copy} and pasted into yours. Press Esc to close it (Esc closes the reference before it ever leaves the editor).")
                {
                    Target = c => c.Editor?.ReferenceSession != null ? c.Editor.ReferencePlayfield : MenuItem(c, "File", "Open Difficulty for Reference", "ADV") ?? MenuItem(c, "File", "Open Difficulty for Reference") ?? MenuItem(c, "File"),
                    Progress = c =>
                    {
                        if (c.Editor?.ReferenceSession != null) c.Bag["referenceOpened"] = true;
                        return c.Bag.ContainsKey("referenceOpened") ? "Opened ✓ — Esc closes it" : null;
                    },
                    Done = c => c.Bag.ContainsKey("referenceOpened") && c.Editor is { ReferenceSession: null },
                    Demo = (d, c) =>
                    {
                        clickMenu(d, c, "File", "Open Difficulty for Reference", "ADV");
                        d.WaitFor(() => c.Bag.ContainsKey("referenceOpened"), "the reference");
                        d.Press(EditorAction.Exit);
                    },
                },
            };
        },
    };

    // ---- 4: camera & background -----------------------------------------------------------

    private static TutorialChapter camera() => new TutorialChapter
    {
        Number = 4,
        Title = "Camera & Background",
        Summary = "Camera lanes (zoom, tilt, spins), the live preview, background and post effects, export badges",
        Minutes = TutorialPlan.MinutesOf(4),
        Requirement = TutorialNeed.Editor(EditorTab.Camera),
        Restore = TutorialNeed.RestoreEditor(EditorTab.Camera),
        Steps = c =>
        {
            CameraTabContent? cam() => c.Editor?.CameraContent;
            Vector2 lane(CamLane l, long pulse, double fx)
            {
                var lanes = cam()!.Lanes;
                return lanes.ToScreenSpace(new Vector2(CameraLanes.LaneLeft(l) + (float)fx * CameraLaneEditor.LANE_WIDTH, lanes.YOfPulse(pulse)));
            }

            return new List<TutorialStep>
            {
                new TutorialStep("The camera tab",
                    "In SOUND VOLTEX the lane moves: it zooms, tilts and spins with the music. Press {key:TabCamera} or click \"camera\".\n\n"
                    + "Each column there is one camera curve over time (time runs up, like the notes on the left): zoom_top, zoom_bottom, zoom_side, "
                    + "center_split, rotation, tilt and spin / swing. The small badge under a name says which formats keep it.")
                {
                    Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "camera"),
                    Requirement = TutorialNeed.Editor(),
                    Restore = TutorialNeed.RestoreEditor(),
                    Done = c => c.Editor?.CurrentTab == EditorTab.Camera,
                    Demo = (d, _) => d.Press(EditorAction.TabCamera),
                },

                new TutorialStep("Zoom in at the speed-up",
                    "At the speed-up the lane should rush towards you. Click twice in the zoom_bottom column: once at the speed-up line, and once a bar later further to the right "
                    + "(bigger zoom). Drag a point to change it; Shift-click makes an instant jump. The points are anchors like on a laser: "
                    + "three clicks in a row bend into a curve, and right-clicking an anchor picks Straight, Perfect curve, Bezier or Delete.")
                {
                    Target = c => cam()?.Lanes,
                    Enter = c => SeekTo(c, at(c.Song.SpeedUp.Measure) - beat),
                    Progress = c => c.Chart is { } ch ? $"{Math.Min(2, ch.Camera.Body.ZoomBottom.Count)} / 2 zoom_bottom points" : null,
                    Done = c => c.Chart?.Camera.Body.ZoomBottom.Count >= 2,
                    Demo = (d, c) =>
                    {
                        int m = c.Song.SpeedUp.Measure;
                        d.ClickAt(() => lane(CamLane.ZoomBottom, at(m), 0.5));
                        d.ClickAt(() => lane(CamLane.ZoomBottom, at(m + 1), 0.8));
                    },
                },

                new TutorialStep("The preview",
                    "The preview on the right shows the lane with your camera at the cursor. Press {key:PlayPause} and watch the zoom, then pause again.")
                {
                    Target = c => cam()?.Preview,
                    Progress = c =>
                    {
                        if (c.Editor?.Playback.IsPlaying.Value == true) c.Bag["played"] = true;
                        return null;
                    },
                    Done = c => c.Bag.ContainsKey("played") && c.Editor?.Playback.IsPlaying.Value == false,
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.PlayPause);
                        d.WaitFor(() => c.Bag.ContainsKey("played"), "playback");
                        d.Press(EditorAction.PlayPause);
                    },
                },

                new TutorialStep("Tilt",
                    "Click in the tilt column to add a tilt point: the lane leans left or right. (\"tilt mode at the cursor\" on the right switches the automatic tilt.)")
                {
                    Target = c => cam()?.Lanes,
                    Enter = c => SeekTo(c, at(c.Song.SpeedUp.Measure + 1) - beat),
                    Done = c => c.Chart?.Camera.Tilt.Count >= 1,
                    Demo = (d, c) => d.ClickAt(() => lane(CamLane.Tilt, at(c.Song.SpeedUp.Measure + 1, 1), 0.7)),
                },

                new TutorialStep("A spin on a slam",
                    "Spins and swings always belong to a slam. Click in the spin / swing column right at the slam of bar 8 (the pink laser jump in the notes on the left).")
                {
                    Target = c => cam()?.Lanes,
                    Enter = c => SeekTo(c, TutorialCharts.SlamLaser.Y - beat),
                    Done = c => c.Chart is { } ch && ch.Camera.Spin.Count + ch.Camera.HalfSpin.Count + ch.Camera.Swing.Count >= 1,
                    Demo = (d, c) => d.ClickAt(() => lane(CamLane.Patterns, TutorialCharts.SlamLaser.Y, 0.5)),
                },

                new TutorialStep("Background and export",
                    "Click \"background\": the background image or animation, the VOX background number (bg_no) and post effects for the game.\n\n"
                    + "Then click \"export\": a table of what each format keeps. kson keeps everything; ksh most of it; VOX (for the game) needs ksh2vox 0.17 "
                    + "or newer for camera work. [c=7ee07e]✓[/c] kept · [c=ff9f43]~[/c] approximated · [c=ff5050]x[/c] lost. The same badges sit under the lane names.")
                {
                    Target = c => TutorialContext.FindText(cam(), c.Bag.ContainsKey("backgroundSeen") ? "export" : "background"),
                    Progress = c =>
                    {
                        if (cam()?.View.Value == 1) c.Bag["backgroundSeen"] = true;
                        return c.Bag.ContainsKey("backgroundSeen") ? "Background ✓ — now \"export\"" : null;
                    },
                    Done = c => c.Bag.ContainsKey("backgroundSeen") && cam()?.View.Value == 2,
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialContext.FindText(cam(), "background"));
                        d.WaitFor(() => c.Bag.ContainsKey("backgroundSeen"), "the background view");
                        d.Click(() => TutorialContext.FindText(cam(), "export"));
                    },
                },

                saveStep(),
            };
        },
    };

    // ---- 5: effects & sounds --------------------------------------------------------------

    private static TutorialChapter effects() => new TutorialChapter
    {
        Number = 5,
        Title = "Effects & Sounds",
        Summary = "FX hold effects, laser filters, custom FX chip sounds",
        Minutes = TutorialPlan.MinutesOf(5),
        Requirement = TutorialNeed.Editor(EditorTab.Effects),
        Restore = TutorialNeed.RestoreEditor(EditorTab.Effects),
        Steps = c =>
        {
            EffectsTabContent? fxTab() => c.Editor?.EffectsContent;

            return new List<TutorialStep>
            {
                new TutorialStep("The effects tab",
                    "FX notes and lasers can change the music while they are played. Press {key:TabEffects} or click \"effects\".")
                {
                    Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "effects"),
                    Requirement = TutorialNeed.Editor(),
                    Restore = TutorialNeed.RestoreEditor(),
                    Done = c => c.Editor?.CurrentTab == EditorTab.Effects,
                    Demo = (d, _) => d.Press(EditorAction.TabEffects),
                },

                new TutorialStep("An effect on an FX hold",
                    "While an FX hold is held, it can play an effect: retrigger, gate, flanger…\n\n"
                    + "Click \"select FX in view\" (it selects the FX notes around the cursor), pick an effect under \"assign to selected FX holds\" and click \"assign\".")
                {
                    Area = c => TutorialContext.Union(TutorialContext.FindText(fxTab(), "select FX in view"), TutorialContext.FindText(fxTab(), "assign")),
                    Enter = c => SeekTo(c, TutorialCharts.FxHold[0].Y),
                    Done = c => c.Chart is { } ch && ch.Notes.Fx.SelectMany(l => l).Any(n => n.IsHold && n.Effects.Count > 0),
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialContext.FindText(fxTab(), "select FX in view"));
                        d.Click(() => TutorialContext.FindText(fxTab(), "assign"));
                    },
                },

                new TutorialStep("Laser filters",
                    "Lasers filter the music while you turn them (peak, low-pass, high-pass, bit crusher). Pick a filter under \"laser filter switches\" and click \"at cursor\": "
                    + "from here on, lasers use it.")
                {
                    Target = c => atCursorForFilters(fxTab()),
                    Enter = c =>
                    {
                        SeekTo(c, TutorialCharts.StraightLaser.Y);
                        c.Bag["filters"] = c.Chart?.Audio.LaserEffects.PulseEvents.Sum(kv => kv.Value.Count) ?? 0;
                    },
                    Done = c => c.Chart is { } ch && c.Bag["filters"] is int n && ch.Audio.LaserEffects.PulseEvents.Sum(kv => kv.Value.Count) > n,
                    Demo = (d, c) => d.Click(() => atCursorForFilters(fxTab())),
                },

                new TutorialStep("Your own FX chip sounds",
                    "FX chips can play a sound of their own. Click a sound under \"FX chip sounds\" (clap, snare …) — \"preview\" plays it — then "
                    + "\"as default (chips without one)\" gives it to every FX chip that has none. \"import\" adds your own .wav / .ogg.")
                {
                    Area = c => TutorialContext.Union(TutorialContext.FindText(fxTab(), "clap", contains: true), TutorialContext.FindText(fxTab(), "as default (chips without one)")),
                    Done = c => c.Chart is { } ch && ch.Notes.Fx.SelectMany(l => l).Any(n => !n.IsHold && n.KeySound != null),
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialContext.FindText(fxTab(), "clap", contains: true));
                        d.Click(() => TutorialContext.FindText(fxTab(), "as default (chips without one)"));
                    },
                },

                saveStep(),
            };
        },
    };

    private static Drawable? atCursorForFilters(Drawable? tab) =>
        tab?.ChildrenOfType<Graphics.UserInterface.FlatButton>()
            .Where(f => TutorialAnchors.IsShown(f) && TutorialContext.FindText(f, "at cursor") != null)
            .OrderByDescending(f => f.ScreenSpaceDrawQuad.Centre.X)
            .FirstOrDefault();

    // ---- 6: test & difficulty -------------------------------------------------------------

    private static TutorialChapter testAndDifficulty() => new TutorialChapter
    {
        Number = 6,
        Title = "Test & Difficulty",
        Summary = "Map a section on your own, playtest it (F5), autoplay, results, level estimate, skill radar",
        Minutes = TutorialPlan.MinutesOf(6),
        Requirement = compose,
        Restore = restoreCompose,
        Steps = c => new List<TutorialStep>
        {
            new TutorialStep("Your own section",
                "Now map on your own: the last four bars (highlighted) are yours. Chips, holds, FX, lasers — whatever fits the music.\n\n"
                + "Rules: at least 12 objects · at least one laser · everything on the 1/4 or 1/8 grid. Zoom out (Alt + wheel) to see more at once.")
            {
                Enter = c => Show(c, at(TutorialCharts.FinalPractice.From), at(TutorialCharts.FinalPractice.From + 2)),
                Zone = _ => (at(TutorialCharts.FinalPractice.From), at(TutorialCharts.FinalPractice.To)),
                Progress = c => c.Chart is { } ch ? practice(ch, TutorialCharts.FinalPractice, 12, 8, lasers: 1) : null,
                Done = c => c.Chart is { } ch && practice(ch, TutorialCharts.FinalPractice, 12, 8, lasers: 1) == null,
                Demo = (d, c) =>
                {
                    int m = TutorialCharts.FinalPractice.From;
                    d.Press(EditorAction.Divisor8);
                    d.Press(EditorAction.ToolBtChip);
                    for (int i = 0; i < 11; i++) d.ClickAt(pf(c, at(m, i * 0.5), bt(i % 4)));
                    d.Press(EditorAction.ToolLaser);
                    d.Press(EditorAction.LaserSideLeft);
                    d.Drag(pf(c, at(m + 1, 2), laser(0)), pf(c, at(m + 1, 3), laser(0.5)));
                },
            },

            new TutorialStep("Test it",
                "Press {key:Playtest} to play your chart, starting 2 s before the cursor. Keys: BT D F J K · FX C M · laser L W/E · laser R O/P "
                + "(rebind them in the options). The test plays what is in the editor, saved or not.\n\n"
                + "In the test press Tab: autoplay plays the chart for you — handy to watch how it feels. Tab again gives control back. "
                + "F3 / F4 (or - / +) change the scroll speed.")
            {
                Requirement = c => c.Playtest != null ? null : compose(c),
                Progress = c => c.Playtest is { IsLoaded: true } ? "Test running — now Tab for autoplay" : null,
                Done = c => c.Playtest is { IsLoaded: true } p && p.Judge.Autoplay,
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.Playtest);
                    d.WaitFor(() => c.Playtest is { IsLoaded: true }, "the playtest");
                    d.Key(Key.Tab);
                },
            },

            new TutorialStep("Results",
                "Press Esc to stop: the results show score, chain and how early or late the hits were. "
                + "Esc again (or Enter) returns to the editor, at the place where the test stopped.")
            {
                Requirement = c => c.Playtest != null || c.Bag.ContainsKey("resultsSeen") && c.Editor != null ? null : "Press {key:Playtest} in the editor to start the test again.",
                Restore = c => c.Editor?.Playtest(),
                Progress = c =>
                {
                    if (c.Playtest is { ShowingResults: true }) c.Bag["resultsSeen"] = true;
                    return c.Bag.ContainsKey("resultsSeen") ? "Results ✓ — Esc again for the editor" : null;
                },
                Done = c => c.Bag.ContainsKey("resultsSeen") && c.Editor != null,
                Demo = (d, c) =>
                {
                    d.Key(Key.Escape);
                    d.WaitFor(() => c.Bag.ContainsKey("resultsSeen"), "the results");
                    d.Key(Key.Escape);
                },
            },

            new TutorialStep("How hard is it?",
                "The editor estimates the level from your notes (fitted on official charts). Press {key:SongSetup} and open the Difficulty page.")
            {
                Requirement = TutorialNeed.Editor(),
                Done = c => c.Dialog<SongSetupDialog>() is { } s && FieldNear<Graphics.UserInterface.Dialog.NumericUpDown>(s, "Level") != null,
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.SongSetup);
                    d.WaitFor(() => c.Dialog<SongSetupDialog>() != null, "Song Setup");
                    d.Click(() => TutorialContext.FindText(c.Dialog<SongSetupDialog>(), "Difficulty"));
                },
            },

            new TutorialStep("Use the estimate",
                "\"Estimated level\" shows the guess and how sure it is. Click \"Use estimate\" (it only fills the Level box), then OK to keep it.")
            {
                // the spotlight moves from "Use estimate" to OK once the level box is filled
                Target = c => TutorialContext.FindText(c.Dialog<SongSetupDialog>(), c.Bag.ContainsKey("used") ? "OK" : "Use estimate"),
                Requirement = c => c.Dialog<SongSetupDialog>() != null || c.Bag.ContainsKey("used") ? TutorialNeed.Editor()(c) : "Song Setup was closed. Press {key:SongSetup} again.",
                Restore = c => c.Editor?.OpenSongSetup(),
                Progress = c =>
                {
                    if (c.Dialog<SongSetupDialog>() is { } s && s.Edit.Estimate.Value?.EstimatedLevel is double l
                                                         && Math.Abs(s.Edit.Level.Value - SongSetupViewModel.SnapLevel(l)) < 0.05)
                        c.Bag["used"] = s.Edit.Level.Value;
                    return c.Bag.TryGetValue("used", out var v) ? $"Level {v:0.0} — now OK" : null;
                },
                Done = c => c.Dialog<SongSetupDialog>() == null && c.Bag.TryGetValue("used", out var v) && v is double lv && c.Chart is { } ch && Math.Abs(ch.Meta.Level - lv) < 0.05,
                Demo = (d, c) =>
                {
                    d.WaitFor(() => c.Dialog<SongSetupDialog>()?.Edit.Estimate.Value?.EstimatedLevel != null, "the estimate");
                    d.Click(() => TutorialContext.FindText(c.Dialog<SongSetupDialog>(), "Use estimate"));
                    d.WaitFor(() => c.Bag.ContainsKey("used"), "the level box filled");
                    d.Click(() => TutorialContext.FindText(c.Dialog<SongSetupDialog>(), "OK"));
                },
            },

            saveStep(),

            new TutorialStep("Back to song select",
                "Press Esc to leave the editor. (Esc first closes whatever is open — a window, a menu, the reference. With unsaved changes it asks "
                + "Save / Discard / Cancel before leaving; we just saved, so it leaves at once.)")
            {
                Requirement = c => c.SongSelect != null || c.Editor != null ? null : compose(c),
                Done = c => c.SongSelect != null,
                Demo = (d, _) => d.Press(EditorAction.Exit),
            },

            new TutorialStep("The skill radar",
                "Next to your song's levels, the hexagon is the chart's skill radar: NOTES, PEAK, TSUMAMI (knobs), TRICKY, HAND-TRIP, ONE-HAND — "
                + "computed the way the VOX converter does it. Select another chart to compare.")
            {
                Requirement = TutorialNeed.SongSelect,
                Restore = TutorialNeed.RestoreSongSelect,
                Area = c => TutorialContext.DeepRect(c.SongSelect?.Radar),
            },
        },
    };

    // ---- 7: save, export & share ------------------------------------------------------------

    private static TutorialChapter saveExportShare() => new TutorialChapter
    {
        Number = 7,
        Title = "Save, Export & Share",
        Summary = "kson saving, export to ksh / VOX (what is lost), .svz for collabs, options search, hitsounds, key bindings",
        Minutes = TutorialPlan.MinutesOf(7),
        Requirement = TutorialNeed.Editor(),
        Restore = TutorialNeed.RestoreEditor(),
        Steps = c => new List<TutorialStep>
        {
            new TutorialStep("Export for K-Shoot Mania",
                "{key:Save} saves the chart as .kson in the song folder — the editor's own format, nothing is lost; a .ksh you opened is never overwritten "
                + "(the save goes to a .kson beside it). The other formats are exports, in the File menu:\n\n"
                + "File → Export Song → ksh… writes the song for K-Shoot Mania into your Exports folder. ksh keeps notes, lasers, most camera work and effects; "
                + "editor extras (laser anchors, VOX backgrounds) become plain points or drop out.\n\n"
                + "\"VOX\" makes files for the game itself through the ksh2vox converter (needs Python, see Options → Export).")
            {
                Target = c => MenuItem(c, "File", "Export Song", "ksh...") ?? MenuItem(c, "File", "Export Song") ?? MenuItem(c, "File"),
                Enter = c => c.Bag["export"] = c.Editor?.LastExportPath,
                Done = c => c.Editor is { ExportsRunning: 0, LastExportPath: string p } && !Equals(p, c.Bag["export"]) && (File.Exists(p) || Directory.Exists(p)),
                Demo = (d, c) =>
                {
                    clickMenu(d, c, "File", "Export Song", "ksh...");
                    d.WaitFor(() => c.Editor is { ExportsRunning: 0, LastExportPath: not null }, "the ksh export");
                },
            },

            new TutorialStep("Share with .svz",
                "Mapping together? File → Export Song → .svz packs the whole song folder (audio, jacket, every difficulty, bookmarks) into one file. "
                + "Your partner drops it onto song select; if they have the song already, they choose Replace or Keep both.")
            {
                Target = c => MenuItem(c, "File", "Export Song", ".svz") ?? MenuItem(c, "File", "Export Song") ?? MenuItem(c, "File"),
                Enter = c => c.Bag["since"] = DateTime.UtcNow.AddSeconds(-1),
                Done = c => c.Bag["since"] is DateTime since && Directory.Exists(c.Game.Library.ExportRoot)
                                                             && Directory.EnumerateFiles(c.Game.Library.ExportRoot, "*.svz", SearchOption.AllDirectories).Any(f => File.GetLastWriteTimeUtc(f) >= since),
                Demo = (d, c) =>
                {
                    clickMenu(d, c, "File", "Export Song", ".svz");
                    d.WaitFor(() => c.Manager.Step?.Title != "Share with .svz", "the .svz export");
                },
            },

            new TutorialStep("Options",
                "Open the options with {key:ShowOptions} (or File → Options…) and type \"hitsound\" into the search box at the top: only matching rows stay.\n\n"
                + "What you hear while editing comes from the hitsound skin: \"Auto\" uses your osu! skin, or pick the built-in sounds; volumes and chord layers sit next to it.")
            {
                Requirement = c => wrongWindow(c) ?? TutorialNeed.Editor()(c),
                Target = c => c.Game.OptionsOverlay.IsOpen.Value ? c.Game.OptionsOverlay.SearchBox : MenuItem(c, "File"),
                Progress = c =>
                {
                    if (c.Game.OptionsOverlay.IsOpen.Value && c.Game.OptionsOverlay.Search.Value.Contains("hitsound", StringComparison.OrdinalIgnoreCase))
                        c.Bag["searched"] = true;
                    return c.Game.OptionsOverlay.IsOpen.Value ? "Options open — now search" : null;
                },
                Done = c => c.Bag.ContainsKey("searched"),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ShowOptions);
                    d.WaitFor(() => c.Game.OptionsOverlay.IsOpen.Value, "the options");
                    d.Click(() => c.Game.OptionsOverlay.SearchBox);
                    d.Do(() => c.Game.OptionsOverlay.Search.Value = "hitsound"); // typing stand-in
                },
            },

            new TutorialStep("Key bindings",
                "Every key can be changed: clear the search and click \"Input\" on the left — every editor action and playtest key with two slots. "
                + "Click a slot and press the new key; if another action uses it, you get Swap / Clear other / Cancel. the reset arrow at the end of a row resets it. "
                + "Close the options with Esc when you are done looking.")
            {
                Target = c => c.Game.OptionsOverlay.IsOpen.Value ? c.Game.OptionsOverlay.SidebarButtonFor("Input") : null,
                Requirement = c => c.Game.OptionsOverlay.IsOpen.Value || c.Bag.ContainsKey("optionsSeen") ? TutorialNeed.Editor()(c) : "The options were closed. Press {key:ShowOptions} to open them again.",
                Restore = c => c.Editor?.OpenOptions(),
                Progress = c =>
                {
                    if (c.Game.OptionsOverlay.IsOpen.Value) c.Bag["optionsSeen"] = true;
                    return null;
                },
                Done = c => c.Bag.ContainsKey("optionsSeen") && !c.Game.OptionsOverlay.IsOpen.Value,
                Demo = (d, c) =>
                {
                    d.Do(() => c.Game.OptionsOverlay.Search.Value = string.Empty); // clearing the box (typing stand-in)
                    d.Click(() => c.Game.OptionsOverlay.SidebarButtonFor("Input"));
                    d.Key(Key.Escape);
                    d.KeyIf(() => c.Game.OptionsOverlay.IsOpen.Value, Key.Escape);
                },
            },

            new TutorialStep("All shortcuts",
                "Forgot a key? {key:ShowShortcuts} lists every shortcut with your current bindings (also Help → Keyboard Shortcuts). Close the list with Esc.\n\n"
                + "That's it — you have seen the whole editor; time to chart your own songs. Help → Tutorial… brings any chapter back.")
            {
                Requirement = TutorialNeed.Editor(),
                Progress = c =>
                {
                    if (c.Dialog<ShortcutsDialog>() != null) c.Bag["shortcutsSeen"] = true;
                    return c.Bag.ContainsKey("shortcutsSeen") ? "Now close it (Esc)" : null;
                },
                Done = c => c.Bag.ContainsKey("shortcutsSeen") && !c.Dialogs.HasOpenDialog,
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ShowShortcuts);
                    d.WaitFor(() => c.Bag.ContainsKey("shortcutsSeen"), "the shortcuts list");
                    d.DialogKey(Key.Escape);
                },
            },
        },
    };
}
