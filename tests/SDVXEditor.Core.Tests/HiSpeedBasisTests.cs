using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// The ksh tempo header (a display range like <c>t=35-321</c> is not a tempo) and the playtest's hi-speed basis
/// (std BPM, else the most common tempo by duration up to the last note, like unnamed-sdvx-clone's M-mod).
/// </summary>
[TestFixture]
public class HiSpeedBasisTests
{
    private const long Bar = 960;

    private static string Measure(params string[] options)
        => string.Concat(options.Select(o => o + "\r\n")) + "1000|00|--\r\n" + string.Concat(Enumerable.Repeat("0000|00|--\r\n", 3)) + "--\r\n";

    private static string EmptyMeasure(params string[] options)
        => string.Concat(options.Select(o => o + "\r\n")) + string.Concat(Enumerable.Repeat("0000|00|--\r\n", 4)) + "--\r\n";

    private static Chart Ksh(string header, params string[] measures)
        => KshReader.Read("title=t\r\nartist=a\r\n" + header + "--\r\n" + string.Concat(measures));

    [Test]
    public void RangedHeaderIsDisplayOnlyAndTheInlineTempoStarts()
    {
        // AureoLe MXM.ksh: header t=35-321, first chart line t=321 — one tempo point, 321, never 35
        var c = Ksh("t=35-321\r\n", Measure("beat=4/4", "t=321"), Measure(), Measure());
        Assert.That(c.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 321.0) }));
        Assert.That(c.Meta.DispBpm, Is.EqualTo("35-321"));
        var t = TimingMap.FromChart(c);
        Assert.That(t.BpmAt(0), Is.EqualTo(321));
        Assert.That(t.PulseToMs(240), Is.EqualTo(60000.0 / 321).Within(1e-9));
        Assert.That(HiSpeedBasis.Of(c, t).Bpm, Is.EqualTo(321));
    }

    [Test]
    public void RangedHeaderWithTheFirstTempoLaterStartsAtThatTempo()
    {
        // libkson: a ranged header inserts nothing; the first inline t= then applies from pulse 0
        var c = Ksh("t=35-321\r\n", Measure(), Measure("t=150"), Measure());
        Assert.That(c.Beat.Bpm.Any(b => b.Bpm == 35), Is.False);
        var t = TimingMap.FromChart(c);
        Assert.That(t.BpmAt(0), Is.EqualTo(150));
        Assert.That(t.PulseToMs(Bar), Is.EqualTo(4 * 60000.0 / 150).Within(1e-9));
    }

    [Test]
    public void InlineTempoOnTheFirstLineReplacesTheHeaderTempo()
    {
        var c = Ksh("t=200\r\n", Measure("t=180"), Measure());
        Assert.That(c.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 180.0) }));
    }

    private static TimingMap Map(params (long Y, double Bpm)[] bpms)
    {
        var b = new BeatInfo();
        foreach (var (y, bpm) in bpms) b.Bpm.Add(new BpmChange(y, bpm));
        return new TimingMap(b);
    }

    [Test]
    public void ModeBpmIsTheTempoWithTheMostTime()
    {
        // 2 bars at 100 (4.8 s), 9 bars at 200 (10.8 s), 2 bars at 100 (4.8 s): 200 has 10.8 s against 9.6 s
        var t = Map((0, 100), (2 * Bar, 200), (11 * Bar, 100));
        Assert.That(t.ModeBpm(13 * Bar), Is.EqualTo(200));
    }

    [Test]
    public void ModeBpmCountsTimeNotBeats()
    {
        // 4 bars at 100 = 9.6 s beats 6 bars at 200 = 7.2 s, although the second has more beats
        var t = Map((0, 100), (4 * Bar, 200));
        Assert.That(t.ModeBpm(10 * Bar), Is.EqualTo(100));
    }

    [Test]
    public void ModeBpmStopsAtTheEndAndBreaksTiesByFirstHeard()
    {
        var t = Map((0, 120), (4 * Bar, 60));
        Assert.That(t.ModeBpm(4 * Bar), Is.EqualTo(120), "nothing after the end counts");
        Assert.That(t.ModeBpm(6 * Bar), Is.EqualTo(120), "tie (8 s each): the first tempo");
        Assert.That(t.ModeBpm(7 * Bar), Is.EqualTo(60));
        Assert.That(t.ModeBpm(0), Is.EqualTo(120), "no range: the tempo at pulse 0");
    }

    [Test]
    public void BasisIsTheModeTempoUpToTheLastNote()
    {
        // a slow intro (1 bar at 70 = 3.4 s) and a long silent outro at 60 must not set the speed: the notes sit in the
        // 160 part (4.5 s up to the last note)
        var c = Ksh("t=70\r\n", EmptyMeasure(), Measure("t=160"), Measure(), Measure(), Measure(),
            EmptyMeasure("t=60"), EmptyMeasure(), EmptyMeasure(), EmptyMeasure(), EmptyMeasure(), EmptyMeasure());
        var t = TimingMap.FromChart(c);
        var basis = HiSpeedBasis.Of(c, t);
        Assert.That(basis.Source, Is.EqualTo(HiSpeedBasisSource.ModeBpm));
        Assert.That(basis.Bpm, Is.EqualTo(160));
        Assert.That(t.BpmAt(0), Is.EqualTo(70), "the start tempo (the old basis) differs");
        Assert.That(basis.HiSpeedFor(600), Is.EqualTo(600 / 160.0).Within(1e-12));
    }

    [Test]
    public void DeclaredStdBpmWins()
    {
        var c = Ksh("t=70-160\r\nto=140\r\n", Measure("t=160"), Measure());
        var basis = HiSpeedBasis.Of(c, TimingMap.FromChart(c));
        Assert.That(basis.Source, Is.EqualTo(HiSpeedBasisSource.StdBpm));
        Assert.That(basis.Bpm, Is.EqualTo(140));
    }

    [Test]
    public void EmptyChartFallsBackToTheStartTempo()
    {
        var c = Ksh("t=180\r\n", EmptyMeasure());
        Assert.That(HiSpeedBasis.Of(c, TimingMap.FromChart(c)).Bpm, Is.EqualTo(180));
    }

    private static IEnumerable<TestCaseData> RealCases() => RealCharts.Cases(".ksh", "RealChartHasOneStartTempo");

    [TestCaseSource(nameof(RealCases))]
    public void RealChartHasOneStartTempo(string path)
    {
        RealCharts.RequireFile(path);
        var c = KshReader.ReadFile(path);
        Assert.That(c.Beat.Bpm.Count(b => b.Y == 0), Is.LessThanOrEqualTo(1), "BPM points at pulse 0");
        var basis = HiSpeedBasis.Of(c, TimingMap.FromChart(c));
        TestContext.Out.WriteLine($"disp {c.Meta.DispBpm}, start {TimingMap.FromChart(c).BpmAt(0)}, basis {basis.Label} {basis.Bpm}");
    }

    [Test]
    public void AureoLeMxmScrollsOnItsMainTempo()
    {
        string path = Path.Combine(RealCharts.Root, "AureoLe_RUSHovEr_for_Angelus", "MXM.ksh");
        RealCharts.RequireFile(path);
        var c = KshReader.ReadFile(path);
        var t = TimingMap.FromChart(c);
        Assert.That((c.Beat.Bpm[0].Y, c.Beat.Bpm[0].Bpm), Is.EqualTo((0L, 321.0)));
        Assert.That(t.BpmAt(t.MsToPulse(144_400)), Is.EqualTo(321), "2:24.4 is in the 321 BPM part");
        Assert.That(HiSpeedBasis.Of(c, t).Bpm, Is.EqualTo(321));
    }
}
