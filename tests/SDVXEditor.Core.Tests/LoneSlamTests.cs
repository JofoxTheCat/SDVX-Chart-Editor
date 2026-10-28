using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Validation;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// A laser section that is one slam and nothing else (official charts: 118,940 of them in 8,602 .vox). No tail is added
/// anywhere; every format carries it; deleting one of its ends removes the section (a single plain point is no laser).
/// </summary>
public class LoneSlamTests
{
    private static Chart withLoneSlam(int width = 1)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(1920)); // something after it, so the chart end lies behind the slam
        c.Notes.Lasers[0].Add(new LaserSection(960, new[] { new LaserPoint(0, 0.2, 0.8) }, width));
        return c;
    }

    [Test]
    public void KsonRoundTrip()
    {
        var c = withLoneSlam();
        string kson = KsonWriter.Write(c);
        Assert.That(kson.Replace(" ", "").Replace("\n", "").Replace("\r", ""), Does.Contain("[0,[0.2,0.8]]"));
        var back = KsonReader.Read(kson).Notes.Lasers[0].Single();
        Assert.That(back.Points.Select(p => (p.Ry, p.V, p.Vf)), Is.EqualTo(new[] { (0L, 0.2, (double?)0.8) }));
        Assert.That(back.Y, Is.EqualTo(960));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void KshRoundTrip(int width)
    {
        var c = withLoneSlam(width);
        string ksh = KshWriter.Write(c);
        var back = KshReader.Read(ksh).Notes.Lasers[0];
        Assert.That(back, Has.Count.EqualTo(1), ksh);
        Assert.That(back[0].Points.Select(p => (p.Ry, p.V, p.Vf)), Is.EqualTo(new[] { (0L, 0.2, (double?)0.8) }), ksh);
        Assert.That((back[0].Y, back[0].Width), Is.EqualTo((960L, width)));
    }

    [Test]
    public void PlayableAndClean()
    {
        var c = withLoneSlam();
        var playable = new PlayableChart(c);
        Assert.That(playable.Slams[0], Has.Count.EqualTo(1));
        Assert.That(GameplayJudge.SimulateAutoplay(playable).Errors, Is.Zero);
        Assert.That(ChartChecker.Run(c).Issues.Where(i => i.RuleId.StartsWith("laser.")), Is.Empty, "a lone slam is valid laser data");
    }

    [Test]
    public void DeletingOneEndRemovesTheSection()
    {
        var c = withLoneSlam();
        var s = c.Notes.Lasers[0][0];
        var cmd = LaserPathCommands.DeleteSlamEnd(c, 0, s, 0, true)!;
        Assert.That(cmd, Is.InstanceOf<RemoveLaserSectionCommand>());

        var sel = new Selection();
        sel.Add(new LaserSlamEndRef(0, s, s.Points[0], false));
        var stack = new CommandStack(c);
        stack.Execute(SelectionOps.Delete(sel)!);
        Assert.That(c.Notes.Lasers[0], Is.Empty);
        stack.Undo();
        Assert.That(c.Notes.Lasers[0].Single(), Is.SameAs(s));
    }

    [Test]
    public void MovingTheSlamKeepsItAlone()
    {
        var c = withLoneSlam();
        var s = c.Notes.Lasers[0][0];
        var moved = LaserPathCommands.Move(c, 0, s, 0, 1200, 0.3)!;
        moved.Execute(c);
        var n = c.Notes.Lasers[0].Single();
        Assert.That(n.Points.Select(p => (p.Ry, p.V, p.Vf)), Is.EqualTo(new[] { (0L, 0.3, (double?)0.8) }));
        Assert.That(n.Y, Is.EqualTo(1200));
    }
}
