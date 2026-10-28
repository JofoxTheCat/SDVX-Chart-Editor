using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Tests;

public class NewDifficultyTests
{
    private static Chart sample()
    {
        var c = new Chart();
        c.Meta.Title = "Song";
        c.Meta.Artist = "Artist";
        c.Meta.DifficultySlot = 2;
        c.Meta.Level = 17.5;
        c.Beat.Bpm.Add(new BpmChange(0, 180));
        c.Beat.ScrollSpeed.Add(new GraphPoint(960, 2));
        c.Audio.Bgm.Filename = "song.ogg";
        c.Notes.Bt[0].Add(new Note(480, 0));
        c.Notes.Fx[1].Add(new Note(960, 240));
        c.Editor.Bookmarks.Add(new PulseValue<Bookmark>(1920, new Bookmark("Part B")));
        return c;
    }

    [Test]
    public void FreeSlotsSkipTheUsedOnes()
    {
        Assert.That(NewDifficulty.FreeSlots(new[] { 0, 2, 3 }), Is.EqualTo(new[] { 1, 4 }));
        Assert.That(NewDifficulty.FreeSlots(new[] { 0, 1, 2, 3, 4 }), Is.Empty);
    }

    [Test]
    public void CopyKeepsNotesAndIsIndependent()
    {
        var src = sample();
        var copy = NewDifficulty.Create(src, 4, copyNotes: true);

        Assert.That(copy.Meta.DifficultySlot, Is.EqualTo(4));
        Assert.That(copy.Notes.Bt[0], Has.Count.EqualTo(1));
        Assert.That(copy.Notes.Fx[1].Single().Length, Is.EqualTo(240));
        Assert.That(copy.Meta.Level, Is.EqualTo(17.5));

        copy.Notes.Bt[0].Clear();
        Assert.That(src.Notes.Bt[0], Has.Count.EqualTo(1), "the copy is deep");
    }

    [Test]
    public void EmptyKeepsTimingAndMetadataOnly()
    {
        var empty = NewDifficulty.Create(sample(), 0, copyNotes: false);

        Assert.That(empty.Meta.DifficultySlot, Is.EqualTo(0));
        Assert.That(empty.Meta.Title, Is.EqualTo("Song"));
        Assert.That(empty.Audio.Bgm.Filename, Is.EqualTo("song.ogg"));
        Assert.That(empty.Beat.Bpm.Single().Bpm, Is.EqualTo(180));
        Assert.That(empty.Beat.ScrollSpeed, Has.Count.EqualTo(1));
        Assert.That(empty.Editor.Bookmarks, Has.Count.EqualTo(1));
        Assert.That(empty.Notes.Bt.Concat(empty.Notes.Fx).Sum(l => l.Count), Is.Zero);
        Assert.That(empty.Notes.Lasers.Sum(l => l.Count), Is.Zero);
    }

    [Test]
    public void FileNameResolvesBackToTheSlot()
    {
        for (int slot = 0; slot < 5; slot++)
        {
            string name = NewDifficulty.FileName(slot, new[] { "exh.ksh", "mxm.kson", "mxm_2.kson" });
            Assert.That(Difficulty.FromFileName(name, out _), Is.EqualTo(slot), name);
        }

        Assert.That(NewDifficulty.FileName(2, new[] { "exh.ksh" }), Is.EqualTo("exh_2.kson"));
        Assert.That(NewDifficulty.FileName(4, new[] { "mxm.kson", "mxm_2.kson" }), Is.EqualTo("mxm_3.kson"));
        Assert.That(NewDifficulty.FileName(0, Array.Empty<string>()), Is.EqualTo("nov.kson"));
    }
}
