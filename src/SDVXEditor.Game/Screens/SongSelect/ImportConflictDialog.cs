using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// An imported song (.svz / .zip / folder) has the folder name of an editor song: replace it (osu!'s "update the
/// set": the collab partner's newer version), keep both (imported as "name 2"), or cancel. Esc = cancel.
/// </summary>
public partial class ImportConflictDialog : DialogWindow
{
    private readonly string folderName;

    /// <summary>The answer once the window closed; null = cancelled.</summary>
    public ExistingSongPolicy? Choice { get; private set; }

    public ImportConflictDialog(string folderName)
        : base("Song already in the library", new Vector2(470, 150))
    {
        this.folderName = folderName;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Body.Children = new Drawable[]
        {
            new FillFlowContainer
            {
                Position = new Vector2(12, 12),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Children = new Drawable[]
                {
                    DialogText.Label($"\"{folderName}\" already exists in your editor songs.", bold: true),
                    DialogText.Label("Replace it with the imported version (the old folder is deleted),"),
                    DialogText.Label("or keep both (the import gets a new folder name)?"),
                },
            },
            new DialogButton("Replace", ReplaceExisting)
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-232, -10),
                Size = new Vector2(100, 28),
            },
            new DialogButton("Keep both", KeepBoth)
            {
                IsDefault = true,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-122, -10),
                Size = new Vector2(100, 28),
            },
            new DialogButton("Cancel", Cancel)
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-12, -10),
                Size = new Vector2(100, 28),
            },
        };
    }

    public void ReplaceExisting()
    {
        Choice = ExistingSongPolicy.Replace;
        Accept();
    }

    /// <summary>Enter = keep both: the choice that never deletes anything.</summary>
    public void KeepBoth()
    {
        Choice = ExistingSongPolicy.Rename;
        Accept();
    }

    protected override void OnAccept() => Choice ??= ExistingSongPolicy.Rename;

    protected override void OnCancel() => Choice = null;
}
