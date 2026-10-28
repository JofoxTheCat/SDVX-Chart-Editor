using System.Globalization;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Compose-input smoke, slams as two points through REAL input on laser L (units 2 and 5..7 of the test stretch, free
/// there): two clicks on one beat + right click place a slam (also a small one whose second click lies inside the first
/// anchor's handle); in select mode each end is hovered and selected on its own, Ctrl+click selects both; the landing
/// end drags its value, the start end drags value and time (each one undo step, the selected end stays selected); Delete
/// with one end selected leaves a plain point at the other value, undo restores it; the right-click menu deletes the
/// start end; a box over one end selects that end and Ctrl+C writes the slam's timestamp. Everything is undone at the
/// end (the chart equals the state before the part).
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> slamEndInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                                Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        bool shots = label.StartsWith("no reference, borderless", StringComparison.Ordinal);
        string? shot(string name) => shots ? "13-slam-" + name : null;
        long p = 0, u = 240, q = 0;
        string before = "";
        int undoSteps = 0;

        float x(double v) => PlayfieldLayout.LaserX(v, false);
        Vector2 at(long pulse, double v) => screen(pulse, x(v));

        (Action, double)[] move(Func<Vector2> to) => new (Action, double)[] { (() => im.MoveMouseTo(to()), 70) };

        (Action, double)[] click(Func<Vector2> to, MouseButton b = MouseButton.Left) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(to()), 50),
            (() => im.PressButton(b), 50),
            (() => im.ReleaseButton(b), 70),
        };

        (Action, double)[] drag(Func<Vector2> from, Func<Vector2> to) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(from()), 50),
            (() => im.PressButton(MouseButton.Left), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.4f)), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.8f)), 50),
            (() => im.MoveMouseTo(to()), 50),
            (() => im.ReleaseButton(MouseButton.Left), 70),
        };

        (Action, double)[] key(Key k, bool down) => new (Action, double)[] { (() => { if (down) im.PressKey(k); else im.ReleaseKey(k); }, 60) };
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        var chart = new Func<Chart>(() => ed().Session.Chart);
        var sel = new Func<Selection>(() => ed().Session.Selection);
        // the slam section on laser L: the one whose slam (or former slam) sits in the part's stretch
        LaserSection? slamSection() => chart().Notes.Lasers[0].FirstOrDefault(s => s.Y >= p + 4 * u && s.Y <= q + u / 2);
        LaserPoint? slam() => slamSection()?.Points.FirstOrDefault(pt => pt.IsSlam);
        long slamPulse() => slamSection() is { } s && slam() is { } pt ? s.Y + pt.Ry : q;
        Vector2 startEnd() => at(slamPulse(), slam()?.V ?? 0);
        Vector2 landingEnd() => at(slamPulse(), slam()?.Vf ?? 0);

        string pts(LaserSection? s) => s == null ? "none" : string.Join(" ", s.Points.Select(pt => inv($"[{(double)(s.Y + pt.Ry - p) / u:0.##}u {pt.V}{(pt.IsSlam ? $">{pt.Vf}" : "")}]")));
        string ends() => string.Join(",", sel().SlamEnds.Select(e => e.End ? "end" : "start")) + (sel().LaserPoints.Count > 0 ? $" +{sel().LaserPoints.Count} points" : "");
        string state()
        {
            var c = ed().Compose;
            string hov = c.HoveredAnchor is { } h ? inv($"{h.Index}{(h.Vf ? " end" : " start")}") : "-";
            return inv($"laser L {pts(slamSection())}; selection {ends()}; hovered anchor {hov}; active {(c.ActiveLaser is { } a ? a.Section.Y.ToString(CultureInfo.InvariantCulture) : "-")}; placement {c.Placement?.Path.Count.ToString() ?? "none"}; tool {ed().Tools.EffectiveTool.Value}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");
        }

        void undo()
        {
            ed().TriggerAction(EditorAction.Undo);
            undoSteps--;
        }

        bool isEnd(LaserSlamEndRef e, bool end) => e.End == end && e.Point.IsSlam && e.Section == slamSection();

        return new List<(Action, string?)>
        {
            (() =>
            {
                p = start();
                u = unit();
                q = p + 6 * u;
                undoSteps = 0;
                before = KsonWriter.Write(chart());
                expect(chart().Notes.Lasers[0].All(s => s.EndY < p + 2 * u || s.Y > q + u), $"{label}: laser L is free at units 2..7 for the slam steps ({state()})");
                ed().SelectTool(EditorTool.Laser);
                ed().Tools.LaserSide.Value = 0;
                ed().Compose.WideLaser.Value = false;
                ed().Session.Selection.Clear();
            }, null),

            // a small slam: the second click lies inside the first anchor's handle radius and still makes the slam
            (() => inputSequence($"{label}: small slam by two clicks", then(click(() => at(p + 2 * u, 0.5)), click(() => at(p + 2 * u, 0.52)), check(() =>
            {
                var pl = ed().Compose.Placement;
                expect(pl != null && pl.Path.Count == 1 && pl.Path.Anchors[0] is { IsSlam: true, V: 0.5 } a && Math.Abs(a.Vf!.Value - 0.52) < 1e-9,
                    inv($"{label}: a second click {x(0.52) - x(0.5):0.0} px beside the first on the same beat makes a slam, not a type cycle ({state()})"));
            }), click(() => at(p + 2 * u, 0.52), MouseButton.Right), check(() =>
            {
                var s = chart().Notes.Lasers[0].FirstOrDefault(l => l.Y == p + 2 * u);
                expect(s != null && s.Points.Count == 1 && s.Points[0].IsSlam && ed().Session.Commands.UndoDescription == "Add laser",
                    $"{label}: right click put the small slam into the chart as a lone slam (no tail), one undo step ({pts(s)}; {state()})");
                undoSteps++;
                undo();
                expect(chart().Notes.Lasers[0].All(l => l.Y != p + 2 * u), $"{label}: one Ctrl+Z removes it ({state()})");
            }))), null),

            // two clicks on one beat, the pointer still on the landing end: the preview shows the slam; right click finishes
            (() => inputSequence($"{label}: slam by two clicks", then(click(() => at(q, 0.2)), click(() => at(q, 0.8)), move(() => at(q, 0.8)), check(() =>
            {
                var pv = ed().Compose.PlacementPreview();
                expect(pv != null && pv.Path.Count == 1 && pv.Path.Anchors[0].IsSlam && pv.Preview.SlamCount == 1 && pv.Preview.Points.Count == 1,
                    $"{label}: after two clicks on one beat the draft is a slam and the preview shows the lone slam, no tail ({(pv == null ? "no preview" : pts(pv.Preview))}; {state()})");
                expect(ed().Compose.HoveredAnchor is { Index: 0, Vf: true }, $"{label}: the pointer on the landing end hovers that end ({state()})");
            }))), null),
            (() => { }, shot("placing")),
            // right click on the slam's landing end finishes without another anchor (later than the slam it would place one)
            (() => inputSequence($"{label}: finish the slam", then(click(() => at(q, 0.8), MouseButton.Right), check(() =>
            {
                var s = slamSection();
                expect(s != null && s.Y == q && s.Points.Count == 1 && s.Points[0] is { V: 0.2, Vf: 0.8 },
                    $"{label}: right click put the two-click slam 0.2 -> 0.8 into the chart as a lone slam, no tail (official charts) ({state()})");
                expect(ed().Session.Commands.UndoDescription == "Add laser", $"{label}: placing the slam is one undo step ({state()})");
                undoSteps++;
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
            }))), null),

            // select mode: each end hovers and selects on its own
            (() => inputSequence($"{label}: hover each end", then(move(startEnd), check(() =>
            {
                expect(ed().Compose.HoveredAnchor is { Index: 0, Vf: false }, $"{label}: hovering the start end hovers the start end ({state()})");
            }), move(landingEnd), check(() =>
            {
                expect(ed().Compose.HoveredAnchor is { Index: 0, Vf: true }, $"{label}: hovering the landing end hovers the landing end ({state()})");
            }))), null),
            (() => { }, shot("hover-end")),
            (() => inputSequence($"{label}: select each end", then(click(startEnd), check(() =>
            {
                expect(sel().Count == 1 && sel().SlamEnds.Count == 1 && isEnd(sel().SlamEnds.First(), false) && ed().Compose.ActiveLaser?.Section == slamSection(),
                    $"{label}: a click on the start end selects only that end and makes the laser active ({state()})");
            }), click(landingEnd), check(() =>
            {
                expect(sel().Count == 1 && isEnd(sel().SlamEnds.First(), true), $"{label}: a click on the landing end selects only that end ({state()})");
            }))), null),
            (() => { }, shot("end-selected")),
            (() => inputSequence($"{label}: Ctrl+click the other end", then(key(Key.ControlLeft, true), click(startEnd), key(Key.ControlLeft, false), check(() =>
            {
                expect(sel().Count == 2 && sel().SlamEnds.Any(e => isEnd(e, false)) && sel().SlamEnds.Any(e => isEnd(e, true)),
                    $"{label}: Ctrl+click adds the start end: both ends selected as two points ({state()})");
            }))), null),
            (() => { }, shot("both-ends-selected")),

            // drag the landing end to 0.9 (value only), then the start end to 0.1 one unit earlier (value + time)
            (() => inputSequence($"{label}: drag the landing end", then(click(() => at(q - u / 2, 0.02)), click(landingEnd), key(Key.LShift, true), drag(landingEnd, () => at(q, 0.9)), key(Key.LShift, false), check(() =>
            {
                var pt = slam();
                expect(pt is { V: 0.2 } && Math.Abs(pt.Vf!.Value - 0.9) < 1e-9 && slamPulse() == q, $"{label}: dragging the landing end changed only Vf ({state()})");
                expect(ed().Session.Commands.UndoDescription == "Move anchor" && sel().Count == 1 && isEnd(sel().SlamEnds.First(), true),
                    $"{label}: one undo step, the landing end stays selected ({state()})");
                undoSteps++;
            }))), null),
            (() => inputSequence($"{label}: drag the start end in time", then(key(Key.LShift, true), drag(startEnd, () => at(q - u, 0.1)), key(Key.LShift, false), check(() =>
            {
                var pt = slam();
                expect(pt != null && Math.Abs(pt.V - 0.1) < 1e-9 && Math.Abs(pt.Vf!.Value - 0.9) < 1e-9 && slamPulse() == q - u,
                    $"{label}: dragging the start end moved the slam one unit earlier and changed V, Vf kept ({state()})");
                expect(ed().Session.Commands.UndoDescription == "Move anchor" && sel().Count == 1 && isEnd(sel().SlamEnds.First(), false),
                    $"{label}: one undo step, the start end stays selected ({state()})");
                undoSteps++;
            }))), null),
            (() => { }, shot("start-dragged")),

            // Delete with only the landing end selected: a lone slam minus one end would be a single plain point, which is
            // no laser - the section goes; Ctrl+Z brings the slam back
            (() => inputSequence($"{label}: delete one end", then(click(landingEnd), key(Key.Delete, true), key(Key.Delete, false), check(() =>
            {
                expect(slamSection() == null,
                    $"{label}: Delete on the selected landing end of a lone slam removes the section ({state()})");
                undoSteps++;
                undo();
                expect(slam() is { } back && Math.Abs(back.V - 0.1) < 1e-9 && Math.Abs(back.Vf!.Value - 0.9) < 1e-9, $"{label}: one Ctrl+Z restores the slam ({state()})");
            }))), null),

            // right-click menu on the start end: "Delete slam start" leaves a plain point at the end value
            (() => inputSequence($"{label}: menu on the start end", then(click(startEnd, MouseButton.Right), check(() =>
            {
                var menu = ed().Playfield.OpenLaserMenu;
                var labels = menu?.ChildrenOfType<LaserContextMenuItem>().Select(i => i.Label).ToList() ?? new List<string>();
                // a lone slam (one anchor): deleting the whole anchor is "Delete laser"
                expect(labels.Contains("Delete slam start") && !labels.Contains("Delete slam end") && labels.Contains("Delete laser") && labels[0].EndsWith("(slam start)", StringComparison.Ordinal),
                    $"{label}: the anchor menu on the start end offers that end and the whole laser ({string.Join(" | ", labels)}; {state()})");
            }))), null),
            (() => { }, shot("menu")),
            (() =>
            {
                Vector2 item(string text) => ed().Playfield.OpenLaserMenu!.ChildrenOfType<LaserContextMenuItem>().First(i => i.Label == text).ScreenSpaceDrawQuad.Centre;
                inputSequence($"{label}: menu: Delete slam start", then(click(() => item("Delete slam start")), check(() =>
                {
                    expect(slamSection() == null && ed().Session.Commands.UndoDescription == "Delete laser",
                        $"{label}: the menu's Delete slam start on a lone slam removes the section ({state()})");
                    undoSteps++;
                    undo();
                    expect(slam() != null, $"{label}: undo restores the slam ({state()})");
                })));
            }, null),

            // a box over the start end only selects that end; Ctrl+C writes the slam
            (() => inputSequence($"{label}: box over one end", then(click(() => at(q - 2 * u, 0.02)),
                drag(() => at(q - 3 * u / 2, 0.0), () => at(q - u / 2, 0.3)), check(() =>
            {
                expect(sel().LaserPoints.Count == 0 && sel().SlamEnds.Count == 1 && isEnd(sel().SlamEnds.First(), false),
                    $"{label}: a box around the start end selects that end and no laser point ({state()})");
                ed().TriggerAction(EditorAction.Copy);
                string ts = (ed().ChartActions as EditorChartActions)?.LastTimestamp ?? "";
                expect(ts.Contains("0.1>0.9", StringComparison.Ordinal), $"{label}: Ctrl+C on one end writes its slam into the timestamp ('{ts}')");
            }))), null),
            (() => { }, shot("box-end")),

            // undo the part: exactly the steps it made
            (() =>
            {
                for (int i = undoSteps; i > 0; i--) undo();
                ed().Session.Selection.Clear();
                expect(KsonWriter.Write(chart()) == before, $"{label}: undoing the slam part's steps restores the chart ({state()})");
                report($"compose-input ({label}): slams as two points - two-click slam (+ a small one inside the handle), each end hovered / selected / Ctrl+selected, landing end drag (Vf), start end drag (V + time), Delete one end + undo, menu Delete slam start, box over one end, Ctrl+C timestamp, all undone - ok");
            }, null),
        };
    }
}
