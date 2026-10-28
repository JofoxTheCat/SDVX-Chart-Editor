using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// Help → Tutorial… / song select "Tutorial": a tab per tutorial (the new one first, the classic one while it is
/// kept), then every chapter of the picked one with a tick when it was completed (editor.ini, one key per tutorial),
/// Start (continues where the tutorial song is, if it holds that chapter) and Reset (writes the chapter's starting
/// state again).
/// </summary>
public partial class TutorialChapterListDialog : DialogWindow
{
    private const float row_height = 50;
    private const float width = 600;
    private const float tabs_height = 40;

    private readonly TutorialManager manager;

    /// <summary>The tutorial whose chapters are listed.</summary>
    public TutorialCourse Course { get; }

    /// <summary>One tab per tutorial, by id (smoke test clicks them).</summary>
    public IReadOnlyDictionary<string, DialogButton> CourseTabs => courseTabs;

    private readonly Dictionary<string, DialogButton> courseTabs = new Dictionary<string, DialogButton>();

    /// <summary>Start / Reset buttons per chapter (smoke test clicks them).</summary>
    public IReadOnlyList<DialogButton> StartButtons => startButtons;

    public IReadOnlyList<DialogButton> ResetButtons => resetButtons;

    public IReadOnlyList<SpriteIcon> Ticks => ticks;

    private readonly List<DialogButton> startButtons = new List<DialogButton>();
    private readonly List<DialogButton> resetButtons = new List<DialogButton>();
    private readonly List<SpriteIcon> ticks = new List<SpriteIcon>();

    public TutorialChapterListDialog(TutorialManager manager, TutorialCourse course)
        : base("Tutorial", new Vector2(width, tabs_height + 70 + course.Count * row_height + 50))
    {
        this.manager = manager;
        Course = course;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var done = manager.CompletedOf(Course);
        double total = Course.Chapters.Sum(c => c.Minutes);

        var tabs = new FillFlowContainer
        {
            Position = new Vector2(10, 8),
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
        };
        foreach (var course in TutorialCourses.All)
        {
            var c = course;
            var tab = new DialogButton(c.Name, () => manager.ShowChapterList(c))
            {
                IsDefault = c == Course,
                Size = new Vector2(c.Name.Length * 7.2f + 30, 28),
            };
            courseTabs[c.Id] = tab;
            tabs.Add(tab);
        }

        var rows = new FillFlowContainer
        {
            Position = new Vector2(10, tabs_height + 58),
            Width = width - 20,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
        };

        foreach (var chapter in Course.Chapters)
        {
            int n = chapter.Number;
            bool complete = done.Contains(n);
            SpriteIcon tick;
            DialogButton start, reset;

            rows.Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = row_height,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.X, Height = 1, Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Colour = Colour4.FromHex("d8d8d8") },
                    tick = new SpriteIcon
                    {
                        Position = new Vector2(4, 15),
                        Size = new Vector2(18),
                        Icon = complete ? FontAwesome.Solid.CheckCircle : FontAwesome.Regular.Circle,
                        Colour = complete ? Colour4.FromHex("2e9e44") : Colour4.FromHex("a0a0a0"),
                    },
                    DialogText.Label($"Chapter {n}: {chapter.Title}", bold: true, size: 14).With(t => t.Position = new Vector2(32, 7)),
                    DialogText.Label($"~{chapter.Minutes:0.##} min · {chapter.Summary}", size: 12).With(t =>
                    {
                        t.Position = new Vector2(32, 27);
                        t.Colour = Colour4.FromHex("555555");
                        t.Width = width - 20 - 32 - 170; // clear of the buttons
                        t.Truncate = true;
                    }),
                    start = new DialogButton(complete ? "Again" : "Start", () => manager.StartChapterLater(Course, n, reset: complete)) // again = from its starting state
                    {
                        IsDefault = !complete && n == firstOpen(done),
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Position = new Vector2(-82, 0),
                        Size = new Vector2(70, 26),
                    },
                    reset = new DialogButton("Reset", () => manager.StartChapterLater(Course, n, true))
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Position = new Vector2(-4, 0),
                        Size = new Vector2(70, 26),
                    },
                },
            });
            ticks.Add(tick);
            startButtons.Add(start);
            resetButtons.Add(reset);
        }

        Body.Children = new Drawable[]
        {
            tabs,
            DialogText.Label(Course.Description, bold: true, size: 14).With(t =>
            {
                t.Position = new Vector2(12, tabs_height + 10);
                t.Width = width - 24;
                t.Truncate = true;
            }),
            DialogText.Label(Course.Temporary
                    ? $"Kept for now next to the new tutorial (~{total:0} min). Song folder \"{Course.FolderName}\"."
                    : $"Every chapter can be run on its own (~{total:0} min in all). One song folder: \"{Course.FolderName}\".", size: 12)
                      .With(t => t.Position = new Vector2(12, tabs_height + 32)),
            rows,
            new DialogButton("Close", Cancel)
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-12, -10),
                Size = new Vector2(100, 28),
            },
            DialogText.Label($"{done.Count} of {Course.Count} chapters completed", size: 12).With(t =>
            {
                t.Anchor = Anchor.BottomLeft;
                t.Origin = Anchor.BottomLeft;
                t.Position = new Vector2(14, -16);
            }),
        };
    }

    private int firstOpen(IReadOnlySet<int> done) => Enumerable.Range(0, Course.Count).FirstOrDefault(i => !done.Contains(i));

    /// <summary>Enter starts the first chapter not completed yet.</summary>
    protected override void OnAccept()
    {
        int n = firstOpen(manager.CompletedOf(Course));
        manager.StartChapterLater(Course, n);
    }
}
