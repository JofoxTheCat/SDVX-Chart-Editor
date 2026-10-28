using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class SelectionOpsTests
{
    private static Chart Sample()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(0));               // A chip
        c.Notes.Bt[1].Add(new Note(240, 480));        // B hold 240..720
        c.Notes.Fx[0].Add(new Note(480, 240));        // FX-L hold 480..720
        c.Notes.Lasers[0].Add(new LaserSection(960, new[]
        {
            new LaserPoint(0, 0.0, null, new Curve(0.2, 0.9)),
            new LaserPoint(240, 1.0, 0.25),
            new LaserPoint(480, 0.5),
        }));
        return c;
    }

    private static Selection SelectAll(Chart c)
    {
        var s = new Selection();
        s.SelectRange(c, 0, 10_000);
        return s;
    }

    private static void RunAndUndo(Chart chart, IEditCommand? cmd, Action check)
    {
        Assert.That(cmd, Is.Not.Null);
        var before = ModelSnapshot.Of(chart);
        var stack = new CommandStack(chart);
        stack.Execute(cmd!);
        check();
        stack.Undo();
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(chart), cmd!.Description + " undo");
        stack.Redo();
        check();
    }

    [Test]
    public void OverlapRuleDefinition()
    {
        Assert.That(OverlapRule.Conflicts(100, 0, 100, 0), Is.True, "two chips on one pulse");
        Assert.That(OverlapRule.Conflicts(100, 0, 0, 200), Is.True, "chip inside a hold");
        Assert.That(OverlapRule.Conflicts(200, 0, 0, 200), Is.False, "chip on the hold's end pulse");
        Assert.That(OverlapRule.Conflicts(0, 200, 200, 100), Is.False, "touching holds");
        Assert.That(OverlapRule.Conflicts(0, 201, 200, 100), Is.True, "overlapping holds");
        Assert.That(OverlapRule.SectionsConflict(0, 240, 240, 480), Is.True, "laser sections sharing a pulse");
        Assert.That(OverlapRule.SectionsConflict(0, 240, 241, 480), Is.False);
    }

    [Test]
    public void MirrorSwapsLanesAndLaserValues()
    {
        var c = Sample();
        RunAndUndo(c, SelectionOps.Mirror(c, SelectAll(c)), () =>
        {
            Assert.That(c.Notes.Bt[3].Single().Y, Is.EqualTo(0));
            Assert.That(c.Notes.Bt[2].Single().Length, Is.EqualTo(480));
            Assert.That(c.Notes.Fx[1].Single().Y, Is.EqualTo(480));
            Assert.That(c.Notes.Lasers[0], Is.Empty);
            var s = c.Notes.Lasers[1].Single();
            Assert.That(s.Points.Select(p => p.V), Is.EqualTo(new[] { 1.0, 0.0, 0.5 }));
            Assert.That(s.Points[1].Vf, Is.EqualTo(0.75));
            Assert.That(s.Points[0].Curve, Is.EqualTo(new Curve(0.2, 0.9)), "eased fraction is invariant under v -> 1-v");
        });
    }

    [Test]
    public void MirrorTwiceIsIdentity()
    {
        var c = Sample();
        var before = ModelSnapshot.Of(c);
        var stack = new CommandStack(c);
        stack.Execute(SelectionOps.Mirror(c, SelectAll(c))!);
        stack.Execute(SelectionOps.Mirror(c, SelectAll(c))!);
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(c), "mirror twice");
    }

    [Test]
    public void ReverseKeepsHoldExtentsAndFlipsLasers()
    {
        var c = Sample();
        var sel = SelectAll(c);
        Assert.That(SelectionOps.Span(sel), Is.EqualTo(((long)0, (long)1440)));
        RunAndUndo(c, SelectionOps.Reverse(c, sel), () =>
        {
            Assert.That(c.Notes.Bt[0].Single().Y, Is.EqualTo(1440));
            var hold = c.Notes.Bt[1].Single();
            Assert.That((hold.Y, hold.Length), Is.EqualTo((720L, 480L)), "[240,720] -> [720,1200]");
            Assert.That(c.Notes.Fx[0].Single().Y, Is.EqualTo(720));
            var s = c.Notes.Lasers[0].Single();
            Assert.That(s.Y, Is.EqualTo(0));
            Assert.That(s.Points.Select(p => p.Ry), Is.EqualTo(new long[] { 0, 240, 480 }));
            Assert.That(s.Points.Select(p => p.V), Is.EqualTo(new[] { 0.5, 0.25, 0.0 }));
            Assert.That(s.Points[1].Vf, Is.EqualTo(1.0), "slam 1 -> 0.25 becomes 0.25 -> 1");
            Assert.That(s.Points[1].Curve!.Value.A, Is.EqualTo(0.8).Within(1e-12), "curve (a,b) -> (1-a,1-b) on the reversed segment");
            Assert.That(s.Points[1].Curve!.Value.B, Is.EqualTo(0.1).Within(1e-12));
            Assert.That(s.Points[0].Curve, Is.Null);
        });
        // reversing twice restores the laser exactly
        var twice = SelectionOps.Reversed(SelectionOps.Reversed(Sample().Notes.Lasers[0][0], 1440), 1440);
        ModelSnapshot.AssertEqual(Sample().Notes.Lasers[0][0], twice, "reverse twice");
    }

    [Test]
    public void MoveClampsAndRespectsOverlaps()
    {
        var c = Sample();
        var sel = new Selection();
        sel.Add(new NoteRef(NoteKind.Bt, 0, c.Notes.Bt[0][0]));
        // lane +5 is clamped to +3 (BT D), pulse -100 to 0 (already at 0): only the lane changes
        RunAndUndo(c, SelectionOps.Move(c, sel, -100, 5), () => Assert.That(c.Notes.Bt[3].Single().Y, Is.EqualTo(0)));

        // fresh chart: selection references follow the model, a stale reference would point at the wrong lane
        c = Sample();
        sel = new Selection();
        sel.Add(new NoteRef(NoteKind.Bt, 0, c.Notes.Bt[0][0]));
        // moving the chip into B's hold is refused
        Assert.That(SelectionOps.Move(c, sel, 300, 1), Is.Null);
        // onto the hold's end pulse is fine
        RunAndUndo(c, SelectionOps.Move(c, sel, 720, 1), () => Assert.That(c.Notes.Bt[1].Select(n => n.Y), Is.EqualTo(new long[] { 240, 720 })));
        // a no-op move yields no command
        Assert.That(SelectionOps.Move(c, sel, 0, -1), Is.Null);
    }

    [Test]
    public void MoveTakesWholeLaserSections()
    {
        var c = Sample();
        var sel = new Selection();
        var s = c.Notes.Lasers[0][0];
        sel.Add(new LaserPointRef(0, s, s.Points[1]));
        RunAndUndo(c, SelectionOps.Move(c, sel, 120), () => Assert.That(c.Notes.Lasers[0].Single().Y, Is.EqualTo(1080)));
        c.Notes.Lasers[0].Add(new LaserSection(1500, new[] { new LaserPoint(0, 0) }));
        Assert.That(SelectionOps.Move(c, sel, 60), Is.Null, "would touch the section at 1500");
    }

    [Test]
    public void DuplicateAndPaste()
    {
        var c = Sample();
        var sel = SelectAll(c);
        RunAndUndo(c, SelectionOps.Duplicate(c, sel, 1920), () =>
        {
            Assert.That(c.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new long[] { 0, 1920 }));
            Assert.That(c.Notes.Lasers[0].Select(l => l.Y), Is.EqualTo(new long[] { 960, 2880 }));
        });
        Assert.That(SelectionOps.Duplicate(c, sel, 120), Is.Null, "would overlap the original");
        var clip = ClipboardData.FromSelection(sel);
        RunAndUndo(c, SelectionOps.Paste(c, clip, 4000, mirror: true), () =>
            Assert.That(c.Notes.Bt[3].Select(n => n.Y), Is.EqualTo(new long[] { 4000 })));
    }

    [Test]
    public void DeleteSelection()
    {
        var c = Sample();
        var sel = new Selection();
        sel.Add(new NoteRef(NoteKind.Bt, 1, c.Notes.Bt[1][0]));
        var s = c.Notes.Lasers[0][0];
        sel.Add(new LaserPointRef(0, s, s.Points[0]));
        RunAndUndo(c, SelectionOps.Delete(sel), () =>
        {
            Assert.That(c.Notes.Bt[1], Is.Empty);
            Assert.That(c.Notes.Lasers[0].Single().Y, Is.EqualTo(1200), "first point removed: section starts at the next one");
            Assert.That(c.Notes.Lasers[0].Single().Points, Has.Count.EqualTo(2));
        });
        var all = SelectAll(c);
        RunAndUndo(c, SelectionOps.Delete(all), () =>
        {
            Assert.That(c.Notes.Bt.Sum(l => l.Count) + c.Notes.Fx.Sum(l => l.Count), Is.EqualTo(0));
            Assert.That(c.Notes.Lasers[0], Is.Empty);
        });
    }
}
