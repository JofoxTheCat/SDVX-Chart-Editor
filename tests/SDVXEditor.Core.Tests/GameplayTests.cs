using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.Gameplay;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class GameplayTests
{
    // 120 BPM: one beat = 240 pulses = 500 ms, 1 pulse = 2.0833 ms
    private static Chart chart(double bpm = 120)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, bpm));
        return c;
    }

    private static double ms(double pulse, double bpm = 120) => pulse * 60000.0 / (bpm * 240);

    // ---- tick rules ----------------------------------------------------------

    [TestCase(60, 60)]
    [TestCase(120, 60)]
    [TestCase(255.9, 60)]
    [TestCase(256, 120)]
    [TestCase(511, 120)]
    [TestCase(512, 240)]
    public void TickIntervalHalvesPerBpmOctaveFrom256(double bpm, long expected)
    {
        Assert.That(TickRules.IntervalPulses(bpm), Is.EqualTo(expected));
    }

    [Test]
    public void HoldTicksSkipTheStartAndTheLastUnit()
    {
        // one beat at 1/16: ticks at +60 and +120 (+180 is not < 240 - 60)
        Assert.That(TickRules.HoldTicks(1000, 240, 60), Is.EqualTo(new[] { 1060L, 1120L }));
        // two beats
        Assert.That(TickRules.HoldTicks(0, 480, 60), Is.EqualTo(new[] { 60L, 120L, 180L, 240L, 300L, 360L }));
        // too short for a tick: one in the middle
        Assert.That(TickRules.HoldTicks(0, 60, 60), Is.EqualTo(new[] { 30L }));
    }

    [Test]
    public void LaserTicksSplitAtSlamsAndDropTheTickOnASlam()
    {
        var plain = new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(960, 1) });
        var (t1, s1) = TickRules.LaserTicks(plain, 60);
        Assert.That(t1, Has.Count.EqualTo(16));
        Assert.That(t1[0], Is.EqualTo(0));
        Assert.That(t1[^1], Is.EqualTo(900));
        Assert.That(s1, Is.Empty);

        var slammed = new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(480, 0, 1), new LaserPoint(960, 1) });
        var (t2, s2) = TickRules.LaserTicks(slammed, 60);
        Assert.That(s2, Is.EqualTo(new[] { new SlamInfo(480, 0, 1) }));
        Assert.That(t2, Has.Count.EqualTo(8 + 7));
        Assert.That(t2, Does.Not.Contain(480L));
        Assert.That(t2, Does.Contain(540L));

        // a lone slam (one point carrying vf) is one slam and no tick
        var lone = new LaserSection(100, new[] { new LaserPoint(0, 1, 0) });
        var (t3, s3) = TickRules.LaserTicks(lone, 60);
        Assert.That(t3, Is.Empty);
        Assert.That(s3.Single().Direction, Is.EqualTo(-1));
    }

    // ---- windows -------------------------------------------------------------

    [TestCase(0, HitResult.SCritical)]
    [TestCase(-30, HitResult.Critical)]
    [TestCase(46, HitResult.Critical)]
    [TestCase(-60, HitResult.Near)]
    [TestCase(92, HitResult.Near)]
    [TestCase(-120, HitResult.Error)]
    [TestCase(138, HitResult.Error)]
    public void DefaultWindows(double offset, HitResult expected)
    {
        Assert.That(JudgementWindows.Default.JudgeChip(offset), Is.EqualTo(expected));
    }

    [Test]
    public void OutsideTheErrorWindowNothingIsJudged()
    {
        Assert.That(JudgementWindows.Default.JudgeChip(-139), Is.Null);
    }

    [Test]
    public void WindowsScaleWithRateInSongTime()
    {
        var half = JudgementWindows.Default.ForRate(0.5);
        Assert.That(half.CriticalMs, Is.EqualTo(23));
        Assert.That(half.NearMs, Is.EqualTo(46));
        Assert.That(half.ErrorMs, Is.EqualTo(69));
        Assert.That(half.LaserLeniency, Is.EqualTo(JudgementWindows.Default.LaserLeniency), "the distance leniency is not a time");
    }

    // ---- chips ---------------------------------------------------------------

    [Test]
    public void ChipPressesAreJudgedByOffsetAndMissesComeLate()
    {
        var c = chart();
        c.Notes.Bt[0].Add(new Note(480));   // 1000 ms
        c.Notes.Bt[0].Add(new Note(960));   // 2000 ms
        c.Notes.Bt[0].Add(new Note(1440));  // 3000 ms
        var judge = new GameplayJudge(new PlayableChart(c));
        var events = new List<JudgementEvent>();
        judge.Judged += events.Add;

        judge.Press(0, 1050); judge.Release(0, 1060);    // +50: NEAR late
        judge.Press(0, 1800); judge.Release(0, 1810);    // -200 before chip 2: nothing
        Assert.That(events, Has.Count.EqualTo(1));
        judge.Press(0, 1900); judge.Release(0, 1910);    // -100: ERROR early
        judge.Update(3200);                                  // chip 3 never pressed → miss after 138 ms

        Assert.That(events.Select(e => e.Result), Is.EqualTo(new[] { HitResult.Near, HitResult.Error, HitResult.Error }));
        Assert.That(events[0].IsLate, Is.True);
        Assert.That(events[1].IsEarly, Is.True);
        Assert.That(double.IsNaN(events[2].OffsetMs), Is.True);
        Assert.That(judge.Score.Points, Is.EqualTo(1));
        Assert.That(judge.Score.Chain, Is.EqualTo(0));
        Assert.That(judge.Score.MaxChain, Is.EqualTo(1));
        Assert.That(judge.Score.NearsLate, Is.EqualTo(1));
        Assert.That(judge.Score.ErrorsEarly, Is.EqualTo(1));
    }

    [Test]
    public void AllNearIsHalfTheScore()
    {
        var c = chart();
        for (int i = 1; i <= 10; i++) c.Notes.Fx[i % 2].Add(new Note(i * 240));
        var judge = new GameplayJudge(new PlayableChart(c));
        for (int i = 1; i <= 10; i++)
        {
            int lane = PlayLanes.FxL + i % 2;
            judge.Press(lane, ms(i * 240) - 70);
            judge.Release(lane, ms(i * 240));
        }
        judge.Update(10_000);
        Assert.That(judge.Score.Score, Is.EqualTo(5_000_000));
        Assert.That(judge.Score.Nears, Is.EqualTo(10));
        Assert.That(judge.Score.ExScore, Is.EqualTo(20));
        Assert.That(judge.Score.ClearMark, Is.EqualTo("ULTIMATE CHAIN"));
    }

    // ---- holds -------------------------------------------------------------

    [Test]
    public void HoldTicksNeedTheButtonDown()
    {
        var c = chart();
        c.Notes.Bt[2].Add(new Note(480, 480)); // 1000..2000 ms, 6 ticks
        var judge = new GameplayJudge(new PlayableChart(c));
        judge.Press(2, 990);
        for (double t = 990; t <= 1500; t += 5) judge.Update(t);
        judge.Release(2, 1500);
        for (double t = 1500; t <= 2500; t += 5) judge.Update(t);

        // ticks at 1125, 1250, 1375, 1500 held; 1625, 1750 released
        Assert.That(judge.Score.Criticals, Is.EqualTo(4));
        Assert.That(judge.Score.Errors, Is.EqualTo(2));
        Assert.That(judge.Finished, Is.True);
    }

    // ---- lasers ------------------------------------------------------------

    private static Chart laserChart()
    {
        var c = chart();
        // left laser 0 → 1 over one measure (1000..3000 ms), then a slam back to 0 and a still part
        c.Notes.Lasers[0].Add(new LaserSection(480, new[]
        {
            new LaserPoint(0, 0),
            new LaserPoint(960, 1, 0),
            new LaserPoint(1440, 0),
        }));
        return c;
    }

    [Test]
    public void FollowingTheLaserWithInputHitsEveryTickAndTheSlam()
    {
        var pc = new PlayableChart(laserChart());
        var judge = new GameplayJudge(pc);
        const double step = 1000.0 / 120;
        double slamMs = ms(480 + 960);
        bool slammed = false;
        for (double t = 900; t <= 4500; t += step)
        {
            // turn right while the laser moves right, flick left at the slam
            if (t >= ms(480) && t < slamMs - 20) judge.MoveLaser(0, 0.02, t);
            if (!slammed && t >= slamMs - 15)
            {
                judge.MoveLaser(0, -0.5, t);
                slammed = true;
            }
            judge.Update(t);
        }

        Assert.That(judge.Score.Errors, Is.EqualTo(0), "no misses");
        Assert.That(judge.Score.Score, Is.EqualTo(ScoreProcessor.MaxScore));
        Assert.That(judge.Score.Judged, Is.EqualTo(pc.MaxChain));
    }

    [Test]
    public void NotTurningLosesAMovingLaserAndMissesTheSlam()
    {
        var judge = new GameplayJudge(new PlayableChart(laserChart()));
        for (double t = 900; t <= 4500; t += 1000.0 / 120) judge.Update(t);
        Assert.That(judge.Score.Errors, Is.GreaterThan(5));
        Assert.That(judge.Chart.Slams[0].Single().Result, Is.EqualTo(HitResult.Error));
    }

    [Test]
    public void ASlamTurnedTheWrongWayIsAnError()
    {
        var c = chart();
        c.Notes.Lasers[1].Add(new LaserSection(480, new[] { new LaserPoint(0, 1, 0) }));
        var judge = new GameplayJudge(new PlayableChart(c));
        judge.MoveLaser(1, +0.3, 995);
        for (double t = 950; t <= 1300; t += 4) judge.Update(t);
        Assert.That(judge.Chart.Slams[1].Single().Result, Is.EqualTo(HitResult.Error));

        var judge2 = new GameplayJudge(new PlayableChart(c));
        judge2.MoveLaser(1, -0.3, 970); // 30 ms early, inside half of 84 ms
        for (double t = 950; t <= 1300; t += 4) judge2.Update(t);
        Assert.That(judge2.Chart.Slams[1].Single().Result, Is.EqualTo(HitResult.Critical));
    }

    // ---- start position, autoplay ---------------------------------------

    [Test]
    public void UnitsBeforeTheStartAreNotCounted()
    {
        var c = chart();
        for (int i = 0; i < 8; i++) c.Notes.Bt[i % 4].Add(new Note(i * 240));
        var pc = new PlayableChart(c, startMs: ms(4 * 240) - 1);
        Assert.That(pc.CountedChips, Is.EqualTo(4));
        var score = GameplayJudge.SimulateAutoplay(pc);
        Assert.That(score.Score, Is.EqualTo(ScoreProcessor.MaxScore));
        Assert.That(score.Judged, Is.EqualTo(4));
    }

    [Test]
    public void AutoplayOnASyntheticChartIsPerfect()
    {
        var c = laserChart();
        c.Notes.Bt[0].Add(new Note(240));
        c.Notes.Bt[1].Add(new Note(480, 960));
        c.Notes.Fx[1].Add(new Note(960));
        c.Notes.Lasers[1].Add(new LaserSection(2400, new[] { new LaserPoint(0, 1), new LaserPoint(240, 0.5, 0.9), new LaserPoint(480, 0.2) }, 2));
        var pc = new PlayableChart(c);
        var score = GameplayJudge.SimulateAutoplay(pc);
        Assert.That(score.Score, Is.EqualTo(ScoreProcessor.MaxScore));
        Assert.That(score.ExScore, Is.EqualTo(score.MaxExScore));
        Assert.That(score.MaxChain, Is.EqualTo(pc.MaxChain));
        Assert.That(score.Errors, Is.EqualTo(0));
        Assert.That(score.Gauge, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(score.ClearMark, Is.EqualTo("PERFECT"));
    }

    [TestCaseSource(typeof(RealCharts), nameof(RealCharts.Cases), new object[] { ".kson", nameof(AutoplayOnRealKsonIsPerfect) })]
    public void AutoplayOnRealKsonIsPerfect(string file)
    {
        RealCharts.RequireFile(file);
        assertAutoplayPerfect(KsonReader.ReadFile(file), file);
    }

    [TestCaseSource(typeof(RealCharts), nameof(RealCharts.Cases), new object[] { ".ksh", nameof(AutoplayOnRealKshIsPerfect) })]
    public void AutoplayOnRealKshIsPerfect(string file)
    {
        RealCharts.RequireFile(file);
        assertAutoplayPerfect(KshReader.ReadFile(file), file);
    }

    /// <summary>A sample of the user's own official charts (read-only, never copied), when the install is present.</summary>
    [Test]
    public void AutoplayOnOfficialChartsIsPerfect()
    {
        const string music = @"D:\SDVX-RIP\SOUND VOLTEX NABLA\data\music";
        if (!Directory.Exists(music)) Assert.Ignore("game install not found");
        var files = Directory.EnumerateFiles(music, "*.vox", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).Where((_, i) => i % 400 == 0).ToList();
        Assert.That(files, Is.Not.Empty);
        foreach (string f in files)
            assertAutoplayPerfect(VoxReader.ReadFile(f), f);
    }

    private static void assertAutoplayPerfect(Chart c, string what)
    {
        var pc = new PlayableChart(c);
        var score = GameplayJudge.SimulateAutoplay(pc, 1000.0 / 120);
        Assert.That(score.Errors, Is.EqualTo(0), what);
        Assert.That(score.Judged, Is.EqualTo(pc.MaxChain), what);
        if (pc.MaxChain > 0)
            Assert.That(score.Score, Is.EqualTo(ScoreProcessor.MaxScore), what);
    }

    [Test]
    public void ScoreIsFlooredToTheMaxPoints()
    {
        var s = new ScoreProcessor(3, 0, 0);
        var o = new ScoreObject { Kind = ScoreObjectKind.Chip };
        s.Apply(new JudgementEvent(o, HitResult.Critical, 0, 0));
        Assert.That(s.Score, Is.EqualTo(3_333_333));
        Assert.That(ScoreProcessor.GradeOf(9_900_000), Is.EqualTo("S"));
        Assert.That(ScoreProcessor.GradeOf(9_899_999), Is.EqualTo("AAA+"));
    }

    [Test]
    public void BindingsParseWithDefaultsAndJoystickButtons()
    {
        var b = PlayBindings.Parse("bt-a=S|Joy1, fx-r = Space, nonsense=Q, back=");
        Assert.That(b[PlayAction.BtA], Is.EqualTo(new[] { "S", "Joy1" }));
        Assert.That(b[PlayAction.FxR], Is.EqualTo(new[] { "Space" }));
        Assert.That(b[PlayAction.BtB], Is.EqualTo(new[] { "F" }), "unnamed actions keep the KSM default");
        Assert.That(b[PlayAction.Back], Is.EqualTo(new[] { "Escape" }), "an empty value keeps the default");
        Assert.That(PlayBindings.JoystickButtonOf("Joy1"), Is.EqualTo(0));
        Assert.That(PlayBindings.JoystickButtonOf("J"), Is.Null);
        Assert.That(PlayBindings.Parse(PlayBindings.DefaultText).ToString(), Is.EqualTo(PlayBindings.DefaultText));
        Assert.That(PlayBindings.DefaultText, Does.Contain("bt-a=D").And.Contain("laser-r-right=P").And.Contain("start=Enter"));
    }

    [Test]
    public void WindowsParseFromTheSettingsLine()
    {
        var w = JudgementWindows.Parse("40, 80, 120");
        Assert.That((w.CriticalMs, w.NearMs, w.ErrorMs, w.SlamMs, w.SCriticalMs), Is.EqualTo((40.0, 80.0, 120.0, 84.0, 20.0)));
        Assert.That(JudgementWindows.Parse("garbage"), Is.EqualTo(JudgementWindows.Default));
        var bad = JudgementWindows.Parse("100,50,20");
        Assert.That(bad.NearMs, Is.GreaterThanOrEqualTo(bad.CriticalMs));
        Assert.That(bad.ErrorMs, Is.GreaterThanOrEqualTo(bad.NearMs));
    }
}
