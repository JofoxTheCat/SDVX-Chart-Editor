namespace SDVXEditor.Game.Screens.Edit.Compose;

/// <summary>One entry of the laser context menu. <see cref="Run"/> null = a title row.</summary>
public sealed record LaserMenuItem(string Label, Action? Run, bool Checked = false, bool Enabled = true)
{
    public static LaserMenuItem Title(string text) => new(text, null, false, false);

    public bool IsTitle => Run == null;
}
