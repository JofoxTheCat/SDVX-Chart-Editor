using NUnit.Framework;
using SDVXEditor.Core.Audio.Effects;
using SDVXEditor.Core.Audio.Effects.Dsp;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Tests;

/// <summary>Effect parameter evaluation (pure): value syntax, tempo sync, overrides, parameter changes, laser filters.</summary>
[TestFixture]
public class AudioEffectTests
{
    private const double bpm = 120; // a whole note = 2000 ms, a measure = 2000 ms

    private static Chart chart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, bpm));
        return c;
    }

    private static EffectFrame eval(Chart c, double ms, LiveEffectInput? input = null)
    {
        var f = new EffectFrame();
        new EffectTimeline(c).Evaluate(ms, input, f);
        return f;
    }

    // ---- value syntax ----

    [TestCase("1/8", 0.125, ValueUnit.WholeNotes)]
    [TestCase("2.0", 2.0, ValueUnit.WholeNotes)]
    [TestCase("50ms", 0.05, ValueUnit.Seconds)]
    [TestCase("1.5s", 1.5, ValueUnit.Seconds)]
    public void LengthValues(string text, double number, ValueUnit unit)
    {
        Assert.That(EffectValue.TryParse(text, ParamKind.Length, out var v), Is.True);
        Assert.That(v.Number, Is.EqualTo(number).Within(1e-12));
        Assert.That(v.Unit, Is.EqualTo(unit));
    }

    [Test]
    public void OtherValueKinds()
    {
        Assert.That(EffectValue.TryParse("70%", ParamKind.Rate, out var r) && Math.Abs(r.Number - 0.7) < 1e-12);
        Assert.That(EffectValue.TryParse("1/4", ParamKind.Rate, out var r2) && r2.Number == 0.25);
        Assert.That(EffectValue.TryParse("30samples", ParamKind.Sample, out var s) && s.Number == 30);
        Assert.That(EffectValue.TryParse("1.5kHz", ParamKind.Freq, out var f) && f.Number == 1500);
        Assert.That(EffectValue.TryParse("-8dB", ParamKind.Db, out var d) && d.Number == -8);
        Assert.That(EffectValue.TryParse("on", ParamKind.Switch, out var o) && o.Number == 1);
        Assert.That(EffectValue.TryParse("12samplez", ParamKind.Sample, out _), Is.False);
        Assert.That(EffectValue.TryParse("abc", ParamKind.Rate, out _), Is.False);
    }

    [Test]
    public void ValueSetSyntax()
    {
        Assert.That(EffectValueSet.TryParse("0%>100%", ParamKind.Rate, out var a));
        Assert.That((a.Off.Number, a.OnMin.Number, a.OnMax.Number), Is.EqualTo((0.0, 1.0, 1.0)));

        Assert.That(EffectValueSet.TryParse("0samples-30samples", ParamKind.Sample, out var b));
        Assert.That((b.Off.Number, b.OnMin.Number, b.OnMax.Number), Is.EqualTo((0.0, 0.0, 30.0)));

        Assert.That(EffectValueSet.TryParse("0>-12", ParamKind.Pitch, out var c));
        Assert.That((c.Off.Number, c.OnMin.Number, c.OnMax.Number), Is.EqualTo((0.0, -12.0, -12.0)));

        Assert.That(EffectValueSet.TryParse("-8dB--2dB", ParamKind.Db, out var d));
        Assert.That((d.OnMin.Number, d.OnMax.Number), Is.EqualTo((-8.0, -2.0)));

        Assert.That(EffectValueSet.TryParse("10%>20%-80%", ParamKind.Rate, out var e));
        Assert.That(e.Sample(false, 0.5, bpm), Is.EqualTo(0.1).Within(1e-12));
        Assert.That(e.Sample(true, 0.5, bpm), Is.EqualTo(0.5).Within(1e-12));
        Assert.That(e.Sample(true, 0, bpm), Is.EqualTo(0.2).Within(1e-12));
    }

    // ---- FX holds ----

    [Test]
    public void TempoSyncedLengthsFollowTheBpm()
    {
        var c = chart();
        c.Beat.Bpm.Add(new BpmChange(1920, 240)); // from measure 3 on: twice as fast
        c.Notes.Fx[0].Add(new Note(0, 960) { Effects = { new FxEffectEvent("gate", 0, new() { ["wave_length"] = "1/8" }) } });
        c.Notes.Fx[0].Add(new Note(1920, 960) { Effects = { new FxEffectEvent("gate", 0, new() { ["wave_length"] = "1/8" }) } });

        var f = eval(c, 100);
        Assert.That(f.FxL.Active && f.FxL.Kind == EffectKind.Gate);
        Assert.That(f.FxL.Params[ParamId.WaveLength], Is.EqualTo(0.25).Within(1e-9), "1/8 of a whole note at 120 BPM = 250 ms");
        Assert.That(f.FxL.Params[ParamId.Rate], Is.EqualTo(0.5).Within(1e-9), "gate default rate 50%");
        Assert.That(f.FxL.Params[ParamId.Mix], Is.EqualTo(0.9).Within(1e-9), "gate mix OnMin 90%");

        var timing = new TimingMap(c.Beat);
        var f2 = eval(c, timing.PulseToMs(1920 + 10));
        Assert.That(f2.FxL.Params[ParamId.WaveLength], Is.EqualTo(0.125).Within(1e-9), "1/8 at 240 BPM = 125 ms");
        Assert.That(eval(c, timing.PulseToMs(1000)).FxL.Active, Is.False, "between the holds");
    }

    [Test]
    public void FixedTimeLengthsIgnoreTheBpm()
    {
        var c = chart();
        c.Notes.Fx[1].Add(new Note(0, 960) { Effects = { new FxEffectEvent("sidechain") } });
        var f = eval(c, 10);
        Assert.That(f.FxR.Kind, Is.EqualTo(EffectKind.SideChain));
        Assert.That(f.FxR.Params[ParamId.HoldTime], Is.EqualTo(0.05).Within(1e-12));
        Assert.That(f.FxR.Params[ParamId.AttackTime], Is.EqualTo(0.01).Within(1e-12));
        Assert.That(f.FxR.Params[ParamId.ReleaseTime], Is.EqualTo(0.25).Within(1e-9), "1/8 tempo-synced");
        Assert.That(f.FxR.Params[ParamId.Ratio], Is.EqualTo(5).Within(1e-12), "ratio 1.0>5.0 → OnMin 5");
    }

    [Test]
    public void DefinitionThenParamChangeThenOverride()
    {
        var c = chart();
        c.Audio.FxEffects.Defs.Add(new AudioEffectDef("myGate", "gate", new() { ["wave_length"] = "1/4", ["rate"] = "30%" }));
        c.Audio.FxEffects.ParamChanges["myGate"] = new() { ["rate"] = new() { new PulseValue<string>(960, "60%") } };
        c.Notes.Fx[0].Add(new Note(0, 3840) { Effects = { new FxEffectEvent("myGate") } });
        c.Notes.Fx[1].Add(new Note(0, 3840) { Effects = { new FxEffectEvent("myGate", 0, new() { ["rate"] = "90%" }) } });
        var timing = new TimingMap(c.Beat);

        var before = eval(c, timing.PulseToMs(100));
        Assert.That(before.FxL.Params[ParamId.Rate], Is.EqualTo(0.3).Within(1e-9), "definition");
        Assert.That(before.FxL.Params[ParamId.WaveLength], Is.EqualTo(0.5).Within(1e-9));
        Assert.That(before.FxR.Active, Is.False, "the same effect on both lanes is one instance (left wins)");

        var after = eval(c, timing.PulseToMs(1000));
        Assert.That(after.FxL.Params[ParamId.Rate], Is.EqualTo(0.6).Within(1e-9), "parameter change from pulse 960");

        // the override wins over the parameter change: only the right lane
        c.Notes.Fx[0].Clear();
        var over = eval(c, timing.PulseToMs(1000));
        Assert.That(over.FxR.Params[ParamId.Rate], Is.EqualTo(0.9).Within(1e-9), "long_event override");
    }

    [Test]
    public void EffectEventsWithinAHoldSwitch()
    {
        var c = chart();
        c.Notes.Fx[0].Add(new Note(0, 1920) { Effects = { new FxEffectEvent("flanger"), new FxEffectEvent("bitcrusher", 960, new() { ["reduction"] = "10samples" }), } });
        var timing = new TimingMap(c.Beat);
        Assert.That(eval(c, timing.PulseToMs(500)).FxL.Kind, Is.EqualTo(EffectKind.Flanger));
        var late = eval(c, timing.PulseToMs(1000));
        Assert.That(late.FxL.Kind, Is.EqualTo(EffectKind.BitCrusher));
        Assert.That(late.FxL.Params[ParamId.Reduction], Is.EqualTo(10));
        Assert.That(late.FxL.ActivatedMs, Is.EqualTo(timing.PulseToMs(960)).Within(1e-9), "a new event activates anew");

        c.Notes.Fx[0][0].Effects.Add(new FxEffectEvent("", 1440));
        Assert.That(eval(c, timing.PulseToMs(1500)).FxL.Active, Is.False, "the empty name = no effect");
    }

    [Test]
    public void TriggersFollowKShootRules()
    {
        var c = chart();
        c.Notes.Fx[0].Add(new Note(1000, 1800) { Effects = { new FxEffectEvent("gate", 0, new() { ["wave_length"] = "1/8" }) } });
        c.Notes.Fx[1].Add(new Note(1000, 1800) { Effects = { new FxEffectEvent("retrigger", 0, new() { ["wave_length"] = "1/16" }) } });
        var timing = new TimingMap(c.Beat);
        var f = eval(c, timing.PulseToMs(1300));
        Assert.That(f.FxL.TriggerMs, Is.EqualTo(timing.PulseToMs(960)).Within(1e-9), "gate: the last bar line");
        Assert.That(f.FxR.TriggerMs, Is.EqualTo(timing.PulseToMs(960)).Within(1e-9), "retrigger: update_period 1/2 grid from the bar line (960, 1440 …)");
        var f2 = eval(c, timing.PulseToMs(1500));
        Assert.That(f2.FxR.TriggerMs, Is.EqualTo(timing.PulseToMs(1440)).Within(1e-9));

        // echo: update_trigger off>on → the press triggers
        c.Notes.Fx[1][0].Effects[0] = new FxEffectEvent("echo", 0, new() { ["wave_length"] = "1/8" });
        Assert.That(eval(c, timing.PulseToMs(1500)).FxR.TriggerMs, Is.EqualTo(timing.PulseToMs(1000)).Within(1e-9));
    }

    [Test]
    public void LiveInputGatesFxHolds()
    {
        var c = chart();
        c.Notes.Fx[0].Add(new Note(960, 960) { Effects = { new FxEffectEvent("flanger") } });
        var timing = new TimingMap(c.Beat);
        double start = timing.PulseToMs(960);
        var input = new LiveEffectInput { ChartDriven = false, GraceMs = 30 };

        Assert.That(eval(c, start + 10, input).FxL.Active, Is.True, "within the grace window of the hold start");
        Assert.That(eval(c, start + 100, input).FxL.Active, Is.False, "not pressed");
        input.FxDown[0] = true;
        Assert.That(eval(c, start + 100, input).FxL.Active, Is.True, "pressed");
        input.ChartDriven = true;
        input.FxDown[0] = false;
        Assert.That(eval(c, start + 100, input).FxL.Active, Is.True, "autoplay / editor");
    }

    // ---- laser filters ----

    private static Chart laserChart()
    {
        var c = chart();
        // left laser 0 → 1 over one measure, right laser 1 → 0 one measure later
        c.Notes.Lasers[0].Add(new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(960, 1) }));
        c.Notes.Lasers[1].Add(new LaserSection(1920, new[] { new LaserPoint(0, 1), new LaserPoint(960, 0) }));
        c.Audio.LaserEffects.PeakingFilterDelay = 0;
        return c;
    }

    [Test]
    public void LaserValueDrivesTheDefaultPeakingFilter()
    {
        var c = laserChart();
        var timing = new TimingMap(c.Beat);
        var f = eval(c, timing.PulseToMs(480));
        Assert.That(f.Laser.Active && f.Laser.Kind == EffectKind.PeakingFilter);
        Assert.That(f.Laser.V, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(f.Laser.Params[ParamId.V], Is.EqualTo(0.5).Within(1e-9), "v 0%-100% follows the laser");
        Assert.That(f.Laser.Params[ParamId.Gain], Is.EqualTo(0.5).Within(1e-9), "gain 50% default");
        Assert.That(f.Laser.Params[ParamId.Freq], Is.EqualTo(50));
        Assert.That(f.Laser.Params[ParamId.FreqMax], Is.EqualTo(9000));

        var r = eval(c, timing.PulseToMs(1920 + 240));
        Assert.That(r.Laser.V, Is.EqualTo(0.25).Within(1e-9), "right laser mirrored: value 0.75 → v 0.25");
        Assert.That(eval(c, timing.PulseToMs(1500)).Laser.Active, Is.False, "no laser section");
    }

    [Test]
    public void PeakingDelaySamplesTheLaserEarlier()
    {
        var c = laserChart();
        c.Audio.LaserEffects.PeakingFilterDelay = 100;
        var timing = new TimingMap(c.Beat);
        double ms = timing.PulseToMs(480);
        var f = eval(c, ms);
        double expected = timing.MsToPulse(ms - 100) / 960;
        Assert.That(f.Laser.V, Is.EqualTo(expected).Within(1e-9));
        Assert.That(new EffectTimeline(chart()).PeakingDelayMs, Is.EqualTo(40), "unstated = 40 ms (ksh default)");
    }

    [Test]
    public void FilterSwitchesAndFilterGain()
    {
        var c = laserChart();
        c.Audio.LaserEffects.PulseEvents["low_pass_filter"] = new() { 240 };
        c.Audio.LaserEffects.PulseEvents["high_pass_filter"] = new() { 1920 };
        c.Audio.LaserEffects.LegacyFilterGain.Add(new PulseValue<double>(0, 1.0));
        var timing = new TimingMap(c.Beat);

        Assert.That(eval(c, timing.PulseToMs(100)).Laser.Kind, Is.EqualTo(EffectKind.PeakingFilter));
        Assert.That(eval(c, timing.PulseToMs(100)).Laser.Params[ParamId.Gain], Is.EqualTo(1.0).Within(1e-9), "pfiltergain 100 → gain 100%");
        var lpf = eval(c, timing.PulseToMs(480)).Laser;
        Assert.That(lpf.Kind, Is.EqualTo(EffectKind.LowPassFilter));
        Assert.That(lpf.Params[ParamId.Q], Is.EqualTo(5.2).Within(1e-9), "low-pass q 2.0..5.2 by filter gain");
        var hpf = eval(c, timing.PulseToMs(2000)).Laser;
        Assert.That(hpf.Kind, Is.EqualTo(EffectKind.HighPassFilter));
        Assert.That(hpf.Params[ParamId.Q], Is.EqualTo(8.0).Within(1e-9), "high-pass q 2.0..8.0");

        // a user definition of the same name is not touched by the legacy gain
        c.Audio.LaserEffects.Defs.Add(new AudioEffectDef("low_pass_filter", "low_pass_filter", new() { ["q"] = "1.0" }));
        Assert.That(eval(c, timing.PulseToMs(480)).Laser.Params[ParamId.Q], Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void LaserBitcrusherFollowsTheRange()
    {
        var c = laserChart();
        c.Audio.LaserEffects.PulseEvents["bitcrusher"] = new() { 0 };
        var timing = new TimingMap(c.Beat);
        var f = eval(c, timing.PulseToMs(720));
        Assert.That(f.Laser.Kind, Is.EqualTo(EffectKind.BitCrusher));
        Assert.That(f.Laser.Params[ParamId.Reduction], Is.EqualTo(22.5).Within(1e-9), "0samples-30samples at v 0.75");
    }

    [Test]
    public void FilterCurvesFollowKShootTables()
    {
        Assert.That(FilterCurve.PeakingFreq.Forward(0), Is.EqualTo(50).Within(1e-9));
        Assert.That(FilterCurve.PeakingFreq.Forward(1), Is.EqualTo(9000).Within(1e-6));
        Assert.That(FilterCurve.PeakingFreq.Forward(128 / 255.0), Is.EqualTo(1875.6437).Within(1e-3));
        Assert.That(FilterCurve.LowPassFreq.Forward(0.5), Is.InRange(3500, 3800));
        Assert.That(FilterCurve.HighPassFreq.Forward(0.5), Is.InRange(640, 690));
        Assert.That(FilterCurve.PeakingGainDb.Forward(89 / 255.0), Is.EqualTo(33.984).Within(1e-6));
        // inverse round trip, also outside the table
        foreach (double hz in new[] { 30.0, 50, 440, 9000, 12000 })
            Assert.That(FilterCurve.PeakingFreq.Forward(FilterCurve.PeakingFreq.Inverse(hz)), Is.EqualTo(hz).Within(hz * 1e-6));
        foreach (double hz in new[] { 20000.0, 15000, 2000, 800, 500 })
            Assert.That(FilterCurve.LowPassFreq.Forward(FilterCurve.LowPassFreq.Inverse(hz)), Is.EqualTo(hz).Within(hz * 1e-6));
        // monotonic in the laser value
        double prev = 0;
        for (int i = 0; i <= 50; i++)
        {
            double f = FilterCurve.PeakingFreq.Forward(i / 50.0);
            Assert.That(f, Is.GreaterThan(prev));
            prev = f;
        }
    }

    [Test]
    public void PreviewForcesAnEffect()
    {
        var c = chart();
        var input = new LiveEffectInput { PreviewName = "wobble", PreviewStartMs = 1000, PreviewEndMs = 3000 };
        Assert.That(eval(c, 1500, input).FxL.Kind, Is.EqualTo(EffectKind.Wobble));
        Assert.That(eval(c, 3500, input).FxL.Active, Is.False);
        input.PreviewName = "low_pass_filter";
        input.PreviewLaser = true;
        var f = eval(c, 2000, input);
        Assert.That(f.Laser.Kind, Is.EqualTo(EffectKind.LowPassFilter));
        Assert.That(f.Laser.V, Is.EqualTo(1).Within(1e-9), "the preview sweeps v 0 → 1 → 0");
    }
}
