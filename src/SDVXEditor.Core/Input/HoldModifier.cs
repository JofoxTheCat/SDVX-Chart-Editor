namespace SDVXEditor.Core.Input;

/// <summary>The key that turns the compose chip tools into their hold variant while held (Options → Input).</summary>
public enum HoldModifierKey
{
    Ctrl,
    Alt,
}

/// <summary>
/// The key that makes a drag of existing laser points / anchors snap (to the beat divisor and the value grid) while
/// held; without it such drags are free (Options → Input → Compose).
/// </summary>
public enum DragSnapKey
{
    Shift,
    Ctrl,
    Alt,
}

public static class DragSnapKeys
{
    /// <summary>The drag-snap key is down, given the modifier state of a drag event.</summary>
    public static bool IsHeld(this DragSnapKey key, bool shift, bool ctrl, bool alt) => key switch
    {
        DragSnapKey.Ctrl => ctrl,
        DragSnapKey.Alt => alt,
        _ => shift,
    };
}

/// <summary>
/// Which note a compose note tool places, given the held modifiers. Shift swaps BT ↔ FX (BT tools only), the hold
/// modifier swaps chip → hold; both together turn the BT chip tool into FX hold. A hold tool stays a hold tool with the
/// hold modifier (it does not flip back to a chip: holding a key should never take a capability away), and the FX tools
/// ignore Shift as before.
/// </summary>
public static class NoteToolVariant
{
    /// <param name="fx">The chosen tool is an FX tool.</param>
    /// <param name="hold">The chosen tool is a hold tool.</param>
    /// <param name="shift">Shift is held.</param>
    /// <param name="holdModifier">The hold modifier is held (and not latched off, see <see cref="HoldModifierLatch"/>).</param>
    public static (bool Fx, bool Hold) Resolve(bool fx, bool hold, bool shift, bool holdModifier) => (fx || shift, hold || holdModifier);
}

/// <summary>
/// Whether the hold modifier currently counts. It is only a click modifier: as soon as another key (Ctrl+Z, Ctrl+C,
/// Alt+Enter …), a wheel turn (Ctrl+wheel divisor, Alt+wheel zoom) or an editor action happens while it is down, it is
/// used up as part of that combination and stays off until the key is released. So pressing Ctrl for Ctrl+Z never shows
/// a hold ghost after the Z, and keeping Ctrl down after an undo does not turn the next click into a hold.
/// </summary>
public sealed class HoldModifierLatch
{
    private bool down;
    private bool usedUp;

    /// <summary>The modifier is down and has not been used for anything else since it went down.</summary>
    public bool Active => down && !usedUp;

    /// <summary>Feeds the current state of the modifier key; a release clears the latch.</summary>
    /// <returns>true when <see cref="Active"/> changed.</returns>
    public bool SetDown(bool isDown)
    {
        bool before = Active;
        if (!isDown) usedUp = false;
        down = isDown;
        return before != Active;
    }

    /// <summary>Another key, a wheel turn or an editor action while the modifier is down: it was part of a combination.</summary>
    /// <returns>true when <see cref="Active"/> changed.</returns>
    public bool OtherInput()
    {
        if (!down) return false;
        bool before = Active;
        usedUp = true;
        return before != Active;
    }
}
