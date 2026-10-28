using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Threading;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Validation;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Panels;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// The 2026-10-27 feature batch (K-Shoot Editor v2 comparison): chart check, side panels (inspector, layers), insert /
/// delete time, swap laser colour, loop playback, finer snap divisors, the toolbox's FX effect and slam sound
/// drop-downs. Wired from <see cref="setupFeatures"/> (load) and <see cref="handleFeatureAction"/> (first look at every
/// action).
/// </summary>
public partial class EditorScreen
{
    /// <summary>Which object types the compose views draw and let you pick (Layers panel).</summary>
    public readonly LayerVisibility Layers = new LayerVisibility();

    public InspectorPanel Inspector { get; private set; } = null!;
    public LayersPanel LayersPanel { get; private set; } = null!;
    public ChartCheckPanel ChartCheck { get; private set; } = null!;

    /// <summary>The last chart check run after a save or before an export (smoke test).</summary>
    public ChartCheckReport? LastSaveCheck { get; private set; }

    private ScheduledDelegate? checkRefresh;
    private bool syncingToolbox;

    private void setupFeatures()
    {
        var compose = (Container)pages[EditorTab.Compose];
        compose.Add(new FillFlowContainer
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Position = new Vector2(-140, 43),
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new Drawable[]
            {
                Inspector = new InspectorPanel(Session, showToast),
                LayersPanel = new LayersPanel(Layers),
                ChartCheck = new ChartCheckPanel(RunChartCheck, JumpToIssue),
            },
        });

        // toolbox drop-downs: the chart's effects; a change applies to the selection (one undo step)
        toolbox.FxEffectChoices = () => EffectCatalog.AvailableNames(Session.Chart, laser: false);
        toolbox.FxEffectLabel = n => EffectCatalog.Find(n)?.Label ?? n;
        toolbox.FxEffect.BindValueChanged(e => { if (!syncingToolbox) applyHoldEffect(HoldEffects.FromLabel(e.NewValue == ComposeToolbox.NoEffect ? null : e.NewValue)); });
        toolbox.SlamSound.BindValueChanged(e => { if (!syncingToolbox) applySlamSound(SlamSounds.FromLabel(e.NewValue)); });
        Session.Selection.Changed += syncToolboxToSelection;

        // the chart check follows edits while it is open (debounced)
        Session.ChartChanged += onFeatureChartChanged;
        Playback.Loop.BindValueChanged(_ => Ruler.LoopRange = Playback.Loop.Value);
        Ruler.Layers = Layers;
        Summary.Layers = Layers;
        Playfield.Layers = Layers;
        Compose.Layers = Layers;
        Compose.NewHoldEffect = () => NewHoldEffect;
        Compose.NewSlamSound = () => NewSlamSound;
    }

    private void teardownFeatures()
    {
        Session.Selection.Changed -= syncToolboxToSelection;
        Session.ChartChanged -= onFeatureChartChanged;
    }

    private void onFeatureChartChanged()
    {
        if (!ChartCheck.Shown.Value) return;
        checkRefresh?.Cancel();
        checkRefresh = Scheduler.AddDelayed(() => ChartCheck.Refresh(), 250);
    }

    /// <summary>
    /// Actions of the batch. Returns true when it handled the action (the rest of <see cref="handleAction"/> is skipped).
    /// </summary>
    private bool handleFeatureAction(EditorAction action)
    {
        switch (action)
        {
            case EditorAction.ChartCheck: ShowChartCheck(toggle: true); return true;
            case EditorAction.Save: saveWithCheck(); return true;
            case EditorAction.InsertBlankTick: InsertBlank(snapTickAtCursor(), "one snap tick"); return true;
            case EditorAction.InsertBlankBeat: InsertBlank(Session.Timing.BeatLength(Session.Timing.MeasureAt(cursorPulse())), "one beat"); return true;
            case EditorAction.InsertBlankCustom: OpenTimeShift(); return true;
            case EditorAction.DeleteRange: DeleteSelectedRange(); return true;
            case EditorAction.SwapLaserColour: SwapLaserColour(); return true;
            case EditorAction.ToggleLoop: ToggleLoop(); return true;
            case EditorAction.CycleHoldEffect: CycleHoldEffect(); return true;

            case EditorAction.ToggleInspector:
                SelectTab(EditorTab.Compose);
                Inspector.Shown.Toggle();
                return true;

            case EditorAction.ToggleLayers:
                SelectTab(EditorTab.Compose);
                LayersPanel.Shown.Toggle();
                return true;

            case >= EditorAction.DivisorSet12 and <= EditorAction.DivisorSet192:
                int[] fine = { 12, 16, 24, 32, 48, 64, 96, 192 };
                setDivisor(fine[action - EditorAction.DivisorSet12]);
                showToast($"Beat snap divisor 1/{divisorControl.Divisor.Value}");
                return true;
        }

        return false;
    }

    // ---- chart check ---------------------------------------------------------------------------------------------

    /// <summary>Runs every chart-check rule on the active chart (with the audio length and the song folder when known).</summary>
    public ChartCheckReport RunChartCheck()
    {
        var context = new ChartCheckContext(Session.Chart, Session.Timing)
        {
            AudioLengthMs = Playback.AudioLengthMs > 0 ? Playback.AudioLengthMs : null,
            SongFolder = Session.Song?.FolderPath,
        };
        return ChartChecker.Run(context);
    }

    /// <summary>Ctrl+Shift+A / Edit → Check Chart: the panel on the compose tab (toggle closes it again).</summary>
    public void ShowChartCheck(bool toggle = false)
    {
        SelectTab(EditorTab.Compose);
        if (toggle && ChartCheck.Shown.Value)
        {
            ChartCheck.Shown.Value = false;
            return;
        }

        ChartCheck.Shown.Value = true;
        ChartCheck.Refresh();
        showToast("Chart check: " + (ChartCheck.Report?.Summary ?? "-"));
    }

    /// <summary>A click on an issue: seek there and select its objects (those still in the chart).</summary>
    public void JumpToIssue(ChartIssue issue)
    {
        SelectTab(EditorTab.Compose);
        if (issue.Pulse is long pulse)
            Playback.CurrentTime.Value = Math.Clamp(Session.PulseToMs(pulse), 0, Playback.LengthMs);

        var sel = Session.Selection;
        sel.Clear();
        foreach (var n in issue.Notes)
            if (Session.Chart.Notes.Lane(n.Kind, n.Lane).Contains(n.Note)) sel.Add(n);
        foreach (var p in issue.LaserPoints)
            if (Session.Chart.Notes.Lasers[p.Lane].Contains(p.Section) && p.Section.Points.Contains(p.Point)) sel.Add(p);
    }

    /// <summary>Ctrl+S: save as before, then the chart check as a non-blocking note in the toast.</summary>
    private void saveWithCheck()
    {
        ChartActions.Perform(EditorAction.Save);
        if (Session.IsDirty) return; // not saved (scratch chart, error): its toast stays
        LastSaveCheck = RunChartCheck();
        if (LastSaveCheck.Errors + LastSaveCheck.Warnings > 0)
            showToast($"{LastToast} - chart check: {LastSaveCheck.Summary} ({EditorKeyBindings.ShortcutOf(EditorAction.ChartCheck)} lists them)");
        if (ChartCheck.Shown.Value) ChartCheck.Refresh();
    }

    /// <summary>The check before an export, as a suffix for the export toast ("" when clean).</summary>
    private string exportCheckNote()
    {
        LastSaveCheck = RunChartCheck();
        return LastSaveCheck.Errors + LastSaveCheck.Warnings > 0
            ? $" - chart check: {LastSaveCheck.Summary} ({EditorKeyBindings.ShortcutOf(EditorAction.ChartCheck)})"
            : "";
    }

    // ---- insert / delete time ------------------------------------------------------------------------------------

    private long snapTickAtCursor()
    {
        long at = cursorPulse();
        return Math.Max(1, Session.Timing.NextSnap(at, divisorControl.Divisor.Value) - at);
    }

    /// <summary>Inserts <paramref name="length"/> pulses of blank time at the cursor (one undo step).</summary>
    public string? InsertBlank(long length, string? what = null)
    {
        long at = cursorPulse();
        if (TimeShift.InsertBlank(Session.Chart, at, length) is not { } r) return "Nothing to insert";
        Session.Selection.Clear();
        Session.Commands.Execute(r.Command);
        showToast($"Inserted {what ?? length + " pulses"} of blank time at {Session.Timing.PositionOf(at).Measure + 1}:{Session.Timing.PositionOf(at).Beat + 1}"
                  + (r.SignaturesMoved ? " (time signatures moved along)" : "") + " - Ctrl+Z undoes");
        return null;
    }

    /// <summary>Deletes [<paramref name="from"/>, from + length) and pulls everything after it back (one undo step).</summary>
    public string? DeleteTime(long from, long length)
    {
        if (TimeShift.DeleteRange(Session.Chart, from, from + length) is not { } r) return "Nothing to delete";
        Session.Selection.Clear();
        Session.Commands.Execute(r.Command);
        showToast($"Deleted {length} pulses ({(double)length / Timebase.PulsesPerQuarter:0.###} beats) at {Session.Timing.PositionOf(from).Measure + 1}:{Session.Timing.PositionOf(from).Beat + 1}, "
                  + $"{r.RemovedObjects} object(s) removed" + (r.SignaturesMoved ? ", time signatures moved along" : "") + " - Ctrl+Z undoes");
        return null;
    }

    /// <summary>Ctrl+Shift+Delete: the selection's range up to the snap tick after its end; without a selection the dialog.</summary>
    public void DeleteSelectedRange()
    {
        if (SelectionOps.Span(Session.Selection) is not { } span)
        {
            OpenTimeShift();
            return;
        }

        long end = Session.Timing.NextSnap(span.End, divisorControl.Divisor.Value);
        if (DeleteTime(span.Start, end - span.Start) is { } refused) showToast(refused);
    }

    public TimeShiftDialog OpenTimeShift()
    {
        if (dialogs.Current is TimeShiftDialog open) return open;
        long at = cursorPulse();
        var pos = Session.Timing.PositionOf(at);
        var dialog = new TimeShiftDialog($"{pos.Measure + 1}:{pos.Beat + 1} ({TimingControlPointsDialog.FormatTime(Session.PulseToMs(at))})",
            (unit, amount) => (long)Math.Round(amount * unit switch
            {
                TimeShiftUnit.SnapTicks => (double)Timebase.PulsesPerQuarter / divisorControl.Divisor.Value,
                TimeShiftUnit.Beats => Session.Timing.BeatLength(pos.Measure),
                TimeShiftUnit.Measures => Session.Timing.MeasureLength(pos.Measure),
                _ => 1,
            }),
            (pulses, delete) => pulses <= 0 ? "Enter a length above 0" : delete ? DeleteTime(at, pulses) : InsertBlank(pulses));
        dialogs.Push(dialog);
        return dialog;
    }

    // ---- lasers, holds, slams ------------------------------------------------------------------------------------

    /// <summary>Ctrl+Shift+H / Edit → Swap Laser Colour: selected lasers change lane, values kept (one undo step).</summary>
    public void SwapLaserColour()
    {
        if (!Session.Selection.Sections.Any())
        {
            showToast("Select laser points first (Swap Laser Colour moves whole sections L-R)");
            return;
        }

        if (LaserSwap.Swap(Session.Chart, Session.Selection) is not { } cmd)
        {
            showToast("Swap Laser Colour: a laser on the other side is in the way");
            return;
        }

        Session.Selection.Clear();
        Session.Commands.Execute(cmd);
        Compose.Reselect(cmd);
        showToast("Swapped laser colour (L-R, values kept)");
    }

    private List<NoteRef> selectedFxHolds() => Session.Selection.Notes.Where(n => n.Kind == NoteKind.Fx && n.Note.IsHold).ToList();

    private List<long> selectedSlamPulses() => Session.Selection.TouchedLaserPoints.Where(p => p.Point.IsSlam).Select(p => p.Section.Y + p.Point.Ry).Distinct().ToList();

    private void applyHoldEffect(string? effect)
    {
        var holds = selectedFxHolds();
        if (holds.Count == 0) return;
        if (HoldEffects.Assign(holds, effect) is not { } cmd) return;
        Session.Commands.Execute(cmd);
        showToast($"FX effect {(effect ?? "none")} on {holds.Count} hold(s)");
    }

    private void applySlamSound(string? sound)
    {
        var slams = selectedSlamPulses();
        if (slams.Count == 0) return;
        if (SlamSounds.Set(Session.Chart, slams, sound) is not { } cmd) return;
        Session.Commands.Execute(cmd);
        showToast($"Slam sound {SlamSounds.Label(sound)} on {slams.Count} slam(s)");
    }

    /// <summary>Ctrl+Shift+E: the next effect for the selected FX holds (the first hold decides which is next).</summary>
    public void CycleHoldEffect(int direction = 1)
    {
        var holds = selectedFxHolds();
        if (holds.Count == 0)
        {
            showToast("Select FX holds first");
            return;
        }

        string? next = HoldEffects.Next(Session.Chart, holds[0].Note, direction);
        if (HoldEffects.Assign(holds, next) is { } cmd) Session.Commands.Execute(cmd);
        syncToolboxToSelection();
        showToast($"FX effect: {(next == null ? "none" : EffectCatalog.Find(next)?.Label ?? next)} ({holds.Count} hold(s))");
    }

    /// <summary>The drop-downs show what the selection has (without applying it again).</summary>
    private void syncToolboxToSelection() => Schedule(() =>
    {
        syncingToolbox = true;
        try
        {
            var holds = selectedFxHolds();
            if (holds.Count > 0)
            {
                var names = holds.Select(h => HoldEffects.Of(h.Note)).Distinct().ToList();
                if (names.Count == 1) toolbox.FxEffect.Value = names[0] ?? ComposeToolbox.NoEffect;
            }

            var slams = selectedSlamPulses();
            if (slams.Count > 0)
            {
                var sounds = slams.Select(p => SlamSounds.At(Session.Chart, p)).Distinct().ToList();
                if (sounds.Count == 1) toolbox.SlamSound.Value = SlamSounds.Label(sounds[0]);
            }
        }
        finally
        {
            syncingToolbox = false;
        }
    });

    /// <summary>The effect the toolbox gives new FX holds (null = none); read by the compose tools.</summary>
    public string? NewHoldEffect => toolbox.FxEffect.Value == ComposeToolbox.NoEffect ? null : toolbox.FxEffect.Value;

    /// <summary>The sound the toolbox gives new slams (null = default); read by the compose tools.</summary>
    public string? NewSlamSound => SlamSounds.FromLabel(toolbox.SlamSound.Value);

    // ---- loop playback -------------------------------------------------------------------------------------------

    /// <summary>
    /// Shift+Space: loop over the selection (start to end), else between the bookmarks around the cursor, else the
    /// measure at the cursor; again = off. Playback starts at the loop start.
    /// </summary>
    public void ToggleLoop()
    {
        if (Playback.Loop.Value != null)
        {
            Playback.Loop.Value = null;
            showToast("Loop off");
            return;
        }

        (long From, long To, string What) range;
        double now = Playback.CurrentTime.Value;
        if (SelectionOps.Span(Session.Selection) is { } span && span.End > span.Start)
            range = (span.Start, Session.Timing.NextSnap(span.End, divisorControl.Divisor.Value), "the selection");
        else
        {
            var marks = Session.Chart.Editor.Bookmarks.Select(b => b.Y).OrderBy(y => y).ToList();
            long pulse = Math.Max(0, (long)Math.Floor(Session.MsToPulse(now)));
            long? before = marks.Where(y => y <= pulse).Cast<long?>().LastOrDefault();
            long? after = marks.Where(y => y > pulse).Cast<long?>().FirstOrDefault();
            if (before is { } b && after is { } a) range = (b, a, "the bookmarks around the cursor");
            else
            {
                int m = Session.Timing.MeasureAt(pulse);
                range = (Session.Timing.MeasureStart(m), Session.Timing.MeasureStart(m + 1), $"measure {m + 1}");
            }
        }

        double start = Session.PulseToMs(range.From), end = Session.PulseToMs(range.To);
        Playback.Loop.Value = (start, end);
        Playback.CurrentTime.Value = start;
        Playback.IsPlaying.Value = true;
        showToast($"Loop on: {range.What} ({TimingControlPointsDialog.FormatTime(start)} - {TimingControlPointsDialog.FormatTime(end)}), {EditorKeyBindings.ShortcutOf(EditorAction.ToggleLoop)} stops looping");
    }
}
