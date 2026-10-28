namespace SDVXEditor.Game.Configuration;

public partial class EditorConfigManager
{
    private void initialiseTutorialDefaults()
    {
        SetDefault(EditorSetting.TutorialProgress, string.Empty);
        SetDefault(EditorSetting.TutorialMainProgress, string.Empty);
        SetDefault(EditorSetting.TutorialHintShown, false);
    }
}
