using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// The tutorial's instruction card: chapter/step header, title, text, live progress, the "how to get back" box and
/// the buttons (Back, Next or Skip step, Exit). It takes the clicks that land on it; everything around it stays usable.
/// </summary>
public partial class TutorialPanel : CompositeDrawable
{
    public const float WIDTH = 470;

    public static readonly Colour4 Accent = Colour4.FromHex("ffd84d");
    public static readonly Colour4 Good = Colour4.FromHex("7ee07e");
    public static readonly Colour4 Warn = Colour4.FromHex("ff9f43");

    private readonly TutorialManager manager;

    private SpriteText header = null!;
    private SpriteText title = null!;
    private FillFlowContainer body = null!;
    private FillFlowContainer tasks = null!;
    private SpriteText progress = null!;
    private Container offTrackBox = null!;
    private TextFlowContainer offTrackText = null!;
    private FillFlowContainer buttons = null!;
    private FillFlowContainer extraButtons = null!;
    private Container solved = null!;

    /// <summary>The buttons by label as shown right now (smoke test: "Next", "Skip step", "Back", "Exit", "Take me back" …).</summary>
    public IReadOnlyDictionary<string, TutorialButton> Buttons => buttonMap;

    private readonly Dictionary<string, TutorialButton> buttonMap = new Dictionary<string, TutorialButton>();
    private TutorialButton takeMeBack = null!;
    private string? shownOffTrack;

    public TutorialPanel(TutorialManager manager)
    {
        this.manager = manager;
        Width = WIDTH;
        AutoSizeAxes = Axes.Y;
        Masking = true;
        CornerRadius = 10;
        BorderThickness = 2;
        BorderColour = Accent.Opacity(0.8f);
        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Colour = Colour4.Black.Opacity(0.5f),
            Radius = 18,
            Offset = new Vector2(0, 4),
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("1c1c26").Opacity(0.97f) },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Horizontal = 18, Top = 12, Bottom = 14 },
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            header = new SpriteText { Font = EditorFont.Bold(13), Colour = Accent, Spacing = new Vector2(1, 0) },
                            solved = new Container
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                AutoSizeAxes = Axes.Both,
                                Alpha = 0,
                                Child = new FillFlowContainer
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(5, 0),
                                    Children = new Drawable[]
                                    {
                                        new SpriteIcon { Icon = FontAwesome.Solid.CheckCircle, Size = new Vector2(15), Colour = Good },
                                        new SpriteText { Text = "Done!", Font = EditorFont.Bold(14), Colour = Good },
                                    },
                                },
                            },
                        },
                    },
                    title = new SpriteText { Font = EditorFont.Bold(23), Colour = Colour4.White, Truncate = true, RelativeSizeAxes = Axes.X },
                    body = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 3),
                    },
                    tasks = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 5),
                    },
                    progress = new SpriteText { Font = EditorFont.Bold(16), Colour = Good, RelativeSizeAxes = Axes.X, Truncate = true },
                    offTrackBox = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Masking = true,
                        CornerRadius = 6,
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = Warn.Opacity(0.18f) },
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Padding = new MarginPadding(10),
                                Spacing = new Vector2(0, 8),
                                Children = new Drawable[]
                                {
                                    offTrackText = new TextFlowContainer(t =>
                                    {
                                        t.Font = EditorFont.Regular(15.5f);
                                        t.Colour = Colour4.FromHex("ffd9b0");
                                    }) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
                                    takeMeBack = new TutorialButton("Take me back", () => manager.TakeMeBack(), Warn),
                                },
                            },
                        },
                    },
                    extraButtons = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Full,
                        Spacing = new Vector2(8, 8),
                    },
                    buttons = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Margin = new MarginPadding { Top = 4 },
                    },
                },
            },
        };
    }

    private int shownVersion = -1;

    /// <summary>Rebuilds the static parts (call when the manager's state changed).</summary>
    public void Refresh(int version)
    {
        if (version == shownVersion) return;
        shownVersion = version;

        buttonMap.Clear();
        buttons.Clear();
        extraButtons.Clear();
        tasks.Clear();
        buttonMap["Take me back"] = takeMeBack;

        if (manager.Hint is { } hint && !manager.IsRunning)
        {
            header.Text = "WELCOME";
            title.Text = hint.Title;
            setBody(hint.Text);
            add(buttons, "Open tutorial", () => manager.ShowChapterList(), Accent, primary: true);
            add(buttons, "Not now", () => manager.HideHint(), Colour4.FromHex("555566"));
            return;
        }

        var chapter = manager.Chapter;
        if (chapter == null) return;

        if (manager.ChapterComplete)
        {
            header.Text = $"CHAPTER {chapter.Number} · COMPLETE";
            title.Text = $"{chapter.Title}: done!";
            var course = manager.Course;
            bool last = chapter.Number + 1 >= course.Count;
            setBody(last
                ? $"That was the whole tutorial. Your tutorial song stays in your editor songs as \"{course.FolderName}\" — open it any time, or delete it in song select. "
                  + "Starting a chapter again rewrites that one folder to the chapter's start.\n\nHelp → Tutorial… brings the chapter list back."
                : $"Well done! Next up: {course.Get(chapter.Number + 1).Title}.\n\nYou can stop here and come back later: Help → Tutorial… (or the Tutorial button in song select) lists every chapter.");
            if (!last) add(buttons, "Next chapter", () => manager.StartNextChapter(), Accent, primary: true);
            add(buttons, "Chapter list", () => manager.ShowChapterList(), Colour4.FromHex("4a6fa5"));
            add(buttons, "Close", () => manager.Exit(), Colour4.FromHex("555566"));
            return;
        }

        var step = manager.Step;
        if (step == null) return;

        header.Text = $"CHAPTER {chapter.Number} · {chapter.Title.ToUpperInvariant()} · STEP {manager.StepIndex + 1} OF {manager.Steps.Count}";
        title.Text = step.Title;
        setBody(step.Text);
        for (int i = 0; i < step.Tasks.Count; i++) tasks.Add(new TaskRow(i + 1, step.Tasks[i].Text, i < manager.TaskIndex ? TaskState.Done : i == manager.TaskIndex ? TaskState.Current : TaskState.Later));

        foreach (var (label, action) in step.Buttons)
        {
            var ctx = manager.Context;
            add(extraButtons, label, () =>
            {
                if (ctx != null) action(ctx);
            }, Colour4.FromHex("4a6fa5"));
        }

        if (manager.StepIndex > 0) add(buttons, "Back", () => manager.Back(), Colour4.FromHex("555566"));
        if (step.IsInformation || manager.ReviewMode) add(buttons, "Next", () => manager.Next(), Accent, primary: true);
        else add(buttons, "Skip step", () => manager.Skip(), Colour4.FromHex("6a5a3a"));
        add(buttons, "Exit tutorial", () => manager.Exit(), Colour4.FromHex("703a4a"));
    }

    private void add(FillFlowContainer flow, string label, Action action, Colour4 colour, bool primary = false)
    {
        var b = new TutorialButton(label, action, colour, primary);
        buttonMap[label] = b;
        flow.Add(b);
    }

    /// <summary>Replaces <c>{key:Action}</c> with the binding in effect (plain text, no markup).</summary>
    public static string ExpandKeys(string text) => TutorialRichText.Plain(text);

    private void setBody(string text)
    {
        body.Clear();
        TutorialRichText.Render(body, text, 16.5f);
    }

    protected override void Update()
    {
        base.Update();

        string? off = manager.OffTrack;
        offTrackBox.Alpha = off != null && manager.Step != null ? 1 : 0;
        if (off != null && shownOffTrack != off)
        {
            shownOffTrack = off;
            TutorialRichText.SetLine(offTrackText, off, 15.5f, Colour4.FromHex("ffd9b0"));
        }

        string p = manager.StepSolved ? "" : ExpandKeys(manager.ProgressText ?? "");
        if (progress.Text.ToString() != p) progress.Text = p;
        progress.Alpha = p.Length > 0 ? 1 : 0;
        solved.Alpha = manager.StepSolved ? 1 : 0;
    }

    // the card takes the clicks that land on it (nothing behind it reacts)
    protected override bool OnMouseDown(MouseDownEvent e) => true;
    protected override bool OnClick(ClickEvent e) => true;
    protected override bool OnScroll(ScrollEvent e) => true;
    protected override bool OnHover(HoverEvent e) => true;
}

/// <summary>A flat rounded button of the tutorial panel.</summary>
public partial class TutorialButton : ClickableContainer
{
    public string Label { get; }

    private readonly Colour4 colour;
    private Box hover = null!;

    public TutorialButton(string label, Action action, Colour4 colour, bool primary = false)
    {
        Label = label;
        Action = action;
        this.colour = colour;
        AutoSizeAxes = Axes.X;
        Height = 32;
        Masking = true;
        CornerRadius = 6;
        Primary = primary;
    }

    public bool Primary { get; }

    [BackgroundDependencyLoader]
    private void load()
    {
        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
            hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 },
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Margin = new MarginPadding { Horizontal = 14 },
                Text = Label,
                Font = EditorFont.Bold(15),
                Colour = Primary ? Colour4.FromHex("1c1c10") : Colour4.White,
            },
        };
    }

    protected override bool OnHover(HoverEvent e)
    {
        hover.FadeTo(0.15f, 80);
        return true;
    }

    protected override void OnHoverLost(HoverLostEvent e) => hover.FadeOut(120);

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        hover.FadeTo(0.3f, 40);
        return true;
    }
}

public enum TaskState
{
    Done,
    Current,
    Later,
}

/// <summary>One numbered task line of the card: ✓ when done, highlighted while current, dimmed after.</summary>
public partial class TaskRow : CompositeDrawable
{
    public TaskRow(int number, string text, TaskState state)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        var colour = state switch
        {
            TaskState.Done => TutorialPanel.Good,
            TaskState.Current => TutorialPanel.Accent,
            _ => Colour4.FromHex("777788"),
        };

        var flow = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Padding = new MarginPadding { Left = 34, Right = 8, Vertical = 5 },
        };
        TutorialRichText.Render(flow, text, 15.5f, state == TaskState.Later ? Colour4.FromHex("9a9aaa") : state == TaskState.Done ? Colour4.FromHex("b8d8b8") : TutorialRichText.TextColour);

        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = state == TaskState.Current ? TutorialPanel.Accent.Opacity(0.13f) : Colour4.White.Opacity(0.03f) },
            },
            new Container
            {
                Size = new Vector2(22),
                Position = new Vector2(6, 5),
                Masking = true,
                CornerRadius = 11,
                BorderThickness = 2,
                BorderColour = colour,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = state == TaskState.Done ? colour : Colour4.Transparent, AlwaysPresent = true },
                    state == TaskState.Done
                        ? new SpriteIcon { Icon = FontAwesome.Solid.Check, Size = new Vector2(11), Anchor = Anchor.Centre, Origin = Anchor.Centre, Colour = Colour4.FromHex("1c1c26") }
                        : new SpriteText { Text = number.ToString(System.Globalization.CultureInfo.InvariantCulture), Font = EditorFont.Bold(13), Anchor = Anchor.Centre, Origin = Anchor.Centre, Colour = colour },
                },
            },
            flow,
        };
    }
}
