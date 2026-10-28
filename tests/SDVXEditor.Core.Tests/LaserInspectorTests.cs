using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Tests;

public class LaserInspectorTests
{
    private static (Chart, LaserSection) setup()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        var s = new LaserSection(960, new[] { new LaserPoint(0, 0), new LaserPoint(240, 0.2, 0.8), new LaserPoint(480, 0.5) });
        c.Notes.Lasers[0].Add(s);
        return (c, s);
    }

    private static Selection all(LaserSection s, int lane = 0)
    {
        var sel = new Selection();
        foreach (var p in s.Points) sel.Add(new LaserPointRef(lane, s, p));
        return sel;
    }

    [Test]
    public void SummaryShowsMixedAndSlamEnds()
    {
        var (_, s) = setup();
        var sum = LaserInspector.Of(all(s));
        Assert.That(sum.Anchors, Is.EqualTo(3));
        Assert.That(sum.Start.Mixed, Is.True);
        Assert.That(sum.AnySlam && !sum.AllSlams);
        Assert.That(sum.Landing.Value, Is.EqualTo(0.8), "the one slam's landing end");

        var one = new Selection();
        one.Add(new LaserSlamEndRef(0, s, s.Points[1], true));
        var single = LaserInspector.Of(one);
        Assert.That((single.Position.Value, single.Start.Value, single.Landing.Value), Is.EqualTo(((long?)1200, (double?)0.2, (double?)0.8)), "a selected slam end shows both ends of its slam");
    }

    [Test]
    public void EditsAreOneStepAndUndoable()
    {
        var (c, s) = setup();
        var stack = new CommandStack(c);
        var one = new Selection();
        one.Add(new LaserPointRef(0, s, s.Points[1]));
        stack.Execute(LaserInspector.SetLanding(c, one, 0.9)!);
        var n = c.Notes.Lasers[0].Single();
        Assert.That(n.Points[1].Vf, Is.EqualTo(0.9));
        stack.Undo();
        Assert.That(c.Notes.Lasers[0].Single(), Is.SameAs(s));

        stack.Execute(LaserInspector.SetLanding(c, one, null)!);
        Assert.That(c.Notes.Lasers[0].Single().SlamCount, Is.Zero, "slam removed, V kept");
        stack.Undo();

        stack.Execute(LaserInspector.SetStart(c, all(s), 0.3)!);
        Assert.That(c.Notes.Lasers[0].Single().Points.Select(p => p.V), Is.All.EqualTo(0.3));
        stack.Undo();

        stack.Execute(LaserInspector.SetType(c, all(s), LaserCurveType.Bezier)!);
        Assert.That(c.Notes.Lasers[0].Single().Path, Is.Not.Null, "a curve type makes a stored path");
        stack.Undo();

        stack.Execute(LaserInspector.SetWidth(c, all(s), 2)!);
        Assert.That(c.Notes.Lasers[0].Single().Width, Is.EqualTo(2));
        Assert.That(c.Notes.Lasers[0].Single().Points[1].V, Is.EqualTo(0.2), "values kept");
        stack.Undo();

        Assert.That(LaserInspector.SetStart(c, all(s), 1.5), Is.Null);
        Assert.That(LaserInspector.SetWidth(c, all(s), 1), Is.Null, "no change");
    }

    [Test]
    public void PositionMovesWithinNeighboursOnly()
    {
        var (c, s) = setup();
        var mid = new Selection();
        mid.Add(new LaserPointRef(0, s, s.Points[1]));
        LaserInspector.SetPosition(c, mid, 1100)!.Execute(c);
        Assert.That(c.Notes.Lasers[0].Single().Points[1].Ry, Is.EqualTo(140));
        var (c2, s2) = setup();
        var mid2 = new Selection();
        mid2.Add(new LaserPointRef(0, s2, s2.Points[1]));
        Assert.That(LaserInspector.SetPosition(c2, mid2, 2000), Is.Null, "past the next anchor is refused, not clamped");
    }

    [Test]
    public void LoneSlamKeepsItsSlam()
    {
        var c = new Chart();
        var s = new LaserSection(0, new[] { new LaserPoint(0, 0, 1) });
        c.Notes.Lasers[1].Add(s);
        var sel = all(s, 1);
        Assert.That(LaserInspector.SetLanding(c, sel, null), Is.Null);
        Assert.That(LaserInspector.SetLanding(c, sel, 0), Is.Null, "landing = start would leave a single plain point");
        LaserInspector.SetLanding(c, sel, 0.5)!.Execute(c);
        Assert.That(c.Notes.Lasers[1].Single().Points.Single().Vf, Is.EqualTo(0.5));
    }
}
