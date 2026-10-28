using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game.Tutorial;

public static partial class TutorialChapters
{
    private static readonly Func<TutorialContext, string?> compose = TutorialNeed.Editor(EditorTab.Compose);
    private static readonly Action<TutorialContext> restoreCompose = TutorialNeed.RestoreEditor(EditorTab.Compose);

    /// <summary>Chapter 1: the compose tab — tools, snap, chips, holds, FX, select, copy/paste, mirror, undo.</summary>
    private static TutorialChapter chipsHoldsFx() => new TutorialChapter
    {
        Number = 1,
        Title = "Chips, Holds and FX",
        Summary = "Tools, beat snap, place and delete notes, select, copy, paste, mirror, undo",
        Minutes = TutorialPlan.MinutesOf(1),
        Requirement = compose,
        Restore = restoreCompose,
        Steps = c => new List<TutorialStep>
        {
            new TutorialStep("The playfield",
                "This is where you place notes. Time runs upwards: the white line near the bottom is \"now\", and notes move down to it while the song plays.\n\n"
                + "Lanes from left to right: laser L (blue), the four BT buttons A B C D, laser R (pink). FX notes are the wide orange ones under two BT lanes each: FX-L under A+B, FX-R under C+D.\n\n"
                + "Scroll with the mouse wheel to move through the song.")
            {
                Anchor = "editor.playfield",
                Enter = c => Show(c, at(0), at(1)),
            },

            new TutorialStep("Pick a tool",
                "The toolbox on the left: {key:ToolSelect} Select · {key:ToolBtChip} BT Chip · {key:ToolBtHold} BT Hold · {key:ToolFxChip} FX Chip · {key:ToolFxHold} FX Hold · {key:ToolLaser} Laser. "
                + "Press {key:ToolBtChip} (or click BT Chip).\n\n"
                + "Notes snap to the grid lines (top right): \"1/4\" means four lines per beat — white = beat, red = half beat, blue = quarter, purple = triplets. "
                + "For the first notes set 1/1, only whole beats: press {key:Divisor1}, or drag the slider (Ctrl + mouse wheel also changes it).")
            {
                Target = c => TutorialAnchors.Resolve(c.Editor?.Tools.Tool.Value == EditorTool.BtChip ? "compose.divisor" : "compose.tool.btchip"),
                Progress = c => c.Editor is { } e ? $"Tool: {(e.Tools.Tool.Value == EditorTool.BtChip ? "BT Chip ✓" : e.Tools.Tool.Value.ToString())} · beat snap: 1/{e.Compose.Divisor.Value}" : null,
                Done = c => c.Editor is { } e && e.Tools.Tool.Value == EditorTool.BtChip && e.Compose.Divisor.Value == 1,
                Demo = (d, _) =>
                {
                    d.Press(EditorAction.ToolBtChip);
                    d.Press(EditorAction.Divisor1);
                },
            },

            new TutorialStep("Delete a stray chip",
                "Someone left a chip in the first bar (red x). Right-click a note to delete it — that works with any tool.")
            {
                Enter = c => Show(c, at(0), at(1)),
                DeleteMarks = _ => new[] { TutorialCharts.StrayChip },
                Done = c => c.Chart is { } ch && !ch.Notes.Bt[TutorialCharts.StrayChip.Lane].Any(n => n.Y == TutorialCharts.StrayChip.Y),
                Demo = (d, c) => d.ClickAt(pf(c, TutorialCharts.StrayChip.Y, bt(TutorialCharts.StrayChip.Lane)), right: true),
            },

            new TutorialStep("Place chips",
                "The kick drum plays four times in this bar. Click each glowing box with the BT Chip tool. A box turns green when its chip is right.")
            {
                Enter = c => Show(c, at(0), at(1)),
                Ghosts = _ => TutorialCharts.Chips,
                Progress = c => placed(c, TutorialCharts.Chips, "chips"),
                Done = c => allPlaced(c, TutorialCharts.Chips),
                Demo = (d, c) =>
                {
                    foreach (var t in TutorialCharts.Chips) d.ClickAt(pf(c, t.Y, bt(t.Lane)));
                },
            },

            new TutorialStep("Holds",
                "Long notes are holds. Press {key:ToolBtHold} (BT Hold) and drag from the bottom of each box to its top.\n\n"
                + "Tip: with the hold tool, dragging on an existing chip pulls it into a hold (a hold's length changes the same way); "
                + "a click with the chip tool on a hold turns it back into a chip. Holding Ctrl (the hold modifier, Options → Input) with the chip tool also drags out a hold.")
            {
                Enter = c => Show(c, at(2), at(3)),
                Ghosts = _ => TutorialCharts.Holds,
                Progress = c => placed(c, TutorialCharts.Holds, "holds"),
                Done = c => allPlaced(c, TutorialCharts.Holds),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ToolBtHold);
                    foreach (var t in TutorialCharts.Holds) d.Drag(pf(c, t.Y, bt(t.Lane)), pf(c, t.EndY, bt(t.Lane)));
                },
            },

            new TutorialStep("FX chips",
                "FX buttons are the big ones below the BT buttons. Press {key:ToolFxChip} (FX Chip) and click the two boxes: the snare drum.\n\n"
                + "Tip: holding Shift turns the BT tools into FX tools while you hold it.")
            {
                Enter = c => Show(c, at(3), at(4)),
                Ghosts = _ => TutorialCharts.FxChips,
                Progress = c => placed(c, TutorialCharts.FxChips, "FX chips"),
                Done = c => allPlaced(c, TutorialCharts.FxChips),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ToolFxChip);
                    foreach (var t in TutorialCharts.FxChips) d.ClickAt(pf(c, t.Y, fx(t.Lane)));
                },
            },

            new TutorialStep("An FX hold",
                "Press {key:ToolFxHold} (FX Hold) and drag over the box. In the Effects chapter it will get a sound effect.")
            {
                Enter = c => Show(c, at(4), at(5)),
                Ghosts = _ => TutorialCharts.FxHold,
                Done = c => allPlaced(c, TutorialCharts.FxHold),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ToolFxHold);
                    foreach (var t in TutorialCharts.FxHold) d.Drag(pf(c, t.Y, fx(t.Lane)), pf(c, t.EndY, fx(t.Lane)));
                },
            },

            new TutorialStep("Select",
                "Press {key:ToolSelect} (Select). Drag a box around the four chips of the first bar: they turn blue. A click selects one note, Ctrl+click adds to the selection.")
            {
                Enter = c => Show(c, at(0), at(1)),
                Ghosts = _ => TutorialCharts.Chips,
                Progress = c => c.Session is { } s ? $"{TutorialCharts.Chips.Count(t => isSelected(s, t))} / 4 selected" : null,
                Done = c => c.Session is { } s && TutorialCharts.Chips.All(t => isSelected(s, t)),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ToolSelect);
                    d.Drag(pf(c, at(0) - beat / 2, PlayfieldLayout.BT_LEFT + 6), pf(c, at(0, 3.5), PlayfieldLayout.BT_RIGHT - 6));
                },
            },

            new TutorialStep("Copy and paste",
                "Copy them with {key:Copy}. Then move the white line to the start of bar 2 — mouse wheel, or → (one snap step) — and paste with {key:Paste}. The copy lands at the line. ({key:SeekStart} jumps to the very start.)")
            {
                Ghosts = _ => TutorialCharts.Pasted,
                Progress = c => placed(c, TutorialCharts.Pasted, "chips in bar 2"),
                Done = c => allPlaced(c, TutorialCharts.Pasted),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.Copy);
                    d.Press(EditorAction.SeekStart);
                    for (int i = 0; i < 4; i++) d.Press(EditorAction.SeekForward); // snap 1/1: one beat per press, from the first beat
                    d.Press(EditorAction.Paste);
                },
            },

            new TutorialStep("Mirror",
                "The pasted chips are selected. Press {key:MirrorHorizontal}: they flip left/right (A with D, B with C, FX-L with FX-R, and lasers too).\n\n"
                + "Every edit can be taken back: then press {key:Undo} to undo the mirror — and {key:Redo} to redo it.")
            {
                Enter = c => Show(c, at(1), at(2)),
                Ghosts = _ => TutorialCharts.Mirrored,
                Progress = c =>
                {
                    if (allPlaced(c, TutorialCharts.Mirrored)) c.Bag["mirrored"] = true;
                    else if (c.Bag.ContainsKey("mirrored") && allPlaced(c, TutorialCharts.Pasted)) c.Bag["undone"] = true;
                    return c.Bag.ContainsKey("undone") ? "Undone — now redo" : c.Bag.ContainsKey("mirrored") ? "Mirrored — now undo" : placed(c, TutorialCharts.Mirrored, "mirrored");
                },
                Done = c => c.Bag.ContainsKey("undone") && allPlaced(c, TutorialCharts.Mirrored),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.MirrorHorizontal);
                    d.WaitFor(() => c.Bag.ContainsKey("mirrored"), "the mirror");
                    d.Press(EditorAction.Undo);
                    d.WaitFor(() => c.Bag.ContainsKey("undone"), "the undo");
                    d.Press(EditorAction.Redo);
                },
            },

            new TutorialStep("Your turn",
                "Bar 6 (the highlighted stretch) is yours: place at least four BT chips, each on a beat. Any lanes you like — use {key:ToolBtChip} for BT Chip.")
            {
                Enter = c => Show(c, at(TutorialCharts.ChipPracticeMeasure), at(TutorialCharts.ChipPracticeMeasure + 1)),
                Zone = _ => (at(TutorialCharts.ChipPracticeMeasure), at(TutorialCharts.ChipPracticeMeasure + 1)),
                Progress = c => c.Chart is { } ch ? $"{Math.Min(4, TutorialCharts.CountBeatChips(ch, at(TutorialCharts.ChipPracticeMeasure), at(TutorialCharts.ChipPracticeMeasure + 1)))} / 4 chips on beats" : null,
                Done = c => c.Chart is { } ch && TutorialCharts.CountBeatChips(ch, at(TutorialCharts.ChipPracticeMeasure), at(TutorialCharts.ChipPracticeMeasure + 1)) >= 4,
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ToolBtChip);
                    for (int b = 0; b < 4; b++) d.ClickAt(pf(c, at(TutorialCharts.ChipPracticeMeasure, b), bt(3 - b)));
                },
            },

            saveStep(),
        },
    };

    private static bool isSelected(EditorSession s, TutorialTarget t) =>
        s.Selection.Notes.Any(r => r.Kind == t.Kind && r.Lane == t.Lane && r.Note.Y == t.Y);

    /// <summary>Chapter 2: lasers — tool and side, drag, slams by click placement, curves, anchors, wide lasers, practice.</summary>
    private static TutorialChapter lasers() => new TutorialChapter
    {
        Number = 2,
        Title = "Lasers",
        Summary = "Laser tool, L/R, straight lasers, slams, curves and anchors, wide lasers",
        Minutes = TutorialPlan.MinutesOf(2),
        Requirement = compose,
        Restore = restoreCompose,
        Steps = c => new List<TutorialStep>
        {
            new TutorialStep("Lasers",
                "Lasers are the paths the two knobs follow: blue = left knob, pink = right knob. Left edge = knob all the way left, right edge = all the way right.\n\n"
                + "Press {key:ToolLaser} for the laser tool. The tile shows L or R: the knob you draw for. Make sure it says L (press {key:LaserSideLeft}, or click L under the tile).")
            {
                Anchor = "compose.tool.laser",
                Enter = c => c.Manager.WhenEditorReady(() => c.Editor!.Compose.Divisor.Value = 4),
                Done = c => c.Editor is { } e && e.Tools.Tool.Value == EditorTool.Laser && e.Tools.LaserSide.Value == 0,
                Demo = (d, _) =>
                {
                    d.Press(EditorAction.ToolLaser);
                    d.Press(EditorAction.LaserSideLeft);
                },
            },

            new TutorialStep("A straight laser",
                "Drag from the start of the blue path (bottom) to its end (top): a laser sweeping from left to right. Time snaps to the grid; the side follows your mouse.")
            {
                Enter = c => Show(c, at(6), at(7)),
                Ghosts = _ => new[] { TutorialCharts.StraightLaser },
                Done = c => allPlaced(c, new[] { TutorialCharts.StraightLaser }),
                Demo = (d, c) =>
                {
                    var t = TutorialCharts.StraightLaser;
                    d.Drag(pf(c, t.Y, laser(0)), pf(c, t.EndY, laser(1)));
                },
            },

            new TutorialStep("A slam",
                "A slam is an instant jump of the knob (it makes a \"whoosh\" in the game). Switch to laser R: press {key:LaserSideRight}.\n\n"
                + "Now place by clicking: click the right end of the pink line, click its left end on the same beat — that is the slam — click the top end, then right-click to finish. "
                + "The two ends of a slam stay two points: later you can select, drag or delete each one on its own.")
            {
                Enter = c => Show(c, at(7), at(8)),
                Ghosts = _ => new[] { TutorialCharts.SlamLaser },
                Progress = c => c.Editor is { } e ? (e.Tools.LaserSide.Value == 1 ? "Laser R" : "Laser L — press " + TutorialContext.KeyOf(EditorAction.LaserSideRight) + " for R") + (e.Compose.Placement != null ? " · placing: right-click finishes" : "") : null,
                Done = c => allPlaced(c, new[] { TutorialCharts.SlamLaser }),
                Demo = (d, c) =>
                {
                    var t = TutorialCharts.SlamLaser;
                    d.Press(EditorAction.LaserSideRight);
                    d.ClickAt(pf(c, t.Y, laser(1)));
                    d.ClickAt(pf(c, t.Y, laser(0)));
                    d.ClickAt(pf(c, t.EndY, laser(0)));
                    d.ClickAt(pf(c, t.EndY, laser(0)), right: true);
                },
            },

            new TutorialStep("Curves",
                "Clicking places anchors, like osu! sliders: 2 anchors = straight, 3 = a perfect curve (an arc), 4 or more = a Bézier curve.\n\n"
                + "Switch back to L ({key:LaserSideLeft}) and click the three dots from bottom to top, then right-click to finish.\n\n"
                + "Right-click on the last point finishes; right-clicking further on adds one more anchor there first. "
                + "Hold F to place K-Shoot style: plain points with straight lines on a 1/10 grid; click the last point again to finish.")
            {
                Enter = c => Show(c, at(8), at(9)),
                Ghosts = _ => new[] { TutorialCharts.CurveLaser },
                Progress = c => c.Editor?.Compose.Placement is { } p ? $"{p.Path.Count} anchor{(p.Path.Count == 1 ? "" : "s")} placed" : null,
                Done = c => allPlaced(c, new[] { TutorialCharts.CurveLaser }),
                Demo = (d, c) =>
                {
                    var t = TutorialCharts.CurveLaser;
                    d.Press(EditorAction.LaserSideLeft);
                    foreach (var p in t.Points) d.ClickAt(pf(c, t.Y + p.Ry, laser(p.V)));
                    d.ClickAt(pf(c, t.EndY, laser(t.Points[^1].V)), right: true);
                },
            },

            new TutorialStep("Anchors",
                "Press {key:ToolSelect} (Select) and point at the curve: it lights up and shows its anchors (white = straight, yellow = perfect curve, red = Bézier). "
                + "The laser tool shows them on hover too (Options → \"Show laser anchors on hover\").\n\n"
                + "Anchors drag right away, even on an unselected laser; hold Shift to snap. Click the curve to select it: then Ctrl+click on the path adds one, and a right-click on an anchor opens a menu: "
                + "Inherit, Straight, Perfect curve, Bezier, Delete anchor.")
            {
                Enter = c => Show(c, at(8), at(9)),
                // hovered (outlined, with its anchors unless the hover option is off) or already selected
                Done = c => c.Editor is { } e && e.Tools.Tool.Value == EditorTool.Select && (e.Compose.HoveredLaser != null || e.Compose.ActiveLaser != null),
                Demo = (d, c) =>
                {
                    d.Press(EditorAction.ToolSelect);
                    d.MoveTo(pf(c, at(8, 2), laser(0.8)));
                },
            },

            new TutorialStep("Wide lasers",
                "Some lasers are twice as wide: half of their range lies outside the lanes. First click empty space (or press Esc) so no laser is selected: "
                + "{key:ToggleWideLaser} also widens or narrows a SELECTED laser. Then turn on \"Wide Laser\" ({key:ToggleWideLaser}) in the toggle bar on the right, "
                + "press {key:LaserSideRight} for laser R, and drag from the far left to the far right of the box — out into the space beside the lanes, "
                + "where wide anchors can be hovered, clicked and dragged like any other. (Holding Shift when you start drawing makes that one laser wide, too.)")
            {
                Enter = c => Show(c, at(9), at(10)),
                Ghosts = _ => new[] { TutorialCharts.WideLaser },
                Progress = c => c.Editor is { } e ? $"Wide Laser {(e.Compose.WideLaser.Value ? "on" : "off")} · laser {(e.Tools.LaserSide.Value == 0 ? "L" : "R")}" : null,
                Tasks = new[]
                {
                    new TutorialTask("Nothing selected (click empty space or Esc).", c => c.Editor?.Compose.ActiveLaser == null) { Area = c => c.PlayfieldRegion(at(9), at(10)) },
                    new TutorialTask("Wide Laser on ({key:ToggleWideLaser}).", c => c.Editor?.Compose.WideLaser.Value == true) { Anchor = "compose.toggle.wide" },
                    new TutorialTask("Laser R ({key:LaserSideRight}).", c => c.Editor?.Tools.LaserSide.Value == 1) { Anchor = "compose.laser-side" },
                    new TutorialTask("Drag along the box.", c => allPlaced(c, new[] { TutorialCharts.WideLaser })) { Area = c => c.PlayfieldRegion(at(9), at(10), wide: true) },
                },
                Demo = (d, c) =>
                {
                    var t = TutorialCharts.WideLaser;
                    d.Press(EditorAction.ToolSelect);
                    d.ClickAtIf(() => c.Manager.TaskIndex == 0, pf(c, at(9, 2.5), bt(3))); // empty space: nothing selected, so W only switches the mode
                    task(d, c, 1);
                    d.Press(EditorAction.ToolLaser);
                    d.Press(EditorAction.ToggleWideLaser);
                    task(d, c, 2);
                    d.Press(EditorAction.LaserSideRight);
                    task(d, c, 3);
                    d.Drag(pf(c, t.Y, laser(0, true)), pf(c, t.EndY, laser(1, true)));
                },
            },

            new TutorialStep("Your turn",
                "Bars 11 and 12 (highlighted) are yours. Click empty space first (or Esc: the wide laser you just drew may still be selected), "
                + "turn Wide Laser off again ({key:ToggleWideLaser}), then map them however you like with these rules:\n\n"
                + "at least 8 objects (chips, holds, FX or lasers) · everything on the 1/4 grid · at least one laser with a slam.")
            {
                Enter = c => Show(c, at(TutorialCharts.LaserPractice.From), at(TutorialCharts.LaserPractice.To)),
                Zone = _ => (at(TutorialCharts.LaserPractice.From), at(TutorialCharts.LaserPractice.To)),
                Progress = c => c.Chart is { } ch ? practice(ch, TutorialCharts.LaserPractice, 8, 4, slams: 1) : null,
                Done = c => c.Chart is { } ch && practice(ch, TutorialCharts.LaserPractice, 8, 4, slams: 1) == null,
                Demo = (d, c) =>
                {
                    var (from, _) = TutorialCharts.LaserPractice;
                    d.Press(EditorAction.ToolSelect);
                    d.ClickAt(pf(c, at(from, 7.5), bt(3))); // empty space: deselect before W
                    d.WaitFor(() => c.Editor?.Compose.ActiveLaser == null, "nothing selected");
                    d.Press(EditorAction.ToggleWideLaser);
                    d.Press(EditorAction.Divisor4);
                    d.Press(EditorAction.ToolBtChip);
                    for (int b = 0; b < 8; b++) d.ClickAt(pf(c, at(from, b * 0.75), bt(b % 4)));
                    d.Press(EditorAction.ToolLaser);
                    d.Press(EditorAction.LaserSideRight);
                    d.ClickAt(pf(c, at(from + 1, 2), laser(0)));
                    d.ClickAt(pf(c, at(from + 1, 2), laser(1)));
                    d.ClickAt(pf(c, at(from + 1, 3), laser(1)));
                    d.ClickAt(pf(c, at(from + 1, 3), laser(1)), right: true);
                },
            },

            saveStep(),
        },
    };

    /// <summary>What a free-mapping section still misses (null = all rules met).</summary>
    private static string? practice(Chart chart, (int From, int To) measures, int objects, int divisor, int slams = 0, int lasers = 0)
    {
        long from = at(measures.From), to = at(measures.To);
        var missing = new List<string>();
        int n = TutorialCharts.CountObjects(chart, from, to);
        if (n < objects) missing.Add($"{n} / {objects} objects");
        var off = TutorialCharts.OffGrid(chart, from, to, divisor);
        if (off.Count > 0) missing.Add($"{off.Count} off the 1/{divisor} grid");
        int s = TutorialCharts.CountSlams(chart, from, to);
        if (s < slams) missing.Add("no slam yet");
        int l = TutorialCharts.CountLasers(chart, from, to);
        if (l < lasers) missing.Add("no laser yet");
        return missing.Count == 0 ? null : "Still missing: " + string.Join(" · ", missing);
    }
}
