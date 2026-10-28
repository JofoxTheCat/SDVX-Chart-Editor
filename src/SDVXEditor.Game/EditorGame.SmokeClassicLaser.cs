using System.Globalization;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Compose-input smoke, the classic-laser key through REAL input on laser L (units 2..6, free there): the laser tile
/// advertises the key; with F held the clicks place plain straight points on the 1/10 value grid (no perfect curve from
/// three clicks), a second click on the last beat is a slam and a click on its end again finishes (one undo step);
/// another key while F is down uses it up; a chain that switches modes (two anchor clicks, one classic point, two anchor
/// clicks) gets the anchor typing again after the classic point. Everything is undone (the chart equals the state before).
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> classicLaserInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                                     Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        bool shots = label.StartsWith("no reference, borderless", StringComparison.Ordinal);
        string? shot(string name) => shots ? "14-classic-" + name : null;
        long p = 0, u = 240;
        string before = "";

        Vector2 at(long units, double v) => screen(p + units * u, PlayfieldLayout.LaserX(v, false));

        (Action, double)[] click(Func<Vector2> to, MouseButton b = MouseButton.Left) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(to()), 50),
            (() => im.PressButton(b), 50),
            (() => im.ReleaseButton(b), 70),
        };

        // the default binding is F (smoke runs simulate US QWERTY, so the physical key is F too)
        (Action, double)[] key(Key k, bool down) => new (Action, double)[] { (() => { if (down) im.PressKey(k); else im.ReleaseKey(k); }, 80) };
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        LaserSection? section() => ed().Session.Chart.Notes.Lasers[0].FirstOrDefault(s => s.Y == p + 2 * u);
        string hint() => ed().Toolbox.LaserTile.Hint2.Value;
        string anchors(LaserPath? path) => path == null ? "none" : string.Join(" ", path.Anchors.Select(a =>
            inv($"[{(double)a.Ry / u:0.##}u {a.V}{(a.IsSlam ? $">{a.Vf}" : "")} {a.Type?.ToString() ?? "-"}{(a.Auto ? "*" : "")}]")));
        string state() => inv($"classic {ed().Tools.ClassicLaserHeld.Value}; hint '{hint()}'; placement {anchors(ed().Compose.Placement?.Path)}; laser {(section() is { } s ? string.Join(" ", s.Points.Select(pt => $"{pt.V}{(pt.IsSlam ? $">{pt.Vf}" : "")}")) : "none")}; tool {ed().Tools.EffectiveTool.Value}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");

        return new List<(Action, string?)>
        {
            (() =>
            {
                p = start();
                u = unit();
                before = KsonWriter.Write(ed().Session.Chart);
                expect(ed().Session.Chart.Notes.Lasers[0].All(s => s.EndY < p + 2 * u || s.Y > p + 6 * u), $"{label}: laser L is free at units 2..6 for the classic steps ({state()})");
                ed().SelectTool(EditorTool.Laser);
                ed().Tools.LaserSide.Value = 0;
                ed().Compose.WideLaser.Value = false;
                ed().Session.Selection.Clear();
                expect(hint() == "F: classic", $"{label}: the laser tile advertises the classic-laser key ({state()})");
            }, null),

            // F held: plain points on the 1/10 grid, a slam, the end clicked again finishes
            (() => inputSequence($"{label}: classic placement", then(key(Key.F, true), check(() =>
            {
                expect(ed().Tools.ClassicLaserHeld.Value && hint() == "classic lasers", $"{label}: holding F turns on classic lasers, the tile says so ({state()})");
            }), click(() => at(2, 0.33)), click(() => at(3, 0.68)), click(() => at(4, 0.12)), check(() =>
            {
                var pl = ed().Compose.Placement;
                expect(pl != null && pl.Path.Anchors.Select(a => a.V).SequenceEqual(new[] { 0.3, 0.7, 0.1 }) && pl.Path.Segments().All(s => s.Effective == LaserCurveType.Linear),
                    $"{label}: three classic clicks are three plain points on the 1/10 grid, straight segments (anchor clicks would make a perfect curve) ({state()})");
            }), click(() => at(4, 0.88)), check(() =>
            {
                expect(ed().Compose.Placement?.Path.Anchors[^1] is { IsSlam: true, V: 0.1, Vf: 0.9 }, $"{label}: a click on the last beat with another value is a slam 0.1 -> 0.9 ({state()})");
            }))), null),
            (() => { }, shot("placing")),
            (() => inputSequence($"{label}: classic finish", then(click(() => at(4, 0.9)), check(() =>
            {
                var s = section();
                expect(ed().Compose.Placement == null && s != null && s.Path == null && s.Points.Count == 3 && s.Points[2] is { IsSlam: true, V: 0.1, Vf: 0.9 }
                       && s.Points[0].V == 0.3 && s.Points[1].V == 0.7 && ed().Session.Commands.UndoDescription == "Add laser",
                    $"{label}: clicking the slam's end again finished the laser: plain points 0.3, 0.7, 0.1>0.9, no stored path, one undo step ({state()})");
            }), key(Key.F, false), check(() =>
            {
                expect(!ed().Tools.ClassicLaserHeld.Value && hint() == "F: classic", $"{label}: releasing F turns classic lasers off ({state()})");
            }))), null),

            // another key while F is down uses it up until F is released
            (() => inputSequence($"{label}: F used up by another key", then(key(Key.F, true), check(() =>
            {
                expect(ed().Tools.ClassicLaserHeld.Value, $"{label}: F down again ({state()})");
            }), key(Key.K, true), key(Key.K, false), check(() =>
            {
                expect(!ed().Tools.ClassicLaserHeld.Value && hint() == "F: classic", $"{label}: after another key F does nothing until released ({state()})");
            }), key(Key.F, false), check(() =>
            {
                ed().TriggerAction(EditorAction.Undo);
                expect(section() == null, $"{label}: one undo removes the classic laser ({state()})");
            }))), null),

            // modes mixed in one chain: anchor, anchor, classic (F), anchor, anchor
            (() => inputSequence($"{label}: modes mixed", then(click(() => at(2, 0.2)), click(() => at(3, 0.8)), key(Key.F, true), click(() => at(4, 0.32)), key(Key.F, false),
                click(() => at(5, 0.6)), click(() => at(6, 0.2)), check(() =>
            {
                var pl = ed().Compose.Placement;
                var segs = pl?.Path.Segments().Select(s => (s.Start, s.End, s.Effective)).ToList();
                expect(pl != null && Math.Abs(pl.Path.Anchors[2].V - 0.3) < 1e-9 && segs!.SequenceEqual(new[]
                    {
                        (0, 1, LaserCurveType.Linear), (1, 2, LaserCurveType.Linear), (2, 4, LaserCurveType.PerfectCurve),
                    }),
                    $"{label}: the classic point (0.3 on the 1/10 grid) joins straight and the two anchor clicks after it make a perfect curve from it ({state()})");
            }))), null),
            (() => { }, shot("mixed")),
            (() => inputSequence($"{label}: finish the mixed laser", then(click(() => at(6, 0.2), MouseButton.Right), check(() =>
            {
                var s = section();
                var path = s == null ? null : LaserPaths.ValidPath(s);
                expect(path != null && path.Anchors[2].Type == LaserCurveType.PerfectCurve && path.Anchors[1].Type == LaserCurveType.Linear,
                    $"{label}: right click put the mixed laser into the chart with its anchors ({anchors(path)}; {state()})");
                ed().TriggerAction(EditorAction.Undo);
                expect(KsonWriter.Write(ed().Session.Chart) == before, $"{label}: undo restores the chart ({state()})");
                report($"compose-input ({label}): classic lasers - tile hint, F held: 1/10 grid plain points, slam, finish on the end, release, used up by another key, mixed chain anchor/classic/anchor typing, undo - ok");
            }))), null),
        };
    }
}
