using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Vox;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class VoxReaderTests
{
    // Synthetic file (no game data): v13 layout, CRLF, a 6/8 measure, a stop, slams and a wide laser.
    private const string V13 =
        "//====================================\r\n" +
        "#FORMAT VERSION\r\n13\r\n#END\r\n\r\n" +
        "#BEAT INFO\r\n001,01,00\t4\t4\r\n003,01,00\t6\t8\r\n004,01,00\t4\t4\r\n#END\r\n\r\n" +
        "#BPM INFO\r\n001,01,00\t150.00\t4\r\n002,01,00\t150.00\t4-\r\n002,03,00\t200.00\t4\r\n#END\r\n\r\n" +
        "#END POSITION\r\n006,01,00\r\n#END\r\n\r\n" +
        "#TRACK1\r\n" +
        "001,01,00\t0.000000\t1\t0\t0\t1\t0\t0\t0\t0\r\n" +
        "001,02,00\t0.500000\t0\t0\t0\t1\t0\t0\t0\t0\r\n" +
        "001,02,00\t1.000000\t0\t0\t0\t1\t0\t0\t0\t0\r\n" +   // slam 0.5 -> 1.0
        "001,04,00\t1.000000\t2\t0\t0\t1\t0\t0\t0\t0\r\n" +
        "#END\r\n" +
        "#TRACK2\r\n002,01,00\t0\t0\t12\r\n#END\r\n" +
        "#TRACK3\r\n001,01,00\t0\t0\t12\r\n001,03,24\t96\t2\t12\r\n#END\r\n" +
        "#TRACK4\r\n003,02,12\t0\t0\r\n#END\r\n" + // 6/8: beat 2 = 120 pulses, +12 ticks = 60 pulses
        "#TRACK5\r\n#END\r\n#TRACK6\r\n#END\r\n" +
        "#TRACK7\r\n004,01,00\t48\t5\t12\r\n#END\r\n" +
        "#TRACK8\r\n" +
        "005,01,00\t1.000000\t1\t0\t0\t2\t0\t0\t0\t0\r\n" +
        "005,01,00\t0.000000\t2\t0\t0\t2\t0\t0\t0\t0\r\n" +   // one-tick section = a lone slam
        "#END\r\n";

    [Test]
    public void ReadsTimingWith192PerDenominatorBeats()
    {
        var c = VoxReader.Read(V13);
        Assert.That(c.Beat.TimeSignatures.Select(s => (s.MeasureIndex, s.Numerator, s.Denominator)),
                    Is.EqualTo(new[] { (0, 4, 4), (2, 6, 8), (3, 4, 4) }));
        Assert.That(c.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 150.0), (960L, 150.0), (1440L, 200.0) }));
        Assert.That(c.Beat.Stops.Select(s => (s.Y, s.Length)), Is.EqualTo(new[] { (960L, 480L) }));
        // measures: 960, 960, 720 (6/8), 960, ... → measure 6 starts at 960+960+720+960+960
        Assert.That(c.EndPulse, Is.EqualTo(960 + 960 + 720 + 960 + 960));
        // 6/8 measure 3 starts at 1920; beat 2 (eighth = 120 pulses) + 12 ticks (60 pulses)
        Assert.That(c.Notes.Bt[1].Single().Y, Is.EqualTo(1920 + 120 + 60));
    }

    [Test]
    public void HonoursBeatResolution()
    {
        // 480 ticks per quarter: offset 240 = an eighth (120 pulses), hold 2400 ticks = 5 quarters
        string text = string.Join("\r\n", new[]
        {
            "#FORMAT VERSION",
            "12",
            "#END",
            "#BEAT RESOLUTION",
            "480",
            "#END",
            "#BEAT INFO",
            "001,01,00\t4\t4",
            "#END",
            "#BPM INFO",
            "001,01,00\t120.00\t4",
            "#END",
            "#TRACK3",
            "001,02,240\t0\t0",
            "002,01,00\t2400\t2",
            "#END",
            "#TRACK1",
            "001,01,00\t0.000000\t1\t0\t0\t1\t0\t0\t0\t0",
            "001,01,432\t1.000000\t2\t0\t0\t1\t0\t0\t0\t0",
            "#END",
        }) + "\r\n";
        var c = VoxReader.Read(text);
        Assert.That(c.Notes.Bt[0].Select(n => (n.Y, n.Length)), Is.EqualTo(new[] { (240L + 120L, 0L), (960L, 1200L) }));
        Assert.That(c.Notes.Lasers[0].Single().Points[^1].Ry, Is.EqualTo(216), "432 ticks = 216 pulses");
    }

    [Test]
    public void ReadsButtonsAndHolds()
    {
        var c = VoxReader.Read(V13);
        Assert.That(c.Notes.Bt[0].Select(n => (n.Y, n.Length)), Is.EqualTo(new[] { (0L, 0L), (480L + 120L, 480L) }));
        Assert.That(c.Notes.Fx[0].Single().Y, Is.EqualTo(960));
        Assert.That(c.Notes.Fx[1].Single().Length, Is.EqualTo(240));
    }

    [Test]
    public void ReadsLasersSlamsAndWidth()
    {
        var c = VoxReader.Read(V13);
        var l = c.Notes.Lasers[0].Single();
        Assert.That(l.Width, Is.EqualTo(1));
        Assert.That(l.Points.Select(p => (p.Ry, p.V, p.Vf)),
                    Is.EqualTo(new (long, double, double?)[] { (0, 0.0, null), (240, 0.5, 1.0), (720, 1.0, null) }));
        var r = c.Notes.Lasers[1].Single();
        Assert.That(r.Width, Is.EqualTo(2));
        Assert.That(r.Points.Count, Is.EqualTo(1));
        Assert.That(r.Points[0].IsSlam && r.Points[0].V == 1.0 && r.Points[0].Vf == 0.0);
    }

    [Test]
    public void OldVersionsScaleLaserValuesFrom127()
    {
        string v10 = "#FORMAT VERSION\n10\n#END\n#BEAT INFO\n001,01,00\t4\t4\n#END\n#BPM INFO\n001,01,00\t120.0000\t4\n#END\n" +
                     "#TRACK1\n001,01,00\t0\t1\t0\t0\t1\t0\n002,01,00\t127\t2\t0\t0\t1\t0\n#END\n";
        var c = VoxReader.Read(v10);
        var s = c.Notes.Lasers[0].Single();
        Assert.That(s.Points.Select(p => p.V), Is.EqualTo(new[] { 0.0, 1.0 }));
        Assert.That(s.EndY, Is.EqualTo(960));
    }

    [TestCase("0001_x_1n.vox", 0)]
    [TestCase("0001_x_2a.vox", 1)]
    [TestCase("0001_x_3e.vox", 2)]
    [TestCase("0001_x_4i.vox", 3)]
    [TestCase("0001_x_5m.vox", 4)]
    [TestCase("chart.vox", null)]
    public void SlotFromSuffix(string file, int? slot) => Assert.That(VoxReader.SlotFromFileName(file), Is.EqualTo(slot));

    /// <summary>Every official file of a local install parses (skipped without one).</summary>
    [Test]
    public void OfficialFilesParse()
    {
        const string music = @"D:\SDVX-RIP\SOUND VOLTEX NABLA\data\music";
        if (!Directory.Exists(music)) Assert.Ignore("no local game install");
        var files = Directory.EnumerateDirectories(music).SelectMany(d => Directory.EnumerateFiles(d, "*.vox")).ToList();
        int failed = 0;
        var noNotes = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.ForEach(files, f =>
        {
            try
            {
                var c = VoxReader.ReadFile(f);
                if (!c.Notes.Bt.Concat(c.Notes.Fx).Any(l => l.Count > 0) && !c.Notes.Lasers.Any(l => l.Count > 0))
                    noNotes.Add(Path.GetRelativePath(music, f));
            }
            catch { Interlocked.Increment(ref failed); }
        });
        TestContext.Out.WriteLine($"{files.Count} files, {failed} failed, {noNotes.Count} without any note: {string.Join(", ", noNotes)}");
        Assert.That(failed, Is.Zero);
        Assert.That(noNotes, Is.Empty);
    }
}
