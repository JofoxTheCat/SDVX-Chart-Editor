using System.Diagnostics;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
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
    /// <summary>Chapter 0: from an audio file to a timed song — song select, drop, Song Setup, Timing tab, F6.</summary>
    private static TutorialChapter songSetupAndTiming() => new TutorialChapter
    {
        Number = 0,
        Title = "Song Setup & Timing",
        Summary = "Create a song from audio, fill in Song Setup, set BPM and offset, add a tempo change (F6)",
        Minutes = TutorialPlan.MinutesOf(0),
        StartsInEditor = false,
        Steps = c =>
        {
            var def = c.Song;
            double speedMs = def.MsAt(at(def.SpeedUp.Measure));
            F6? f6() => c.Dialog<F6>();
            SongSetupDialog? setup() => c.Dialog<SongSetupDialog>();

            return new List<TutorialStep>
            {
                new TutorialStep("Welcome!",
                    "This tutorial maps one short song with you, step by step. Read the card, then do what it says: the yellow frame and arrow show where.\n\n"
                    + "A step that wants you to do something ends by itself once you did it. Stuck? \"Skip step\" moves on, \"Exit tutorial\" stops (Esc never traps you).")
                {
                    Requirement = TutorialNeed.SongSelect,
                    Restore = TutorialNeed.RestoreSongSelect,
                },

                new TutorialStep("Create a song from audio",
                    "Every chart starts with an audio file. Drag one from Explorer and drop it anywhere on this window: the editor makes a new song with an empty chart.\n\n"
                    + "The tutorial song is waiting in a folder for you — \"Show the file\" opens it. Drop \"" + Path.GetFileName(c.Manager.Workspace.StagedAudio) + "\" here. "
                    + "(\"Import it for me\" does exactly what a drop does.)")
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
                    "Your new song is selected in the list on the right (\"" + c.Manager.Workspace.FolderName + "\" — the title comes from the file name). Click EDIT, or press Enter.")
                {
                    Target = c => TutorialContext.FindText(c.SongSelect as Drawable, "EDIT"),
                    Requirement = c => c.Editor != null ? null : c.AnyEditor != null ? "That is another song. Press Esc and pick \"" + c.Manager.Workspace.FolderName + "\"." : TutorialNeed.SongSelect(c),
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
                    "Up top: the menu bar, then the timeline with the song's waveform. These tabs switch what the middle shows:\n\n"
                    + "compose {key:TabCompose} — place notes · camera {key:TabCamera} — lane movement · timing {key:TabTiming} — BPM and offset · effects {key:TabEffects} — FX and sounds · song setup {key:SongSetup} — the song's details.\n\n"
                    + "At the bottom: the time, a mini map of the whole song, play controls and the playback speed.")
                {
                    Target = c => TutorialContext.Find<EditorTabStrip>(c.Editor),
                    Requirement = TutorialNeed.Editor(),
                },

                new TutorialStep("Song Setup",
                    "First the song's details. Press {key:SongSetup} or click \"song setup\".\n\n"
                    + "Its General page holds artist, title and who charted it: click the Charter box and type your name. "
                    + "(The title came from the file name — change it too if you like.)")
                {
                    Target = c => setup() is { } s ? FieldNear<DialogTextBox>(s, "Charter") : TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "song setup"),
                    Requirement = TutorialNeed.Editor(),
                    Done = c => setup()?.Edit.Charter.Value.Trim().Length > 0,
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.SongSetup);
                        d.WaitFor(() => setup() != null, "Song Setup");
                        d.Click(() => FieldNear<DialogTextBox>(setup(), "Charter"));
                        d.Do(() => setup()!.Edit.Charter.Value = "Tutorial Tester"); // typing: the OS text stream cannot be injected
                    },
                },

                new TutorialStep("Difficulty and level",
                    "Open the Difficulty page. Slot says which of the five difficulties this chart is: NOV, ADV, EXH, INF or MXM. Level is the number players see.\n\n"
                    + "Set the Level to 5 for now (type it, or use the little arrows). Later the editor can estimate it for you.")
                {
                    Target = c => setup() is { } s && FieldNear<NumericUpDown>(s, "Level") is { } f ? f : TutorialContext.FindText(setup(), "Difficulty"),
                    Requirement = TutorialNeed.Dialog<SongSetupDialog>("Song Setup", "Press F4"),
                    Restore = c => c.Editor?.OpenSongSetup(),
                    Progress = c => setup() is { } s ? "Level: " + exactly(s.Edit.Level.Value, 5, "", 1) : null,
                    Done = c => setup() is { } s && Math.Abs(s.Edit.Level.Value - 5) < 0.001,
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialContext.FindText(setup(), "Difficulty"));
                        d.WaitFor(() => FieldNear<NumericUpDown>(setup(), "Level") != null, "the Difficulty page");
                        d.Click(() => FieldNear<NumericUpDown>(setup(), "Level"));
                        d.Do(() => FieldNear<NumericUpDown>(setup(), "Level")!.SetText("5"));
                    },
                },

                new TutorialStep("Keep the changes",
                    "Two more pages for real songs: Jacket — the cover picture (\"Browse…\" copies an image into the song folder) — and Audio, "
                    + "whose Preview is the part song select plays (\"Use Current Time\" takes the editor's cursor). The tutorial song needs neither.\n\n"
                    + "Click OK. Everything you changed in this window becomes one step you can undo later.")
                {
                    Target = c => TutorialContext.FindText(setup(), "OK"),
                    Requirement = c => setup() != null || c.Chart?.Meta.ChartAuthor.Trim().Length > 0 ? TutorialNeed.Editor()(c)
                        : "Song Setup was closed without OK. Press {key:SongSetup} to open it again and fill in the Charter.",
                    Restore = c => c.Editor?.OpenSongSetup(),
                    Done = c => setup() == null && c.Chart?.Meta.ChartAuthor.Trim().Length > 0,
                    Demo = (d, c) => d.Click(() => TutorialContext.FindText(setup(), "OK")),
                },

                new TutorialStep("The Timing tab",
                    "Now the most important part. If the timing is off, every note you place will be off too. Press {key:TabTiming} or click \"timing\".")
                {
                    Target = c => TutorialContext.FindText(TutorialContext.Find<EditorTabStrip>(c.Editor), "timing"),
                    Requirement = TutorialNeed.Editor(),
                    Done = c => c.Editor?.CurrentTab == EditorTab.Timing,
                    Demo = (d, _) => d.Press(EditorAction.TabTiming),
                },

                new TutorialStep("Set the BPM",
                    $"BPM = beats per minute, the song's tempo. This one starts at {def.Bpm:0.##} BPM.\n\n"
                    + "Drag the number sideways, scroll the mouse wheel over it (Shift: ten at a time), or click its arrows until it shows "
                    + $"{def.Bpm:0.##}.\n\nFor a song you don't know, tap \"Tap here!\" (top right) along with the beat: it measures the BPM for you.")
                {
                    Anchor = "timing.bpm",
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Progress = c => c.Chart is { } ch && ch.Beat.Bpm.Count > 0 ? "BPM now: " + exactly(ch.Beat.Bpm[0].Bpm, def.Bpm, "", ch.Beat.Bpm[0].Bpm % 1 == 0 ? 0 : 2) : null,
                    Done = c => c.Chart is { } ch && ch.Beat.Bpm.Count > 0 && Math.Abs(ch.Beat.Bpm[0].Bpm - def.Bpm) < 0.0005,
                    Demo = (d, c) => dragStepper(d, () => TutorialAnchors.Resolve("timing.bpm"), () => c.Chart?.Beat.Bpm.FirstOrDefault()?.Bpm ?? 120, def.Bpm, 0.05),
                },

                new TutorialStep("Set the offset",
                    $"The offset says where the first beat is in the audio file, in milliseconds. This song has a short silence first: its first beat is at {def.OffsetMs} ms.\n\n"
                    + $"Drag the Offset number to the right until it shows {def.OffsetMs} ms (or scroll / click the arrows for single steps).")
                {
                    Anchor = "timing.offset",
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Progress = c => c.Chart is { } ch ? "Offset now: " + (Math.Abs(ch.Audio.Bgm.OffsetMs - def.OffsetMs) <= 1 ? $"{ch.Audio.Bgm.OffsetMs} ms ✓" : exactly(ch.Audio.Bgm.OffsetMs, def.OffsetMs, " ms")) : null,
                    Done = c => c.Chart is { } ch && Math.Abs(ch.Audio.Bgm.OffsetMs - def.OffsetMs) <= 1,
                    Demo = (d, c) => dragStepper(d, () => TutorialAnchors.Resolve("timing.offset"), () => c.Chart?.Audio.Bgm.OffsetMs ?? 0, def.OffsetMs, 1),
                },

                new TutorialStep("Check it with the metronome",
                    "Tick \"Metronome clicks\", then press {key:PlayPause} to play. The clicks should land on the drum hits; the lamps below light up on every beat (green = first beat of a bar).\n\n"
                    + "Press {key:PlayPause} again to stop.")
                {
                    Anchor = "timing.metronome",
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Progress = c =>
                    {
                        if (c.Editor is not { } e) return null;
                        if (e.Playback.IsPlaying.Value && e.Timing.MetronomeClicks.Value) c.Bag["played"] = true;
                        return !e.Timing.MetronomeClicks.Value ? "Metronome: off" : c.Bag.ContainsKey("played") ? "Now stop it again" : "Metronome: on — now play";
                    },
                    Done = c => c.Editor is { } e && e.Timing.MetronomeClicks.Value && c.Bag.ContainsKey("played") && !e.Playback.IsPlaying.Value,
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialAnchors.Resolve("timing.metronome"));
                        d.Press(EditorAction.PlayPause);
                        d.WaitFor(() => c.Bag.ContainsKey("played"), "playback with the metronome");
                        d.Press(EditorAction.PlayPause);
                    },
                },

                new TutorialStep("A tempo change: open F6",
                    $"At {clock(speedMs)} the song speeds up to {def.SpeedUp.Bpm:0.##} BPM. Tempo changes live in the \"Timing and Control Points\" window. "
                    + "Press {key:TimingSetup} or click \"Timing Setup Panel\".")
                {
                    Target = c => TutorialContext.FindText(c.Editor?.ChildrenOfType<TimingTabContent>().FirstOrDefault(), "Timing Setup Panel"),
                    Requirement = TutorialNeed.Editor(EditorTab.Timing),
                    Restore = TutorialNeed.RestoreEditor(EditorTab.Timing),
                    Done = c => f6() != null,
                    Demo = (d, _) => d.Press(EditorAction.TimingSetup),
                },

                new TutorialStep("Add a BPM point",
                    "The list on the right holds every timing point: BPM points, scroll speed (how fast notes fly), stops and meter (time signature) changes.\n\n"
                    + "Pick \"BPM\" in the box next to the green + and click +. A new BPM point appears at the cursor.")
                {
                    Area = c => TutorialContext.Union(TutorialContext.Find<ComboBox<F6.NewPointKind>>(f6()), TutorialContext.Find<DialogIconButton>(f6()), c.OpenPopup()),
                    Requirement = TutorialNeed.Dialog<F6>("Timing and Control Points", "Press F6"),
                    Restore = c => c.Editor?.OpenTimingSetup(),
                    Progress = c => f6() is { } w ? "+ adds: " + w.AddKind.Value switch { F6.NewPointKind.Bpm => "BPM", F6.NewPointKind.ScrollSpeed => "Speed", F6.NewPointKind.Stop => "Stop", _ => "Meter" } : null,
                    Done = c => f6() is { } w && w.Draft.Beat.Bpm.Count >= 2,
                    Demo = (d, c) =>
                    {
                        d.Click(() => TutorialContext.Find<ComboBox<F6.NewPointKind>>(f6()));
                        d.WaitFor(() => c.OpenPopup() != null, "the drop-down list");
                        d.Click(() => TutorialContext.FindText(c.OpenPopup(), "BPM"));
                        d.WaitFor(() => f6()?.AddKind.Value == F6.NewPointKind.Bpm, "BPM picked");
                        d.Click(() => TutorialContext.Find<DialogIconButton>(f6()));
                    },
                },

                new TutorialStep("Put it at the speed-up",
                    $"The new point is selected. On the left, type its time into Offset: {Math.Round(speedMs)} ms — and its tempo into BPM: {def.SpeedUp.Bpm:0.##}.\n\n"
                    + "Below, \"When applying timing changes…\" decides what happens to notes you already placed when the timing changes later: "
                    + "move them with the grid (they stay on their beat — usually what you want), scale them, snap them, and move bookmarks and preview along. "
                    + "Our chart has no notes yet. Coming from osu!? This window works just like osu!'s timing setup.")
                {
                    Requirement = TutorialNeed.Dialog<F6>("Timing and Control Points", "Press F6"),
                    Restore = c => c.Editor?.OpenTimingSetup(),
                    Progress = c => f6() is { } w ? $"Selected point: {w.OffsetField.Value:0} ms, {w.Value.Value:0.##} BPM" : null,
                    Tasks = new[]
                    {
                        new TutorialTask($"[ui]Offset[/ui]: {Math.Round(speedMs)}", c => f6() is { } w && w.Draft.Beat.Bpm.Any(b => Math.Abs(w.Draft.BuildMap().PulseToMs(b.Y) - speedMs) <= 1.5))
                        {
                            Target = c => f6()?.ChildrenOfType<NumericUpDown>().Where(TutorialAnchors.IsShown).Skip(1).FirstOrDefault(),
                        },
                        new TutorialTask($"[ui]BPM[/ui]: {def.SpeedUp.Bpm:0.##}", c => f6() is { } w && w.Draft.Beat.Bpm.Any(b => Math.Abs(w.Draft.BuildMap().PulseToMs(b.Y) - speedMs) <= 1.5 && Math.Abs(b.Bpm - def.SpeedUp.Bpm) < 0.0005))
                        {
                            Target = c => f6()?.ChildrenOfType<NumericUpDown>().Where(TutorialAnchors.IsShown).FirstOrDefault(),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        NumericUpDown? field(int i) => f6()?.ChildrenOfType<NumericUpDown>().Where(TutorialAnchors.IsShown).Skip(i).FirstOrDefault();
                        d.Click(() => field(1));
                        d.Do(() => field(1)!.SetText(Math.Round(speedMs).ToString(System.Globalization.CultureInfo.CurrentCulture)));
                        task(d, c, 1);
                        d.Click(() => field(0));
                        d.Do(() => field(0)!.SetText(def.SpeedUp.Bpm.ToString(System.Globalization.CultureInfo.CurrentCulture)));
                    },
                },

                new TutorialStep("Apply with OK",
                    "One more tool in here for later: the Property Transformer ramps, scales or shifts scroll-speed points over a stretch of the song "
                    + "(From / To, then Ramp / Scale / Shift) — for a slow-down before a drop, for example.\n\n"
                    + "Click OK. Every change made in this window becomes one undo step.")
                {
                    Target = c => TutorialContext.FindText(f6(), "OK"),
                    Requirement = c => f6() != null || hasSpeedUp(c) ? TutorialNeed.Editor()(c)
                        : "The window was closed without the new BPM point. Press {key:TimingSetup} to open it again.",
                    Restore = c => c.Editor?.OpenTimingSetup(),
                    Done = c => f6() == null && hasSpeedUp(c),
                    Demo = (d, c) => d.Click(() => TutorialContext.FindText(f6(), "OK")),
                },

                saveStep(),
            };

            bool hasSpeedUp(TutorialContext c) =>
                c.Session is { } s && s.Chart.Beat.Bpm.Any(b => Math.Abs(s.PulseToMs(b.Y) - speedMs) <= 1.5 && Math.Abs(b.Bpm - def.SpeedUp.Bpm) < 0.0005);
        },
    };

    /// <summary>Demo: drags a stepper sideways toward <paramref name="target"/>, correcting up to three times.</summary>
    private static void dragStepper(ITutorialDemo d, Func<Drawable?> stepper, Func<double> current, double target, double perPixel)
    {
        // the stepper measures from where the drag was detected (the first move), so that first move is added on top
        const float first = 16;
        for (int i = 0; i < 2; i++)
        {
            Vector2 from = Vector2.Zero, detect = Vector2.Zero, to = Vector2.Zero;
            d.Do(() =>
            {
                var s = stepper()!;
                from = s.ScreenSpaceDrawQuad.Centre;
                float dx = (float)((target - current()) / perPixel);
                float lead = Math.Sign(dx) * first;
                var local = s.ToLocalSpace(from);
                detect = s.ToScreenSpace(local + new Vector2(lead, 0));
                to = s.ToScreenSpace(local + new Vector2(lead + dx, 0));
            });
            d.DragPath(() => from, () => detect, () => Vector2.Lerp(detect, to, 0.5f), () => to);
        }
    }
}
