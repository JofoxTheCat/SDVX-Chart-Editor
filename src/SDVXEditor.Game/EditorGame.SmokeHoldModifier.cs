using System.Globalization;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game;

/// <summary>
/// Compose-input smoke, the hold modifier through real input (keys and mouse injected into the smoke input manager),
/// once with Ctrl and once with Alt chosen in the options:
/// <list type="bullet">
/// <item>BT Chip + modifier: the BT Hold tile lights, the chip tile says "now Hold", a drag places a BT hold.</item>
/// <item>BT Chip + modifier + Shift: FX hold. FX Chip + modifier: FX hold. A plain click with BT Chip: a chip.</item>
/// <item>Ctrl+Z with BT Chip active only undoes; the modifier is used up by the combination, so the next click while it is
/// still down places a chip (Alt: the same after Alt+wheel zoom).</item>
/// <item>Alt chosen: Ctrl alone does nothing; Alt pressed and released alone leaves the keyboard working (key 3).</item>
/// </list>
/// Area: units 4..13 of the compose-input stretch, lanes the earlier steps leave free. Undone by the compose-input undo loop.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> holdModifierInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                                     Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        bool shots = label.StartsWith("no reference, borderless", StringComparison.Ordinal);
        long p = 0, b = 240;
        float lane(NoteKind kind, int l) => PlayfieldLayout.LaneLeft(kind, l) + PlayfieldLayout.LaneWidth(kind) / 2;
        Vector2 at(NoteKind kind, int l, int beats) => screen(p + beats * b, lane(kind, l));
        var modifierBefore = HoldModifierKey.Ctrl;
        float zoomBefore = 1;
        string chartBefore = "";

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
            (() => im.MoveMouseTo(Vector2.Lerp(from(), to(), 0.5f)), 50),
            (() => im.MoveMouseTo(to()), 50),
            (() => im.ReleaseButton(MouseButton.Left), 70),
        };

        (Action, double)[] key(Key k, bool down) => new (Action, double)[] { (() => { if (down) im.PressKey(k); else im.ReleaseKey(k); }, 60) };
        (Action, double)[] wheel(int direction) => new (Action, double)[] { (() => im.ScrollVerticalBy(direction), 80) };
        (Action, double)[] then(params (Action, double)[][] parts) => parts.SelectMany(a => a).ToArray();
        (Action, double)[] check(Action a) => new (Action, double)[] { (a, 0d) };

        Note? note(NoteKind kind, int l, int beats) => ed().Session.Chart.Notes.Lane(kind, l).FirstOrDefault(n => n.Y == p + beats * b);
        string len(Note? n) => n == null ? "none" : inv($"{(double)n.Length / b:0.##} units");
        ToolTile tile(EditorTool t) => ed().Toolbox.TileFor(t);
        string state() => inv($"tool {ed().Tools.Tool.Value} -> {ed().Tools.EffectiveTool.Value}; hold {ed().Tools.HoldHeld.Value}; shift {ed().Tools.ShiftHeld.Value}; chip hint '{tile(EditorTool.BtChip).Hint2.Value}'; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");

        void expectTool(EditorTool tool, string what) =>
            expect(ed().Tools.EffectiveTool.Value == tool && tile(tool).Active.Value, $"{label}: {what} ({state()})");

        List<(Action, string?)> round(HoldModifierKey modifier, int c)
        {
            Key mod = modifier == HoldModifierKey.Alt ? Key.AltLeft : Key.ControlLeft;
            string name = modifier.KeyLabel();
            string l2 = $"{label}, {name}";

            return new List<(Action, string?)>
            {
                (() =>
                {
                    config.SetValue(EditorSetting.HoldModifier, modifier);
                    ed().SelectTool(EditorTool.BtChip);
                    expect(ed().Tools.HoldModifier.Value == modifier && tile(EditorTool.BtChip).Hint2.Value == name + ": Hold" && tile(EditorTool.FxChip).Hint2.Value == name + ": Hold"
                           && tile(EditorTool.BtHold).Hint2.Value == "", $"{l2}: the chip tiles advertise '{name}: Hold', the hold tiles nothing ({state()})");
                }, null),

                // BT Chip + modifier: BT hold (tile and hint live), then released: chip again
                (() => inputSequence($"{l2}: BT chip + {name} = BT hold", then(check(() => im.MoveMouseTo(at(NoteKind.Bt, 0, c + 1))), key(mod, true), check(() =>
                {
                    expectTool(EditorTool.BtHold, $"{name} held turns the BT chip tool into BT hold (lit tile)");
                    expect(tile(EditorTool.BtChip).Hint2.Value == "now Hold", $"{l2}: the chip tile says 'now Hold' ({state()})");
                }))), shots && modifier == HoldModifierKey.Ctrl ? "13-hold-modifier-ctrl" : null),
                (() => inputSequence($"{l2}: BT hold by {name}+drag", then(drag(() => at(NoteKind.Bt, 0, c), () => at(NoteKind.Bt, 0, c + 1)), key(mod, false), check(() =>
                {
                    var n = note(NoteKind.Bt, 0, c);
                    expect(n != null && n.Length == b, $"{l2}: {name}+drag with the BT chip tool placed a 1-unit BT hold ({len(n)}; {state()})");
                    expectTool(EditorTool.BtChip, $"releasing {name} restores the BT chip tool");
                    expect(tile(EditorTool.BtChip).Hint2.Value == name + ": Hold", $"{l2}: the hint is back ({state()})");
                }))), null),

                // + Shift: FX hold
                (() => inputSequence($"{l2}: {name}+Shift = FX hold", then(key(mod, true), key(Key.ShiftLeft, true), check(() =>
                    expectTool(EditorTool.FxHold, $"{name}+Shift turn the BT chip tool into FX hold")),
                    drag(() => at(NoteKind.Fx, 0, c + 2), () => at(NoteKind.Fx, 0, c + 3)), key(Key.ShiftLeft, false), key(mod, false), check(() =>
                {
                    var n = note(NoteKind.Fx, 0, c + 2);
                    expect(n != null && n.Length == b && note(NoteKind.Bt, 0, c + 2) == null, $"{l2}: {name}+Shift+drag placed a 1-unit FX-L hold ({len(n)}; {state()})");
                    ed().SelectTool(EditorTool.FxChip);
                }))), null),

                // FX Chip + modifier: FX hold (on FX-R in the second round, the FX-L stretch above is taken)
                (() => inputSequence($"{l2}: FX chip + {name} = FX hold", then(key(mod, true), check(() =>
                    expectTool(EditorTool.FxHold, $"{name} turns the FX chip tool into FX hold")),
                    drag(() => at(NoteKind.Fx, c == 4 ? 0 : 1, c + 4), () => at(NoteKind.Fx, c == 4 ? 0 : 1, c + 5)), key(mod, false), check(() =>
                {
                    var n = note(NoteKind.Fx, c == 4 ? 0 : 1, c + 4);
                    expect(n != null && n.Length == b, $"{l2}: FX chip tool + {name}+drag placed a 1-unit FX hold ({len(n)}; {state()})");
                    expectTool(EditorTool.FxChip, $"releasing {name} restores the FX chip tool");
                    ed().SelectTool(EditorTool.BtChip);
                    chartBefore = KsonWriter.Write(ed().Session.Chart);
                }))), null),

                // plain click: still a chip
                (() => inputSequence($"{l2}: plain click = chip", then(click(() => at(NoteKind.Bt, 1, c)), check(() =>
                    expect(note(NoteKind.Bt, 1, c) is { Length: 0 }, $"{l2}: a plain click with the BT chip tool still places a chip ({len(note(NoteKind.Bt, 1, c))}; {state()})")))), null),

                // Ctrl+Z with the chip tool only undoes; the hold modifier is used up while it stays down
                (() => inputSequence($"{l2}: Ctrl+Z only undoes", then(check(() => im.MoveMouseTo(at(NoteKind.Bt, 2, c + 2))), key(Key.ControlLeft, true), key(Key.Z, true), key(Key.Z, false), check(() =>
                {
                    expect(KsonWriter.Write(ed().Session.Chart) == chartBefore, $"{l2}: Ctrl+Z with the BT chip tool active undid the chip and placed nothing ({state()})");
                    expectTool(EditorTool.BtChip, "after Ctrl+Z (Ctrl still down) the tool is the BT chip tool, no hold ghost");
                }), key(Key.ControlLeft, false), check(() => expectTool(EditorTool.BtChip, "Ctrl released after Ctrl+Z")))), null),

                modifier == HoldModifierKey.Ctrl
                    // Ctrl down, Z, then a click while Ctrl is still down: the Ctrl belonged to the undo, the click is a chip
                    ? (() => inputSequence($"{l2}: click after Ctrl+Z", then(key(Key.ControlLeft, true), key(Key.Z, true), key(Key.Z, false), click(() => at(NoteKind.Bt, 2, c + 1)), key(Key.ControlLeft, false), check(() =>
                        expect(note(NoteKind.Bt, 2, c + 1) is { Length: 0 }, $"{l2}: a click with Ctrl still down after Ctrl+Z places a chip, not a hold ({len(note(NoteKind.Bt, 2, c + 1))}; {state()})")))), null)
                    // Alt chosen: Ctrl alone changes nothing; Alt+wheel zoom uses Alt up (click = chip); Alt alone keeps the keyboard
                    : (() => inputSequence($"{l2}: Ctrl inert, Alt+wheel, Alt alone", then(key(Key.ControlLeft, true), check(() =>
                        expectTool(EditorTool.BtChip, "with Alt chosen, Ctrl alone leaves the BT chip tool")), key(Key.ControlLeft, false),
                        check(() =>
                        {
                            zoomBefore = ed().Zoom.Value;
                            im.MoveMouseTo(at(NoteKind.Bt, 2, c + 1));
                        }), key(Key.AltLeft, true), wheel(1), check(() =>
                        {
                            expect(Math.Abs(ed().Zoom.Value - zoomBefore) > 1e-4, $"{l2}: Alt+wheel over the playfield zoomed ({zoomBefore:0.00} -> {ed().Zoom.Value:0.00})");
                            expectTool(EditorTool.BtChip, "Alt+wheel used Alt up: no hold");
                        }), wheel(-1), check(() => ed().Zoom.Value = zoomBefore), click(() => at(NoteKind.Bt, 2, c + 1)), key(Key.AltLeft, false), check(() =>
                        expect(note(NoteKind.Bt, 2, c + 1) is { Length: 0 }, $"{l2}: a click with Alt still down after Alt+wheel places a chip ({len(note(NoteKind.Bt, 2, c + 1))}; {state()})")),
                        key(Key.AltLeft, true), key(Key.AltLeft, false), key(Key.Number3, true), key(Key.Number3, false), check(() =>
                        {
                            expect(ed().Tools.Tool.Value == EditorTool.BtHold, $"{l2}: after Alt pressed and released alone, key 3 still picks the BT hold tool ({state()})");
                            ed().SelectTool(EditorTool.BtChip);
                        }))), null),
            };
        }

        var steps = new List<(Action, string?)>
        {
            (() =>
            {
                p = start();
                b = unit();
                modifierBefore = config.Get<HoldModifierKey>(EditorSetting.HoldModifier);
                ed().Session.Selection.Clear();
            }, null),
        };

        // Ctrl round on units 4..9, Alt round on 8..13 (other lanes where they share units)
        steps.AddRange(round(HoldModifierKey.Ctrl, 4));
        steps.AddRange(round(HoldModifierKey.Alt, 8));
        steps.Add((() =>
        {
            config.SetValue(EditorSetting.HoldModifier, modifierBefore);
            report($"compose-input ({label}): hold modifier Ctrl and Alt - chip + modifier = hold, + Shift = FX hold, FX chip + modifier = FX hold, plain click = chip, Ctrl+Z only undoes, a used-up modifier places chips, Alt alone keeps the keyboard - ok");
        }, null));
        return steps;
    }
}
