using osu.Framework.Allocation;
using osu.Framework.Platform;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Screens.Edit.Timing;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// Timing-tab, F6 and Song Setup wiring of the editor screen: the timing view model follows the
/// session (every edit, undo and redo), bookmarks live in the chart so timing changes can move them,
/// and the window title shows the chart and its unsaved state.
/// </summary>
public partial class EditorScreen
{
    private readonly TapState tapState = new TapState();
    private TimingTabContent timingContent = null!;

    [Resolved]
    private GameHost host { get; set; } = null!;

    /// <summary>Metronome lamps / tap block of the timing tab (smoke test).</summary>
    public TapTempoBlock TapBlock => tapBlock;

    public TimingTabContent TimingContent => timingContent;

    private void setupTiming()
    {
        Timing.CurrentTime = () => Playback.CurrentTime.Value;
        Timing.Message = showToast;
        Timing.Attach(Session);
        tapBlock.ApplyTap = timingContent.ApplyTap;

        // the chart is the truth for bookmarks: every change is a command, timing edits move them, undo brings them back
        Session.ChartChanged += onSessionChanged;
        updateWindowTitle();
    }

    private void teardownTiming()
    {
        Session.ChartChanged -= onSessionChanged;
        Timing.Detach();
        if (host.Window != null) host.Window.Title = EditorGame.WindowTitle;
    }

    private void onSessionChanged()
    {
        updateChartLength();
        reloadBookmarks();
        updateWindowTitle();
    }

    private void reloadBookmarks()
    {
        var ms = Session.Chart.Editor.Bookmarks.Select(b => Math.Round(Session.PulseToMs(b.Y))).ToList();
        if (ms.SequenceEqual(Bookmarks)) return;

        Bookmarks.Clear();
        Bookmarks.AddRange(ms);
    }

    /// <summary>"SDVX Chart Editor - Title [EXH 17] *" (the star = unsaved changes).</summary>
    public string TitleText
    {
        get
        {
            var m = Session.Chart.Meta;
            string level = Core.Charts.Difficulty.FormatLevel(m.Level);
            return $"{EditorGame.WindowTitle} - {m.Title} [{m.DifficultyDisplayName} {level}]{(Session.IsDirty ? " *" : "")}";
        }
    }

    private void updateWindowTitle()
    {
        if (host.Window != null) host.Window.Title = TitleText;
    }

    private void resnapAll()
    {
        Session.Commands.Execute(TimingCommands.Resnap(Session.Chart, divisorControl.Divisor.Value));
        showToast($"All notes snapped to 1/{divisorControl.Divisor.Value} (Ctrl+Z restores them)");
    }

    private void setPreviewHere()
    {
        var bgm = Session.Chart.Audio.Bgm;
        long at = Math.Max(0, (long)Math.Round(Playback.CurrentTime.Value));
        if (at == bgm.PreviewOffsetMs) return;
        Session.Commands.Execute(TimingCommands.SetPreview(at, bgm.PreviewDurationMs));
        showToast($"Preview starts at {Dialogs.TimingControlPointsDialog.FormatTime(at)}");
    }
}
