using osu.Framework.Allocation;
using osu.Framework.Platform;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// Bottom-bar wiring of the editor screen: the scrubbable length (the audio's, see <see cref="EditorClock.LengthMs"/>),
/// the bookmark buttons and keys (every change one undo step), the Collab button, the jump-to-time dialog behind the
/// time display and the modding timestamps Ctrl+C puts on the OS clipboard (<see cref="ChartTimestamp"/>).
/// </summary>
public partial class EditorScreen
{
    [Resolved]
    private Clipboard osClipboard { get; set; } = null!;

    private void setupBottomBar()
    {
        BottomBar.Trigger = a => TriggerAction(a);
        BottomBar.OpenCollab = () => OpenBookmarkList();
        BottomBar.OpenTimeDialog = () => OpenTimeDialog();

        if (ChartActions is EditorChartActions actions)
            actions.CopyTimestamp = text => osClipboard.SetText(text);
    }

    /// <summary>Tells the clock where the chart ends (after every chart change; the audio length it knows itself).</summary>
    private void updateChartLength()
    {
        Playback.LastObjectMs = Session.PulseToMs(Session.Chart.LastEventPulse());
        Playback.ChartEndMs = Session.ChartEndMs;
    }

    private static string time(double ms) => ChartTimestamp.FormatTime(ms);

    // ---- bookmarks ---------------------------------------------------------

    /// <summary>Ctrl+B / the bottom bar's + button: a bookmark at the cursor (one undo step), unless one is there.</summary>
    public void AddBookmarkAtCursor()
    {
        double now = Playback.CurrentTime.Value;
        if (BookmarkTools.HasBookmarkAtCursor)
        {
            showToast($"There is a bookmark at {time(now)} already");
            return;
        }

        BookmarkTools.AddAtCursor();
        showToast($"Bookmark at {time(now)} (Ctrl+Z removes it)");
    }

    /// <summary>Ctrl+Shift+B / the bottom bar's − button: deletes the bookmark nearest to the cursor within 2 s (one undo step).</summary>
    public void RemoveBookmarkNearCursor()
    {
        if (BookmarkTools.RemoveNearCursor() is double removed)
            showToast($"Bookmark at {time(removed)} removed (Ctrl+Z restores it)");
        else
            showToast("No bookmark within 2 s of the cursor");
    }

    // ---- jump to time / timestamps -----------------------------------------

    /// <summary>The time display's dialog: current time pre-selected, Enter jumps to a typed time or pasted timestamp.</summary>
    public JumpToTimeDialog OpenTimeDialog()
    {
        if (dialogs.Current is JumpToTimeDialog open)
            return open;

        Playback.IsPlaying.Value = false;
        var dialog = new JumpToTimeDialog(time(Playback.CurrentTime.Value), text => JumpTo(text) is (false, var why) ? why : null);
        dialogs.Push(dialog);
        return dialog;
    }

    /// <summary>
    /// Jumps to a time or an osu!-style timestamp. A timestamp with objects also selects exactly those objects
    /// (compose tab, select tool); objects it cannot find or that are ambiguous are named in the toast.
    /// </summary>
    public (bool Ok, string Message) JumpTo(string text)
    {
        if (!ChartTimestamp.TryParse(text, out var ts))
            return (false, "Not a time: use mm:ss:mmm, ss.mmm, ms or a timestamp like 01:23:456 (...) -");

        Playback.IsPlaying.Value = false;
        Playback.CurrentTime.Value = Math.Clamp(ts.TimeMs, 0, Playback.LengthMs);

        if (ts.Items.Count == 0 && ts.Unparsed.Count == 0)
        {
            string jumped = $"Jumped to {time(Playback.CurrentTime.Value)}";
            showToast(jumped);
            return (true, jumped);
        }

        var result = ts.Resolve(Session.Chart, Session.Timing);
        ReferenceSession?.Selection.Clear();
        result.ApplyTo(Session.Selection);
        if (result.Count > 0)
        {
            SelectTab(EditorTab.Compose);
            SelectTool(EditorTool.Select);
        }

        var message = new List<string> { $"Jumped to {time(ts.TimeMs)}, selected {result.Count} object(s)" };
        if (result.Missing.Count > 0) message.Add($"not found: {string.Join(", ", result.Missing.Take(3))}{(result.Missing.Count > 3 ? ", ..." : "")}");
        if (result.Ambiguous.Count > 0) message.Add($"ambiguous: {string.Join(", ", result.Ambiguous.Take(3))}{(result.Ambiguous.Count > 3 ? ", ..." : "")}");
        if (ts.Unparsed.Count > 0) message.Add($"unreadable: {string.Join(", ", ts.Unparsed.Take(3))}");
        string all = string.Join("; ", message);
        showToast(all);
        LastJump = result;
        return (true, all);
    }

    /// <summary>What the last timestamp jump resolved to (smoke test).</summary>
    public TimestampResolution? LastJump { get; private set; }
}
