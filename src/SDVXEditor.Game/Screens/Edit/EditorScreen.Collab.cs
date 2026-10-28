using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Songs;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// Collab features of the editor screen: labelled bookmarks (list dialog), "open difficulty for reference"
/// (osu!mania: a second, read-only playfield right of the main one, same clock / zoom / scroll; select there,
/// Ctrl+C, paste into the main chart) and the .svz package export.
/// </summary>
public partial class EditorScreen
{
    [Resolved(canBeNull: true)]
    private LibraryManager? library { get; set; }

    /// <summary>Bookmark list / edit helpers (the chart's bookmarks are the truth).</summary>
    public BookmarkTools BookmarkTools { get; private set; } = null!;

    /// <summary>The reference difficulty's view-only session, or null while none is open.</summary>
    public EditorSession? ReferenceSession { get; private set; }

    /// <summary>Select-only tools of the reference playfield (locked: no placing, moving or deleting).</summary>
    public ComposeEditor? ReferenceCompose { get; private set; }

    public ComposePlayfield? ReferencePlayfield { get; private set; }

    /// <summary>The submenu File → Open Difficulty for Reference (its items follow the song's charts).</summary>
    private EditorMenuItem referenceMenu = null!;

    private readonly EditorToolState referenceTools = new EditorToolState();

    private void setupCollab()
    {
        BookmarkTools = new BookmarkTools(Session, Playback);

        if (ChartActions is EditorChartActions actions)
        {
            actions.ReferenceSelection = () => ReferenceSession is { } r
                ? (r.Selection, r.Chart.Meta.DifficultyDisplayName + " " + Difficulty.FormatLevel(r.Chart.Meta.Level))
                : null;
        }

        // one selection at a time: selecting in the reference clears the main one and vice versa, so Ctrl+C is never ambiguous
        Session.Selection.Changed += () =>
        {
            if (!Session.Selection.IsEmpty) ReferenceSession?.Selection.Clear();
        };

        refreshReferenceMenu();
    }

    private EditorMenuItem createReferenceMenu() => referenceMenu = new EditorMenuItem("Open Difficulty for Reference", Array.Empty<MenuItem>());

    private void refreshReferenceMenu()
    {
        if (referenceMenu == null) return;

        var items = new List<MenuItem>();
        foreach (var chart in Session.Charts)
        {
            if (chart == Session.ActiveChart) continue;
            var m = chart.Chart.Meta;
            var c = chart;
            items.Add(new EditorMenuItem($"{m.DifficultyDisplayName} {Difficulty.FormatLevel(m.Level)} ({Path.GetFileName(chart.FilePath)})", "", () => OpenReference(c)));
        }

        if (items.Count == 0) items.Add(new EditorMenuItem("(no other difficulty in this song)", "", () => showToast("This song has no other difficulty")));
        items.Add(new EditorMenuSeparator());
        items.Add(new EditorMenuItem("Close Reference", "Esc", () =>
        {
            if (ReferenceSession == null)
            {
                showToast("No reference difficulty is open");
                return;
            }

            CloseReference();
            showToast("Reference closed");
        }));
        referenceMenu.Items = items;
    }

    /// <summary>Shows <paramref name="chart"/> read-only right of the compose playfield (replacing an open reference).</summary>
    public bool OpenReference(SongChart chart)
    {
        if (Session.Song == null || chart == Session.ActiveChart)
        {
            showToast("Pick another difficulty of this song as the reference");
            return false;
        }

        CloseReference();

        referenceTools.Tool.Value = EditorTool.Select;
        ReferenceSession = EditorSession.CreateReference(Session.Song, chart);
        ReferenceCompose = new ComposeEditor(ReferenceSession, referenceTools);
        ReferenceCompose.LockNotes.Value = true; // select only: no move, no right-click delete
        ReferenceCompose.Divisor.BindTo(Compose.Divisor);

        ReferenceSession.Selection.Changed += () =>
        {
            if (ReferenceSession is { Selection.IsEmpty: false }) Session.Selection.Clear();
        };

        var m = chart.Chart.Meta;
        ComposeContent.ReferenceContainer.Children = new Drawable[]
        {
            ReferencePlayfield = new ComposePlayfield(ReferenceSession, Playback, ReferenceCompose, referenceTools, Zoom)
            {
                // clearly greyed (Johannes): it is context, not the chart being edited — darkened and
                // half see-through, with a grey wash on top that takes the colour out
                Colour = new Colour4(0.5f, 0.5f, 0.5f, 1f),
                Alpha = 0.8f,
            },
            // input passes through a plain Box, so selecting and copying in the reference still works
            new Box { RelativeSizeAxes = Axes.Both, Colour = new Colour4(0.45f, 0.45f, 0.48f, 0.35f) },
            new Container
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 4,
                Masking = true,
                CornerRadius = 4,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.7f) },
                    new SpriteText
                    {
                        Text = $"REFERENCE  {m.DifficultyDisplayName} {Difficulty.FormatLevel(m.Level)}  (read-only, Ctrl+C copies, Esc closes)",
                        Font = EditorFont.Bold(15),
                        Colour = EditorColours.TextMuted,
                        Margin = new MarginPadding { Horizontal = 8, Vertical = 3 },
                    },
                },
            },
        };
        ComposeContent.SetReferenceOpen(true);
        showToast($"Reference: {m.DifficultyDisplayName} {Difficulty.FormatLevel(m.Level)} (read-only)");
        return true;
    }

    public void CloseReference()
    {
        if (ReferenceSession == null) return;

        ComposeContent.ReferenceContainer.Clear();
        ComposeContent.SetReferenceOpen(false);
        ReferenceCompose?.Divisor.UnbindAll();
        ReferenceSession.Dispose();
        ReferenceSession = null;
        ReferenceCompose = null;
        ReferencePlayfield = null;
    }

    /// <summary>After a chart switch / revert: the reference must not be the chart being edited, and the menu lists the others.</summary>
    private void onCollabChartReplaced()
    {
        if (ReferenceSession != null && ReferenceSession.ActiveChart == Session.ActiveChart)
            CloseReference();
        refreshReferenceMenu();
    }

    // ---- bookmarks ---------------------------------------------------------

    public BookmarkListDialog OpenBookmarkList()
    {
        if (dialogs.Current is BookmarkListDialog open)
            return open;

        var dialog = new BookmarkListDialog(BookmarkTools);
        dialogs.Push(dialog);
        return dialog;
    }

    // ---- .svz --------------------------------------------------------------

    /// <summary>File → Export → .svz: packages the song folder as it is on disk (save first to include unsaved edits).</summary>
    public async Task<string?> ExportSvz()
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

        if (Session.IsDirty)
            showToast("Unsaved changes are not in the package: save first (Ctrl+S)");

        try
        {
            var result = await library.ExportSvzAsync(Session.Song.FolderPath);
            Schedule(() => showToast($"Exported {Path.GetFileName(result.Path)} to {Path.GetDirectoryName(result.Path)}"));
            return result.Path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.Log($"svz export failed: {e}");
            Schedule(() => showToast("Export failed: " + e.Message));
            return null;
        }
    }
}
