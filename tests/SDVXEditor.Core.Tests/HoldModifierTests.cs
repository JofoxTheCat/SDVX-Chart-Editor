using NUnit.Framework;
using SDVXEditor.Core.Input;

namespace SDVXEditor.Core.Tests;

/// <summary>Compose hold modifier (Ctrl / Alt): which note a tool places, and when a held modifier counts.</summary>
public class HoldModifierTests
{
    [TestCase(false, false, false, false, false, false, TestName = "BT chip stays BT chip")]
    [TestCase(false, false, true, false, true, false, TestName = "BT chip + Shift = FX chip")]
    [TestCase(false, false, false, true, false, true, TestName = "BT chip + hold modifier = BT hold")]
    [TestCase(false, false, true, true, true, true, TestName = "BT chip + Shift + hold modifier = FX hold")]
    [TestCase(true, false, false, true, true, true, TestName = "FX chip + hold modifier = FX hold")]
    [TestCase(false, true, false, true, false, true, TestName = "BT hold + hold modifier stays BT hold")]
    [TestCase(false, true, true, false, true, true, TestName = "BT hold + Shift = FX hold")]
    [TestCase(true, true, false, true, true, true, TestName = "FX hold + hold modifier stays FX hold")]
    public void Resolve(bool fx, bool hold, bool shift, bool holdModifier, bool expectFx, bool expectHold)
    {
        Assert.That(NoteToolVariant.Resolve(fx, hold, shift, holdModifier), Is.EqualTo((expectFx, expectHold)));
    }

    [Test]
    public void ModifierCountsWhileHeldAlone()
    {
        var latch = new HoldModifierLatch();
        Assert.That(latch.Active, Is.False);

        Assert.That(latch.SetDown(true), Is.True);
        Assert.That(latch.Active, Is.True);

        // repeated polls of the same state change nothing
        Assert.That(latch.SetDown(true), Is.False);
        Assert.That(latch.Active, Is.True);

        Assert.That(latch.SetDown(false), Is.True);
        Assert.That(latch.Active, Is.False);
    }

    [Test]
    public void ShortcutUsesTheModifierUpUntilRelease()
    {
        var latch = new HoldModifierLatch();

        // Ctrl down, Z: the Ctrl belonged to Ctrl+Z
        latch.SetDown(true);
        Assert.That(latch.OtherInput(), Is.True);
        Assert.That(latch.Active, Is.False);

        // Z released, Ctrl still down: still used up (no hold ghost after the undo, a click places a chip)
        latch.SetDown(true);
        Assert.That(latch.Active, Is.False);

        // released and pressed again: counts again
        latch.SetDown(false);
        latch.SetDown(true);
        Assert.That(latch.Active, Is.True);
    }

    [Test]
    public void OtherInputWithoutTheModifierIsIgnored()
    {
        var latch = new HoldModifierLatch();
        Assert.That(latch.OtherInput(), Is.False);

        // a key pressed before the modifier does not pre-spend the next press
        latch.SetDown(true);
        Assert.That(latch.Active, Is.True);
    }
}
