namespace SDVXEditor.Core.Audio.Effects.Dsp;

/// <summary>
/// A biquad (RBJ "Audio EQ Cookbook" coefficients) with its own state per channel, transposed direct form II.
/// Coefficients can change between samples without clicks large enough to matter at the update rates used here.
/// </summary>
public sealed class Biquad
{
    private double b0 = 1, b1, b2, a1, a2;
    private readonly double[] z1, z2;

    public Biquad(int channels)
    {
        z1 = new double[channels];
        z2 = new double[channels];
    }

    public void Reset()
    {
        Array.Clear(z1);
        Array.Clear(z2);
    }

    public float Process(float x, int ch)
    {
        double y = b0 * x + z1[ch];
        z1[ch] = b1 * x - a1 * y + z2[ch];
        z2[ch] = b2 * x - a2 * y;
        // denormals / blow-ups never leave the filter
        if (double.IsNaN(y) || double.IsInfinity(y))
        {
            z1[ch] = z2[ch] = 0;
            return x;
        }
        return (float)y;
    }

    private void set(double nb0, double nb1, double nb2, double na0, double na1, double na2)
    {
        b0 = nb0 / na0;
        b1 = nb1 / na0;
        b2 = nb2 / na0;
        a1 = na1 / na0;
        a2 = na2 / na0;
    }

    private static (double W0, double Cos, double Sin) omega(double freq, int rate)
    {
        double f = Math.Clamp(freq, 10, rate * 0.49);
        double w0 = 2 * Math.PI * f / rate;
        return (w0, Math.Cos(w0), Math.Sin(w0));
    }

    public void SetLowPass(double freq, double q, int rate)
    {
        var (_, c, s) = omega(freq, rate);
        double alpha = s / (2 * Math.Max(0.01, q));
        set((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + alpha, -2 * c, 1 - alpha);
    }

    public void SetHighPass(double freq, double q, int rate)
    {
        var (_, c, s) = omega(freq, rate);
        double alpha = s / (2 * Math.Max(0.01, q));
        set((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + alpha, -2 * c, 1 - alpha);
    }

    /// <summary>Peaking EQ with the bandwidth in octaves (cookbook BW form).</summary>
    public void SetPeaking(double freq, double bandwidthOct, double gainDb, int rate)
    {
        var (w0, c, s) = omega(freq, rate);
        double a = Math.Pow(10, gainDb / 40);
        double alpha = s * Math.Sinh(Math.Log(2) / 2 * Math.Max(0.01, bandwidthOct) * w0 / Math.Max(1e-9, s));
        set(1 + alpha * a, -2 * c, 1 - alpha * a, 1 + alpha / a, -2 * c, 1 - alpha / a);
    }

    public void SetAllPass(double freq, double q, int rate)
    {
        var (_, c, s) = omega(freq, rate);
        double alpha = s / (2 * Math.Max(0.01, q));
        set(1 - alpha, -2 * c, 1 + alpha, 1 + alpha, -2 * c, 1 - alpha);
    }

    /// <summary>High shelf, slope S = 1.</summary>
    public void SetHighShelf(double freq, double gainDb, int rate)
    {
        var (_, c, s) = omega(freq, rate);
        double a = Math.Pow(10, gainDb / 40);
        double alpha = s / 2 * Math.Sqrt(2);
        double sq = 2 * Math.Sqrt(a) * alpha;
        set(a * ((a + 1) + (a - 1) * c + sq), -2 * a * ((a - 1) + (a + 1) * c), a * ((a + 1) + (a - 1) * c - sq),
            (a + 1) - (a - 1) * c + sq, 2 * ((a - 1) - (a + 1) * c), (a + 1) - (a - 1) * c - sq);
    }
}

/// <summary>
/// Cutoff curves of K-Shoot Mania v2's laser filters (ksmaudio <c>*FilterDSP.cpp</c> <c>kFreqTable</c> / <c>kGainTable</c>,
/// 256-entry tables precomputed from KSM v1). Only a few entries were read and are interpolated here (frequencies
/// geometrically, gains linearly between them), which follows the measured shape within a few per cent. The laser
/// value is mapped into table space between the positions of <c>freq</c> and <c>freq_max</c> (inverse lookup), so
/// custom ranges keep the curve's shape — exactly like ksmaudio.
/// </summary>
public sealed class FilterCurve
{
    private readonly double[] x, y;
    private readonly bool geometric;

    private FilterCurve((double Index, double Value)[] points, bool geometric)
    {
        x = points.Select(p => p.Index / 255.0).ToArray();
        y = points.Select(p => p.Value).ToArray();
        this.geometric = geometric;
    }

    public static readonly FilterCurve PeakingFreq = new(new[]
    {
        (0.0, 50.0), (4, 51.9046), (8, 57.6169), (16, 80.4440), (24, 118.4107), (32, 171.3997), (48, 321.7439), (64, 529.6227),
        (96, 1107.0899), (128, 1875.6437), (160, 2798.1873), (192, 3830.8348), (224, 6103.6017), (240, 7599.9059), (255, 9000.0),
    }, true);

    public static readonly FilterCurve PeakingGainDb = new(new[]
    {
        (0.0, 0.0), (4, 3.1974), (8, 6.3948), (16, 12.7897), (24, 19.1845), (32, 25.5794), (48, 31.2273), (64, 32.3031), (89, 33.9840),
        (96, 33.5454), (128, 31.3939), (160, 29.2423), (192, 27.0908), (224, 22.1902), (240, 18.9181), (255, 15.8505),
    }, false);

    public static readonly FilterCurve LowPassFreq = new(new[]
    {
        (0.0, 15000.0), (16, 13241.4957), (32, 11520.4376), (64, 8328.0860), (96, 5648.8336), (128, 3605.7511), (160, 2208.4726),
        (192, 1372.0444), (224, 954.2605), (255, 800.0),
    }, true);

    public static readonly FilterCurve HighPassFreq = new(new[]
    {
        (0.0, 100.0), (16, 109.0021), (32, 135.9723), (64, 243.3140), (96, 420.3089), (128, 664.1274), (160, 970.8714),
        (192, 1335.6366), (224, 1752.5915), (255, 2200.0),
    }, true);

    /// <summary>Value at table position <paramref name="t"/> (0..1; outside: extrapolated from the end segment, geometric curves only).</summary>
    public double Forward(double t)
    {
        int n = x.Length;
        if (t <= 0 || t >= 1)
        {
            if (!geometric) return t <= 0 ? y[0] : y[n - 1];
            int i0 = t <= 0 ? 0 : n - 2;
            double slope = Math.Log(y[i0 + 1] / y[i0]) / (x[i0 + 1] - x[i0]);
            return t <= 0 ? y[0] * Math.Exp(slope * t) : y[n - 1] * Math.Exp(slope * (t - 1));
        }

        int k = 0;
        while (k < n - 2 && x[k + 1] < t) k++;
        double f = (t - x[k]) / (x[k + 1] - x[k]);
        return geometric ? y[k] * Math.Pow(y[k + 1] / y[k], f) : y[k] + (y[k + 1] - y[k]) * f;
    }

    /// <summary>Table position of a frequency (geometric curves; extrapolated outside the table).</summary>
    public double Inverse(double value)
    {
        int n = x.Length;
        bool increasing = y[n - 1] > y[0];
        double lv = Math.Log(Math.Max(1e-6, value));
        double first = Math.Log(y[0]), last = Math.Log(y[n - 1]);
        if (increasing ? lv <= first : lv >= first)
        {
            double slope = (Math.Log(y[1]) - first) / (x[1] - x[0]);
            return Math.Abs(slope) < 1e-9 ? 0 : (lv - first) / slope;
        }
        if (increasing ? lv >= last : lv <= last)
        {
            double slope = (last - Math.Log(y[n - 2])) / (x[n - 1] - x[n - 2]);
            return Math.Abs(slope) < 1e-9 ? 1 : 1 + (lv - last) / slope;
        }

        for (int k = 0; k < n - 1; k++)
        {
            double a = Math.Log(y[k]), b = Math.Log(y[k + 1]);
            if (increasing ? lv >= a && lv <= b : lv <= a && lv >= b)
                return x[k] + (x[k + 1] - x[k]) * (lv - a) / (b - a);
        }
        return 0;
    }
}
