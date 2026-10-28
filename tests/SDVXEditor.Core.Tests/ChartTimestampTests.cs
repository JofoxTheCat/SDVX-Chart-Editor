using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class ChartTimestampTests
{
    /// <summary>
    /// 120 BPM (a quarter = 500 ms, 240 pulses): every object kind, a slam, two laser sections meeting on one pulse
    /// (same time, different values) and a point one pulse from another.
    /// </summary>
    private static Chart Sample()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(240));            // BT-A chip at 500 ms
        c.Notes.Bt[3].Add(new Note(480, 240));       // BT-D hold at 1000 ms
        c.Notes.Fx[0].Add(new Note(480));            // FX-L chip at 1000 ms
        c.Notes.Fx[1].Add(new Note(720, 480));       // FX-R hold at 1500 ms
        c.Notes.Lasers[0].Add(new LaserSection(960, new[]
        {
            new LaserPoint(0, 0.25),                 // 2000 ms
            new LaserPoint(240, 0.25, 0.75),         // 2500 ms slam
            new LaserPoint(480, 1.0 / 3),            // 3000 ms, value with more than three decimals
        }));
        c.Notes.Lasers[0].Add(new LaserSection(1440, new[]
        {
            new LaserPoint(0, 0.5),                  // 3000 ms too: the next section starts where the first ends
            new LaserPoint(1, 0.6),                  // one pulse later (~2 ms)
        }));
        c.Notes.Lasers[1].Add(new LaserSection(60_000, new[] { new LaserPoint(0, 1), new LaserPoint(240, 0) })); // 125 s
        return c;
    }

    private static TimingMap Timing(Chart c) => TimingMap.FromChart(c);

    private static Selection All(Chart c)
    {
        var s = new Selection();
        s.SelectRange(c, 0, long.MaxValue);
        return s;
    }

    private static void AssertRoundTrip(Chart chart, Selection selection)
    {
        var timing = Timing(chart);
        string text = ChartTimestamp.Of(timing, selection).ToString();
        Assert.That(ChartTimestamp.TryParse(text, out var parsed), Is.True, text);
        Assert.That(parsed.Unparsed, Is.Empty, text);
        var r = parsed.Resolve(chart, timing);
        Assert.That(r.Missing, Is.Empty, text);
        Assert.That(r.Ambiguous, Is.Empty, text);
        Assert.That(r.Notes, Is.EquivalentTo(selection.Notes), text);
        Assert.That(r.LaserPoints, Is.EquivalentTo(selection.LaserPoints), text);
    }

    [TestCase(0.0, "00:00:000")]
    [TestCase(83456.0, "01:23:456")]
    [TestCase(83456.4, "01:23:456")]
    [TestCase(83456.5, "01:23:457")]
    [TestCase(6_000_000.0, "100:00:000")]
    [TestCase(-500.0, "-00:00:500")]
    public void FormatTime(double ms, string expected) => Assert.That(ChartTimestamp.FormatTime(ms), Is.EqualTo(expected));

    [TestCase("01:23:456", 83456)]
    [TestCase(" 1:23:456 ", 83456)]
    [TestCase("01:23.456", 83456)]
    [TestCase("01:23.5", 83500)]
    [TestCase("01:23", 83000)]
    [TestCase("83.456", 83456)]
    [TestCase("83,456", 83456)]
    [TestCase("83456", 83456)]
    [TestCase("83456ms", 83456)]
    [TestCase("120:00:000", 7_200_000)]
    [TestCase("-00:00:500", -500)]
    public void ParseTime(string text, double expected)
    {
        Assert.That(ChartTimestamp.TryParseTime(text, out double ms), Is.True);
        Assert.That(ms, Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("abc")]
    [TestCase("01:75:000")]
    [TestCase("1:2:3:4")]
    public void ParseTimeRejects(string text) => Assert.That(ChartTimestamp.TryParseTime(text, out _), Is.False);

    [Test]
    public void FormatsEveryObjectKind()
    {
        var c = Sample();
        var s = All(c);
        string text = ChartTimestamp.Of(Timing(c), s).ToString();
        Assert.That(text, Is.EqualTo(
            "00:00:500 (00:00:500|BT-A,00:01:000|BT-D hold,00:01:000|FX-L,00:01:500|FX-R hold," +
            "00:02:000|LASER-L 0.25,00:02:500|LASER-L 0.25>0.75,00:03:000|LASER-L 0.333,00:03:000|LASER-L 0.5," +
            "00:03:002|LASER-L 0.6,02:05:000|LASER-R 1,02:05:500|LASER-R 0) - "));
    }

    [Test]
    public void RoundTripWholeChart()
    {
        var c = Sample();
        AssertRoundTrip(c, All(c));
    }

    [TestCase(NoteKind.Bt, 0)]
    [TestCase(NoteKind.Bt, 3)]
    [TestCase(NoteKind.Fx, 0)]
    [TestCase(NoteKind.Fx, 1)]
    public void RoundTripEachNoteLane(NoteKind kind, int lane)
    {
        var c = Sample();
        var s = new Selection();
        s.Add(new NoteRef(kind, lane, c.Notes.Lane(kind, lane)[0]));
        AssertRoundTrip(c, s);
    }

    [TestCase(0, 0, 0)] // plain point
    [TestCase(0, 0, 1)] // slam
    [TestCase(0, 0, 2)] // 1/3, written with three decimals, shares its time with the next section's start
    [TestCase(0, 1, 0)] // the next section's start
    [TestCase(0, 1, 1)] // one pulse after it
    [TestCase(1, 0, 1)]
    public void RoundTripEachLaserPoint(int lane, int section, int point)
    {
        var c = Sample();
        var sec = c.Notes.Lasers[lane][section];
        var s = new Selection();
        s.Add(new LaserPointRef(lane, sec, sec.Points[point]));
        AssertRoundTrip(c, s);
    }

    [Test]
    public void RoundTripWithBpmChangeAndOffset()
    {
        var c = Sample();
        c.Beat.Bpm.Add(new BpmChange(600, 173.5));
        c.Audio.Bgm.OffsetMs = 37;
        AssertRoundTrip(c, All(c));
    }

    [Test]
    public void EmptySelectionIsBareTime()
    {
        var c = Sample();
        var ts = ChartTimestamp.Of(Timing(c), new Selection(), 83456.2);
        Assert.That(ts.ToString(), Is.EqualTo("01:23:456 - "));
        Assert.That(ChartTimestamp.TryParse(ts.ToString(), out var back), Is.True);
        Assert.That(back.TimeMs, Is.EqualTo(83456));
        Assert.That(back.Items, Is.Empty);
    }

    [TestCase("01:23:456 -", 83456)]
    [TestCase("01:23:456 - ", 83456)]
    [TestCase("01:23:456", 83456)]
    [TestCase("01:23:456 - fix this", 83456)]
    [TestCase("see 01:23:456 () - here", 83456)]
    public void BareTimestampForms(string text, double expected)
    {
        Assert.That(ChartTimestamp.TryParse(text, out var ts), Is.True);
        Assert.That(ts.TimeMs, Is.EqualTo(expected));
        Assert.That(ts.Items, Is.Empty);
    }

    [Test]
    public void TolerantItemSpellings()
    {
        var c = Sample();
        const string text = "modding: 00:00:500 (500|bta, 00:01.000|BT D long ,1000|fxl,00:01:500|FX_R hold, 00:02:500|VOL-L 0.25 -> 0.75, 00:02:000|knob l) - too dense";
        Assert.That(ChartTimestamp.TryParse(text, out var ts), Is.True);
        Assert.That(ts.Unparsed, Is.Empty);
        Assert.That(ts.Items, Has.Count.EqualTo(6));
        var r = ts.Resolve(c, Timing(c));
        Assert.That(r.Missing, Is.Empty);
        Assert.That(r.Ambiguous, Is.Empty);
        Assert.That(r.Notes, Has.Count.EqualTo(4));
        Assert.That(r.LaserPoints.Select(p => p.Point), Is.EquivalentTo(new[] { c.Notes.Lasers[0][0].Points[1], c.Notes.Lasers[0][0].Points[0] }));
    }

    [Test]
    public void UnreadableItemsAreKeptAndTheRestResolves()
    {
        var c = Sample();
        Assert.That(ChartTimestamp.TryParse("00:00:500 (00:00:500|BT-A,1,2|3,00:01:000|BT-E,00:01:000|FX-L 0.5) - ", out var ts), Is.True);
        Assert.That(ts.Unparsed, Is.EqualTo(new[] { "1", "2|3", "00:01:000|BT-E", "00:01:000|FX-L 0.5" }));
        var r = ts.Resolve(c, Timing(c));
        Assert.That(r.Notes.Single().Note, Is.SameAs(c.Notes.Bt[0][0]));
    }

    [Test]
    public void MissingObjectsAreReported()
    {
        var c = Sample();
        Assert.That(ChartTimestamp.TryParse("00:00:500 (00:00:500|BT-A,00:00:500|BT-B,00:02:000|LASER-L 0.9,00:09:000|LASER-R) - ", out var ts), Is.True);
        var r = ts.Resolve(c, Timing(c));
        Assert.That(r.Notes, Has.Count.EqualTo(1));
        Assert.That(r.LaserPoints, Is.Empty);
        Assert.That(r.Missing.Select(i => i.Lane), Is.EqualTo(new[] { TimestampLane.BtB, TimestampLane.LaserL, TimestampLane.LaserR }));
    }

    [Test]
    public void ObjectMovedByMoreThanTheToleranceIsMissing()
    {
        var c = Sample();
        var timing = Timing(c);
        var s = new Selection();
        s.Add(new NoteRef(NoteKind.Bt, 0, c.Notes.Bt[0][0]));
        var ts = ChartTimestamp.Of(timing, s);
        c.Notes.Bt[0][0].Y += 5; // ~10 ms later
        var r = ts.Resolve(c, timing);
        Assert.That(r.Missing, Has.Count.EqualTo(1));
    }

    [Test]
    public void LaserWithoutValueAtASharedTimeIsAmbiguous()
    {
        var c = Sample();
        Assert.That(ChartTimestamp.TryParse("00:03:000 (00:03:000|LASER-L) - ", out var ts), Is.True);
        var r = ts.Resolve(c, Timing(c));
        Assert.That(r.Ambiguous, Has.Count.EqualTo(1));
        Assert.That(r.LaserPoints, Has.Count.EqualTo(2), "both candidates are selected");
    }

    [Test]
    public void LaserWithValuePicksTheRightSection()
    {
        var c = Sample();
        Assert.That(ChartTimestamp.TryParse("00:03:000 (00:03:000|LASER-L 0.5) - ", out var ts), Is.True);
        var r = ts.Resolve(c, Timing(c));
        Assert.That(r.Ambiguous, Is.Empty);
        Assert.That(r.LaserPoints.Single().Section, Is.SameAs(c.Notes.Lasers[0][1]));
    }

    [Test]
    public void PlainItemPrefersPlainPointOverSlam()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Lasers[0].Add(new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(240, 0.25) }));
        c.Notes.Lasers[0].Add(new LaserSection(240, new[] { new LaserPoint(0, 0.25, 1), new LaserPoint(240, 1) }));
        var timing = Timing(c);
        Assert.That(ChartTimestamp.TryParse("00:00:500 (00:00:500|LASER-L 0.25) - ", out var plain), Is.True);
        Assert.That(plain.Resolve(c, timing).LaserPoints.Single().Section, Is.SameAs(c.Notes.Lasers[0][0]));
        Assert.That(ChartTimestamp.TryParse("00:00:500 (00:00:500|LASER-L 0.25>1) - ", out var slam), Is.True);
        Assert.That(slam.Resolve(c, timing).LaserPoints.Single().Section, Is.SameAs(c.Notes.Lasers[0][1]));
    }

    [Test]
    public void ApplyToReplacesTheSelection()
    {
        var c = Sample();
        var s = All(c);
        Assert.That(ChartTimestamp.TryParse("00:00:500 (00:00:500|BT-A) - ", out var ts), Is.True);
        ts.Resolve(c, Timing(c)).ApplyTo(s);
        Assert.That(s.Count, Is.EqualTo(1));
        Assert.That(s.Notes.Single().Note, Is.SameAs(c.Notes.Bt[0][0]));
    }

    [TestCase("")]
    [TestCase("hello")]
    [TestCase("(BT-A)")]
    public void NoTimeNoTimestamp(string text) => Assert.That(ChartTimestamp.TryParse(text, out _), Is.False);
}
