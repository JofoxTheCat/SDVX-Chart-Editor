using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// Chart actions of the key binding table, carried out with Core's <see cref="SelectionOps"/> and
/// commands on the session's undo stack. Refused operations (overlap, empty selection) answer with a
/// toast and still count as handled; only actions that are not built yet return false.
/// </summary>
public sealed class EditorChartActions : IEditorChartActions
{
    private readonly EditorSession session;
    private readonly ComposeEditor editor;
    private readonly Func<double> currentTime;
    private readonly Action<string> toast;

    /// <summary>
    /// Receives the osu!-style modding timestamp of every Ctrl+C / Ctrl+X of the chart's own selection
    /// (<see cref="ChartTimestamp"/>); the screen puts it on the OS clipboard.
    /// </summary>
    public Action<string>? CopyTimestamp { get; set; }

    /// <summary>The last timestamp handed to <see cref="CopyTimestamp"/> (smoke test).</summary>
    public string? LastTimestamp { get; private set; }

    /// <summary>Runs after the active chart was switched or reverted.</summary>
    public Action? ChartReplaced { get; set; }

    /// <summary>
    /// A second, read-only selection Ctrl+C copies from while the main selection is empty — the reference
    /// difficulty ("open difficulty for reference"). Returns null when no reference is open, plus a name for the toast.
    /// </summary>
    public Func<(Selection Selection, string Name)?>? ReferenceSelection { get; set; }

    private (Selection Selection, string Name)? activeReference =>
        selection.IsEmpty && ReferenceSelection?.Invoke() is { Selection.IsEmpty: false } r ? r : null;

    public EditorChartActions(EditorSession session, ComposeEditor editor, Func<double> currentTime, Action<string> toast)
    {
        this.session = session;
        this.editor = editor;
        this.currentTime = currentTime;
        this.toast = toast;
    }

    private Chart chart => session.Chart;
    private Selection selection => session.Selection;
    private CommandStack commands => session.Commands;

    /// <summary>
    /// Asked first for every action: the screen takes the ones that need dialogs (open / create a difficulty, with the
    /// unsaved-changes prompt; EditorScreen.Menu.cs). Returns true when it handled the action.
    /// </summary>
    public Func<EditorAction, bool>? Intercept { get; set; }

    public bool Perform(EditorAction action)
    {
        if (Intercept?.Invoke(action) == true) return true;

        switch (action)
        {
            case EditorAction.Save: save(); return true;

            case EditorAction.Undo:
                if (!commands.Undo()) toast("Nothing to undo");
                else toast($"Undo: {commands.RedoDescription}");
                return true;

            case EditorAction.Redo:
                if (!commands.Redo()) toast("Nothing to redo");
                else toast($"Redo: {commands.UndoDescription}");
                return true;

            case EditorAction.Copy: copy(); return true;

            case EditorAction.Cut:
                if (activeReference is { } reference)
                {
                    toast($"The reference ({reference.Name}) is read-only: use Ctrl+C");
                    return true;
                }

                if (copy()) run(SelectionOps.Delete(selection), "Cut");
                return true;

            case EditorAction.Paste: paste(); return true;
            case EditorAction.Delete: run(SelectionOps.Delete(selection), "Delete"); return true;

            case EditorAction.SelectAll:
                selection.SelectRange(chart, 0, long.MaxValue);
                return true;

            case EditorAction.ReverseSelection: invertSelection(); return true;

            case EditorAction.Clone:
                if (SelectionOps.Span(selection) is { } span)
                    run(SelectionOps.Duplicate(chart, selection, session.Timing.NextSnap(span.End, editor.Divisor.Value)), "Clone", reselect: true);
                else
                    toast("Nothing selected");
                return true;

            case EditorAction.MirrorHorizontal: run(SelectionOps.Mirror(chart, selection), "Mirror", reselect: true); return true;
            case EditorAction.ReverseInTime: run(SelectionOps.Reverse(chart, selection), "Reverse", reselect: true); return true;

            case EditorAction.NudgeBackward:
            case EditorAction.MoveSelectionEarlier:
                moveByTick(-1);
                return true;

            case EditorAction.NudgeForward:
            case EditorAction.MoveSelectionLater:
                moveByTick(1);
                return true;

            case EditorAction.MoveSelectionLaneLeft: run(SelectionOps.Move(chart, selection, 0, -1), "Move", reselect: true); return true;
            case EditorAction.MoveSelectionLaneRight: run(SelectionOps.Move(chart, selection, 0, 1), "Move", reselect: true); return true;

            case EditorAction.ToggleSelectionWide: toggleWide(); return true;
            case EditorAction.ToggleSelectionSlam: toggleSlam(); return true;

            case EditorAction.ClearAllNotes: clearAll(); return true;

            case EditorAction.RevertToSaved:
            case EditorAction.RevertToSavedFull:
                if (session.ChartPath == null) toast("Nothing saved to revert to");
                else
                {
                    session.RevertToSaved();
                    ChartReplaced?.Invoke();
                    toast("Reverted to " + Path.GetFileName(session.ChartPath));
                }

                return true;

            case EditorAction.OpenDifficulty: nextDifficulty(); return true;
        }

        return false;
    }

    private void save()
    {
        if (session.SaveTarget == null)
        {
            toast("Scratch chart: open a song folder to save");
            return;
        }

        try
        {
            string path = session.Save();
            toast("Saved " + Path.GetFileName(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            toast("Save failed: " + e.Message);
        }
    }

    private bool copy()
    {
        if (activeReference is { } reference)
        {
            EditorSession.Clipboard = ClipboardData.FromSelection(reference.Selection);
            toast($"Copied {reference.Selection.Count} object(s) from the reference ({reference.Name}); Ctrl+V pastes at the cursor");
            return true;
        }

        if (selection.IsEmpty)
        {
            toast("Nothing selected");
            return false;
        }

        EditorSession.Clipboard = ClipboardData.FromSelection(selection);
        // like osu!: the OS clipboard gets the modding timestamp of the selection ("01:23:456 (…) - ")
        LastTimestamp = ChartTimestamp.Of(session.Timing, selection, currentTime()).ToString();
        CopyTimestamp?.Invoke(LastTimestamp);
        string shown = LastTimestamp.TrimEnd(' ', '-');
        if (shown.Length > 60) shown = shown[..57] + "...";
        toast($"Copied {selection.Count} object(s), timestamp on the clipboard: {shown}");
        return true;
    }

    private void paste()
    {
        if (EditorSession.Clipboard is not { IsEmpty: false } clip)
        {
            toast("Clipboard is empty");
            return;
        }

        long pulse = Math.Max(0, session.Timing.Snap((long)Math.Round(session.MsToPulse(currentTime())), editor.Divisor.Value));
        run(SelectionOps.Paste(chart, clip, pulse), "Paste", reselect: true);
    }

    private void moveByTick(int direction)
    {
        if (selection.StartPulse is not long start)
        {
            toast("Nothing selected");
            return;
        }

        long delta = direction > 0
            ? session.Timing.NextSnap(start, editor.Divisor.Value) - start
            : session.Timing.PreviousSnap(start, editor.Divisor.Value) - start;
        run(SelectionOps.Move(chart, selection, delta), "Move", reselect: true);
    }

    /// <summary>Executes a Core command; null means the operation was refused.</summary>
    private void run(IEditCommand? cmd, string what, bool reselect = false)
    {
        if (cmd == null)
        {
            toast(selection.IsEmpty ? "Nothing selected" : $"{what}: would overlap or leave the chart");
            return;
        }

        if (reselect) selection.Clear();
        commands.Execute(cmd);
        if (reselect) editor.Reselect(cmd);
    }

    private void invertSelection()
    {
        var notes = selection.Notes.ToHashSet();
        var points = selection.LaserPoints.ToHashSet();
        var ends = selection.SlamEnds.ToHashSet();
        selection.Clear();

        foreach (var kind in new[] { NoteKind.Bt, NoteKind.Fx })
            for (int lane = 0; lane < NoteData.LaneCount(kind); lane++)
                foreach (var n in chart.Notes.Lane(kind, lane))
                {
                    var r = new NoteRef(kind, lane, n);
                    if (!notes.Contains(r)) selection.Add(r);
                }

        for (int lane = 0; lane < 2; lane++)
            foreach (var s in chart.Notes.Lasers[lane])
                foreach (var p in s.Points)
                {
                    var r = new LaserPointRef(lane, s, p);
                    if (points.Contains(r)) continue;
                    // a slam with one end selected: the other end
                    var start = new LaserSlamEndRef(lane, s, p, false);
                    if (ends.Contains(start) || ends.Contains(start with { End = true }))
                    {
                        if (!ends.Contains(start)) selection.Add(start);
                        else if (!ends.Contains(start with { End = true })) selection.Add(start with { End = true });
                    }
                    else selection.Add(r);
                }
    }

    /// <summary>Ctrl+W: every selected laser switches between normal and wide, keeping its values (it stretches around the centre, as before).</summary>
    private void toggleWide()
    {
        var sections = selection.Sections.ToList();
        if (sections.Count == 0)
        {
            toast("Select laser points first");
            return;
        }

        SetWidth(sections.Select(s => (s.Lane, s.Section, s.Section.Width == 2 ? 1 : 2)).ToList(), "Toggle wide laser", keepPosition: false);
    }

    /// <summary>
    /// The Wide Laser toggle (W, toggle bar, menu) with lasers selected: they become wide / normal with it. Returns false
    /// when no laser is selected (the toggle then only sets the width of new lasers).
    /// </summary>
    public bool SetSelectionWidth(bool wide)
    {
        var sections = selection.Sections.Where(s => s.Section.Width != (wide ? 2 : 1)).ToList();
        if (!selection.Sections.Any()) return false;
        if (sections.Count == 0) return true;
        SetWidth(sections.Select(s => (s.Lane, s.Section, wide ? 2 : 1)).ToList(), wide ? "Wide laser" : "Normal laser");
        return true;
    }

    /// <summary>
    /// One undo step: each section at its new width with its points / anchors kept where they are on screen (Core
    /// <see cref="LaserEditing.WithWidth"/>). Narrowing clamps what lay beyond the lanes to 0 / 1 and says so.
    /// </summary>
    private void SetWidth(IReadOnlyList<(int Lane, LaserSection Section, int Width)> sections, string name, bool keepPosition = true)
    {
        int clamped = 0;
        var items = sections.Select(s =>
        {
            var (section, n) = LaserEditing.WithWidth(s.Section, s.Width, keepPosition);
            clamped += n;
            return (s.Lane, s.Section, s.Lane, section);
        }).ToList();
        run(new ReplaceLaserSectionsCommand(name, items), name, reselect: true);
        if (clamped > 0) toast($"{name}: {clamped} value(s) beyond the normal range were moved to the edge (Ctrl+Z undoes)");
    }

    /// <summary>A selected slam (or slam end) loses its jump; a selected plain point merges with the next point into a slam.</summary>
    private void toggleSlam()
    {
        var groups = selection.TouchedLaserPoints.GroupBy(p => (p.Lane, p.Section)).ToList();
        if (groups.Count == 0)
        {
            toast("Select laser points first");
            return;
        }

        var items = new List<(int, LaserSection, int, LaserSection)>();
        foreach (var g in groups)
        {
            var (lane, section) = g.Key;
            LaserSection result = section;
            foreach (int index in g.Select(p => section.Points.IndexOf(p.Point)).Where(i => i >= 0).OrderByDescending(i => i))
            {
                var pt = result.Points[index];
                if (pt.IsSlam)
                {
                    result = result.Clone();
                    result.Points[index].Vf = null;
                }
                else if (index < result.Points.Count - 1)
                {
                    long pulse = result.Y + pt.Ry;
                    result = ComposeEditor.EditPoint(result, index + 1, pulse, result.Points[index + 1].V, false) ?? result;
                }
            }

            if (result != section) items.Add((lane, section, lane, result));
        }

        if (items.Count == 0) toast("Nothing to change");
        else run(new ReplaceLaserSectionsCommand("Toggle slam", items), "Toggle slam", reselect: true);
    }

    private void clearAll()
    {
        var cmds = new List<IEditCommand>();
        var notes = new List<NoteRef>();
        foreach (var kind in new[] { NoteKind.Bt, NoteKind.Fx })
            for (int lane = 0; lane < NoteData.LaneCount(kind); lane++)
                notes.AddRange(chart.Notes.Lane(kind, lane).Select(n => new NoteRef(kind, lane, n)));
        if (notes.Count > 0) cmds.Add(new RemoveNotesCommand(notes, "Clear notes"));
        for (int lane = 0; lane < 2; lane++)
            cmds.AddRange(chart.Notes.Lasers[lane].ToList().Select(s => (IEditCommand)new RemoveLaserSectionCommand(lane, s)));

        if (cmds.Count == 0)
        {
            toast("The chart is already empty");
            return;
        }

        selection.Clear();
        commands.Execute(new CompositeCommand("Clear all notes", cmds));
        toast("All notes cleared (Ctrl+Z restores them)");
    }

    private void nextDifficulty()
    {
        var charts = session.Charts;
        if (charts.Count < 2)
        {
            toast("This song has no other difficulty");
            return;
        }

        if (session.IsDirty)
        {
            toast("Save first (Ctrl+S): switching drops the undo history");
            return;
        }

        int i = charts.ToList().FindIndex(c => c == session.ActiveChart);
        var next = charts[(i + 1) % charts.Count];
        session.SwitchChart(next);
        ChartReplaced?.Invoke();
        toast($"Opened {next.Chart.Meta.DifficultyDisplayName} ({Path.GetFileName(next.FilePath)})");
    }
}
