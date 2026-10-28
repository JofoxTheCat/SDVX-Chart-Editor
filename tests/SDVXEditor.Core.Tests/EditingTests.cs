using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class EditingTests
{
    private static Chart NewChart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[0].Add(new Note(480));
        c.Notes.Fx[1].Add(new Note(240, 240) { Effects = { new FxEffectEvent("gate") } });
        c.Notes.Lasers[0].Add(new LaserSection(960, new[] { new LaserPoint(0, 0), new LaserPoint(240, 1) }));
        return c;
    }

    [Test]
    public void AddRemoveMoveUndoRedo()
    {
        var chart = NewChart();
        var before = ModelSnapshot.Of(chart);
        var stack = new CommandStack(chart);
        int changes = 0;
        stack.Changed += () => changes++;

        var chip = new NoteRef(NoteKind.Bt, 2, new Note(120));
        stack.Execute(new AddNotesCommand(new[] { chip }));
        Assert.That(chart.Notes.Bt[2], Has.Count.EqualTo(1));

        var first = new NoteRef(NoteKind.Bt, 0, chart.Notes.Bt[0][0]);
        stack.Execute(new MoveNotesCommand(new[] { first, chip }, 600, 1));
        Assert.That(chart.Notes.Bt[1].Select(n => n.Y), Is.EqualTo(new long[] { 600 }));
        Assert.That(chart.Notes.Bt[3].Select(n => n.Y), Is.EqualTo(new long[] { 720 }));
        Assert.That(chart.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new long[] { 480 }));

        var fx = new NoteRef(NoteKind.Fx, 1, chart.Notes.Fx[1][0]);
        stack.Execute(new MoveNotesCommand(new[] { fx }, 0, -1));
        Assert.That(chart.Notes.Fx[0].Single().Effects.Single().Name, Is.EqualTo("gate"), "effects move with the note");

        stack.Execute(new RemoveNotesCommand(new[] { new NoteRef(NoteKind.Bt, 0, chart.Notes.Bt[0][0]) }));
        Assert.That(chart.Notes.Bt[0], Is.Empty);
        Assert.That(stack.IsDirty, Is.True);

        while (stack.Undo()) { }
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(chart), "after undoing everything");
        Assert.That(stack.IsDirty, Is.False);
        while (stack.Redo()) { }
        Assert.That(chart.Notes.Bt[1].Single().Y, Is.EqualTo(600));
        Assert.That(chart.Notes.Fx[0], Has.Count.EqualTo(1));
        Assert.That(changes, Is.EqualTo(4 + 4 + 4));
    }

    [Test]
    public void MoveRejectsMissingLanes()
        => Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MoveNotesCommand(new[] { new NoteRef(NoteKind.Fx, 1, new Note(0)) }, 0, 1));

    [Test]
    public void LaserPointsRebaseTheSection()
    {
        var chart = NewChart();
        var stack = new CommandStack(chart);
        var section = chart.Notes.Lasers[0][0];
        var before = ModelSnapshot.Of(chart);

        stack.Execute(new AddLaserPointCommand(section, 840, new LaserPoint { V = 0.5 }));
        Assert.That(section.Y, Is.EqualTo(840));
        Assert.That(section.Points.Select(p => p.Ry), Is.EqualTo(new long[] { 0, 120, 360 }));

        stack.Execute(new AddLaserPointCommand(section, 1080, new LaserPoint { V = 0.25, Vf = 0.75 }));
        Assert.That(section.Points.Select(p => p.Ry), Is.EqualTo(new long[] { 0, 120, 240, 360 }));
        Assert.Throws<InvalidOperationException>(() => stack.Execute(new AddLaserPointCommand(section, 1080, new LaserPoint())));

        stack.Execute(new RemoveLaserPointCommand(section, section.Points[0]));
        Assert.That(section.Y, Is.EqualTo(960));
        Assert.That(section.Points.Select(p => p.Ry), Is.EqualTo(new long[] { 0, 120, 240 }));

        while (stack.Undo()) { }
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(chart), "laser undo");
    }

    [Test]
    public void TransactionsGroupIntoOneStep()
    {
        var chart = NewChart();
        var stack = new CommandStack(chart);
        var note = new NoteRef(NoteKind.Bt, 0, chart.Notes.Bt[0][1]);
        stack.BeginTransaction("Drag");
        for (int i = 0; i < 5; i++) stack.Execute(new MoveNotesCommand(new[] { note }, 10));
        stack.Commit();
        Assert.That(note.Note.Y, Is.EqualTo(530));
        Assert.That(stack.History, Is.EqualTo(new[] { "Drag" }));
        stack.Undo();
        Assert.That(note.Note.Y, Is.EqualTo(480));

        stack.BeginTransaction("Cancelled drag");
        stack.Execute(new MoveNotesCommand(new[] { note }, 100));
        stack.Rollback();
        Assert.That(note.Note.Y, Is.EqualTo(480));
        Assert.That(stack.CanUndo, Is.False);
    }

    [Test]
    public void MetaChange()
    {
        var chart = NewChart();
        var stack = new CommandStack(chart);
        stack.Execute(MetaCommands.Set("Title", m => m.Title, (m, v) => m.Title = v, "New title"));
        stack.Execute(MetaCommands.Set("Level", m => m.Level, (m, v) => m.Level = v, 17.5));
        Assert.That(chart.Meta.Title, Is.EqualTo("New title"));
        Assert.That(stack.UndoDescription, Is.EqualTo("Change Level"));
        stack.Undo();
        stack.Undo();
        Assert.That(chart.Meta.Title, Is.EqualTo(""));
        Assert.That(chart.Meta.Level, Is.EqualTo(1));
    }

    [Test]
    public void SelectionCopyPasteMirror()
    {
        var chart = NewChart();
        var selection = new Selection();
        selection.SelectRange(chart, 0, 1000);
        Assert.That(selection.Notes, Has.Count.EqualTo(3));
        Assert.That(selection.LaserPoints, Has.Count.EqualTo(1));
        Assert.That(selection.StartPulse, Is.EqualTo(0));
        Assert.That(selection.EndPulse, Is.EqualTo(960));

        var clip = ClipboardData.FromSelection(selection);
        Assert.That(clip.Length, Is.EqualTo(1200));
        var stack = new CommandStack(chart);
        stack.Execute(clip.PasteAt(1920, mirror: true));
        Assert.That(chart.Notes.Bt[3].Select(n => n.Y), Is.EqualTo(new long[] { 1920, 2400 }));
        Assert.That(chart.Notes.Fx[0].Single().Y, Is.EqualTo(2160));
        var pasted = chart.Notes.Lasers[1].Single();
        Assert.That(pasted.Y, Is.EqualTo(2880));
        Assert.That(pasted.Points.Select(p => p.V), Is.EqualTo(new[] { 1.0, 0.0 }));
        // the original is untouched
        Assert.That(chart.Notes.Lasers[0].Single().Points.Select(p => p.V), Is.EqualTo(new[] { 0.0, 1.0 }));
        stack.Undo();
        Assert.That(chart.Notes.Lasers[1], Is.Empty);
        Assert.That(chart.Notes.Bt[3], Is.Empty);
    }

    [Test]
    public void SetNoteLengthChipHoldRoundTrip()
    {
        var chart = NewChart();
        var before = ModelSnapshot.Of(chart);
        var stack = new CommandStack(chart);

        // chip -> hold keeps the note object and its start; its key sound goes (chips only)
        var chip = new NoteRef(NoteKind.Bt, 0, chart.Notes.Bt[0][0]);
        Assert.That(OverlapRule.CanResize(chart, chip, 240), Is.True);
        stack.Execute(new SetNoteLengthCommand(chip, 240));
        Assert.That(chart.Notes.Bt[0][0], Is.SameAs(chip.Note));
        Assert.That((chip.Note.Y, chip.Note.Length), Is.EqualTo((0L, 240L)));
        Assert.That(stack.UndoDescription, Is.EqualTo("Chip to hold"));

        // a hold that would swallow the next chip (480) is refused; ending on it (touching) is fine
        Assert.That(OverlapRule.CanResize(chart, chip, 481), Is.False);
        Assert.That(OverlapRule.CanResize(chart, chip, 480), Is.True);

        // FX hold -> chip drops its effects, undo brings them back
        var fx = new NoteRef(NoteKind.Fx, 1, chart.Notes.Fx[1][0]);
        stack.Execute(new SetNoteLengthCommand(fx, 0));
        Assert.That(fx.Note.IsHold, Is.False);
        Assert.That(fx.Note.Effects, Is.Empty);
        Assert.That(stack.UndoDescription, Is.EqualTo("Hold to chip"));
        stack.Undo();
        Assert.That(fx.Note.Effects.Select(e => e.Name), Is.EqualTo(new[] { "gate" }));

        // shortening keeps effects before the new end only
        fx.Note.Effects.Add(new FxEffectEvent("flanger", 200));
        var fxBefore = ModelSnapshot.Of(chart);
        stack.Execute(new SetNoteLengthCommand(fx, 120));
        Assert.That(fx.Note.Effects.Select(e => e.Name), Is.EqualTo(new[] { "gate" }));
        Assert.That(stack.UndoDescription, Is.EqualTo("Hold length"));
        stack.Undo();
        Assert.That(ModelSnapshot.FirstDifference(fxBefore, ModelSnapshot.Of(chart)), Is.Null);
        fx.Note.Effects.RemoveAt(1);

        while (stack.Undo()) { }
        Assert.That(ModelSnapshot.FirstDifference(before, ModelSnapshot.Of(chart)), Is.Null);
    }
}
