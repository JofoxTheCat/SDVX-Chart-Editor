using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// Undoable change of one bookmark, addressed by its pulse: add (<c>before</c> null), rename/recolour,
/// or delete (<c>after</c> null). Every bookmark change of the editor (Ctrl+B, Ctrl+Shift+B, the bottom bar buttons,
/// the list dialog) is one of these, so Ctrl+Z undoes it. Addressing by pulse instead of by list index keeps undo
/// correct after timing edits re-sorted the list.
/// </summary>
public sealed class SetBookmarkCommand : IEditCommand
{
    private readonly long y;
    private readonly Bookmark? before;
    private readonly Bookmark? after;

    public SetBookmarkCommand(long y, Bookmark? before, Bookmark? after, string? description = null)
    {
        this.y = y;
        this.before = before;
        this.after = after;
        Description = description ?? (before == null ? "Add bookmark" : after == null ? "Delete bookmark" : "Edit bookmark");
    }

    public string Description { get; }

    public void Execute(Chart chart) => apply(chart.Editor.Bookmarks, after);

    public void Undo(Chart chart) => apply(chart.Editor.Bookmarks, before);

    private void apply(BookmarkList list, Bookmark? value)
    {
        if (value == null) list.RemoveOn(y);
        else list.Set(y, value);
    }

    /// <summary>The command that sets the bookmark on <paramref name="y"/> to <paramref name="value"/> (null = delete), or null when nothing changes.</summary>
    public static SetBookmarkCommand? Create(Chart chart, long y, Bookmark? value)
    {
        var current = chart.Editor.Bookmarks.At(y)?.Value;
        if (current == value) return null;
        if (value != null) value = value with { Label = Bookmark.CleanLabel(value.Label), Colour = Bookmark.NormaliseColour(value.Colour) };
        if (current == value) return null;
        return new SetBookmarkCommand(y, current, value);
    }
}
