using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>One bookmark as the list shows it.</summary>
public sealed class BookmarkRow
{
    public required long Y { get; init; }
    public required Bookmark Value { get; init; }
    public required double TimeMs { get; init; }
    public required string Position { get; init; }

    public string DisplayLabel => Value.Label.Length > 0 ? Value.Label : "(no label)";
    public Colour4 Colour => BookmarkTools.ColourOf(Value);
}

/// <summary>
/// Bookmark editing for the editor screen: the chart's <see cref="EditorInfo.Bookmarks"/> are the truth.
/// Rename / recolour / delete go through the undo stack (<see cref="SetBookmarkCommand"/>), so they mark the
/// chart dirty and survive save, kson, ksh and .svz export.
/// </summary>
public sealed class BookmarkTools
{
    /// <summary>Colours offered in the list (collab roles: one per mapper), plus "default" = null.</summary>
    public static readonly (string Name, string Hex)[] Palette =
    {
        ("blue", Bookmark.DefaultColour),
        ("pink", "#ff66aa"),
        ("green", "#4fdb4f"),
        ("yellow", "#ffd400"),
        ("orange", "#ff8a1f"),
        ("purple", "#a764ff"),
        ("cyan", "#28d7e6"),
        ("red", "#ff3b3b"),
    };

    private readonly EditorSession session;
    private readonly EditorClock clock;

    public BookmarkTools(EditorSession session, EditorClock clock)
    {
        this.session = session;
        this.clock = clock;
    }

    public static Colour4 ColourOf(Bookmark b) => Colour4.FromHex((Bookmark.NormaliseColour(b.Colour) ?? Bookmark.DefaultColour).TrimStart('#'));

    /// <summary>Label for hover texts: the label, else the time.</summary>
    public static string HoverText(Bookmark b, double ms) => b.Label.Length > 0 ? b.Label : "Bookmark " + TimingControlPointsDialog.FormatTime(ms);

    public IReadOnlyList<BookmarkRow> Rows() => session.Chart.Editor.Bookmarks.Select(b =>
    {
        var pos = session.Timing.PositionOf(Math.Max(0, b.Y));
        return new BookmarkRow { Y = b.Y, Value = b.Value, TimeMs = session.PulseToMs(b.Y), Position = $"{pos.Measure + 1}:{pos.Beat + 1}" };
    }).ToList();

    /// <summary>Sets (or with null deletes) the bookmark on <paramref name="y"/>; one undo step. False when nothing changed.</summary>
    public bool Set(long y, Bookmark? value)
    {
        var cmd = SetBookmarkCommand.Create(session.Chart, y, value);
        if (cmd == null) return false;
        session.Commands.Execute(cmd);
        return true;
    }

    /// <summary>Adds a bookmark at the cursor (snapped to the nearest pulse) unless one is there; returns its pulse.</summary>
    public long AddAtCursor(string label = "", string? colour = null)
    {
        long y = Math.Max(0, (long)Math.Round(session.MsToPulse(clock.CurrentTime.Value)));
        var existing = session.Chart.Editor.Bookmarks.FirstOrDefault(b => Math.Abs(session.PulseToMs(b.Y) - clock.CurrentTime.Value) < 1);
        if (existing != null)
        {
            if (label.Length > 0 || colour != null)
                Set(existing.Y, new Bookmark(label.Length > 0 ? label : existing.Value.Label, colour ?? existing.Value.Colour));
            return existing.Y;
        }

        Set(y, new Bookmark(label, colour));
        return y;
    }

    /// <summary>True when a bookmark sits on the cursor (within 1 ms).</summary>
    public bool HasBookmarkAtCursor => session.Chart.Editor.Bookmarks.Any(b => Math.Abs(session.PulseToMs(b.Y) - clock.CurrentTime.Value) < 1);

    /// <summary>
    /// Deletes the bookmark nearest to the cursor within <paramref name="windowMs"/> (one undo step); returns its time
    /// in ms, or null when none is that close.
    /// </summary>
    public double? RemoveNearCursor(double windowMs = 2000)
    {
        double now = clock.CurrentTime.Value;
        var near = session.Chart.Editor.Bookmarks
                          .Select(b => (b.Y, Ms: session.PulseToMs(b.Y)))
                          .Where(b => Math.Abs(b.Ms - now) <= windowMs)
                          .OrderBy(b => Math.Abs(b.Ms - now))
                          .Cast<(long Y, double Ms)?>()
                          .FirstOrDefault();
        if (near is not { } hit) return null;

        Set(hit.Y, null);
        return hit.Ms;
    }

    public void JumpTo(long y) => clock.CurrentTime.Value = Math.Clamp(session.PulseToMs(y), 0, clock.LengthMs);
}

/// <summary>
/// Edit → Bookmarks…: every bookmark with time, position, label and colour. Rename (Enter in the field),
/// colour swatches, delete, add at the cursor, jump (double click). Each change is one undo step.
/// </summary>
public partial class BookmarkListDialog : DialogWindow
{
    private readonly BookmarkTools tools;

    public ListView<BookmarkRow> List { get; private set; } = null!;
    public DialogTextBox LabelBox { get; private set; } = null!;

    public BookmarkListDialog(BookmarkTools tools)
        : base("Bookmarks", new Vector2(560, 400))
    {
        this.tools = tools;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Body.Children = new Drawable[]
        {
            DialogText.Label("Bookmarks travel with the chart (kson, ksh comment lines, .svz). Use labels and colours to split work between mappers.")
                .With(t => t.Position = new Vector2(10, 8)),
            List = new ListView<BookmarkRow>(new[]
            {
                new ListViewColumn<BookmarkRow>("Time", 92, r => TimingControlPointsDialog.FormatTime(r.TimeMs), r => r.Colour),
                new ListViewColumn<BookmarkRow>("Beat", 60, r => r.Position),
                new ListViewColumn<BookmarkRow>("Label", 384, r => r.DisplayLabel),
            })
            {
                Position = new Vector2(10, 30),
                Size = new Vector2(540, 230),
            },
            DialogText.Label("Label").With(t => t.Position = new Vector2(10, 272)),
            LabelBox = new DialogTextBox
            {
                Position = new Vector2(52, 268),
                Width = 330,
                PlaceholderText = "e.g. Part B - mapper2",
            },
            new DialogButton("Rename", Rename) { Position = new Vector2(390, 268), Width = 76 },
            new DialogButton("Delete", DeleteSelected) { Position = new Vector2(474, 268), Width = 76 },
            DialogText.Label("Colour").With(t => t.Position = new Vector2(10, 306)),
            swatchRow(),
            new DialogButton("Add at cursor", () => { long y = tools.AddAtCursor(LabelBox.Text.Trim()); refresh(y); }) { Position = new Vector2(10, 340), Width = 110 },
            new DialogButton("Jump to", JumpToSelected) { Position = new Vector2(128, 340), Width = 80 },
            new DialogButton("Close", Accept)
            {
                IsDefault = true,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-10, -10),
                Size = new Vector2(100, 28),
            },
        };

        List.SelectedItems.BindCollectionChanged((_, _) =>
        {
            if (Selected is { } r) LabelBox.Text = r.Value.Label;
        });
        List.ItemDoubleClicked += r => tools.JumpTo(r.Y);
        LabelBox.OnCommit += (_, _) => Rename();

        refresh(null);
    }

    public BookmarkRow? Selected => List.SelectedItems.FirstOrDefault();

    private void refresh(long? select)
    {
        long? keep = select ?? Selected?.Y;
        List.Items.Clear();
        List.Items.AddRange(tools.Rows());
        List.Select(List.Items.FirstOrDefault(r => r.Y == keep));
    }

    public void Rename()
    {
        if (Selected is not { } r) return;
        tools.Set(r.Y, r.Value with { Label = LabelBox.Text });
        refresh(r.Y);
    }

    public void SetColour(string? hex)
    {
        if (Selected is not { } r) return;
        tools.Set(r.Y, r.Value with { Colour = hex });
        refresh(r.Y);
    }

    public void DeleteSelected()
    {
        if (Selected is not { } r) return;
        tools.Set(r.Y, null);
        refresh(null);
    }

    public void JumpToSelected()
    {
        if (Selected is { } r) tools.JumpTo(r.Y);
    }

    /// <summary>Enter / Close: a label typed but not applied yet is kept.</summary>
    protected override void OnAccept()
    {
        if (Selected is { } r && LabelBox.Text != r.Value.Label) Rename();
    }

    /// <summary>The colour swatch row (the first one is the default blue).</summary>
    private Drawable swatchRow()
    {
        var row = new FillFlowContainer
        {
            Position = new Vector2(60, 302),
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4, 0),
        };

        foreach (var (_, hex) in BookmarkTools.Palette)
            row.Add(new DialogIconButton(FontAwesome.Solid.Circle, Colour4.FromHex(hex.TrimStart('#')), () => SetColour(hex == Bookmark.DefaultColour ? null : hex)));

        return row;
    }
}
