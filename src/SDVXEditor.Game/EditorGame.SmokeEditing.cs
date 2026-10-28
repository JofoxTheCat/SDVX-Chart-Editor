using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using osu.Framework.Configuration;
using osu.Framework.Screens;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part for <c>--open</c>: works on a copy of the song (never the folder that was
/// passed), renders it, edits it through the same tool code the mouse uses, checks undo/redo,
/// saves, reloads and compares, and measures frame times while the dense part plays.
/// </summary>
public partial class EditorGame
{
    private readonly StringBuilder smokeReport = new StringBuilder();

    private void report(string line)
    {
        smokeReport.AppendLine(line);
        osu.Framework.Logging.Logger.Log("smoke: " + line);
    }

    private List<(Action Act, string? Shot)> chartEditingSteps(string songFolder, int? slot, string? shots, double interval)
    {
        // work on a copy: the smoke test never writes into the folder it was given
        string work = Path.Combine(smokeWorkRoot(shots),
            "smoke-song", Path.GetFileName(songFolder.TrimEnd('\\', '/')));

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test: editor not open");

        string? originalChartHash = null;
        string? originalChartPath = null;
        long p0 = 0, beat = 240;
        int notes0 = 0;
        Note? chip = null;
        double denseMs = 0, laserMs = 0;
        var frameTimes = new List<double>();
        var playfieldTimes = new List<double>();
        var drawTimes = new List<double>();
        var quadCounts = new List<int>();
        var drift = new List<double>();
        bool sampling = false;
        FrameSync? oldFrameSync = null;
        double oldInactiveHz = 0;
        double offsetBefore = 0, trackWithOffset = 0;

        int allNotes(Chart c) => c.Notes.Bt.Concat(c.Notes.Fx).Sum(l => l.Count);
        float laneX(NoteKind kind, int lane) => PlayfieldLayout.LaneLeft(kind, lane) + PlayfieldLayout.LaneWidth(kind) / 2;
        PlayfieldPointer at(long pulse, float x) => ed().Playfield.PointerAt(pulse, x);

        void click(long pulse, float x, PointerButton button = PointerButton.Left, bool shift = false)
        {
            var c = ed().Compose;
            if (c.PointerDown(at(pulse, x), button, false, shift)) c.PointerUp(at(pulse, x));
        }

        void drag(long fromPulse, float fromX, long toPulse, float toX)
        {
            var c = ed().Compose;
            expect(c.PointerDown(at(fromPulse, fromX), PointerButton.Left, false, false), "a drag starts");
            c.PointerDrag(at((fromPulse + toPulse) / 2, (fromX + toX) / 2), false);
            c.PointerDrag(at(toPulse, toX), false);
            c.PointerUp(at(toPulse, toX));
        }

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder))
                    File.Copy(f, Path.Combine(work, Path.GetFileName(f)));

                var session = EditorSession.Load(work, slot);
                originalChartPath = session.ChartPath!;
                originalChartHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(originalChartPath)));
                report($"opened {Path.GetFileName(songFolder)} / {Path.GetFileName(originalChartPath)} ({session.Chart.Meta.DifficultyDisplayName} {session.Chart.Meta.Level}) as a copy in {work}");
                report($"chart: {allNotes(session.Chart)} BT/FX notes, {session.Chart.Notes.Lasers.Sum(l => l.Sum(s => s.Points.Count))} laser points, {session.Chart.Notes.Lasers.Sum(l => l.Sum(s => s.SlamCount))} slams");

                // densest 4 s of notes, and the 4 s with the most laser points
                denseMs = densest(session, notesOnly: true);
                laserMs = densest(session, notesOnly: false);
                pushAndWait(new EditorScreen(session));
            }, null),
            (() =>
            {
                var e = ed();
                expect(e.Playback.Track != null, "the song audio was found and loaded");
                e.Playback.CurrentTime.Value = denseMs;
            }, null),
            (() => expect(ed().Playfield.LastQuadCount > 50, "the playfield draws the chart"), "08-compose-real-chart"),
            (() => ed().Playback.CurrentTime.Value = laserMs, null),
            (() => { }, "09-laser-section"),
            (() => ed().SelectTab(EditorTab.Timing), null),
            (() => expect(ed().Playback.Track!.IsLoaded && ed().Playback.Track!.Length > 10_000, "track length known"), "10-waveform-timing-tab"),
            (() =>
            {
                var e = ed();
                e.SelectTab(EditorTab.Compose);
                // a selection on the real chart: two measures from the dense spot
                var s = e.Session;
                long from = s.Timing.MeasureStart(s.Timing.MeasureAt((long)s.MsToPulse(denseMs)));
                s.Selection.SelectRange(s.Chart, from, s.Timing.MeasureStart(s.Timing.MeasureAt(from) + 2) - 1);
                e.SelectTool(EditorTool.Select);
                e.Playback.CurrentTime.Value = s.PulseToMs(from) + 300;
                report($"selection screenshot: {s.Selection.Count} objects selected");
                expect(s.Selection.Count > 0, "range selection on the real chart");
            }, null),
            (() => { }, "11-selection"),

            // ---- edits, in an empty stretch after the chart's last event
            (() =>
            {
                var e = ed();
                var s = e.Session;
                s.Selection.Clear();
                int m = s.Timing.MeasureAt(s.Chart.LastEventPulse()) + 4;
                p0 = s.Timing.MeasureStart(m);
                beat = s.Timing.BeatLength(m);
                notes0 = allNotes(s.Chart);
                e.Playback.LengthMs = s.PulseToMs(p0 + 20 * beat);

                // BT chip
                e.SelectTool(EditorTool.BtChip);
                click(p0, laneX(NoteKind.Bt, 0));
                expect(allNotes(s.Chart) == notes0 + 1, "BT chip placed");
                chip = s.Chart.Notes.Bt[0].Single(n => n.Y == p0);
                click(p0, laneX(NoteKind.Bt, 0));
                expect(allNotes(s.Chart) == notes0 + 1, "a second chip on the same spot is refused");

                // move it one beat later and one lane right with the select tool
                e.SelectTool(EditorTool.Select);
                drag(p0, laneX(NoteKind.Bt, 0), p0 + beat, laneX(NoteKind.Bt, 1));
                expect(s.Chart.Notes.Bt[1].Contains(chip) && chip.Y == p0 + beat, "chip dragged to BT-B one beat later");
                e.TriggerAction(EditorAction.Undo);
                expect(s.Chart.Notes.Bt[0].Contains(chip) && chip.Y == p0, "undo puts the chip back");
                e.TriggerAction(EditorAction.Redo);
                expect(s.Chart.Notes.Bt[1].Contains(chip) && chip.Y == p0 + beat, "redo moves it again");

                // right click deletes
                click(p0 + beat, laneX(NoteKind.Bt, 1), PointerButton.Right);
                expect(allNotes(s.Chart) == notes0, "right click deleted the chip");
                e.TriggerAction(EditorAction.Undo);
                expect(allNotes(s.Chart) == notes0 + 1, "undo restores the deleted chip");
                e.TriggerAction(EditorAction.Redo);
                expect(allNotes(s.Chart) == notes0, "redo deletes it again");
                report("chip: place, refuse overlap, drag-move, undo/redo, right-click delete, undo/redo - ok");

                // BT hold by drag, FX chip via Shift
                e.SelectTool(EditorTool.BtHold);
                drag(p0, laneX(NoteKind.Bt, 2), p0 + 2 * beat, laneX(NoteKind.Bt, 2));
                expect(s.Chart.Notes.Bt[2].Any(n => n.Y == p0 && n.Length == 2 * beat), "BT hold drawn over two beats");
                e.SelectTool(EditorTool.BtChip);
                e.Tools.ShiftHeld.Value = true;
                click(p0 + 3 * beat, PlayfieldLayout.BT_LEFT + 20);
                e.Tools.ShiftHeld.Value = false;
                expect(s.Chart.Notes.Fx[0].Any(n => n.Y == p0 + 3 * beat), "Shift+BT chip placed an FX-L chip");
                e.SelectTool(EditorTool.FxHold);
                drag(p0 + 4 * beat, PlayfieldLayout.BT_RIGHT - 20, p0 + 6 * beat, PlayfieldLayout.BT_RIGHT - 20);
                expect(s.Chart.Notes.Fx[1].Any(n => n.Y == p0 + 4 * beat && n.Length == 2 * beat), "FX-R hold");
                report("holds: BT hold drag, Shift = FX chip, FX hold - ok");
            }, null),
            (() =>
            {
                var e = ed();
                var s = e.Session;

                // laser L: draw a section 0 -> 1 over four beats
                e.TriggerAction(EditorAction.LaserSideLeft);
                expect(e.Tools.Tool.Value == EditorTool.Laser && e.Tools.LaserSide.Value == 0, "Q = laser L");
                drag(p0, PlayfieldLayout.LaserX(0, false), p0 + 4 * beat, PlayfieldLayout.LaserX(1, false));
                var section = s.Chart.Notes.Lasers[0].Single(x => x.Y == p0);
                expect(section.Points.Count == 2 && section.Points[0].V == 0 && section.Points[1].V == 1 && section.EndY == p0 + 4 * beat, "laser section drawn");

                // click in the middle inserts a point; dragging it onto the end pulse makes a slam
                var c = e.Compose;
                expect(c.PointerDown(at(p0 + 2 * beat, PlayfieldLayout.LaserX(0.5, false)), PointerButton.Left, false, false), "laser point grabbed");
                section = s.Chart.Notes.Lasers[0].Single(x => x.Y == p0);
                expect(section.Points.Count == 3 && Math.Abs(section.Points[1].V - 0.5) < 1e-9, "click on the section inserted a point at 0.5");
                c.PointerDrag(at(p0 + 3 * beat, PlayfieldLayout.LaserX(0.5, false)), false);
                c.PointerDrag(at(p0 + 4 * beat, PlayfieldLayout.LaserX(0.5, false)), false);
                c.PointerUp(at(p0 + 4 * beat, PlayfieldLayout.LaserX(0.5, false)));
                section = s.Chart.Notes.Lasers[0].Single(x => x.Y == p0);
                expect(section.Points.Count == 2 && section.SlamCount == 1 && section.Points[1].V == 0.5 && section.Points[1].Vf == 1,
                    "point dragged onto its neighbour's pulse became a slam 0.5 -> 1");

                e.TriggerAction(EditorAction.Undo);
                section = s.Chart.Notes.Lasers[0].Single(x => x.Y == p0);
                expect(section.Points.Count == 2 && section.SlamCount == 0, "undo of insert+drag is one step");
                e.TriggerAction(EditorAction.Redo);
                section = s.Chart.Notes.Lasers[0].Single(x => x.Y == p0);
                expect(section.SlamCount == 1, "redo restores the slam");

                // laser R, wide via Shift, free value with Ctrl
                e.TriggerAction(EditorAction.LaserSideRight);
                var cc = e.Compose;
                expect(cc.PointerDown(at(p0 + 5 * beat, PlayfieldLayout.LaserX(0.3, true)), PointerButton.Left, false, true), "wide laser drag starts");
                cc.PointerDrag(at(p0 + 7 * beat, PlayfieldLayout.LaserX(0.8, true) + 1.3f), true);
                cc.PointerUp(at(p0 + 7 * beat, PlayfieldLayout.LaserX(0.8, true) + 1.3f));
                var wide = s.Chart.Notes.Lasers[1].Single(x => x.Y == p0 + 5 * beat);
                expect(wide.Width == 2 && Math.Abs(wide.Points[0].V - 0.3) < 1e-9, "Shift drew a wide laser R, start snapped to 1/50");
                expect(Math.Abs(wide.Points[1].V * 50 - Math.Round(wide.Points[1].V * 50)) > 1e-6, "Ctrl left the end value unsnapped");

                // right click on a point of a two-point section removes the section
                click(p0 + 5 * beat, PlayfieldLayout.LaserX(0.3, true), PointerButton.Right);
                expect(!s.Chart.Notes.Lasers[1].Contains(wide), "right click removed the laser");
                e.TriggerAction(EditorAction.Undo);
                e.Playback.CurrentTime.Value = s.PulseToMs(p0 + 2 * beat);
                report("lasers: draw, insert point, drag to slam, undo/redo as one step, wide (Shift), free value (Ctrl), right-click delete - ok");
            }, "12-edits"),
            (() =>
            {
                var e = ed();
                var s = e.Session;

                // box select the edits, copy, paste four beats later, mirror, undo both
                e.SelectTool(EditorTool.Select);
                e.Compose.SelectInBox(at(p0 - 10, PlayfieldLayout.SIDE), at(p0 + 7 * beat + 10, PlayfieldLayout.SIDE + PlayfieldLayout.LANES), false);
                int selected = s.Selection.Count;
                expect(selected >= 5, $"box selection caught the edits ({selected})");
                e.TriggerAction(EditorAction.Copy);
                e.Playback.CurrentTime.Value = s.PulseToMs(p0 + 8 * beat);
                int before = allNotes(s.Chart);
                e.TriggerAction(EditorAction.Paste);
                expect(allNotes(s.Chart) > before, "paste at the current time added notes");
                expect(s.Selection.Count >= selected, "the pasted objects are selected");
                e.TriggerAction(EditorAction.MirrorHorizontal);
                expect(s.Chart.Notes.Lasers[1].Any(x => x.Y == p0 + 8 * beat), "mirror moved the pasted laser L to R");
                e.TriggerAction(EditorAction.NudgeForward);
                e.TriggerAction(EditorAction.Undo);
                e.TriggerAction(EditorAction.Undo);
                e.TriggerAction(EditorAction.Undo);
                expect(allNotes(s.Chart) == before, "undo removed nudge, mirror and paste");
                e.Playback.CurrentTime.Value = s.PulseToMs(p0 + 3 * beat);
                report($"selection: box ({selected}), copy, paste at time, mirror, nudge, 3x undo - ok");
            }, "13-after-paste-undo"),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                expect(s.IsDirty, "edits make the chart dirty");
                e.TriggerAction(EditorAction.AddBookmark);
                e.TriggerAction(EditorAction.Save);
                expect(!s.IsDirty, "saved");
                string saved = s.ChartPath!;
                expect(saved.EndsWith(".kson", StringComparison.OrdinalIgnoreCase) && File.Exists(saved), "saved as .kson");
                // a .ksh is never overwritten (the save is a .kson beside it); an opened .kson is saved in place
                // (ABYSS MOTION ships both, and the .kson is the one opened). Either way the folder the smoke test
                // was given is never written.
                string hashNow(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                if (originalChartPath!.EndsWith(".kson", StringComparison.OrdinalIgnoreCase))
                    expect(string.Equals(saved, originalChartPath, StringComparison.OrdinalIgnoreCase), $"an opened .kson is saved in place ({Path.GetFileName(saved)})");
                else
                    expect(hashNow(originalChartPath) == originalChartHash, "the original .ksh is untouched");
                expect(hashNow(Path.Combine(songFolder, Path.GetFileName(originalChartPath))) == originalChartHash, "the chart in the folder given to --open is untouched");

                var reloaded = SongFolder.Load(work);
                var again = reloaded.Charts.Single(c => string.Equals(c.FilePath, saved, StringComparison.OrdinalIgnoreCase));
                string a = KsonWriter.Write(s.Chart), b = KsonWriter.Write(again.Chart);
                expect(a == b, "reloaded chart equals the edited chart (kson text)");
                expect(again.Chart.Notes.Lasers[0].Any(x => x.Y == p0 && x.SlamCount == 1), "the slam survived the round trip");
                expect(again.Chart.Editor.Bookmarks.Count >= 1, "the bookmark was saved");
                report($"save: {Path.GetFileName(saved)} written ({new FileInfo(saved).Length} bytes), {Path.GetFileName(originalChartPath)} in the --open folder unchanged, reload identical - ok");
            }, null),

            // ---- universal audio offset: the track runs offset ms ahead of the drawn time
            // (Track.Seek reaches Track.CurrentTime through the audio thread: read it a step later)
            (() =>
            {
                var offset = config.GetBindable<double>(EditorSetting.AudioOffset);
                offsetBefore = offset.Value;
                offset.Value = 50;
                var track = ed().Playback.Track!;
                ed().Playback.CurrentTime.Value = 10_000;
                // the seek reaches Track.CurrentTime through the audio thread; a wrong offset still times out here
                waitUntil(() => Math.Abs(track.CurrentTime - 10_050) < 2, FormattableString.Invariant($"the track to run 50 ms ahead of the drawn 10000 ms (offset +50)"), 3000);
            }, null),
            (() =>
            {
                var track = ed().Playback.Track!;
                trackWithOffset = track.CurrentTime;
                config.GetBindable<double>(EditorSetting.AudioOffset).Value = 0;
                waitUntil(() => Math.Abs(track.CurrentTime - 10_000) < 2, "the track to follow the drawn 10000 ms again (offset 0)", 3000);
            }, null),
            (() =>
            {
                var e = ed();
                double without = e.Playback.Track!.CurrentTime;
                config.GetBindable<double>(EditorSetting.AudioOffset).Value = offsetBefore;
                expect(Math.Abs(trackWithOffset - 10_050) < 2 && Math.Abs(without - 10_000) < 2,
                    FormattableString.Invariant($"the universal offset puts the track offset ms ahead of the drawn time (+50: {trackWithOffset:0.0}, 0: {without:0.0})"));
                var dialog = e.OpenAudioOffset();
                Scheduler.AddDelayed(() => dialog.Accept(), 200);
                report(FormattableString.Invariant($"audio offset: +50 ms -> track {trackWithOffset:0.0} ms at drawn 10000 ms, 0 -> {without:0.0} ms - ok"));
            }, null),

            // ---- performance while the densest part plays
            (() =>
            {
                var e = ed();
                var sync = frameworkConfig.GetBindable<FrameSync>(FrameworkSetting.FrameSync);
                oldFrameSync = sync.Value;
                sync.Value = FrameSync.Unlimited;
                // an unfocused window is throttled to 60 Hz; measure the same way either way
                oldInactiveHz = Host.MaximumInactiveHz;
                Host.MaximumInactiveHz = Host.MaximumUpdateHz;
                e.Zoom.Value = 0.5f; // twice the default amount of chart on screen
                e.Playback.CurrentTime.Value = denseMs;
                e.Playback.IsPlaying.Value = true;
                sampling = true;
                Scheduler.AddDelayed(() => sampling = false, 5000);
            }, null),
        };

        // spectral timeline waveform: the real song, then a synthetic kick / tone / noise file (EditorGame.SmokeWaveform.cs)
        steps.InsertRange(steps.FindIndex(st => st.Item2 == "10-waveform-timing-tab") + 1, spectralWaveformSteps(ed, Path.GetDirectoryName(work)!));

        // sample each update frame while playing
        Scheduler.AddDelayed(() =>
        {
            if (!sampling || screenStack.CurrentScreen is not EditorScreen e) return;
            frameTimes.Add(Clock.ElapsedFrameTime);
            playfieldTimes.Add(e.Playfield.LastUpdateMs);
            quadCounts.Add(e.Playfield.LastQuadCount);
            drawTimes.Add(Host.DrawThread.Clock.ElapsedFrameTime);
            if (e.Playback.Track is { IsRunning: true } track)
                drift.Add(Math.Abs(track.CurrentTime - e.Playback.TrackTime));
        }, 0, true);

        for (int i = 0; i < Math.Ceiling(5200 / interval); i++)
            steps.Add((() => { }, null));

        steps.Add((() =>
        {
            var e = ed();
            e.Playback.IsPlaying.Value = false;
            if (oldFrameSync is FrameSync fs) frameworkConfig.GetBindable<FrameSync>(FrameworkSetting.FrameSync).Value = fs;
            Host.MaximumInactiveHz = oldInactiveHz;
            expect(frameTimes.Count > 30, "frame samples collected");
            expect(e.Hitsounds.Played > 0, "hitsounds played during playback");
            var hs = e.Hitsounds;
            expect(hs.Resolution.Count == Enum.GetValues<Core.Audio.HitsoundSlot>().Length, "every hitsound slot resolved to a sample, the synth fallback or none");
            expect(hs.LeadMs is >= 0 and <= 200, "hitsound lead from the output latency is sane");
            // chords in the played stretch: notes of one slot on one pulse must be layered
            var song = e.Session;
            long pFrom = (long)song.MsToPulse(denseMs), pTo = (long)song.MsToPulse(denseMs + 4500);
            int maxChord = song.Chart.Notes.Bt.SelectMany(l => l).Where(n => n.Y >= pFrom && n.Y <= pTo).GroupBy(n => (n.Y, n.IsHold)).Select(g => g.Count()).DefaultIfEmpty(0).Max();
            expect(maxChord < 2 || hs.MaxChordLayers >= 2, "BT chords play one layered sample per note");
            expect(drift.Count > 30, "the song audio was running while the clock played");
            e.Playback.Rate.Value = 0.5;
            expect(Math.Abs(e.Playback.Track!.Tempo.Value - 0.5) < 1e-9, "playback rate goes to the track tempo (pitch kept)");
            e.Playback.Rate.Value = 1;

            static string stats(List<double> v) => string.Create(CultureInfo.InvariantCulture,
                $"avg {v.Average():0.00} ms, p95 {v.OrderBy(x => x).ElementAt((int)(v.Count * 0.95)):0.00} ms, max {v.Max():0.00} ms");

            report($"perf ({frameTimes.Count} update frames, zoom 0.5, densest 4 s, frame limiter off):");
            report($"  update frame time  {stats(frameTimes)} (~{1000 / frameTimes.Average():0} fps)");
            report($"  draw frame time    {stats(drawTimes)} (~{1000 / drawTimes.Average():0} fps)");
            report($"  playfield update   {stats(playfieldTimes)}");
            report($"  quads per frame    avg {quadCounts.Average():0}, max {quadCounts.Max()}");
            report($"  hitsound ticks     {e.Hitsounds.Played} frames, {hs.ChannelsPlayed} samples, largest chord {hs.MaxChordLayers} layers (largest BT chord in the stretch {maxChord}), lead {hs.LeadMs:0} ms");
            report($"  hitsound samples   {hs.SkinFolder ?? "built-in only"}: {string.Join(", ", hs.Resolution.Select(r => $"{Core.Audio.HitsoundMap.KeyOf(r.Key)}={r.Value}"))}");
            report($"  clock vs track     {stats(drift)} ({drift.Count} samples while the track ran; internal consistency only, not output latency)");

            string file = Path.Combine(Path.GetDirectoryName(work)!, "smoke-report.txt");
            File.WriteAllText(file, smokeReport.ToString());
        }, null));

        steps.AddRange(timingEditingSteps(work, ed));
        steps.Add((() => { ed().Exit(); }, null));
        return steps;
    }

    /// <summary>Audio time where the 4 s window with the most notes (or laser points) starts.</summary>
    private static double densest(EditorSession s, bool notesOnly)
    {
        var times = new List<double>();
        if (notesOnly)
        {
            foreach (var lane in s.Chart.Notes.Bt.Concat(s.Chart.Notes.Fx))
                times.AddRange(lane.Select(n => s.PulseToMs(n.Y)));
        }
        else
        {
            foreach (var lane in s.Chart.Notes.Lasers)
                foreach (var sec in lane)
                    times.AddRange(sec.Points.Select(p => s.PulseToMs(sec.Y + p.Ry)));
        }

        if (times.Count == 0) return 0;
        times.Sort();
        int best = 0, bestCount = 0;
        for (int i = 0, j = 0; i < times.Count; i++)
        {
            while (times[i] - times[j] > 4000) j++;
            if (i - j + 1 > bestCount)
            {
                bestCount = i - j + 1;
                best = j;
            }
        }

        // put the window's start a little above the judgement line
        return Math.Max(0, times[best] + 600);
    }
}
