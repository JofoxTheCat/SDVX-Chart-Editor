using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Core.Validation;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit.Components;

namespace SDVXEditor.Game.Screens.Edit.Panels;

/// <summary>
/// Chart check (Ctrl+Shift+A, osu!'s AIMod): every issue of Core <see cref="ChartChecker"/> as a row — severity colour,
/// position, message. A click jumps there and selects the objects. Non-modal: the list re-runs (debounced) after every
/// edit while the panel is open, so fixing an issue removes its row.
/// </summary>
public partial class ChartCheckPanel : SidePanel
{
    public const float LIST_HEIGHT = 360;

    private readonly Func<ChartCheckReport> run;
    private readonly Action<ChartIssue> jump;

    /// <summary>Show "info" rows (format notes); warnings and errors always show.</summary>
    public readonly BindableBool ShowInfo = new BindableBool();

    private SpriteText summary = null!;
    private FillFlowContainer rows = null!;

    /// <summary>The report the list shows (smoke test).</summary>
    public ChartCheckReport? Report { get; private set; }

    /// <summary>The rows on screen, in list order (smoke test clicks them).</summary>
    public IReadOnlyList<Drawable> Rows => rows.Children;

    /// <summary>The issues behind <see cref="Rows"/>.</summary>
    public IReadOnlyList<ChartIssue> RowIssues { get; private set; } = Array.Empty<ChartIssue>();

    public ChartCheckPanel(Func<ChartCheckReport> run, Action<ChartIssue> jump)
        : base("Chart check")
    {
        this.run = run;
        this.jump = jump;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Content.AddRange(new Drawable[]
        {
            summary = new SpriteText { Font = EditorFont.Regular(17), Colour = EditorColours.Text },
            PanelControls.Row(
                PanelControls.Button("Refresh", Refresh, 80),
                new PinkCheckbox("show notes (formats)", 16).With(c => c.Current.BindTo(ShowInfo))),
            // the help sits by the list it explains, not at the panel's bottom (audit U22)
            PanelControls.Note("Click a row to jump there and select the objects. The list updates after every edit; saving and exporting never wait for it.", 14),
            new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = LIST_HEIGHT,
                Masking = true,
                Child = new BasicScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarOverlapsContent = false,
                    Child = rows = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                    },
                },
            },
        });
        ShowInfo.BindValueChanged(_ => fill());
        if (Shown.Value) Refresh();
    }

    protected override void OnShown()
    {
        if (IsLoaded) Refresh();
    }

    /// <summary>Runs the check again and rebuilds the list.</summary>
    public void Refresh()
    {
        Report = run();
        fill();
    }

    private void fill()
    {
        if (Report == null || rows == null) return;
        summary.Text = Report.IsClean ? "No problems found." : char.ToUpperInvariant(Report.Summary[0]) + Report.Summary[1..];
        RowIssues = Report.Issues.Where(i => ShowInfo.Value || i.Severity != IssueSeverity.Info).ToList();
        rows.Clear();
        foreach (var issue in RowIssues)
            rows.Add(new IssueRow(issue, () => jump(issue)));
        if (RowIssues.Count == 0)
            rows.Add(PanelControls.Note(Report.Infos > 0 ? $"Only notes ({Report.Infos}): tick \"show notes\" to see them." : "Nothing to fix."));
    }

    public static Colour4 ColourOf(IssueSeverity s) => s switch
    {
        IssueSeverity.Error => Colour4.FromHex("ff4d4d"),
        IssueSeverity.Warning => Colour4.FromHex("ffc23d"),
        _ => Colour4.FromHex("5aa9ff"),
    };

    private partial class IssueRow : ClickableContainer
    {
        private readonly Box hover;

        public IssueRow(ChartIssue issue, Action action)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Action = action;
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White.Opacity(0.04f) },
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 },
                new Box { RelativeSizeAxes = Axes.Y, Width = 4, Colour = ColourOf(issue.Severity) },
                new TextFlowContainer(t =>
                {
                    t.Font = EditorFont.Regular(15); // was 13 (~8 px on screen, audit U22)
                    t.Colour = Colour4.White;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Left = 10, Right = 4, Vertical = 3 },
                    Text = $"[{issue.Category}] {issue.Message}",
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeTo(0.1f, 60);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(60);
            base.OnHoverLost(e);
        }
    }
}
