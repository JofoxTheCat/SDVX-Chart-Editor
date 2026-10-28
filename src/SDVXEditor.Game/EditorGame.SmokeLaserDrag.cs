using System.Globalization;
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
/// Compose-input smoke, laser editing details through REAL input on laser L (units 2..6, free there): right click while
/// placing puts a last anchor at the cursor; a press on an anchor of an unselected laser drags it at once, free (no grid)
/// by default and snapped with Shift; with a note selected as well the press moves both; W with the laser selected makes
/// it wide (positions kept) and its anchor then reaches the wide range, W again narrows it (clamping what lies beyond the
/// lanes); a Bezier control anchor drags out of time order and past the lanes while the bake stays valid. Each step is
/// undone (the chart equals the state before the part).
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> laserDragInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                                  Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        bool shots = label.StartsWith("no reference, borderless", StringComparison.Ordinal);
        string? shot(string name) => shots ? "15-laser-" + name : null;
        long p = 0, u = 240;
        string before = "";
        bool wideBefore = false;
        Note? extra = null;

        long y(double units) => p + (long)Math.Round(units * u);
        Vector2 at(double units, double v, bool wide = false) => screen(y(units), PlayfieldLayout.LaserX(v, wide));

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

        (Action, double)[] key(Key k, bool down) => new (Action, double)[] { (() => { if (down) im.PressKey(k); else im.ReleaseKey(k); }, 70) };
        (Action, double)[] tap(Key k) => then(key(k, true), key(k, false));
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        LaserSection? section() => ed().Session.Chart.Notes.Lasers[0].FirstOrDefault(s => s.Y >= y(1.5) && s.Y <= y(5));
        string pts(LaserSection? s) => s == null ? "none" : $"w{s.Width} " + string.Join(" ", s.Points.Select(pt => inv($"[{(double)(s.Y + pt.Ry - p) / u:0.###}u {pt.V:0.####}{(pt.IsSlam ? $">{pt.Vf}" : "")}]")));
        string anchors(LaserPath? path, long sy) => path == null ? "no path" : string.Join(" ", path.Anchors.Select(a => inv($"[{(double)(sy + a.Ry - p) / u:0.###}u {a.V:0.####} {a.Type?.ToString() ?? "-"}]")));
        string state() => inv($"laser {pts(section())}; active {(ed().Compose.ActiveLaser is { } a ? "yes" : "-")}; selection {ed().Session.Selection.Count}; tool {ed().Tools.EffectiveTool.Value}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");
        void undo() => ed().TriggerAction(EditorAction.Undo);

        return new List<(Action, string?)>
        {
            (() =>
            {
                p = start();
                u = unit();
                before = KsonWriter.Write(ed().Session.Chart);
                wideBefore = ed().Compose.WideLaser.Value;
                ed().Compose.WideLaser.Value = false;
                expect(ed().Session.Chart.Notes.Lasers[0].All(s => s.EndY < y(1.5) || s.Y > y(6.5)), $"{label}: laser L is free at units 2..6 ({state()})");
                ed().SelectTool(EditorTool.Laser);
                ed().Tools.LaserSide.Value = 0;
                ed().Session.Selection.Clear();
            }, null),

            // 1. right click while placing: a last anchor at the cursor, then finished
            (() => inputSequence($"{label}: right click places the last anchor", then(click(() => at(2, 0.2)), click(() => at(3, 0.6)), click(() => at(4, 0.4), MouseButton.Right), check(() =>
            {
                var s = section();
                var path = s == null ? null : LaserPaths.ValidPath(s);
                expect(ed().Compose.Placement == null && path != null && path.Count == 3 && s!.EndY == y(4) && Math.Abs(path.Anchors[2].V - 0.4) < 1e-9
                       && path.Anchors[0].Type == LaserCurveType.PerfectCurve && ed().Session.Commands.UndoDescription == "Add laser",
                    $"{label}: two clicks + right click at 4u = three anchors (a perfect curve) ending under the cursor, one undo step ({anchors(path, s?.Y ?? 0)}; {state()})");
                undo();
                expect(section() == null, $"{label}: undo removes it ({state()})");
            }))), null),

            // 2. + 4. a straight laser; in select mode a press on its end point drags it at once, free by default, snapped with Shift
            (() => inputSequence($"{label}: direct free drag", then(drag(() => at(2, 0.2), () => at(4, 0.8)), check(() =>
            {
                expect(section() is { Points.Count: 2 } s && s.EndY == y(4), $"{label}: a straight laser 2u..4u drawn ({state()})");
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
            }), drag(() => at(4, 0.8), () => at(4.37, 0.613)), check(() =>
            {
                var s = section();
                var end = s?.Points[^1];
                long endY = s == null ? 0 : s.Y + end!.Ry;
                double grid = end == null ? 0 : Math.Abs(end.V * 50 - Math.Round(end.V * 50));
                expect(end != null && Math.Abs(end.V - 0.613) < 0.006 && grid > 0.05 && Math.Abs(endY - y(4.37)) <= 5 && endY % ComposeEditor.FREE_DRAG_PULSES == 0 && endY != ed().Compose.PulseAt(ed().Session.PulseToMs(endY)),
                    inv($"{label}: one press-drag on the unselected laser's end moved it freely: value {end?.V:0.####} (off the 1/50 grid), {(double)(endY - p) / u:0.###}u (off the beat grid) ({state()})"));
                expect(ed().Compose.ActiveLaser?.Section == s && ed().Session.Commands.UndoDescription == "Move anchor",
                    $"{label}: the press selected the laser, the drag is one undo step ({state()})");
            }), key(Key.LShift, true), drag(() => at(4.37, 0.613), () => at(4, 0.7)), key(Key.LShift, false), check(() =>
            {
                var s = section();
                expect(s != null && s.EndY == y(4) && Math.Abs(s.Points[^1].V - 0.7) < 1e-9, $"{label}: with Shift held the drag snaps to the beat and the 1/50 grid ({state()})");
            }))), null),
            (() => { }, shot("free-drag")),

            // multi-selection: the laser and a BT chip selected, a press on the laser's anchor moves both one unit
            (() =>
            {
                var e = ed();
                extra = new Note(y(5));
                e.Session.Commands.Execute(new AddNotesCommand(new[] { new NoteRef(NoteKind.Bt, 3, extra) }));
                e.Session.Selection.Clear();
                e.Session.SelectSections(new[] { (0, section()!) });
                e.Session.Selection.Add(new NoteRef(NoteKind.Bt, 3, extra));
                inputSequence($"{label}: multi-selection moves together", then(key(Key.LShift, true), drag(() => at(4, 0.7), () => at(5, 0.7)), key(Key.LShift, false), check(() =>
                {
                    var s = section();
                    expect(s != null && s.Y == y(3) && s.EndY == y(5) && extra!.Y == y(6) && Math.Abs(s.Points[^1].V - 0.7) < 1e-9,
                        $"{label}: pressing the selected laser's anchor with a note also selected moved the whole selection one unit ({state()})");
                    undo();
                    expect(section() is { } back && back.Y == y(2) && extra!.Y == y(5), $"{label}: one undo moves both back ({state()})");
                    undo(); // the chip
                    e.Session.Selection.Clear();
                })));
            }, null),

            // K-Shoot Editor 2.2 gestures in the laser tool: Ctrl+Shift+click = wide keeping the position, Ctrl+Shift+right click = keeping the values
            (() => inputSequence($"{label}: Ctrl+Shift click / right click toggle wide", then(check(() => ed().SelectTool(EditorTool.Laser)),
                key(Key.ControlLeft, true), key(Key.LShift, true), click(() => at(3, 0.45)), check(() =>
            {
                expect(section() is { Width: 2 } s && Math.Abs(s.Points[0].V - 0.35) < 1e-9 && ed().Session.Commands.UndoDescription == "Wide laser (keep position)",
                    $"{label}: Ctrl+Shift+click on the laser made it wide where it is (0.2 -> 0.35) ({state()})");
            }), click(() => at(3, 0.475, true), MouseButton.Right), key(Key.LShift, false), key(Key.ControlLeft, false), check(() =>
            {
                expect(section() is { Width: 1 } s && Math.Abs(s.Points[0].V - 0.35) < 1e-9 && ed().Session.Commands.UndoDescription == "Normal laser (keep values)",
                    $"{label}: Ctrl+Shift+right click made it normal keeping the values (0.35 stays) ({state()})");
                undo();
                undo();
                expect(section() is { Width: 1 } back && Math.Abs(back.Points[0].V - 0.2) < 1e-9, $"{label}: two undos restore it ({state()})");
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
            }))), null),

            // 3. W with the laser selected: wide, same place on screen; the anchor then reaches the wide range; W again narrows (clamped)
            (() => inputSequence($"{label}: wide toggle on a placed laser", then(click(() => at(3, 0.45)), tap(Key.W), check(() =>
            {
                var s = section();
                expect(s is { Width: 2 } && Math.Abs(s.Points[0].V - 0.35) < 1e-9 && Math.Abs(s.Points[^1].V - 0.6) < 1e-9 && ed().Compose.WideLaser.Value,
                    $"{label}: W with the laser selected made it wide, points kept on screen (0.2 -> 0.35, 0.7 -> 0.6) ({state()})");
            }), key(Key.LShift, true), drag(() => at(2, 0.35, true), () => at(2, 0.06, true)), key(Key.LShift, false), check(() =>
            {
                var s = section();
                expect(s is { Width: 2 } && Math.Abs(s.Points[0].V - 0.06) < 1e-9 && PlayfieldLayout.LaserX(0.06, true) < PlayfieldLayout.LANES_LEFT,
                    $"{label}: the wide laser's start anchor dragged out past the lanes to 0.06 ({state()})");
            }), tap(Key.W), check(() =>
            {
                var s = section();
                expect(s is { Width: 1 } && s.Points[0].V == 0 && Math.Abs(s.Points[^1].V - 0.7) < 1e-9 && ed().LastToast.Contains("1 value", StringComparison.Ordinal),
                    $"{label}: W again narrowed it: 0.06 lay beyond the lanes and is clamped to 0 (toast says so), 0.6 -> 0.7 ({state()})");
                undo();
                undo();
                undo();
                expect(section() is { Width: 1 } back && Math.Abs(back.Points[0].V - 0.2) < 1e-9, $"{label}: three undos restore the normal laser ({state()})");
                undo(); // the free + snapped drags
                undo();
                undo(); // the straight laser
                expect(section() == null, $"{label}: and the laser is gone ({state()})");
            }))), null),
            (() => { }, shot("wide")),

            // 5. a 4-anchor Bezier; its control anchor drags out of time order and past the lanes, the bake stays valid
            (() => inputSequence($"{label}: Bezier control freedom", then(check(() =>
            {
                ed().Compose.WideLaser.Value = false;
                ed().SelectTool(EditorTool.Laser);
            }), click(() => at(2, 0.3)), click(() => at(3, 0.7)), click(() => at(4, 0.4)), click(() => at(5, 0.6)), click(() => at(5, 0.6), MouseButton.Right), check(() =>
            {
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
                var s = section();
                expect(s != null && LaserPaths.ValidPath(s) is { Count: 4 } path && path.Anchors[0].Type == LaserCurveType.Bezier && path.IsControl(1),
                    $"{label}: four clicks = a Bezier with control anchors ({state()})");
            }), drag(() => at(3, 0.7), () => at(4.5, 1.3)), check(() =>
            {
                var s = section();
                var path = s == null ? null : LaserPaths.ValidPath(s);
                var c = path?.Anchors[1];
                bool bakeOk = s != null && s.Points.All(pt => pt.V is >= 0 and <= 1) && s.Points.Zip(s.Points.Skip(1)).All(x => x.First.Ry < x.Second.Ry);
                expect(path != null && c != null && c.Ry > path.Anchors[2].Ry && c.V > 1.2 && path.IsControl(1) && bakeOk,
                    $"{label}: the control anchor moved after the next anchor and past the lanes (v {c?.V:0.###}); the baked laser stays in 0..1 and in time order ({anchors(path, s?.Y ?? 0)}; {state()})");
            }), new (Action, double)[] { (() => im.MoveMouseTo(at(3, 0.45)), 60), (() => im.MoveMouseTo(at(4.5, 1.3)), 80) }, check(() =>
            {
                expect(ed().Compose.HoveredAnchor is { Index: 1 }, $"{label}: the control anchor out there is still hovered / grabbable ({state()})");
            }))), null),
            (() => { }, shot("bezier-control")),
            (() =>
            {
                undo(); // the control drag
                undo(); // the Bezier
                ed().Session.Selection.Clear();
                ed().Compose.WideLaser.Value = wideBefore;
                expect(KsonWriter.Write(ed().Session.Chart) == before, $"{label}: undoing the laser-drag part restores the chart ({state()})");
                report($"compose-input ({label}): laser editing - right click places the last anchor, direct free anchor drag (Shift snaps), multi-selection moves, W widens / narrows a selected laser (clamp), Bezier control out of order and past the lanes - ok");
            }, null),
        };
    }
}
