namespace SDVXEditor.Core.Audio.Effects.Dsp;

/// <summary>
/// Shared part of the three laser filters (ksmaudio <c>PeakingFilterDSP</c> / <c>LowPassFilterDSP</c> /
/// <c>HighPassFilterDSP</c>): <c>v</c> eased by at most 0.01 per 44.1 kHz frame, mapped into the filter's table between
/// the positions of <c>freq</c> and <c>freq_max</c>; coefficients refreshed every <see cref="UPDATE"/> frames; the wet
/// signal is skipped (dry) beyond a threshold frequency where the filter would only boost rumble or do nothing.
/// Output lerp(x, filtered, mix). The filter keeps running while skipped so switching does not click.
/// </summary>
public abstract class LaserFilterDsp : EffectDsp
{
    public const int UPDATE = 16;
    private const double easing_per_frame = 0.01;

    protected Biquad Filter = new(2);
    protected double EasedV = double.NaN;

    /// <summary>The last cutoff in Hz (instrumentation, tests).</summary>
    public double CurrentFreq { get; protected set; }

    public override void Init(int sampleRate, int channels)
    {
        Filter = new Biquad(channels);
        base.Init(sampleRate, channels);
    }

    public override void Reset()
    {
        Filter.Reset();
        EasedV = double.NaN;
    }

    protected abstract FilterCurve Curve { get; }

    protected double FreqAt(double v, EffectParams p)
    {
        double a = Curve.Inverse(p[ParamId.Freq]), b = Curve.Inverse(p[ParamId.FreqMax]);
        return Curve.Forward(a + (b - a) * v);
    }

    /// <summary>Sets the coefficients for the eased value; returns whether the wet signal is used.</summary>
    protected abstract bool Configure(double easedV, double rawV, EffectParams p);

    /// <summary>Wet mix factor of a frame (the peaking filter's release tail overrides it).</summary>
    protected virtual double WetMix(bool use, double mix) => use ? mix : 0;

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        double target = Math.Clamp(p[ParamId.V], 0, 1);
        if (double.IsNaN(EasedV)) EasedV = target;
        double mix = Math.Clamp(p[ParamId.Mix], 0, 1);
        double step = easing_per_frame / RateScale * UPDATE;

        for (int i0 = 0; i0 < frames; i0 += UPDATE)
        {
            EasedV += Math.Clamp(target - EasedV, -step, step);
            bool use = Configure(EasedV, target, p);
            int n = Math.Min(UPDATE, frames - i0);
            for (int i = i0; i < i0 + n; i++)
            {
                double m = WetMix(use, mix);
                for (int ch = 0; ch < Channels; ch++)
                {
                    int k = i * Channels + ch;
                    float y = Filter.Process(s[k], ch);
                    s[k] = Lerp(s[k], y, m);
                }
            }
        }
    }
}

/// <summary>
/// Peaking filter (the default laser filter): cutoff 50 → 9000 Hz along K-Shoot's table, gain = the table's dB for the
/// (un-eased) laser value × <c>gain</c> (50 % default = <c>pfiltergain</c> 50), bandwidth in octaves (1.2). Below
/// 100 Hz the wet signal is skipped; after skipping starts, a 50 ms release keeps the last cutoff with the gain falling to 0.
/// </summary>
public sealed class PeakingFilterDsp : LaserFilterDsp
{
    private const double min_freq = 100, release_sec = 0.05;

    private double lastFreq, lastGainDb, lastMix;
    private long releaseFrames, releaseCounter = long.MaxValue;

    protected override FilterCurve Curve => FilterCurve.PeakingFreq;

    public override void Reset()
    {
        base.Reset();
        releaseCounter = long.MaxValue;
    }

    protected override bool Configure(double easedV, double rawV, EffectParams p)
    {
        releaseFrames = (long)(Rate * release_sec);
        double a = Curve.Inverse(p[ParamId.Freq]), b = Curve.Inverse(p[ParamId.FreqMax]);
        double freq = Curve.Forward(a + (b - a) * easedV);
        double baseGain = FilterCurve.PeakingGainDb.Forward(Math.Clamp(a + (b - a) * rawV, 0, 1));
        double gainDb = baseGain * Math.Clamp(p[ParamId.Gain], 0, 2);
        double bw = p[ParamId.Bandwidth] > 0 ? p[ParamId.Bandwidth] : 1.2;
        CurrentFreq = freq;

        if (freq >= min_freq && p[ParamId.Mix] > 0)
        {
            Filter.SetPeaking(freq, bw, gainDb, Rate);
            lastFreq = freq;
            lastGainDb = baseGain;
            lastMix = Math.Clamp(p[ParamId.Mix], 0, 1);
            releaseCounter = 0;
            return true;
        }

        // release tail: hold the last cutoff, gain decays linearly over 50 ms
        if (releaseCounter < releaseFrames)
        {
            double k = Math.Clamp(1 - (double)releaseCounter / releaseFrames, 0, 1);
            Filter.SetPeaking(lastFreq, bw, lastGainDb * k * Math.Clamp(p[ParamId.Gain], 0, 2), Rate);
            releaseCounter += UPDATE;
            return true;
        }

        Filter.SetPeaking(Math.Max(freq, 20), bw, 0, Rate);
        return false;
    }

    protected override double WetMix(bool use, double mix) => use ? (releaseCounter > 0 && releaseCounter < long.MaxValue ? lastMix : mix) : 0;
}

/// <summary>Low-pass laser filter: cutoff 15000 → 800 Hz along K-Shoot's table, q 3.6 (2.0..5.2 with <c>pfiltergain</c>); dry above 14800 Hz.</summary>
public sealed class LowPassFilterDsp : LaserFilterDsp
{
    protected override FilterCurve Curve => FilterCurve.LowPassFreq;

    protected override bool Configure(double easedV, double rawV, EffectParams p)
    {
        double freq = FreqAt(easedV, p);
        CurrentFreq = freq;
        Filter.SetLowPass(freq, p[ParamId.Q] > 0 ? p[ParamId.Q] : 3.6, Rate);
        return freq <= 14800 && p[ParamId.Mix] > 0;
    }
}

/// <summary>High-pass laser filter: cutoff 100 → 2200 Hz along K-Shoot's table, q 5.0 (2.0..8.0 with <c>pfiltergain</c>); dry below 200 Hz.</summary>
public sealed class HighPassFilterDsp : LaserFilterDsp
{
    protected override FilterCurve Curve => FilterCurve.HighPassFreq;

    protected override bool Configure(double easedV, double rawV, EffectParams p)
    {
        double freq = FreqAt(easedV, p);
        CurrentFreq = freq;
        Filter.SetHighPass(freq, p[ParamId.Q] > 0 ? p[ParamId.Q] : 5.0, Rate);
        return freq >= 200 && p[ParamId.Mix] > 0;
    }
}

/// <summary>
/// Wobble (ksmaudio <c>WobbleDSP</c>): a low-pass whose cutoff follows lerp(freq_2, freq_1, lfo), lfo = a triangle over
/// <c>wave_length</c> from the last bar line, shaped by sin(π/2·x) and sin(π/2.25·x)/sin(π/2.25); q = <c>q</c>.
/// Coefficients every 8 frames. <c>wave_length</c> 0 bypasses. Output lerp(x, filtered, mix).
/// </summary>
public sealed class WobbleDsp : EffectDsp
{
    private const int update = 8;
    private static readonly double sin_pi_2_25 = Math.Sin(Math.PI / 2.25);
    private Biquad filter = new(2);

    public override void Init(int sampleRate, int channels)
    {
        filter = new Biquad(channels);
        base.Init(sampleRate, channels);
    }

    public override void Reset() => filter.Reset();

    public static double Lfo(double phase)
    {
        double tri = Triangle(phase);
        double x = Math.Sin(tri * Math.PI / 2);
        return Math.Sin(x * Math.PI / 2.25) / sin_pi_2_25;
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        double periodFrames = p[ParamId.WaveLength] * Rate;
        double mix = p[ParamId.Mix];
        if (periodFrames < 1 || mix <= 0) return;
        double since = time.FramesSince(slot.TriggerMs);
        double lo = p[ParamId.Freq1], hi = p[ParamId.Freq2], q = Math.Max(0.1, p[ParamId.Q]);

        for (int i0 = 0; i0 < frames; i0 += update)
        {
            double lfo = Lfo((since + i0) / periodFrames);
            filter.SetLowPass(hi + (lo - hi) * lfo, q, Rate);
            int n = Math.Min(update, frames - i0);
            for (int i = i0; i < i0 + n; i++)
                for (int ch = 0; ch < Channels; ch++)
                {
                    int k = i * Channels + ch;
                    s[k] = Lerp(s[k], filter.Process(s[k], ch), mix);
                }
        }
    }
}

/// <summary>
/// Phaser (kson spec + unnamed-sdvx-clone <c>PhaserDSP</c>): <c>stage</c> (even, ≤ 12) all-pass biquads with q =
/// <c>q</c> at a frequency swept logarithmically between <c>freq_1</c> and <c>freq_2</c> by a triangle LFO of
/// <c>period</c> from the press (right channel offset by <c>stereo_width</c> of a cycle), <c>feedback</c> of the chain
/// output, a high shelf of <c>hi_cut_gain</c> dB at the band's geometric centre. Output lerp(x, chain, mix): the spec's
/// default 50 % is the full notch depth ("effectively doubled").
/// </summary>
public sealed class PhaserDsp : EffectDsp
{
    private const int update = 16;
    private Biquad[][] stages = Array.Empty<Biquad[]>();
    private Biquad[] shelf = Array.Empty<Biquad>();
    private double[] last = Array.Empty<double>();

    public override void Init(int sampleRate, int channels)
    {
        stages = Enumerable.Range(0, channels).Select(_ => Enumerable.Range(0, 12).Select(_ => new Biquad(1)).ToArray()).ToArray();
        shelf = Enumerable.Range(0, channels).Select(_ => new Biquad(1)).ToArray();
        last = new double[channels];
        base.Init(sampleRate, channels);
    }

    public override void Reset()
    {
        foreach (var st in stages)
            foreach (var b in st) b.Reset();
        foreach (var b in shelf) b.Reset();
        Array.Clear(last);
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        double mix = p[ParamId.Mix];
        int count = (int)Math.Clamp(p[ParamId.Stage], 0, 12) / 2 * 2;
        if (mix <= 0 || count == 0) return;
        double periodFrames = p[ParamId.Period] * Rate;
        double f1 = Math.Max(10, p[ParamId.Freq1]), f2 = Math.Max(10, p[ParamId.Freq2]);
        double q = Math.Clamp(p[ParamId.Q], 0.1, 50), fb = Math.Clamp(p[ParamId.Feedback], 0, 0.95);
        double width = p[ParamId.StereoWidth];
        double since = time.FramesSince(slot.ActivatedMs);
        double centre = Math.Sqrt(f1 * f2);

        for (int i0 = 0; i0 < frames; i0 += update)
        {
            for (int ch = 0; ch < Channels; ch++)
            {
                double phase = periodFrames >= 1 ? (since + i0) / periodFrames : 0;
                if (ch == 1) phase += width;
                double f = f1 * Math.Pow(f2 / f1, Triangle(phase));
                for (int k = 0; k < count; k++) stages[ch][k].SetAllPass(f, q, Rate);
                shelf[ch].SetHighShelf(centre, Math.Min(0, p[ParamId.HiCutGain]), Rate);
            }

            int n = Math.Min(update, frames - i0);
            for (int i = i0; i < i0 + n; i++)
                for (int ch = 0; ch < Channels; ch++)
                {
                    int k = i * Channels + ch;
                    float y = (float)(s[k] + fb * last[ch]);
                    for (int st = 0; st < count; st++) y = stages[ch][st].Process(y, 0);
                    last[ch] = y;
                    y = shelf[ch].Process(y, 0);
                    s[k] = Lerp(s[k], y, mix);
                }
        }
    }
}

/// <summary>
/// Flanger (kson spec, unnamed-sdvx-clone <c>FlangerDSP</c>): delay between <c>delay</c> and <c>delay</c> +
/// <c>depth</c> samples (44.1 kHz units) moved by a triangle LFO of <c>period</c> from the press (right channel
/// <c>stereo_width</c> of a cycle later), <c>feedback</c> into the delay line; wet = (x + delayed) · <c>vol</c>,
/// output lerp(x, wet, mix). <c>period</c> 0 holds the LFO.
/// </summary>
public sealed class FlangerDsp : EffectDsp
{
    private const int size = 4096;
    private float[] line = Array.Empty<float>();
    private long write;

    public override void Init(int sampleRate, int channels)
    {
        line = new float[size * channels];
        base.Init(sampleRate, channels);
    }

    public override void Reset()
    {
        Array.Clear(line);
        write = 0;
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        double mix = p[ParamId.Mix];
        if (mix <= 0) return;
        double min = Math.Abs(p[ParamId.Delay]) * RateScale, depth = Math.Abs(p[ParamId.Depth]) * RateScale;
        min = Math.Clamp(min, 0, size / 2);
        depth = Math.Clamp(depth, 0, size / 2 - 2);
        double fb = Math.Clamp(p[ParamId.Feedback], 0, 0.95), vol = p[ParamId.Vol], width = p[ParamId.StereoWidth];
        double periodFrames = p[ParamId.Period] * Rate;
        double since = time.FramesSince(slot.ActivatedMs);

        for (int i = 0; i < frames; i++)
        {
            int w = (int)(write & (size - 1)) * Channels;
            for (int ch = 0; ch < Channels; ch++)
            {
                double phase = periodFrames >= 1 ? (since + i) / periodFrames : 0;
                if (ch == 1) phase += width;
                double d = Math.Max(1, min + depth * Triangle(phase));
                double pos = write - d;
                long a = (long)Math.Floor(pos);
                double f = pos - a;
                float x0 = line[(int)(a & (size - 1)) * Channels + ch], x1 = line[(int)((a + 1) & (size - 1)) * Channels + ch];
                float delayed = (float)(x0 + (x1 - x0) * f);
                int k = i * Channels + ch;
                float x = s[k];
                line[w + ch] = (float)(x + fb * delayed);
                s[k] = Lerp(x, (float)((x + delayed) * vol), mix);
            }
            write++;
        }
    }
}
