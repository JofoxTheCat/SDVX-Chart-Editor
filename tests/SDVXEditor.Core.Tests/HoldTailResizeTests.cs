using NUnit.Framework;
using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Tests;

/// <summary>Dragging a hold's tail: grab zone, the grabbed length, the same delta for the other selected holds.</summary>
public class HoldTailResizeTests
{
    [Test]
    public void GrabbedLengthFollowsThePointerAndBecomesAChipOnlyAtItsStart()
    {
        Assert.That(HoldTailResize.GrabbedLength(960, 1440), Is.EqualTo(480));
        Assert.That(HoldTailResize.GrabbedLength(960, 1020), Is.EqualTo(60));
        Assert.That(HoldTailResize.GrabbedLength(960, 960), Is.EqualTo(0));
        Assert.That(HoldTailResize.GrabbedLength(960, 480), Is.EqualTo(0));
    }

    [Test]
    public void OtherHoldsChangeByTheSameDeltaButStayHolds()
    {
        Assert.That(HoldTailResize.OtherLength(480, 240, 60), Is.EqualTo(720));
        Assert.That(HoldTailResize.OtherLength(480, -240, 60), Is.EqualTo(240));
        // never below one snap tick, never a chip
        Assert.That(HoldTailResize.OtherLength(480, -900, 60), Is.EqualTo(60));
        // a hold already shorter than a tick is not lengthened by a shrinking drag
        Assert.That(HoldTailResize.OtherLength(30, -100, 60), Is.EqualTo(30));
    }

    [TestCase(0, 50, true)]
    [TestCase(7, 50, true)]
    [TestCase(7.5, 50, false)]
    [TestCase(4, 4, false)]   // tiny hold: equally near the head, the head wins (move)
    [TestCase(3, 5, true)]
    public void TailGrabZone(double fromEnd, double fromStart, bool tail)
    {
        Assert.That(HoldTailResize.IsTailGrab(fromEnd, fromStart, 7), Is.EqualTo(tail));
    }
}
