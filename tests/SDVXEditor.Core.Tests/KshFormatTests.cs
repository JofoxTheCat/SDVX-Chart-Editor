using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class KshFormatTests
{
    private static IEnumerable<TestCaseData> LoadCases() => RealCharts.Cases(".ksh", "RealChartLoads");
    private static IEnumerable<TestCaseData> RoundTripCases() => RealCharts.Cases(".ksh", "RoundTripIsLossless");

    [TestCaseSource(nameof(LoadCases))]
    public void RealChartLoads(string path)
    {
        RealCharts.RequireFile(path);
        var c = KshReader.ReadFile(path);
        Assert.That(c.Notes.Bt.Sum(l => l.Count) + c.Notes.Fx.Sum(l => l.Count), Is.GreaterThan(0));
        Assert.That(c.Beat.Bpm, Is.Not.Empty);
        Assert.That(c.Meta.Title, Is.Not.Empty);
        // every laser section must have at least one point, sorted, starting at ry 0
        foreach (var lane in c.Notes.Lasers)
            foreach (var s in lane)
            {
                Assert.That(s.Points, Is.Not.Empty);
                Assert.That(s.Points[0].Ry, Is.EqualTo(0));
                Assert.That(s.Points.Select(p => p.Ry), Is.Ordered.Ascending);
            }
        // holds never overlap within a lane
        foreach (var lane in c.Notes.Bt.Concat(c.Notes.Fx))
            for (int i = 1; i < lane.Count; i++)
                Assert.That(lane[i].Y, Is.GreaterThanOrEqualTo(lane[i - 1].EndY), $"overlap at {lane[i].Y}");
        TestContext.Out.WriteLine(ChartFacts.Describe(c));
    }

    [TestCaseSource(nameof(RoundTripCases))]
    public void RoundTripIsLossless(string path)
    {
        RealCharts.RequireFile(path);
        var a = KshReader.ReadFile(path);
        string text = KshWriter.Write(a);
        var b = KshReader.Read(text);
        ModelSnapshot.AssertEqual(a.Notes, b.Notes, "notes");
        ModelSnapshot.AssertEqual(a.Beat, b.Beat, "timing");
        ModelSnapshot.AssertEqual(a, b, "whole chart");
        // the second generation must be byte-identical (writer is deterministic and idempotent)
        Assert.That(KshWriter.Write(b), Is.EqualTo(text));
    }

    [Test]
    public void SlamIsInferredOnTheFirstLine()
    {
        // 16 lines per measure (60 pulses); "0" then "o" 1/32 apart is a slam at the first line.
        string ksh = "title=t\r\nartist=a\r\nt=120\r\n--\r\n" +
                     "beat=4/4\r\n" +
                     "0000|00|0-\r\n0000|00|o-\r\n0000|00|:-\r\n0000|00|o-\r\n" +
                     string.Concat(Enumerable.Repeat("0000|00|--\r\n", 28)) + "--\r\n";
        var c = KshReader.Read(ksh);
        var s = c.Notes.Lasers[0].Single();
        Assert.That(s.Points, Has.Count.EqualTo(2));
        Assert.That(s.Points[0].Ry, Is.EqualTo(0));
        Assert.That(s.Points[0].V, Is.EqualTo(0));
        Assert.That(s.Points[0].Vf, Is.EqualTo(1));
        Assert.That(s.Points[1].Ry, Is.EqualTo(90));
    }

    /// <summary>
    /// The four patterns of ABYSS MOTION lane R (73680/79680) in one 192-line measure (5 pulses per line):
    /// a curved point whose 1/32 successor starts a 5-pulse run, the run itself, a real 1/32 slam right
    /// after the run, and a lone pair 20 pulses apart. Only the 1/32 slam is a slam; the old pair rule
    /// (RecognizeDenseMovement = false, = ksh2vox/libkson) reads 5 slams.
    /// </summary>
    [Test]
    public void DenselySampledMovementIsNotReadAsSlams()
    {
        var lines = new string[192];
        for (int i = 0; i < 192; i++) lines[i] = "0000|00|-:";
        lines[0] = "0000|00|-0";                                            // pulse 0, curved
        string run = "OQSTUWXYYZaaab";                                        // pulse 30..95, every 5
        for (int i = 0; i < run.Length; i++) lines[6 + i] = "0000|00|-" + run[i];
        lines[20] = "0000|00|-b";                                            // pulse 100, held
        lines[26] = "0000|00|-C";                                            // pulse 130: slam b→C (1/32)
        lines[60] = "0000|00|-d";                                            // pulse 300
        lines[64] = "0000|00|-j";                                            // pulse 320: 20 pulses later
        lines[100] = "0000|00|-b";                                           // pulse 500
        for (int i = 101; i < 192; i++) lines[i] = "0000|00|--";
        string ksh = "title=t\r\nartist=a\r\nt=120\r\n--\r\nlaser_r_curve=0.3;0.6\r\n" + string.Join("\r\n", lines) + "\r\n--\r\n";

        var s = KshReader.Read(ksh).Notes.Lasers[1].Single();
        Assert.That(s.Points.Where(p => p.IsSlam).Select(p => (p.Ry, p.V, p.Vf)), Is.EqualTo(new[] { (100L, 37 / 50.0, (double?)(12 / 50.0)) }),
            "the 1/32 step b(100) → C(130) after the run is the only slam");
        // 0, the 14 run points, the slam at 100, 300, 320, 500
        Assert.That(s.Points.Select(p => p.Ry), Is.EqualTo(new long[] { 0, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 95, 100, 300, 320, 500 }));
        Assert.That(s.Points[0].Curve, Is.Not.Null);

        var old = KshReader.Read(ksh, new KshReadOptions { RecognizeDenseMovement = false }).Notes.Lasers[1].Single();
        Assert.That(old.SlamCount, Is.GreaterThan(1));

        // the model survives a ksh round trip unchanged
        ModelSnapshot.AssertEqual(s, KshReader.Read(KshWriter.Write(KshReader.Read(ksh))).Notes.Lasers[1].Single(), "round trip");
    }

    [Test]
    public void OptionsHoldsAndSpinsParse()
    {
        string ksh = "title=t\r\nartist=a\r\nt=120\r\nver=171\r\no=25\r\nm=song.ogg;song_f.ogg\r\n--\r\n" +
                     "beat=4/4\r\n" +
                     "zoom_bottom=10\r\nzoom_bottom=-10\r\ntilt=keep_bigger\r\nfx-l=Retrigger;16\r\n" +
                     "2010|10|--\r\n" +
                     "stop=96\r\n" +
                     "2000|10|0-@(192\r\n" +
                     "0000|00|o-\r\n" +
                     "t=240\r\n" +
                     "0000|00|--\r\n" +
                     "--\r\n" +
                     "beat=6/8\r\n0100|02|--\r\n--\r\n";
        var c = KshReader.Read(ksh);
        Assert.That(c.Audio.Bgm.OffsetMs, Is.EqualTo(25));
        Assert.That(c.Audio.Bgm.LegacyFpFilenames, Is.EqualTo(new[] { "song_f.ogg" }));
        Assert.That(c.Notes.Bt[0].Single().Length, Is.EqualTo(480));
        Assert.That(c.Notes.Bt[2].Single().Length, Is.EqualTo(0));
        Assert.That(c.Notes.Fx[0].Single().Length, Is.EqualTo(480));
        Assert.That(c.Notes.Fx[0].Single().Effects.Single().Params["wave_length"], Is.EqualTo("1/16"));
        Assert.That(c.Notes.Fx[0].Single().Effects.Single().Name, Is.EqualTo("retrigger"));
        var zb = c.Camera.Body.ZoomBottom.Single();
        Assert.That((zb.V, zb.Vf), Is.EqualTo((10.0, (double?)-10.0)));
        Assert.That(c.Camera.Tilt.Single().Value.Auto, Is.EqualTo(TiltMode.KeepBigger));
        Assert.That(c.Beat.Stops.Single().Y, Is.EqualTo(240));
        Assert.That(c.Beat.Stops.Single().Length, Is.EqualTo(480));
        Assert.That(c.Camera.Spin.Single().Y, Is.EqualTo(240));
        Assert.That(c.Camera.Spin.Single().Direction, Is.EqualTo(-1));
        Assert.That(c.Camera.Spin.Single().Length, Is.EqualTo(960));
        Assert.That(c.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 120.0), (720L, 240.0) }));
        Assert.That(c.Beat.TimeSignatures.Select(t => (t.MeasureIndex, t.Numerator, t.Denominator)),
            Is.EqualTo(new[] { (0, 4, 4), (1, 6, 8) }));
        Assert.That(c.Notes.Bt[1].Single().Y, Is.EqualTo(960));
        Assert.That(c.Notes.Fx[1].Single().Y, Is.EqualTo(960));
        Assert.That(c.EndPulse, Is.EqualTo(960 + 720));
        // round trip of the synthetic chart
        var b2 = KshReader.Read(KshWriter.Write(c));
        ModelSnapshot.AssertEqual(c, b2, "synthetic round trip");
    }

    [Test]
    public void ShiftJisWithoutBomIsDecoded()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var sjis = System.Text.Encoding.GetEncoding(932);
        byte[] bytes = sjis.GetBytes("title=じゃすと・べりー\r\nartist=x\r\nt=150\r\n--\r\n0000|00|--\r\n--\r\n");
        var c = KshReader.Read(KshReader.DecodeText(bytes));
        Assert.That(c.Meta.Title, Is.EqualTo("じゃすと・べりー"));
    }

    [Test]
    public void WriterUsesTheCoarsestGrid()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[0].Add(new Note(480));
        c.Notes.Bt[1].Add(new Note(960 + 320)); // a triplet in measure 2
        string text = KshWriter.Write(c);
        var measures = text.Split("\r\n--\r\n").Skip(1).ToArray();
        Assert.That(measures[0].Split("\r\n").Count(l => l.Contains('|')), Is.EqualTo(2));
        Assert.That(measures[1].Split("\r\n").Count(l => l.Contains('|')), Is.EqualTo(3));
    }
}
