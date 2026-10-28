namespace SDVXEditor.Core.Audio;

/// <summary>RMS amplitude of the three bands of a stretch of audio (linear, full scale = 1).</summary>
public readonly record struct BandLevels(float Low, float Mid, float High);

/// <summary>
/// Loudness and spectral balance of a song for the timeline waveform, at 1 ms resolution.
/// <para>
/// Built from decoded PCM (any channel count; mixed to mono) in one streaming pass. Per block of
/// <see cref="BlockSamples"/> samples (≈1 ms) it keeps the peak of the mono mix and the energy of three bands,
/// split by Linkwitz-Riley 4th-order crossovers (two cascaded Butterworth biquads per edge) at
/// <see cref="LowCrossoverHz"/> and <see cref="HighCrossoverHz"/>. Filters instead of an FFT keep the time
/// resolution of a transient: a kick starts in the block it starts in, not smeared over a 1024-sample window
/// (the framework's own waveform takes one FFT1024 per 23 ms and its low band is 2 FFT bins, 0–86 Hz).
/// </para>
/// <para>
/// Queries take any time range: <see cref="PeakIn"/> is the loudest block (level of detail by peak, so a kick
/// stays a spike at any zoom), <see cref="BandsIn"/> the band RMS over the range, widened to at least
/// <see cref="MinBandWindowMs"/> — a 50 Hz kick has a 20 ms period, a single 1 ms block says nothing about it.
/// Band energies are stored as prefix sums, so every query is O(blocks in range) for the peak and O(1) for the bands.
/// </para>
/// </summary>
public sealed class SpectralAnalysis
{
    /// <summary>
    /// Low / mid edge: kick and 808 fundamentals and the kick's body (40–200 Hz) are below it. Measured on the
    /// Nautica songs (CHANGELOG): 150 Hz left most J-core / hardcore kicks mid-dominant (their body sits at
    /// 100–250 Hz), 200 Hz made about half of all low-band onsets red-dominant, 250 Hz pulled snare bodies in.
    /// </summary>
    public const double LowCrossoverHz = 200;

    /// <summary>Mid / high edge: vocals, leads and snare body below it, hats, cymbals and the snare's noise above.</summary>
    public const double HighCrossoverHz = 4000;

    /// <summary>Band RMS is taken over at least this window (centred on the query), see the class summary.</summary>
    public const double MinBandWindowMs = 16;

    public int SampleRate { get; }

    /// <summary>Samples per block (sample rate / 1000, so ≈1 ms).</summary>
    public int BlockSamples { get; }

    public double BlockMs => BlockSamples * 1000.0 / SampleRate;

    public int BlockCount => peak.Length;

    public double LengthMs => BlockCount * BlockMs;

    /// <summary>Loud reference level: the 99.5th percentile of the block peaks (a few clipped spikes do not shrink the whole song).</summary>
    public float ReferencePeak { get; }

    /// <summary>Band RMS over the whole song (for calibration and tests).</summary>
    public BandLevels Overall { get; }

    private readonly float[] peak;
    private readonly double[] lowPrefix, midPrefix, highPrefix, fullPrefix;

    private SpectralAnalysis(int sampleRate, int blockSamples, float[] peak, double[] low, double[] mid, double[] high, double[] full)
    {
        fullPrefix = full;
        SampleRate = sampleRate;
        BlockSamples = blockSamples;
        this.peak = peak;
        lowPrefix = low;
        midPrefix = mid;
        highPrefix = high;

        if (peak.Length > 0)
        {
            var sorted = (float[])peak.Clone();
            Array.Sort(sorted);
            ReferencePeak = Math.Max(1e-4f, sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.995))]);
        }
        else
            ReferencePeak = 1;

        Overall = bands(0, peak.Length);

        // short-term loudness: RMS of a 10 ms window centred on every block
        int half = Math.Max(1, (int)Math.Round(LevelWindowMs / 2 / BlockMs));
        level = new float[peak.Length];
        for (int i = 0; i < level.Length; i++)
            level[i] = rmsOf(Math.Max(0, i - half), Math.Min(level.Length, i + half));

        if (level.Length > 0)
        {
            var sorted = (float[])level.Clone();
            Array.Sort(sorted);
            ReferenceLevel = Math.Max(1e-5f, sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.995))]);
        }
        else
            ReferenceLevel = 1;
    }

    /// <summary>Window of the short-term loudness behind <see cref="LevelIn"/>.</summary>
    public const double LevelWindowMs = 10;

    private readonly float[] level;

    /// <summary>Loud reference for <see cref="LevelIn"/>: the 99.5th percentile of the short-term loudness.</summary>
    public float ReferenceLevel { get; }

    /// <summary>
    /// Loudest short-term RMS (<see cref="LevelWindowMs"/> window) in [fromMs, toMs): the waveform height.
    /// The peak sample would be flat on a limited master (measured: galvanizer's block peaks barely move between
    /// kicks), the 10 ms RMS rises on every kick and snare; taking the loudest window keeps a transient a spike at
    /// any zoom instead of averaging it away.
    /// </summary>
    public float LevelIn(double fromMs, double toMs)
    {
        (int a, int b) = blockRange(fromMs, toMs);
        float max = 0;
        for (int i = a; i < b; i++)
            if (level[i] > max)
                max = level[i];
        return max;
    }

    /// <summary>RMS of the mono mix over [fromMs, toMs) (at least one block).</summary>
    public float RmsIn(double fromMs, double toMs)
    {
        (int a, int b) = blockRange(fromMs, toMs);
        return rmsOf(a, b);
    }

    private float rmsOf(int a, int b) => b <= a ? 0 : (float)Math.Sqrt(Math.Max(0, fullPrefix[b] - fullPrefix[a]) / ((double)(b - a) * BlockSamples));

    /// <summary>Analyses a mono signal (tests, calibration).</summary>
    public static SpectralAnalysis FromMono(ReadOnlySpan<float> samples, int sampleRate, double lowHz = LowCrossoverHz, double highHz = HighCrossoverHz)
    {
        var b = new Builder(sampleRate, 1, lowHz, highHz);
        b.Add(samples);
        return b.Build();
    }

    /// <summary>Peak of the mono mix in [fromMs, toMs) (at least the one block under <paramref name="fromMs"/>); 0 outside the song.</summary>
    public float PeakIn(double fromMs, double toMs)
    {
        (int a, int b) = blockRange(fromMs, toMs);
        float max = 0;
        for (int i = a; i < b; i++)
            if (peak[i] > max)
                max = peak[i];
        return max;
    }

    /// <summary>Band RMS over [fromMs, toMs), widened symmetrically to <see cref="MinBandWindowMs"/>.</summary>
    public BandLevels BandsIn(double fromMs, double toMs)
    {
        double width = toMs - fromMs;
        if (width < MinBandWindowMs)
        {
            double mid = (fromMs + toMs) / 2;
            fromMs = mid - MinBandWindowMs / 2;
            toMs = mid + MinBandWindowMs / 2;
        }

        (int a, int b) = blockRange(fromMs, toMs);
        return bands(a, b);
    }

    private BandLevels bands(int a, int b)
    {
        if (b <= a) return default;

        double n = (double)(b - a) * BlockSamples;
        return new BandLevels(
            (float)Math.Sqrt(Math.Max(0, lowPrefix[b] - lowPrefix[a]) / n),
            (float)Math.Sqrt(Math.Max(0, midPrefix[b] - midPrefix[a]) / n),
            (float)Math.Sqrt(Math.Max(0, highPrefix[b] - highPrefix[a]) / n));
    }

    private (int, int) blockRange(double fromMs, double toMs)
    {
        double bm = BlockMs;
        int a = (int)Math.Floor(fromMs / bm);
        int b = Math.Max(a + 1, (int)Math.Ceiling(toMs / bm));
        return (Math.Clamp(a, 0, BlockCount), Math.Clamp(b, 0, BlockCount));
    }

    /// <summary>Streaming analysis: feed interleaved PCM chunks of any size, then <see cref="Build"/>.</summary>
    public sealed class Builder
    {
        private readonly int sampleRate, channels, blockSamples;
        private readonly Biquad lowA, lowB, midHpA, midHpB, midLpA, midLpB, highA, highB;

        private readonly List<float> peaks = new List<float>();
        private readonly List<double> lowE = new List<double>(), midE = new List<double>(), highE = new List<double>(), fullE = new List<double>();

        private int inBlock;
        private float blockPeak;
        private double blockLow, blockMid, blockHigh, blockFull;
        private int channel;
        private float frameSum;

        public Builder(int sampleRate, int channels, double lowHz = LowCrossoverHz, double highHz = HighCrossoverHz)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));

            this.sampleRate = sampleRate;
            this.channels = channels;
            blockSamples = Math.Max(1, (int)Math.Round(sampleRate / 1000.0));

            lowA = Biquad.LowPass(sampleRate, lowHz);
            lowB = Biquad.LowPass(sampleRate, lowHz);
            midHpA = Biquad.HighPass(sampleRate, lowHz);
            midHpB = Biquad.HighPass(sampleRate, lowHz);
            midLpA = Biquad.LowPass(sampleRate, highHz);
            midLpB = Biquad.LowPass(sampleRate, highHz);
            highA = Biquad.HighPass(sampleRate, highHz);
            highB = Biquad.HighPass(sampleRate, highHz);
        }

        /// <summary>Interleaved samples, full scale ±1. A chunk may end in the middle of a frame.</summary>
        public void Add(ReadOnlySpan<float> interleaved)
        {
            foreach (float s in interleaved)
            {
                frameSum += s;
                if (++channel < channels) continue;

                double x = frameSum / channels;
                channel = 0;
                frameSum = 0;

                float ax = (float)Math.Abs(x);
                if (ax > blockPeak) blockPeak = ax;

                double l = lowB.Process(lowA.Process(x));
                double m = midLpB.Process(midLpA.Process(midHpB.Process(midHpA.Process(x))));
                double h = highB.Process(highA.Process(x));
                blockLow += l * l;
                blockMid += m * m;
                blockHigh += h * h;
                blockFull += x * x;

                if (++inBlock == blockSamples) flush();
            }
        }

        private void flush()
        {
            peaks.Add(blockPeak);
            lowE.Add(blockLow);
            midE.Add(blockMid);
            highE.Add(blockHigh);
            fullE.Add(blockFull);
            inBlock = 0;
            blockPeak = 0;
            blockLow = blockMid = blockHigh = blockFull = 0;
        }

        public SpectralAnalysis Build()
        {
            if (inBlock > 0)
            {
                // the last partial block counts as a full one of silence-padded samples
                flush();
            }

            return new SpectralAnalysis(sampleRate, blockSamples, peaks.ToArray(), prefix(lowE), prefix(midE), prefix(highE), prefix(fullE));
        }

        private static double[] prefix(List<double> e)
        {
            var p = new double[e.Count + 1];
            for (int i = 0; i < e.Count; i++) p[i + 1] = p[i] + e[i];
            return p;
        }
    }

    /// <summary>RBJ cookbook biquad, Butterworth Q (1/√2); two in series make one Linkwitz-Riley 4th-order edge.</summary>
    private sealed class Biquad
    {
        private readonly double b0, b1, b2, a1, a2;
        private double z1, z2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            this.b0 = b0 / a0;
            this.b1 = b1 / a0;
            this.b2 = b2 / a0;
            this.a1 = a1 / a0;
            this.a2 = a2 / a0;
        }

        private static (double cos, double alpha) prepare(int rate, double hz)
        {
            hz = Math.Clamp(hz, 1, rate * 0.45);
            double w = 2 * Math.PI * hz / rate;
            return (Math.Cos(w), Math.Sin(w) / (2 * Math.Sqrt(0.5)));
        }

        public static Biquad LowPass(int rate, double hz)
        {
            var (c, al) = prepare(rate, hz);
            return new Biquad((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al);
        }

        public static Biquad HighPass(int rate, double hz)
        {
            var (c, al) = prepare(rate, hz);
            return new Biquad((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al);
        }

        // transposed direct form II
        public double Process(double x)
        {
            double y = b0 * x + z1;
            z1 = b1 * x - a1 * y + z2;
            z2 = b2 * x - a2 * y;
            return y;
        }
    }
}
