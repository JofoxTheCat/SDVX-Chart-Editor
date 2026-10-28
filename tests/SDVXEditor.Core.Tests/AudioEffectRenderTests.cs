using System.Diagnostics;
using NUnit.Framework;
using SDVXEditor.Core.Audio.Effects;
using SDVXEditor.Core.Audio.Effects.Dsp;
using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// Offline renders: the effect rack runs over generated sine / noise buffers (as the audio thread would, in 2048-frame
/// blocks) and the tests check measurable outcomes of each effect.
/// </summary>
[TestFixture]
public class AudioEffectRenderTests
{
    private const int rate = 44100;
    private const double bpm = 120; // whole note = 2000 ms

    private static Chart chart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, bpm));
        return c;
    }

    private static float[] sine(double seconds, double hz, float amp = 0.5f)
    {
        int n = (int)(seconds * rate);
        var s = new float[n * 2];
        for (int i = 0; i < n; i++) s[2 * i] = s[2 * i + 1] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate));
        return s;
    }

    private static float[] noise(double seconds, int seed = 1, float amp = 0.3f)
    {
        int n = (int)(seconds * rate);
        var s = new float[n * 2];
        var r = new Random(seed);
        for (int i = 0; i < n; i++) s[2 * i] = s[2 * i + 1] = (float)(amp * (r.NextDouble() * 2 - 1));
        return s;
    }

    private static (float[] Output, EffectRack Rack) render(Chart c, float[] input, LiveEffectInput? live = null, int block = 2048)
    {
        var rack = new EffectRack(rate, 2) { Timeline = new EffectTimeline(c) };
        if (live != null) rack.Input = live;
        var output = (float[])input.Clone();
        for (int f = 0; f < output.Length / 2; f += block)
        {
            int n = Math.Min(block, output.Length / 2 - f);
            rack.Process(output.AsSpan(f * 2, n * 2), f);
        }
        return (output, rack);
    }

    /// <summary>RMS of the left channel over [from, to) seconds.</summary>
    private static double rms(float[] s, double from, double to)
    {
        int a = (int)(from * rate), b = Math.Min(s.Length / 2, (int)(to * rate));
        double sum = 0;
        for (int i = a; i < b; i++) sum += s[2 * i] * s[2 * i];
        return Math.Sqrt(sum / Math.Max(1, b - a));
    }

    /// <summary>RMS of the first difference (a crude high-band level: +6 dB/oct).</summary>
    private static double highBand(float[] s, double from, double to)
    {
        int a = Math.Max(1, (int)(from * rate)), b = Math.Min(s.Length / 2, (int)(to * rate));
        double sum = 0;
        for (int i = a; i < b; i++)
        {
            double d = s[2 * i] - s[2 * (i - 1)];
            sum += d * d;
        }
        return Math.Sqrt(sum / Math.Max(1, b - a));
    }

    /// <summary>Level of one frequency (Goertzel) over [from, to) seconds, left channel.</summary>
    private static double tone(float[] s, double hz, double from, double to)
    {
        int a = (int)(from * rate), b = Math.Min(s.Length / 2, (int)(to * rate));
        double w = 2 * Math.PI * hz / rate, coeff = 2 * Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = a; i < b; i++)
        {
            double s0 = s[2 * i] + coeff * s1 - s2;
            s2 = s1;
            s1 = s0;
        }
        return Math.Sqrt(s1 * s1 + s2 * s2 - coeff * s1 * s2) / (b - a);
    }

    private static int zeroCrossings(float[] s, double from, double to)
    {
        int a = (int)(from * rate), b = Math.Min(s.Length / 2, (int)(to * rate)), n = 0;
        for (int i = a + 1; i < b; i++)
            if ((s[2 * i - 2] < 0) != (s[2 * i] < 0)) n++;
        return n;
    }

    private static double db(double ratio) => 20 * Math.Log10(ratio);

    private static void addHold(Chart c, int lane, long y, long len, string effect, Dictionary<string, string>? p = null)
        => c.Notes.Fx[lane].Add(new Note(y, len) { Effects = { new FxEffectEvent(effect, 0, p ?? new()) } });

    private static void constantLaser(Chart c, int lane, long y, long len, double v)
        => c.Notes.Lasers[lane].Add(new LaserSection(y, new[] { new LaserPoint(0, v), new LaserPoint(len, v) }));

    // ---------------------------------------------------------------------------------------------

    [Test]
    public void NoEffectIsBitIdentical()
    {
        var c = chart();
        addHold(c, 0, 1920, 960, "gate", new() { ["wave_length"] = "1/8" }); // after the rendered range
        var input = noise(1.5);
        var (output, rack) = render(c, input);
        Assert.That(output, Is.EqualTo(input), "no active effect = untouched samples");
        Assert.That(rack.ActiveFrames, Is.Zero);
    }

    [Test]
    public void LowPassLaserLowersTheHighBand()
    {
        var c = chart();
        constantLaser(c, 0, 0, 3840, 1.0);
        c.Audio.LaserEffects.PulseEvents["low_pass_filter"] = new() { 0 };
        var input = noise(2);
        var (output, rack) = render(c, input);
        double dry = highBand(input, 0.5, 2), wet = highBand(output, 0.5, 2);
        TestContext.Out.WriteLine($"LPF at v=1: high band {db(wet / dry):0.0} dB, cutoff {rack.LastLaserFreq:0} Hz");
        Assert.That(db(wet / dry), Is.LessThan(-12), "cutoff 800 Hz takes most of the high band away");
        Assert.That(rack.LastLaserFreq, Is.EqualTo(800).Within(1));
        Assert.That(rack.FramesByKind[(int)EffectKind.LowPassFilter], Is.GreaterThan(0));
    }

    [Test]
    public void HighPassLaserLowersALowTone()
    {
        var c = chart();
        constantLaser(c, 0, 0, 3840, 1.0);
        c.Audio.LaserEffects.PulseEvents["high_pass_filter"] = new() { 0 };
        var input = sine(2, 100);
        var (output, _) = render(c, input);
        Assert.That(db(rms(output, 0.5, 2) / rms(input, 0.5, 2)), Is.LessThan(-15), "100 Hz under a 2200 Hz high-pass");
    }

    [Test]
    public void PeakingFilterBoostsAroundItsCutoff()
    {
        var c = chart();
        constantLaser(c, 0, 0, 3840, 0.5);
        c.Audio.LaserEffects.PeakingFilterDelay = 0;
        var input = noise(2);
        var (output, rack) = render(c, input);
        double f = rack.LastLaserFreq;
        Assert.That(f, Is.EqualTo(FilterCurve.PeakingFreq.Forward(0.5)).Within(1));
        double boost = db(tone(output, f, 0.5, 2) / tone(input, f, 0.5, 2));
        double far = db(tone(output, f * 6, 0.5, 2) / tone(input, f * 6, 0.5, 2));
        TestContext.Out.WriteLine($"peaking at {f:0} Hz: +{boost:0.0} dB at the cutoff, {far:0.0} dB 2.6 octaves above");
        Assert.That(boost, Is.GreaterThan(8), "≈ 0.5 × 33 dB at the cutoff");
        Assert.That(Math.Abs(far), Is.LessThan(4));
    }

    [Test]
    public void GateSilencesAtTheExpectedRate()
    {
        var c = chart();
        addHold(c, 0, 0, 1920 * 2, "gate", new() { ["wave_length"] = "1/8", ["mix"] = "0%>100%" }); // 250 ms cycle, 50 % open
        var input = sine(3, 440);
        var (output, _) = render(c, input);

        int silent = 0, loud = 0;
        for (double t = 0.5; t < 2.5; t += 0.25)
        {
            loud += rms(output, t + 0.01, t + 0.115) > 0.3 ? 1 : 0;
            silent += rms(output, t + 0.135, t + 0.245) < 0.01 ? 1 : 0;
        }
        Assert.That(loud, Is.EqualTo(8), "open first half of every 250 ms cycle");
        Assert.That(silent, Is.EqualTo(8), "silent second half: 4 gaps per second");
    }

    [Test]
    public void RetriggerRepeatsTheLoop()
    {
        var c = chart();
        // hold from beat 2 (500 ms); update_period 1/2 → the loop restarts at 0 ms grid points of 1000 ms
        addHold(c, 1, 480, 1440, "retrigger", new() { ["wave_length"] = "1/16", ["rate"] = "100%" }); // 125 ms loop
        var input = noise(2.5, 7);
        var (output, _) = render(c, input);

        // from the grid point at 1000 ms: [1.000, 1.125) is the source; [1.125, 1.250) must repeat it
        int a = (int)(1.0 * rate), l = (int)(0.125 * rate);
        double err = 0, energy = 0;
        for (int i = 10; i < l - 10; i++)
        {
            double x = output[2 * (a + i)], y = output[2 * (a + l + i)];
            err += (x - y) * (x - y);
            energy += x * x;
        }
        Assert.That(err / energy, Is.LessThan(1e-6), "the second loop is the first one again");
        Assert.That(output[2 * (a + 5)], Is.EqualTo(input[2 * (a + 5)]).Within(1e-6), "the first loop is the live input");
        // before the hold nothing happens
        Assert.That(rms(output, 0, 0.45), Is.EqualTo(rms(input, 0, 0.45)).Within(1e-9));
    }

    [Test]
    public void SideChainDucksAfterEachTrigger()
    {
        var c = chart();
        addHold(c, 0, 0, 3840, "sidechain"); // period 1/4 = 500 ms, attack 10 ms, hold 50 ms, release 1/8 = 250 ms, ratio 5
        var input = sine(2, 440);
        var (output, _) = render(c, input);
        double ducked = rms(output, 0.512, 0.558), open = rms(output, 0.35, 0.49);
        Assert.That(ducked / open, Is.EqualTo(0.2).Within(0.03), "gain 1/ratio during the hold");
        Assert.That(open / rms(input, 0.35, 0.49), Is.EqualTo(1).Within(0.02), "released before the next trigger");
    }

    [Test]
    public void BitCrusherHoldsSamples()
    {
        var c = chart();
        addHold(c, 0, 0, 1920, "bitcrusher", new() { ["reduction"] = "10samples" });
        var input = noise(1);
        var (output, _) = render(c, input);
        int changes = 0;
        int a = (int)(0.2 * rate), b = (int)(0.8 * rate);
        for (int i = a + 1; i < b; i++)
            if (output[2 * i] != output[2 * i - 2]) changes++;
        double perChange = (double)(b - a) / changes;
        Assert.That(perChange, Is.EqualTo(10).Within(0.2), "a new sample every 10");
    }

    [Test]
    public void PitchShiftRaisesAnOctave()
    {
        var c = chart();
        addHold(c, 0, 0, 1920, "pitch_shift"); // pitch 0>12, mix 0%>100%
        var input = sine(1.5, 440);
        var (output, _) = render(c, input);
        double inZc = zeroCrossings(input, 0.3, 0.9), outZc = zeroCrossings(output, 0.3, 0.9);
        double ratio = outZc / inZc;
        double at880 = tone(output, 880, 0.3, 0.9), at440 = tone(output, 440, 0.3, 0.9);
        TestContext.Out.WriteLine($"pitch shift: zero-crossing ratio {ratio:0.000}, 880 Hz {db(at880 / at440):0.0} dB over 440 Hz");
        Assert.That(at880, Is.GreaterThan(at440 * 4), "the octave dominates");
    }

    [Test]
    public void TapeStopRunsDown()
    {
        var c = chart();
        addHold(c, 0, 0, 3840, "tapestop", new() { ["speed"] = "10%" });
        var input = sine(3, 440);
        var (output, _) = render(c, input);
        double early = rms(output, 0.0, 0.05), late = rms(output, 2.0, 2.5);
        Assert.That(early, Is.GreaterThan(0.3));
        Assert.That(late, Is.LessThan(0.01), "the tape has stopped");
        // the pitch falls while it slows
        Assert.That(zeroCrossings(output, 0.3, 0.4), Is.LessThan(zeroCrossings(input, 0.3, 0.4)));
    }

    [Test]
    public void EchoRepeatsWithFeedback()
    {
        var c = chart();
        addHold(c, 0, 0, 3840, "echo", new() { ["wave_length"] = "1/8" }); // 250 ms, feedback 60 %
        int n = 2 * rate;
        var input = new float[n * 2];
        input[2 * 1000] = input[2 * 1000 + 1] = 1f; // an impulse
        var (output, _) = render(c, input);
        int l = (int)(0.25 * rate);
        Assert.That(output[2 * 1000], Is.EqualTo(1f).Within(1e-6));
        Assert.That(output[2 * (1000 + l)], Is.EqualTo(0.6f).Within(1e-4), "first repeat at the feedback level");
        Assert.That(output[2 * (1000 + 2 * l)], Is.EqualTo(0.36f).Within(1e-4), "second repeat");
    }

    [Test]
    public void WobblePhaserFlangerChangeTheSignalAndStayFinite()
    {
        foreach (var (name, p) in new (string, Dictionary<string, string>)[]
                 {
                     ("wobble", new() { ["wave_length"] = "1/8" }),
                     ("phaser", new()),
                     ("flanger", new()),
                 })
        {
            var c = chart();
            addHold(c, 0, 0, 3840, name, p);
            var input = noise(2, 3);
            var (output, rack) = render(c, input);
            Assert.That(output.All(float.IsFinite), name);
            Assert.That(output.Max(Math.Abs), Is.LessThan(4), name);
            double diff = 0;
            for (int i = 0; i < output.Length; i++) diff += Math.Abs(output[i] - input[i]);
            Assert.That(diff / output.Length, Is.GreaterThan(0.01), name + " changes the sound");
            Assert.That(rack.FramesByKind[(int)EffectSpecs.KindOf(name)], Is.GreaterThan(rate), name);
        }

        // wobble: the cutoff sweeps — the high band rises and falls within a cycle
        var w = chart();
        addHold(w, 0, 0, 3840, "wobble", new() { ["wave_length"] = "1/4", ["mix"] = "0%>100%" }); // 500 ms
        var (wo, _) = render(w, noise(2, 5));
        double atLine = highBand(wo, 1.0, 1.03), mid = highBand(wo, 1.235, 1.265);
        Assert.That(atLine / mid, Is.GreaterThan(3), "open at the bar line (lfo 0 = freq_2), closed mid-cycle (freq_1)");
    }

    [Test]
    public void PlayerInputGatesTheEffect()
    {
        var c = chart();
        addHold(c, 0, 0, 3840, "bitcrusher", new() { ["reduction"] = "20samples" });
        var input = noise(1);
        var live = new LiveEffectInput { ChartDriven = false, GraceMs = 30 };
        var (output, _) = render(c, input, live);
        Assert.That(rms(output, 0.2, 0.9), Is.EqualTo(rms(input, 0.2, 0.9)).Within(1e-9), "not pressed = dry");
        live.FxDown[0] = true;
        var (pressed, _) = render(c, input, live);
        Assert.That(Math.Abs(rms(pressed, 0.2, 0.9) - rms(input, 0.2, 0.9)), Is.GreaterThan(1e-4));
    }

    [Test]
    public void SeekResetsAndKeepsWorking()
    {
        var c = chart();
        addHold(c, 0, 0, 3840, "gate", new() { ["wave_length"] = "1/8", ["mix"] = "0%>100%" });
        var rack = new EffectRack(rate, 2) { Timeline = new EffectTimeline(c) };
        var a = sine(0.1, 440);
        rack.Process(a, 0);
        rack.Process(sine(0.1, 440), (long)(1.0 * rate)); // jump
        Assert.That(rack.Seeks, Is.EqualTo(1));
        Assert.That(rack.Activations, Is.EqualTo(2));
    }

    [Test]
    public void SmallDecoderSlipsDoNotReset()
    {
        var c = chart();
        addHold(c, 0, 0, 3840, "flanger");
        var rack = new EffectRack(rate, 2) { Timeline = new EffectTimeline(c) };
        rack.Process(sine(2048.0 / rate, 440), 0);
        rack.Process(sine(392.0 / rate, 440), 2048 - 49); // MP3 right after a seek: blocks ~50 frames back
        rack.Process(sine(392.0 / rate, 440), 2048 - 49 + 392 + 30);
        Assert.That(rack.Seeks, Is.Zero);
        Assert.That(rack.Slips, Is.EqualTo(2));
        Assert.That(rack.Activations, Is.EqualTo(1), "the flanger keeps its state");
    }

    [Test]
    public void CostPerSecondOfAudio()
    {
        var c = chart();
        addHold(c, 0, 0, 3840 * 8, "phaser");
        addHold(c, 1, 0, 3840 * 8, "pitch_shift");
        constantLaser(c, 0, 0, 3840 * 8, 0.6);
        var input = noise(30);
        var sw = Stopwatch.StartNew();
        var (_, rack) = render(c, input);
        sw.Stop();
        double realtime = 30_000 / sw.Elapsed.TotalMilliseconds;
        double maxBlockMs = rack.MaxBlockTicks * 1000.0 / Stopwatch.Frequency;
        TestContext.Out.WriteLine($"3 slots (phaser, pitch shift, peaking filter): {realtime:0}x real time, worst 2048-frame block {maxBlockMs:0.00} ms (block = 46 ms of audio)");
        Assert.That(realtime, Is.GreaterThan(20), "well below the audio thread's budget");
    }
}
