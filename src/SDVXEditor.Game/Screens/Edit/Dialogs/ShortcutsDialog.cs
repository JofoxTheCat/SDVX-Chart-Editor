using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osuTK;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>Help → Keyboard Shortcuts: the central binding table, one tab per category.</summary>
public partial class ShortcutsDialog : DialogWindow
{
    private sealed class Row
    {
        public required string Keys { get; init; }
        public required string Description { get; init; }
    }

    private DialogTabControl tabs = null!;

    public ShortcutsDialog()
        : base("Keyboard Shortcuts", new Vector2(620, 470))
    {
    }

    public int PageCount => tabs.PageTitles.Count;

    private readonly List<Row> rows = new List<Row>();

    /// <summary>Every row shown, as (keys, description) (smoke test: the dialog shows the bindings in effect).</summary>
    public IEnumerable<(string Keys, string Description)> Rows => rows.Select(r => (r.Keys, r.Description));

    public void ShowPage(int index) => tabs.SelectedIndex.Value = index;

    [BackgroundDependencyLoader]
    private void load()
    {
        tabs = new DialogTabControl(0)
        {
            Position = new Vector2(4, 4),
            Size = new Vector2(612, 418),
        };

        // the bindings in effect (user overrides from the options included), every action, unbound ones too
        foreach (string category in EditorKeyBindings.Categories)
        {
            var list = new ListView<Row>(new[]
            {
                new ListViewColumn<Row>("Keys", 150, r => r.Keys),
                new ListViewColumn<Row>("Action", 440, r => r.Description),
            })
            {
                RelativeSizeAxes = Axes.Both,
            };

            // one row per action; alternative keys are joined ("Space / C")
            foreach (var action in EditorKeyBindings.ActionsIn(category))
            {
                var keys = EditorKeyBindings.All.Where(b => b.Action == action).ToList();
                var row = new Row
                {
                    Keys = keys.Count == 0 ? Overlays.Options.KeyBindButton.UNBOUND : string.Join(" / ", keys.Select(a => EditorKeyBindings.Describe(a.Keys))),
                    Description = EditorKeyBindings.InfoOf(action).Description,
                };
                rows.Add(row);
                list.Items.Add(row);
            }

            tabs.AddPage(category, list);
        }

        Body.Children = new Drawable[]
        {
            tabs,
            new DialogButton("Close", Accept)
            {
                IsDefault = true,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-4, -6),
                Size = new Vector2(120, 32),
            },
        };
    }
}
