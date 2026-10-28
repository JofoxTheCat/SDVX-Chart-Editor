using osu.Framework.Graphics;
using SDVXEditor.Core.Tutorial;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// One step of a chapter: friendly text, an optional spotlight target, and how the step ends — a condition that is
/// checked every frame (the step advances by itself once it holds) or, without one, the Next button.
/// </summary>
public sealed class TutorialStep
{
    public TutorialStep(string title, string text)
    {
        Title = title;
        Text = text;
    }

    public string Title { get; }

    /// <summary>
    /// Body text. <c>{key:Undo}</c> is replaced by the binding currently in effect for that <see cref="Screens.Edit.Input.EditorAction"/>
    /// (rebinding in the options changes the text too); a blank line starts a new paragraph.
    /// </summary>
    public string Text { get; }

    /// <summary>Spotlight target by anchor name (<see cref="TutorialAnchors"/>).</summary>
    public string? Anchor { get; init; }

    /// <summary>Spotlight target found in code (wins over <see cref="Anchor"/>).</summary>
    public Func<TutorialContext, Drawable?>? Target { get; init; }

    /// <summary>
    /// Spotlight target as a screen-space rectangle (a chart region, one camera lane column); wins over
    /// <see cref="Target"/> and <see cref="Anchor"/>. Null result = nothing to highlight right now.
    /// </summary>
    public Func<TutorialContext, osu.Framework.Graphics.Primitives.RectangleF?>? Area { get; init; }

    /// <summary>
    /// The step's actions one after another ("1. press X", "2. click Y"): the card lists them with a tick each, the
    /// spotlight follows the current one. Without <see cref="Done"/> the step is complete when every task is done.
    /// A task stays done once it was (the card never takes a tick back).
    /// </summary>
    public IReadOnlyList<TutorialTask> Tasks { get; init; } = Array.Empty<TutorialTask>();

    /// <summary>The step is complete (checked every frame). Null = an information step: Next continues.</summary>
    public Func<TutorialContext, bool>? Done { get; init; }

    /// <summary>A short live progress line ("2 / 4 chips placed", what a rule check still misses).</summary>
    public Func<TutorialContext, string?>? Progress { get; init; }

    /// <summary>
    /// Null when the user is where this step needs them; otherwise a hint how to get back ("Press F3 to return to the
    /// Timing tab"). Defaults to the chapter's own requirement.
    /// </summary>
    public Func<TutorialContext, string?>? Requirement { get; init; }

    /// <summary>What "Take me back" does when <see cref="Requirement"/> fails (default: the chapter's).</summary>
    public Action<TutorialContext>? Restore { get; init; }

    /// <summary>Runs once when the step starts (snapshots for "changed" conditions, preparing a stray object …).</summary>
    public Action<TutorialContext>? Enter { get; init; }

    /// <summary>Ghost objects to reproduce in the compose playfield.</summary>
    public Func<TutorialContext, IReadOnlyList<TutorialTarget>>? Ghosts { get; init; }

    /// <summary>Objects marked red (to delete).</summary>
    public Func<TutorialContext, IReadOnlyList<TutorialTarget>>? DeleteMarks { get; init; }

    /// <summary>
    /// A highlighted stretch of the chart: pulses [From, To) — a band over the compose playfield (free mapping, ghost
    /// targets) and over the camera lanes on the camera tab, with <see cref="ZoneLabel"/> written on it.
    /// </summary>
    public Func<TutorialContext, (long From, long To)>? Zone { get; init; }

    /// <summary>Text on the zone band ("bar 16, beats 1–3").</summary>
    public Func<TutorialContext, string?>? ZoneLabel { get; init; }

    /// <summary>Camera anchors to click (numbered rings in the camera lanes, green once the chart has them).</summary>
    public Func<TutorialContext, IReadOnlyList<CameraGhost>>? CameraGhosts { get; init; }

    /// <summary>Clicks outside the spotlight are blocked (only where a stray click would break the step).</summary>
    public bool BlockOutside { get; init; }

    /// <summary>Extra buttons on the panel ("Import it for me").</summary>
    public IReadOnlyList<(string Label, Action<TutorialContext> Action)> Buttons { get; init; } = Array.Empty<(string, Action<TutorialContext>)>();

    /// <summary>
    /// Smoke test: what a user would do to finish the step, as real input (<see cref="ITutorialDemo"/>). The tutorial
    /// itself never runs it.
    /// </summary>
    public Action<ITutorialDemo, TutorialContext>? Demo { get; init; }

    public bool IsInformation => Done == null && Tasks.Count == 0;

    /// <summary>Whether the step names something to highlight (the smoke test checks it is on screen and spotlighted).</summary>
    public bool HasTarget => Area != null || Target != null || Anchor != null || Tasks.Any(t => t.HasTarget);
}

/// <summary>
/// One action of a step (<see cref="TutorialStep.Tasks"/>): its own line on the card (rich text like the body), its own
/// spotlight target and its own condition. The task's target wins over the step's while it is the current one; a task
/// without a target leaves the step's spotlight in place.
/// </summary>
public sealed class TutorialTask
{
    public TutorialTask(string text, Func<TutorialContext, bool> done)
    {
        Text = text;
        Done = done;
    }

    public string Text { get; }
    public Func<TutorialContext, bool> Done { get; }

    public string? Anchor { get; init; }
    public Func<TutorialContext, Drawable?>? Target { get; init; }
    public Func<TutorialContext, osu.Framework.Graphics.Primitives.RectangleF?>? Area { get; init; }

    public bool HasTarget => Area != null || Target != null || Anchor != null;
}

/// <summary>
/// A camera anchor to place: lane, pulse, x across the lane (0..1); <see cref="Placed"/> says whether the chart has it
/// (the ring turns green). <see cref="Number"/> is drawn in the ring (click order).
/// </summary>
public sealed record CameraGhost(Screens.Edit.Camera.CamLane Lane, long Pulse, double Fx, int Number, Func<SDVXEditor.Core.Charts.Chart, bool> Placed);

/// <summary>A chapter: runnable on its own, it prepares the tutorial song in the state its first step needs.</summary>
public sealed class TutorialChapter
{
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Summary { get; init; } = "";

    /// <summary>Rough time for a beginner, minutes.</summary>
    public double Minutes { get; init; }

    /// <summary>False: the chapter starts on song select (chapter 0 creates the song itself).</summary>
    public bool StartsInEditor { get; init; } = true;

    /// <summary>Default requirement and restore for every step of the chapter.</summary>
    public Func<TutorialContext, string?>? Requirement { get; init; }

    public Action<TutorialContext>? Restore { get; init; }

    /// <summary>Builds the steps (fresh closures every time the chapter starts).</summary>
    public Func<TutorialContext, IReadOnlyList<TutorialStep>> Steps { get; init; } = _ => Array.Empty<TutorialStep>();
}

/// <summary>
/// Real-input actions of a smoke-test "user" (implemented by the smoke part with the smoke <c>ManualInputManager</c>
/// for the screen layer and the root input manager for dialogs and the tutorial panel). Actions run one after another.
/// </summary>
public interface ITutorialDemo
{
    /// <summary>Moves to the centre of the drawable and clicks it (the input path is picked by where it lives).</summary>
    void Click(Func<Drawable?> target, bool right = false);

    /// <summary>Clicks a screen-space point of the screen layer (playfield, lanes).</summary>
    void ClickAt(Func<osuTK.Vector2> screenSpace, bool right = false);

    /// <summary>Clicks a screen-space point only if <paramref name="condition"/> holds when its turn comes.</summary>
    void ClickAtIf(Func<bool> condition, Func<osuTK.Vector2> screenSpace);

    /// <summary>Drags between two screen-space points of the screen layer.</summary>
    void Drag(Func<osuTK.Vector2> from, Func<osuTK.Vector2> to);

    /// <summary>Presses at the first point, moves through the others, releases at the last (screen layer).</summary>
    void DragPath(params Func<osuTK.Vector2>[] points);

    /// <summary>Moves the mouse only (hover).</summary>
    void MoveTo(Func<osuTK.Vector2> screenSpace);

    /// <summary>Presses the key combination currently bound to an editor action (the user may have rebound it).</summary>
    void Press(Screens.Edit.Input.EditorAction action);

    /// <summary>A key press with modifiers, to the screen layer (editor, playtest).</summary>
    void Key(osuTK.Input.Key key, params osuTK.Input.Key[] modifiers);

    /// <summary>Presses (<paramref name="down"/>) or releases a key on the screen layer and leaves it so (a modifier held through a drag).</summary>
    void HoldKey(osuTK.Input.Key key, bool down);

    /// <summary>A key press to the screen layer, only if <paramref name="condition"/> holds when its turn comes.</summary>
    void KeyIf(Func<bool> condition, osuTK.Input.Key key, params osuTK.Input.Key[] modifiers);

    /// <summary>A key press to the root input manager (dialogs).</summary>
    void DialogKey(osuTK.Input.Key key, params osuTK.Input.Key[] modifiers);

    /// <summary>Mouse wheel at a screen-space point of the screen layer, with modifiers held.</summary>
    void Wheel(Func<osuTK.Vector2> at, float notches, params osuTK.Input.Key[] modifiers);

    /// <summary>What real input cannot do in an unattended run (typing into a text box: the framework's text input is a separate OS stream).</summary>
    void Do(Action action);

    /// <summary>Waits until <paramref name="condition"/> holds (async results: an import, an export, a screen push).</summary>
    void WaitFor(Func<bool> condition, string what);
}
