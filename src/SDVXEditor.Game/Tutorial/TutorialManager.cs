using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Screens;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Play;
using SDVXEditor.Game.Screens.SongSelect;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// Runs the tutorial: the chapter list, preparing a chapter's song, the current step and its checks (every frame),
/// progress in editor.ini. The <see cref="TutorialOverlay"/> draws what this says; nothing here blocks input.
/// </summary>
public partial class TutorialManager : Component
{
    private readonly EditorGame game;

    [Resolved]
    private EditorConfigManager config { get; set; } = null!;

    public TutorialManager(EditorGame game)
    {
        this.game = game;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // the per-chapter folders of the first version ("Tutorial (Chapter N)") and its staged audio go away once
        try
        {
            if (TutorialWorkspace.MigrateLegacy(game.Library.Library.EditorRoot) > 0)
                _ = game.Library.ScanAsync();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Logger.Error(e, "tutorial: could not remove the old per-chapter folders");
        }
    }

    // ---- state -------------------------------------------------------------------------

    public bool IsRunning { get; private set; }
    public TutorialChapter? Chapter { get; private set; }
    public IReadOnlyList<TutorialStep> Steps { get; private set; } = Array.Empty<TutorialStep>();
    public int StepIndex { get; private set; }
    public TutorialStep? Step => IsRunning && !ChapterComplete && StepIndex < Steps.Count ? Steps[StepIndex] : null;
    public TutorialContext? Context { get; private set; }

    /// <summary>The chapter's last step is done; the panel shows "Chapter complete".</summary>
    public bool ChapterComplete { get; private set; }

    /// <summary>The current step was entered with Back: it does not advance by itself (its condition may already hold).</summary>
    public bool ReviewMode { get; private set; }

    /// <summary>Hint shown while the user is not where the step needs them; null when on track.</summary>
    public string? OffTrack { get; private set; }

    public string? ProgressText { get; private set; }

    /// <summary>The first task of the step that is not done yet (= <c>Step.Tasks.Count</c> when all are).</summary>
    public int TaskIndex { get; private set; }

    /// <summary>The task the user is on now, or null (no tasks, all done, review mode).</summary>
    public TutorialTask? CurrentTask => Step is { } s && !ReviewMode && TaskIndex < s.Tasks.Count ? s.Tasks[TaskIndex] : null;

    /// <summary>The step's condition was met; it advances in a moment (the panel shows a tick meanwhile).</summary>
    public bool StepSolved => advanceAt != null;

    /// <summary>Raised on the update thread whenever chapter, step or completion changes.</summary>
    public event Action? StateChanged;

    /// <summary>Bumped on every state change (the panel rebuilds when it differs).</summary>
    public int Version { get; private set; }

    private void changed()
    {
        Version++;
        StateChanged?.Invoke();
    }

    /// <summary>Steps finished (by condition, Next or Skip) since start-up, and how many were skipped (smoke report).</summary>
    public int StepsAdvanced { get; private set; }

    public int StepsSkipped { get; private set; }

    /// <summary>A one-off panel without a chapter (the first-start hint).</summary>
    public (string Title, string Text, string? Anchor)? Hint { get; private set; }

    private double? advanceAt;
    private bool adopted;
    private double nextAdoptCheck;

    private readonly Dictionary<TutorialCourse, TutorialWorkspace> workspaces = new Dictionary<TutorialCourse, TutorialWorkspace>();

    /// <summary>The overlay drawing this manager's state (set by the game).</summary>
    public TutorialOverlay? Overlay { get; set; }

    /// <summary>The tutorial running, or the one last picked in the chapter list (the new one by default).</summary>
    public TutorialCourse Course { get; private set; } = TutorialCourses.Main;

    public TutorialWorkspace Workspace => WorkspaceFor(Course);

    public TutorialWorkspace WorkspaceFor(TutorialCourse course)
    {
        if (!workspaces.TryGetValue(course, out var w))
            workspaces[course] = w = new TutorialWorkspace(game.Library.Library.EditorRoot, course);
        return w;
    }

    // ---- progress (one editor.ini key per tutorial, so the ticks never mix) -------------------

    public IReadOnlySet<int> Completed => CompletedOf(Course);

    public IReadOnlySet<int> CompletedOf(TutorialCourse course) => parse(config.Get<string>(course.ProgressSetting));

    private static HashSet<int> parse(string text) =>
        text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToHashSet();

    private void markCompleted(TutorialCourse course, int chapter)
    {
        var set = parse(config.Get<string>(course.ProgressSetting));
        if (set.Add(chapter))
            config.SetValue(course.ProgressSetting, string.Join(",", set.OrderBy(n => n)));
    }

    public void ResetProgress(TutorialCourse course) => config.SetValue(course.ProgressSetting, string.Empty);

    // ---- entry points ------------------------------------------------------------------

    /// <summary>Help → Tutorial…, the song select button: the chapter list (with a tab per tutorial), of <paramref name="course"/> or the current one.</summary>
    public TutorialChapterListDialog ShowChapterList(TutorialCourse? course = null)
    {
        HideHint();
        course ??= Course;
        if (game.TutorialDialogs.Current is TutorialChapterListDialog open)
        {
            if (open.Course == course) return open;
            open.Cancel(); // switching tabs: the other tutorial's list replaces it
        }

        if (!IsRunning) Course = course;
        var dialog = new TutorialChapterListDialog(this, course);
        game.TutorialDialogs.Push(dialog);
        return dialog;
    }

    /// <summary>Starts a chapter on the next frame (from a dialog button: the dialog closes first).</summary>
    public void StartChapterLater(TutorialCourse course, int number, bool reset = false) => Schedule(() => StartChapter(course, number, reset));

    private void startChapterConfirmed(TutorialCourse course, int number, bool reset) => Schedule(() => StartChapter(course, number, reset, confirmed: true));

    /// <summary>Starts chapter <paramref name="number"/> of the current tutorial.</summary>
    public bool StartChapter(int number, bool reset = false) => StartChapter(Course, number, reset);

    /// <summary>
    /// Starts chapter <paramref name="number"/> of <paramref name="course"/> (on its own, from its starting state, in the
    /// tutorial's one song folder). Asks first when that would throw away the user's work: unsaved changes in the
    /// tutorial song, or saved changes / added difficulties in the folder — except when simply moving on to the next
    /// chapter (<paramref name="flow"/>, or the chapter after the one the folder holds), where replacing the previous
    /// chapter's result is the point.
    /// </summary>
    public bool StartChapter(TutorialCourse course, int number, bool reset = false, bool confirmed = false, bool flow = false)
    {
        var chapter = course.Get(number);

        if (game.TutorialScreens.CurrentScreen is EditorScreen { Session.IsDirty: true } busy && !isTutorialFolder(busy.Session.Song?.FolderPath))
        {
            game.TutorialDialogs.Push(new ChoiceDialog("Tutorial", new[]
            {
                "Your chart has unsaved changes.",
                "Save it (Ctrl+S) or close it first: the tutorial opens its own song.",
            }, new[] { new DialogChoice("OK", () => { }) }));
            return false;
        }

        var workspace = WorkspaceFor(course);
        string[]? warning = null;
        try
        {
            if (!confirmed) warning = lossWarning(course, workspace, number, reset, flow);
        }
        catch (IOException e)
        {
            Logger.Error(e, "tutorial: could not read the song folder");
        }

        if (warning != null)
        {
            game.TutorialDialogs.Push(new ChoiceDialog("Tutorial", warning, new[]
            {
                new DialogChoice("Replace", () => startChapterConfirmed(course, number, reset), IsDefault: true),
                new DialogChoice("Cancel", null),
            }));
            return false;
        }

        HideHint();
        stopRun();
        game.TutorialDialogs.CloseAll();
        ReturnToSongSelect();
        Course = course;

        string folder;
        try
        {
            folder = workspace.Prepare(number, reset);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            Logger.Error(e, "tutorial: could not prepare the chapter");
            game.TutorialDialogs.Push(new ChoiceDialog("Tutorial", new[] { "The tutorial song could not be prepared:", e.Message }, new[] { new DialogChoice("OK", () => { }) }));
            return false;
        }

        Chapter = chapter;
        Context = new TutorialContext(game, this, course, workspace.Song, number, folder);
        Steps = chapter.Steps(Context);
        IsRunning = true;
        ChapterComplete = false;

        _ = game.Library.RefreshAsync(folder);

        if (chapter.StartsInEditor)
            OpenTutorialSong();

        Logger.Log($"tutorial: {course.Id} chapter {number} \"{chapter.Title}\" started ({Steps.Count} steps, {(reset ? "reset" : "kept")} {folder})");
        enterStep(0, false);
        return true;
    }

    /// <summary>Opens the chapter's song in the editor (from wherever the user is).</summary>
    public void OpenTutorialSong()
    {
        if (Context == null) return;

        ReturnToSongSelect();

        try
        {
            var session = EditorSession.Load(Context.SongFolder, Difficulty.Exhaust);
            game.TutorialScreens.Push(new EditorScreen(session));
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or FormatException)
        {
            Logger.Error(e, "tutorial: could not open the tutorial song");
            OffTrack = "The tutorial song could not be opened: " + e.Message + " Use \"Reset chapter\" in the chapter list.";
        }
    }

    /// <summary>Leaves editors and playtests (closing their dialogs) until song select is on screen.</summary>
    public void ReturnToSongSelect()
    {
        game.TutorialDialogs.CloseAll();

        for (int guard = 0; guard < 8; guard++)
        {
            var screen = game.TutorialScreens.CurrentScreen;
            if (screen == null || screen is SongSelectScreen) return;

            if (screen is PlaytestScreen p) p.Exit();
            else screen.Exit();
        }
    }

    /// <summary>The lines of the "Replace?" question, or null when nothing of the user's would be lost.</summary>
    private string[]? lossWarning(TutorialCourse course, TutorialWorkspace workspace, int number, bool reset, bool flow)
    {
        var lines = new List<string>();
        bool unsaved = game.TutorialScreens.CurrentScreen is EditorScreen { Session.IsDirty: true } open
                       && string.Equals(open.Session.Song?.FolderPath?.TrimEnd('\\', '/'), workspace.Folder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        if (unsaved && !flow) lines.Add("The tutorial song has unsaved changes."); // "Next chapter": the finished chapter's result is superseded anyway

        var state = workspace.State();
        bool keeps = !reset && number > 0 && state.Marker?.Chapter == number; // continuing the same chapter: nothing is rewritten
        bool movingOn = flow || state.Marker?.Chapter == number - 1;
        if (state is { Exists: true, Ours: true, HasUserWork: true } && !keeps && !movingOn)
        {
            string from = state.Marker is { Chapter: >= 0 } m ? $"chapter {m.Chapter}" : "an earlier run";
            lines.Add($"The song folder \"{course.FolderName}\" holds your work from {from}.");
            if (state.ExtraCharts.Count > 0) lines.Add("Charts the tutorial did not write are deleted too: " + string.Join(", ", state.ExtraCharts) + ".");
        }

        if (lines.Count == 0) return null;
        lines.Insert(0, $"Start chapter {number} from its beginning?");
        lines.Add($"Chapter {number} replaces the charts with its starting chart. Other files (a jacket …) stay.");
        return lines.ToArray();
    }

    private bool isTutorialFolder(string? folder) =>
        folder != null && TutorialCourses.All.Any(c => string.Equals(Path.GetFileName(folder.TrimEnd('\\', '/')), c.FolderName, StringComparison.Ordinal));

    // ---- step navigation ---------------------------------------------------------------

    public void Next()
    {
        if (Step == null) return;
        advance();
    }

    public void Skip()
    {
        if (Step == null) return;
        StepsSkipped++;
        Logger.Log($"tutorial: step skipped: {Chapter?.Number}.{StepIndex + 1} {Step.Title}");
        advance();
    }

    public void Back()
    {
        if (!IsRunning) return;

        if (ChapterComplete)
        {
            ChapterComplete = false;
            enterStep(Steps.Count - 1, true);
            return;
        }

        if (StepIndex > 0) enterStep(StepIndex - 1, true);
    }

    /// <summary>Ends the tutorial (the user stays where they are; their tutorial song is kept).</summary>
    public void Exit()
    {
        HideHint();
        stopRun();
        changed();
    }

    /// <summary>"Take me back": puts the user where the step needs them.</summary>
    public void TakeMeBack()
    {
        if (Step == null || Context == null) return;

        var restore = Step.Restore ?? Chapter?.Restore ?? TutorialNeed.RestoreEditor();
        try
        {
            restore(Context);
        }
        catch (Exception e)
        {
            Logger.Error(e, "tutorial: restore failed");
        }
    }

    /// <summary>The chapter after this one, or null at the end.</summary>
    public void StartNextChapter()
    {
        if (Chapter == null) return;
        if (Chapter.Number + 1 < Course.Count) StartChapter(Course, Chapter.Number + 1, flow: true);
        else Exit();
    }

    private void stopRun()
    {
        whenEditorReady.Clear();
        adopted = false;
        IsRunning = false;
        Chapter = null;
        Steps = Array.Empty<TutorialStep>();
        Context = null;
        OffTrack = null;
        ProgressText = null;
        ChapterComplete = false;
        advanceAt = null;
    }

    private void advance()
    {
        advanceAt = null;
        StepsAdvanced++;

        if (StepIndex + 1 < Steps.Count)
        {
            enterStep(StepIndex + 1, false);
            return;
        }

        ChapterComplete = true;
        if (Chapter != null) markCompleted(Course, Chapter.Number);
        Logger.Log($"tutorial: {Course.Id} chapter {Chapter?.Number} complete");
        changed();
    }

    private void enterStep(int index, bool review)
    {
        StepIndex = index;
        ReviewMode = review;
        TaskIndex = review ? Steps[index].Tasks.Count : 0;
        advanceAt = null;
        OffTrack = null;
        ProgressText = null;

        if (Context != null && Steps[index].Enter is { } enter)
        {
            try
            {
                enter(Context);
            }
            catch (Exception e)
            {
                Logger.Error(e, $"tutorial: step {index + 1} enter failed");
            }
        }

        changed();
    }

    // ---- the per-frame check -----------------------------------------------------------

    private readonly List<Action> whenEditorReady = new List<Action>();
    private int editorReadyFrames;

    /// <summary>Runs <paramref name="action"/> once the tutorial song's editor is loaded and laid out (now, if it is).</summary>
    public void WhenEditorReady(Action action)
    {
        whenEditorReady.Add(action);
        runWhenEditorReady(false);
    }

    private void runWhenEditorReady(bool frame)
    {
        var e = Context?.Editor;
        bool ready = e != null && e.IsLoaded && e.IsCurrentScreen() && e.Playfield.DrawHeight > 1;
        if (!ready) editorReadyFrames = 0;
        else if (frame) editorReadyFrames++;
        if (editorReadyFrames < 3 || whenEditorReady.Count == 0) return;

        var actions = whenEditorReady.ToList();
        whenEditorReady.Clear();
        foreach (var a in actions)
        {
            try
            {
                a();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "tutorial: deferred editor action failed");
            }
        }
    }

    protected override void Update()
    {
        base.Update();
        runWhenEditorReady(true);

        if (Step is not { } step || Context is not { } ctx) return;

        // chapter 0: the drop / "Import it for me" created the folder; mark it as the tutorial's own
        if (ctx.Chapter == 0 && !adopted && Time.Current >= nextAdoptCheck)
        {
            nextAdoptCheck = Time.Current + 500;
            try
            {
                adopted = Workspace.AdoptIfNeeded(0) || File.Exists(Path.Combine(Workspace.Folder, TutorialWorkspace.MarkerFile));
            }
            catch (IOException)
            {
                // the import is still writing: next time
            }
        }

        if (advanceAt is double at)
        {
            if (Time.Current >= at) advance();
            return;
        }

        if (!ReviewMode && step.Tasks.Count > 0)
        {
            // tasks tick one after another; a ticked task stays ticked
            bool moved = false;
            while (TaskIndex < step.Tasks.Count && safe(() => step.Tasks[TaskIndex].Done(ctx)))
            {
                TaskIndex++;
                moved = true;
            }

            if (moved) changed();
        }

        var stepDone = step.Done ?? (step.Tasks.Count > 0 ? (Func<TutorialContext, bool>)(_ => TaskIndex >= step.Tasks.Count) : null);
        if (!ReviewMode && stepDone is { } done && safe(() => done(ctx)))
        {
            advanceAt = Time.Current + 650;
            OffTrack = null;
            changed();
            return;
        }

        var requirement = step.Requirement ?? Chapter?.Requirement;
        OffTrack = requirement == null ? null : safeText(() => requirement(ctx));
        ProgressText = step.Progress == null ? null : safeText(() => step.Progress(ctx));
    }

    private static bool safe(Func<bool> f)
    {
        try
        {
            return f();
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or ArgumentException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static string? safeText(Func<string?> f)
    {
        try
        {
            return f();
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or ArgumentException or KeyNotFoundException)
        {
            return null;
        }
    }

    // ---- first start ---------------------------------------------------------------------

    /// <summary>Shows a one-off hint panel (no chapter running).</summary>
    public void ShowHint(string title, string text, string? anchor)
    {
        if (IsRunning) return;
        Hint = (title, text, anchor);
        changed();
    }

    public void HideHint()
    {
        if (Hint == null) return;
        Hint = null;
        changed();
    }
}
