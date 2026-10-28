using osu.Framework.Allocation;
using osu.Framework.Configuration;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Screens;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Timing;
using SDVXEditor.Game.Songs;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// The editor's menu bar (osu!stable layout, SDVX meanings). Every item does something visible — a dialog, a state
/// change or at least a toast (<see cref="runMenuAction"/> shows the item's name when the action itself said nothing);
/// items that made no sense for SDVX were removed. The shortcut column shows the binding in effect
/// (<see cref="EditorKeyBindings.ShortcutOf"/>, rebuilt on every rebind and keyboard layout change).
/// File: New / Open difficulty with the unsaved-changes prompt, Save, Export (kson, ksh, VOX, .zip, .svz — the song
/// select's <see cref="LibraryManager.ExportAsync"/>), Revert, Open song folder, Exit with the prompt.
/// </summary>
public partial class EditorScreen
{
    [Resolved]
    private StartupOptions startupOptions { get; set; } = null!;

    /// <summary>Exports started from the menu that have not finished yet (smoke test).</summary>
    public int ExportsRunning { get; private set; }

    /// <summary>The file or folder the last menu export wrote.</summary>
    public string? LastExportPath { get; private set; }

    /// <summary>The folder File → Open Song Folder showed last (a smoke run records it instead of starting Explorer).</summary>
    public string? LastOpenedFolder { get; private set; }

    /// <summary>How many menu items ran, and the last one's text (smoke test).</summary>
    public int MenuActionsRun { get; private set; }

    public string? LastMenuAction { get; private set; }

    /// <summary>Number of toasts shown so far (smoke test: a repeated message still counts).</summary>
    public int ToastCount => toast.ShowCount;

    private void setupMenu()
    {
        // Ctrl+O and the (unbound by default) new-difficulty action need dialogs, so the screen takes them
        if (ChartActions is EditorChartActions actions)
        {
            actions.Intercept = action => action switch
            {
                EditorAction.OpenDifficulty => OpenDifficultyChooser(),
                EditorAction.NewDifficulty => StartNewDifficulty(),
                _ => false,
            };
        }
    }

    /// <summary>Every clickable item of the menu bar with its parents, as "File &gt; Export Song &gt; kson..." (smoke test walks them all).</summary>
    public IReadOnlyList<(string Path, MenuItem Item, IReadOnlyList<MenuItem> Parents)> MenuLeaves()
    {
        var list = new List<(string, MenuItem, IReadOnlyList<MenuItem>)>();

        void walk(IReadOnlyList<MenuItem> items, List<MenuItem> parents)
        {
            foreach (var item in items)
            {
                if (item is EditorMenuSeparator) continue;

                var chain = parents.Append(item).ToList();
                if (item.Items.Count > 0) walk(item.Items, chain);
                else list.Add((string.Join(" > ", chain.Select(p => p.Text.Value.ToString())), item, parents.ToList()));
            }
        }

        walk(MenuBar.Items, new List<MenuItem>());
        return list;
    }

    // ---- menu --------------------------------------------------------------

    private MenuItem[] createMenu()
    {
        // the shortcut column shows the binding in effect (rebindable in the options)
        static string k(EditorAction action) => EditorKeyBindings.ShortcutOf(action);
        EditorMenuItem item(string text, string shortcut, Action action) => new EditorMenuItem(text, shortcut, () => runMenuAction(text, action));
        EditorMenuItem act(string text, EditorAction action) => item(text, k(action), () => TriggerAction(action));
        EditorMenuItem sub(string text, params MenuItem[] items) => new EditorMenuItem(text, items);

        return new MenuItem[]
        {
            sub("File",
                item("Create a New Difficulty...", k(EditorAction.NewDifficulty), () => StartNewDifficulty()),
                item("Open Difficulty...", k(EditorAction.OpenDifficulty), () => OpenDifficultyChooser()),
                createReferenceMenu(),
                new EditorMenuSeparator(),
                act("Save", EditorAction.Save),
                sub("Export Song",
                    item("kson...", "", () => _ = ExportSong(SongExportFormat.Kson)),
                    item("ksh...", "", () => _ = ExportSong(SongExportFormat.Ksh)),
                    item("VOX (via ksh2vox)...", "", () => _ = ExportSong(SongExportFormat.Vox)),
                    item("Song folder as .zip...", "", () => _ = ExportSong(SongExportFormat.Zip)),
                    item(".svz (share / collab)...", "", () => _ = ExportSvz())),
                new EditorMenuSeparator(),
                item("Revert to Saved", k(EditorAction.RevertToSaved), RevertWithPrompt),
                act("Clear All Notes", EditorAction.ClearAllNotes),
                item("Test Chart", k(EditorAction.Playtest), Playtest),
                new EditorMenuSeparator(),
                item("Open Song Folder", "", OpenSongFolder),
                item("Options...", k(EditorAction.ShowOptions), () => OpenOptions()),
                new EditorMenuSeparator(),
                item("Exit...", k(EditorAction.Exit), ExitWithPrompt)),
            sub("Edit",
                act("Undo", EditorAction.Undo),
                act("Redo", EditorAction.Redo),
                new EditorMenuSeparator(),
                act("Cut", EditorAction.Cut),
                act("Copy", EditorAction.Copy),
                act("Paste", EditorAction.Paste),
                act("Delete", EditorAction.Delete),
                act("Select All", EditorAction.SelectAll),
                act("Invert Selection", EditorAction.ReverseSelection),
                new EditorMenuSeparator(),
                act("Clone", EditorAction.Clone),
                act("Mirror Horizontally", EditorAction.MirrorHorizontal),
                act("Reverse in Time", EditorAction.ReverseInTime),
                act("Nudge Backward", EditorAction.NudgeBackward),
                act("Nudge Forward", EditorAction.NudgeForward),
                act("Toggle Wide Laser (selection)", EditorAction.ToggleSelectionWide),
                act("Toggle Slam (selection)", EditorAction.ToggleSelectionSlam),
                act("Swap Laser Colour (selection)", EditorAction.SwapLaserColour),
                act("Cycle FX Hold Effect (selection)", EditorAction.CycleHoldEffect),
                new EditorMenuSeparator(),
                act("Check Chart...", EditorAction.ChartCheck),
                new EditorMenuSeparator(),
                act("Add Bookmark", EditorAction.AddBookmark),
                item("Remove Bookmark", k(EditorAction.RemoveBookmark), () => bookmarkAction(EditorAction.RemoveBookmark, "No bookmark within 2 s of the cursor")),
                item("Previous Bookmark", k(EditorAction.PreviousBookmark), () => bookmarkAction(EditorAction.PreviousBookmark, "No bookmark before the cursor")),
                item("Next Bookmark", k(EditorAction.NextBookmark), () => bookmarkAction(EditorAction.NextBookmark, "No bookmark after the cursor")),
                item("Bookmarks...", "", () => OpenBookmarkList())),
            sub("View",
                item("Compose", k(EditorAction.TabCompose), () => SelectTab(EditorTab.Compose)),
                item("Camera", k(EditorAction.TabCamera), () => SelectTab(EditorTab.Camera)),
                item("Timing", k(EditorAction.TabTiming), () => SelectTab(EditorTab.Timing)),
                item("Effects", k(EditorAction.TabEffects), () => SelectTab(EditorTab.Effects)),
                item("Song Setup...", k(EditorAction.SongSetup), () => OpenSongSetup()),
                item("Timing Setup...", k(EditorAction.TimingSetup), () => OpenTimingSetup()),
                item("Audio Offset & Hitsounds...", "", () => OpenAudioOffset()),
                item("Keyboard Shortcuts...", k(EditorAction.ShowShortcuts), () => OpenShortcuts()),
                new EditorMenuSeparator(),
                act("Inspector", EditorAction.ToggleInspector),
                act("Layers", EditorAction.ToggleLayers),
                act("Loop Playback", EditorAction.ToggleLoop),
                new EditorMenuSeparator(),
                sub("Background Dim", new[] { 0f, 0.25f, 0.5f, 0.75f }.Select(v => (MenuItem)item($"{v * 100:0} %", "", () => setDim(v))).ToArray()),
                new EditorMenuSeparator(),
                item("Borderless / Windowed", "Alt+Enter", toggleWindowMode)),
            sub("Compose",
                sub("Tool",
                    composeItem("Select", EditorAction.ToolSelect),
                    composeItem("BT Chip", EditorAction.ToolBtChip),
                    composeItem("BT Hold", EditorAction.ToolBtHold),
                    composeItem("FX Chip", EditorAction.ToolFxChip),
                    composeItem("FX Hold", EditorAction.ToolFxHold),
                    composeItem("Laser L", EditorAction.LaserSideLeft),
                    composeItem("Laser R", EditorAction.LaserSideRight)),
                sub("Snap Divisor", EditorColours.Divisor.Steps.Select(d => (MenuItem)item($"1/{d}", divisorShortcut(d), () => setDivisorFromMenu(d))).ToArray()),
                sub("Audio Rate", new[] { 0.25, 0.5, 0.75, 1.0 }.Select(r => (MenuItem)item($"{r * 100:0} %", "", () => setRate(r))).ToArray()),
                new EditorMenuSeparator(),
                composeToggle("Grid Snapping", EditorAction.ToggleGridSnap, () => toggles.GridSnap),
                item("Wide Laser", k(EditorAction.ToggleWideLaser), ToggleWideLaser),
                composeToggle("Slam Assist", EditorAction.ToggleSlamAssist, () => toggles.SlamAssist),
                composeToggle("Lock Notes", EditorAction.ToggleLockNotes, () => toggles.LockNotes),
                composeToggle("Mirror Preview", EditorAction.ToggleMirrorPreview, () => toggles.MirrorPreview)),
            sub("Camera",
                item("Camera Tab", k(EditorAction.TabCamera), () => SelectTab(EditorTab.Camera)),
                item("Add Zoom Top Point", "", () => addCameraPoint(CamGraph.ZoomTop)),
                item("Add Zoom Bottom Point", "", () => addCameraPoint(CamGraph.ZoomBottom)),
                item("Add Tilt Point", "", addTiltPoint),
                item("Reset Camera at Cursor", "", resetCameraHere)),
            sub("Timing",
                item("Time Signature...", "", openTimeSignature),
                item("Metronome Clicks", "", () =>
                {
                    Timing.MetronomeClicks.Toggle();
                    showToast($"Metronome clicks {(Timing.MetronomeClicks.Value ? "on" : "off")}");
                }),
                item("Add BPM Point", k(EditorAction.AddBpmPoint), () => addTimingPoint(ControlPointKind.Bpm)),
                item("Add Scroll Speed Point", k(EditorAction.AddScrollSpeedPoint), () => addTimingPoint(ControlPointKind.ScrollSpeed)),
                item("Delete Timing Point", k(EditorAction.DeleteTimingPoint), deleteTimingPoint),
                item("Timing Setup...", k(EditorAction.TimingSetup), () => OpenTimingSetup()),
                item("Audio Offset & Hitsounds...", "", () => OpenAudioOffset()),
                new EditorMenuSeparator(),
                item("Resnap All Notes", "", resnapAll),
                act("Insert Blank Snap Tick", EditorAction.InsertBlankTick),
                act("Insert Blank Beat", EditorAction.InsertBlankBeat),
                act("Insert / Delete Time...", EditorAction.InsertBlankCustom),
                act("Delete Selected Range", EditorAction.DeleteRange),
                sub("Move All Notes",
                    item("One Snap Tick Earlier", "", () => moveAllNotes(-snapTickPulses(), "one snap tick earlier")),
                    item("One Snap Tick Later", "", () => moveAllNotes(snapTickPulses(), "one snap tick later")),
                    item("One Beat Earlier", "", () => moveAllNotes(-Timebase.PulsesPerQuarter, "one beat earlier")),
                    item("One Beat Later", "", () => moveAllNotes(Timebase.PulsesPerQuarter, "one beat later"))),
                item("Set Current Position as Preview Point", "", setPreviewHere)),
            sub("Help",
                item("Keyboard Shortcuts", k(EditorAction.ShowShortcuts), () => OpenShortcuts()),
                item("Tutorial...", "", () => (Dependencies.Get<osu.Framework.Game>() as EditorGame)?.Tutorial.ShowChapterList()),
                item("About SDVX Chart Editor", "", () => OpenAbout())),
            // top-level, no submenu: one click opens the credits
            item("Thanks", "", () => OpenThanks()),
        };

        // compose-tab actions (tools) only act on the Compose tab: the menu switches there first
        EditorMenuItem composeItem(string text, EditorAction action) => item(text, k(action), () =>
        {
            SelectTab(EditorTab.Compose);
            TriggerAction(action);
            showToast($"{text} tool");
        });

        EditorMenuItem composeToggle(string text, EditorAction action, Func<osu.Framework.Bindables.BindableBool> toggle) => item(text, k(action), () =>
        {
            var b = toggle();
            b.Toggle();
            showToast($"{text} {(b.Value ? "on" : "off")}");
        });
    }

    /// <summary>
    /// Runs a menu item. When the action itself gave no feedback (no toast, no dialog, still on this screen) the item's
    /// name is shown as a toast, so no menu click is ever silent.
    /// </summary>
    private void runMenuAction(string text, Action action)
    {
        int toasts = toast.ShowCount;
        int open = dialogs.OpenDialogs.Count;
        MenuActionsRun++;
        LastMenuAction = text;

        action();

        if (toast.ShowCount == toasts && dialogs.OpenDialogs.Count == open && this.IsCurrentScreen())
            showToast(text.TrimEnd('.'));
    }

    private void bookmarkAction(EditorAction action, string none)
    {
        int count = Bookmarks.Count;
        double time = Playback.CurrentTime.Value;
        TriggerAction(action);
        if (Bookmarks.Count == count && Math.Abs(Playback.CurrentTime.Value - time) < 0.5)
            showToast(none);
    }

    private void setDim(float value)
    {
        Background.Dim.Value = value;
        showToast($"Background dim {value * 100:0} %");
    }

    private void setRate(double rate)
    {
        Playback.Rate.Value = rate;
        showToast($"Audio rate {rate * 100:0} %");
    }

    private static string divisorShortcut(int d)
    {
        if (d <= 9) return EditorKeyBindings.ShortcutOf(EditorAction.Divisor1 + (d - 1));
        int i = Array.IndexOf(new[] { 12, 16, 24, 32, 48, 64, 96, 192 }, d);
        return i >= 0 ? EditorKeyBindings.ShortcutOf(EditorAction.DivisorSet12 + i) : "";
    }

    private void setDivisorFromMenu(int divisor)
    {
        setDivisor(divisor);
        showToast($"Beat snap divisor 1/{divisorControl.Divisor.Value}");
    }

    private void toggleWindowMode()
    {
        var mode = frameworkConfig.GetBindable<WindowMode>(FrameworkSetting.WindowMode);
        mode.Value = EditorGame.ToggledWindowMode(mode.Value);
        showToast(mode.Value == WindowMode.Borderless ? "Borderless (fullscreen window)" : "Windowed");
    }

    public AboutDialog OpenAbout()
    {
        if (dialogs.Current is AboutDialog open)
            return open;

        var dialog = new AboutDialog();
        dialogs.Push(dialog);
        return dialog;
    }

    /// <summary>Menu bar → Thanks: the credits list (embedded credits.json).</summary>
    public CreditsDialog OpenThanks()
    {
        if (dialogs.Current is CreditsDialog open)
            return open;

        var dialog = new CreditsDialog();
        dialogs.Push(dialog);
        return dialog;
    }

    private void openTimeSignature()
    {
        var dialog = OpenTimingSetup();
        dialog.AddKind.Value = TimingControlPointsDialog.NewPointKind.TimeSignature;
        dialog.ShowListTab(1);
        showToast("Time signatures: pick \"Time signature\" and press + (meter column in the list)");
    }

    // ---- camera ------------------------------------------------------------

    private long cursorPulse() =>
        Session.Timing.Snap(Math.Max(0, (long)Math.Round(Session.MsToPulse(Playback.CurrentTime.Value))), divisorControl.Divisor.Value);

    private void addCameraPoint(CamGraph graph)
    {
        long pulse = cursorPulse();
        var list = CameraGraphs.Get(Session.Chart.Camera, graph);
        if (list.Any(p => p.Y == pulse))
        {
            showToast($"{CameraGraphs.Name(graph)} already has a point here");
            return;
        }

        // an anchor with the value the graph has here, so adding it changes nothing until it is edited; inside a curve a
        // new anchor would reshape it, so that is left to the camera tab (Ctrl+click inserts a control anchor there)
        var lane = (CamPathLane)(int)graph;
        if (CameraPathCommands.InsideCurve(Session.Chart, lane, pulse))
        {
            showToast($"{CameraGraphs.Name(graph)} is a curve here: add anchors in the Camera tab (Ctrl+click on the curve)");
            return;
        }

        double value = Math.Round(Graph.ValueAt(list, pulse, 0), 6);
        if (CameraPathCommands.Add(Session.Chart, lane, pulse, value, null, null) is not { } added) return;
        Session.Commands.Execute(added.Command);
        showToast($"{CameraGraphs.Name(graph)} point at {TimingControlPointsDialog.FormatTime(Playback.CurrentTime.Value)} (edit it in the Camera tab)");
    }

    private void addTiltPoint()
    {
        long pulse = cursorPulse();
        var tilt = Session.Chart.Camera.Tilt;
        if (tilt.Any(p => p.Y == pulse))
        {
            showToast("Tilt already has a point here");
            return;
        }

        var value = tilt.LastOrDefault(p => p.Y <= pulse) is { } before ? before.Final ?? before.Value : TiltValue.Of(TiltMode.Normal);
        if (value.IsAuto)
            Session.Commands.Execute(new AddTiltPointCommand(new TiltPoint(pulse, value)));
        else
        {
            // a manual value: an anchor of that manual run (on the curve's value), refused inside a curve like the graphs
            if (CameraPathCommands.InsideCurve(Session.Chart, CamPathLane.Tilt, pulse))
            {
                showToast("Tilt is a curve here: add anchors in the Camera tab (Ctrl+click on the curve)");
                return;
            }

            double manual = Math.Round(new CameraEvaluator(Session.Chart).EvaluateTilt(pulse).Input, 6);
            if (CameraPathCommands.Add(Session.Chart, CamPathLane.Tilt, pulse, manual, null, null) is not { } added) return;
            Session.Commands.Execute(added.Command);
        }

        showToast($"Tilt point at {TimingControlPointsDialog.FormatTime(Playback.CurrentTime.Value)} (edit it in the Camera tab)");
    }

    /// <summary>Every camera graph back to 0 at the cursor (one undo step).</summary>
    private void resetCameraHere()
    {
        long pulse = cursorPulse();
        // one anchor per graph (lanes are independent, so the adds compose into one step)
        var cmds = CameraGraphs.All
                               .Select(g => CameraPathCommands.Add(Session.Chart, (CamPathLane)(int)g, pulse, 0, null, null))
                               .Where(r => r != null)
                               .Select(r => (IEditCommand)r!.Value.Command)
                               .ToList();

        if (cmds.Count == 0)
        {
            showToast("Every camera graph already has a point here");
            return;
        }

        Session.Commands.Execute(new CompositeCommand("Reset camera", cmds));
        showToast($"Camera reset to 0 at {TimingControlPointsDialog.FormatTime(Playback.CurrentTime.Value)} ({cmds.Count} graph(s))");
    }

    // ---- timing ------------------------------------------------------------

    private long snapTickPulses() => Math.Max(1, Timebase.PulsesPerQuarter / Math.Max(1, divisorControl.Divisor.Value));

    private void moveAllNotes(long delta, string what)
    {
        var all = new Selection();
        all.SelectRange(Session.Chart, 0, long.MaxValue);
        if (all.IsEmpty)
        {
            showToast("The chart has no notes to move");
            return;
        }

        if (SelectionOps.Move(Session.Chart, all, delta) is not { } cmd)
        {
            showToast($"Cannot move every note {what}: a note would leave the chart");
            return;
        }

        Session.Selection.Clear();
        Session.Commands.Execute(cmd);
        showToast($"Moved {all.Count} object(s) {what} (Ctrl+Z restores them)");
    }

    // ---- files -------------------------------------------------------------

    private string chartLabel => $"{Session.Chart.Meta.DifficultyDisplayName} {Difficulty.FormatLevel(Session.Chart.Meta.Level)}";

    /// <summary>
    /// Runs <paramref name="proceed"/> now when the chart is saved, else asks Save / Discard / Cancel first. Discard with
    /// <paramref name="revertOnDiscard"/> re-reads the chart from disk before proceeding.
    /// </summary>
    private void confirmUnsaved(string beforeWhat, Action proceed, bool revertOnDiscard = false)
    {
        if (!Session.IsDirty)
        {
            proceed();
            return;
        }

        dialogs.Push(new ChoiceDialog("Unsaved changes", new[]
        {
            $"{chartLabel} has unsaved changes.",
            $"Save them before {beforeWhat}?",
        }, new[]
        {
            new DialogChoice("Save", () =>
            {
                TriggerAction(EditorAction.Save);
                if (!Session.IsDirty) proceed();
            }, IsDefault: true),
            new DialogChoice("Discard", () =>
            {
                if (revertOnDiscard && Session.ChartPath != null)
                {
                    Session.RevertToSaved();
                    onChartReplaced();
                }

                proceed();
            }),
            new DialogChoice("Cancel", null),
        }));
    }

    /// <summary>File → Exit and Esc (once nothing else is open): asks about unsaved changes first.</summary>
    public void ExitWithPrompt() => confirmUnsaved("leaving the editor", () => this.Exit());

    /// <summary>File → Revert to Saved: confirms when there is something to lose.</summary>
    public void RevertWithPrompt()
    {
        if (!Session.IsDirty || Session.ChartPath == null)
        {
            TriggerAction(EditorAction.RevertToSaved);
            return;
        }

        dialogs.Push(new ChoiceDialog("Revert to Saved", new[]
        {
            $"Revert {chartLabel} to the saved file?",
            "Every unsaved change and the undo history are lost.",
        }, new[]
        {
            new DialogChoice("Revert", () => TriggerAction(EditorAction.RevertToSaved), IsDefault: true),
            new DialogChoice("Cancel", null),
        }));
    }

    /// <summary>File → Open Difficulty… (Ctrl+O): the song's charts as buttons; switching asks about unsaved changes.</summary>
    public bool OpenDifficultyChooser()
    {
        if (Session.Song == null)
        {
            showToast("Scratch chart: open a song folder first");
            return true;
        }

        if (Session.Charts.Count < 2)
        {
            showToast("This song has no other difficulty (File → Create a New Difficulty adds one)");
            return true;
        }

        var choices = Session.Charts.Select(c =>
        {
            var m = c.Chart.Meta;
            bool current = c == Session.ActiveChart;
            string label = $"{m.DifficultyDisplayName} {Difficulty.FormatLevel(m.Level)}  ({Path.GetFileName(c.FilePath)}){(current ? "  - open" : "")}";
            string file = c.FilePath;
            return new DialogChoice(label, current
                ? () => showToast($"{m.DifficultyDisplayName} is already open")
                : () => confirmUnsaved("switching the difficulty", () => switchToChart(file)));
        }).Append(new DialogChoice("Cancel", null)).ToList();

        dialogs.Push(new ChoiceDialog("Open Difficulty", new[] { $"{Session.Song.Title}", "Which difficulty do you want to edit?" }, choices, vertical: true));
        return true;
    }

    private void switchToChart(string filePath)
    {
        // fresh from disk: a discarded edit of the old chart must not come back when it is opened again
        Session.ReloadSong();
        var target = Session.Charts.FirstOrDefault(c => string.Equals(Path.GetFullPath(c.FilePath), Path.GetFullPath(filePath), StringComparison.OrdinalIgnoreCase));
        if (target == null)
        {
            showToast($"{Path.GetFileName(filePath)} is gone from the song folder");
            return;
        }

        Session.SwitchChart(target);
        onChartReplaced();
        showToast($"Opened {target.Chart.Meta.DifficultyDisplayName} ({Path.GetFileName(target.FilePath)})");
    }

    /// <summary>
    /// File → Create a New Difficulty (osu!): unsaved changes first, then "copy of the current difficulty" or "empty
    /// chart", then a free slot; the chart is written into the (forked if needed) song folder and Song Setup opens on it.
    /// </summary>
    public bool StartNewDifficulty()
    {
        if (Session.Song == null)
        {
            showToast("Scratch chart: open a song folder first");
            return true;
        }

        if (NewDifficulty.FreeSlots(Session.Charts.Select(c => c.Slot)).Count == 0)
        {
            showToast("All five difficulty slots (NOV, ADV, EXH, INF, MXM) are in use");
            return true;
        }

        confirmUnsaved("creating a new difficulty", () => dialogs.Push(new ChoiceDialog("Create a New Difficulty", new[]
        {
            "How should the new difficulty start?",
            "A copy keeps every note. An empty chart keeps timing, metadata, audio and bookmarks.",
        }, new[]
        {
            new DialogChoice($"Copy of the current difficulty ({chartLabel})", () => chooseNewSlot(true), IsDefault: true),
            new DialogChoice("Empty chart (map from zero)", () => chooseNewSlot(false)),
            new DialogChoice("Cancel", null),
        }, vertical: true, width: 520)), revertOnDiscard: true);

        return true;
    }

    private void chooseNewSlot(bool copy)
    {
        var used = Session.Charts.Select(c => c.Slot).ToList();
        var free = NewDifficulty.FreeSlots(used);
        var choices = free.Select(s => new DialogChoice(Difficulty.ShortName(s), () => createDifficulty(copy, s), IsDefault: s == free[0]))
                          .Append(new DialogChoice("Cancel", null)).ToList();

        dialogs.Push(new ChoiceDialog("New Difficulty: Slot", new[]
        {
            "Which slot should the new chart use?",
            $"Already used: {string.Join(", ", used.Distinct().OrderBy(s => s).Select(Difficulty.ShortName))}",
        }, choices, width: Math.Max(470, choices.Count * 112 + 20)));
    }

    private void createDifficulty(bool copy, int slot)
    {
        try
        {
            var chart = NewDifficulty.Create(Session.Chart, slot, copy);
            var added = Session.AddDifficulty(chart, slot);
            onChartReplaced();
            // a copy keeps the source's level (the notes are the same) - say so, it read like a bug (audit P9)
            showToast(copy
                ? $"New {Difficulty.ShortName(slot)} (copy) saved as {Path.GetFileName(added.FilePath)} - level {Difficulty.FormatLevel(chart.Meta.Level)} copied, set its own in Song Setup"
                : $"New {Difficulty.ShortName(slot)} (empty) saved as {Path.GetFileName(added.FilePath)} - set its level in Song Setup");
            OpenSongSetup();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.Log($"new difficulty failed: {e}");
            showToast("Could not create the difficulty: " + e.Message);
        }
    }

    /// <summary>File → Open Song Folder: the folder in the system file manager (a smoke run only records it).</summary>
    public void OpenSongFolder()
    {
        string? folder = Session.Song?.FolderPath;
        if (folder == null || !Directory.Exists(folder))
        {
            showToast("Scratch chart: this chart has no song folder");
            return;
        }

        LastOpenedFolder = folder;
        if (!startupOptions.SmokeTest)
            host.OpenFileExternally(folder);
        showToast($"Opened {folder}");
    }

    /// <summary>File → Export Song: the song folder as it is on disk (save first to include unsaved edits), like song select's export.</summary>
    public async Task<string?> ExportSong(SongExportFormat format)
    {
        if (Session.Song == null)
        {
            showToast("Scratch chart: open a song folder to export");
            return null;
        }

        if (library == null)
        {
            showToast("Exporting needs the song library");
            return null;
        }

        string what = format switch
        {
            SongExportFormat.Kson => ".kson",
            SongExportFormat.Ksh => ".ksh",
            SongExportFormat.Vox => "VOX",
            SongExportFormat.Svz => ".svz",
            _ => ".zip",
        };

        string folder = Session.Song.FolderPath;
        ExportsRunning++;
        string checkNote = exportCheckNote(); // EditorScreen.Features.cs: non-blocking, the export runs regardless
        showToast((Session.IsDirty ? $"Exporting as {what}… (unsaved changes are not included: save first)" : $"Exporting as {what}…") + checkNote);

        try
        {
            var song = library.Library.Find(folder) ?? new LibrarySong { FolderPath = folder };
            string path = await library.ExportAsync(song, format, p => Schedule(() => showToast($"Exporting as {what}: {p}")));
            Schedule(() =>
            {
                LastExportPath = path;
                showToast($"Exported to {path}{checkNote}");
            });
            return path;
        }
        catch (Exception e)
        {
            Logger.Error(e, $"export of {folder} as {what} failed");
            Schedule(() => showToast($"Export as {what} failed: {e.GetBaseException().Message}"));
            return null;
        }
        finally
        {
            Schedule(() => ExportsRunning--);
        }
    }
}
