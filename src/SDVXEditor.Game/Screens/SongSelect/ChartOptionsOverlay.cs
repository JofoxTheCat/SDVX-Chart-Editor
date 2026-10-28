using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// "What do you want to do with this chart?" (reference 06): full-screen dim, header,
/// a centred stack of wide numbered buttons; keys 1-6 pick, Esc cancels.
/// </summary>
public partial class ChartOptionsOverlay : CompositeDrawable
{
    /// <summary>Raised with the chosen action for the chart the overlay was opened for.</summary>
    public event Action<ChartAction, ISongListItem>? ActionChosen;

    private ISongListItem? item;
    private SpriteText title = null!;
    private SpriteText question = null!;

    /// <summary>The opaque band behind the title (smoke test: the title must lie inside it).</summary>
    public Box HeaderBand { get; private set; } = null!;

    /// <summary>The question line ("What do you want to do with this chart?").</summary>
    public SpriteText Question => question;
    private FillFlowContainer<NumberedMenuButton> buttons = null!;

    public bool IsOpen => item != null;

    public ChartOptionsOverlay()
    {
        RelativeSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.78f) },
            // the song header underneath is white text too: an opaque band keeps the title readable (audit B7)
            HeaderBand = new Box { RelativeSizeAxes = Axes.X, Height = 120, Colour = Colour4.Black },
            new FillFlowContainer
            {
                Position = new Vector2(14, 6),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    title = new SpriteText { Font = EditorFont.Regular(44), Colour = Colour4.White, Shadow = true, MaxWidth = 1560, Truncate = true },
                    question = new SpriteText
                    {
                        Font = EditorFont.Regular(44),
                        Colour = Colour4.White,
                        Shadow = true,
                    },
                },
            },
            buttons = new FillFlowContainer<NumberedMenuButton>
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 233,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 28),
            },
        };
    }

    private NumberedMenuButton button(int number, string label, Colour4 colour, ChartAction action) =>
        new NumberedMenuButton(number, label, colour, () => choose(action));

    /// <summary>The page currently shown (main, export or delete confirmation).</summary>
    public OptionsPage Page { get; private set; }

    public void Open(ISongListItem chart)
    {
        item = chart;
        title.Text = $"{chart.Artist} - {chart.Title}";
        showPage(OptionsPage.Main);
        this.FadeIn(150);
    }

    public void Close()
    {
        item = null;
        this.FadeOut(150);
    }

    private void showPage(OptionsPage page)
    {
        Page = page;
        question.Text = page switch
        {
            OptionsPage.Export => "Export this song as…",
            OptionsPage.ConfirmDelete => "Really delete this song? It goes to the recycle bin.",
            _ => "What do you want to do with this chart?",
        };

        buttons.Clear();
        buttons.AddRange(page switch
        {
            OptionsPage.Export => new[]
            {
                button(1, ".kson (all charts)", EditorColours.Osu.MenuBlue, ChartAction.ExportKson),
                button(2, ".ksh (all charts)", EditorColours.Osu.MenuBlue, ChartAction.ExportKsh),
                button(3, "VOX (ksh2vox)", EditorColours.Osu.MenuBlue, ChartAction.ExportVox),
                button(4, ".zip (song folder)", EditorColours.Osu.MenuBlue, ChartAction.ExportZip),
                button(5, ".svz (share / collab)", EditorColours.Osu.MenuGreen, ChartAction.ExportSvz),
                new NumberedMenuButton(6, "Back", EditorColours.Osu.MenuGrey, () => showPage(OptionsPage.Main)),
            },
            OptionsPage.ConfirmDelete => new[]
            {
                button(1, "Yes, delete it", EditorColours.Osu.MenuRed, ChartAction.Delete),
                new NumberedMenuButton(2, "No, keep it", EditorColours.Osu.MenuGrey, () => showPage(OptionsPage.Main)),
            },
            _ => new[]
            {
                button(1, "Manage Collections", EditorColours.Osu.MenuGreen, ChartAction.Collections),
                new NumberedMenuButton(2, "Delete...", EditorColours.Osu.MenuRed, requestDelete),
                button(3, "Edit", EditorColours.Osu.MenuYellow, ChartAction.Edit),
                button(4, "Open Folder", EditorColours.Osu.MenuBlue, ChartAction.OpenFolder),
                new NumberedMenuButton(5, "Export...", EditorColours.Osu.MenuBlue, () => showPage(OptionsPage.Export)),
                button(6, "Cancel", EditorColours.Osu.MenuGrey, ChartAction.Cancel),
            },
        });
    }

    /// <summary>Launcher songs are not ours to delete: the screen explains that instead of asking.</summary>
    private void requestDelete()
    {
        if (item?.IsReadOnly == true)
            choose(ChartAction.DeleteRefused);
        else
            showPage(OptionsPage.ConfirmDelete);
    }

    /// <summary>Presses the numbered button on the current page (keys 1-6; smoke test).</summary>
    public void Press(int number) => buttons.Children.FirstOrDefault(b => b.Number == number)?.TriggerClick();

    private void choose(ChartAction action)
    {
        var chart = item;
        Close();

        if (chart != null && action != ChartAction.Cancel)
            ActionChosen?.Invoke(action, chart);
    }

    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => IsOpen && base.ReceivePositionalInputAt(screenSpacePos);

    protected override bool OnMouseDown(MouseDownEvent e) => IsOpen;
    protected override bool OnScroll(ScrollEvent e) => IsOpen;
    protected override bool OnHover(HoverEvent e) => IsOpen;

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (!IsOpen)
            return false;

        if (e.Key == Key.Escape)
        {
            if (Page == OptionsPage.Main) choose(ChartAction.Cancel);
            else showPage(OptionsPage.Main);
            return true;
        }

        int n = e.Key switch
        {
            >= Key.Number1 and <= Key.Number6 => e.Key - Key.Number1 + 1,
            >= Key.Keypad1 and <= Key.Keypad6 => e.Key - Key.Keypad1 + 1,
            _ => 0,
        };

        if (n > 0 && !e.Repeat)
            Press(n);

        // modal while open
        return true;
    }
}

public enum OptionsPage
{
    Main,
    Export,
    ConfirmDelete,
}

public enum ChartAction
{
    Collections,

    /// <summary>Delete confirmed by the user.</summary>
    Delete,

    /// <summary>Delete asked for a launcher song (refused, the screen explains why).</summary>
    DeleteRefused,

    Edit,
    OpenFolder,
    ExportKson,
    ExportKsh,
    ExportVox,
    ExportZip,

    /// <summary>The editor's share / collab package (.svz).</summary>
    ExportSvz,
    Cancel,
}
