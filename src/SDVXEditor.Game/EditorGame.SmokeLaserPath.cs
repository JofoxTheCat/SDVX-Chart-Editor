using System.Globalization;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game;

/// <summary>
/// Compose-input smoke, laser paths (osu!lazer slider editing) through the REAL input path: the L | R switch, placing a
/// 3-anchor laser (perfect curve) and a 4th anchor (Bezier), clicking a placed anchor to change its type, right click to
/// finish; in select mode hovering and clicking the laser, the right-click menu on an anchor (Straight), dragging an
/// anchor; then kson save + reload keeps the anchors and the ksh export keeps the baked shape within the error budget.
/// The undo at the end of the compose-input part takes all of it back.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> laserPathInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                                  Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        bool shots = label.StartsWith("no reference, borderless", StringComparison.Ordinal);
        string? shot(string name) => shots ? "11-laser-" + name : null;
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

        long q = 0, u = 240;
        float x(double v) => PlayfieldLayout.LaserX(v, false);
        Vector2 at(long ru, double v) => screen(q + ru * u, x(v));

        (Action, double)[] move(Func<Vector2> to) => new (Action, double)[] { (() => im.MoveMouseTo(to()), 60) };

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

        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double) check(Action a) => (a, 0d);

        LaserSection? section() => ed().Session.Chart.Notes.Lasers[1].FirstOrDefault(s => s.Y == q);
        string state() => inv($"placement {ed().Compose.Placement?.Path.Count.ToString() ?? "none"}; tool {ed().Tools.EffectiveTool.Value}; side {ed().Tools.LaserSide.Value}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");
        string anchors(LaserPath? p) => p == null ? "no path" : string.Join(" ", p.Anchors.Select(a => inv($"[{(a.Ry) / Math.Max(1, u)}u {a.V} {a.Type?.ToString() ?? "-"}{(a.Auto ? "*" : "")}]")));

        return new List<(Action, string?)>
        {
            (() =>
            {
                q = start();
                u = unit();
                ed().SelectTool(EditorTool.Laser);
                ed().Tools.LaserSide.Value = 0;
            }, null),

            // the L | R switch under the laser tile
            (() =>
            {
                var toggle = ed().Toolbox.SideToggle;
                inputSequence($"{label}: L|R switch", then(click(() => toggle.SegmentFor(1).ScreenSpaceDrawQuad.Centre), new[]
                {
                    check(() => expect(ed().Tools.LaserSide.Value == 1 && ed().Toolbox.LaserTile.Badge.Value == "R",
                        $"{label}: a click on R in the L|R switch picks laser R and the tile shows an R badge ({state()})")),
                }));
            }, null),
            (() => { }, shot("lr-toggle")),
            (() =>
            {
                var toggle = ed().Toolbox.SideToggle;
                inputSequence($"{label}: L|R switch back", then(click(() => toggle.SegmentFor(0).ScreenSpaceDrawQuad.Centre), new[]
                {
                    check(() => expect(ed().Tools.LaserSide.Value == 0 && ed().Tools.EffectiveTool.Value == EditorTool.Laser && ed().Toolbox.LaserTile.Badge.Value == "L",
                        $"{label}: a click on L picks laser L ({state()})")),
                }, click(() => toggle.SegmentFor(1).ScreenSpaceDrawQuad.Centre), new[]
                {
                    check(() => expect(ed().Tools.LaserSide.Value == 1, $"{label}: and R again: the laser goes on R ({state()})")),
                }));
            }, null),

            // placement: three clicks = perfect curve (the pointer previews the 4th)
            (() => inputSequence($"{label}: place a 3-anchor laser", then(click(() => at(0, 0.3)), click(() => at(4, 0.7)), click(() => at(8, 0.4)), move(() => at(10, 0.5)), new[]
            {
                check(() =>
                {
                    var pl = ed().Compose.Placement;
                    expect(pl != null && pl.Path.Count == 3 && pl.Y == q && pl.Path.Anchors[0].Type == LaserCurveType.PerfectCurve,
                        $"{label}: three clicks place a 3-anchor perfect curve ({anchors(pl?.Path)}; {state()})");
                }),
            })), null),
            (() => { }, shot("perfect-curve")),
            (() => inputSequence($"{label}: 4th anchor", then(click(() => at(12, 0.6)), move(() => at(13, 0.45)), new[]
            {
                check(() =>
                {
                    var pl = ed().Compose.Placement;
                    expect(pl != null && pl.Path.Count == 4 && pl.Path.Anchors[0].Type == LaserCurveType.Bezier,
                        $"{label}: a 4th anchor turns the curve into a Bezier ({anchors(pl?.Path)}; {state()})");
                }),
            })), null),
            (() => { }, shot("bezier-anchors")),
            // a click on a placed anchor changes its type; right click finishes
            (() => inputSequence($"{label}: anchor type while placing, finish", then(click(() => at(8, 0.4)), new[]
            {
                check(() =>
                {
                    var pl = ed().Compose.Placement;
                    expect(pl != null && pl.Path.Count == 4 && pl.Path.Anchors[2].Type != null,
                        $"{label}: clicking a placed anchor makes it a typed segment start ({anchors(pl?.Path)}; {state()})");
                }),
            }, click(() => at(10, 0.5), MouseButton.Right), new[]
            {
                check(() =>
                {
                    var s = section();
                    var path = s == null ? null : LaserPaths.ValidPath(s);
                    expect(ed().Compose.Placement == null && path != null && path.Count == 4 && path.Anchors[0].Type == LaserCurveType.PerfectCurve
                           && path.Anchors[2].Type != null && path.Anchors[3].Ry == 12 * u && ed().Tools.LaserSide.Value == 1,
                        $"{label}: right click put the laser into the chart with its anchors ({anchors(path)}; {state()})");
                    ed().SelectTool(EditorTool.Select);
                }),
            })), null),

            // select mode: hover outlines, click selects, right click on an anchor = type menu
            (() =>
            {
                Vector2 body() => screen(q + u / 2, x(LaserPathBaker.PointsValueAt(section()!.Points, u / 2) ?? 0.5));
                inputSequence($"{label}: hover and select the laser", then(move(body), new[]
                {
                    check(() => expect(ed().Compose.HoveredLaser is { } h && h.Section == section(), $"{label}: hovering the laser path highlights it ({state()})")),
                }, click(body), new[]
                {
                    check(() => expect(ed().Compose.ActiveLaser is { } a && a.Section == section(), $"{label}: a click on the path selects the section ({state()})")),
                }, click(() => at(8, 0.4), MouseButton.Right), new[]
                {
                    check(() => expect(ed().Playfield.OpenLaserMenu != null, $"{label}: right click on an anchor opens the curve-type menu ({state()})")),
                }));
            }, null),
            (() => { }, shot("context-menu")),
            (() =>
            {
                Vector2 item(string text) => ed().Playfield.OpenLaserMenu!.ChildrenOfType<LaserContextMenuItem>().First(i => i.Label == text).ScreenSpaceDrawQuad.Centre;
                inputSequence($"{label}: menu: Straight", then(click(() => item("Straight")), new[]
                {
                    check(() =>
                    {
                        var path = section() is { } s ? LaserPaths.ValidPath(s) : null;
                        expect(ed().Playfield.OpenLaserMenu == null && path != null && path.Anchors[2] is { Type: LaserCurveType.Linear, Auto: false } && path.Anchors[0].Type == LaserCurveType.PerfectCurve,
                            $"{label}: choosing Straight in the menu typed anchor 3 straight, the perfect curve before it stays ({anchors(path)}; {state()})");
                    }),
                }));
            }, null),
            // drag the last anchor of the selected laser one unit later and to 0.5 (Shift = snap; anchor drags are free without it)
            (() => inputSequence($"{label}: drag an anchor", then(new (Action, double)[] { (() => im.PressKey(Key.LShift), 50) }, drag(() => at(12, 0.6), () => at(13, 0.5)), new (Action, double)[] { (() => im.ReleaseKey(Key.LShift), 50) }, new[]
            {
                check(() =>
                {
                    var path = section() is { } s ? LaserPaths.ValidPath(s) : null;
                    expect(path != null && path.Anchors[3].Ry == 13 * u && Math.Abs(path.Anchors[3].V - 0.5) < 1e-9 && path.Anchors[2].Type == LaserCurveType.Linear,
                        $"{label}: dragging the anchor moved it (time + value) and kept the types ({anchors(path)}; {state()})");
                }),
            })), shot("anchor-dragged")),

            // save + reload keeps the anchors; the ksh export keeps the baked shape
            (() =>
            {
                var s = section() ?? throw new SmokeTestException($"{label}: the laser is gone");
                var path = LaserPaths.ValidPath(s)!;
                string file = Path.Combine(Path.GetTempPath(), $"sdvxeditor-smoke-laser-{Environment.ProcessId}.kson");
                try
                {
                    KsonWriter.WriteFile(ed().Session.Chart, file);
                    var back = KsonReader.ReadFile(file).Notes.Lasers[1].FirstOrDefault(x => x.Y == q);
                    var backPath = back == null ? null : LaserPaths.ValidPath(back);
                    expect(backPath != null && backPath.Anchors.Select(a => (a.Ry, a.V, a.Type, a.Auto)).SequenceEqual(path.Anchors.Select(a => (a.Ry, a.V, a.Type, a.Auto))),
                        $"{label}: kson save + reload keeps the anchors ({anchors(backPath)} vs {anchors(path)})");
                }
                finally
                {
                    File.Delete(file);
                }

                // ksh: the file must read back as the ksh bake (only the 1/50 quantisation in between). How close that bake is
                // to the path is grid-limited for short steep curves (no ksh point may sit within 1/32 of an anchor) and
                // is reported; the unit tests prove the 0.004 budget for musically sized curves.
                var ksh = KshReader.Read(KshWriter.Write(ed().Session.Chart)).Notes.Lasers[1].FirstOrDefault(x => x.Y == q);
                var kshBake = LaserPathBaker.Bake(path, s.Y, LaserBakeOptions.Ksh);
                double readBack = double.MaxValue;
                if (ksh != null)
                {
                    readBack = 0;
                    for (long ry = 0; ry <= kshBake[^1].Ry; ry += 5)
                    {
                        if (kshBake.Any(pt => pt.Ry == ry && pt.IsSlam)) continue;
                        if (LaserPathBaker.PointsValueAt(kshBake, ry) is double want && LaserPathBaker.PointsValueAt(ksh.Points, s.Y + ry - ksh.Y) is double got)
                            readBack = Math.Max(readBack, Math.Abs(want - got));
                    }
                }
                double kshErr = LaserPathBaker.MaxError(path, s.Y, kshBake, s.Y);
                double ksonErr = LaserPathBaker.MaxError(path, s.Y, s.Points, s.Y);
                expect(ksonErr <= LaserBakeOptions.TOLERANCE + 1e-6 && kshErr <= LaserBakeOptions.TOLERANCE + 1e-6,
                    inv($"{label}: kson and ksh bakes within {LaserBakeOptions.TOLERANCE} of the path (kson {ksonErr:0.0000}, ksh {kshErr:0.0000})"));
                expect(readBack <= 0.5 / 50 + 1e-6 && ksh!.SlamCount == 0 && ksh.Points.Count == kshBake.Count,
                    inv($"{label}: the ksh export reads back as the baked shape (max difference {readBack:0.0000}, slams {ksh?.SlamCount}, points {ksh?.Points.Count} vs {kshBake.Count})"));
                report(inv($"compose-input ({label}): laser path - L|R switch, 3 anchors = perfect curve, 4 = Bezier, anchor click retypes, right click finishes, hover/select, menu Straight, anchor drag, kson reload; bake error vs path kson {ksonErr:0.0000} / ksh {kshErr:0.0000} (unit {u} pulses), ksh read-back {readBack:0.0000} - ok"));
            }, null),
        };
    }
}
