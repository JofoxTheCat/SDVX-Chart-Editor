using SDVXEditor.Core.LevelEstimation;
using SDVXEditor.Core.Patterns;

namespace SDVXEditor.Tools.DifficultyCalibrator;

/// <summary>
/// Level model with pattern features: the ridge (DifficultyFeatures + slot) as base, gradient-boosted trees on its
/// residual over every feature (DifficultyFeatures, <c>pat.</c>-prefixed PatternAnalysis features, slot), then
/// optionally the isotonic post-map fitted on inner out-of-fold predictions.
/// </summary>
public static class LevelFit
{
    public const string PatternPrefix = DifficultyEstimator.PatternPrefix;

    public static GbdtOptions TreeOptions { get; set; } = new() { Trees = 400, MaxDepth = 4, LearningRate = 0.05, MinLeaf = 60 };

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Sample, Dictionary<string, double>> merged = new();

    /// <summary>DifficultyFeatures + slot indicators + <c>pat.</c> pattern features.</summary>
    public static Dictionary<string, double> Merge(Sample s) => merged.GetValue(s, x =>
    {
        var d = new Dictionary<string, double>(x.Inputs);
        foreach (var (k, v) in x.Patterns) d[PatternPrefix + k] = v;
        return d;
    });

    /// <summary>"all" = DifficultyFeatures + pattern features in the trees; "pattern" = pattern features only.</summary>
    public static string TreeSet { get; set; } = "all";

    public static List<string> TreeNames(bool slotAware)
    {
        var pattern = PatternAnalysis.FeatureNames.Select(n => PatternPrefix + n);
        var names = (TreeSet == "pattern" ? pattern : DifficultyFeatures.FeatureNames.Concat(pattern)).ToList();
        if (slotAware) names.AddRange(new[] { "slot_1", "slot_2", "slot_3", "slot_4" });
        return names;
    }

    public static LevelModel Train(List<Sample> train, List<string> ridgeNames, List<string>? treeNames, double lambda, bool isotonic,
                                   int folds, int seed, GbdtOptions? options = null)
    {
        options ??= TreeOptions;
        var model = Fit(train, ridgeNames, treeNames, lambda, options);
        if (isotonic)
        {
            var inner = Dataset.FoldAssignment(train.Select(s => s.SongId), folds, seed + 101);
            var oof = new (double, double)[train.Count];
            Parallel.For(0, folds, f =>
            {
                var sub = Fit(train.Where(s => inner[s.SongId] != f).ToList(), ridgeNames, treeNames, lambda, options);
                for (int i = 0; i < train.Count; i++)
                    if (inner[train[i].SongId] == f) oof[i] = (sub.Raw(Merge(train[i])), train[i].Level);
            });
            (model.IsotonicX, model.IsotonicY) = Program.Isotonic(oof.ToList());
        }
        return model;
    }

    private static LevelModel Fit(List<Sample> train, List<string> ridgeNames, List<string>? treeNames, double lambda, GbdtOptions options)
    {
        var model = Program.FitRidge(train, ridgeNames, lambda);
        if (treeNames == null) return model;
        var x = train.Select(s =>
        {
            var d = Merge(s);
            return treeNames.Select(n => d.TryGetValue(n, out double v) ? v : 0).ToArray();
        }).ToArray();
        var y = train.Select(s => s.Level).ToArray();
        var baseline = train.Select(s => model.Raw(s.Inputs)).ToArray();
        model.TreeFeatures = treeNames.ToList();
        model.Trees = Gbdt.Fit(x, y, baseline, options);
        return model;
    }

    public static double Predict(LevelModel m, Sample s) => Math.Clamp(m.PostMap(m.Raw(Merge(s))), 1, 21);

    public static List<(Sample S, double P)> CrossValidate(List<Sample> data, List<string> ridgeNames, List<string>? treeNames, double lambda,
                                                           bool isotonic, Dictionary<int, int> foldOf, int folds)
    {
        var preds = new (Sample, double)[data.Count];
        for (int f = 0; f < folds; f++)
        {
            var model = Train(data.Where(s => foldOf[s.SongId] != f).ToList(), ridgeNames, treeNames, lambda, isotonic, folds, f);
            Parallel.For(0, data.Count, i =>
            {
                if (foldOf[data[i].SongId] == f) preds[i] = (data[i], Predict(model, data[i]));
            });
        }
        return preds.ToList();
    }
}
