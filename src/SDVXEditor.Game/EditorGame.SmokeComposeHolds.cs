using System.Globalization;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Compose-input smoke through the REAL input path (mouse and Shift injected into <see cref="SmokeInput"/>):
/// <list type="bullet">
/// <item>osu!mania chip &lt;-&gt; hold: the hold tool dragged from a chip makes it a hold (undo / redo), pressed on a hold's
/// body drags its length, its tail dragged back to the start leaves a chip, a click without movement changes nothing;
/// the chip tool on a hold makes it a chip; the same for FX through the Shift swap; a drag into another note stops at
/// the last length that fits (OverlapRule).</item>
/// <item>Wide-laser anchors in the overhang (value 0.04, left of the playfield drawable): hover shows its anchors, right
/// click opens the menu, click selects, drag moves it; the laser tool hovers and drags it too; the hover preview of a
/// normal laser in laser mode; with "Show laser anchors on hover" off the hover outlines but shows no anchors.</item>
/// </list>
/// Everything here is taken back by the undo loop at the end of the compose-input part.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> holdInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                             Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        long p = 0, b = 240;
        float lane(NoteKind kind, int l) => PlayfieldLayout.LaneLeft(kind, l) + PlayfieldLayout.LaneWidth(kind) / 2;
        Vector2 bt(int l, double beats) => screen(p + (long)Math.Round(beats * b), lane(NoteKind.Bt, l));
        Vector2 fxR(double beats) => screen(p + (long)Math.Round(beats * b), lane(NoteKind.Fx, 1));

        (Action, double)[] click(Func<Vector2> to) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(to()), 50),
            (() => im.PressButton(MouseButton.Left), 50),
            (() => im.ReleaseButton(MouseButton.Left), 70),
        };

        (Action, double)[] drag(Func<Vector2> from, Func<Vector2> to) => new (Action, double)[]
        {
            (() => im.MoveMouseTo(from()), 50),
            (() => im.PressButton(MouseButton.Left), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.3f)), 50),
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.7f)), 50),
            (() => im.MoveMouseTo(to()), 50),
            (() => im.ReleaseButton(MouseButton.Left), 70),
        };

        (Action, double)[] key(Key k, bool down) => new (Action, double)[] { (() => { if (down) im.PressKey(k); else im.ReleaseKey(k); }, 60) };
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        Note? note(NoteKind kind, int l, double beats) => ed().Session.Chart.Notes.Lane(kind, l).FirstOrDefault(n => n.Y == p + (long)Math.Round(beats * b));
        int count(NoteKind kind, int l) => ed().Session.Chart.Notes.Lane(kind, l).Count(n => n.Y >= p);
        string len(Note? n) => n == null ? "none" : inv($"{(double)n.Length / b:0.##} beats");
        string state() => inv($"tool {ed().Tools.EffectiveTool.Value}; shift {ed().Tools.ShiftHeld.Value}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");
        string? undoBefore = null;

        return new List<(Action, string?)>
        {
            (() =>
            {
                p = start();
                b = unit();
                ed().Session.Selection.Clear();
                ed().SelectTool(EditorTool.BtHold);
                // BT-D has its chip at 3 beats (the BT chip step), FX-R at 5 beats
                expect(note(NoteKind.Bt, 3, 3) is { Length: 0 } && note(NoteKind.Fx, 1, 5) is { Length: 0 }, $"{label}: chips for chip -> hold are there ({state()})");
            }, null),

            // BT: chip -> hold by dragging with the hold tool, undo, redo
            (() => inputSequence($"{label}: BT chip -> hold", then(drag(() => bt(3, 3), () => bt(3, 5)), check(() =>
            {
                var n = note(NoteKind.Bt, 3, 3);
                expect(n != null && n.Length == 2 * b && count(NoteKind.Bt, 3) == 1 && ed().Session.Commands.UndoDescription == "Chip to hold",
                    $"{label}: the hold tool dragged from a BT chip made it a 2-beat hold at the chip's pulse, one undo step ({len(n)}, notes {count(NoteKind.Bt, 3)}; {state()})");
                ed().TriggerAction(EditorAction.Undo);
                expect(note(NoteKind.Bt, 3, 3) is { Length: 0 } && count(NoteKind.Bt, 3) == 1, $"{label}: one undo makes it the chip again ({len(note(NoteKind.Bt, 3, 3))})");
                ed().TriggerAction(EditorAction.Redo);
                expect(note(NoteKind.Bt, 3, 3) is { Length: var l } && l == 2 * b, $"{label}: redo makes it the hold again");
            }))), null),

            // the hold tool on a hold's body drags its length (osu!)
            (() => inputSequence($"{label}: BT hold length", then(drag(() => bt(3, 4), () => bt(3, 6)), check(() =>
            {
                var n = note(NoteKind.Bt, 3, 3);
                expect(n != null && n.Length == 3 * b && count(NoteKind.Bt, 3) == 1 && ed().Session.Commands.UndoDescription == "Hold length",
                    $"{label}: dragging the hold's body to 6 beats made it 3 beats long, start kept ({len(n)}; {state()})");
                ed().SelectTool(EditorTool.BtChip);
            }))), null),

            // the chip tool on a hold: back to a chip at the hold's start
            (() => inputSequence($"{label}: BT hold -> chip (chip tool)", then(click(() => bt(3, 5)), check(() =>
            {
                var n = note(NoteKind.Bt, 3, 3);
                expect(n is { Length: 0 } && count(NoteKind.Bt, 3) == 1 && ed().Session.Commands.UndoDescription == "Hold to chip",
                    $"{label}: a chip-tool click on the hold's body made it a chip at its start ({len(n)}, notes {count(NoteKind.Bt, 3)}; {state()})");
                ed().SelectTool(EditorTool.BtHold);
            }))), null),

            // hold tool: chip -> 1-beat hold, then its tail dragged back to the start = chip; a click alone changes nothing
            (() => inputSequence($"{label}: BT tail back to zero", then(drag(() => bt(3, 3), () => bt(3, 4)), check(() =>
                expect(note(NoteKind.Bt, 3, 3) is { Length: var l } && l == b, $"{label}: chip -> 1-beat hold ({len(note(NoteKind.Bt, 3, 3))}; {state()})")),
                drag(() => bt(3, 4), () => bt(3, 3)), check(() =>
            {
                expect(note(NoteKind.Bt, 3, 3) is { Length: 0 } && count(NoteKind.Bt, 3) == 1 && ed().Session.Commands.UndoDescription == "Hold to chip",
                    $"{label}: dragging the hold's tail back to its start leaves a chip ({len(note(NoteKind.Bt, 3, 3))}; {state()})");
                undoBefore = ed().Session.Commands.UndoDescription + "#" + ed().Session.Commands.History.Count();
            }), click(() => bt(3, 3)), check(() =>
                expect(note(NoteKind.Bt, 3, 3) is { Length: 0 } && ed().Session.Commands.UndoDescription + "#" + ed().Session.Commands.History.Count() == undoBefore,
                    $"{label}: a hold-tool click on a chip without dragging keeps the chip and adds no undo step ({state()})")))), null),

            // OverlapRule: a drag past another chip stops at the last length that fits (end on the chip = touching, allowed)
            (() => inputSequence($"{label}: BT hold drag into a chip", then(check(() => ed().SelectTool(EditorTool.BtChip)), click(() => bt(3, 5)),
                check(() => ed().SelectTool(EditorTool.BtHold)), drag(() => bt(3, 3), () => bt(3, 7)), check(() =>
            {
                var n = note(NoteKind.Bt, 3, 3);
                expect(n != null && n.Length > 0 && n.EndY <= p + 5 * b && note(NoteKind.Bt, 3, 5) is { Length: 0 } && count(NoteKind.Bt, 3) == 2,
                    $"{label}: a hold dragged past the next chip stops before it, the chip stays ({len(n)}, chip at 5: {len(note(NoteKind.Bt, 3, 5))}; {state()})");
            }))), null),

            // FX through the Shift swap: hold tool -> FX hold tool, chip tool -> FX chip tool
            (() => inputSequence($"{label}: FX chip -> hold (Shift)", then(key(Key.LShift, true), check(() =>
                expect(ed().Tools.EffectiveTool.Value == EditorTool.FxHold, $"{label}: Shift turns the BT hold tool into FX hold ({state()})")),
                drag(() => fxR(5), () => fxR(7)), check(() =>
            {
                var n = note(NoteKind.Fx, 1, 5);
                expect(n != null && n.Length == 2 * b && count(NoteKind.Fx, 1) == 1 && note(NoteKind.Fx, 0, 5) == null && ed().Session.Commands.UndoDescription == "Chip to hold",
                    $"{label}: Shift + hold tool dragged from the FX-R chip made it a 2-beat FX hold ({len(n)}; {state()})");
                ed().TriggerAction(EditorAction.Undo);
                expect(note(NoteKind.Fx, 1, 5) is { Length: 0 }, $"{label}: undo makes it the FX chip again");
                ed().TriggerAction(EditorAction.Redo);
            }), key(Key.LShift, false), check(() => ed().SelectTool(EditorTool.BtChip)), key(Key.LShift, true), check(() =>
                expect(ed().Tools.EffectiveTool.Value == EditorTool.FxChip, $"{label}: Shift turns the BT chip tool into FX chip ({state()})")),
                click(() => fxR(6)), check(() =>
            {
                var n = note(NoteKind.Fx, 1, 5);
                expect(n is { Length: 0 } && count(NoteKind.Fx, 1) == 1 && ed().Session.Commands.UndoDescription == "Hold to chip",
                    $"{label}: Shift + chip tool on the FX hold made it a chip at its start ({len(n)}; {state()})");
            }), key(Key.LShift, false), check(() => ed().SelectTool(EditorTool.FxHold)),
                drag(() => fxR(5), () => fxR(6)), drag(() => fxR(6), () => fxR(5)), check(() =>
            {
                var n = note(NoteKind.Fx, 1, 5);
                expect(n is { Length: 0 } && count(NoteKind.Fx, 1) == 1 && ed().Session.Commands.UndoDescription == "Hold to chip",
                    $"{label}: FX hold tool: chip -> hold -> tail back to the start = chip ({len(n)}; {state()})");
                report($"compose-input ({label}): chip <-> hold - BT/FX chip -> hold by drag (undo/redo), hold length by body drag, chip tool on a hold, tail back to zero, click keeps a chip, drag stops at the next chip, Shift swap - ok");
            }))), null),
        };
    }

    private List<(Action Act, string? Shot)> wideAnchorInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                                   Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        bool shots = label.StartsWith("no reference, borderless", StringComparison.Ordinal);
        string? shot(string name) => shots ? "12-wide-" + name : null;
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        long p = 0, b = 240;
        long wideY() => p + 12 * b;

        LaserSection? wide() => ed().Session.Chart.Notes.Lasers[0].FirstOrDefault(s => s.Y == wideY());
        LaserSection? normal() => ed().Session.Chart.Notes.Lasers[0].FirstOrDefault(s => s.Y == p + 8 * b);
        Vector2 anchor(double v) => screen(wideY(), PlayfieldLayout.LaserX(v, true));
        Vector2 away() => screen(p + 6 * b, PlayfieldLayout.BT_LEFT + 2 * PlayfieldLayout.BT_LANE);

        (Action, double)[] move(Func<Vector2> to) => new (Action, double)[] { (() => im.MoveMouseTo(away()), 50), (() => im.MoveMouseTo(to()), 60) };

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
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.5f)), 50),
            (() => im.MoveMouseTo(to()), 50),
            (() => im.ReleaseButton(MouseButton.Left), 70),
        };

        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        string state()
        {
            var c = ed().Compose;
            string sec((int Lane, LaserSection Section)? s) => s is { } x ? inv($"{x.Lane}@{x.Section.Y} w{x.Section.Width}") : "-";
            return inv($"hover {(c.Hover is { } h ? $"x {h.X:0.0}" : "none")}; hovered {sec(c.HoveredLaser)}; preview {sec(c.AnchorPreview)}; anchor {c.HoveredAnchor?.Index.ToString() ?? "-"}; active {sec(c.ActiveLaser)}; tool {ed().Tools.EffectiveTool.Value}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}; {queueAt(ed().Playfield)}");
        }

        bool hovering(LaserSection? s, bool preview, int? anchorIndex)
        {
            var c = ed().Compose;
            return s != null && c.HoveredLaser is { } h && h.Section == s
                   && (preview ? c.AnchorPreview is { } pv && pv.Section == s : c.AnchorPreview == null)
                   && (anchorIndex == null || c.HoveredAnchor is { } a && a.Section == s && a.Index == anchorIndex);
        }

        var setting = config.GetBindable<bool>(EditorSetting.ShowLaserAnchorsOnHover);
        bool settingBefore = true;

        return new List<(Action, string?)>
        {
            (() =>
            {
                p = start();
                b = unit();
                settingBefore = setting.Value;
                setting.Value = true;
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
                var s = wide();
                expect(s is { Width: 2 } && Math.Abs(s.Points[0].V - 0.04) < 1e-9, $"{label}: the wide laser starts at 0.04 ({(s == null ? "none" : inv($"w{s.Width} {s.Points[0].V}"))})");
                float x = PlayfieldLayout.LaserX(0.04, true);
                var sp = anchor(0.04);
                expect(x < 0 && ed().ComposeContent.PlayfieldContainer.ScreenSpaceDrawQuad.Contains(sp),
                    inv($"{label}: the 0.04 anchor lies left of the playfield drawable (x {x:0.0}) inside the overhang space"));
            }, null),

            // select tool: hover preview, right-click menu, click selects, drag moves - all in the left overhang
            (() => inputSequence($"{label}: wide anchor in the overhang: hover", then(move(() => anchor(0.04)), check(() =>
                expect(hovering(wide(), true, 0), $"{label}: hovering the wide laser's 0.04 anchor (left overhang) previews its anchors, anchor 1 hot ({state()})")))), shot("hover")),
            (() => inputSequence($"{label}: wide anchor in the overhang: right click", then(click(() => anchor(0.04), MouseButton.Right), check(() =>
                expect(ed().Playfield.OpenLaserMenu != null, $"{label}: right click on the overhang anchor opens the laser menu ({state()})")))), shot("menu")),
            (() =>
            {
                Vector2 item(string text) => ed().Playfield.OpenLaserMenu!.ChildrenOfType<LaserContextMenuItem>().First(i => i.Label == text).ScreenSpaceDrawQuad.Centre;
                inputSequence($"{label}: wide anchor menu: Straight", then(click(() => item("Straight")), check(() =>
                {
                    var s = wide();
                    expect(ed().Playfield.OpenLaserMenu == null && s is { Width: 2 } && Math.Abs(s.Points[0].V - 0.04) < 1e-9,
                        $"{label}: the menu item ran and closed the menu, the wide laser is unchanged in shape ({state()})");
                })));
            }, null),
            (() => inputSequence($"{label}: wide anchor in the overhang: click + drag", then(click(() => anchor(0.04)), check(() =>
                expect(ed().Compose.ActiveLaser is { } a && a.Section == wide(), $"{label}: a click on the overhang anchor selects the wide laser ({state()})")),
                new (Action, double)[] { (() => im.PressKey(Key.LShift), 50) }, drag(() => anchor(0.04), () => anchor(0.02)), new (Action, double)[] { (() => im.ReleaseKey(Key.LShift), 50) }, check(() =>
            {
                var s = wide();
                expect(s is { Width: 2 } && Math.Abs(s.Points[0].V - 0.02) < 1e-9 && ed().Session.Commands.UndoDescription == "Move anchor",
                    $"{label}: dragging the overhang anchor to 0.02 moved it ({(s == null ? "none" : inv($"{s.Points[0].V}"))}; {state()})");
                ed().Session.Selection.Clear();
                ed().SelectTool(EditorTool.Laser);
                ed().Tools.LaserSide.Value = 1; // the laser tool prefers, but is not limited to, the active side
            }))), shot("dragged")),

            // laser tool: hover preview of the wide anchor and of a normal laser, drag in the overhang
            (() => inputSequence($"{label}: laser tool: wide anchor hover + drag", then(move(() => anchor(0.02)), check(() =>
                expect(hovering(wide(), true, 0), $"{label}: with the laser tool, hovering the overhang anchor previews the wide laser's anchors ({state()})")),
                move(() => screen(p + 9 * b, PlayfieldLayout.LaserX(0.5, false))), check(() =>
                expect(hovering(normal(), true, null), $"{label}: with the laser tool, hovering a normal laser's path previews its anchors ({state()})")),
                new (Action, double)[] { (() => im.PressKey(Key.LShift), 50) }, drag(() => anchor(0.02), () => anchor(0.06)), new (Action, double)[] { (() => im.ReleaseKey(Key.LShift), 50) }, check(() =>
            {
                var s = wide();
                expect(s is { Width: 2 } && Math.Abs(s.Points[0].V - 0.06) < 1e-9, $"{label}: the laser tool dragged the overhang anchor to 0.06 ({(s == null ? "none" : inv($"{s.Points[0].V}"))}; {state()})");
                setting.Value = false;
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
            }))), null),

            // setting off: hover still outlines, but no anchors
            (() => inputSequence($"{label}: hover anchors off", then(move(() => anchor(0.06)), check(() =>
                expect(hovering(wide(), false, null), $"{label}: with 'Show laser anchors on hover' off, hovering outlines the laser but shows no anchors ({state()})")),
                check(() => ed().SelectTool(EditorTool.Laser)), move(() => screen(p + 9 * b, PlayfieldLayout.LaserX(0.5, false))), check(() =>
            {
                expect(ed().Compose.AnchorPreview == null, $"{label}: ... nor with the laser tool ({state()})");
                setting.Value = settingBefore;
                ed().SelectTool(EditorTool.Select);
                report($"compose-input ({label}): wide laser anchor at 0.04 in the left overhang - hover preview, right-click menu, click select, drag (select and laser tool); normal laser hover preview; setting off = no hover anchors - ok");
            }))), shot("setting-off")),
        };
    }
}
