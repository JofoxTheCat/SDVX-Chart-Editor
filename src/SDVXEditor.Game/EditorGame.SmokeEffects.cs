using System.Globalization;
using osu.Framework.Screens;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Play;

namespace SDVXEditor.Game;

/// <summary>
/// Smoke part <c>effects</c> (needs <c>--open</c>): the VOX game effects authored through the REAL input path
/// (<see cref="SmokeInput"/>) on a copy of the song — camera tab → effects view, a ColorConversion click + drag in the
/// lane post lane (Mode + Strength rows), a LaneY tween click + drag, a lane colour, BAROFF + BAR in the bar lane, a
/// Live2D asset, right click deletes, a preset and an inspector edit, Ctrl+Z / Ctrl+Y; the camera preview and a
/// playtest show the negative (ColorConversion mode 2) and the lane slide; kson save + reload and a ksh round trip keep
/// every row. docs/VOX_EFFECTS.md.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> effectsSteps(string songFolder, int? slot, string? shots)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        string work = Path.Combine(smokeWorkRoot(shots), "smoke-song-effects", Path.GetFileName(songFolder.TrimEnd('\\', '/')));
        long q = 0, b = 240;
        int undoBefore = 0;
        string? chartPath = null;

        EditorScreen ed() => screenStack.CurrentScreen as EditorScreen ?? throw new InvalidOperationException("smoke test: editor not open");
        PlaytestScreen pt() => screenStack.CurrentScreen as PlaytestScreen ?? throw new InvalidOperationException("smoke test: playtest not open");
        CameraTabContent cam() => ed().CameraContent;
        EffectLanes lanes() => cam().FxLanes;
        VoxExtrasInfo vox() => ed().Session.Chart.VoxExtras;
        string? shot(string name) => shots != null ? "40" + name : null;
        string state() => string.Create(CultureInfo.InvariantCulture, $"{vox().PostEffects.Count} post rows [{string.Join(" ", vox().PostEffects.Select(p => $"{p.Name}.{p.Param}@{(p.Y - q) / (double)b:0.##}b s{p.Stage} {p.From}->{p.To}/{p.Length}"))}], ")
                              + string.Create(CultureInfo.InvariantCulture, $"{vox().Commands.Count} commands [{string.Join(" ", vox().Commands.Select(c => $"{c.Key}@{(c.Y - q) / (double)b:0.##}b {c.P1} {c.P2}/{c.Length}"))}], undo '{ed().Session.Commands.UndoDescription}'");

        // a point of a lane at q + beats·b, x = fraction of the lane (post lanes: the effect's column)
        Vector2 at(FxLane lane, double beats, double fx) =>
            lanes().ToScreenSpace(new Vector2(EffectLanes.XOf(lane, fx), lanes().YOfPulse(q + (long)Math.Round(beats * b))));
        Vector2 col(FxLane lane, double beats, string effect) =>
            lanes().ToScreenSpace(new Vector2(EffectLanes.PostColumnX(lane, effect), lanes().YOfPulse(q + (long)Math.Round(beats * b))));
        double fxOf(FxLane lane, double v) => EffectLaneEditor.FxOf(lane, v);

        (Action, double)[] click(Func<Vector2> to, MouseButton button = MouseButton.Left) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(to()), 50), (() => im.PressButton(button), 50), (() => im.ReleaseButton(button), 80),
        };
        (Action, double)[] drag(Func<Vector2> from, Func<Vector2> to) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(from()), 50), (() => im.PressButton(MouseButton.Left), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.5f)), 50), (() => im.MoveMouseTo(to()), 50),
            (() => im.ReleaseButton(MouseButton.Left), 80),
        };
        (Action, double)[] key(Key k, bool down) => new (Action, double)[] { (() => { if (down) im.PressKey(k); else im.ReleaseKey(k); }, 60) };
        (Action, double)[] combo(Key mod, Key k) => key(mod, true).Concat(key(k, true)).Concat(key(k, false)).Concat(key(mod, false)).ToArray();
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        return new List<(Action, string?)>
        {
            (() =>
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                Directory.CreateDirectory(work);
                foreach (string f in Directory.EnumerateFiles(songFolder)) File.Copy(f, Path.Combine(work, Path.GetFileName(f)));
                var session = EditorSession.Load(work, slot);
                chartPath = session.ChartPath;
                                var c = session.Chart;
                // the middle of the chart (the playtest then has notes and audio), unless the chart has effect rows: then after them
                long effectsEnd = c.VoxExtras.Commands.Select(r => r.Y + r.Length).Concat(c.VoxExtras.PostEffects.Select(r => r.Y + r.Length)).DefaultIfEmpty(-1).Max();
                int m = effectsEnd < 0 ? Math.Max(1, session.Timing.MeasureAt(c.LastEventPulse() / 2)) : session.Timing.MeasureAt(Math.Max(c.LastEventPulse(), effectsEnd)) + 2;
                q = session.Timing.MeasureStart(m);
                b = session.Timing.BeatLength(m);
                report(inv($"effects: copy in {work}, test stretch at measure {m + 1} (pulse {q}), {c.VoxExtras.PostEffects.Count} post rows and {c.VoxExtras.Commands.Count} commands in the chart"));
                pushAndWait(new EditorScreen(session));
            }, null),
            (() =>
            {
                var e = ed();
                e.SelectTab(EditorTab.Camera);
                cam().View.Value = CameraTabContent.EFFECTS_VIEW;
                e.Session.Commands.MarkSaved();
                undoBefore = e.Session.Commands.History.Count();
                // judgement line a beat before q, zoom so q + 4 beats is on screen
                float judgeY = lanes().DrawHeight - PlayfieldLayout.JUDGEMENT_FROM_BOTTOM;
                e.Playback.CurrentTime.Value = e.Session.PulseToMs(q - b);
                e.Zoom.Value = Math.Clamp((float)Math.Floor((judgeY - 40) / ((e.Session.PulseToMs(q + 4 * b) - e.Session.PulseToMs(q - b)) * PlayfieldLayout.PX_PER_MS) * 20) / 20,
                    e.Zoom.MinValue, 1f);
                im.UseParentInput = false;
            }, null),
            (() => expect(lanes().IsPresent && lanes().YOfPulse(q + 4 * b) > 0, inv($"effects: the effect lanes are shown and the test stretch is on screen (y {lanes().YOfPulse(q + 4 * b):0})")), shot("a-effects-view")),

            // ColorConversion in the lane stage: click in its column at q, drag to q + 2 beats = Mode + Strength 0 -> 100 over 2 beats
            (() =>
            {
                cam().FxEditor.ColorMode.Value = 2;
                inputSequence("effects: ColorConversion click + drag", then(drag(() => col(FxLane.PostLane, 0, PostEffects.ColorConversion), () => col(FxLane.PostLane, 2, PostEffects.ColorConversion)), check(() =>
                {
                    var rows = vox().PostEffects.Where(p => p.Y == q && p.Name == PostEffects.ColorConversion && p.Stage == 1).ToList();
                    expect(rows.Any(r => r.Param == "Mode" && r.From == 2) && rows.Any(r => r.Param == "Strength" && r.To == 100 && r.Length == 2 * b),
                        $"effects: a click + drag in the CC column adds Mode 2 and Strength 0 -> 100 over two beats ({state()})");
                    expect(cam().FxEditor.Selected is { Kind: VoxRowKind.PostEffect }, "effects: the new row is selected");
                })));
            }, shot("b-effects-negative")),

            // LaneY: click at q + 1 beat with value 0, drag to q + 2 beats at 512
            (() => inputSequence("effects: LaneY tween", then(drag(() => at(FxLane.LaneY, 1, fxOf(FxLane.LaneY, 0)), () => at(FxLane.LaneY, 2, fxOf(FxLane.LaneY, 512))), check(() =>
            {
                var r = vox().Commands.FirstOrDefault(c => c.Name == "LaneY" && c.Y == q + b);
                expect(r != null && VoxToken.Number(r.P1) == 0 && VoxToken.Number(r.P2) == 512 && r.Length == b, $"effects: LaneY 0 -> 512 over one beat ({state()})");
            }))), null),

            // lane colour, bar lines (click = BAROFF ON at the measure, Shift+click = BAR), an asset; right click deletes the BAR
            (() => inputSequence("effects: colour, bars, asset", then(
                click(() => at(FxLane.Colours, 1, 0.25)),
                click(() => at(FxLane.Bars, 0, 0.5)),
                key(Key.ShiftLeft, true), click(() => at(FxLane.Bars, 2, 0.5)), key(Key.ShiftLeft, false),
                click(() => at(FxLane.Assets, 3, 0.5)),
                check(() =>
                {
                    expect(vox().Commands.Any(c => c.Kind == "LANECOL" && c.Y == q + b), $"effects: a click in the colour lane adds LANECOL ({state()})");
                    expect(vox().Commands.Any(c => c.Name == "BAROFF" && c.P1 == "ON" && c.Y == q), $"effects: a click in the bar lane adds BAROFF ON at the measure ({state()})");
                    expect(vox().Commands.Any(c => c.Name == "BAR" && c.Y == q + 2 * b), $"effects: Shift+click adds a BAR line ({state()})");
                    expect(vox().Commands.Any(c => c.Kind == "LIVE2D" && c.Y == q + 3 * b), $"effects: a click in the asset lane adds the brush (LIVE2D) ({state()})");
                }),
                click(() => at(FxLane.Bars, 2, 0.5), MouseButton.Right),
                check(() => expect(!vox().Commands.Any(c => c.Name == "BAR" && c.Y == q + 2 * b), $"effects: right click deletes the BAR line ({state()})")))), shot("c-effects-lanes")),

            // undo / redo through the keyboard
            (() =>
            {
                int before = vox().Commands.Count;
                inputSequence("effects: Ctrl+Z / Ctrl+Y", then(combo(Key.ControlLeft, Key.Z), check(() =>
                        expect(vox().Commands.Count == before + 1, $"effects: Ctrl+Z brings the BAR back ({state()})")),
                    combo(Key.ControlLeft, Key.Y), check(() => expect(vox().Commands.Count == before, $"effects: Ctrl+Y deletes it again ({state()})"))));
            }, null),

            // a preset and an inspector edit
            (() =>
            {
                var e = ed();
                e.Playback.CurrentTime.Value = e.Session.PulseToMs(q + 4 * b);
                cam().AddPresetAtCursor("Blur hit", 1, 1);
                expect(vox().PostEffects.Any(p => p.Name == PostEffects.RadicalBlur && p.Y == q + 4 * b && p.From > 0), $"effects: the Blur hit preset adds RadicalBlur at the cursor ({state()})");
                cam().Field("fx_to").Commit("0.05");
                expect(vox().PostEffects.Any(p => p.Name == PostEffects.RadicalBlur && Math.Abs(p.To - 0.05) < 1e-9), $"effects: the inspector edits the selected row ({state()})");
                e.Playback.CurrentTime.Value = e.Session.PulseToMs(q - b);
            }, null),

            // a note script: template, assigned to the first BT chips after the stretch (compose selection), ksh2vox-valid
            (() =>
            {
                var e = ed();
                int id = cam().AddScript("slide in from the right");
                var chips = e.Session.Chart.Notes.Bt.SelectMany((l, k) => l.Where(n => n.Length == 0 && n.Y >= q).Select(n => new NoteRef(NoteKind.Bt, k, n))).OrderBy(n => n.Note.Y).Take(3).ToList();
                e.Session.Selection.Clear();
                foreach (var n in chips) e.Session.Selection.Add(n);
                int assigned = cam().AssignScriptToSelection(id);
                expect(chips.Count == 0 || assigned == chips.Count, inv($"effects: script {id} assigned to {assigned} of {chips.Count} selected chips"));
                expect(new SDVXEditor.Core.Validation.EffectChecks().Run(new SDVXEditor.Core.Validation.ChartCheckContext(e.Session.Chart)).All(i => !i.RuleId.StartsWith("effect.vox.script", StringComparison.Ordinal)),
                    "effects: the chart check finds no script problem");
                e.Session.Selection.Clear();
            }, null),

            // the camera preview: negative + lane slide at q + 2.5 beats
            (() =>
            {
                var e = ed();
                cam().View.Value = 0;
                e.Playback.CurrentTime.Value = e.Session.PulseToMs(q + 5 * b / 2);
            }, null),
            (() =>
            {
                var fx = cam().Preview.Effects;
                expect(fx.Lane.Conversions.Any(c => c.Mode == 2 && c.Strength >= 99.9) && Math.Abs(fx.LaneY - 512) < 1e-6,
                    inv($"effects: the camera preview shows the negative and LaneY 512 ({fx.Lane.Conversions.Count} conversions, LaneY {fx.LaneY})"));
            }, shot("d-effects-preview")),

            // playtest from q + 2 beats: the playfield takes the effects, the HUD does not
            (() =>
            {
                im.UseParentInput = true;
                var e = ed();
                e.Playback.CurrentTime.Value = e.Session.PulseToMs(q + 2 * b) + PlaytestScreen.LEAD_IN_MS * e.Playback.Rate.Value;
                e.Playtest();
                waitForPlaytest();
            }, null),
            (() => pt().SetAutoplay(true, persist: false), null),
            (() => waitUntil(() => pt().RunningMs >= 2400, "effects: playtest running", 30_000), null),
            (() =>
            {
                var fx = pt().Playfield.Effects;
                expect(fx.Lane.Conversions.Any(c => c.Mode == 2 && c.Strength >= 99.9), inv($"effects: the playtest applies the negative (song time {pt().Playfield.SongTime:0} ms)"));
                expect(Math.Abs(fx.LaneY - 512) < 1e-6, inv($"effects: the playtest slides the lane (LaneY {fx.LaneY})"));
                report(inv($"effects: playtest at {pt().Playfield.SongTime:0} ms: {fx.Lane.Conversions.Count} lane conversions, LaneY {fx.LaneY}, lane colour {fx.LaneColour:X8}, {pt().Playfield.LastQuadCount} quads"));
            }, shot("e-effects-playtest")),
            (() => pt().Exit(), null),
            (() => waitUntil(() => screenStack.CurrentScreen is EditorScreen, "effects: back in the editor", 20_000), null),

            // save, reload; ksh round trip
            (() =>
            {
                var e = ed();
                e.Session.Save();
                var again = EditorSession.Load(work, slot).Chart;
                var a = KsonWriter.Write(new Chart { VoxExtras = e.Session.Chart.VoxExtras });
                var bk = KsonWriter.Write(new Chart { VoxExtras = again.VoxExtras });
                expect(a == bk, "effects: kson save + reload keeps every effect row");
                var viaKsh = KshReader.Read(KshWriter.Write(e.Session.Chart)).VoxExtras;
                expect(viaKsh.PostEffects.Count == again.VoxExtras.PostEffects.Count && viaKsh.Commands.Count == again.VoxExtras.Commands.Count,
                    inv($"effects: a ksh export keeps the effect rows ({viaKsh.PostEffects.Count}/{again.VoxExtras.PostEffects.Count} post, {viaKsh.Commands.Count}/{again.VoxExtras.Commands.Count} commands)"));
                report(inv($"effects: saved {e.Session.ChartPath}, {again.VoxExtras.PostEffects.Count} post rows, {again.VoxExtras.Commands.Count} commands, {e.Session.Commands.History.Count() - undoBefore} undo steps"));
                e.Exit();
            }, null),
            (() => waitUntil(() => screenStack.CurrentScreen is not EditorScreen, "effects: editor closed", 20_000), null),
        };
    }
}
