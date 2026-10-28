using System.Globalization;
using osu.Framework.Configuration;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game;

/// <summary>
/// Collab smoke, compose tools through the REAL input path: mouse positions and buttons are injected into the root
/// input manager (where the window's input arrives) at screen points computed from a target lane / laser value, and
/// the placed object's lane / value is asserted. Guards against mouse → local mapping bugs (the overhang padding
/// once moved every placement 100 px right of the cursor, unseen by the steps that call the tools directly).
/// Runs without and with a reference difficulty open.
/// </summary>
public partial class EditorGame
{
    /// <summary>The compose input checks borderless (screen size) and windowed 1600x900; the window mode/size is restored after.</summary>
    private List<(Action, string?)> composeInputAtSizes(Func<EditorScreen> ed, string label, bool reference)
    {
        var mode = frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode);
        var size = frameworkConfig.GetBindable<System.Drawing.Size>(FrameworkSetting.WindowedSize);
        WindowMode modeBefore = mode.Value;
        System.Drawing.Size sizeBefore = size.Value;

        (Action, string?) resizeTo(string what, Action resize) => (() =>
        {
            var before = DrawSize;
            double t0 = Clock.CurrentTime;
            resize();
            // a mode switch takes a few frames; when the size stays (already that size) go on after 2.5 s
            waitUntil(() => Clock.CurrentTime - t0 > 1200 && (DrawSize != before || Clock.CurrentTime - t0 > 2500), $"the window to become {what}", 5000);
        }, null);

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                modeBefore = mode.Value;
                sizeBefore = size.Value;
            }, null),
            resizeTo("borderless", () => mode.Value = WindowMode.Borderless),
        };
        steps.AddRange(composeInputSteps(ed, label + ", borderless").Select(x => (x.Act, x.Shot)));
        if (reference) steps.AddRange(referenceInputSteps(ed).Select(x => (x.Act, x.Shot)));
        steps.Add(resizeTo("windowed 1600x900", () =>
        {
            mode.Value = WindowMode.Windowed;
            size.Value = new System.Drawing.Size(1600, 900);
        }));
        steps.AddRange(composeInputSteps(ed, label + ", windowed 1600x900").Select(x => (x.Act, x.Shot)));
        if (reference) steps.AddRange(referenceInputSteps(ed).Select(x => (x.Act, x.Shot)));
        steps.Add(resizeTo("as before", () =>
        {
            mode.Value = modeBefore;
            size.Value = sizeBefore;
        }));
        return steps;
    }

    private List<(Action Act, string? Shot)> composeInputSteps(Func<EditorScreen> ed, string label)
    {
        long p = 0, beat = 240;
        string before = "";
        float zoomBefore = 1;
        int divisorBefore = 4;

        Vector2 screen(long pulse, float x) => ed().Playfield.ToScreenSpace(new Vector2(x, ed().Playfield.YOfPulse(pulse)));
        float lane(NoteKind kind, int l) => PlayfieldLayout.LaneLeft(kind, l) + PlayfieldLayout.LaneWidth(kind) / 2;
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        void button(MouseButton b, bool down)
        {
            if (down) im.PressButton(b);
            else im.ReleaseButton(b);
        }

        void inputMoveTo(Vector2 screenSpace) => im.MoveMouseTo(screenSpace);
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        (Action, double)[] click(Func<Vector2> at, MouseButton b = MouseButton.Left) => new (Action, double)[]
        {
            (() => inputMoveTo(at()), 40),
            (() => button(b, true), 50),
            (() => button(b, false), 60),
        };

        (Action, double)[] drag(Func<Vector2> from, Func<Vector2> to) => new (Action, double)[]
        {
            (() => inputMoveTo(from()), 40),
            (() => button(MouseButton.Left, true), 40),
            (() => inputMoveTo(Vector2.Lerp(from(), to(), 0.3f)), 40),
            (() => inputMoveTo(Vector2.Lerp(from(), to(), 0.7f)), 40),
            (() => inputMoveTo(to()), 40),
            (() => button(MouseButton.Left, false), 60),
        };

        Note? noteAt(NoteKind kind, int l, long y) => ed().Session.Chart.Notes.Lane(kind, l).FirstOrDefault(n => n.Y == y);
        string hover() => (ed().Compose.Hover is { } h ? inv($"hover x {h.X:0.0} ms {h.Ms:0}") : "no hover")
                          + inv($"; p {p} = {ed().Session.PulseToMs(p):0} ms; notes {ed().Session.Chart.Notes.Bt.Sum(l => l.Count)}; toast '{ed().LastToast}'; tool {ed().Tools.EffectiveTool.Value}; lock {ed().Compose.LockNotes.Value}; undo {ed().Session.Commands.UndoDescription}; ") ;

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                var e = ed();
                var s = e.Session;
                p = s.Timing.MeasureStart(s.Timing.MeasureAt(s.Chart.LastEventPulse()) + 4);
                beat = s.Timing.BeatLength(s.Timing.MeasureAt(p));
                e.Playback.LengthMs = s.PulseToMs(p + 64 * beat);
                e.Compose.GridSnap.Value = true;
                e.Compose.WideLaser.Value = false;
                divisorBefore = e.Compose.Divisor.Value;
                e.Compose.Divisor.Value = 4;
                e.SelectTool(EditorTool.BtChip);
                before = KsonWriter.Write(s.Chart);
                im.UseParentInput = false; // injected input, independent of where the real cursor is

                // The whole stretch (p - 1 .. p + 14 units) must be on screen: the clock sits on p - 1 unit (judgement
                // line), the zoom is chosen so p + 14 units stays 20 px below the top. The playfield cannot zoom out
                // below Zoom.MinValue, so for slow songs (Canto IX: 70-97 BPM) the unit shrinks from a beat to half /
                // a quarter beat (still on the 1/4 grid set above) until the stretch fits.
                zoomBefore = e.Zoom.Value;
                float judgeY = e.Playfield.DrawHeight - PlayfieldLayout.JUDGEMENT_FROM_BOTTOM;
                float zoomFor(long unit) => (float)((judgeY - 20) / ((s.PulseToMs(p + 14 * unit) - s.PulseToMs(p - unit)) * PlayfieldLayout.PX_PER_MS));
                for (int k = 0; k < 2 && zoomFor(beat) < e.Zoom.MinValue && beat % 2 == 0; k++) beat /= 2;
                e.Playback.CurrentTime.Value = s.PulseToMs(p - beat);
                e.Zoom.Value = Math.Clamp((float)Math.Floor(zoomFor(beat) * 20) / 20, e.Zoom.MinValue, 1f);
                float yTop = e.Playfield.YOfPulse(p + 14 * beat), yBottom = e.Playfield.YOfPulse(p - beat);
                expect(yTop > 0 && yBottom < e.Playfield.DrawHeight, inv($"{label}: the test stretch is on screen (y {yTop:0}..{yBottom:0} of {e.Playfield.DrawHeight:0}, time {e.Playback.CurrentTime.Value:0} ms)"));
                var sz = e.Playfield.ToScreenSpace(Vector2.Zero);
                report(inv($"compose-input ({label}): window draw size {DrawSize.X:0}x{DrawSize.Y:0}, playfield local 0 at screen {sz.X:0.0}, zoom {e.Zoom.Value:0.00}"));
            }, null),

            // BT chips on all four lanes, FX chip on FX-R: the cursor ghost under the mouse, the chip in that lane
            (() => inputSequence($"{label}: BT chips by mouse", Enumerable.Range(0, 4).SelectMany(l => click(() => screen(p + l * beat, lane(NoteKind.Bt, l)))).Append((() =>
            {
                var e = ed();
                for (int l = 0; l < 4; l++)
                    expect(noteAt(NoteKind.Bt, l, p + l * beat) != null, inv($"{label}: a click on BT lane {l} centre placed a chip in lane {l} at its pulse (lanes there: {string.Join(", ", Enumerable.Range(0, 4).Select(k => e.Session.Chart.Notes.Bt[k].Count(n => n.Y >= p)))}; {hover()})"));
                expect(e.Compose.Hover is { } h && Math.Abs(h.X - lane(NoteKind.Bt, 3)) < 1.5f, inv($"{label}: the tool's pointer x equals the mouse ({hover()} vs {lane(NoteKind.Bt, 3):0.0})"));
                e.SelectTool(EditorTool.FxChip);
            }, 0d)).ToArray()), null),
            (() => inputSequence($"{label}: FX chip by mouse", click(() => screen(p + 5 * beat, lane(NoteKind.Fx, 1))).Append((() =>
            {
                expect(noteAt(NoteKind.Fx, 1, p + 5 * beat) != null && noteAt(NoteKind.Fx, 0, p + 5 * beat) == null, $"{label}: a click on FX-R placed an FX-R chip ({hover()})");
                ed().SelectTool(EditorTool.Laser);
                ed().Tools.LaserSide.Value = 0;
            }, 0d)).ToArray()), null),

            // normal laser 0.3 → 0.7, then wide 0.06 → 0.94 drawn from the overhang on both sides
            (() => inputSequence($"{label}: laser by mouse", drag(() => screen(p + 8 * beat, PlayfieldLayout.LaserX(0.3, false)), () => screen(p + 10 * beat, PlayfieldLayout.LaserX(0.7, false))).Append((() =>
            {
                var sec = ed().Session.Chart.Notes.Lasers[0].FirstOrDefault(x => x.Y == p + 8 * beat);
                expect(sec != null && Math.Abs(sec.Points[0].V - 0.3) < 1e-9 && Math.Abs(sec.Points[^1].V - 0.7) < 1e-9 && sec.EndY == p + 10 * beat,
                    inv($"{label}: a mouse-drawn laser runs 0.3 -> 0.7 ({(sec == null ? "none" : $"{sec.Points[0].V} -> {sec.Points[^1].V}")}; {hover()})"));
                ed().Compose.WideLaser.Value = true;
            }, 0d)).ToArray()), null),
            (() => inputSequence($"{label}: wide laser past both edges by mouse", drag(() => screen(p + 12 * beat, PlayfieldLayout.LaserX(0.04, true)), () => screen(p + 14 * beat, PlayfieldLayout.LaserX(0.94, true))).Append((() =>
            {
                var sec = ed().Session.Chart.Notes.Lasers[0].FirstOrDefault(x => x.Y == p + 12 * beat);
                expect(sec is { Width: 2 } && Math.Abs(sec.Points[0].V - 0.04) < 1e-9 && Math.Abs(sec.Points[^1].V - 0.94) < 1e-9,
                    inv($"{label}: a wide laser drawn from the left overhang to the right one runs 0.04 -> 0.94 ({(sec == null ? "none" : $"w{sec.Width} {sec.Points[0].V} -> {sec.Points[^1].V}")}; {hover()})"));
                ed().Compose.WideLaser.Value = false;
            }, 0d)).ToArray()), null),

            // right-click delete (BT lane 2), box select, drag-move (whole selection one beat later)
            (() => inputSequence($"{label}: right-click delete", click(() => screen(p + 2 * beat, lane(NoteKind.Bt, 2)), MouseButton.Right).Append((() =>
            {
                expect(noteAt(NoteKind.Bt, 2, p + 2 * beat) == null && noteAt(NoteKind.Bt, 1, p + beat) != null, $"{label}: right click deleted exactly the chip under the mouse");
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
            }, 0d)).ToArray()), null),
            (() => inputSequence($"{label}: box select", drag(() => screen(p - beat / 2, PlayfieldLayout.BT_LEFT + 4), () => screen(p + 3 * beat + beat / 2, PlayfieldLayout.BT_LEFT + 2 * PlayfieldLayout.BT_LANE - 4)).Append((() =>
            {
                var sel = ed().Session.Selection;
                var lanes = sel.Notes.Where(n => n.Kind == NoteKind.Bt).Select(n => n.Lane).OrderBy(l => l).ToList();
                expect(lanes.SequenceEqual(new[] { 0, 1 }), $"{label}: the box over BT-A/B selected exactly lanes 0 and 1 ({string.Join(",", lanes)}; {hover()})");
            }, 0d)).ToArray()), null),
            (() => inputSequence($"{label}: drag-move", drag(() => screen(p, lane(NoteKind.Bt, 0)), () => screen(p + beat, lane(NoteKind.Bt, 0))).Append((() =>
            {
                expect(noteAt(NoteKind.Bt, 0, p + beat) != null && noteAt(NoteKind.Bt, 1, p + 2 * beat) != null && noteAt(NoteKind.Bt, 0, p) == null,
                    $"{label}: dragging a selected chip moved the selection one beat ({hover()})");
            }, 0d)).ToArray()), null),
        };

        steps.AddRange(holdInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeComposeHolds.cs
        steps.AddRange(laserPathInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeLaserPath.cs
        steps.AddRange(wideAnchorInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeComposeHolds.cs
        steps.AddRange(holdModifierInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeHoldModifier.cs
        steps.AddRange(holdTailInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeHoldTail.cs
        steps.AddRange(slamEndInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeSlamEnds.cs
        steps.AddRange(classicLaserInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeClassicLaser.cs
        steps.AddRange(laserDragInputSteps(ed, label, () => p, () => beat, screen)); // EditorGame.SmokeLaserDrag.cs

        steps.Add(
            (() =>
            {
                var e = ed();
                for (int i = 0; i < 80 && KsonWriter.Write(e.Session.Chart) != before; i++)
                    e.TriggerAction(Screens.Edit.Input.EditorAction.Undo);
                expect(KsonWriter.Write(e.Session.Chart) == before, $"{label}: undo restores the chart");
                e.Session.Selection.Clear();
                im.UseParentInput = true;
                e.Zoom.Value = zoomBefore;
                e.Compose.Divisor.Value = divisorBefore;
                report($"compose-input ({label}): BT chips on 4 lanes, FX-R chip, laser 0.3->0.7, wide 0.04->0.94 through both overhangs, right-click delete, box select, drag-move, chip <-> hold, laser path placement/menu/anchor drag, wide anchors in the overhang, hold modifier Ctrl/Alt, hold tail drag, slams as two points, classic lasers, laser drags / right-click last anchor / wide toggle / Bezier controls - all under the mouse - ok");
            }, null));
        return steps;
    }

    /// <summary>Real-input click on a note of the reference playfield selects that note (same lane) in the reference.</summary>
    private List<(Action Act, string? Shot)> referenceInputSteps(Func<EditorScreen> ed)
    {
        NoteRef? target = null;
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        void button(bool down)
        {
            if (down) im.PressButton(MouseButton.Left);
            else im.ReleaseButton(MouseButton.Left);
        }

        void inputMoveTo(Vector2 screenSpace) => im.MoveMouseTo(screenSpace);

        return new List<(Action, string?)>
        {
            (() =>
            {
                var e = ed();
                var s = e.ReferenceSession ?? throw new InvalidOperationException("smoke test: no reference open");
                var n = s.Chart.Notes.Bt.SelectMany((l, i) => l.Where(x => x.Length == 0).Select(x => new NoteRef(NoteKind.Bt, i, x))).OrderBy(r => r.Note.Y).Skip(5).First();
                target = n;
                e.Playback.CurrentTime.Value = s.PulseToMs(n.Note.Y) + 150;
                s.Selection.Clear();
                im.UseParentInput = false;
            }, null),
            (() =>
            {
                var e = ed();
                var t = target!;
                var pf = e.ReferencePlayfield!;
                Vector2 at() => pf.ToScreenSpace(new Vector2(PlayfieldLayout.LaneLeft(NoteKind.Bt, t.Lane) + PlayfieldLayout.BT_LANE / 2, pf.YOfPulse(t.Note.Y)));
                inputSequence("reference: click a note", (() => inputMoveTo(at()), 40), (() => button(true), 50), (() => button(false), 60), (() =>
                {
                    var sel = e.ReferenceSession!.Selection;
                    expect(sel.Count == 1 && sel.Contains(t), $"a mouse click on a reference note (lane {t.Lane}) selected exactly it ({string.Join(",", sel.Notes.Select(r => r.Lane))})");
                    im.UseParentInput = true;
                    report(FormattableString.Invariant($"compose-input (reference, window {DrawSize.X:0}x{DrawSize.Y:0}): a click on lane {t.Lane} selected that note - ok"));
                }, 0d));
            }, null),
        };
    }
}
