using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;

namespace SDVXEditor.Game.Input;

/// <summary>
/// The mouse wheel as the user turned it.
/// <para>
/// osu-framework 2026.807.0 turns a notched (non-precise) vertical wheel turn into a <b>horizontal</b> one while Shift
/// is held: <c>MouseScrollRelativeInput.Apply</c> does <c>if (!IsPrecise &amp;&amp; Delta.X == 0 &amp;&amp; ShiftPressed)
/// Delta = (Delta.Y, 0)</c> (read from the IL; the SDL window feeds every wheel turn through it). So Shift+wheel up
/// arrives as <c>ScrollDelta = (1, 0)</c> and <c>KeyCombination.FromScrollDelta</c> names it <c>MouseWheelLeft</c>
/// — every control that read only <c>ScrollDelta.Y</c> ignored Shift+wheel (the ×10 steppers stood still) and the
/// Shift+wheel bindings (seek four ticks) never fired. macOS and some mouse drivers send Shift+wheel horizontally on
/// their own. With Shift held the X axis therefore counts as the wheel: positive = up = left, as the framework
/// defines both.
/// </para>
/// </summary>
public static class WheelInput
{
    /// <summary>The wheel delta, positive = up; a horizontal delta counts only while Shift is held.</summary>
    public static float Delta(ScrollEvent e) => e.ScrollDelta.Y != 0 ? e.ScrollDelta.Y : e.ShiftPressed ? e.ScrollDelta.X : 0;

    /// <summary>+1 wheel up, −1 wheel down, 0 no wheel turn (a plain horizontal scroll).</summary>
    public static int Direction(ScrollEvent e) => Math.Sign(Delta(e));

    /// <summary>
    /// A binding with Shift and a vertical wheel key, plus the same binding on the horizontal key the framework
    /// delivers for it (Shift+wheel up → <c>MouseWheelLeft</c>, down → <c>MouseWheelRight</c>); other bindings unchanged.
    /// </summary>
    public static IEnumerable<KeyCombination> WithShiftWheelAlias(KeyCombination combination)
    {
        yield return combination;

        var keys = combination.Keys;
        bool shift = keys.Any(k => k is InputKey.Shift or InputKey.LShift or InputKey.RShift);
        if (!shift || !keys.Any(k => k is InputKey.MouseWheelUp or InputKey.MouseWheelDown))
            yield break;

        yield return new KeyCombination(keys.Select(k => k switch
        {
            InputKey.MouseWheelUp => InputKey.MouseWheelLeft,
            InputKey.MouseWheelDown => InputKey.MouseWheelRight,
            _ => k,
        }).ToArray());
    }
}
