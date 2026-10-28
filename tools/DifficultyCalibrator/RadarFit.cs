using System.Globalization;
using SDVXEditor.Core.Patterns;
using SDVXEditor.Core.Radar;

namespace SDVXEditor.Tools.DifficultyCalibrator;

/// <summary>Fits and validates the per-axis skill-radar models (<see cref="RadarModelFile"/>).</summary>
public static class RadarFit
{
    /// <summary>
    /// Linear base terms per axis (the physically obvious statistic, so the model extrapolates sensibly); trees
    /// then fit the residual from every pattern feature. Chosen from the correlation analysis (docs/RADAR_MODEL.md §4).
    /// </summary>
    public static readonly string[][] BaseTerms =
    {
        new[] { "chips_ps", "hold_time_frac" },                 // NOTES: 16.7 x (chips + hold seconds) / length
        new[] { "peak_chords_1.25", "peak_notes_2.5" },         // PEAK
        new[] { "laser_frac", "laser_travel_ps", "slam_ps" },   // TSUMAMI
        Array.Empty<string>(),                                  // TRICKY
        new[] { "handtrip_moving_ps", "handtrip_ps" },          // HAND-TRIP
        new[] { "onehand_moving_ps", "onehand_ps" },            // ONE-HAND
    };

    public const double Max = 200;

    public static GbdtOptions TreeOptions { get; set; } = new();

    public static List<(Sample S, int[] Radar)> WithRadar(List<Sample> data)
        => data.Where(s => s.Radar != null && s.Radar.Any(v => v > 0)).Select(s => (s, s.Radar!)).ToList();

    /// <summary>Trains one axis model of the given form ("linear", "trees", "linear+trees").</summary>
    public static AdditiveModel Train(List<(Sample S, int[] Radar)> train, int axis, string form, IReadOnlyList<string> features,
                                      GbdtOptions? options = null, double[]? gains = null)
    {
        options ??= TreeOptions;
        var model = new AdditiveModel { Features = features.ToList(), Min = 0, Max = Max, Form = form };
        var y = train.Select(t => (double)t.Radar[axis]).ToArray();
        var baseline = new double[train.Count];
        if (form.StartsWith("linear", StringComparison.Ordinal) && BaseTerms[axis].Length > 0)
        {
            var terms = BaseTerms[axis];
            var x = train.Select(t => terms.Select(n => t.S.Patterns[n]).ToArray()).ToArray();
            var (b, w) = Gbdt.Ols(x, y);
            model.Intercept = Math.Round(b, 6);
            model.Linear = terms.Select((n, j) => new LinearTerm { Feature = n, Weight = Math.Round(w[j], 6) }).ToList();
            for (int i = 0; i < train.Count; i++) baseline[i] = model.Raw(train[i].S.Patterns);
        }
        else
        {
            model.Intercept = Math.Round(y.Average(), 6);
            Array.Fill(baseline, model.Intercept);
        }
        if (form.EndsWith("trees", StringComparison.Ordinal))
        {
            var xs = train.Select(t => model.Vector(t.S.Patterns)).ToArray();
            var gainList = gains != null ? new double[features.Count] : null;
            model.Trees = Gbdt.Fit(xs, y, baseline, options with { Seed = options.Seed + axis }, gainList);
            if (gains != null) for (int j = 0; j < gains.Length; j++) gains[j] += gainList![j];
        }
        if (model.Trees.Count == 0) model.Features = new List<string>();
        model.Form = (model.Linear.Count > 0, model.Trees.Count > 0) switch
        {
            (true, true) => "linear+trees",
            (true, false) => "linear",
            _ => "trees",
        };
        return model;
    }

    public static int Predict(AdditiveModel m, Sample s) => (int)Math.Round(m.Predict(s.Patterns), MidpointRounding.AwayFromZero);

    /// <summary>Song-grouped k-fold CV predictions for one axis and form.</summary>
    public static int[] CrossValidate(List<(Sample S, int[] Radar)> data, int axis, string form, IReadOnlyList<string> features,
                                      Dictionary<int, int> foldOf, int folds, GbdtOptions? options = null)
    {
        var pred = new int[data.Count];
        Parallel.For(0, folds, f =>
        {
            var train = data.Where(t => foldOf[t.S.SongId] != f).ToList();
            var m = Train(train, axis, form, features, options);
            for (int i = 0; i < data.Count; i++)
                if (foldOf[data[i].S.SongId] == f) pred[i] = Predict(m, data[i].S);
        });
        return pred;
    }

    public readonly record struct Metrics(int N, double Mae, double R, double Bias, double Within5);

    public static Metrics Score(IReadOnlyList<int> pred, IReadOnlyList<int> truth)
    {
        int n = pred.Count;
        if (n == 0) return new Metrics(0, double.NaN, double.NaN, double.NaN, double.NaN);
        double mae = 0, bias = 0, w5 = 0;
        for (int i = 0; i < n; i++)
        {
            double d = pred[i] - truth[i];
            mae += Math.Abs(d);
            bias += d;
            if (Math.Abs(d) <= 5) w5++;
        }
        return new Metrics(n, mae / n, Pearson(pred.Select(v => (double)v).ToArray(), truth.Select(v => (double)v).ToArray()), Math.Round(bias / n, 2) + 0.0, w5 / n);
    }

    public static double Pearson(double[] x, double[] y)
    {
        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < x.Length; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }
        return sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : double.NaN;
    }

    public static string Fmt(Metrics m)
        => string.Create(CultureInfo.InvariantCulture, $"n={m.N,5} MAE {m.Mae,6:0.00}  r {m.R:0.000}  bias {m.Bias,6:+0.00;-0.00}  within±5 {100 * m.Within5,5:0.0}%");

    /// <summary>The classic radar (radar.py port) on the same charts, as the baseline.</summary>
    public static int[][] Classic(List<(Sample S, int[] Radar)> data)
    {
        var res = new int[data.Count][];
        Parallel.For(0, data.Count, i =>
        {
            var r = RadarCalculator.Compute(data[i].S.Chart, RadarSource.Kson);
            res[i] = SkillRadar.Axes.Select(a => r[a]).ToArray();
        });
        return res;
    }
}
