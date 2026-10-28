using NUnit.Framework;
using SDVXEditor.Core.Audio;

namespace SDVXEditor.Core.Tests;

public class SpectralAnalysisTests
{
    private const int rate = 44100;

    private static float[] signal(double seconds, Func<double, double> f)
    {
        var s = new float[(int)(seconds * rate)];
        for (int i = 0; i < s.Length; i++) s[i] = (float)f((double)i / rate);
        return s;
    }

    private static int dominant(SpectralAnalysis a, double fromMs, double toMs) => SpectralPalette.Column(a, fromMs, toMs).Dominant;

    [Test]
    public void TonesAndNoiseLandInTheirBands()
    {
        var rng = new Random(3);
        var a = SpectralAnalysis.FromMono(signal(3, t =>
            t is >= 0.2 and < 0.8 ? 0.8 * Math.Sin(2 * Math.PI * 50 * t)
            : t is >= 1.2 and < 1.8 ? 0.5 * Math.Sin(2 * Math.PI * 2000 * t)
            : t is >= 2.2 and < 2.8 ? 0.5 * (rng.NextDouble() * 2 - 1)
            : 0), rate);

        Assert.That(a.LengthMs, Is.EqualTo(3000).Within(2));

        var low = a.BandsIn(400, 600);
        Assert.That(low.Low, Is.GreaterThan(low.Mid * 20), "50 Hz is low band");
        var mid = a.BandsIn(1400, 1600);
        Assert.That(mid.Mid, Is.GreaterThan(mid.High * 5).And.GreaterThan(mid.Low * 20), "2 kHz is mid band");
        var high = a.BandsIn(2400, 2600);
        Assert.That(high.High, Is.GreaterThan(high.Mid), "white noise is mostly above 4 kHz");

        for (double ms = 300; ms < 700; ms += 5) Assert.That(dominant(a, ms, ms + 5), Is.EqualTo(0), $"red at {ms}");
        for (double ms = 1300; ms < 1700; ms += 5) Assert.That(dominant(a, ms, ms + 5), Is.EqualTo(1), $"green at {ms}");
        for (double ms = 2300; ms < 2700; ms += 5) Assert.That(dominant(a, ms, ms + 5), Is.EqualTo(2), $"blue at {ms}");

        Assert.That(SpectralPalette.Column(a, 1000, 1005).Height, Is.Zero, "silence draws nothing");
    }

    /// <summary>A synthesised drum loop: kick (150→45 Hz sweep), snare (190 Hz body + noise), closed hat (noise above 7 kHz).</summary>
    [Test]
    public void DrumLoopColoursKickRedSnareNotRedHatBlue()
    {
        var rng = new Random(5);
        double hp = 0, prevNoise = 0;
        const double beat = 0.5; // 120 BPM: kick on beats, snare on 2 and 4, hats on the 8th offbeats

        var s = signal(8, t =>
        {
            double b = t % (2 * beat);
            double kick = b < beat ? 0.9 * Math.Sin(2 * Math.PI * (45 * b + (150 - 45) / 25.0 * (1 - Math.Exp(-25 * b)))) * Math.Exp(-8 * b) : 0;
            double sb = b - beat;
            double noise = rng.NextDouble() * 2 - 1;
            double snare = sb >= 0 ? (0.5 * Math.Sin(2 * Math.PI * 190 * sb) * Math.Exp(-20 * sb) + 0.5 * noise * Math.Exp(-15 * sb)) : 0;
            // crude high-pass for the hat: first difference of white noise
            hp = noise - prevNoise;
            prevNoise = noise;
            double hb = t % beat - beat / 2;
            double hat = hb >= 0 ? 0.3 * hp * Math.Exp(-60 * hb) : 0;
            return kick + snare + hat;
        });
        var a = SpectralAnalysis.FromMono(s, rate);

        for (int bar = 2; bar < 14; bar += 2)
        {
            double kickMs = bar * beat * 1000;
            Assert.That(dominant(a, kickMs, kickMs + 15), Is.EqualTo(0), $"kick at {kickMs} ms is red");
            double snareMs = kickMs + beat * 1000;
            Assert.That(dominant(a, snareMs, snareMs + 15), Is.Not.EqualTo(0), $"snare at {snareMs} ms is green/blue, not red");
            double hatMs = kickMs + beat * 500;
            Assert.That(dominant(a, hatMs, hatMs + 10), Is.EqualTo(2), $"hat at {hatMs} ms is blue");
        }
    }

    [Test]
    public void KicksAreLobesWithGapsBetweenBeats()
    {
        const double beat = 0.5;
        var a = SpectralAnalysis.FromMono(signal(4, t =>
        {
            double b = t % beat;
            return 0.9 * Math.Sin(2 * Math.PI * (45 * b + (150 - 45) / 25.0 * (1 - Math.Exp(-25 * b)))) * Math.Exp(-12 * b) + 0.02 * Math.Sin(2 * Math.PI * 440 * t);
        }), rate);

        for (int k = 1; k < 7; k++)
        {
            double on = k * beat * 1000;
            float atKick = SpectralPalette.Column(a, on, on + 15).Height;
            float gap = SpectralPalette.Column(a, on + 350, on + 365).Height;
            Assert.That(atKick, Is.GreaterThan(0.8f), $"kick {k} is tall");
            Assert.That(gap, Is.LessThan(atKick * 0.35f).And.GreaterThan(0), $"the gap after kick {k} is low but visible");
        }
    }

    [Test]
    public void StreamingStereoEqualsMonoInOneGo()
    {
        var rng = new Random(9);
        var mono = signal(1.3, t => Math.Sin(2 * Math.PI * 80 * t) * 0.5 + (rng.NextDouble() - 0.5) * 0.2);
        var stereo = new float[mono.Length * 2];
        for (int i = 0; i < mono.Length; i++) stereo[2 * i] = stereo[2 * i + 1] = mono[i];

        var b = new SpectralAnalysis.Builder(rate, 2);
        // odd chunk sizes: chunks end in the middle of a frame
        for (int at = 0; at < stereo.Length; at += 1001)
            b.Add(stereo.AsSpan(at, Math.Min(1001, stereo.Length - at)));
        var streamed = b.Build();
        var once = SpectralAnalysis.FromMono(mono, rate);

        Assert.That(streamed.BlockCount, Is.EqualTo(once.BlockCount));
        foreach (double ms in new[] { 0.0, 100, 517, 1200 })
        {
            Assert.That(streamed.PeakIn(ms, ms + 20), Is.EqualTo(once.PeakIn(ms, ms + 20)).Within(1e-6));
            Assert.That(streamed.LevelIn(ms, ms + 20), Is.EqualTo(once.LevelIn(ms, ms + 20)).Within(1e-6));
            Assert.That(streamed.BandsIn(ms, ms + 20).Low, Is.EqualTo(once.BandsIn(ms, ms + 20).Low).Within(1e-6));
        }
    }

    [Test]
    public void QueriesOutsideTheSongAreSilent()
    {
        var a = SpectralAnalysis.FromMono(signal(0.5, t => 0.5), rate);
        Assert.That(a.PeakIn(-100, -50), Is.Zero);
        Assert.That(a.LevelIn(600, 700), Is.Zero);
        Assert.That(a.BandsIn(600, 700), Is.EqualTo(default(BandLevels)));
        Assert.That(SpectralPalette.Column(a, 600, 700), Is.EqualTo(default(SpectralColumn)));
    }

    [Test]
    public void HeightCurve()
    {
        Assert.That(SpectralPalette.Height(1), Is.EqualTo(1).Within(1e-6));
        Assert.That(SpectralPalette.Height(2), Is.EqualTo(1).Within(1e-6), "clamped");
        Assert.That(SpectralPalette.Height(0), Is.Zero);
        float m3 = SpectralPalette.Height(MathF.Pow(10, -3 / 20f));
        Assert.That(m3, Is.InRange(0.5f, 0.65f), "−3 dB is clearly shorter (lobes)");
        float m20 = SpectralPalette.Height(0.1f);
        Assert.That(m20, Is.InRange(0.08f, 0.2f), "−20 dB stays visible");

        float last = 0;
        for (float x = 0.001f; x <= 1; x *= 1.1f)
        {
            float h = SpectralPalette.Height(x);
            Assert.That(h, Is.GreaterThanOrEqualTo(last), "monotonic");
            last = h;
        }
    }
}
