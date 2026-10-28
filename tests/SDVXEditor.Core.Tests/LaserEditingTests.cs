using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class LaserEditingTests
{
    // 960 → 1920: 0 at 960, 0.5 at 1440, 1 at 1920
    private static LaserSection three() =>
        new(960, new[] { new LaserPoint(0, 0), new LaserPoint(480, 0.5), new LaserPoint(960, 1) });

    [Test]
    public void NewSectionInEitherDirection()
    {
        var a = LaserEditing.NewSection(100, 0.2, 400, 0.8, 1)!;
        var b = LaserEditing.NewSection(400, 0.8, 100, 0.2, 1)!;
        foreach (var s in new[] { a, b })
        {
            Assert.That(s.Y, Is.EqualTo(100));
            Assert.That(s.Points.Select(p => (p.Ry, p.V)), Is.EqualTo(new[] { (0L, 0.2), (300L, 0.8) }));
        }
    }

    [Test]
    public void NewSectionOnOnePulseIsALoneSlam()
    {
        var s = LaserEditing.NewSection(100, 0, 100, 1, 2)!;
        Assert.That(s.Width, Is.EqualTo(2));
        Assert.That(s.Points.Single().IsSlam && s.Points[0].V == 0 && s.Points[0].Vf == 1, "no tail: official charts end sections on the slam");
        Assert.That(s.EndY, Is.EqualTo(100));
        Assert.That(LaserEditing.NewSection(100, 0.5, 100, 0.5, 1), Is.Null);
    }

    [Test]
    public void MovePointKeepsTheOriginalAndClampsBetweenNeighbours()
    {
        var s = three();
        var m = LaserEditing.MovePoint(s, 1, 5000, 0.25)!;
        Assert.That(s.Points[1].Ry, Is.EqualTo(480), "input unchanged");
        // clamped onto the next point's pulse → merged into a slam 0.25 → 1
        Assert.That(m.Points.Count, Is.EqualTo(2));
        Assert.That(m.Points[1].Ry, Is.EqualTo(960));
        Assert.That(m.Points[1].V, Is.EqualTo(0.25));
        Assert.That(m.Points[1].Vf, Is.EqualTo(1));
    }

    [Test]
    public void DraggingOntoThePreviousPointMakesASlamFromIt()
    {
        var m = LaserEditing.MovePoint(three(), 1, 960, 0.75)!;
        Assert.That(m.Y, Is.EqualTo(960));
        Assert.That(m.Points.Count, Is.EqualTo(2));
        Assert.That(m.Points[0].V, Is.EqualTo(0));
        Assert.That(m.Points[0].Vf, Is.EqualTo(0.75));
        Assert.That(m.SlamCount, Is.EqualTo(1));
    }

    [Test]
    public void MergingEqualValuesIsNoSlam()
    {
        var m = LaserEditing.MovePoint(three(), 1, 960, 0)!;
        Assert.That(m.Points.Count, Is.EqualTo(2));
        Assert.That(m.SlamCount, Is.EqualTo(0));
    }

    [Test]
    public void MovingTheFirstPointRebasesTheSection()
    {
        var m = LaserEditing.MovePoint(three(), 0, 720, 0.1)!;
        Assert.That(m.Y, Is.EqualTo(720));
        Assert.That(m.Points.Select(p => p.Ry), Is.EqualTo(new[] { 0L, 720L, 1200L }));
        Assert.That(m.EndY, Is.EqualTo(1920));
        Assert.That(LaserEditing.MovePoint(three(), 0, -50, 0)!.Y, Is.EqualTo(0), "never before pulse 0");
    }

    [Test]
    public void SlamEndValueEdit()
    {
        var s = new LaserSection(0, new[] { new LaserPoint(0, 0, 1), new LaserPoint(240, 1) });
        var m = LaserEditing.MovePoint(s, 0, 999, 0.4, slamEnd: true)!;
        Assert.That(m.Points[0].Ry, Is.EqualTo(0), "the pulse does not move");
        Assert.That(m.Points[0].Vf, Is.EqualTo(0.4));
        Assert.That(LaserEditing.MovePoint(s, 0, 0, 0, slamEnd: true)!.Points[0].IsSlam, Is.False, "vf = v removes the jump");
    }

    [Test]
    public void InsertPoint()
    {
        var r = LaserEditing.InsertPoint(three(), 1200, 0.3)!.Value;
        Assert.That(r.Index, Is.EqualTo(1));
        Assert.That(r.Section.Points[1].Ry, Is.EqualTo(240));
        Assert.That(LaserEditing.InsertPoint(three(), 1440, 0.3), Is.Null, "a point already sits there");
        Assert.That(LaserEditing.InsertPoint(three(), 2000, 0.3), Is.Null, "outside the section");
    }
}
