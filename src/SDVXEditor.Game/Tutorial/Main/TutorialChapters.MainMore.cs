using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Effects;

namespace SDVXEditor.Game.Tutorial;

public static partial class TutorialChapters
{
    // ---- 3: navigating -------------------------------------------------------------------

    private static TutorialChapter mainNavigating() => new TutorialChapter
    {
        Number = 3,
        Title = "Navigating",
        Summary = "Timeline, mini map, zoom, playback speed, bookmarks and the collab list, jump to a time, modding timestamps, (extra) reference difficulty",
        Minutes = MainTutorialPlan.MinutesOf(3),
        Requirement = mainCompose,
        Restore = mainRestoreCompose,
        Steps = c =>
        {
            var def = c.Main;
            var t = c.Targets;
            var ex = def.Exercises;
            double dropMs = def.MsOfMeasure(def.Drop);
            string dropText = clock(dropMs);
            Drawable? rate(TutorialContext c, double r) => c.Editor?.BottomBar.RateButtons.FirstOrDefault(b => Math.Abs(b.Rate - r) < 0.01);
            string? lastTs(TutorialContext c) => (c.Editor?.ChartActions as EditorChartActions)?.LastTimestamp;
            Drawable? refItem(TutorialContext c) => MenuItem(c, "File", "Open Difficulty for Reference", "ADV");

            return new List<TutorialStep>
            {
                new TutorialStep("The timeline",
                    "The band at the top is the [b]timeline[/b]: the song's waveform with a ruler of bars and beats.\n"
                    + "• Click or drag in it to move through the song.\n"
                    + "• Coloured marks are timing points ([c=ff5050]red[/c] BPM, [c=7ee07e]green[/c] speed) and bookmarks.\n"
                    + "• [ui]+[/ui] / [ui]-[/ui] on the left zoom it.")
                {
                    Anchor = "editor.timeline",
                },

                new TutorialStep("The mini map",
                    "The column left of the playfield is a [b]mini map[/b] of all notes: the whole song from bottom to top. "
                    + "The light box is what the playfield shows right now. Click in it to jump there.")
                {
                    Target = c => c.Editor?.Density,
                },

                new TutorialStep("Zoom",
                    "Hold [k]Alt[/k] and turn the mouse wheel over the playfield (or the timeline): both zoom together. "
                    + "Zoom in to spread dense notes apart, out to see more at once.")
                {
                    Anchor = "editor.playfield",
                    Enter = c => c.Manager.WhenEditorReady(() => c.Bag["zoom"] = c.Editor!.Zoom.Value),
                    Progress = c => c.Editor is { } e ? $"Zoom: {e.Zoom.Value:0.00}×" : null,
                    Done = c => c.Bag.TryGetValue("zoom", out var z) && z is float z0 && c.Editor is { } e && Math.Abs(e.Zoom.Value - z0) >= 0.09f,
                    Demo = (d, c) => d.Wheel(() => c.Editor!.Playfield.ScreenSpaceDrawQuad.Centre, 2, Key.LAlt),
                },

                new TutorialStep("Slow it down",
                    "235 BPM is fast. Listen at half speed — the pitch stays the same.")
                {
                    Tasks = new[]
                    {
                        new TutorialTask("Click [ui]50%[/ui] at the bottom.", c => c.Editor is { } e && Math.Abs(e.Playback.Rate.Value - 0.5) < 0.01 || c.Bag.ContainsKey("halfPlayed"))
                        {
                            Target = c => rate(c, 0.5),
                        },
                        new TutorialTask("Press {key:PlayPause} to play.", c =>
                        {
                            if (c.Editor is { } e && Math.Abs(e.Playback.Rate.Value - 0.5) < 0.01 && e.Playback.IsPlaying.Value) c.Bag["halfPlayed"] = true;
                            return c.Bag.ContainsKey("halfPlayed");
                        }) { Target = c => c.Editor?.BottomBar },
                        new TutorialTask("Press {key:PlayPause} again to pause.", c => c.Bag.ContainsKey("halfPlayed") && c.Editor?.Playback.IsPlaying.Value == false) { Target = c => c.Editor?.BottomBar },
                        new TutorialTask("Click [ui]100%[/ui] to get back to full speed.", c => c.Editor is { } e && Math.Abs(e.Playback.Rate.Value - 1) < 0.01) { Target = c => rate(c, 1) },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => rate(c, 0.5));
                        task(d, c, 1);
                        d.Press(EditorAction.PlayPause);
                        task(d, c, 2);
                        d.Press(EditorAction.PlayPause);
                        task(d, c, 3);
                        d.Click(() => rate(c, 1));
                    },
                },

                new TutorialStep("Bookmarks",
                    $"Bookmarks mark places to come back to. This song already has {def.Sections.Count} — {string.Join(", ", def.Sections.Select(s => s.Name))} — the flags in the timeline.\n"
                    + "• {key:PreviousBookmark} / the bookmark arrow to the left jumps back, the bookmark button with the minus removes the nearest one.")
                {
                    Enter = c =>
                    {
                        c.Bag["bookmarks"] = c.Chart?.Editor.Bookmarks.Count ?? 0;
                        SeekTo(c, at(4)); // somewhere without a bookmark yet
                    },
                    Tasks = new[]
                    {
                        new TutorialTask("Add your own: press {key:AddBookmark} or click the bookmark button with the plus at the bottom.", c =>
                        {
                            if (!c.Bag.ContainsKey("bookmarkAt") && c.Chart is { } ch && c.Bag["bookmarks"] is int n && ch.Editor.Bookmarks.Count > n && c.Editor is { } e)
                                c.Bag["bookmarkAt"] = e.Playback.CurrentTime.Value;
                            return c.Bag.ContainsKey("bookmarkAt");
                        }) { Target = c => c.Editor?.BottomBar.AddBookmarkButton },
                        new TutorialTask("Jump to the next one: {key:NextBookmark} or the bookmark arrow to the right.", c => c.Bag.TryGetValue("bookmarkAt", out var x) && x is double added && c.Editor is { } e
                                                                                                         && Math.Abs(e.Playback.CurrentTime.Value - added) > 50 && e.Bookmarks.Any(b => Math.Abs(b - e.Playback.CurrentTime.Value) < 2))
                        {
                            Target = c => c.Editor?.BottomBar.NextBookmarkButton,
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Editor?.BottomBar.AddBookmarkButton);
                        task(d, c, 1);
                        d.Press(EditorAction.NextBookmark);
                    },
                },

                new TutorialStep("The collab list",
                    "Charting with a friend? [ui]Collab[/ui] lists every bookmark with a label and a colour — notes for each other like \"your part starts here\".\n"
                    + "• Double-click a row to jump there.\n"
                    + "• Rename a bookmark, pick a colour, delete it.\n"
                    + "Bookmarks are saved in the chart (and survive in .ksh too).")
                {
                    Requirement = TutorialNeed.Editor(),
                    Tasks = new[]
                    {
                        new TutorialTask("Click [ui]Collab[/ui].", c =>
                        {
                            if (c.Dialog<BookmarkListDialog>() != null) c.Bag["collabOpened"] = true;
                            return c.Bag.ContainsKey("collabOpened");
                        }) { Target = c => c.Editor?.BottomBar.CollabButton },
                        new TutorialTask("Look at it, then close it: [k]Esc[/k] or [ui]Close[/ui].", c => c.Bag.ContainsKey("collabOpened") && !c.Dialogs.HasOpenDialog) { Target = c => c.Dialog<BookmarkListDialog>() },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Editor?.BottomBar.CollabButton);
                        task(d, c, 1);
                        d.DialogKey(Key.Escape);
                    },
                },

                new TutorialStep("Jump to a time",
                    "The time at the bottom left is also a button.")
                {
                    Requirement = c => c.Dialog<JumpToTimeDialog>() != null || c.Bag.ContainsKey("jumpOpened") ? TutorialNeed.Editor()(c) : mainCompose(c),
                    Restore = mainRestoreCompose,
                    Tasks = new[]
                    {
                        new TutorialTask("Click the time: a small window asks where to go.", c =>
                        {
                            if (c.Dialog<JumpToTimeDialog>() != null) c.Bag["jumpOpened"] = true;
                            return c.Bag.ContainsKey("jumpOpened");
                        }) { Target = c => c.Editor?.BottomBar.TimeDisplay },
                        new TutorialTask($"Type [b]{dropText}[/b] and press [k]Enter[/k]: you land on the drop.",
                            c => c.Dialog<JumpToTimeDialog>() == null && c.Editor is { } e && Math.Abs(e.Playback.CurrentTime.Value - dropMs) < 60)
                        {
                            Target = c => c.Dialog<JumpToTimeDialog>(),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Editor?.BottomBar.TimeDisplay);
                        task(d, c, 1);
                        d.Do(() => c.Dialog<JumpToTimeDialog>()!.TimeBox.Text = dropText); // typing stand-in
                        d.DialogKey(Key.Enter);
                    },
                },

                new TutorialStep("Modding timestamps",
                    "When charters review each other's charts (\"modding\", like in osu!), they point at notes with [b]timestamps[/b]. In-game charters rarely do this — the feature is meant for exactly that kind of feedback.\n\n"
                    + "Copying notes puts such a timestamp on the clipboard, e.g. [b]00:00:682 (00:00:682|…) -[/b]. Paste it anywhere you can type text: a chat, a document, a comment.")
                {
                    Enter = c =>
                    {
                        Show(c, ex.Chips.FromPulse, ex.Chips.ToPulse);
                        c.Bag["ts"] = lastTs(c);
                    },
                    Requirement = TutorialNeed.Editor(EditorTab.Compose),
                    Ghosts = _ => t.Chips,
                    Tasks = new[]
                    {
                        toolTask(Screens.Edit.Components.EditorTool.Select, EditorAction.ToolSelect, "Select"),
                        new TutorialTask($"Drag a box around the chips of {bars(ex.Chips.FromPulse, ex.Chips.ToPulse)}.", c => c.Session is { } s && t.Chips.All(x => mainIsSelected(s, x)))
                        {
                            Area = c => c.PlayfieldRegion(ex.Chips.FromPulse - beat / 2, ex.Chips.ToPulse - beat / 4),
                        },
                        new TutorialTask("Press {key:Copy}: the notes and their timestamp are copied.", c => lastTs(c) is { } ts && !ReferenceEquals(ts, c.Bag["ts"]))
                        {
                            Area = c => region(c, t.Chips),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolSelect);
                        task(d, c, 1);
                        d.Drag(pf(c, ex.Chips.FromPulse - beat / 2 + 20, PlayfieldLayout.BT_LEFT + 6), pf(c, ex.Chips.ToPulse - beat / 2, PlayfieldLayout.BT_RIGHT - 6));
                        task(d, c, 2);
                        d.Press(EditorAction.Copy);
                    },
                },

                new TutorialStep("Open a timestamp",
                    "Now the other way round: someone sent you a timestamp. The jump window understands it.")
                {
                    Enter = c =>
                    {
                        SeekTo(c, at(8)); // away from the chips: the jump has to bring you back
                        c.Bag["jump"] = c.Editor?.LastJump;
                    },
                    Requirement = c => c.Dialog<JumpToTimeDialog>() != null || c.Bag.ContainsKey("tsJumpOpened") ? TutorialNeed.Editor()(c) : mainCompose(c),
                    Restore = mainRestoreCompose,
                    Tasks = new[]
                    {
                        new TutorialTask("Click the time at the bottom left.", c =>
                        {
                            if (c.Dialog<JumpToTimeDialog>() != null) c.Bag["tsJumpOpened"] = true;
                            return c.Bag.ContainsKey("tsJumpOpened");
                        }) { Target = c => c.Editor?.BottomBar.TimeDisplay },
                        new TutorialTask("Paste the timestamp ([k]Ctrl+V[/k]) and press [k]Enter[/k]: the editor jumps there and selects exactly those notes.",
                            c => c.Dialog<JumpToTimeDialog>() == null && c.Editor?.LastJump is { } j && !ReferenceEquals(j, c.Bag["jump"]) && j.Count >= t.Chips.Count)
                        {
                            Target = c => c.Dialog<JumpToTimeDialog>(),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Editor?.BottomBar.TimeDisplay);
                        task(d, c, 1);
                        d.Do(() => c.Dialog<JumpToTimeDialog>()!.TimeBox.Text = lastTs(c)!); // pasting stand-in
                        d.DialogKey(Key.Enter);
                    },
                },

                new TutorialStep("Extra: a reference difficulty",
                    "Optional — [ui]Skip step[/ui] if you chart one difficulty only. Charting several? Open another one beside yours, [b]read-only[/b]: it sits to the right, greyed out, "
                    + "and scrolls with yours. Its notes can be copied ({key:Copy}) and pasted into yours.")
                {
                    Tasks = new[]
                    {
                        new TutorialTask("Click [ui]File[/ui] in the menu bar.", c => MenuItem(c, "File", "Open Difficulty for Reference") != null || c.Editor?.ReferenceSession != null)
                        {
                            Target = c => MenuItem(c, "File"),
                        },
                        new TutorialTask("Point at [ui]Open Difficulty for Reference[/ui]: a submenu opens.", c => refItem(c) != null || c.Editor?.ReferenceSession != null)
                        {
                            Target = c => MenuItem(c, "File", "Open Difficulty for Reference"),
                        },
                        new TutorialTask("Click [ui]ADV[/ui] in the submenu.", c =>
                        {
                            if (c.Editor?.ReferenceSession != null) c.Bag["referenceOpened"] = true;
                            return c.Bag.ContainsKey("referenceOpened");
                        }) { Target = refItem },
                        new TutorialTask("Press [k]Esc[/k] to close it again (Esc closes the reference before it ever leaves the editor).", c => c.Bag.ContainsKey("referenceOpened") && c.Editor is { ReferenceSession: null })
                        {
                            Target = c => c.Editor?.ReferencePlayfield,
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.WaitFor(() => MenuItem(c, "File") != null, "the File menu");
                        d.Click(() => MenuItem(c, "File"));
                        task(d, c, 1);
                        d.Click(() => MenuItem(c, "File", "Open Difficulty for Reference"));
                        task(d, c, 2);
                        d.Click(() => refItem(c));
                        task(d, c, 3);
                        d.Press(EditorAction.Exit);
                    },
                },
            };
        },
    };

    // ---- 4: camera & background -----------------------------------------------------------

    private static TutorialChapter mainCamera() => new TutorialChapter
    {
        Number = 4,
        Title = "Camera & Background",
        Summary = "Camera lanes (zoom, tilt, auto tilt, spins) and their anchors, the live preview, background, post effects, export badges",
        Minutes = MainTutorialPlan.MinutesOf(4),
        Requirement = TutorialNeed.Editor(EditorTab.Camera),
        Restore = TutorialNeed.RestoreEditor(EditorTab.Camera),
        Steps = c =>
        {
            var def = c.Main;
            var slam = c.Targets.Slam!;
            int drop = def.Drop;
            CameraTabContent? cam() => c.Editor?.CameraContent;
            Vector2 lane(CamLane l, long pulse, double fx)
            {
                var lanes = cam()!.Lanes;
                return lanes.ToScreenSpace(new Vector2(CameraLanes.LaneLeft(l) + (float)fx * CameraLaneEditor.LANE_WIDTH, lanes.YOfPulse(pulse)));
            }

            // the three zoom anchors at the drop, the tilt point, the spin and its end
            var zoom = new[] { (at(drop), 0.5), (at(drop, 2), 0.7), (at(drop + 1), 0.8) };
            long tiltAt = at(drop + 2, 1);
            const double tilt_fx = 0.7;
            double spinFx = (CameraLaneEditor.PatternColumnLeft(CamPatternKind.Spin) + CameraLaneEditor.PATTERN_BAR_WIDTH / 2) / CameraLaneEditor.LANE_WIDTH;
            long spinEnd = slam.Y + MainTutorialSong.Measure;

            bool zoomAnchor(Chart ch, long pulse, double fx)
            {
                double v = CameraLaneEditor.ValueOf(CamLane.ZoomBottom, fx);
                return CameraPaths.Runs(ch.Camera, CamPathLane.ZoomBottom).Any(r => r.Anchors().Anchors.Any(a => r.Y + a.Ry == pulse && Math.Abs(a.V - v) <= 40)
                                                                                     || r.Points.Any(p => r.Y + p.Ry == pulse && Math.Abs(p.V - v) <= 40));
            }

            CamRun? zoomRun(TutorialContext c) => c.Chart is { } ch ? CameraPaths.Runs(ch.Camera, CamPathLane.ZoomBottom).FirstOrDefault(r => r.Y >= at(drop) - beat) : null;
            bool tiltPlaced(Chart ch) => ch.Camera.Tilt.Any(p => p.Y == tiltAt && p.Value.Auto == null && Math.Abs(p.Value.Manual - CameraLaneEditor.ValueOf(CamLane.Tilt, tilt_fx)) <= 0.3);
            CamPattern? spin(Chart ch) => ch.Camera.Spin.FirstOrDefault(p => p.Y == slam.Y);
            Drawable? viewButton(string name) => TutorialContext.FindText(cam(), name);
            bool backgroundShown() => TutorialContext.FindText(cam(), "bg_no", contains: true) is { } h && h.ScreenSpaceDrawQuad.AABBFloat.Width > 1 && cam()?.View.Value != 0 && cam()?.ExportMatrix is { } m && !TutorialAnchors.IsShown(m)
                                      || TutorialContext.FindText(cam(), "background image", contains: true) != null;
            bool exportShown() => cam()?.ExportMatrix is { } m && TutorialAnchors.IsShown(m);
            // the right-hand panel of the background view (the view's own container, found from its first header)
            Drawable? bgPanel()
            {
                var header = TutorialContext.FindText(cam(), "background image (", contains: true);
                for (Drawable? p = header?.Parent; p != null; p = p.Parent)
                {
                    if (p.ScreenSpaceDrawQuad.AABBFloat.Height > 400) return p;
                }

                return null;
            }

            return new List<TutorialStep>
            {
                new TutorialStep("The camera tab",
                    "In SOUND VOLTEX the lane moves with the music: it [b]zooms[/b], [b]tilts[/b] and [b]spins[/b]. All of that is drawn on the camera tab.\n\n"
                    + "Press {key:TabCamera} or click [ui]camera[/ui].")
                {
                    Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "camera"),
                    Requirement = TutorialNeed.Editor(),
                    Restore = TutorialNeed.RestoreEditor(),
                    Done = c => c.Editor?.CurrentTab == EditorTab.Camera,
                    Demo = (d, _) => d.Press(EditorAction.TabCamera),
                },

                new TutorialStep("The camera lanes",
                    "Each column is one camera curve over time — time runs up, like the notes on the left:\n"
                    + "• [ui]zoom_top[/ui] — tips the far end of the lane up or down\n"
                    + "• [ui]zoom_bottom[/ui] — moves the lane towards you or away\n"
                    + "• [ui]zoom_side[/ui] — shifts it sideways\n"
                    + "• [ui]center_split[/ui] — splits the lane in the middle\n"
                    + "• [ui]rotation_deg[/ui] — turns it\n"
                    + "• [ui]tilt[/ui] — leans it left/right · [ui]spin / swing[/ui] — spins on slams\n\n"
                    + "The badges under a name ([c=7ee07e]kson ✓[/c] [c=7ee07e]ksh ✓[/c] [c=ff9f43]vox ~[/c] …) say which file formats keep that curve.")
                {
                    Anchor = "camera.lanes",
                },

                new TutorialStep("Zoom in at the drop",
                    $"At the drop ({bars(at(drop), at(drop + 1))}) the lane should rush towards you. Camera points are anchors like on a laser: [b]three clicks in a row bend into a curve[/b].\n\n"
                    + "Click the rings [b]1 → 2 → 3[/b] in the zoom_bottom column (further right = bigger zoom). They turn [c=7ee07e]green[/c] when the anchor is right.\n"
                    + "• Drag a point to change it later; [k]Shift[/k]+click makes an instant jump.")
                {
                    Enter = c => SeekTo(c, at(drop) - beat),
                    Area = c => c.CameraColumn(CamLane.ZoomBottom, at(drop) - beat / 2, at(drop + 1) + beat / 2),
                    Zone = _ => (at(drop), at(drop + 1)),
                    ZoneLabel = _ => "the drop · " + bars(at(drop), at(drop + 1)),
                    CameraGhosts = _ => zoom.Select((z, i) => new CameraGhost(CamLane.ZoomBottom, z.Item1, z.Item2, i + 1, ch => zoomAnchor(ch, z.Item1, z.Item2))).ToList(),
                    Progress = c => c.Chart is { } ch ? $"{zoom.Count(z => zoomAnchor(ch, z.Item1, z.Item2))} / 3 anchors" + (zoomRun(c) is { Curved: true } ? " · curved ✓" : "") : null,
                    Done = c => c.Chart is { } ch && zoom.All(z => zoomAnchor(ch, z.Item1, z.Item2)) && zoomRun(c) is { Curved: true },
                    Demo = (d, c) =>
                    {
                        foreach (var (p, fx) in zoom) d.ClickAt(() => lane(CamLane.ZoomBottom, p, fx));
                    },
                },

                new TutorialStep("Camera anchor types",
                    "Camera anchors have the same menu as laser anchors: [ui]Inherit[/ui], [ui]Straight[/ui], [ui]Perfect curve[/ui], [ui]Bezier[/ui], [ui]Delete anchor[/ui].")
                {
                    Tasks = new[]
                    {
                        new TutorialTask("Right-click anchor [b]1[/b] (at the drop line).", c => cam()?.Lanes.OpenMenu != null || firstIsBezier(c))
                        {
                            Area = c => c.CameraColumn(CamLane.ZoomBottom, at(drop) - beat / 2, at(drop) + beat / 2),
                        },
                        new TutorialTask("Pick [ui]Bezier[/ui].", firstIsBezier)
                        {
                            Target = c => cam()?.Lanes.OpenMenu?.ChildrenOfType<LaserContextMenuItem>().FirstOrDefault(i => i.Label == "Bezier"),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.ClickAt(() => lane(CamLane.ZoomBottom, at(drop), 0.5), right: true);
                        task(d, c, 1);
                        d.Click(() => cam()?.Lanes.OpenMenu?.ChildrenOfType<LaserContextMenuItem>().FirstOrDefault(i => i.Label == "Bezier"));
                    },
                },

                new TutorialStep("The preview",
                    "The preview shows the lane with your camera at the cursor (an approximation of the game's camera).")
                {
                    Enter = c => SeekTo(c, at(drop) - 2 * beat),
                    Tasks = new[]
                    {
                        new TutorialTask("Press {key:PlayPause} and watch the zoom at the drop.", c =>
                        {
                            if (c.Editor?.Playback.IsPlaying.Value == true) c.Bag["played"] = true;
                            return c.Bag.ContainsKey("played");
                        }) { Target = c => cam()?.Preview },
                        new TutorialTask("Press {key:PlayPause} again to pause.", c => c.Bag.ContainsKey("played") && c.Editor?.Playback.IsPlaying.Value == false) { Target = c => cam()?.Preview },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.PlayPause);
                        task(d, c, 1);
                        d.Press(EditorAction.PlayPause);
                    },
                },

                new TutorialStep("Manual tilt",
                    $"[ui]tilt[/ui] leans the lane: left of the middle = to the left, right = to the right. A point here is a [b]manual[/b] tilt value.\n\n"
                    + $"Click the ring in the tilt column ({bars(tiltAt, tiltAt + beat)}).")
                {
                    Enter = c => SeekTo(c, at(drop + 2) - beat),
                    Area = c => c.CameraColumn(CamLane.Tilt, tiltAt - beat, tiltAt + beat),
                    Zone = _ => (tiltAt, tiltAt + beat),
                    ZoneLabel = _ => bars(tiltAt, tiltAt + beat),
                    CameraGhosts = _ => new[] { new CameraGhost(CamLane.Tilt, tiltAt, tilt_fx, 1, tiltPlaced) },
                    Done = c => c.Chart is { } ch && tiltPlaced(ch),
                    Demo = (d, c) => d.ClickAt(() => lane(CamLane.Tilt, tiltAt, tilt_fx)),
                },

                new TutorialStep("Automatic tilt",
                    "Without manual points the game tilts the lane [b]by itself[/b] when the lasers pull to one side. [ui]tilt mode at the cursor[/ui] picks how strongly, from the cursor on:\n"
                    + "• [ui]normal[/ui] — the default\n"
                    + "• [ui]bigger[/ui] / [ui]biggest[/ui] — leans further\n"
                    + "• [ui]zero[/ui] — no automatic tilt\n"
                    + "• [ui]keep[/ui] — the lane stays leaning where it is instead of swinging back\n"
                    + "Modes show as a coloured strip in the tilt column.")
                {
                    Enter = c => SeekTo(c, at(drop + 4)),
                    Target = c => viewButton("bigger"),
                    Done = c => c.Chart is { } ch && ch.Camera.Tilt.Any(p => p.Value.Auto is TiltMode.Bigger or TiltMode.KeepBigger),
                    Progress = _ => "Click \"bigger\"",
                    Demo = (d, c) => d.Click(() => viewButton("bigger")),
                },

                new TutorialStep("A spin on a slam",
                    $"Spins and swings always belong to a [b]slam[/b] — here the one in {bars(slam.Y - slam.Y % MainTutorialSong.Measure, slam.Y - slam.Y % MainTutorialSong.Measure + MainTutorialSong.Measure)}. "
                    + "A spin/swing without a slam is framed [c=ff5050]red[/c]: the game ignores it.\n"
                    + "• [ui]spin[/ui] — a full turn · [ui]half spin[/ui] · [ui]swing[/ui] — a wobble\n"
                    + "• its direction follows the slam ([ui]flip L/R[/ui] in the panel changes it)")
                {
                    Enter = c => SeekTo(c, slam.Y - beat),
                    Zone = _ => (slam.Y, spinEnd),
                    ZoneLabel = _ => "the slam",
                    CameraGhosts = _ => new[]
                    {
                        new CameraGhost(CamLane.Patterns, slam.Y, spinFx, 1, ch => spin(ch) != null),
                        new CameraGhost(CamLane.Patterns, spinEnd, spinFx, 2, ch => spin(ch)?.Length == MainTutorialSong.Measure),
                    },
                    Tasks = new[]
                    {
                        new TutorialTask("Under [ui]new spin / swing[/ui], pick [ui]spin[/ui].", c => cam()?.LaneEditor.PatternKind.Value == CamPatternKind.Spin) { Target = c => viewButton("spin") },
                        new TutorialTask("Click ring [b]1[/b] in the spin / swing column, at the slam: a spin bar appears (it snaps to the slam).", c => c.Chart is { } ch && spin(ch) != null)
                        {
                            Area = c => c.CameraColumn(CamLane.Patterns, slam.Y - beat / 2, slam.Y + beat / 2),
                        },
                        new TutorialTask("Its length is how long it turns. Drag the bar's [b]top end[/b] up to ring [b]2[/b] — one bar long (like a hold's end).", c => c.Chart is { } ch && spin(ch)?.Length == MainTutorialSong.Measure)
                        {
                            Area = c => c.CameraColumn(CamLane.Patterns, slam.Y, spinEnd + beat / 2),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        // "spin" is the default kind: the first task is ticked before anyone clicks
                        task(d, c, 1);
                        d.ClickAt(() => lane(CamLane.Patterns, slam.Y, spinFx));
                        task(d, c, 2);
                        d.MoveTo(() => lane(CamLane.Patterns, slam.Y + (spinOf(c)?.Length ?? 0), spinFx));
                        d.Drag(() => lane(CamLane.Patterns, slam.Y + (spinOf(c)?.Length ?? 0), spinFx), () => lane(CamLane.Patterns, spinEnd, spinFx));
                    },
                },

                new TutorialStep("The background view",
                    "Besides the camera, this panel has more views at the top.\n\n"
                    + "Click [ui]background[/ui].")
                {
                    Target = c => viewButton("background"),
                    Done = c => backgroundShown(),
                    Demo = (d, c) => d.Click(() => viewButton("background")),
                },

                new TutorialStep("The background preview",
                    "Behind the lane, K-Shoot Mania and the game show different things:\n"
                    + "• the [b]preview[/b] on the left shows what K-Shoot shows (the image, the animation layer)\n"
                    + "• the lines on the right say what [b]the game[/b] plays — its own background animation, chosen by number (bg_no)")
                {
                    Target = c => cam()?.BgPreview is { } p && TutorialAnchors.IsShown(p) ? p : null,
                },

                new TutorialStep("K-Shoot backgrounds",
                    "For K-Shoot Mania (kson / ksh — the game ignores these):\n"
                    + "• [ui]background image[/ui] — a picture in the song folder or a KSM preset (desert, night, cyber …); a second one replaces it once the gauge reaches 70 %\n"
                    + "• [ui]animation layer[/ui] — an animated overlay (smoke, snow, sakura …); loop ms < 0 plays it backwards, 0 = synced to the tempo\n"
                    + "• [ui]movie[/ui] — a video file, with an offset in ms")
                {
                    Area = c => section(bgPanel(), "background image (", "game background ("),
                },

                new TutorialStep("Game background and post effects",
                    "For the game (VOX export through ksh2vox):\n"
                    + "• [ui]bg_no[/ui] — the number of the game's background animation (real songs use 2, 7, 10, 12, 60, 63 …); empty = the converter's default. The line beside it names the song that first used it.\n"
                    + "• [ui]post effects[/ui] — screen effects at a time (chromatic aberration, glitches …); [ui]add at cursor[/ui] places one, the [ui]screen fx[/ui] view shows them as lanes\n"
                    + "• [ui]SPCONTROLER rows[/ui] — raw camera rows of the game; [ui]add template defaults[/ui] fills in the usual ones")
                {
                    Area = c => section(bgPanel(), "game background (", "SPCONTROLER rows") is { } r ? new RectangleF(r.X, r.Y, r.Width, r.Height + 64) : null, // down to the SPCONTROLER buttons
                },

                new TutorialStep("What each format keeps",
                    "Click [ui]export[/ui]: a table of what each format keeps.\n"
                    + "• [c=7ee07e]✓[/c] kept · [c=ff9f43]~[/c] approximated · [c=ff5050]x[/c] lost\n"
                    + "• [b]kson[/b] keeps everything; [b]ksh[/b] most of it; [b]VOX[/b] (for the game) needs ksh2vox 0.17 or newer for camera work.\n"
                    + "The same badges sit under the lane names.")
                {
                    Target = c => exportShown() ? cam()?.ExportMatrix : viewButton("export"),
                    Done = c => exportShown(),
                    Demo = (d, c) => d.Click(() => viewButton("export")),
                },

                saveStep(),
            };

            bool firstIsBezier(TutorialContext c) => zoomRun(c) is { } r && cam() is { } ct && ct.LaneEditor.AnchorsOf(r).Anchors.FirstOrDefault() is { Type: LaserCurveType.Bezier, Auto: false };
            CamPattern? spinOf(TutorialContext c) => c.Chart is { } ch ? spin(ch) : null;
        },
    };

    // ---- 5: effects & sounds --------------------------------------------------------------

    private static TutorialChapter mainEffects() => new TutorialChapter
    {
        Number = 5,
        Title = "Effects & Sounds",
        Summary = "FX hold effects, laser filters and their switches, custom FX chip sounds",
        Minutes = MainTutorialPlan.MinutesOf(5),
        Requirement = TutorialNeed.Editor(EditorTab.Effects),
        Restore = TutorialNeed.RestoreEditor(EditorTab.Effects),
        Steps = c =>
        {
            var t = c.Targets;
            var hold = t.FxHold[0];
            EffectsTabContent? fxTab() => c.Editor?.EffectsContent;
            Note? holdNote(Chart ch) => ch.Notes.Fx[hold.Lane].FirstOrDefault(n => n.Y == hold.Y && n.IsHold);
            int chipsWithSounds(Chart ch) => ch.Notes.Fx.SelectMany(l => l).Count(n => !n.IsHold && n.KeySound != null);
            bool holdSelected(TutorialContext c) => c.Session is { } s && s.Selection.Notes.Any(r => r.Kind == NoteKind.Fx && r.Lane == hold.Lane && r.Note.Y == hold.Y);
            var assignBox = new Func<Drawable?>(() => dropdownBeside(fxTab(), "assign"));
            Drawable? filterAtCursor() => mainFilterAtCursor(fxTab());
            Drawable? filterBox() => filterAtCursor()?.Parent is { } row ? row.ChildrenOfType<SDVXEditor.Game.Graphics.UserInterface.OsuDropdown<string>>().FirstOrDefault(TutorialAnchors.IsShown) : null;
            long filterAt = t.StraightLaser!.Y;
            string? filterAtName(Chart ch) => ch.Audio.LaserEffects.PulseEvents.FirstOrDefault(kv => kv.Value.Contains(filterAt)).Key;
            // the row of the switch list: its "x" button (tooltip "Deletes this filter switch …") and the text beside it
            Drawable? filterRow() => fxTab()?.ChildrenOfType<Graphics.UserInterface.FlatButton>()
                                             .FirstOrDefault(b => TutorialAnchors.IsShown(b) && b.TooltipText.ToString().StartsWith("Deletes this filter switch", StringComparison.Ordinal))?.Parent;

            return new List<TutorialStep>
            {
                new TutorialStep("The effects tab",
                    "FX notes and lasers can [b]change the music[/b] while they are played: an FX hold can stutter or filter it, a laser sweeps a filter as the knob turns.\n\n"
                    + "Press {key:TabEffects} or click [ui]effects[/ui].")
                {
                    Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "effects"),
                    Requirement = TutorialNeed.Editor(),
                    Restore = TutorialNeed.RestoreEditor(),
                    Done = c => c.Editor?.CurrentTab == EditorTab.Effects,
                    Demo = (d, _) => d.Press(EditorAction.TabEffects),
                },

                new TutorialStep("Select the FX hold",
                    "An effect goes onto [b]selected[/b] FX holds. The small playfield on the left shows the chart around the cursor — the FX hold from chapter 1 is in it now.\n\n"
                    + "Click [ui]select FX in view[/ui]: it selects every FX note that playfield shows. They flash [c=4f8dff]blue[/c] there, the line under the button says how many, "
                    + "and the list highlights them.")
                {
                    Enter = c => SeekTo(c, hold.Y),
                    Target = c => TutorialContext.FindText(fxTab(), "select FX in view"),
                    Progress = c => holdSelected(c) ? "FX hold selected ✓" : null,
                    Done = holdSelected,
                    Demo = (d, c) => d.Click(() => TutorialContext.FindText(fxTab(), "select FX in view")),
                },

                new TutorialStep("Pick an effect",
                    "While an FX hold is held, its effect plays:\n"
                    + "• [ui]retrigger[/ui] — repeats a slice of the music\n"
                    + "• [ui]gate[/ui] — chops it rhythmically\n"
                    + "• [ui]flanger[/ui], [ui]phaser[/ui], [ui]wobble[/ui], [ui]bitcrusher[/ui], [ui]tapestop[/ui] …\n\n"
                    + "(Retrigger, gate, wobble and echo need a [b]wave length[/b] in a definition — as plain presets they stay silent.)\n\n"
                    + "Under [ui]2 · assign to selected FX holds[/ui], open the box and pick [ui]flanger[/ui] — a sweeping, jet-plane sound.")
                {
                    Area = c => TutorialContext.Union(assignBox(), c.OpenPopup()), // the box, and its list while open
                    Done = c => assignBox() is SDVXEditor.Game.Graphics.UserInterface.OsuDropdown<string> dd && dd.Current.Value == "flanger",
                    Demo = (d, c) =>
                    {
                        d.Click(() => assignBox());
                        d.WaitFor(() => TutorialContext.FindText(c.OpenPopup() ?? assignBox(), "flanger") != null, "the effect list");
                        d.Click(() => TutorialContext.FindText(c.OpenPopup() ?? assignBox(), "flanger"));
                    },
                },

                new TutorialStep("Assign it",
                    "Click [ui]assign[/ui]: the selected FX hold gets the flanger. The list shows it beside the hold; [ui]clear[/ui] would remove it again.")
                {
                    Target = c => TutorialContext.FindText(fxTab(), "assign"),
                    Done = c => c.Chart is { } ch && holdNote(ch)?.Effects.Count > 0,
                    Demo = (d, c) => d.Click(() => TutorialContext.FindText(fxTab(), "assign")),
                },

                new TutorialStep("Laser filters",
                    "While a laser is turned, it filters the music. Which filter, is set by [b]filter switches[/b]:\n"
                    + "• [ui]peaking_filter[/ui] — a boosted band sweeps with the knob (the default \"wah\")\n"
                    + "• [ui]low_pass_filter[/ui] — cuts the highs: muffled, underwater\n"
                    + "• [ui]high_pass_filter[/ui] — cuts the lows: thin, tinny\n"
                    + "• [ui]bitcrusher[/ui] — crunchy, digital\n\n"
                    + "[ui]play effects during editor playback[/ui] lets you hear them while the song plays in the editor.")
                {
                    Target = c => TutorialContext.FindText(fxTab(), "laser filter switches"),
                },

                new TutorialStep("A filter switch",
                    "The cursor is at the straight laser from chapter 2.")
                {
                    Enter = c => SeekTo(c, filterAt),
                    Tasks = new[]
                    {
                        new TutorialTask("Open the box under [ui]laser filter switches[/ui] and pick [ui]low_pass_filter[/ui].", c => filterBox() is SDVXEditor.Game.Graphics.UserInterface.OsuDropdown<string> dd && dd.Current.Value == "low_pass_filter"
                                                                                                                                       || c.Chart is { } ch && filterAtName(ch) == "low_pass_filter")
                        {
                            Area = c => TutorialContext.Union(filterBox(), c.OpenPopup()),
                        },
                        new TutorialTask("Click [ui]at cursor[/ui]: lasers use it [b]from here on[/b], until the next switch. ([ui]at selected sections[/ui] sets it for selected lasers only.)",
                            c => c.Chart is { } ch && filterAtName(ch) == "low_pass_filter")
                        {
                            Target = c => filterAtCursor(),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(filterBox);
                        d.WaitFor(() => TutorialContext.FindText(c.OpenPopup() ?? filterBox(), "low_pass_filter") is { } i && i != filterBox(), "the filter list");
                        d.Click(() => TutorialContext.FindText(c.OpenPopup() ?? filterBox(), "low_pass_filter"));
                        task(d, c, 1);
                        d.Click(filterAtCursor);
                    },
                },

                new TutorialStep("The switch list",
                    "Your switch is now in the list: its position ([b]bar:beat:pulse[/b]) and the filter. The red [c=ff5050]x[/c] at the end of a row deletes that switch. "
                    + "Before the first switch, lasers use peaking_filter.")
                {
                    Target = c => filterRow(),
                },

                new TutorialStep("Hear the effects",
                    "The editor can play FX hold effects, laser filters and slam volume on the song — in the editor and in the playtest.")
                {
                    Enter = c => c.Manager.WhenEditorReady(() =>
                    {
                        // notice a press of "preview laser filter" (the tab hands the preview to the editor through this hook)
                        if (fxTab() is not { } tab || c.Bag.ContainsKey("previewHooked")) return;
                        c.Bag["previewHooked"] = true;
                        var inner = tab.PreviewEffect;
                        tab.PreviewEffect = (name, laser) =>
                        {
                            if (laser && c.Manager.Context == c) c.Bag["previewed"] = true;
                            return inner?.Invoke(name, laser) ?? false;
                        };
                    }),
                    Tasks = new[]
                    {
                        new TutorialTask("Tick [ui]play effects during playback[/ui] under [ui]editor playback[/ui].", c => fxTab()?.PlayEffects.Value == true)
                        {
                            Target = c => TutorialContext.FindText(fxTab(), "play effects during playback", contains: true),
                        },
                        new TutorialTask("Click [ui]preview laser filter[/ui]: the low-pass filter you picked sweeps over 2 s of the song from the cursor (knob 0 → 1 → 0). "
                                         + "[ui]preview FX effect[/ui] plays the effect picked under \"assign\" the same way.", c => c.Bag.ContainsKey("previewed"))
                        {
                            Target = c => TutorialContext.FindText(fxTab(), "preview laser filter"),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialContext.FindText(fxTab(), "play effects during playback", contains: true));
                        task(d, c, 1);
                        d.Click(() => TutorialContext.FindText(fxTab(), "preview laser filter"));
                    },
                },

                new TutorialStep("Your own FX chip sounds",
                    "FX chips can play a sound of their own instead of the default tick.")
                {
                    Enter = c => c.Bag["sounds"] = c.Chart is { } ch ? chipsWithSounds(ch) : 0,
                    Tasks = new[]
                    {
                        new TutorialTask("Under [ui]FX chip sounds[/ui], pick a sound: click [ui]snare[/ui] (a click also plays it; [ui]preview[/ui] plays the chosen one again).", c => fxTab()?.CurrentSound == "snare")
                        {
                            Target = c => TutorialContext.FindText(fxTab(), "snare"),
                        },
                        new TutorialTask("Click [ui]as default (chips without one)[/ui]: every FX chip that has no sound gets it. ([ui]to selected chips[/ui] gives it to the selection only; [ui]import[/ui] adds your own .wav / .ogg.)",
                            c => c.Chart is { } ch && c.Bag["sounds"] is int n && chipsWithSounds(ch) > n)
                        {
                            Target = c => TutorialContext.FindText(fxTab(), "as default (chips without one)"),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialContext.FindText(fxTab(), "snare"));
                        task(d, c, 1);
                        d.Click(() => TutorialContext.FindText(fxTab(), "as default (chips without one)"));
                    },
                },

                saveStep(),
            };
        },
    };

    private static Drawable? mainFilterAtCursor(Drawable? tab) =>
        tab?.ChildrenOfType<Graphics.UserInterface.FlatButton>()
            .Where(f => TutorialAnchors.IsShown(f) && TutorialContext.FindText(f, "at cursor") != null)
            .OrderByDescending(f => f.ScreenSpaceDrawQuad.Centre.X)
            .FirstOrDefault();

    // ---- 6: test & difficulty -------------------------------------------------------------

    private static TutorialChapter mainTestAndDifficulty() => new TutorialChapter
    {
        Number = 6,
        Title = "Test & Difficulty",
        Summary = "Map a section of the drop on your own, playtest it (F5), autoplay, results, level estimate, skill radar",
        Minutes = MainTutorialPlan.MinutesOf(6),
        Requirement = mainCompose,
        Restore = mainRestoreCompose,
        Steps = c =>
        {
            var w = c.Main.Exercises.FinalPractice;
            SongSetupDialog? setup() => c.Dialog<SongSetupDialog>();

            return new List<TutorialStep>
            {
                new TutorialStep("Your own section",
                    $"Now chart on your own: {w.BarsText}, the start of the drop (the yellow band). Chips, holds, FX, lasers — whatever fits the music. Rules:\n"
                    + "• at least [b]12 objects[/b]\n"
                    + "• at least [b]one laser[/b]\n"
                    + "• everything on the [b]1/4 or 1/8[/b] grid\n\n"
                    + "Zoom out ([k]Alt[/k] + wheel) to see more at once; listen at 50 % if it goes by too fast.")
                {
                    Enter = c => Show(c, w.FromPulse, w.FromPulse + 2 * MainTutorialSong.Measure),
                    Zone = _ => (w.FromPulse, w.ToPulse),
                    ZoneLabel = _ => w.BarsText,
                    Area = c => region(c, w),
                    Progress = c => c.Chart is { } ch ? mainPractice(ch, w, 12, 8, lasers: 1) : null,
                    Done = c => c.Chart is { } ch && mainPractice(ch, w, 12, 8, lasers: 1) == null,
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.Divisor8);
                        d.Press(EditorAction.ToolBtChip);
                        for (int i = 0; i < 11; i++) d.ClickAt(pf(c, w.FromPulse + i * beat / 2, bt(i % 4)));
                        d.Press(EditorAction.ToolLaser);
                        d.Press(EditorAction.LaserSideLeft);
                        d.Drag(pf(c, w.FromPulse + MainTutorialSong.Measure + 2 * beat, laser(0)), pf(c, w.FromPulse + MainTutorialSong.Measure + 3 * beat, laser(0.5)));
                    },
                },

                new TutorialStep("Test it",
                    "The playtest plays what is in the editor, saved or not, starting 2 s before the cursor. Keys:\n"
                    + "• BT: [k]D[/k] [k]F[/k] [k]J[/k] [k]K[/k] · FX: [k]C[/k] [k]M[/k]\n"
                    + "• [c=4de1ff]laser L[/c]: [k]W[/k] / [k]E[/k] · [c=ff5ad1]laser R[/c]: [k]O[/k] / [k]P[/k]\n"
                    + "• [k]F3[/k] / [k]F4[/k] (or [k]-[/k] / [k]+[/k]) — scroll speed\n"
                    + "(Rebind them in the options.)")
                {
                    Requirement = c => c.Playtest != null ? null : mainCompose(c),
                    Tasks = new[]
                    {
                        new TutorialTask("Press {key:Playtest} to play your chart.", c => c.Playtest is { IsLoaded: true } || c.Bag.ContainsKey("tested"))
                        {
                        },
                        new TutorialTask("Press [k]Tab[/k]: [b]autoplay[/b] plays the chart for you — handy to watch how it feels. Tab again gives control back.", c => c.Playtest is { IsLoaded: true } p && p.Judge.Autoplay),
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.Playtest);
                        d.WaitFor(() => c.Playtest is { IsLoaded: true }, "the playtest");
                        d.Key(Key.Tab);
                    },
                },

                new TutorialStep("Results",
                    "The results show score, chain and how early or late your hits were.")
                {
                    Requirement = c => c.Playtest != null || c.Bag.ContainsKey("resultsSeen") && c.Editor != null ? null : "Press {key:Playtest} in the editor to start the test again.",
                    Restore = c => c.Editor?.Playtest(),
                    Tasks = new[]
                    {
                        new TutorialTask("Press [k]Esc[/k] to stop: the results appear.", c =>
                        {
                            if (c.Playtest is { ShowingResults: true }) c.Bag["resultsSeen"] = true;
                            return c.Bag.ContainsKey("resultsSeen");
                        }),
                        new TutorialTask("Press [k]Esc[/k] again (or [k]Enter[/k]): back to the editor, where the test stopped.", c => c.Bag.ContainsKey("resultsSeen") && c.Editor != null),
                    },
                    Demo = (d, c) =>
                    {
                        d.Key(Key.Escape);
                        d.WaitFor(() => c.Bag.ContainsKey("resultsSeen"), "the results");
                        d.Key(Key.Escape);
                    },
                },

                new TutorialStep("How hard is it?",
                    "The editor [b]estimates the level[/b] from your notes (fitted on official charts).")
                {
                    Requirement = TutorialNeed.Editor(),
                    Tasks = new[]
                    {
                        new TutorialTask("Press {key:SongSetup} for Song Setup.", c => setup() != null) { Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "song setup") },
                        new TutorialTask("Open the [ui]Difficulty[/ui] page.", c => FieldNear<Graphics.UserInterface.Dialog.NumericUpDown>(setup(), "Level") != null) { Target = c => TabHeader(setup(), "Difficulty") },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.SongSetup);
                        task(d, c, 1);
                        d.Click(() => TabHeader(setup(), "Difficulty"));
                    },
                },

                new TutorialStep("Use the estimate",
                    "[ui]Estimated level[/ui] shows the guess, the range it is probably in, how sure it is and what drives it.")
                {
                    Requirement = c => setup() != null || c.Bag.ContainsKey("used") ? TutorialNeed.Editor()(c) : "Song Setup was closed. Press {key:SongSetup} again.",
                    Restore = c => c.Editor?.OpenSongSetup(),
                    Tasks = new[]
                    {
                        new TutorialTask("Click [ui]Use estimate[/ui]: it only fills the Level box.", c =>
                        {
                            if (setup() is { } s && s.Edit.Estimate.Value?.EstimatedLevel is double l && Math.Abs(s.Edit.Level.Value - SongSetupViewModel.SnapLevel(l)) < 0.05)
                                c.Bag["used"] = s.Edit.Level.Value;
                            return c.Bag.ContainsKey("used");
                        }) { Target = c => TutorialContext.FindText(setup(), "Use estimate") },
                        new TutorialTask("Click [ui]OK[/ui] to keep it.", c => setup() == null && c.Bag.TryGetValue("used", out var v) && v is double lv && c.Chart is { } ch && Math.Abs(ch.Meta.Level - lv) < 0.05)
                        {
                            Target = c => TutorialContext.FindText(setup(), "OK"),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.WaitFor(() => setup()?.Edit.Estimate.Value?.EstimatedLevel != null, "the estimate");
                        d.Click(() => TutorialContext.FindText(setup(), "Use estimate"));
                        task(d, c, 1);
                        d.Click(() => TutorialContext.FindText(setup(), "OK"));
                    },
                },

                saveStep(),

                new TutorialStep("Back to song select",
                    "Press [k]Esc[/k] to leave the editor. Esc first closes whatever is open (a window, a menu, the reference); with unsaved changes it asks [ui]Save[/ui] / [ui]Discard[/ui] / [ui]Cancel[/ui] before leaving — we just saved, so it leaves at once.")
                {
                    Requirement = c => c.SongSelect != null || c.Editor != null ? null : mainCompose(c),
                    Done = c => c.SongSelect != null,
                    Demo = (d, _) => d.Press(EditorAction.Exit),
                },

                new TutorialStep("The skill radar",
                    "Next to your song's levels, the hexagon is the chart's [b]skill radar[/b] — computed the way the VOX converter does it:\n"
                    + "• [c=4de1ff]NOTES[/c] — note density · [c=ff5050]PEAK[/c] — the hardest burst\n"
                    + "• [c=ff5ad1]TSUMAMI[/c] — knob work · [c=ffd84d]TRICKY[/c] — speed changes, odd rhythms\n"
                    + "• [c=c47cff]HAND-TRIP[/c] / [c=7ee07e]ONE-HAND[/c] — buttons while the knobs move\n"
                    + "Select another chart to compare.")
                {
                    Requirement = TutorialNeed.SongSelect,
                    Restore = TutorialNeed.RestoreSongSelect,
                    Area = c => TutorialContext.DeepRect(c.SongSelect?.Radar),
                },
            };
        },
    };

    // ---- 7: save, export & share ------------------------------------------------------------

    /// <summary>A window opened by mistake (Ctrl+O in the editor = Open Difficulty, not the options): what to do.</summary>
    private static string? wrongWindow(TutorialContext c) =>
        c.Dialogs.Current is ChoiceDialog { Title: "Open Difficulty" }
            ? "That is [b]Open Difficulty[/b] — in the editor [k]Ctrl+O[/k] opens a difficulty (like in other editors); the options are {key:ShowOptions} there. Press [k]Esc[/k] or [ui]Cancel[/ui]."
            : null;

    private static TutorialChapter mainSaveExportShare() => new TutorialChapter
    {
        Number = 7,
        Title = "Save, Export & Share",
        Summary = "kson saving, export to ksh / VOX (what is lost), .svz for collabs, Support the Artist, shortcuts, options search, hitsounds, key bindings",
        Minutes = MainTutorialPlan.MinutesOf(7),
        Requirement = c => wrongWindow(c) ?? TutorialNeed.Editor()(c),
        Restore = TutorialNeed.RestoreEditor(),
        Steps = c =>
        {
            Drawable? optionsButton() => TutorialContext.FindText(c.SongSelect as Drawable, "Options");
            var options = c.Game.OptionsOverlay;
            bool inputShown() => TutorialContext.FindText(options, "Hold modifier") is { } h && options.ScreenSpaceDrawQuad.AABBFloat.Contains(h.ScreenSpaceDrawQuad.Centre);

            return new List<TutorialStep>
            {
                new TutorialStep("Export for K-Shoot Mania",
                    "{key:Save} saves [b].kson[/b] in the song folder — the editor's own format, nothing is lost. A .ksh you opened is never overwritten. Other formats are [b]exports[/b]:\n"
                    + "• [b]ksh[/b] — for K-Shoot Mania: notes, lasers, most camera work and effects; editor extras (laser anchors, VOX backgrounds) become plain points or drop out\n"
                    + "• [b]VOX[/b] — for the game itself, through the ksh2vox converter (needs Python, see Options → Export)")
                {
                    Enter = c => c.Bag["export"] = c.Editor?.LastExportPath,
                    Tasks = new[]
                    {
                        new TutorialTask("Click [ui]File[/ui].", c => MenuItem(c, "File", "Export Song") != null || c.Bag.ContainsKey("exported")) { Target = c => MenuItem(c, "File") },
                        new TutorialTask("Point at [ui]Export Song[/ui].", c => MenuItem(c, "File", "Export Song", "ksh...") != null || c.Bag.ContainsKey("exported")) { Target = c => MenuItem(c, "File", "Export Song") },
                        new TutorialTask("Click [ui]ksh…[/ui]: the song goes into your Exports folder.", c =>
                        {
                            if (c.Editor is { ExportsRunning: 0, LastExportPath: string p } && !Equals(p, c.Bag["export"]) && (File.Exists(p) || Directory.Exists(p))) c.Bag["exported"] = true;
                            return c.Bag.ContainsKey("exported");
                        }) { Target = c => MenuItem(c, "File", "Export Song", "ksh...") },
                    },
                    Demo = (d, c) =>
                    {
                        d.WaitFor(() => MenuItem(c, "File") != null, "the File menu");
                        d.Click(() => MenuItem(c, "File"));
                        task(d, c, 1);
                        d.Click(() => MenuItem(c, "File", "Export Song"));
                        task(d, c, 2);
                        d.Click(() => MenuItem(c, "File", "Export Song", "ksh..."));
                        d.WaitFor(() => c.Editor is { ExportsRunning: 0, LastExportPath: not null }, "the ksh export");
                    },
                },

                new TutorialStep("Share with .svz",
                    "Charting together? A [b].svz[/b] packs the whole song folder — audio, jacket, every difficulty, bookmarks — into one file. "
                    + "Your partner drops it onto song select; if they have the song already, they choose [ui]Replace[/ui] or [ui]Keep both[/ui].")
                {
                    Enter = c => c.Bag["since"] = DateTime.UtcNow.AddSeconds(-1),
                    Tasks = new[]
                    {
                        new TutorialTask("Click [ui]File[/ui].", c => MenuItem(c, "File", "Export Song") != null || svzWritten(c)) { Target = c => MenuItem(c, "File") },
                        new TutorialTask("Point at [ui]Export Song[/ui].", c => MenuItem(c, "File", "Export Song", ".svz") != null || svzWritten(c)) { Target = c => MenuItem(c, "File", "Export Song") },
                        new TutorialTask("Click [ui].svz[/ui].", svzWritten) { Target = c => MenuItem(c, "File", "Export Song", ".svz") },
                    },
                    Demo = (d, c) =>
                    {
                        d.WaitFor(() => MenuItem(c, "File") != null, "the File menu");
                        d.Click(() => MenuItem(c, "File"));
                        task(d, c, 1);
                        d.Click(() => MenuItem(c, "File", "Export Song"));
                        task(d, c, 2);
                        d.Click(() => MenuItem(c, "File", "Export Song", ".svz"));
                        d.WaitFor(() => c.Manager.Step?.Title != "Share with .svz", "the .svz export");
                    },
                },

                new TutorialStep("Support the Artist",
                    "[ui]Song Setup → Sources[/ui] lists where a song comes from (SoundCloud, YouTube, Bandcamp …), saved with every difficulty. "
                    + $"A song with sources shows [ui]Support the Artist[/ui] — here at the end of the menu bar and in song select. It opens the artist's pages: for this song {c.Main.Artist}'s SoundCloud.\n\n"
                    + "Charting someone's song? Add its links there, so players can find the music.")
                {
                    Target = c => c.Editor?.SupportButton is { } b && TutorialAnchors.IsShown(b) ? b : c.Editor?.MenuBar,
                },

                new TutorialStep("All shortcuts",
                    "Forgot a key? The shortcut list shows every shortcut with [b]your[/b] current bindings (also Help → Keyboard Shortcuts).")
                {
                    Requirement = TutorialNeed.Editor(),
                    Tasks = new[]
                    {
                        new TutorialTask("Press {key:ShowShortcuts}.", c =>
                        {
                            if (c.Dialog<ShortcutsDialog>() != null) c.Bag["shortcutsSeen"] = true;
                            return c.Bag.ContainsKey("shortcutsSeen");
                        }),
                        new TutorialTask("Close it with [k]Esc[/k].", c => c.Bag.ContainsKey("shortcutsSeen") && !c.Dialogs.HasOpenDialog) { Target = c => c.Dialog<ShortcutsDialog>() },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ShowShortcuts);
                        task(d, c, 1);
                        d.DialogKey(Key.Escape);
                    },
                },

                new TutorialStep("Back to song select",
                    "The options live one level up, in song select — there [k]Ctrl+O[/k] opens them, like in osu!. (In the editor [k]Ctrl+O[/k] means Open Difficulty and the options are {key:ShowOptions}.)\n\n"
                    + "Press [k]Esc[/k] to go back to song select.")
                {
                    Requirement = c => wrongWindow(c) ?? (c.SongSelect != null || c.Editor != null ? null : TutorialNeed.Editor()(c)),
                    Done = c => c.SongSelect != null,
                    Demo = (d, _) => d.Press(EditorAction.Exit),
                },

                new TutorialStep("Options",
                    "Every setting of the editor is in the options; the search box finds any of them.")
                {
                    Requirement = c => wrongWindow(c) ?? (c.Game.OptionsOverlay.IsOpen.Value ? null : TutorialNeed.SongSelect(c)),
                    Restore = TutorialNeed.RestoreSongSelect,
                    Tasks = new[]
                    {
                        new TutorialTask("Press [k]Ctrl+O[/k] or click [ui]Options[/ui] at the bottom.", c => options.IsOpen.Value) { Target = c => optionsButton() },
                        new TutorialTask("Type [b]hitsound[/b] into the search box at the top: only matching rows stay.\n"
                                         + "What you hear while editing comes from the [ui]hitsound skin[/ui]: [ui]Auto[/ui] uses your osu! skin, or pick the built-in sounds; volumes and chord layers sit next to it.",
                            c =>
                            {
                                if (options.IsOpen.Value && options.Search.Value.Contains("hitsound", StringComparison.OrdinalIgnoreCase)) c.Bag["searched"] = true;
                                return c.Bag.ContainsKey("searched");
                            }) { Target = c => options.IsOpen.Value ? options.SearchBox : null },
                    },
                    Demo = (d, c) =>
                    {
                        d.Key(Key.O, Key.LControl);
                        task(d, c, 1);
                        d.WaitFor(() => options.IsSettled, "the options to slide in");
                        d.Click(() => options.SearchBox);
                        d.Do(() => options.Search.Value = "hitsound"); // typing stand-in
                    },
                },

                new TutorialStep("Key bindings",
                    "Every key can be changed. [ui]Input[/ui] lists every editor action and playtest key with two slots each, and the [ui]Hold modifier[/ui]:\n"
                    + "• click a slot and press the new key\n"
                    + "• if another action uses it: [ui]Swap[/ui] / [ui]Clear other[/ui] / [ui]Cancel[/ui]\n"
                    + "• the reset arrow at the end of a row resets it")
                {
                    Requirement = c => wrongWindow(c) ?? (options.IsOpen.Value || c.Bag.ContainsKey("inputSeen") ? null : "The options were closed. Press [k]Ctrl+O[/k] in song select to open them again."),
                    Restore = c => options.Open(),
                    Tasks = new[]
                    {
                        new TutorialTask("Clear the search box.", c => options.Search.Value.Length == 0 || c.Bag.ContainsKey("inputSeen")) { Target = c => options.IsOpen.Value ? options.SearchBox : null },
                        new TutorialTask("Click [ui]Input[/ui] on the left.", c =>
                        {
                            if (options.IsOpen.Value && options.Search.Value.Length == 0 && inputShown()) c.Bag["inputSeen"] = true;
                            return c.Bag.ContainsKey("inputSeen");
                        }) { Target = c => options.IsOpen.Value ? options.SidebarButtonFor("Input") : null },
                        new TutorialTask("Close the options with [k]Esc[/k] when you are done looking.", c => c.Bag.ContainsKey("inputSeen") && !options.IsOpen.Value),
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => options.SearchBox);
                        d.Do(() => options.Search.Value = string.Empty); // clearing the box (typing stand-in)
                        task(d, c, 1);
                        d.Click(() => options.SidebarButtonFor("Input"));
                        task(d, c, 2);
                        d.Key(Key.Escape);
                        d.KeyIf(() => options.IsOpen.Value, Key.Escape);
                    },
                },

                new TutorialStep("That's it!",
                    "You have seen the whole editor — time to chart your own songs.\n\n"
                    + $"Thanks to [b]{c.Main.Artist}[/b] for \"{c.Main.Title}\". [ui]Help → Tutorial…[/ui] in the editor (or [ui]Tutorial[/ui] down here) brings any chapter back.")
                {
                    Requirement = TutorialNeed.SongSelect,
                    Restore = TutorialNeed.RestoreSongSelect,
                    Anchor = "songselect.tutorial",
                },
            };

            bool svzWritten(TutorialContext c) => c.Bag["since"] is DateTime since && Directory.Exists(c.Game.Library.ExportRoot)
                                                                                 && Directory.EnumerateFiles(c.Game.Library.ExportRoot, "*.svz", SearchOption.AllDirectories).Any(f => File.GetLastWriteTimeUtc(f) >= since);
        },
    };
}
