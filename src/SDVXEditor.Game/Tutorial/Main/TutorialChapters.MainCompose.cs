using osu.Framework.Graphics;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Input;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game.Tutorial;

public static partial class TutorialChapters
{
    private static readonly Func<TutorialContext, string?> mainCompose = TutorialNeed.Editor(EditorTab.Compose);
    private static readonly Action<TutorialContext> mainRestoreCompose = TutorialNeed.RestoreEditor(EditorTab.Compose);

    private static long mAt(double measure) => MainTutorialSong.AtMeasure(measure);

    private static IReadOnlyList<TutorialTarget> one(TutorialTarget? t) => t == null ? Array.Empty<TutorialTarget>() : new[] { t };

    /// <summary>"Ctrl" / "Alt": the hold modifier in effect.</summary>
    private static string holdModifierName(TutorialContext c) => (c.Editor?.Tools.HoldModifier.Value ?? HoldModifierKey.Ctrl).KeyLabel();

    private static bool toolIs(TutorialContext c, EditorTool tool) => c.Editor is { } e && e.Tools.Tool.Value == tool;

    /// <summary>A task "press X for tool Y" with the tile as its spotlight.</summary>
    private static TutorialTask toolTask(EditorTool tool, EditorAction key, string name, string? extra = null) =>
        new TutorialTask($"Press {{key:{key}}} or click the [ui]{name}[/ui] tile.{(extra != null ? " " + extra : "")}", c => toolIs(c, tool))
        {
            Anchor = "compose.tool." + tool.ToString().ToLowerInvariant(),
        };

    // the playfield's colours, as the cards name them
    private const string laser_l = "[c=4de1ff]laser L (blue)[/c]", laser_r = "[c=ff5ad1]laser R (pink)[/c]", fx_word = "[c=ff9f43]FX[/c]";

    /// <summary>Main tutorial, chapter 1: tools, snap, chips, holds (also by hold modifier, resized by their end), FX, select, copy/paste, mirror, undo.</summary>
    private static TutorialChapter mainChipsHoldsFx() => new TutorialChapter
    {
        Number = 1,
        Title = "Chips, Holds and FX",
        Summary = "Tools, beat snap, place and delete notes, holds by drag, hold modifier, resizing, select, copy, paste, mirror, undo",
        Minutes = MainTutorialPlan.MinutesOf(1),
        Requirement = mainCompose,
        Restore = mainRestoreCompose,
        Steps = c =>
        {
            var t = c.Targets;
            var ex = c.Main.Exercises;

            return new List<TutorialStep>
            {
                new TutorialStep("The playfield",
                    "This is where you place notes. [b]Time runs upwards[/b]: the [c=7ee07e]green line[/c] near the bottom is \"now\"; while the song plays, notes move down to it.\n\n"
                    + "Lanes from left to right:\n"
                    + $"• {laser_l} — the left knob\n"
                    + "• the four [b]BT[/b] buttons A B C D (white chips)\n"
                    + $"• {laser_r} — the right knob\n"
                    + $"• {fx_word} notes are the wide orange ones under two BT lanes each: FX-L under A+B, FX-R under C+D.\n\n"
                    + "Scroll the mouse wheel to move through the song.")
                {
                    Anchor = "editor.playfield",
                    Enter = c => Show(c, ex.Chips.FromPulse, ex.Chips.ToPulse),
                },

                new TutorialStep("The toolbox",
                    "Everything you place comes from a tool in the toolbox on the left. Each has a key:\n"
                    + "• {key:ToolSelect} [ui]Select[/ui] — pick, move, resize\n"
                    + "• {key:ToolBtChip} [ui]BT Chip[/ui] · {key:ToolBtHold} [ui]BT Hold[/ui]\n"
                    + $"• {{key:ToolFxChip}} [ui]FX Chip[/ui] · {{key:ToolFxHold}} [ui]FX Hold[/ui] ({fx_word})\n"
                    + "• {key:ToolLaser} [ui]Laser[/ui]\n\n"
                    + "The small grey text on a tile shows what a modifier key turns it into (Shift: FX, Ctrl: Hold).")
                {
                    Anchor = "compose.toolbox",
                },

                new TutorialStep("Pick BT Chip",
                    "A [b]chip[/b] is a single tap on a button.\n\n"
                    + "Press {key:ToolBtChip} or click the [ui]BT Chip[/ui] tile.")
                {
                    Anchor = "compose.tool.btchip",
                    Done = c => toolIs(c, EditorTool.BtChip),
                    Demo = (d, _) => d.Press(EditorAction.ToolBtChip),
                },

                new TutorialStep("Beat snap",
                    "Notes snap to the grid lines. [ui]Beat Snap Divisor[/ui] says how many lines per beat:\n"
                    + "• [c=ffffff]white[/c] — a beat\n"
                    + "• [c=ff5050]red[/c] — half a beat\n"
                    + "• [c=4f8dff]blue[/c] — a quarter beat\n"
                    + "• [c=c47cff]purple[/c] — triplets\n\n"
                    + "At 235 BPM a beat is only a quarter of a second, so start with whole beats: set [b]1/1[/b] — press {key:Divisor1}, drag the slider, or [k]Ctrl[/k] + mouse wheel.")
                {
                    Anchor = "compose.divisor",
                    Progress = c => c.Editor is { } e ? $"Beat snap: 1/{e.Compose.Divisor.Value}{(e.Compose.Divisor.Value == 1 ? " ✓" : " — needs 1/1")}" : null,
                    Done = c => c.Editor is { } e && e.Compose.Divisor.Value == 1,
                    Demo = (d, _) => d.Press(EditorAction.Divisor1),
                },

                new TutorialStep("Delete a stray chip",
                    $"Someone left a chip in {bars(ex.Chips.FromPulse, ex.Chips.ToPulse)} (marked with a [c=ff4d4d]red x[/c]).\n\n"
                    + "[b]Right-click[/b] it to delete it — that works with any tool.")
                {
                    Enter = c => Show(c, ex.Chips.FromPulse, ex.Chips.ToPulse),
                    Area = c => region(c, new[] { t.StrayChip }),
                    DeleteMarks = _ => new[] { t.StrayChip },
                    Done = c => c.Chart is { } ch && !ch.Notes.Bt[t.StrayChip.Lane].Any(n => n.Y == t.StrayChip.Y),
                    Demo = (d, c) => d.ClickAt(pf(c, t.StrayChip.Y, bt(t.StrayChip.Lane)), right: true),
                },

                new TutorialStep("Place chips",
                    $"The music hits on every beat of {bars(ex.Chips.FromPulse, ex.Chips.ToPulse)}. Click each of the {t.Chips.Count} glowing boxes with the BT Chip tool.\n\n"
                    + "A box turns [c=7ee07e]green[/c] when its chip is right.")
                {
                    Enter = c => Show(c, ex.Chips.FromPulse, ex.Chips.ToPulse),
                    Area = c => region(c, t.Chips),
                    Ghosts = _ => t.Chips,
                    Progress = c => placed(c, t.Chips, "chips"),
                    Done = c => allPlaced(c, t.Chips),
                    Demo = (d, c) =>
                    {
                        foreach (var x in t.Chips) d.ClickAt(pf(c, x.Y, bt(x.Lane)));
                    },
                },

                new TutorialStep("Holds",
                    "A [b]hold[/b] is a long note: the player keeps the button down.")
                {
                    Enter = c => Show(c, ex.Holds.FromPulse, ex.Holds.ToPulse),
                    Ghosts = _ => t.Holds,
                    Progress = c => placed(c, t.Holds, "holds"),
                    Tasks = new[]
                    {
                        toolTask(EditorTool.BtHold, EditorAction.ToolBtHold, "BT Hold"),
                        new TutorialTask("Drag from the [b]bottom[/b] of each box to its [b]top[/b]. (Dragging on an existing chip with the hold tool pulls it into a hold.)", c => allPlaced(c, t.Holds))
                        {
                            Area = c => region(c, t.Holds),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolBtHold);
                        task(d, c, 1);
                        foreach (var x in t.Holds) d.Drag(pf(c, x.Y, bt(x.Lane)), pf(c, x.EndY, bt(x.Lane)));
                    },
                },

                new TutorialStep("The hold modifier",
                    "No need to switch tools for every hold: while the [b]hold modifier[/b] is down, the chip tools place holds (the tile then says \"now Hold\"); with [k]Shift[/k] as well you get an FX hold. "
                    + "[ui]Options → Input → Hold modifier[/ui] picks [k]Ctrl[/k] or [k]Alt[/k].")
                {
                    Enter = c => Show(c, mAt(ex.HoldModifier.From), mAt(ex.HoldModifier.From + 1)),
                    Ghosts = _ => one(t.ModifierHold),
                    Progress = c => c.Editor is { } e ? $"Tool: {e.Tools.Tool.Value}{(e.Tools.EffectiveTool.Value != e.Tools.Tool.Value ? $" (now {e.Tools.EffectiveTool.Value})" : "")}" : null,
                    Tasks = new[]
                    {
                        toolTask(EditorTool.BtChip, EditorAction.ToolBtChip, "BT Chip"),
                        new TutorialTask("Hold [k]" + holdModifierName(c) + "[/k] and drag over the box from bottom to top.", c => allPlaced(c, one(t.ModifierHold)))
                        {
                            Area = c => region(c, one(t.ModifierHold)),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        var x = t.ModifierHold!;
                        var key = c.Editor?.Tools.HoldModifier.Value == HoldModifierKey.Alt ? Key.LAlt : Key.LControl;
                        d.Press(EditorAction.ToolBtChip);
                        task(d, c, 1);
                        d.HoldKey(key, true);
                        d.Drag(pf(c, x.Y, bt(x.Lane)), pf(c, x.EndY, bt(x.Lane)));
                        d.HoldKey(key, false);
                    },
                },

                new TutorialStep("Resize a hold",
                    "The hold next to it is too short. Holds are changed with the Select tool:\n"
                    + "• drag a hold's [b]end[/b] — it gets longer or shorter, the start stays\n"
                    + "• drag its [b]middle[/b] — the whole note moves\n"
                    + "• several holds selected — all change by the same amount")
                {
                    Enter = c => Show(c, mAt(ex.HoldModifier.From), mAt(ex.HoldModifier.From + 1)),
                    Ghosts = _ => one(t.ResizeHold),
                    Tasks = new[]
                    {
                        toolTask(EditorTool.Select, EditorAction.ToolSelect, "Select"),
                        new TutorialTask("Point at the [b]top end[/b] of the short hold (the cursor shows a resize hint) and drag it up to the top of the box.", c => allPlaced(c, one(t.ResizeHold)))
                        {
                            Area = c => region(c, one(t.ResizeHold)),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        var start = t.ResizeStart!;
                        var goal = t.ResizeHold!;
                        d.Press(EditorAction.ToolSelect);
                        task(d, c, 1);
                        d.MoveTo(() => c.PlayfieldPoint(start.EndY, bt(start.Lane)) + new Vector2(0, 3));
                        d.Drag(() => c.PlayfieldPoint(start.EndY, bt(start.Lane)) + new Vector2(0, 3), pf(c, goal.EndY, bt(goal.Lane)));
                    },
                },

                new TutorialStep("FX chips",
                    $"{fx_word} buttons are the big ones below the BT buttons. Here they are the accents between the chords.\n\n"
                    + "Tip: holding [k]Shift[/k] turns the BT tools into FX tools while you hold it.")
                {
                    Enter = c => Show(c, ex.FxChips.FromPulse, ex.FxChips.ToPulse),
                    Ghosts = _ => t.FxChips,
                    Progress = c => placed(c, t.FxChips, "FX chips"),
                    Tasks = new[]
                    {
                        toolTask(EditorTool.FxChip, EditorAction.ToolFxChip, "FX Chip"),
                        new TutorialTask("Click the boxes.", c => allPlaced(c, t.FxChips)) { Area = c => region(c, t.FxChips) },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolFxChip);
                        task(d, c, 1);
                        foreach (var x in t.FxChips) d.ClickAt(pf(c, x.Y, fx(x.Lane)));
                    },
                },

                new TutorialStep("An FX hold",
                    "An FX hold can play a sound effect while it is held — you will give it one in the Effects chapter.")
                {
                    Enter = c => Show(c, ex.FxHold.FromPulse, ex.FxHold.ToPulse),
                    Ghosts = _ => t.FxHold,
                    Tasks = new[]
                    {
                        toolTask(EditorTool.FxHold, EditorAction.ToolFxHold, "FX Hold"),
                        new TutorialTask("Drag over the box from bottom to top.", c => allPlaced(c, t.FxHold)) { Area = c => region(c, t.FxHold) },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolFxHold);
                        task(d, c, 1);
                        foreach (var x in t.FxHold) d.Drag(pf(c, x.Y, fx(x.Lane)), pf(c, x.EndY, fx(x.Lane)));
                    },
                },

                new TutorialStep("Select",
                    "Selected notes turn [c=4f8dff]blue[/c]. A click selects one note, [k]Ctrl[/k]+click adds one to the selection, dragging on empty space draws a box.")
                {
                    Enter = c => Show(c, ex.Chips.FromPulse, ex.Chips.ToPulse),
                    Ghosts = _ => t.Chips,
                    Progress = c => c.Session is { } s ? $"{t.Chips.Count(x => mainIsSelected(s, x))} / {t.Chips.Count} selected" : null,
                    Tasks = new[]
                    {
                        toolTask(EditorTool.Select, EditorAction.ToolSelect, "Select"),
                        new TutorialTask($"Drag a box around the {t.Chips.Count} chips of {bars(ex.Chips.FromPulse, ex.Chips.ToPulse)}.", c => c.Session is { } s && t.Chips.All(x => mainIsSelected(s, x)))
                        {
                            Area = c => c.PlayfieldRegion(ex.Chips.FromPulse - beat / 2, ex.Chips.ToPulse - beat / 4),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolSelect);
                        task(d, c, 1);
                        d.Drag(pf(c, ex.Chips.FromPulse - beat / 2 + 20, PlayfieldLayout.BT_LEFT + 6), pf(c, ex.Chips.ToPulse - beat / 2, PlayfieldLayout.BT_RIGHT - 6));
                    },
                },

                new TutorialStep("Copy",
                    "Press {key:Copy} to copy the selected chips.\n\n"
                    + "(Besides the notes, the editor also puts a [b]modding timestamp[/b] on the clipboard — more about that in the Navigating chapter.)")
                {
                    Enter = c => c.Bag["ts"] = (c.Editor?.ChartActions as EditorChartActions)?.LastTimestamp,
                    Area = c => region(c, t.Chips),
                    Done = c => c.Editor?.ChartActions is EditorChartActions { LastTimestamp: { } ts } && !ReferenceEquals(ts, c.Bag["ts"]),
                    Demo = (d, _) => d.Press(EditorAction.Copy),
                },

                new TutorialStep("Paste",
                    "A paste lands at the [c=7ee07e]green line[/c] (the cursor).")
                {
                    Ghosts = _ => t.Pasted,
                    Progress = c => placed(c, t.Pasted, "chips in the next bar"),
                    Tasks = new[]
                    {
                        new TutorialTask($"Move the line to the start of {bars(ex.Paste.FromPulse, ex.Paste.ToPulse)}: mouse wheel, or [k]→[/k] (one snap step). {{key:SeekStart}} jumps to the very start.",
                            c => Math.Abs(c.CursorPulse - ex.Paste.FromPulse) <= 2 || allPlaced(c, t.Pasted))
                        {
                            Area = c => c.PlayfieldRegion(ex.Chips.FromPulse, ex.Paste.ToPulse),
                        },
                        new TutorialTask("Press {key:Paste}.", c => allPlaced(c, t.Pasted)) { Area = c => region(c, t.Pasted) },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.SeekStart);
                        // snap 1/1: one beat per press, from the first beat to the start of the next bar
                        for (int i = 0; i < (int)Math.Round(ex.Paste.From * 4); i++) d.Press(EditorAction.SeekForward);
                        task(d, c, 1);
                        d.Press(EditorAction.Paste);
                    },
                },

                new TutorialStep("Mirror, undo, redo",
                    "The pasted chips are still selected. [b]Every edit can be taken back.[/b]")
                {
                    Enter = c => Show(c, ex.Paste.FromPulse, ex.Paste.ToPulse),
                    Area = c => region(c, t.Mirrored),
                    Ghosts = _ => t.Mirrored,
                    Tasks = new[]
                    {
                        new TutorialTask("Press {key:MirrorHorizontal}: they flip left/right (A with D, B with C, FX-L with FX-R — lasers too).", c =>
                        {
                            if (allPlaced(c, t.Mirrored)) c.Bag["mirrored"] = true;
                            return c.Bag.ContainsKey("mirrored");
                        }),
                        new TutorialTask("Press {key:Undo} to undo the mirror.", c =>
                        {
                            if (c.Bag.ContainsKey("mirrored") && allPlaced(c, t.Pasted)) c.Bag["undone"] = true;
                            return c.Bag.ContainsKey("undone");
                        }),
                        new TutorialTask("Press {key:Redo} to redo it.", c => c.Bag.ContainsKey("undone") && allPlaced(c, t.Mirrored)),
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.MirrorHorizontal);
                        task(d, c, 1);
                        d.Press(EditorAction.Undo);
                        task(d, c, 2);
                        d.Press(EditorAction.Redo);
                    },
                },

                new TutorialStep("Your turn",
                    $"{cap(ex.ChipPractice.BarsText)} (the yellow band) is yours: place [b]at least four BT chips, each on a beat[/b]. Any lanes you like — {{key:ToolBtChip}} for BT Chip.")
                {
                    Enter = c => Show(c, ex.ChipPractice.FromPulse, ex.ChipPractice.ToPulse),
                    Zone = _ => (ex.ChipPractice.FromPulse, ex.ChipPractice.ToPulse),
                    ZoneLabel = _ => ex.ChipPractice.BarsText,
                    Area = c => region(c, ex.ChipPractice, lasers: false),
                    Progress = c => c.Chart is { } ch ? $"{Math.Min(4, TutorialChecks.CountBeatChips(ch, ex.ChipPractice.FromPulse, ex.ChipPractice.ToPulse))} / 4 chips on beats" : null,
                    Done = c => c.Chart is { } ch && TutorialChecks.CountBeatChips(ch, ex.ChipPractice.FromPulse, ex.ChipPractice.ToPulse) >= 4,
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolBtChip);
                        for (int b = 0; b < 4; b++) d.ClickAt(pf(c, ex.ChipPractice.FromPulse + b * beat, bt(3 - b)));
                    },
                },

                saveStep(),
            };
        },
    };

    private static string cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static bool mainIsSelected(EditorSession s, TutorialTarget t) =>
        s.Selection.Notes.Any(r => r.Kind == t.Kind && r.Lane == t.Lane && r.Note.Y == t.Y);

    private static string side(TutorialTarget? t) => t?.Lane == 1 ? "R" : "L";

    private static string sideColoured(TutorialTarget? t) => t?.Lane == 1 ? "[c=ff5ad1]R[/c]" : "[c=4de1ff]L[/c]";

    private static EditorAction sideKey(TutorialTarget? t) => t?.Lane == 1 ? EditorAction.LaserSideRight : EditorAction.LaserSideLeft;

    private static TutorialTask sideTask(TutorialTarget? t) =>
        new TutorialTask($"Switch to laser {sideColoured(t)}: press {{key:{sideKey(t)}}} or click {sideColoured(t)} under the Laser tile.", c => c.Editor is { } e && e.Tools.LaserSide.Value == (t?.Lane ?? 0))
        {
            Anchor = "compose.laser-side",
        };

    /// <summary>Demo: places a laser target by clicking its points (a slam = two clicks on one pulse), right-click finishes.</summary>
    private static void clickLaser(ITutorialDemo d, TutorialContext c, TutorialTarget t)
    {
        foreach (var p in t.Points)
        {
            d.ClickAt(pf(c, t.Y + p.Ry, laser(p.V, t.Wide)));
            if (p.IsSlam) d.ClickAt(pf(c, t.Y + p.Ry, laser(p.ValueAfter, t.Wide)));
        }

        d.ClickAt(pf(c, t.EndY, laser(t.Points[^1].ValueAfter, t.Wide)), right: true);
    }

    /// <summary>Main tutorial, chapter 2: lasers — tool and side, drag, slams by click placement, curves, anchors and their menu, wide lasers, practice.</summary>
    private static TutorialChapter mainLasers() => new TutorialChapter
    {
        Number = 2,
        Title = "Lasers",
        Summary = "Laser tool, L/R, straight lasers, slams, curves, anchors and their types, wide lasers",
        Minutes = MainTutorialPlan.MinutesOf(2),
        Requirement = mainCompose,
        Restore = mainRestoreCompose,
        Steps = c =>
        {
            var t = c.Targets;
            var ex = c.Main.Exercises;
            var straight = t.StraightLaser!;
            var slam = t.Slam!;
            var curve = t.CurveLaser!;
            var wide = t.WideLaser!;

            LaserSection? curveSection(TutorialContext c) => c.Chart?.Notes.Lasers[curve.Lane].FirstOrDefault(s => s.Y == curve.Y);
            Drawable? bezierItem(TutorialContext c) => c.Editor?.Playfield.OpenLaserMenu?.ChildrenOfType<LaserContextMenuItem>().FirstOrDefault(i => i.Label == "Bezier");

            return new List<TutorialStep>
            {
                new TutorialStep("Lasers",
                    "Lasers are the paths the two knobs follow:\n"
                    + $"• {laser_l} — the left knob\n"
                    + $"• {laser_r} — the right knob\n\n"
                    + "The lane's left edge = knob turned all the way left, the right edge = all the way right. A laser can cross over to the other side.")
                {
                    Anchor = "editor.playfield",
                    Enter = c =>
                    {
                        c.Manager.WhenEditorReady(() => c.Editor!.Compose.Divisor.Value = 4);
                        Show(c, straight.Y, straight.EndY + beat);
                    },
                },

                new TutorialStep("The laser tool",
                    "The Laser tile shows [c=4de1ff]L[/c] or [c=ff5ad1]R[/c]: the knob you draw for.")
                {
                    Tasks = new[]
                    {
                        toolTask(EditorTool.Laser, EditorAction.ToolLaser, "Laser"),
                        sideTask(straight),
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolLaser);
                        task(d, c, 1);
                        d.Press(sideKey(straight));
                    },
                },

                new TutorialStep("A straight laser",
                    "[b]Drag[/b] from the start of the path (bottom) to its end (top): a laser sweeping across. Time snaps to the grid; the side follows your mouse.")
                {
                    Enter = c => Show(c, straight.Y, straight.EndY + beat),
                    Area = c => region(c, new[] { straight }),
                    Ghosts = _ => new[] { straight },
                    Done = c => allPlaced(c, new[] { straight }),
                    Demo = (d, c) => d.Drag(pf(c, straight.Y, laser(straight.Points[0].V)), pf(c, straight.EndY, laser(straight.Points[^1].V))),
                },

                new TutorialStep("A slam",
                    "A [b]slam[/b] is an instant jump of the knob — it makes a \"whoosh\" in the game. Here it sits on the hit of the fill.")
                {
                    Enter = c => Show(c, slam.Y - beat, slam.EndY + beat),
                    Ghosts = _ => new[] { slam },
                    Progress = c => c.Editor?.Compose.Placement != null ? "Placing — right-click finishes" : null,
                    Tasks = new[]
                    {
                        sideTask(slam),
                        new TutorialTask("Place it by clicking:\n• click where the line starts\n• click its other end [b]on the same beat[/b] — that is the slam\n• click the top end, then [b]right-click[/b] to finish\n"
                                         + "The two ends of a slam stay two points: later you can drag or delete each one.", c => allPlaced(c, new[] { slam }))
                        {
                            Area = c => region(c, new[] { slam }),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(sideKey(slam));
                        task(d, c, 1);
                        clickLaser(d, c, slam);
                    },
                },

                new TutorialStep("Curves",
                    "Clicking places [b]anchors[/b], like osu! sliders: 2 anchors = straight, 3 = a perfect curve (an arc), 4 or more = a Bézier curve.")
                {
                    Enter = c => Show(c, curve.Y, curve.EndY),
                    Ghosts = _ => new[] { curve },
                    Progress = c => c.Editor?.Compose.Placement is { } p ? $"{p.Path.Count} anchor{(p.Path.Count == 1 ? "" : "s")} placed" : null,
                    Tasks = new[]
                    {
                        sideTask(curve),
                        new TutorialTask("Click the three dots from bottom to top, then [b]right-click[/b] to finish.\n"
                                         + "(A right-click on the last point finishes; further on it adds one more anchor first. Hold [k]F[/k] to place K-Shoot style: straight lines on a 1/10 grid.)",
                            c => allPlaced(c, new[] { curve }))
                        {
                            Area = c => region(c, new[] { curve }),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(sideKey(curve));
                        task(d, c, 1);
                        foreach (var p in curve.Points) d.ClickAt(pf(c, curve.Y + p.Ry, laser(p.V)));
                        d.ClickAt(pf(c, curve.EndY, laser(curve.Points[^1].V)), right: true);
                    },
                },

                new TutorialStep("Anchors",
                    "Anchors show their type by colour: [c=ffffff]white[/c] = straight, [c=ffd84d]yellow[/c] = perfect curve, [c=ff5050]red[/c] = Bézier. "
                    + "They drag right away, even on a laser that is not selected; hold [k]Shift[/k] to snap. (Options → \"Show laser anchors on hover\" shows them with the laser tool too.)")
                {
                    Enter = c => Show(c, curve.Y, curve.EndY),
                    Tasks = new[]
                    {
                        toolTask(EditorTool.Select, EditorAction.ToolSelect, "Select"),
                        new TutorialTask("Point at the curve: it lights up and shows its anchors.", c => c.Editor is { } e && toolIs(c, EditorTool.Select) && (e.Compose.HoveredLaser != null || e.Compose.ActiveLaser != null))
                        {
                            Area = c => region(c, new[] { curve }),
                        },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolSelect);
                        task(d, c, 1);
                        d.MoveTo(pf(c, curve.Y + curve.Points[1].Ry, laser(curve.Points[1].V)));
                    },
                },

                new TutorialStep("Anchor types",
                    "A right-click on an anchor opens its menu: [ui]Inherit[/ui], [ui]Straight[/ui], [ui]Perfect curve[/ui], [ui]Bezier[/ui], [ui]Delete anchor[/ui]. "
                    + "[k]Ctrl[/k]+click on the path adds an anchor.")
                {
                    Enter = c => Show(c, curve.Y, curve.EndY),
                    Tasks = new[]
                    {
                        new TutorialTask("Click the curve to select it.", c => c.Editor?.Compose.ActiveLaser != null) { Area = c => region(c, new[] { curve }) },
                        new TutorialTask("Right-click its [b]first[/b] anchor (the bottom one).", c => c.Editor?.Playfield.OpenLaserMenu != null || curveIsBezier(c))
                        {
                            Area = c => c.PlayfieldRegion(curve.Y - beat / 4, curve.Y + beat / 4, lasers: true),
                        },
                        new TutorialTask("Pick [ui]Bezier[/ui]: the arc becomes a Bézier curve with the middle anchor as its handle.", curveIsBezier) { Target = bezierItem },
                    },
                    Demo = (d, c) =>
                    {
                        long mid = curve.Points[1].Ry / 2;
                        d.ClickAt(() => c.PlayfieldPoint(curve.Y + mid, laser(curveSection(c) is { } s ? LaserPathBaker.PointsValueAt(s.Points, mid) ?? curve.Points[0].V : curve.Points[0].V)));
                        task(d, c, 1);
                        d.ClickAt(pf(c, curve.Y, laser(curve.Points[0].V)), right: true);
                        task(d, c, 2);
                        d.Click(() => bezierItem(c));
                    },
                },

                new TutorialStep("Wide lasers",
                    "Some lasers are [b]twice as wide[/b]: half of their range lies outside the lanes. Wide anchors can be hovered, clicked and dragged out there like any other. "
                    + "(Holding [k]Shift[/k] when you start drawing makes just that one laser wide.)")
                {
                    Enter = c => Show(c, wide.Y, wide.EndY + beat),
                    Ghosts = _ => new[] { wide },
                    Progress = c => c.Editor is { } e ? $"Wide Laser {(e.Compose.WideLaser.Value ? "on" : "off")} · laser {(e.Tools.LaserSide.Value == 0 ? "L" : "R")}" : null,
                    Tasks = new[]
                    {
                        new TutorialTask("Nothing may be selected — {key:ToggleWideLaser} also widens a [b]selected[/b] laser. Click empty space (or press [k]Esc[/k]).", c => c.Editor?.Compose.ActiveLaser == null && c.Session?.Selection.IsEmpty != false)
                        {
                            Area = c => c.PlayfieldRegion(wide.Y - beat, wide.EndY + beat),
                        },
                        toolTask(EditorTool.Laser, EditorAction.ToolLaser, "Laser"),
                        new TutorialTask("Turn on [ui]Wide Laser[/ui] in the toggle bar on the right ({key:ToggleWideLaser}).", c => c.Editor?.Compose.WideLaser.Value == true) { Anchor = "compose.toggle.wide" },
                        sideTask(wide),
                        new TutorialTask("Drag along the box — out into the space beside the lanes.", c => allPlaced(c, new[] { wide })) { Area = c => region(c, new[] { wide }, wide: true) },
                    },
                    Demo = (d, c) =>
                    {
                        d.Press(EditorAction.ToolSelect);
                        d.ClickAtIf(() => c.Manager.TaskIndex == 0, pf(c, wide.EndY + beat / 2, bt(3))); // empty space: the curve is no longer selected, so W only switches the mode
                        task(d, c, 1);
                        d.Press(EditorAction.ToolLaser);
                        task(d, c, 2);
                        d.Press(EditorAction.ToggleWideLaser);
                        task(d, c, 3);
                        d.Press(sideKey(wide));
                        task(d, c, 4);
                        d.Drag(pf(c, wide.Y, laser(wide.Points[0].V, true)), pf(c, wide.EndY, laser(wide.Points[^1].V, true)));
                    },
                },

                new TutorialStep("Your turn",
                    $"{cap(ex.LaserPractice.BarsText)} (the yellow band) are yours. Map them however you like with these rules:\n"
                    + "• at least [b]8 objects[/b] (chips, holds, FX or lasers)\n"
                    + "• everything on the [b]1/4[/b] grid\n"
                    + "• at least [b]one laser with a slam[/b]")
                {
                    Enter = c => Show(c, ex.LaserPractice.FromPulse, ex.LaserPractice.ToPulse),
                    Zone = _ => (ex.LaserPractice.FromPulse, ex.LaserPractice.ToPulse),
                    ZoneLabel = _ => ex.LaserPractice.BarsText,
                    Progress = c => c.Chart is { } ch ? mainPractice(ch, ex.LaserPractice, 8, 4, slams: 1) : null,
                    Tasks = new[]
                    {
                        new TutorialTask("Click empty space (or [k]Esc[/k]): the wide laser you just drew may still be selected.", c => c.Editor?.Compose.ActiveLaser == null && c.Session?.Selection.IsEmpty != false)
                        {
                            Area = c => region(c, ex.LaserPractice),
                        },
                        new TutorialTask("Turn [ui]Wide Laser[/ui] off again ({key:ToggleWideLaser}).", c => c.Editor?.Compose.WideLaser.Value == false) { Anchor = "compose.toggle.wide" },
                        new TutorialTask("Map the band.", c => c.Chart is { } ch && mainPractice(ch, ex.LaserPractice, 8, 4, slams: 1) == null) { Area = c => region(c, ex.LaserPractice) },
                    },
                    Demo = (d, c) =>
                    {
                        long from = ex.LaserPractice.FromPulse;
                        d.Press(EditorAction.ToolSelect);
                        d.ClickAtIf(() => c.Manager.TaskIndex == 0, pf(c, from + 15 * beat / 2, bt(3))); // empty space: deselect before W
                        task(d, c, 1);
                        d.Press(EditorAction.ToggleWideLaser);
                        task(d, c, 2);
                        d.Press(EditorAction.Divisor4);
                        d.Press(EditorAction.ToolBtChip);
                        for (int b = 0; b < 8; b++) d.ClickAt(pf(c, from + (long)Math.Round(b * 0.75 * beat), bt(b % 4)));
                        d.Press(EditorAction.ToolLaser);
                        d.Press(EditorAction.LaserSideRight);
                        d.ClickAt(pf(c, from + 6 * beat, laser(0)));
                        d.ClickAt(pf(c, from + 6 * beat, laser(1)));
                        d.ClickAt(pf(c, from + 7 * beat, laser(1)));
                        d.ClickAt(pf(c, from + 7 * beat, laser(1)), right: true);
                    },
                },

                saveStep(),
            };

            bool curveIsBezier(TutorialContext c) => curveSection(c) is { } s && LaserPaths.ValidPath(s) is { } p && p.Anchors[0] is { Type: LaserCurveType.Bezier, Auto: false };
        },
    };

    /// <summary>What a free-mapping window still misses (null = all rules met).</summary>
    private static string? mainPractice(Chart chart, TutorialWindow w, int objects, int divisor, int slams = 0, int lasers = 0)
    {
        long from = w.FromPulse, to = w.ToPulse;
        var missing = new List<string>();
        int n = TutorialChecks.CountObjects(chart, from, to);
        if (n < objects) missing.Add($"{n} / {objects} objects");
        var off = TutorialChecks.OffGrid(chart, from, to, divisor);
        if (off.Count > 0) missing.Add($"{off.Count} off the 1/{divisor} grid");
        if (TutorialChecks.CountSlams(chart, from, to) < slams) missing.Add("no slam yet");
        if (TutorialChecks.CountLasers(chart, from, to) < lasers) missing.Add("no laser yet");
        return missing.Count == 0 ? null : "Still missing: " + string.Join(" · ", missing);
    }
}
