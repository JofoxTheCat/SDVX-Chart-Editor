using System.Globalization;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.LevelEstimation;
using SDVXEditor.Core.Patterns;

namespace SDVXEditor.Tools.DifficultyCalibrator;

/// <summary>
/// Level part of the calibrator run: the ridge + isotonic model (the previous form) against ridge + pattern trees,
/// same protocol for both (song-grouped CV, per slot, per level, temporal hold-out, fan charts). The tree model is
/// written only when it beats the ridge model on CV <b>and</b> is not worse on the fan charts than both the refitted
/// ridge model and the currently embedded file.
/// </summary>
public static class LevelReport
{
    private static readonly string[] SlotNames = { "NOV", "ADV", "EXH", "INF", "MXM" };

    private sealed record Variant(string Name, bool Trees, bool Isotonic);

    public static DifficultyCoefficients Run(List<Sample> data, int folds, int seed, double lambda, string? nautica, bool worst, string? baselinePath)
    {
        var featureNames = DifficultyFeatures.FeatureNames.ToList();
        var slots = new List<string> { "slot_1", "slot_2", "slot_3", "slot_4" };
        var ridgeFree = featureNames;
        var ridgeAware = featureNames.Concat(slots).ToList();
        var foldOf = Dataset.FoldAssignment(data.Select(s => s.SongId), folds, seed);

        Console.WriteLine();
        Console.WriteLine($"=== LEVEL: {folds}-fold CV grouped by song (seed {seed}, ridge lambda {lambda}; trees {LevelFit.TreeOptions})");
        var slotOnly = LevelFit.CrossValidate(data, slots, null, lambda, true, foldOf, folds);
        Program.Report("slot only", slotOnly, perSlot: false);

        var variants = new[]
        {
            new Variant("ridge+isotonic (previous form)", false, true),
            new Variant("ridge+trees", true, false),
            new Variant("ridge+trees+isotonic", true, true),
        };
        var results = new Dictionary<(string, bool), List<(Sample S, double P)>>();
        foreach (var v in variants)
            foreach (bool aware in new[] { false, true })
            {
                var preds = LevelFit.CrossValidate(data, aware ? ridgeAware : ridgeFree, v.Trees ? LevelFit.TreeNames(aware) : null,
                                                   lambda, v.Isotonic, foldOf, folds);
                results[(v.Name, aware)] = preds;
                Program.Report($"{(aware ? "SLOT-AWARE" : "SLOT-FREE ")} {v.Name}", preds, perSlot: true);
            }

        // pick the best tree variant per design by CV
        string bestTrees(bool aware) => variants.Where(v => v.Trees).OrderBy(v => Program.Mae(results[(v.Name, aware)])).First().Name;
        string treeFree = bestTrees(false), treeAware = bestTrees(true);
        var ridgeVariant = variants[0];
        foreach (bool aware in new[] { false, true })
        {
            Program.ErrorByLevel($"{(aware ? "slot-aware" : "slot-free")} {ridgeVariant.Name}", results[(ridgeVariant.Name, aware)]);
            Program.ErrorByLevel($"{(aware ? "slot-aware" : "slot-free")} {(aware ? treeAware : treeFree)}", results[(aware ? treeAware : treeFree, aware)]);
        }
        if (worst)
            foreach (var (s, p) in results[(treeAware, true)].OrderByDescending(x => Math.Abs(x.P - x.S.Level)).Take(30))
                Console.WriteLine($"    worst: song {s.SongId} {SlotNames[s.Slot]} level {s.Level:0.0} pred {p:0.0}");

        // temporal hold-out
        Console.WriteLine();
        const int cut = 2100;
        var oldSongs = data.Where(s => s.SongId < cut).ToList();
        var newSongs = data.Where(s => s.SongId >= cut).ToList();
        foreach (bool aware in new[] { false, true })
            foreach (var v in new[] { ridgeVariant, variants.First(x => x.Name == (aware ? treeAware : treeFree)) })
            {
                var m = LevelFit.Train(oldSongs, aware ? ridgeAware : ridgeFree, v.Trees ? LevelFit.TreeNames(aware) : null, lambda, v.Isotonic, folds, seed);
                var p = newSongs.Select(s => (s, LevelFit.Predict(m, s))).ToList();
                Program.Report($"temporal hold-out {(aware ? "slot-aware" : "slot-free ")} {v.Name}: train id<{cut} n={oldSongs.Count}, test n={newSongs.Count}", p, perSlot: false);
            }

        // final models of both forms
        DifficultyCoefficients build(Variant free, Variant aware)
        {
            var mf = LevelFit.Train(data, ridgeFree, free.Trees ? LevelFit.TreeNames(false) : null, lambda, free.Isotonic, folds, seed);
            var ma = LevelFit.Train(data, ridgeAware, aware.Trees ? LevelFit.TreeNames(true) : null, lambda, aware.Isotonic, folds, seed);
            Program.Finish(mf, results[(free.Name, false)], data);
            Program.Finish(ma, results[(aware.Name, true)], data);
            bool trees = free.Trees || aware.Trees;
            return new DifficultyCoefficients
            {
                Generated = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Generator = $"tools/DifficultyCalibrator ({folds}-fold song-grouped CV, ridge lambda {lambda}"
                            + (trees ? $", residual trees {LevelFit.TreeOptions.Trees}x depth {LevelFit.TreeOptions.MaxDepth} lr {LevelFit.TreeOptions.LearningRate.ToString(CultureInfo.InvariantCulture)}" : "")
                            + $"; slot-free: {free.Name}, slot-aware: {aware.Name})",
                TrainingCharts = data.Count,
                TrainingSongs = data.Select(s => s.SongId).Distinct().Count(),
                Note = trees
                    ? "Fitted aggregate parameters only. Level = [isotonic](ridge(DifficultyFeatures + slot) + trees(DifficultyFeatures, pat.* PatternAnalysis features, slot)); band = 80th percentile of out-of-fold |residual| per estimated level."
                    : "Fitted aggregate coefficients only. Level = isotonic(ridge(features)); band = 80th percentile of out-of-fold |residual| per estimated level.",
                SlotFree = mf,
                SlotAware = ma,
                MinLevel = 1,
                MaxLevel = Math.Ceiling(data.Max(s => s.Level)),
            };
        }
        var ridgeModel = build(ridgeVariant, ridgeVariant);
        var treeModel = build(variants.First(v => v.Name == treeFree), variants.First(v => v.Name == treeAware));

        // external check + decision
        // baseline = the coefficient file about to be replaced (or --baseline)
        DifficultyCoefficients? shipped = null;
        if (baselinePath != null && File.Exists(baselinePath))
        {
            try { shipped = DifficultyCoefficients.Parse(File.ReadAllText(baselinePath)); }
            catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { Console.WriteLine($"    baseline {baselinePath} unreadable: {e.Message}"); }
        }
        var nShipped = nautica != null && shipped != null ? Nautica(nautica, shipped, $"baseline {baselinePath}", verbose: false) : null;
        var nRidge = nautica != null ? Nautica(nautica, ridgeModel, "refitted ridge+isotonic", verbose: false) : null;
        var nTrees = nautica != null ? Nautica(nautica, treeModel, "ridge+pattern trees", verbose: true) : null;

        double cvRidge = Program.Mae(results[(ridgeVariant.Name, true)]), cvTrees = Program.Mae(results[(treeAware, true)]);
        double cvRidgeFree = Program.Mae(results[(ridgeVariant.Name, false)]), cvTreesFree = Program.Mae(results[(treeFree, false)]);
        // per design: trees only when better on CV and, on the fan charts, not worse than the refitted ridge model
        // and the currently embedded file
        bool awareTrees = cvTrees < cvRidge && (nTrees == null || nTrees.Value.Aware <= Math.Min(nRidge!.Value.Aware, nShipped?.Aware ?? double.MaxValue) + 1e-9);
        bool freeTrees = cvTreesFree < cvRidgeFree && (nTrees == null || nTrees.Value.Free <= Math.Min(nRidge!.Value.Free, nShipped?.Free ?? double.MaxValue) + 1e-9);
        Console.WriteLine();
        Console.WriteLine($"--- decision (CV MAE ridge vs trees; fan-chart MAE baseline / ridge / trees)");
        Console.WriteLine($"    slot-aware: CV {cvRidge:0.000} vs {cvTrees:0.000}" + (nTrees != null ? $"; fans {nShipped?.Aware:0.00} / {nRidge!.Value.Aware:0.00} / {nTrees.Value.Aware:0.00}" : "")
                          + $"  -> {(awareTrees ? "ridge + pattern trees" : "ridge + isotonic")}");
        Console.WriteLine($"    slot-free:  CV {cvRidgeFree:0.000} vs {cvTreesFree:0.000}" + (nTrees != null ? $"; fans {nShipped?.Free:0.00} / {nRidge!.Value.Free:0.00} / {nTrees.Value.Free:0.00}" : "")
                          + $"  -> {(freeTrees ? "ridge + pattern trees" : "ridge + isotonic")}");
        var chosen = awareTrees == freeTrees
            ? (awareTrees ? treeModel : ridgeModel)
            : build(freeTrees ? variants.First(v => v.Name == treeFree) : ridgeVariant, awareTrees ? variants.First(v => v.Name == treeAware) : ridgeVariant);
        if (chosen != treeModel && nautica != null) Nautica(nautica, chosen, "written file", verbose: true);
        return chosen;
    }

    /// <summary>Fan charts with author-declared levels; returns (slot-free MAE, slot-aware MAE).</summary>
    public static (double Free, double Aware)? Nautica(string root, DifficultyCoefficients coefficients, string label, bool verbose)
    {
        if (!Directory.Exists(root))
        {
            Console.WriteLine($"--- external check skipped: {root} not found");
            return null;
        }
        var est = new DifficultyEstimator(coefficients);
        var rows = new List<(string Name, int Slot, double Declared, double Free, double Aware, double Band)>();
        foreach (string dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            var song = SongFolder.Load(dir);
            foreach (var c in song.Charts)
            {
                if (c.Chart.Meta.Level <= 0) continue;
                var set = DifficultyFeatures.Extract(c.Chart);
                var pat = est.UsesPatterns ? PatternAnalysis.Analyze(c.Chart) : null;
                var free = est.Estimate(set, pat, c.Slot, new DifficultyEstimateOptions { UseDifficultySlot = false });
                var aware = est.Estimate(set, pat, c.Slot, new DifficultyEstimateOptions { UseDifficultySlot = true });
                if (free.EstimatedLevel == null || aware.EstimatedLevel == null) continue;
                rows.Add(($"{song.Name}/{Path.GetFileName(c.FilePath)}", c.Slot, c.Chart.Meta.Level, free.EstimatedLevel.Value, aware.EstimatedLevel.Value, aware.Band));
            }
        }
        if (rows.Count == 0) return null;
        Console.WriteLine();
        Console.WriteLine($"--- external check, {label}: fan charts with author-declared levels ({root}); never trained on");
        if (verbose)
            foreach (var r in rows)
                Console.WriteLine($"    {Trunc(r.Name, 44),-44} {SlotNames[Math.Clamp(r.Slot, 0, 4)]}  declared {r.Declared,4:0.0}  slot-free {r.Free,5:0.0} ({r.Free - r.Declared:+0.0;-0.0})  slot-aware {r.Aware,5:0.0} ({r.Aware - r.Declared:+0.0;-0.0}) ±{r.Band:0.0}");
        double mf = rows.Average(r => Math.Abs(r.Free - r.Declared)), ma = rows.Average(r => Math.Abs(r.Aware - r.Declared));
        Console.WriteLine($"    n={rows.Count}  slot-free MAE {mf:0.00} bias {rows.Average(r => r.Free - r.Declared):+0.00;-0.00}"
                          + $"  |  slot-aware MAE {ma:0.00} bias {rows.Average(r => r.Aware - r.Declared):+0.00;-0.00}"
                          + $"  within±1 (slot-aware) {100.0 * rows.Count(r => Math.Abs(r.Aware - r.Declared) <= 1) / rows.Count:0}%");
        return (mf, ma);
    }

    private static string Trunc(string s, int n)
    {
        var e = new StringInfo(s);
        return e.LengthInTextElements <= n ? s : e.SubstringByTextElements(0, n - 1) + "…";
    }
}
