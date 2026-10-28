using System.Diagnostics;
using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Library;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Timing;
using F6 = SDVXEditor.Game.Screens.Edit.Dialogs.TimingControlPointsDialog;

namespace SDVXEditor.Game.Tutorial;

public static partial class TutorialChapters
{
    /// <summary>The clickable tab header labelled <paramref name="text"/> inside <paramref name="root"/> (a dialog's page tabs).</summary>
    internal static Drawable? TabHeader(Drawable? root, string text) =>
        root?.ChildrenOfType<ClickableContainer>().FirstOrDefault(cc => TutorialAnchors.IsShown(cc) && cc.ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteText>().Any(t => t.Text.ToString() == text)
                                                                       && cc.ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteText>().Count() == 1);

    /// <summary>"needs 235 — 5 too low" style feedback for an exact number.</summary>
    private static string exactly(double now, double want, string unit, int decimals = 0)
    {
        string f(double v) => v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        double diff = Math.Round(now - want, decimals);
        if (diff == 0) return $"{f(now)}{unit} ✓";
        return $"{f(now)}{unit} — needs {f(want)}{unit} ({f(Math.Abs(diff))} too {(diff < 0 ? "low" : "high")})";
    }

    /// <summary>Main tutorial, chapter 0: from the audio file to a timed song — drop, Song Setup, BPM 235, offset, F6 (a scroll speed jump at the drop).</summary>
    private static TutorialChapter mainSongSetupAndTiming() => new TutorialChapter
    {
        Number = 0,
        Title = "Song Setup & Timing",
        Summary = "Create a song from audio, fill in Song Setup, set BPM and offset, a scroll speed jump at the drop (F6)",
        Minutes = MainTutorialPlan.MinutesOf(0),
        StartsInEditor = false,
        Steps = c =>
        {
            var def = c.Main;
            double dropMs = def.MsOfMeasure(def.Drop);
            long dropMsRounded = (long)Math.Round(dropMs);
            F6? f6() => c.Dialog<F6>();
            SongSetupDialog? setup() => c.Dialog<SongSetupDialog>();
            NumericUpDown? f6Offset() => f6()?.ChildrenOfType<NumericUpDown>().Where(TutorialAnchors.IsShown).Skip(1).FirstOrDefault(); // Speed/BPM, then Offset
            Drawable? jumpBox() => f6() is { } w ? TutorialContext.FindText(w, "Jump to") : null;
            NumericUpDown? jumpValue() => f6() is { } w ? FieldNear<NumericUpDown>(w, "Jump to") : null;

            // a speed point at the drop (±1 ms) — and with the jump to ×1.25 on it
            GraphPoint? atDrop(TimingDraftLike d) => d.Points.FirstOrDefault(g => Math.Abs(d.Ms(g.Y) - dropMs) <= 1);
            bool jumpsRight(GraphPoint g) => g.Vf is double vf && Math.Abs(vf - def.DropSpeed) < 0.001 && Math.Abs(g.V - 1) < 0.001;
            TimingDraftLike? draft() => f6() is { } w ? new TimingDraftLike(w.Draft.Beat.ScrollSpeed, y => w.Draft.BuildMap().PulseToMs(y)) : null;
            bool hasDropSpeed(TutorialContext c) => c.Session is { } s && atDrop(new TimingDraftLike(s.Chart.Beat.ScrollSpeed, y => s.PulseToMs(y))) is { } g && jumpsRight(g);

            string speedState(TutorialContext c)
            {
                if (draft() is not { } d) return "";
                if (atDrop(d) is not { } g)
                    return d.Points.Count == 0 ? "No speed point yet" : $"Selected point at {f6()!.OffsetField.Value:0} ms — needs {dropMsRounded} ms";
                if (g.Vf == null) return $"At {dropMsRounded} ms ✓ · \"Jump to\" not ticked yet";
                return jumpsRight(g) ? $"At {dropMsRounded} ms ✓ · ×1.00 → ×{def.DropSpeed:0.00} ✓" : $"At {dropMsRounded} ms ✓ · jumps to ×{g.Vf:0.00} — needs ×{def.DropSpeed:0.00}" + (Math.Abs(g.V - 1) < 0.001 ? "" : $" (and Speed ×{g.V:0.00} should stay 1.00)");
            }

            return new List<TutorialStep>
            {
                new TutorialStep("Welcome!",
                    $"This tutorial charts a real song with you, step by step: [b]\"{def.Title}\"[/b] by [b]{def.Artist}[/b], used with the artist's permission.\n\n"
                    + "• Read the card, then do what it says.\n"
                    + "• The [c=ffd84d]yellow frame and arrow[/c] show where.\n"
                    + "• Steps with numbered tasks tick them off one by one; the frame moves to the next task by itself.\n"
                    + "• Stuck? [ui]Skip step[/ui] moves on, [ui]Exit tutorial[/ui] stops. [k]Esc[/k] never traps you.")
                {
                    Requirement = TutorialNeed.SongSelect,
                    Restore = TutorialNeed.RestoreSongSelect,
                },

                new TutorialStep("Create a song from audio",
                    "Every chart starts with an audio file. [b]Drag it from Explorer and drop it anywhere on this window[/b]: the editor makes a new song with an empty chart.\n\n"
                    + "• [ui]Show the file[/ui] opens the folder with the tutorial's audio: drop [b]\"" + Path.GetFileName(c.Manager.Workspace.StagedAudio) + "\"[/b] here.\n"
                    + "• [ui]Import it for me[/ui] does exactly what a drop does.")
                {
                    Requirement = TutorialNeed.SongSelect,
                    Restore = TutorialNeed.RestoreSongSelect,
                    Done = c => Directory.Exists(c.SongFolder) && Directory.EnumerateFiles(c.SongFolder, "*.kson").Any()
                                && c.Game.Library.Songs.Any(s => string.Equals(s.FolderPath.TrimEnd('\\'), Path.GetFullPath(c.SongFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)),
                    Buttons = new (string, Action<TutorialContext>)[]
                    {
                        ("Show the file", c =>
                        {
                            if (c.Game.Options.SmokeTest || !OperatingSystem.IsWindows()) return;
                            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{c.Manager.Workspace.StagedAudio}\"") { UseShellExecute = true });
                        }),
                        ("Import it for me", c => _ = c.SongSelect?.ImportFiles(new[] { c.Manager.Workspace.StagedAudio }, ExistingSongPolicy.Replace)),
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => c.Manager.Overlay?.Panel.Buttons.GetValueOrDefault("Import it for me"));
                        d.WaitFor(() => c.Manager.Step?.Title != "Create a song from audio", "the import");
                    },
                },

                new TutorialStep("Open it in the editor",
                    "Your new song is selected in the list on the right ([b]\"" + c.Manager.Workspace.FolderName + "\"[/b] — the title comes from the file name).\n\n"
                    + "Click [ui]EDIT[/ui], or press [k]Enter[/k].")
                {
                    Target = c => TutorialContext.FindText(c.SongSelect as Drawable, "EDIT"),
                    Requirement = c => c.Editor != null ? null : c.AnyEditor != null ? "That is another song. Press [k]Esc[/k] and pick \"" + c.Manager.Workspace.FolderName + "\"." : TutorialNeed.SongSelect(c),
                    Restore = c => c.Manager.OpenTutorialSong(),
                    Done = c => c.Editor != null,
                    Demo = (d, c) =>
                    {
                        d.WaitFor(() => c.SongSelect?.Wheel.Selected.Value?.FolderPath is string f && c.IsTutorialSessionFolder(f), "the new song to be selected");
                        d.Click(() => TutorialContext.FindText(c.SongSelect as Drawable, "EDIT"));
                        d.WaitFor(() => c.Editor?.IsLoaded == true, "the editor");
                    },
                },

                new TutorialStep("The editor at a glance",
                    "These tabs switch what the middle of the editor shows:\n"
                    + "• [ui]compose[/ui] {key:TabCompose} — place notes and lasers\n"
                    + "• [ui]camera[/ui] {key:TabCamera} — how the lane zooms, tilts and spins\n"
                    + "• [ui]timing[/ui] {key:TabTiming} — BPM, offset, speed changes\n"
                    + "• [ui]effects[/ui] {key:TabEffects} — FX sounds and laser filters\n"
                    + "• [ui]song setup[/ui] {key:SongSetup} — title, artist, difficulty, jacket\n\n"
                    + "Above them: the menu bar. Below: the timeline with the waveform. At the very bottom: the time, a mini map of the song, play controls and the playback speed.")
                {
                    Target = c => TutorialContext.Find<EditorTabStrip>(c.Editor),
                    Requirement = TutorialNeed.Editor(),
                },

                new TutorialStep("Song Setup",
                    "First the song's details. [ui]Song Setup[/ui] is a window with pages (General, Difficulty, Audio, Jacket …).")
                {
                    Requirement = TutorialNeed.Editor(),
                    Tasks = new[]
                    {
                        new TutorialTask("Press {key:SongSetup} or click [ui]song setup[/ui].", c => setup() != null)
                        {
                            Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "song setup"),
                        },
                        new TutorialTask($"On the [ui]General[/ui] page, click the [ui]Charter[/ui] box and type your name. (The title came from the file name — the real one is \"{def.Title}\" by {def.Artist}; type it in too if you like.)",
                            c => setup()?.Edit.Charter.Value.Trim().Length > 0)
                        {
                            Target = c => FieldNear<DialogTextBox>(setup(), "Charter"),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.SongSetup);
                        d.WaitFor(() => setup() != null, "Song Setup");
                        task(d, c, 1);
                        d.Click(() => FieldNear<DialogTextBox>(setup(), "Charter"));
                        d.Do(() => setup()!.Edit.Charter.Value = "Tutorial Tester"); // typing: the OS text stream cannot be injected
                    },
                },

                new TutorialStep("Difficulty and level",
                    "[ui]Slot[/ui] says which of the five difficulties this chart is: [c=a79ef2]NOV[/c], [c=ffd84d]ADV[/c], [c=ff5050]EXH[/c], [c=c47cff]INF[/c] or [c=ffffff]MXM[/c]. "
                    + "[ui]Level[/ui] is the number players see (1–20, tenths from 17 up).")
                {
                    Requirement = TutorialNeed.Dialog<SongSetupDialog>("Song Setup", "Press F4"),
                    Restore = c => c.Editor?.OpenSongSetup(),
                    Progress = c => setup() is { } s && FieldNear<NumericUpDown>(s, "Level") != null ? "Level: " + exactly(s.Edit.Level.Value, 5, "", 1) : null,
                    Tasks = new[]
                    {
                        new TutorialTask("Open the [ui]Difficulty[/ui] page (its tab at the top of the window).", c => FieldNear<NumericUpDown>(setup(), "Level") != null)
                        {
                            Target = c => TabHeader(setup(), "Difficulty"),
                        },
                        new TutorialTask("Set the [ui]Level[/ui] to [b]5[/b]: type it, or use the little arrows. Later the editor estimates it for you.",
                            c => setup() is { } s && Math.Abs(s.Edit.Level.Value - 5) < 0.001)
                        {
                            Target = c => FieldNear<NumericUpDown>(setup(), "Level"),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => TabHeader(setup(), "Difficulty"));
                        task(d, c, 1);
                        d.Click(() => FieldNear<NumericUpDown>(setup(), "Level"));
                        d.Do(() => FieldNear<NumericUpDown>(setup(), "Level")!.SetText("5"));
                    },
                },

                new TutorialStep("Keep the changes",
                    "Two more pages matter for real songs:\n"
                    + "• [ui]Jacket[/ui] — the cover picture; [ui]Browse…[/ui] copies an image into the song folder.\n"
                    + "• [ui]Audio[/ui] — [ui]Preview[/ui] is the part song select plays; [ui]Use Current Time[/ui] takes the editor's cursor.\n\n"
                    + "Click [ui]OK[/ui]. Everything you changed in this window becomes [b]one[/b] step you can undo later.")
                {
                    Target = c => TutorialContext.FindText(setup(), "OK"),
                    Requirement = c => setup() != null || c.Chart?.Meta.ChartAuthor.Trim().Length > 0 ? TutorialNeed.Editor()(c)
                        : "Song Setup was closed without OK. Press {key:SongSetup} to open it again and fill in the Charter.",
                    Restore = c => c.Editor?.OpenSongSetup(),
                    Done = c => setup() == null && c.Chart?.Meta.ChartAuthor.Trim().Length > 0 && Math.Abs(c.Chart.Meta.Level - 5) < 0.001,
                    Demo = (d, c) => d.Click(() => TutorialContext.FindText(setup(), "OK")),
                },

                new TutorialStep("The Timing tab",
                    "Now the most important part. [b]If the timing is off, every note you place will be off too.[/b]\n\n"
                    + "Press {key:TabTiming} or click [ui]timing[/ui].")
                {
                    Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "timing"),
                    Requirement = TutorialNeed.Editor(),
                    Done = c => c.Editor?.CurrentTab == EditorTab.Timing,
                    Demo = (d, _) => d.Press(EditorAction.TabTiming),
                },

                new TutorialStep("Set the BPM",
                    $"[ui]BPM[/ui] = beats per minute, the song's tempo. This one is fast: [b]{def.Bpm:0.##} BPM[/b], the same all the way through. It has to be [b]exactly[/b] {def.Bpm:0.##}.\n\n"
                    + "• Drag the number sideways, or\n"
                    + "• turn the mouse wheel over it ([k]Shift[/k] + wheel: ten at a time), or\n"
                    + "• click its arrows.\n\n"
                    + "For a song you don't know, tap [ui]Tap here![/ui] (top right) along with the beat: it measures the BPM for you.")
                {
                    Anchor = "timing.bpm",
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Progress = c => c.Chart is { } ch && ch.Beat.Bpm.Count > 0 ? "BPM now: " + exactly(ch.Beat.Bpm[0].Bpm, def.Bpm, "", ch.Beat.Bpm[0].Bpm % 1 == 0 ? 0 : 2) : null,
                    Done = c => c.Chart is { } ch && ch.Beat.Bpm.Count > 0 && Math.Abs(ch.Beat.Bpm[0].Bpm - def.Bpm) < 0.0005,
                    Demo = (d, c) => mainWheelStepper(d, () => TutorialAnchors.Resolve("timing.bpm"), c.Chart?.Beat.Bpm.FirstOrDefault()?.Bpm ?? 120, def.Bpm),
                },

                new TutorialStep("Set the offset",
                    $"The [ui]Offset[/ui] says where the first beat is in the audio file, in milliseconds. This song starts right away: its first beat is at [b]{def.OffsetMs} ms[/b].\n\n"
                    + $"Drag the Offset number to the right until it shows {def.OffsetMs} — the wheel and the arrows change it 1 ms at a time, [k]Shift[/k] + wheel 10 ms. "
                    + "It has to be within 1 ms: a few ms off already makes hits feel early or late.")
                {
                    Anchor = "timing.offset",
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Progress = c => c.Chart is { } ch ? "Offset now: " + (Math.Abs(ch.Audio.Bgm.OffsetMs - def.OffsetMs) <= 1 ? $"{ch.Audio.Bgm.OffsetMs} ms ✓" : exactly(ch.Audio.Bgm.OffsetMs, def.OffsetMs, " ms")) : null,
                    Done = c => c.Chart is { } ch && Math.Abs(ch.Audio.Bgm.OffsetMs - def.OffsetMs) <= 1,
                    Demo = (d, c) => mainWheelStepper(d, () => TutorialAnchors.Resolve("timing.offset"), c.Chart?.Audio.Bgm.OffsetMs ?? 0, def.OffsetMs),
                },

                new TutorialStep("Check it with the metronome",
                    "Listen whether the grid fits the music. The lamps light up on every beat ([c=7ee07e]green[/c] = the first beat of a bar).")
                {
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Tasks = new[]
                    {
                        new TutorialTask("Tick [ui]Metronome clicks[/ui].", c => c.Editor is { } e && e.Timing.MetronomeClicks.Value) { Anchor = "timing.metronome" },
                        new TutorialTask("Press {key:PlayPause} to play. The clicks should land on the beat of the music.", c =>
                        {
                            if (c.Editor is { } e && e.Playback.IsPlaying.Value && e.Timing.MetronomeClicks.Value) c.Bag["played"] = true;
                            return c.Bag.ContainsKey("played");
                        }) { Anchor = "timing.metronome" },
                        new TutorialTask("Press {key:PlayPause} again to stop.", c => c.Bag.ContainsKey("played") && c.Editor is { } e && !e.Playback.IsPlaying.Value)
                        {
                            Anchor = "timing.metronome",
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialAnchors.Resolve("timing.metronome"));
                        d.Press(EditorAction.PlayPause);
                        d.WaitFor(() => c.Bag.ContainsKey("played"), "playback with the metronome");
                        d.Press(EditorAction.PlayPause);
                    },
                },

                new TutorialStep("Timing points: open F6",
                    $"At [b]{clock(dropMs)}[/b] the drop hits — let the notes speed up there. Speed changes (and tempo changes in other songs) live in the [ui]Timing and Control Points[/ui] window.\n\n"
                    + "Press {key:TimingSetup} or click [ui]Timing Setup Panel[/ui].")
                {
                    Target = c => TutorialContext.FindText(c.Editor?.ChildrenOfType<TimingTabContent>().FirstOrDefault(), "Timing Setup Panel"),
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Done = c => f6() != null,
                    Demo = (d, _) => d.Press(EditorAction.TimingSetup),
                },

                new TutorialStep("Add a speed point",
                    "The list on the right holds every timing point:\n"
                    + "• [c=ff5050]BPM[/c] — tempo (a song that changes tempo gets one per change)\n"
                    + "• [c=7ee07e]Speed[/c] — how fast the notes fly, ×1.00 = normal\n"
                    + "• [c=4f8dff]Stop[/c] — the notes freeze for a moment\n"
                    + "• Meter — a time signature change (3/4, 7/8 …)")
                {
                    Requirement = TutorialNeed.Dialog<F6>("Timing and Control Points", "Press F6"),
                    Restore = c => c.Editor?.OpenTimingSetup(),
                    Tasks = new[]
                    {
                        new TutorialTask("The box next to the green [c=7ee07e]+[/c] says what [c=7ee07e]+[/c] adds: make sure it says [ui]Speed[/ui].", c => f6()?.AddKind.Value == F6.NewPointKind.ScrollSpeed)
                        {
                            Target = c => TutorialContext.Find<ComboBox<F6.NewPointKind>>(f6()),
                        },
                        new TutorialTask("Click [c=7ee07e]+[/c]: a new speed point appears at the cursor and is selected.", c => f6() is { } w && w.Draft.Beat.ScrollSpeed.Count >= 1)
                        {
                            Target = c => TutorialContext.Find<DialogIconButton>(f6()),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        // "Speed" is the default: the first task is ticked before anyone touches the box
                        task(d, c, 1);
                        d.Click(() => TutorialContext.Find<DialogIconButton>(f6()));
                    },
                },

                new TutorialStep("Put it at the drop",
                    "The fields on the left edit the selected point.")
                {
                    Requirement = TutorialNeed.Dialog<F6>("Timing and Control Points", "Press F6"),
                    Restore = c => c.Editor?.OpenTimingSetup(),
                    Progress = speedState,
                    Tasks = new[]
                    {
                        new TutorialTask($"Type its time into [ui]Offset[/ui]: [b]{dropMsRounded}[/b] (ms — the start of bar {def.Drop + 1}).", c => draft() is { } d && atDrop(d) != null)
                        {
                            Target = c => f6Offset(),
                        },
                        new TutorialTask("Tick [ui]Jump to[/ui]: the speed changes [b]instantly[/b] on the point (without it, it would glide there from the point before).", c => f6()?.Slam.Value == true)
                        {
                            Target = c => jumpBox(),
                        },
                        new TutorialTask($"Set the box beside it to [b]{def.DropSpeed:0.00}[/b]: the notes keep ×1.00 up to the drop and jump to ×{def.DropSpeed:0.00} on it.", c => draft() is { } d && atDrop(d) is { } g && jumpsRight(g))
                        {
                            Target = c => jumpValue(),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Click(() => f6Offset());
                        d.Do(() => f6Offset()!.SetText(dropMsRounded.ToString(CultureInfo.CurrentCulture)));
                        task(d, c, 1);
                        d.Click(() => jumpBox());
                        task(d, c, 2);
                        d.Click(() => jumpValue());
                        d.Do(() => jumpValue()!.SetText(def.DropSpeed.ToString("0.00", CultureInfo.CurrentCulture)));
                    },
                },

                new TutorialStep("When timing changes later",
                    "This box decides what happens to notes you already placed when you change BPM or offset later:\n"
                    + "• [ui]Move notes with the grid[/ui] — they stay on their beat (usually what you want).\n"
                    + "• [ui]Scale notes to new timing[/ui] — stretch with a BPM change.\n"
                    + "• [ui]Snap notes to new timing[/ui] — round onto the new grid (the divisor on the right).\n"
                    + "• [ui]Adjust bookmarks and preview[/ui] — move those along.\n\n"
                    + "Coming from osu!? This window works like osu!'s timing setup.")
                {
                    Target = c => ancestor<GroupBox>(TutorialContext.FindText(f6(), "When applying timing changes", contains: true)),
                    Requirement = TutorialNeed.Dialog<F6>("Timing and Control Points", "Press F6"),
                    Restore = c => c.Editor?.OpenTimingSetup(),
                },

                new TutorialStep("The Property Transformer",
                    "One more tool for later: [ui]Property Transformer[/ui] changes many scroll-speed points at once over a stretch of the song:\n"
                    + "• [ui]From[/ui] / [ui]To[/ui] — the stretch\n"
                    + "• [ui]Ramp[/ui] — glide from one speed to another (a slow-down before a drop)\n"
                    + "• [ui]Scale[/ui] / [ui]Shift[/ui] — multiply or add to every point\n\n"
                    + "Not needed now — just know where it is.")
                {
                    Target = c => TutorialContext.FindText(f6(), "Property Transformer"),
                    Requirement = TutorialNeed.Dialog<F6>("Timing and Control Points", "Press F6"),
                    Restore = c => c.Editor?.OpenTimingSetup(),
                },

                new TutorialStep("Apply with OK",
                    "Click [ui]OK[/ui]. Every change made in this window becomes one undo step. ([ui]Cancel[/ui] would throw them all away.)")
                {
                    Target = c => TutorialContext.FindText(f6(), "OK"),
                    Requirement = c => f6() != null || hasDropSpeed(c) ? TutorialNeed.Editor()(c)
                        : "The window was closed without the speed point. Press {key:TimingSetup} to open it again.",
                    Restore = c => c.Editor?.OpenTimingSetup(),
                    Done = c => f6() == null && hasDropSpeed(c),
                    Demo = (d, c) => d.Click(() => TutorialContext.FindText(f6(), "OK")),
                },

                saveStep(),
            };
        },
    };

    /// <summary>Scroll-speed points with their times (the F6 draft or the chart).</summary>
    private sealed record TimingDraftLike(IReadOnlyList<GraphPoint> Points, Func<long, double> Ms);

    /// <summary>
    /// Demo: turns the mouse wheel over a stepper of step 1 from <paramref name="from"/> to <paramref name="target"/> —
    /// Shift + wheel for the tens, the plain wheel for the rest (235 BPM / 682 ms are too far for one drag on screen).
    /// </summary>
    private static void mainWheelStepper(ITutorialDemo d, Func<Drawable?> stepper, double from, double target)
    {
        int tens = (int)Math.Truncate((target - from) / 10);
        int ones = (int)Math.Round(target - from - tens * 10);
        if (tens != 0) d.Wheel(() => stepper()!.ScreenSpaceDrawQuad.Centre, tens, osuTK.Input.Key.LShift);
        if (ones != 0) d.Wheel(() => stepper()!.ScreenSpaceDrawQuad.Centre, ones);
    }
}
