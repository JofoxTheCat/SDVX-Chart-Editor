using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osuTK;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// Compact controls of the camera / effects side panels: a labelled text field bound to a getter and a
/// commit action (refreshed from the chart unless it has focus), small flat buttons and section headers.
/// </summary>
public static class PanelControls
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Num(double v) => v.ToString("0.####", Inv);
    public static string Num(double? v) => v is { } d ? Num(d) : "";

    public static bool TryNum(string s, out double v) =>
        double.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Float, Inv, out v);

    public static FlatButton Button(string text, Action action, float width = 90, Colour4? colour = null, string? tip = null) => new FlatButton(text, colour ?? EditorColours.Osu.MenuGrey, null, 15)
    {
        Size = new Vector2(width, 26),
        Action = action,
        TooltipText = tip ?? string.Empty,
    };

    /// <summary>A smaller sub-heading inside a group (white, no rule), optionally with export badges.</summary>
    public static Drawable SubHeader(string text, string? badgeId = null)
    {
        var c = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 18,
            Child = new SpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Text = text, Font = EditorFont.Bold(14), Colour = Colour4.White },
        };
        if (badgeId != null) c.Add(new ExportBadge(badgeId, 10) { Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight });
        return c;
    }

    /// <summary>
    /// A collapsible block: a "▸ title" row that shows / hides <paramref name="content"/> (collapsed at first). The
    /// hidden content takes no space (flows skip non-present children).
    /// </summary>
    public static CollapsibleSection Collapsible(string title, Drawable content, bool open = false) => new CollapsibleSection(title, content, open);

    public static Drawable Header(string text, string? badgeId = null)
    {
        var c = new Container
        {
            RelativeSizeAxes = Axes.X,
            Height = 22,
            Children = new Drawable[]
            {
                new SpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Text = text, Font = EditorFont.Bold(16), Colour = EditorColours.Osu.Pink },
                new Box { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, RelativeSizeAxes = Axes.X, Height = 1, Colour = Colour4.White.Opacity(0.15f) },
            },
        };
        if (badgeId != null) c.Add(new ExportBadge(badgeId) { Anchor = Anchor.CentreRight, Origin = Anchor.CentreRight });
        return c;
    }

    /// <summary>Muted help text. Default 13.5 (was 12: ~8 px on a 1600 px window, audit U22).</summary>
    public static TextFlowContainer Note(string text, float size = 13.5f) => new TextFlowContainer(t =>
    {
        t.Font = EditorFont.Regular(size);
        t.Colour = EditorColours.TextMuted;
    })
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Text = text,
    };

    public static FillFlowContainer Row(params Drawable[] children) => new FillFlowContainer
    {
        RelativeSizeAxes = Axes.X,
        AutoSizeAxes = Axes.Y,
        Direction = FillDirection.Full,
        Spacing = new Vector2(6, 4),
        Children = children,
    };
}

/// <summary>
/// Label + text box. Enter / focus loss commits; <see cref="Refresh"/> re-reads the chart value. With
/// <c>get</c> null it is a plain input (a name to add, a path to import) that keeps what was typed.
/// </summary>
public partial class PanelField : CompositeDrawable, IHasTooltip
{
    /// <summary>Hover text of the label and box (empty = none).</summary>
    public LocalisableString TooltipText { get; set; }

    private readonly Func<string>? get;
    private readonly Action<string> commit;

    public OsuTextBox Box { get; }

    public PanelField(string label, Func<string>? get, Action<string> commit, float width = 160, float labelWidth = 70)
    {
        this.get = get;
        this.commit = commit;
        Size = new Vector2(width, 26);
        InternalChildren = new Drawable[]
        {
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = label,
                Font = EditorFont.Regular(14),
                Colour = EditorColours.Text,
                Truncate = true,
                Width = labelWidth - 4,
            },
            Box = new OsuTextBox
            {
                X = labelWidth,
                Width = width - labelWidth,
                Height = 26,
                FontSize = 16,
                CommitOnFocusLost = true,
                Text = get?.Invoke() ?? "",
            },
        };
        Box.OnCommit += (_, _) =>
        {
            commit(Box.Text);
            Refresh();
        };
    }

    /// <summary>Re-reads the value unless the user is typing in the field.</summary>
    public void Refresh()
    {
        if (Box.HasFocus || get == null) return;
        string v = get();
        if (Box.Text != v) Box.Text = v;
    }

    /// <summary>Sets the text and commits it (smoke test / scripted edits).</summary>
    public void Commit(string text)
    {
        Box.Text = text;
        commit(text);
        Refresh();
    }
}

/// <summary>A heading row with a chevron that shows / hides its content (<see cref="PanelControls.Collapsible"/>).</summary>
public partial class CollapsibleSection : FillFlowContainer
{
    private readonly Container body;
    private readonly SpriteIcon chevron;

    public bool Open { get; private set; }

    /// <summary>The clickable heading row (smoke test).</summary>
    public Drawable HeaderRow { get; }

    public CollapsibleSection(string title, Drawable content, bool open)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        Spacing = new Vector2(0, 6);
        Children = new[]
        {
            HeaderRow = new HeaderButton
            {
                RelativeSizeAxes = Axes.X,
                Height = 24,
                Masking = true,
                CornerRadius = 4,
                Action = Toggle,
                TooltipText = "Show / hide",
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White.Opacity(0.06f) },
                    chevron = new SpriteIcon { Anchor = Anchor.CentreLeft, Origin = Anchor.Centre, X = 14, Size = new Vector2(10), Icon = FontAwesome.Solid.ChevronRight, Colour = EditorColours.Osu.Pink },
                    new SpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, X = 26, Text = title, Font = EditorFont.Bold(14), Colour = EditorColours.Osu.Pink },
                },
            },
            body = new Container { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Child = content },
        };
        set(open);
    }

    public void Toggle() => set(!Open);

    private void set(bool open)
    {
        Open = open;
        body.Alpha = open ? 1 : 0;
        chevron.Icon = open ? FontAwesome.Solid.ChevronDown : FontAwesome.Solid.ChevronRight;
    }

    private partial class HeaderButton : ClickableContainer, IHasTooltip
    {
        private Box hover = null!;

        public LocalisableString TooltipText { get; set; }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Add(hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 });
        }

        protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
        {
            hover.FadeTo(0.08f, 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
        {
            hover.FadeOut(80);
            base.OnHoverLost(e);
        }
    }
}
