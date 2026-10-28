using System.Globalization;
using osu.Framework.Input.Bindings;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part <c>bottombar</c> (no song needed): a generated song with a 6 s sine as its audio and every object
/// kind in its first 3 s. Real input throughout — bottom-bar clicks and editor keys through <see cref="SmokeInput"/>,
/// keys in the jump-to-time dialog (it lives outside the screen layer) through the root input manager. Checks: the
/// scrubbable length is the audio's (not the old 30 s floor), a note after the audio's end extends it and shows the
/// "no audio" stretch, 150 % is gone and the rate underline follows clicks, the four bookmark buttons and Ctrl+Z /
/// Ctrl+Y on bookmark add / remove / rename, the Collab button opens the bookmark window, the time dialog copies its
/// pre-selected time to the OS clipboard and jumps, Ctrl+C puts the modding timestamp of a mixed selection on the OS
/// clipboard, and pasting it into the time dialog selects exactly those objects again.
/// </summary>
public partial class EditorGame
{
    private const double smoke_audio_seconds = 6.0;

    private List<(Action Act, string? Shot)> bottomBarSteps(string? shots)
    {
        string work = Path.Combine(smokeWorkRoot(shots), "smoke-bottombar", "Short Audio");
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        Clipboard clipboard() => (Clipboard)dependencies.Get(typeof(Clipboard));

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test (bottombar): editor not open");
        JumpToTimeDialog timeDialog() => dialogOverlay.Current as JumpToTimeDialog ?? throw new InvalidOperationException("smoke test (bottombar): jump-to-time dialog not open");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        string? clipboardBefore = null;
        string copiedTimestamp = "";
        List<object> selectedBefore = new List<object>();

        (Action, double)[] click(Func<osuTK.Vector2> at) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(at()), 60),
            (() => im.PressButton(MouseButton.Left), 50),
            (() => im.ReleaseButton(MouseButton.Left), 80),
        };

        // the editor's current binding for an action, pressed and released through the smoke input manager
        (Action, double)[] editorKeys(EditorAction action)
        {
            var keys = EditorKeyBindings.All.First(b => b.Action == action).Keys.Keys.Select(toKey).ToList();
            return keys.Select(k => ((Action)(() => im.PressKey(k)), 30.0))
                       .Concat(keys.AsEnumerable().Reverse().Select(k => ((Action)(() => im.ReleaseKey(k)), 30.0)))
                       .Append(((Action)(() => { }), 60.0))
                       .ToArray();
        }

        // keys for the dialog: it sits outside the smoke input manager, so they go to the root input manager
        (Action, double)[] rootKeys(params Key[] keys) =>
            keys.Select(k => ((Action)(() => inputKey(k, true)), 30.0))
                .Concat(keys.AsEnumerable().Reverse().Select(k => ((Action)(() => inputKey(k, false)), 30.0)))
                .Append(((Action)(() => { }), 80.0))
                .ToArray();

        int bookmarks() => ed().Session.Chart.Editor.Bookmarks.Count;
        double now() => ed().Playback.CurrentTime.Value;

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                clipboardBefore = clipboard().GetText();
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                writeSineWav(Path.Combine(work, "short.wav"), smoke_audio_seconds);
                KsonWriter.WriteFile(shortAudioChart(), Path.Combine(work, "exh.kson"));
                pushAndWait(new EditorScreen(EditorSession.Load(work)));
                report($"bottombar: generated song with a {smoke_audio_seconds:0.0} s audio in {work}");
            }, null),
            (() => waitUntil(() => ed().Playback.AudioLengthMs > 0, "the short audio to load", 15_000), null),

            // ---- 2: the length is the audio's
            (() =>
            {
                var e = ed();
                double audio = e.Playback.AudioLengthMs;
                expect(Math.Abs(audio - smoke_audio_seconds * 1000) < 50, inv($"the track is {smoke_audio_seconds} s long ({audio:0} ms)"));
                expect(Math.Abs(e.Playback.LengthMs - audio) < 1, inv($"the scrubbable length is the audio's ({e.Playback.LengthMs:0} vs {audio:0} ms; it used to be at least 30 s)"));
                expect(!e.BottomBar.BeyondAudioShown, "no 'no audio' stretch while the chart ends inside the audio");
                e.TriggerAction(EditorAction.SeekEnd);
                expect(Math.Abs(now() - audio) < 1, "Ctrl+End = the audio's end");
                e.Playback.CurrentTime.Value = 0;

                // a note after the audio's end: the range follows it and the bar marks the part without audio
                e.Session.Commands.Execute(new AddNotesCommand(new[] { new NoteRef(NoteKind.Bt, 1, new Note(4800)) }, "smoke: note at 10 s"));
                expect(Math.Abs(e.Playback.LengthMs - 10_000) < 1, inv($"a note at 10 s extends the range to it ({e.Playback.LengthMs:0} ms)"));
                waitUntil(() => ed().BottomBar.BeyondAudioShown, "the bottom bar to mark the stretch after the audio", 3000);
            }, "70-bottombar-beyond-audio"),
            (() =>
            {
                var e = ed();
                e.TriggerAction(EditorAction.Undo);
                expect(Math.Abs(e.Playback.LengthMs - e.Playback.AudioLengthMs) < 1, "undo: back to the audio's length");
                waitUntil(() => !ed().BottomBar.BeyondAudioShown, "the 'no audio' stretch to go", 3000);
                report(inv($"bottombar: length = audio {e.Playback.AudioLengthMs:0} ms; a note at 10 s -> 10000 ms with the 'no audio' stretch; undo -> back - ok"));
            }, null),

            // ---- 8/9: rates
            (() =>
            {
                var e = ed();
                var rates = e.BottomBar.RateButtons.Select(r => r.Rate).ToArray();
                expect(rates.SequenceEqual(new[] { 0.25, 0.5, 0.75, 1 }), $"rates are 25/50/75/100 % ({string.Join("/", rates)})");
                expect(e.Playback.Rate.MaxValue <= 1, "the clock refuses more than 100 %");
                e.Playback.Rate.Value = 1;
                e.TriggerAction(EditorAction.PlaybackRateUp);
                expect(Math.Abs(e.Playback.Rate.Value - 1) < 0.001, "Alt+Up at 100 % stays at 100 %");
                im.UseParentInput = false;
                inputSequence("bottombar: click 50 %", click(() => e.BottomBar.RateButtons[1].ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                var e = ed();
                expect(Math.Abs(e.Playback.Rate.Value - 0.5) < 0.001, $"a click on 50 % sets the rate ({e.Playback.Rate.Value})");
                expect(e.BottomBar.RateButtons.Count(r => r.IsActive) == 1 && e.BottomBar.RateButtons[1].IsActive, "only 50 % is underlined");
            }, "71-bottombar-rate-50"),
            (() => inputSequence("bottombar: click 100 %", click(() => ed().BottomBar.RateButtons[3].ScreenSpaceDrawQuad.Centre)), null),
            (() =>
            {
                var e = ed();
                expect(Math.Abs(e.Playback.Rate.Value - 1) < 0.001 && e.BottomBar.RateButtons[3].IsActive && !e.BottomBar.RateButtons[1].IsActive, "the underline moved to 100 %");
                report("bottombar: rates 25/50/75/100 %, clicks move the underline - ok");

                // ---- 10: bookmark buttons, all undoable
                e.Playback.CurrentTime.Value = 2000;
                inputSequence("bottombar: add bookmark", click(() => e.BottomBar.AddBookmarkButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(bookmarks() == 1 && Math.Abs(ed().Bookmarks[0] - 2000) < 1, $"the + button adds a bookmark at the cursor ({bookmarks()})");
                inputSequence("bottombar: Ctrl+Z after add", editorKeys(EditorAction.Undo));
            }, null),
            (() =>
            {
                expect(bookmarks() == 0, "Ctrl+Z removes the added bookmark");
                inputSequence("bottombar: Ctrl+Y", editorKeys(EditorAction.Redo));
            }, null),
            (() =>
            {
                expect(bookmarks() == 1, "redo brings it back");
                ed().Playback.CurrentTime.Value = 1000;
                inputSequence("bottombar: add a second bookmark", click(() => ed().BottomBar.AddBookmarkButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(bookmarks() == 2, "two bookmarks");
                ed().Playback.CurrentTime.Value = 0;
                inputSequence("bottombar: next bookmark", click(() => ed().BottomBar.NextBookmarkButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(Math.Abs(now() - 1000) < 1, inv($"› jumps to the first bookmark ({now():0})"));
                inputSequence("bottombar: next bookmark again", click(() => ed().BottomBar.NextBookmarkButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(Math.Abs(now() - 2000) < 1, inv($"› again jumps to the second ({now():0})"));
                inputSequence("bottombar: previous bookmark", click(() => ed().BottomBar.PreviousBookmarkButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(Math.Abs(now() - 1000) < 1, inv($"‹ jumps back ({now():0})"));
                ed().Playback.CurrentTime.Value = 1300;
                inputSequence("bottombar: remove bookmark", click(() => ed().BottomBar.RemoveBookmarkButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(bookmarks() == 1 && Math.Abs(ed().Bookmarks[0] - 2000) < 1, "− removes the nearest bookmark (the one at 1 s)");
                inputSequence("bottombar: Ctrl+Z after remove", editorKeys(EditorAction.Undo));
            }, null),
            (() =>
            {
                var e = ed();
                expect(bookmarks() == 2, "Ctrl+Z restores the removed bookmark");
                long y = e.Session.Chart.Editor.Bookmarks.First().Y;
                expect(e.BookmarkTools.Set(y, new Bookmark("Part A - smoke", "#ff66aa")), "rename through the bookmark tools");
                inputSequence("bottombar: Ctrl+Z after rename", editorKeys(EditorAction.Undo));
            }, null),
            (() =>
            {
                var e = ed();
                expect(e.Session.Chart.Editor.Bookmarks.First().Value.Label == "", "Ctrl+Z undoes the rename");
                expect(e.BottomBar.AddBookmarkButton.TooltipText.ToString().Contains(EditorKeyBindings.ShortcutOf(EditorAction.AddBookmark)), "the + tooltip names its key");
                report("bottombar: bookmark + / - / prev / next buttons; Ctrl+Z / Ctrl+Y on add, remove and rename - ok");
                inputSequence("bottombar: collab button", click(() => e.BottomBar.CollabButton.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                expect(dialogOverlay.Current is BookmarkListDialog { List.Items.Count: 2 }, "the Collab button opens the bookmark window with both bookmarks");
            }, "72-bottombar-collab"),
            (() =>
            {
                dialogOverlay.Current!.Cancel();
                report("bottombar: Collab button opens the bookmark / collab window - ok");

                // ---- 11: jump-to-time dialog
                ed().Playback.CurrentTime.Value = 1234;
                inputSequence("bottombar: click the time", click(() => ed().BottomBar.TimeDisplay.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                var d = timeDialog();
                expect(d.TimeBox.Text == "00:01:234", $"the dialog shows the current time ({d.TimeBox.Text})");
                expect(d.TimeBox.HasFocus, "the time field has the focus");
                clipboard().SetText("smoke sentinel");
                inputSequence("bottombar: Ctrl+C in the time field", rootKeys(Key.ControlLeft, Key.C));
            }, "73-bottombar-time-dialog"),
            (() =>
            {
                expect(clipboard().GetText() == "00:01:234", $"Ctrl+C copied the pre-selected time to the OS clipboard ({clipboard().GetText()})");
                timeDialog().TimeBox.Text = "00:04.250";
                inputSequence("bottombar: Enter jumps", rootKeys(Key.Enter));
            }, null),
            (() =>
            {
                expect(dialogOverlay.Current is not JumpToTimeDialog, "Enter closed the dialog");
                expect(Math.Abs(now() - 4250) < 1, inv($"jumped to 4.250 s ({now():0})"));
                inputSequence("bottombar: reopen the time dialog", click(() => ed().BottomBar.TimeDisplay.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                timeDialog().TimeBox.Text = "not a time";
                inputSequence("bottombar: Enter on garbage", rootKeys(Key.Enter));
            }, null),
            (() =>
            {
                var d = timeDialog();
                expect(d.ErrorText.Text.ToString().Length > 0, "a text that is no time keeps the dialog open with the reason");
                expect(Math.Abs(now() - 4250) < 1, "and does not move the cursor");
                inputSequence("bottombar: Esc closes", rootKeys(Key.Escape));
            }, null),
            (() =>
            {
                expect(dialogOverlay.Current is not JumpToTimeDialog, "Esc closed the dialog in one press");
                report("bottombar: time dialog - current time pre-selected, Ctrl+C to the OS clipboard, Enter jumps (ss.mmm), garbage refused, Esc closes - ok");

                // ---- 12: modding timestamp of a mixed selection
                var e = ed();
                e.Session.Selection.SelectRange(e.Session.Chart, 0, 2000);
                selectedBefore = e.Session.Selection.Notes.Cast<object>().Concat(e.Session.Selection.LaserPoints).ToList();
                expect(selectedBefore.Count == 7, $"7 objects selected ({selectedBefore.Count})");
                clipboard().SetText("smoke sentinel");
                inputSequence("bottombar: Ctrl+C on a selection", editorKeys(EditorAction.Copy));
            }, null),
            (() =>
            {
                copiedTimestamp = clipboard().GetText() ?? "";
                const string expected = "00:00:500 (00:00:500|BT-A,00:01:000|BT-D hold,00:01:000|FX-L,00:01:500|FX-R hold,"
                                        + "00:02:000|LASER-L 0.25,00:02:500|LASER-L 0.25>0.75,00:03:000|LASER-L 0.5) - ";
                expect(copiedTimestamp == expected, $"Ctrl+C put the modding timestamp on the OS clipboard ('{copiedTimestamp}')");
                expect(EditorSession.Clipboard is { Notes.Count: 4, Lasers.Count: 1 }, "and still copied the objects for Ctrl+V");
                report("bottombar: Ctrl+C timestamp: " + copiedTimestamp);

                ed().Session.Selection.Clear();
                ed().Playback.CurrentTime.Value = 5000;
                inputSequence("bottombar: open the time dialog for the paste", click(() => ed().BottomBar.TimeDisplay.ScreenSpaceDrawQuad.Centre));
            }, null),
            (() =>
            {
                timeDialog().TimeBox.Text = copiedTimestamp;
                inputSequence("bottombar: Enter on the timestamp", rootKeys(Key.Enter));
            }, null),
            (() =>
            {
                var e = ed();
                var selected = e.Session.Selection.Notes.Cast<object>().Concat(e.Session.Selection.LaserPoints).ToList();
                expect(Math.Abs(now() - 500) < 1, inv($"the timestamp jumped to its first object ({now():0})"));
                expect(selected.Count == selectedBefore.Count && selected.All(selectedBefore.Contains), $"the timestamp selected exactly the copied objects ({selected.Count})");
                expect(e.LastJump is { Missing.Count: 0, Ambiguous.Count: 0 }, "nothing missing or ambiguous");

                // a timestamp naming an object that is not there: the rest is selected, the missing one reported
                var (ok, message) = e.JumpTo("00:00:500 (00:00:500|BT-A,00:00:750|BT-C) - looks off");
                expect(ok && e.Session.Selection.Count == 1 && e.LastJump is { Missing.Count: 1 } && message.Contains("not found"), $"a missing object is reported ({message})");
                var (bareOk, _) = e.JumpTo("00:02:500 -");
                expect(bareOk && Math.Abs(now() - 2500) < 1, "a bare 'mm:ss:mmm -' jumps too");
                report("bottombar: pasted timestamp -> jump + the same 7 objects selected; a missing object reported; 'mm:ss:mmm -' jumps - ok");
            }, "74-bottombar-timestamp-selected"),
            (() =>
            {
                im.UseParentInput = true;
                if (clipboardBefore != null) clipboard().SetText(clipboardBefore);
                ed().Exit();
            }, null),
        };

        return steps;
    }

    /// <summary>The <see cref="Key"/> an <see cref="InputKey"/> of a binding is pressed with (left modifiers).</summary>
    private static Key toKey(InputKey k) => k switch
    {
        InputKey.Control or InputKey.LControl => Key.ControlLeft,
        InputKey.RControl => Key.ControlRight,
        InputKey.Shift or InputKey.LShift => Key.ShiftLeft,
        InputKey.RShift => Key.ShiftRight,
        InputKey.Alt or InputKey.LAlt => Key.AltLeft,
        InputKey.RAlt => Key.AltRight,
        _ => Enum.Parse<Key>(k.ToString()),
    };

    /// <summary>120 BPM, every object kind within the first 3 s: BT chip, BT hold, FX chip, FX hold, a laser with a slam.</summary>
    private static Chart shortAudioChart()
    {
        var c = new Chart();
        c.Meta.Title = "Short Audio";
        c.Meta.Artist = "smoke";
        c.Meta.DifficultySlot = 2;
        c.Meta.Level = 5;
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Audio.Bgm.Filename = "short.wav";
        c.Notes.Bt[0].Add(new Note(240));
        c.Notes.Bt[3].Add(new Note(480, 240));
        c.Notes.Fx[0].Add(new Note(480));
        c.Notes.Fx[1].Add(new Note(720, 480));
        c.Notes.Lasers[0].Add(new LaserSection(960, new[] { new LaserPoint(0, 0.25), new LaserPoint(240, 0.25, 0.75), new LaserPoint(480, 0.5) }));
        return c;
    }

    /// <summary>A mono 16-bit 44.1 kHz sine of <paramref name="seconds"/>.</summary>
    private static void writeSineWav(string path, double seconds)
    {
        const int rate = 44100;
        int samples = (int)(rate * seconds);
        using var w = new BinaryWriter(File.Create(path));
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8);
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8);
        w.Write(samples * 2);
        for (int i = 0; i < samples; i++)
            w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * 3000));
    }
}
