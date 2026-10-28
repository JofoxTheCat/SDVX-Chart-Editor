namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// Editor tabs in strip order. Keys: F1 compose, F2 camera, F3 timing, Shift+F2 effects,
/// F4 song setup (opens the Song Setup dialog), F5 playtest, F6 timing dialog.
/// </summary>
public enum EditorTab
{
    Compose,
    Camera,
    Timing,
    Effects,

    /// <summary>Not a page of its own: selecting it opens the Song Setup dialog.</summary>
    SongSetup,
}

public static class EditorTabExtensions
{
    /// <summary>Label in the tab strip (lower-case, as in the reference).</summary>
    public static string DisplayName(this EditorTab tab) => tab switch
    {
        EditorTab.Compose => "compose",
        EditorTab.Camera => "camera",
        EditorTab.Timing => "timing",
        EditorTab.Effects => "effects",
        EditorTab.SongSetup => "song setup",
        _ => tab.ToString().ToLowerInvariant(),
    };

    public static string Hotkey(this EditorTab tab) => tab switch
    {
        EditorTab.Compose => "F1",
        EditorTab.Camera => "F2",
        EditorTab.Timing => "F3",
        EditorTab.Effects => "Shift+F2",
        EditorTab.SongSetup => "F4",
        _ => string.Empty,
    };
}
