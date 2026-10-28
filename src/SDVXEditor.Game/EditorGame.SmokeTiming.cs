using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Timing;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Dialogs;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke-test part for the timing tab, F6 and Song Setup on the scratch copy of the <c>--open</c>
/// song: a BPM change with "keep notes in time", a scroll-speed ramp through the Property
/// Transformer, a metadata change in Song Setup, undo/redo of each, save, reload, compare.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> timingEditingSteps(string work, Func<EditorScreen> ed)
    {
        string before = "";
        double oldBpm = 0, newBpm = 0;
        List<(long Pulse, double Ms)> notesBefore = new();
        int undoDepth = 0;
        long rampFrom = 0, rampTo = 0;
        string newTitle = "";

        List<(long, double)> btNotes(EditorSession s) =>
            s.Chart.Notes.Bt.SelectMany(l => l).Select(n => (n.Y, s.PulseToMs(n.Y))).OrderBy(x => x.Item1).ToList();

        TimingControlPointsDialog f6() => dialogOverlay.Current as TimingControlPointsDialog ?? throw new InvalidOperationException("smoke test: F6 not open");

        return new List<(Action, string?)>
        {
            // ---- BPM with "keep notes in time" (re-snapped to 1/16)
            (() =>
            {
                var e = ed();
                var s = e.Session;
                e.SelectTab(EditorTab.Timing);
                e.Playback.CurrentTime.Value = s.PulseToMs(s.Timing.MeasureStart(4));
                before = KsonWriter.Write(s.Chart);
                notesBefore = btNotes(s);
                undoDepth = s.Commands.History.Count();
                oldBpm = e.Timing.ActiveBpmPoint()!.Bpm;
                newBpm = Math.Round(oldBpm * 1.02, 3);

                e.Timing.MoveNotesWithTiming.Value = false;
                e.Timing.SnapNotes.Value = true;
                e.Timing.SnapDivisor.Value = 16;
                e.Timing.Bpm.Value = newBpm; // the stepper's write path (one click)

                expect(Math.Abs(e.Timing.ActiveBpmPoint()!.Bpm - newBpm) < 1e-9, "BPM stepper wrote the active BPM point");
                expect(s.Commands.History.Count() == undoDepth + 1, "BPM change is one undo step");

                // every note after the change keeps its time within half a 1/16 step of the new tempo
                var after = btNotes(s);
                // keep time + snap: a note may move by at most half a 1/16 step (7.5 pulses) plus one pulse of
                // grid rounding, measured in ms at the tempo where it lands (charts with many BPM changes differ per note)
                var map = s.Timing;
                double worstExcess = double.MinValue, worst = 0, limitAtWorst = 0;
                foreach (var (o, n) in notesBefore.Zip(after))
                {
                    double drift = Math.Abs(o.Ms - n.Item2);
                    double limit = Math.Max(map.PulseToMs(n.Item1 + 8.5) - n.Item2, n.Item2 - map.PulseToMs(n.Item1 - 8.5)) + 0.5;
                    if (drift - limit > worstExcess)
                    {
                        worstExcess = drift - limit;
                        worst = drift;
                        limitAtWorst = limit;
                    }
                }

                double halfStep = limitAtWorst;
                expect(worstExcess <= 0, $"keep-time: notes stay within half a 1/16 step of their time (worst {worst:0.0} ms, limit there {limitAtWorst:0.0})");
                expect(after.Any(a => notesBefore.All(b => b.Pulse != a.Item1)) || notesBefore.Count == 0, "keep-time moved notes to new pulses");
                report($"timing: BPM {oldBpm} -> {newBpm} with keep-time + 1/16 snap, {after.Count} BT notes, worst drift {worst:0.0} ms (limit {halfStep:0.0}) - ok");

                e.TriggerAction(Screens.Edit.Input.EditorAction.Undo);
                expect(KsonWriter.Write(s.Chart) == before, "undo restores the chart exactly");
                expect(Math.Abs(e.Timing.Bpm.Value - oldBpm) < 1e-9, "the stepper shows the old BPM again after undo");
                e.TriggerAction(Screens.Edit.Input.EditorAction.Redo);
                expect(Math.Abs(e.Timing.ActiveBpmPoint()!.Bpm - newBpm) < 1e-9, "redo");

                // a stepper drag is one transaction however many values it passes
                int depth = s.Commands.History.Count();
                e.Timing.BeginGesture("Change offset");
                for (int i = 1; i <= 5; i++) e.Timing.Offset.Value = s.Chart.Audio.Bgm.OffsetMs + 2;
                e.Timing.EndGesture();
                expect(s.Commands.History.Count() == depth + 1, "an offset drag is one undo step");
                e.TriggerAction(Screens.Edit.Input.EditorAction.Undo);
                expect(s.Commands.History.Count() == depth, "and undoes as one");
                e.Timing.MoveNotesWithTiming.Value = true;
                e.Timing.MetronomeClicks.Value = true;
                e.Playback.IsPlaying.Value = true;
                // at least one beat must pass (a slow song has 500 ms and more between clicks)
                waitUntil(() => e.TapBlock.Clicks > 0, "a metronome click while the timing tab plays", 5000);
            }, null),
            (() => { }, "20-timing-tab-metronome"),
            (() =>
            {
                var e = ed();
                e.Playback.IsPlaying.Value = false;
                expect(e.TapBlock.Clicks > 0, "metronome clicks played while the timing tab played");
                report($"timing: metronome lamps + {e.TapBlock.Clicks} clicks while playing - ok");

                // tap tempo: 4 taps 500 ms apart, applied with the confirm button path
                // checked on the point by its pulse: with "move notes with timing" the faster tempo shifts the beats
                // under the (unchanged) cursor time, so on a chart with a BPM change shortly after (Canto IX) the
                // cursor then lies past it and "the active point" is the next one - depending on how far it played
                long tapY = e.Timing.ActiveBpmPoint()!.Y;
                double bpmAt(long y) => e.Session.Chart.Beat.Bpm.Single(b => b.Y == y).Bpm;
                for (int i = 0; i < 4; i++) e.TapBlock.Tap(10_000 + i * 500);
                expect(e.TimingContent.ApplyTap(), "4 taps at 120 BPM applied to the active BPM point");
                expect(Math.Abs(bpmAt(tapY) - 120) < 1e-9, $"tapped BPM written to the point at {tapY} ({bpmAt(tapY)})");
                e.TriggerAction(Screens.Edit.Input.EditorAction.Undo);
                expect(Math.Abs(bpmAt(tapY) - newBpm) < 1e-9, "tap undone");
            }, null),

            // ---- F6 + Property Transformer: scroll-speed ramp
            (() => ed().OpenTimingSetup(), null),
            (() => f6().ShowListTab(2), null),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                var dialog = f6();
                double from = s.PulseToMs(s.Timing.MeasureStart(8));
                double to = s.PulseToMs(s.Timing.MeasureStart(10));
                rampFrom = s.Timing.MeasureStart(8);
                rampTo = s.Timing.MeasureStart(10);
                e.Playback.CurrentTime.Value = from;
                var pt = dialog.OpenPropertyTransformer();
                pt.FromMs.Value = Math.Round(from);
                pt.ToMs.Value = Math.Round(to);
                pt.Mode.Value = PropertyTransformerDialog.Tool.Ramp;
                pt.StartValue.Value = 1;
                pt.EndValue.Value = 2;
                pt.Shape.Value = RampShape.Exponential;
                pt.Steps.Value = 4;
            }, "22-property-transformer"),
            (() =>
            {
                var pt = dialogOverlay.Current as PropertyTransformerDialog ?? throw new InvalidOperationException("smoke test: transformer not open");
                pt.Accept();
                var dialog = f6();
                var ramp = dialog.Draft.Beat.ScrollSpeed.Where(p => p.Y >= rampFrom - 2 && p.Y <= rampTo + 2).ToList();
                expect(ramp.Count == 5, $"the ramp put 5 points into the draft ({ramp.Count})");
                expect(Math.Abs(ramp[2].V - Math.Sqrt(2)) < 0.01, "exponential midpoint x1.41");
                expect(dialog.SelectedSources.Count == 5, "the new points are selected in the list");
            }, "21-f6-after-ramp"),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                int depth = s.Commands.History.Count();
                var dialog = f6();
                dialog.Accept();
                expect(dialog.Applied, "F6 OK applied the draft");
                expect(s.Commands.History.Count() == depth + 1, "F6 OK is one undo step");
                var pts = s.Chart.Beat.ScrollSpeed.Where(p => p.Y >= rampFrom - 2 && p.Y <= rampTo + 2).ToList();
                expect(pts.Count == 5 && Math.Abs(pts[^1].V - 2) < 1e-9, "the ramp is in the chart");
                report($"F6: Property Transformer exponential ramp x1 -> x2 over measures 9-10 (5 points), OK as one undo step - ok");
                e.TriggerAction(Screens.Edit.Input.EditorAction.Undo);
                expect(!s.Chart.Beat.ScrollSpeed.Any(p => p.Y > rampFrom && p.Y < rampTo), "undo removed the ramp");
                e.TriggerAction(Screens.Edit.Input.EditorAction.Redo);
            }, null),

            // ---- Song Setup
            (() => ed().OpenSongSetup(), null),
            (() =>
            {
                var d = dialogOverlay.Current as SongSetupDialog ?? throw new InvalidOperationException("smoke test: Song Setup not open");
                newTitle = d.Edit.Title.Value + " (smoke edit)";
                d.Edit.Title.Value = newTitle;
                d.Edit.ArtistYomigana.Value = "SMOKE ARTIST";
                d.ShowPage(1);
                expect(d.Edit.Estimate.Value?.EstimatedLevel != null, "the level estimator ran");
                double level = d.Edit.Level.Value;

                // tenths from 17 up, whole levels below (Johannes): 17 -0.1 -> 16, 16 +0.1 -> 17, 17 +0.1 -> 17.1
                d.Edit.Level.Value = 17;
                d.Edit.Level.Value = 16.9;
                expect(d.Edit.Level.Value == 16, "a 0.1 step down from 17 lands on 16");
                d.Edit.Level.Value = 16.1;
                expect(d.Edit.Level.Value == 17, "a 0.1 step up from 16 lands on 17");
                d.Edit.Level.Value = 17.1;
                expect(Math.Abs(d.Edit.Level.Value - 17.1) < 1e-9, "17.1 is kept");
                expect(Difficulty.FormatLevel(18) == "18.0" && Difficulty.FormatLevel(15) == "15", "level display rule");
                d.Edit.Level.Value = level;

                d.UseEstimate();
                double est = d.Edit.Estimate.Value!.EstimatedLevel!.Value;
                expect(d.Edit.Level.Value == Math.Clamp(Screens.Edit.SongSetupViewModel.SnapLevel(est), 1, 20), "Use estimate rounds like the game shows levels");
                expect(Math.Abs(ed().Session.Chart.Meta.Level - level) < 1e-9, "Use estimate does not write the chart by itself");
                report($"song setup: estimate {d.Edit.Estimate.Value!.EstimatedLevel:0.0} (band +-{d.Edit.Estimate.Value.Band:0.0}), declared {level}");
            }, "23-song-setup-estimate"),
            (() =>
            {
                var e = ed();
                var s = e.Session;
                var d = (SongSetupDialog)dialogOverlay.Current!;
                int depth = s.Commands.History.Count();
                d.Accept();
                expect(d.Applied && s.Commands.History.Count() == depth + 1, "Song Setup OK is one undo step");
                expect(s.Chart.Meta.Title == newTitle && s.Chart.Meta.ArtistTranslit == "SMOKE ARTIST", "metadata written");
                expect(e.TitleText.EndsWith(" *", StringComparison.Ordinal) && e.TitleText.Contains(newTitle), "window title shows the dirty chart");
                e.TriggerAction(Screens.Edit.Input.EditorAction.Undo);
                expect(s.Chart.Meta.Title != newTitle, "undo restores the title");
                e.TriggerAction(Screens.Edit.Input.EditorAction.Redo);
                expect(s.Chart.Meta.Title == newTitle, "redo");
                report($"song setup: title/romanised artist/level via OK, undo/redo, title bar \"{e.TitleText}\" - ok");
            }, null),

            // ---- save, reload, compare
            (() =>
            {
                var e = ed();
                var s = e.Session;
                e.TriggerAction(Screens.Edit.Input.EditorAction.Save);
                expect(!s.IsDirty && !e.TitleText.EndsWith("*", StringComparison.Ordinal), "saved, title bar clean");
                var again = SongFolder.Load(work).Charts.Single(c => string.Equals(c.FilePath, s.ChartPath, StringComparison.OrdinalIgnoreCase)).Chart;
                expect(KsonWriter.Write(again) == KsonWriter.Write(s.Chart), "reloaded chart equals the edited chart (kson text)");
                expect(again.Meta.Title == newTitle, "title survived");
                expect(new TimingMap(again.Beat).BpmAt(0) > 0 && again.Beat.Bpm.Any(b => Math.Abs(b.Bpm - newBpm) < 1e-9), "BPM survived");
                expect(again.Beat.ScrollSpeed.Count(p => p.Y >= rampFrom - 2 && p.Y <= rampTo + 2) == 5, "ramp survived");
                report("timing/setup: save + reload identical - ok");
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(work)!, "smoke-report.txt"), smokeReport.ToString());
            }, null),
        };
    }
}
