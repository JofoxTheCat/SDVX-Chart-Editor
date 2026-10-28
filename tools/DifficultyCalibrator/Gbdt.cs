using SDVXEditor.Core.Patterns;

namespace SDVXEditor.Tools.DifficultyCalibrator;

/// <summary>Hyper-parameters of <see cref="Gbdt"/>.</summary>
public sealed record GbdtOptions
{
    public int Trees { get; init; } = 400;
    public int MaxDepth { get; init; } = 4;
    public double LearningRate { get; init; } = 0.05;
    public int MinLeaf { get; init; } = 20;
    public double RowSample { get; init; } = 0.8;
    public double ColSample { get; init; } = 0.8;
    public int MaxBins { get; init; } = 64;

    /// <summary>"l2" (mean leaves) or "l1" (median leaves on sign gradients, robust to label noise).</summary>
    public string Loss { get; init; } = "l2";

    public int Seed { get; init; } = 7;
}

/// <summary>
/// Small deterministic histogram gradient-boosting trainer (regression). Trees are exported as
/// <see cref="RegressionTree"/> arrays with raw-value thresholds (<c>x ≤ t</c> goes left), learning rate
/// already folded into the leaf values, so the editor only sums tree outputs.
/// </summary>
public static class Gbdt
{
    private sealed class Binned
    {
        public required byte[][] Bins;          // [feature][row]
        public required double[][] Thresholds;  // [feature][bin] upper edge (raw value) of bins 0..k-2
    }

    private static Binned Bin(double[][] x, int maxBins)
    {
        int n = x.Length, p = x[0].Length;
        var bins = new byte[p][];
        var thr = new double[p][];
        for (int j = 0; j < p; j++)
        {
            var col = new double[n];
            for (int i = 0; i < n; i++) col[i] = x[i][j];
            var sorted = (double[])col.Clone();
            Array.Sort(sorted);
            var uniq = new List<double>();
            foreach (double v in sorted)
                if (uniq.Count == 0 || v != uniq[^1]) uniq.Add(v);
            List<double> edges;
            if (uniq.Count <= maxBins)
            {
                edges = new List<double>();
                for (int k = 0; k + 1 < uniq.Count; k++) edges.Add(Mid(uniq[k], uniq[k + 1]));
            }
            else
            {
                edges = new List<double>();
                for (int k = 1; k < maxBins; k++)
                {
                    double q = sorted[(int)((long)k * (n - 1) / maxBins)];
                    // edge between q and the next larger distinct value
                    int idx = uniq.BinarySearch(q);
                    if (idx < 0) idx = ~idx;
                    if (idx + 1 >= uniq.Count) continue;
                    double e = Mid(uniq[idx], uniq[idx + 1]);
                    if (edges.Count == 0 || e > edges[^1]) edges.Add(e);
                }
            }
            thr[j] = edges.ToArray();
            var b = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int k = Array.BinarySearch(thr[j], col[i]);
                b[i] = (byte)(k >= 0 ? k : ~k); // value ≤ edge[k] → bin k
            }
            bins[j] = b;
        }
        return new Binned { Bins = bins, Thresholds = thr };
    }

    /// <summary>A split threshold between two adjacent distinct values, rounded to 6 significant digits when that keeps it strictly between.</summary>
    private static double Mid(double a, double b)
    {
        double m = (a + b) / 2;
        double r = double.Parse(m.ToString("G6", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
        return r >= a && r < b ? r : m;
    }

    /// <summary>Fits trees to <paramref name="y"/> − <paramref name="baseline"/>; returns the trees (learning rate folded in).</summary>
    public static List<RegressionTree> Fit(double[][] x, double[] y, double[] baseline, GbdtOptions o, IList<double>? gainByFeature = null)
    {
        int n = x.Length, p = x[0].Length;
        var data = Bin(x, o.MaxBins);
        var pred = (double[])baseline.Clone();
        var rng = new Random(o.Seed);
        var trees = new List<RegressionTree>(o.Trees);
        var grad = new double[n];
        for (int t = 0; t < o.Trees; t++)
        {
            for (int i = 0; i < n; i++)
            {
                double r = y[i] - pred[i];
                grad[i] = o.Loss == "l1" ? Math.Sign(r) : r;
            }
            var rows = new List<int>(n);
            for (int i = 0; i < n; i++)
                if (rng.NextDouble() < o.RowSample) rows.Add(i);
            var cols = new List<int>(p);
            for (int j = 0; j < p; j++)
                if (rng.NextDouble() < o.ColSample) cols.Add(j);
            if (cols.Count == 0) cols.Add(rng.Next(p));

            var tree = new TreeBuilder(data, grad, y, pred, o, cols, gainByFeature);
            tree.Build(rows);
            var rt = tree.Export();
            trees.Add(rt);
            for (int i = 0; i < n; i++) pred[i] += rt.Evaluate(x[i]);
        }
        return trees;
    }

    private sealed class TreeBuilder
    {
        private readonly Binned data;
        private readonly double[] grad, y, pred;
        private readonly GbdtOptions o;
        private readonly List<int> cols;
        private readonly IList<double>? gains;
        private readonly List<(int F, double T, int L, int R, double V)> nodes = new();

        public TreeBuilder(Binned data, double[] grad, double[] y, double[] pred, GbdtOptions o, List<int> cols, IList<double>? gains)
        {
            this.data = data; this.grad = grad; this.y = y; this.pred = pred; this.o = o; this.cols = cols; this.gains = gains;
        }

        public void Build(List<int> rows) => Grow(rows, 0);

        private int Grow(List<int> rows, int depth)
        {
            int id = nodes.Count;
            nodes.Add((-1, 0, 0, 0, Leaf(rows)));
            if (depth >= o.MaxDepth || rows.Count < 2 * o.MinLeaf) return id;

            double gTotal = 0;
            foreach (int i in rows) gTotal += grad[i];
            double bestGain = 1e-9, bestThr = 0;
            int bestF = -1, bestBin = -1;
            var hg = new double[256];
            var hc = new int[256];
            foreach (int f in cols)
            {
                var edges = data.Thresholds[f];
                if (edges.Length == 0) continue;
                Array.Clear(hg);
                Array.Clear(hc);
                var b = data.Bins[f];
                foreach (int i in rows)
                {
                    hg[b[i]] += grad[i];
                    hc[b[i]]++;
                }
                double gl = 0;
                int cl = 0;
                for (int k = 0; k < edges.Length; k++)
                {
                    gl += hg[k];
                    cl += hc[k];
                    int cr = rows.Count - cl;
                    if (cl < o.MinLeaf) continue;
                    if (cr < o.MinLeaf) break;
                    double gr = gTotal - gl;
                    double gain = gl * gl / cl + gr * gr / cr - gTotal * gTotal / rows.Count;
                    if (gain > bestGain)
                    {
                        bestGain = gain; bestF = f; bestBin = k; bestThr = edges[k];
                    }
                }
            }
            if (bestF < 0) return id;
            if (gains != null) lock (gains) gains[bestF] += bestGain;
            var left = new List<int>();
            var right = new List<int>();
            var bins = data.Bins[bestF];
            foreach (int i in rows) (bins[i] <= bestBin ? left : right).Add(i);
            int l = Grow(left, depth + 1);
            int r = Grow(right, depth + 1);
            nodes[id] = (bestF, bestThr, l, r, 0);
            return id;
        }

        private double Leaf(List<int> rows)
        {
            if (rows.Count == 0) return 0;
            if (o.Loss == "l1")
            {
                var res = rows.Select(i => y[i] - pred[i]).OrderBy(v => v).ToList();
                double med = res.Count % 2 == 1 ? res[res.Count / 2] : (res[res.Count / 2 - 1] + res[res.Count / 2]) / 2;
                return Round(o.LearningRate * med);
            }
            double s = 0;
            foreach (int i in rows) s += grad[i];
            return Round(o.LearningRate * s / rows.Count);
        }

        private static double Round(double v) => Math.Round(v, 6);

        public RegressionTree Export() => new()
        {
            Feature = nodes.Select(n => n.F).ToArray(),
            Threshold = nodes.Select(n => n.T).ToArray(),
            Left = nodes.Select(n => n.L).ToArray(),
            Right = nodes.Select(n => n.R).ToArray(),
            Value = nodes.Select(n => n.V).ToArray(),
        };
    }

    // ---------------------------------------------------------------- linear part

    /// <summary>Ordinary least squares with a tiny ridge (raw features, intercept unpenalised).</summary>
    public static (double Intercept, double[] Weights) Ols(double[][] x, double[] y, double ridge = 1e-6)
    {
        int n = x.Length, k = x.Length == 0 ? 0 : x[0].Length;
        var mu = new double[k];
        for (int j = 0; j < k; j++) mu[j] = x.Average(r => r[j]);
        double my = y.Average();
        var a = new double[k, k + 1];
        for (int i = 0; i < n; i++)
            for (int p = 0; p < k; p++)
            {
                double xp = x[i][p] - mu[p];
                for (int q = 0; q < k; q++) a[p, q] += xp * (x[i][q] - mu[q]);
                a[p, k] += xp * (y[i] - my);
            }
        for (int p = 0; p < k; p++) a[p, p] += ridge * Math.Max(1, a[p, p]);
        var w = Solve(a, k);
        double b = my;
        for (int j = 0; j < k; j++) b -= w[j] * mu[j];
        return (b, w);
    }

    private static double[] Solve(double[,] m, int n)
    {
        for (int c = 0; c < n; c++)
        {
            int p = c;
            for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[p, c])) p = r;
            for (int q = 0; q <= n; q++) (m[c, q], m[p, q]) = (m[p, q], m[c, q]);
            if (Math.Abs(m[c, c]) < 1e-12) continue;
            for (int r = 0; r < n; r++)
            {
                if (r == c) continue;
                double f = m[r, c] / m[c, c];
                if (f == 0) continue;
                for (int q = c; q <= n; q++) m[r, q] -= f * m[c, q];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = Math.Abs(m[i, i]) < 1e-12 ? 0 : m[i, n] / m[i, i];
        return x;
    }
}
