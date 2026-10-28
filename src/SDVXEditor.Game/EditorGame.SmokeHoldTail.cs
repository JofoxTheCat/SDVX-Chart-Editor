using System.Globalization;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game;

/// <summary>
/// Compose-input smoke, dragging a hold by its tail through real input (select tool): hovering the tail shows the resize
/// hint; tail drag longer / shorter / onto the start (= chip) keeps the start and is one undo step each; a body drag still
/// moves the hold; with two holds selected a tail drag changes both by the same delta (one undo step). Uses the BT-A holds
/// the hold-modifier steps left at units 4..5 and 8..9 (runs right after them). Undone by the compose-input undo loop.
/// </summary>
public partial class EditorGame
{
    private List<(Action Act, string? Shot)> holdTailInputSteps(Func<EditorScreen> ed, string label, Func<long> start, Func<long> unit,
                                                                 Func<long, float, Vector2> screen)
    {
        var im = SmokeInput ?? throw new InvalidOperationException("smoke test: no smoke input manager");
        string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        bool shots = label.StartsWith("no reference, borderless", StringComparison.Ordinal);
        long p = 0, b = 240;
        float laneX = PlayfieldLayout.LaneLeft(NoteKind.Bt, 0) + PlayfieldLayout.LaneWidth(NoteKind.Bt) / 2;
        Vector2 at(double units) => screen(p + (long)Math.Round(units * b), laneX);
        Note? first = null, second = null;

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
        string len(Note? n) => n == null ? "none" : inv($"y {(double)(n.Y - p) / b:0.##}, {(double)n.Length / b:0.##} units");
        string state() => inv($"first {len(first)}; second {len(second)}; tool {ed().Tools.EffectiveTool.Value}; tail hint {(ed().Compose.TailHint is { } t ? len(t.Note) : "none")}; toast '{ed().LastToast}'; undo {ed().Session.Commands.UndoDescription}");
        void undo() => ed().TriggerAction(EditorAction.Undo);

        return new List<(Action, string?)>
        {
            (() =>
            {
                p = start();
                b = unit();
                var lane = ed().Session.Chart.Notes.Bt[0];
                first = lane.FirstOrDefault(n => n.Y == p + 4 * b);
                second = lane.FirstOrDefault(n => n.Y == p + 8 * b);
                expect(first is { Length: var l1 } && l1 == b && second is { Length: var l2 } && l2 == b, $"{label}: the BT-A holds for the tail drag are there ({state()})");
                ed().SelectTool(EditorTool.Select);
                ed().Session.Selection.Clear();
            }, null),

            // hover: the tail shows the resize hint, the body does not
            (() => inputSequence($"{label}: tail hover hint", then(check(() => im.MoveMouseTo(at(4.5))), check(() => { }), (new (Action, double)[] { (() => im.MoveMouseTo(at(5)), 80) }), check(() =>
                expect(ed().Compose.TailHint is { } t && t.Note == first, $"{label}: hovering the hold's end shows the resize hint on it ({state()})")))), shots ? "14-hold-tail-hover" : null),

            // longer, then undo
            (() => inputSequence($"{label}: tail drag longer", then(drag(() => at(5), () => at(7)), check(() =>
            {
                expect(first!.Y == p + 4 * b && first.Length == 3 * b && ed().Session.Commands.UndoDescription == "Hold length",
                    $"{label}: select tool, tail dragged from 5 to 7: start kept, 3 units long, one 'Hold length' step ({state()})");
                undo();
                expect(first.Y == p + 4 * b && first.Length == b, $"{label}: undo restores the 1-unit hold ({state()})");
            }))), null),

            // longer, then shorter, then onto the start = chip
            (() => inputSequence($"{label}: tail drag shorter / to a chip", then(drag(() => at(5), () => at(7)), drag(() => at(7), () => at(6)), check(() =>
                expect(first!.Y == p + 4 * b && first.Length == 2 * b, $"{label}: the tail dragged back from 7 to 6 made it 2 units ({state()})")),
                drag(() => at(6), () => at(4)), check(() =>
            {
                expect(first!.Y == p + 4 * b && first.Length == 0 && ed().Session.Commands.UndoDescription == "Hold to chip",
                    $"{label}: the tail dragged onto the start made it a chip ('Hold to chip') ({state()})");
                undo();
                expect(first.Length == 2 * b, $"{label}: undo makes it the 2-unit hold again ({state()})");
            }))), null),

            // the body still moves the hold
            (() => inputSequence($"{label}: body drag moves", then(drag(() => at(5), () => at(6)), check(() =>
            {
                expect(first!.Y == p + 5 * b && first.Length == 2 * b && ed().Session.Commands.UndoDescription == "Move",
                    $"{label}: a drag on the hold's body moved it one unit, length kept ({state()})");
                undo();
                expect(first.Y == p + 4 * b, $"{label}: undo moves it back ({state()})");

                // both holds selected for the multi drag
                var sel = ed().Session.Selection;
                sel.Clear();
                sel.Add(new NoteRef(NoteKind.Bt, 0, first));
                sel.Add(new NoteRef(NoteKind.Bt, 0, second!));
            }))), null),

            // multi-selection: one tail, same delta for both
            (() => inputSequence($"{label}: tail drag with two holds selected", then(drag(() => at(6), () => at(7)), check(() =>
            {
                expect(first!.Length == 3 * b && second!.Length == 2 * b && first.Y == p + 4 * b && second.Y == p + 8 * b,
                    $"{label}: dragging one selected tail one unit longer made both selected holds one unit longer ({state()})");
                undo();
                expect(first.Length == 2 * b && second!.Length == b, $"{label}: one undo restores both ({state()})");
                ed().Session.Selection.Clear();
                report($"compose-input ({label}): hold tail - hover hint, longer / shorter / onto the start = chip, undo, body drag still moves, two selected holds by the same delta - ok");
            }))), null),
        };
    }
}
