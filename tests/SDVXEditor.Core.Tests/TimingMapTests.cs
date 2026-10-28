using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class TimingMapTests
{
    private static BeatInfo Beat(params (long Y, double Bpm)[] bpms)
    {
        var b = new BeatInfo();
        foreach (var (y, bpm) in bpms) b.Bpm.Add(new BpmChange(y, bpm));
        return b;
    }

    [Test]
    public void ConstantTempoAndOffset()
    {
        var t = new TimingMap(Beat((0, 120)), offsetMs: 80);
        Assert.That(t.PulseToMs(0), Is.EqualTo(80));
        Assert.That(t.PulseToMs(240), Is.EqualTo(580)); // one quarter at 120 BPM = 500 ms
        Assert.That(t.PulseToMs(960), Is.EqualTo(2080));
        Assert.That(t.PulseToMs(-240), Is.EqualTo(-420)); // extrapolated before the chart
        Assert.That(t.MsToPulse(580), Is.EqualTo(240).Within(1e-9));
    }

    [Test]
    public void BpmChanges()
    {
        var t = new TimingMap(Beat((0, 120), (960, 240), (1920, 60)));
        Assert.That(t.PulseToMs(960), Is.EqualTo(2000));
        Assert.That(t.PulseToMs(1440), Is.EqualTo(2500));
        Assert.That(t.PulseToMs(1920), Is.EqualTo(3000));
        Assert.That(t.PulseToMs(2160), Is.EqualTo(4000));
        Assert.That(t.BpmAt(1919), Is.EqualTo(240));
        Assert.That(t.BpmAt(1920), Is.EqualTo(60));
        foreach (double ms in new[] { 0.0, 1999, 2000, 2750, 3000, 3999.5, 10000 })
            Assert.That(t.PulseToMs(t.MsToPulse(ms)), Is.EqualTo(ms).Within(1e-6));
    }

    [Test]
    public void StopsFreezeTheScrollButNotTheClock()
    {
        var b = Beat((0, 120));
        b.Stops.Add(new Stop(960, 480));
        var t = new TimingMap(b);
        Assert.That(t.PulseToMs(1920), Is.EqualTo(4000), "kson stop = scroll_speed 0; audio time unchanged");
        Assert.That(t.VisualPosition(960), Is.EqualTo(960));
        Assert.That(t.VisualPosition(1200), Is.EqualTo(960));
        Assert.That(t.VisualPosition(1440), Is.EqualTo(960));
        Assert.That(t.VisualPosition(1920), Is.EqualTo(1440));
        Assert.That(t.ScrollSpeedAt(1000), Is.EqualTo(0));
        Assert.That(t.ScrollSpeedAt(1500), Is.EqualTo(1));
    }

    [Test]
    public void ScrollSpeedIntegration()
    {
        var b = Beat((0, 120));
        b.ScrollSpeed.Add(new GraphPoint(0, 1));
        b.ScrollSpeed.Add(new GraphPoint(960, 3));             // linear ramp 1 → 3
        b.ScrollSpeed.Add(new GraphPoint(1920, 1, 2));          // instant 1 → 2
        b.ScrollSpeed.Add(new GraphPoint(2880, 2, -1));         // reverse scroll afterwards
        var t = new TimingMap(b);
        Assert.That(t.VisualPosition(960), Is.EqualTo(1920));   // (1+3)/2 · 960
        Assert.That(t.ScrollSpeedAt(1440), Is.EqualTo(2));      // 3 → 1 between 960 and 1920 (graph to the next V)
        Assert.That(t.VisualPosition(1920), Is.EqualTo(1920 + 1920));
        Assert.That(t.VisualPosition(2880), Is.EqualTo(3840 + 1920));
        Assert.That(t.VisualPosition(3840), Is.EqualTo(5760 - 960));

        // a curved segment integrates numerically and stays between the linear bounds
        var c = Beat((0, 120));
        c.ScrollSpeed.Add(new GraphPoint(0, 1, null, new Curve(0.9, 0.1)));
        c.ScrollSpeed.Add(new GraphPoint(960, 3));
        double curved = new TimingMap(c).VisualPosition(960);
        Assert.That(curved, Is.GreaterThan(960).And.LessThan(1920));
    }

    [Test]
    public void SixEightMeasures()
    {
        var b = Beat((0, 120));
        b.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        b.TimeSignatures.Add(new TimeSignature(1, 6, 8));
        b.TimeSignatures.Add(new TimeSignature(3, 4, 4));
        var t = new TimingMap(b);
        Assert.That(t.MeasureStart(1), Is.EqualTo(960));
        Assert.That(t.MeasureLength(1), Is.EqualTo(720));
        Assert.That(t.MeasureStart(2), Is.EqualTo(1680));
        Assert.That(t.MeasureStart(3), Is.EqualTo(2400));
        Assert.That(t.MeasureStart(4), Is.EqualTo(3360));
        Assert.That(t.BeatLength(1), Is.EqualTo(120), "a 6/8 beat is an eighth (VOX uses 192/den ticks too)");
        Assert.That(t.PositionOf(960 + 5 * 120 + 30), Is.EqualTo(new MusicalPosition(1, 5, 30)));
        Assert.That(t.PositionOf(2399), Is.EqualTo(new MusicalPosition(2, 5, 119)));
        Assert.That(t.PositionOf(2400), Is.EqualTo(new MusicalPosition(3, 0, 0)));
        Assert.That(t.ToPulse(2, 3, 10), Is.EqualTo(1680 + 360 + 10));
        Assert.That(t.MeasureAt(1679), Is.EqualTo(1));
        Assert.That(t.BarLines(2400), Is.EqualTo(new long[] { 0, 960, 1680, 2400 }));
        // snapping in 6/8: 1/2 beat = 60 pulses, anchored at the bar line
        Assert.That(t.Snap(960 + 85, 2), Is.EqualTo(960 + 60));
        Assert.That(t.Snap(960 + 95, 2), Is.EqualTo(960 + 120));
    }

    [Test]
    public void SnapHelpers()
    {
        var t = new TimingMap(Beat((0, 120)));
        Assert.That(TimingMap.SnapDivisors, Is.EqualTo(new[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 192 }));
        // 1/192 of a beat is 1.25 pulses: every line rounded, none dropped or doubled
        Assert.That(t.NextSnap(0, 192), Is.EqualTo(1));
        Assert.That(t.NextSnap(1, 192), Is.EqualTo(3), "2.5 rounds away from zero");
        Assert.That(t.Snap(239, 192), Is.EqualTo(239));
        Assert.That(t.Snap(70, 4), Is.EqualTo(60));
        Assert.That(t.Snap(95, 4), Is.EqualTo(120));
        Assert.That(t.Snap(85, 3), Is.EqualTo(80));
        Assert.That(t.NextSnap(60, 4), Is.EqualTo(120));
        Assert.That(t.NextSnap(61, 4), Is.EqualTo(120));
        Assert.That(t.PreviousSnap(60, 4), Is.EqualTo(0));
        Assert.That(t.PreviousSnap(960, 4), Is.EqualTo(900), "crosses the bar line backwards");
        Assert.That(t.NextSnap(950, 1), Is.EqualTo(960));
        // 1/32 of a quarter is 7.5 pulses: grid lines are rounded to whole pulses
        Assert.That(t.NextSnap(0, 32), Is.EqualTo(8));
        Assert.That(t.NextSnap(8, 32), Is.EqualTo(15));
        Assert.That(t.IsGridLine(15, 32), Is.True);
        Assert.That(t.IsGridLine(5, 48), Is.True);
        Assert.That(t.IsGridLine(6, 48), Is.False);
        foreach (int d in TimingMap.SnapDivisors)
        {
            long p = 0;
            for (int i = 0; i < 2 * 4 * d; i++) p = t.NextSnap(p, d);
            Assert.That(p, Is.EqualTo(1920), $"{d}: 2 measures of 1/{d} beat steps");
        }
    }

    [Test]
    public void RealChartWithStopsAndBpmChanges()
    {
        string path = Path.Combine(RealCharts.RequireRoot(), "Aegleseeker", "MXMver6.1.ksh");
        RealCharts.RequireFile(path);
        var chart = KshReader.ReadFile(path);
        var t = TimingMap.FromChart(chart);
        Assert.That(chart.Beat.Stops, Is.Not.Empty);
        Assert.That(chart.Beat.Bpm.Select(b => b.Bpm).Distinct().Count(), Is.GreaterThan(1));
        // time strictly increases, the visual position never decreases (no negative scroll in this chart)
        double prevMs = double.MinValue, prevVis = double.MinValue;
        for (long p = 0; p <= chart.EndPulse; p += 30)
        {
            double ms = t.PulseToMs(p), vis = t.VisualPosition(p);
            Assert.That(ms, Is.GreaterThan(prevMs));
            Assert.That(vis, Is.GreaterThanOrEqualTo(prevVis - 1e-9));
            Assert.That(t.MsToPulse(ms), Is.EqualTo((double)p).Within(1e-6));
            prevMs = ms;
            prevVis = vis;
        }
        // during each stop the note stream stands still
        foreach (var s in chart.Beat.Stops)
            Assert.That(t.VisualPosition(s.Y + s.Length), Is.EqualTo(t.VisualPosition(s.Y)).Within(1e-9));
        TestContext.Out.WriteLine($"Aegleseeker: {chart.Beat.Bpm.Count} BPM changes, {chart.Beat.Stops.Count} stops, " +
                                  $"length {t.PulseToMs(chart.EndPulse!.Value) / 1000:F1} s");
    }
}
