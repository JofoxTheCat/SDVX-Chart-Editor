using System.Globalization;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Camera-part smoke, anchor paths on the camera curves through the REAL input path (<see cref="SmokeInput"/>): three
/// clicks in zoom_bottom place a perfect curve (hover shows the anchors), a click on a placed anchor cycles its type and
/// Ctrl+Z takes that back, right click on the first anchor opens the anchor menu and Bezier makes the segment one exact kson
/// curve, the control anchor drags; the live camera follows the curve, a kson write + read keeps the anchors and the ksh
/// export reads back as the curve within the lane's bake bound. The anchors stay for the camera part's save + reload.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> cameraAnchorInputSteps(Func<EditorScreen> ed, Func<long> start, string? shots)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        const CamLane lane = CamLane.ZoomBottom;
        long q = 0, b = 240;
        float zoomBefore = 1;

        CameraLanes lanes() => ed().CameraContent.Lanes;
        CameraLaneEditor le() => ed().CameraContent.LaneEditor;
        Vector2 at(long beats, double value) =>
            lanes().ToScreenSpace(new Vector2(CameraLanes.LaneLeft(lane) + (float)CameraLaneEditor.FxOf(lane, value) * CameraLaneEditor.LANE_WIDTH, lanes().YOfPulse(q + beats * b)));

        CamRun? run() => CameraPaths.Runs(ed().Session.Chart.Camera, CamPathLane.ZoomBottom).FirstOrDefault();
        LaserPath? anchors() => run() is { } r ? le().AnchorsOf(r) : null;
        // the three test anchors (pulses relative to the run): A = q, B = q + b, C = q + 2b
        LaserAnchor? anchor(long beats) => run() is { } r && anchors() is { } p ? p.Anchors.FirstOrDefault(a => r.Y + a.Ry == q + beats * b) : null;
        string state() => inv($"anchors {string.Join(" ", (anchors()?.Anchors ?? new List<LaserAnchor>()).Where(a => run()!.Y + a.Ry >= q - b && run()!.Y + a.Ry <= q + 3 * b).Select(a => $"[{(run()!.Y + a.Ry - q) / (double)b:0.##}b {a.V} {a.Type?.ToString() ?? "-"}{(a.Auto ? "*" : "")}]"))}; chain {(le().Chain != null ? "on" : "off")}; menu {(lanes().OpenMenu != null ? "open" : "closed")}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");
        string? shot(string name) => shots != null ? "30" + name : null;

        (Action, double)[] move(Func<Vector2> to) => new (Action, double)[] { (() => im.MoveMouseTo(to()), 60) };

        (Action, double)[] click(Func<Vector2> to, MouseButton button = MouseButton.Left) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(to()), 50),
            (() => im.PressButton(button), 50),
            (() => im.ReleaseButton(button), 70),
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
        (Action, double) check(Action a) => (a, 0d);

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                var e = ed();
                var s = e.Session;
                q = start();
                b = s.Timing.BeatLength(s.Timing.MeasureAt(q));
                im.UseParentInput = false;

                // A..C and a beat before/after on screen: the judgement line a beat before A, the zoom so q + 3 beats sits
                // 30 px below the top (half beats for slow songs, as the compose-input part does)
                zoomBefore = e.Zoom.Value;
                float judgeY = lanes().DrawHeight - PlayfieldLayout.JUDGEMENT_FROM_BOTTOM;
                float zoomFor(long unit) => (float)((judgeY - 30) / ((s.PulseToMs(q + 3 * unit) - s.PulseToMs(q - unit)) * PlayfieldLayout.PX_PER_MS));
                for (int k = 0; k < 2 && zoomFor(b) < e.Zoom.MinValue && b % 2 == 0; k++) b /= 2;
                e.Playback.CurrentTime.Value = s.PulseToMs(q - b);
                e.Zoom.Value = Math.Clamp((float)Math.Floor(zoomFor(b) * 20) / 20, e.Zoom.MinValue, 1f);
            }, null),
            (() =>
            {
                float yTop = lanes().YOfPulse(q + 3 * b), yBottom = lanes().YOfPulse(q - b);
                expect(yTop > 0 && yBottom < lanes().DrawHeight, inv($"camera anchors: the test stretch is on screen (y {yTop:0}..{yBottom:0} of {lanes().DrawHeight:0})"));
                expect(!CameraPaths.Graph(ed().Session.Chart.Camera, CamPathLane.ZoomBottom).Any(p => p.Y > q - b && p.Y < q + 3 * b), "camera anchors: no zoom_bottom point in the test stretch");
            }, null),

            // three clicks in a row = a perfect curve; hovering shows the anchors
            (() => inputSequence("camera anchors: three clicks", then(click(() => at(0, 0)), click(() => at(1, 150)), click(() => at(2, -60)), move(() => at(1, 150)), new[]
            {
                check(() =>
                {
                    var a = anchor(0);
                    expect(a is { Type: LaserCurveType.PerfectCurve, Auto: true } && anchor(1) is { Type: null } && anchor(2) != null && run()!.Curved,
                        $"camera anchors: three clicks in zoom_bottom place a perfect curve ({state()})");
                    expect(le().HoveredAnchor is { Lane: CamPathLane.ZoomBottom } h && h.Y == q + b, $"camera anchors: hovering the middle anchor highlights it ({state()})");
                    var g = CameraPaths.Graph(ed().Session.Chart.Camera, CamPathLane.ZoomBottom);
                    expect(g.Count(p => p.Y > q && p.Y < q + 2 * b) > 2, $"camera anchors: the perfect curve is baked into graph points ({g.Count(p => p.Y > q && p.Y < q + 2 * b)} between A and C)");
                }),
            })), shot("a-camera-anchors-perfect")),

            // while placing, a click on a placed anchor cycles its type; Ctrl+Z takes it back
            (() => inputSequence("camera anchors: click cycles, Ctrl+Z", then(click(() => at(1, 150)), new[]
            {
                check(() => expect(anchor(1) is { Type: not null } && anchor(0)?.Type == LaserCurveType.Linear,
                    $"camera anchors: a click on the inherit anchor while placing makes it a segment start: two straight pairs ({state()})")),
            }, key(Key.ControlLeft, true), key(Key.Z, true), key(Key.Z, false), key(Key.ControlLeft, false), new[]
            {
                check(() => expect(anchor(1) is { Type: null } && anchor(0)?.Type == LaserCurveType.PerfectCurve,
                    $"camera anchors: Ctrl+Z restores the perfect curve ({state()})")),
            })), null),

            // right click on A: the anchor menu; Bezier
            (() => inputSequence("camera anchors: menu", then(click(() => at(0, 0), MouseButton.Right), new[]
            {
                check(() => expect(lanes().OpenMenu != null, $"camera anchors: right click on an anchor opens the anchor menu ({state()})")),
            })), shot("b-camera-anchor-menu")),
            (() =>
            {
                Vector2 item(string text) => lanes().OpenMenu!.ChildrenOfType<LaserContextMenuItem>().First(i => i.Label == text).ScreenSpaceDrawQuad.Centre;
                inputSequence("camera anchors: menu Bezier", then(click(() => item("Bezier")), new[]
                {
                    check(() =>
                    {
                        expect(lanes().OpenMenu == null && anchor(0) is { Type: LaserCurveType.Bezier, Auto: false } && anchor(1) is { Type: null },
                            $"camera anchors: Bezier from the menu types the segment ({state()})");
                        var g = CameraPaths.Graph(ed().Session.Chart.Camera, CamPathLane.ZoomBottom);
                        var head = g.FirstOrDefault(p => p.Y == q);
                        expect(head?.Curve is { IsLinear: false } && !g.Any(p => p.Y > q && p.Y < q + 2 * b),
                            $"camera anchors: a 3-anchor Bezier is one exact kson curve on A (curve {head?.Curve?.ToString() ?? "none"}, {g.Count(p => p.Y > q && p.Y < q + 2 * b)} points between)");
                    }),
                }));
            }, null),

            // drag the control anchor B up to 240; the live camera follows the curve
            (() => inputSequence("camera anchors: drag", then(drag(() => at(1, 150), () => at(1, 240)), new[]
            {
                check(() =>
                {
                    expect(anchor(1) is { V: 240, Type: null } && anchor(0)?.Type == LaserCurveType.Bezier,
                        $"camera anchors: dragging the control anchor moved it and kept the types ({state()})");
                    var r = run()!;
                    var path = r.Stored!.Path;
                    double worst = 0;
                    for (long y = q + 5; y < q + 2 * b; y += Math.Max(5, b / 16))
                    {
                        double want = LaserPathBaker.ValueAt(path, y - r.Y) ?? double.NaN;
                        double got = new CameraEvaluator(ed().Session.Chart).Evaluate(y).ZoomBottom;
                        worst = Math.Max(worst, Math.Abs(want - got));
                    }

                    expect(worst <= 0.5, inv($"camera anchors: the preview's camera follows the curve (max difference {worst:0.000})"));
                }),
            })), shot("c-camera-anchor-dragged")),

            // kson keeps the anchors, the ksh export reads back as the curve
            (() =>
            {
                var chart = ed().Session.Chart;
                var r = run()!;
                var path = r.Stored!.Path;
                var back = KsonReader.Read(KsonWriter.Write(chart));
                var backRun = CameraPaths.Runs(back.Camera, CamPathLane.ZoomBottom).FirstOrDefault();
                expect(back.StaleCameraPaths == 0 && backRun?.Stored?.Path is { } bp
                       && bp.Anchors.Select(a => (a.Ry, a.V, a.Vf, a.Type, a.Auto)).SequenceEqual(path.Anchors.Select(a => (a.Ry, a.V, a.Vf, a.Type, a.Auto))),
                    $"camera anchors: kson write + read keeps the anchors (stale {back.StaleCameraPaths})");

                // the error over the test stretch (the rest of the song's graph is not this test's business)
                double windowError(CamRun? other)
                {
                    if (other == null) return double.MaxValue;
                    double worst = 0;
                    for (long y = q; y <= q + 2 * b; y += 5)
                    {
                        if (LaserPathBaker.ValueAt(path, y - r.Y) is double want && LaserPathBaker.PointsValueAt(other.Points, y - other.Y) is double got)
                            worst = Math.Max(worst, Math.Abs(want - got));
                    }

                    return worst;
                }

                var ksh = KshReader.Read(KshWriter.Write(chart));
                var kr = CameraPaths.Runs(ksh.Camera, CamPathLane.ZoomBottom).FirstOrDefault();
                double err = windowError(kr);
                var kshInside = ksh.Camera.Body.ZoomBottom.Where(p => p.Y > q && p.Y < q + 2 * b).ToList();
                expect(err <= CameraPaths.Tolerance(CamPathLane.ZoomBottom) + 1e-6 && kshInside.Count > 0 && kshInside.All(p => p.Curve == null),
                    inv($"camera anchors: the ksh export bakes the Bezier into {kshInside.Count} plain points within {CameraPaths.Tolerance(CamPathLane.ZoomBottom)} (error {err:0.0000})"));
                double ksonErr = windowError(r);
                report(inv($"camera anchors (real input): 3 clicks = perfect curve, hover, click cycles while placing + Ctrl+Z, menu Bezier = one kson curve, control anchor drag, preview follows, kson reload keeps anchors, ksh bake error {err:0.0000} (kson {ksonErr:0.0000}, bound {CameraPaths.Tolerance(CamPathLane.ZoomBottom)}) - ok"));

                le().EndChain();
                im.UseParentInput = true;
                ed().Zoom.Value = zoomBefore;
            }, null),
        };

        // osu!: right click on empty space while placing puts a last anchor at the cursor and ends the chain (inserted
        // before the export checks above and undone again, so they see A..C as before)
        Vector2 atPulse(long pulse, double value) =>
            lanes().ToScreenSpace(new Vector2(CameraLanes.LaneLeft(lane) + (float)CameraLaneEditor.FxOf(lane, value) * CameraLaneEditor.LANE_WIDTH, lanes().YOfPulse(pulse)));
        LaserAnchor? anchorAt(long pulse) => run() is { } r && anchors() is { } p ? p.Anchors.FirstOrDefault(a => r.Y + a.Ry == pulse) : null;
        steps.Insert(steps.Count - 1, (() => inputSequence("camera anchors: right click places the last anchor", then(click(() => atPulse(q + 9 * b / 4, 30)), new[]
        {
            check(() => expect(le().Chain != null && anchorAt(q + 9 * b / 4) != null, $"camera anchors: a click after C starts a chain ({state()})")),
        }, click(() => atPulse(q + 5 * b / 2, 90), MouseButton.Right), new[]
        {
            check(() =>
            {
                var last = anchorAt(q + 5 * b / 2);
                expect(last != null && Math.Abs(last.V - 90) < 15 && le().Chain == null,
                    $"camera anchors: right click after the chain's tail put a last anchor at the cursor (~90) and ended the chain ({state()})");
                ed().TriggerAction(EditorAction.Undo);
                expect(anchorAt(q + 5 * b / 2) == null && anchorAt(q + 9 * b / 4) != null, $"camera anchors: one Ctrl+Z removes just the last anchor ({state()})");
                ed().TriggerAction(EditorAction.Undo);
                expect(anchorAt(q + 9 * b / 4) == null && anchor(2) != null, $"camera anchors: a second Ctrl+Z the click before it ({state()})");
                report("camera anchors (real input): right click after the chain's tail places a last anchor and ends the chain, one undo step - ok");
            }),
        })), null));
        return steps;
    }
}
