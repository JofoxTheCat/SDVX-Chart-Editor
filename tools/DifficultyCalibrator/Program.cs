using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.LevelEstimation;

namespace SDVXEditor.Tools.DifficultyCalibrator;

/// <summary>
/// Fits the level-estimation model and the skill-radar model from the user's own SOUND VOLTEX install: pairs every
/// <c>data/music/*/*.vox</c> with its <c>difnum</c> and radar values from <c>data/others/music_db.xml</c>, validates
/// (song-grouped k-fold CV, per slot / level / VOX version, temporal hold-out on the newest songs, fan charts with
/// declared levels) and writes <c>Difficulty/coefficients.json</c> and <c>Radar/radar_model.json</c> — aggregate
/// numbers only. One command regenerates both:
/// <code>
/// DifficultyCalibrator &lt;game dir | data dir&gt; --core src/SDVXEditor.Core [--nautica &lt;dir&gt;]
///     [--out coefficients.json] [--radar-out radar_model.json] [--baseline old-coefficients.json]
///     [--radar-only | --level-only] [--dry-run] [--radar-exclude cam_]
///     [--folds 5] [--seed 1] [--lambda 1] [--hands] [--worst]
///     [--trees 400 --depth 4 --lr 0.05 --min-leaf 20 --loss l2]                       (radar trees)
///     [--level-trees 400 --level-depth 4 --level-lr 0.05 --level-min-leaf 60 --level-tree-set all|pattern]
/// </code>
/// </summary>
public static class Program
{
    private static readonly string[] SlotTags = { "novice", "advanced", "exhaust", "infinite", "maximum", "ultimate" };
    private static readonly string[] SlotNames = { "NOV", "ADV", "EXH", "INF", "MXM" };

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("usage: DifficultyCalibrator <game dir | data dir> [--core src/SDVXEditor.Core] [--out coefficients.json] [--radar-out radar_model.json] [--nautica <dir>] [--radar-only|--level-only] [--dry-run] [--folds 5] [--seed 1] [--lambda 1] [--hands] (see Program.cs for tree options)");
            return 2;
        }
        if (args[0] == "--explain")
        {
            // pattern features of chart files (no game data needed)
            foreach (string f in args.Skip(1))
            {
                var (chart, _) = SDVXEditor.Core.Radar.RadarCalculator.ReadForRadar(f);
                var set = SDVXEditor.Core.Patterns.PatternAnalysis.Analyze(chart);
                Console.WriteLine(f);
                Console.WriteLine("  " + string.Join(" ", set.Features.Select(kv => $"{kv.Key}={kv.Value:0.###}")));
                Console.WriteLine("  radar: " + SDVXEditor.Core.Radar.SkillRadarModel.Default.Estimate(chart).Radar);
            }
            return 0;
        }
        string root = args[0];
        string outPath = Opt(args, "--out") ?? "coefficients.json";
        string? nautica = Opt(args, "--nautica");
        int folds = int.Parse(Opt(args, "--folds") ?? "5");
        int seed = int.Parse(Opt(args, "--seed") ?? "1");
        double lambda = double.Parse(Opt(args, "--lambda") ?? "1");

        string? dataDir = Dataset.FindDataDir(root);
        if (dataDir == null)
        {
            Console.Error.WriteLine($"no data/music + data/others/music_db.xml under {root}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var loaded = Dataset.Load(dataDir);
        var data = loaded.Samples;
        Console.WriteLine($"vox files: {loaded.Files}, parsed: {loaded.Parsed}, failed: {loaded.Failures.Count}, empty: {loaded.Empty}, unpaired: {loaded.Unpaired}");
        foreach (var (f, e) in loaded.Failures.Take(10)) Console.WriteLine($"  FAIL {f}: {e}");
        Console.WriteLine($"paired charts: {data.Count} from {data.Select(s => s.SongId).Distinct().Count()} songs  ({sw.Elapsed.TotalSeconds:0.0} s)");

        RadarFit.TreeOptions = new GbdtOptions
        {
            Trees = int.Parse(Opt(args, "--trees") ?? "400"),
            MaxDepth = int.Parse(Opt(args, "--depth") ?? "4"),
            LearningRate = double.Parse(Opt(args, "--lr") ?? "0.05"),
            MinLeaf = int.Parse(Opt(args, "--min-leaf") ?? "20"),
            Loss = Opt(args, "--loss") ?? "l2",
        };
        LevelFit.TreeOptions = LevelFit.TreeOptions with
        {
            Trees = int.Parse(Opt(args, "--level-trees") ?? LevelFit.TreeOptions.Trees.ToString(CultureInfo.InvariantCulture)),
            MaxDepth = int.Parse(Opt(args, "--level-depth") ?? LevelFit.TreeOptions.MaxDepth.ToString(CultureInfo.InvariantCulture)),
            LearningRate = double.Parse(Opt(args, "--level-lr") ?? LevelFit.TreeOptions.LearningRate.ToString(CultureInfo.InvariantCulture)),
            MinLeaf = int.Parse(Opt(args, "--level-min-leaf") ?? LevelFit.TreeOptions.MinLeaf.ToString(CultureInfo.InvariantCulture)),
        };
        LevelFit.TreeSet = Opt(args, "--level-tree-set") ?? LevelFit.TreeSet;

        if (Opt(args, "--radar-exclude") is string ex) RadarReport.Excluded = ex.Split(',', StringSplitOptions.RemoveEmptyEntries);
        string? core = Opt(args, "--core");
        string radarOut = Opt(args, "--radar-out") ?? (core != null ? Path.Combine(core, "Radar", "radar_model.json") : "radar_model.json");
        if (core != null && Opt(args, "--out") == null) outPath = Path.Combine(core, "Difficulty", "coefficients.json");
        bool dryRun = args.Contains("--dry-run");

        // ---------------------------------------------------------------- radar
        if (!args.Contains("--level-only"))
        {
            var only = Opt(args, "--axes")?.Split(',').Select(int.Parse).ToArray();
            var forms = Opt(args, "--forms")?.Split(',');
            var radar = RadarReport.Run(data, folds, seed, forms, only);
            if (args.Contains("--hands")) RadarReport.HandModels(data, folds, seed);
            if (nautica != null) RadarReport.Nautica(nautica, radar);
            if (!dryRun && only == null)
            {
                File.WriteAllText(radarOut, radar.ToJson());
                Console.WriteLine($"wrote {Path.GetFullPath(radarOut)}");
            }
            if (args.Contains("--radar-only"))
            {
                Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:0.0} s");
                return 0;
            }
        }

        // ---------------------------------------------------------------- level
        var chosen = LevelReport.Run(data, folds, seed, lambda, nautica, args.Contains("--worst"), Opt(args, "--baseline") ?? outPath);
        if (!dryRun)
        {
            File.WriteAllText(outPath, chosen.ToJson());
            Console.WriteLine($"wrote {Path.GetFullPath(outPath)}");
        }
        Console.WriteLine($"done in {sw.Elapsed.TotalSeconds:0.0} s");
        return 0;
    }

    private static readonly HashSet<string> LogFeatures = new()
    {
        "dur", "n_events", "n_notes", "slams", "travel", "nbpm", "bpm_max", "bpm_min", "bpm_ratio",
    };

    internal static string TransformOf(string name) => LogFeatures.Contains(name) || name.EndsWith("_w", StringComparison.Ordinal) ? "log1p" : "id";

    // ==================================================================== data

    private static string? Opt(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static Dictionary<int, int> FoldAssignment(List<Sample> data, int folds, int seed)
        => Dataset.FoldAssignment(data.Select(s => s.SongId), folds, seed);

    // ==================================================================== fitting

    /// <summary>Standardised ridge regression; returns a LevelModel without post-map.</summary>
    internal static LevelModel FitRidge(List<Sample> train, List<string> names, double lambda)
    {
        int n = train.Count, k = names.Count;
        var transforms = names.Select(TransformOf).ToArray();
        var X = new double[n, k];
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            y[i] = train[i].Level;
            for (int j = 0; j < k; j++)
            {
                double raw = train[i].Inputs.TryGetValue(names[j], out double v) ? v : 0;
                X[i, j] = transforms[j] == "log1p" ? Math.Log(1 + Math.Max(0, raw)) : raw;
            }
        }
        var mu = new double[k];
        var sd = new double[k];
        for (int j = 0; j < k; j++)
        {
            double s = 0;
            for (int i = 0; i < n; i++) s += X[i, j];
            mu[j] = s / n;
            double ss = 0;
            for (int i = 0; i < n; i++) ss += (X[i, j] - mu[j]) * (X[i, j] - mu[j]);
            sd[j] = Math.Sqrt(ss / n);
            if (sd[j] < 1e-12) sd[j] = 1;
            for (int i = 0; i < n; i++) X[i, j] = (X[i, j] - mu[j]) / sd[j];
        }
        double ym = y.Average();
        var A = new double[k, k + 1];
        for (int a = 0; a < k; a++)
        {
            for (int b = a; b < k; b++)
            {
                double s = 0;
                for (int i = 0; i < n; i++) s += X[i, a] * X[i, b];
                A[a, b] = A[b, a] = s;
            }
            A[a, a] += lambda;
            double r = 0;
            for (int i = 0; i < n; i++) r += X[i, a] * (y[i] - ym);
            A[a, k] = r;
        }
        var w = Solve(A, k);
        return new LevelModel
        {
            Intercept = ym,
            Terms = names.Select((name, j) => new FeatureTerm { Name = name, Transform = transforms[j], Mean = mu[j], Sd = sd[j], Weight = w[j] }).ToList(),
        };
    }

    private static double[] Solve(double[,] M, int n)
    {
        for (int i = 0; i < n; i++)
        {
            int p = i;
            for (int r = i + 1; r < n; r++) if (Math.Abs(M[r, i]) > Math.Abs(M[p, i])) p = r;
            if (p != i)
                for (int c = 0; c <= n; c++) (M[i, c], M[p, c]) = (M[p, c], M[i, c]);
            for (int r = i + 1; r < n; r++)
            {
                double f = M[r, i] / M[i, i];
                if (f == 0) continue;
                for (int c = i; c <= n; c++) M[r, c] -= f * M[i, c];
            }
        }
        var x = new double[n];
        for (int i = n - 1; i >= 0; i--)
        {
            double s = M[i, n];
            for (int j = i + 1; j < n; j++) s -= M[i, j] * x[j];
            x[i] = s / M[i, i];
        }
        return x;
    }

    /// <summary>Pool-adjacent-violators: non-decreasing fit of y on x; one breakpoint per pooled block.</summary>
    internal static (List<double> X, List<double> Y) Isotonic(List<(double X, double Y)> pts)
    {
        var sorted = pts.OrderBy(p => p.X).ToList();
        var blocks = new List<(double SumX, double SumY, int N)>();
        foreach (var (x, y) in sorted)
        {
            blocks.Add((x, y, 1));
            while (blocks.Count > 1 && blocks[^2].SumY / blocks[^2].N >= blocks[^1].SumY / blocks[^1].N)
            {
                var b = blocks[^1];
                var a = blocks[^2];
                blocks.RemoveAt(blocks.Count - 1);
                blocks[^1] = (a.SumX + b.SumX, a.SumY + b.SumY, a.N + b.N);
            }
        }
        var bx = new List<double>();
        var by = new List<double>();
        foreach (var b in blocks)
        {
            double x = Math.Round(b.SumX / b.N, 4), y = Math.Round(b.SumY / b.N, 4);
            if (bx.Count > 0 && x <= bx[^1]) continue; // keep x strictly increasing
            bx.Add(x);
            by.Add(y);
        }
        return (bx, by);
    }

    /// <summary>Ridge on all of <paramref name="train"/> + isotonic map fitted on inner out-of-fold predictions.</summary>
    internal static LevelModel TrainPipeline(List<Sample> train, List<string> names, double lambda, int folds, int seed)
    {
        var inner = FoldAssignment(train, folds, seed + 101);
        var oof = new List<(double, double)>(train.Count);
        for (int f = 0; f < folds; f++)
        {
            var fit = FitRidge(train.Where(s => inner[s.SongId] != f).ToList(), names, lambda);
            foreach (var s in train.Where(s => inner[s.SongId] == f)) oof.Add((fit.Raw(s.Inputs), s.Level));
        }
        var model = FitRidge(train, names, lambda);
        (model.IsotonicX, model.IsotonicY) = Isotonic(oof);
        return model;
    }

    internal static double Predict(LevelModel m, Sample s) => Math.Clamp(m.PostMap(m.Raw(s.Inputs)), 1, 21);

    private static List<(Sample S, double P)> Evaluate(string label, List<Sample> data, List<string> names, Dictionary<int, int> foldOf,
                                                       int folds, double lambda, bool perSlot)
    {
        var preds = new List<(Sample, double)>(data.Count);
        for (int f = 0; f < folds; f++)
        {
            var model = TrainPipeline(data.Where(s => foldOf[s.SongId] != f).ToList(), names, lambda, folds, f);
            preds.AddRange(data.Where(s => foldOf[s.SongId] == f).Select(s => (s, Predict(model, s))));
        }
        Report($"{label} [{names.Count} terms]", preds, perSlot);
        return preds;
    }

    /// <summary>Band, ranges and CV MAE from the out-of-fold predictions of the same design.</summary>
    internal static void Finish(LevelModel model, List<(Sample S, double P)> oof, List<Sample> data)
    {
        model.CvMae = Math.Round(Mae(oof), 3);
        // Buckets by floor(estimate), each with at least 60 residuals; a thin tail at either end is
        // merged into its neighbour and the quantile recomputed over the merged set.
        var groups = new List<(int From, List<double> Res)>();
        foreach (var g in oof.GroupBy(x => (int)Math.Floor(x.P)).OrderBy(g => g.Key))
        {
            var res = g.Select(x => Math.Abs(x.P - x.S.Level)).ToList();
            if (groups.Count > 0 && groups[^1].Res.Count < 60) groups[^1].Res.AddRange(res);
            else groups.Add((g.Key, res));
        }
        if (groups.Count > 1 && groups[^1].Res.Count < 60)
        {
            groups[^2].Res.AddRange(groups[^1].Res);
            groups.RemoveAt(groups.Count - 1);
        }
        var buckets = groups.Select((g, i) => new BandBucket
        {
            From = i == 0 ? 0 : g.From,
            HalfWidth = Math.Round(Quantile(g.Res, 0.8), 2),
            Count = g.Res.Count,
        }).ToList();
        model.Band = buckets;
        model.DefaultHalfWidth = Math.Round(Quantile(oof.Select(x => Math.Abs(x.P - x.S.Level)).ToList(), 0.8), 2);
        foreach (var t in model.Terms)
        {
            var xs = data.Select(s => t.Apply(s.Inputs.TryGetValue(t.Name, out double v) ? v : 0)).ToList();
            model.Ranges[t.Name] = new[] { Math.Round(xs.Min(), 5), Math.Round(xs.Max(), 5) };
            t.Mean = Math.Round(t.Mean, 8);
            t.Sd = Math.Round(t.Sd, 8);
            t.Weight = Math.Round(t.Weight, 8);
        }
        model.Intercept = Math.Round(model.Intercept, 8);
    }

    // ==================================================================== reporting

    internal static double Mae(List<(Sample, double)> preds)
        => preds.Average(x => Math.Abs(x.Item2 - x.Item1.Level));

    internal static double Quantile(List<double> values, double q)
    {
        var s = values.OrderBy(v => v).ToList();
        return s[Math.Min(s.Count - 1, (int)(q * s.Count))];
    }

    internal static string Line(IReadOnlyCollection<(Sample S, double P)> p)
    {
        if (p.Count == 0) return "n=0";
        double mae = p.Average(x => Math.Abs(x.P - x.S.Level));
        double rmse = Math.Sqrt(p.Average(x => (x.P - x.S.Level) * (x.P - x.S.Level)));
        double bias = Math.Round(p.Average(x => x.P - x.S.Level), 2) + 0.0;
        double w05 = p.Count(x => Math.Abs(Math.Round(x.P, 1) - x.S.Level) <= 0.5 + 1e-9) / (double)p.Count;
        double w1 = p.Count(x => Math.Abs(Math.Round(x.P, 1) - x.S.Level) <= 1.0 + 1e-9) / (double)p.Count;
        return $"n={p.Count,5}  MAE {mae:0.000}  RMSE {rmse:0.000}  bias {bias:+0.00;-0.00}  within±0.5 {100 * w05,5:0.0}%  within±1 {100 * w1,5:0.0}%";
    }

    internal static void Report(string label, List<(Sample S, double P)> preds, bool perSlot)
    {
        Console.WriteLine($"{label}");
        Console.WriteLine($"    all  {Line(preds)}");
        if (!perSlot) return;
        for (int slot = 0; slot <= 4; slot++)
            Console.WriteLine($"    {SlotNames[slot]}  {Line(preds.Where(x => x.S.Slot == slot).ToList())}");
    }

    internal static void ErrorByLevel(string label, List<(Sample S, double P)> preds)
    {
        Console.WriteLine($"  error by official level ({label}): level  n  bias  MAE");
        foreach (var g in preds.GroupBy(x => (int)Math.Round(x.S.Level, MidpointRounding.AwayFromZero)).OrderBy(g => g.Key))
            Console.WriteLine($"    {g.Key,2} {g.Count(),5} {g.Average(x => x.P - x.S.Level),6:+0.00;-0.00} {g.Average(x => Math.Abs(x.P - x.S.Level)),5:0.00}");
    }
}
