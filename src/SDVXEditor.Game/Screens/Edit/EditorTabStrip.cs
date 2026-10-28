using SDVXEditor.Game.Graphics.UserInterface;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>The editor's "compose camera timing effects song setup" strip.</summary>
public partial class EditorTabStrip : BigTabStrip<EditorTab>
{
    public EditorTabStrip()
        : base(Enum.GetValues<EditorTab>().Select(t => (t, t.DisplayName())))
    {
    }
}
