using System.Globalization;
using SDVXEditor.Core.Patterns;
using SDVXEditor.Core.Radar;

namespace SDVXEditor.Tools.DifficultyCalibrator;

/// <summary>Radar part of the calibrator run: baseline, CV per form, hold-outs, final fit, importance.</summary>
public static class RadarReport
{
    /// <summary>Simplest first; a later form must beat the chosen one by max(0.1, 5 %) of MAE to replace it.</summary>
    /// <summary>
    /// Pattern-feature prefixes no model may use. Default <c>cam_</c>: on official charts camera work grows with the
    /// difficulty slot (median zoom travel 0 on NOV, 0.085/s on MXM), so the trees learn "camera = hard chart"; fan
    /// charts use the camera at any difficulty (a fan NOV with zoom came out TRICKY 67, above its own MXM). Costs
    /// TRICKY CV MAE 8.5 → 10.7, buys sane fan-chart values. <c>--radar-exclude ""</c> lifts it.
    /// </summary>
    public static string[] Excluded { get; set; } = { "cam_" };

    public static bool IsExcluded(string feature) => Excluded.Any(x => feature.StartsWith(x, StringComparison.Ordinal));

    public static readonly string[] Forms = { "linear", "linear+trees", "trees" };

    public static RadarModelFile Run(List<Sample> all, int folds, int seed, string[]? onlyForms = null, int[]? onlyAxes = null)
    {
        var data = RadarFit.WithRadar(all);
        var features = PatternAnalysis.FeatureNames.Where(n => !IsExcluded(n)).ToList();
        Console.WriteLine();
        if (Excluded.Length > 0) Console.WriteLine($"(features excluded from the radar trees: {string.Join(", ", Excluded)}*)");
        Console.WriteLine($"=== SKILL RADAR: {data.Count} charts with a music_db radar, {features.Count} pattern features, trees {RadarFit.TreeOptions}");
        var classic = RadarFit.Classic(data);
        var foldOf = Dataset.FoldAssignment(data.Select(t => t.S.SongId), folds, seed);

        var file = new RadarModelFile
        {
            Generated = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Generator = $"tools/DifficultyCalibrator (per axis: linear base + gradient-boosted trees, {folds}-fold song-grouped CV)",
            TrainingCharts = data.Count,
            HandModel = HandModel.Default.Name,
            Note = "Fitted aggregate parameters only (OLS weights, tree split thresholds on pattern features, leaf values). No chart data.",
        };

        const int cut = 2100;
        var oldIdx = Enumerable.Range(0, data.Count).Where(i => data[i].S.SongId < cut).ToList();
        var newIdx = Enumerable.Range(0, data.Count).Where(i => data[i].S.SongId >= cut).ToList();

        foreach (var axis in SkillRadar.Axes)
        {
            int a = (int)axis;
            if (onlyAxes != null && !onlyAxes.Contains(a)) continue;
            var truth = data.Select(t => t.Radar[a]).ToArray();
            Console.WriteLine();
            Console.WriteLine($"--- {SkillRadar.Label(axis)}  (official mean {truth.Average():0.0}, zeros {100.0 * truth.Count(v => v == 0) / truth.Length:0}%)");
            var classicM = RadarFit.Score(classic.Select(c => c[a]).ToArray(), truth);
            Console.WriteLine($"    classic radar.py    {RadarFit.Fmt(classicM)}");

            string best = "";
            int[] bestPred = Array.Empty<int>();
            RadarFit.Metrics bestM = default;
            foreach (string form in onlyForms ?? Forms)
            {
                if (form.StartsWith("linear", StringComparison.Ordinal) && RadarFit.BaseTerms[a].Length == 0 && form == "linear") continue;
                var pred = RadarFit.CrossValidate(data, a, form, features, foldOf, folds);
                var m = RadarFit.Score(pred, truth);
                Console.WriteLine($"    CV {form,-16} {RadarFit.Fmt(m)}");
                if (best == "" || m.Mae < bestM.Mae - Math.Max(0.1, 0.05 * bestM.Mae)) { best = form; bestPred = pred; bestM = m; }
            }
            Console.WriteLine($"    chosen: {best}");
            foreach (var g in Enumerable.Range(0, data.Count).GroupBy(i => data[i].S.VoxVersion).OrderBy(g => g.Key))
            {
                var idx = g.ToList();
                Console.WriteLine($"      VOX v{g.Key,-2}          {RadarFit.Fmt(RadarFit.Score(idx.Select(i => bestPred[i]).ToArray(), idx.Select(i => truth[i]).ToArray()))}"
                                  + $"   classic MAE {idx.Average(i => Math.Abs(classic[i][a] - truth[i])):0.0}");
            }
            foreach (var g in Enumerable.Range(0, data.Count).GroupBy(i => data[i].S.Slot).OrderBy(g => g.Key))
            {
                var idx = g.ToList();
                Console.WriteLine($"      slot {g.Key}            {RadarFit.Fmt(RadarFit.Score(idx.Select(i => bestPred[i]).ToArray(), idx.Select(i => truth[i]).ToArray()))}");
            }

            // temporal hold-out: newest songs
            var trainOld = oldIdx.Select(i => data[i]).ToList();
            var hm = RadarFit.Train(trainOld, a, best, features);
            var hp = newIdx.Select(i => RadarFit.Predict(hm, data[i].S)).ToArray();
            var hmM = RadarFit.Score(hp, newIdx.Select(i => truth[i]).ToArray());
            Console.WriteLine($"    hold-out id>={cut}   {RadarFit.Fmt(hmM)}   (train id<{cut}, n={trainOld.Count})"
                              + $"   classic MAE {newIdx.Average(i => Math.Abs(classic[i][a] - truth[i])):0.0}");

            // final
            var gains = new double[features.Count];
            var model = RadarFit.Train(data, a, best, features, gains: gains);
            file.Axes[SkillRadar.Key(axis)] = model;
            file.Metrics[SkillRadar.Key(axis)] = new RadarAxisMetrics
            {
                CvMae = Math.Round(bestM.Mae, 3), CvR = Math.Round(bestM.R, 4),
                HoldoutMae = Math.Round(hmM.Mae, 3), HoldoutR = Math.Round(hmM.R, 4),
                ClassicMae = Math.Round(classicM.Mae, 3), ClassicR = Math.Round(classicM.R, 4),
            };
            if (model.Linear.Count > 0)
                Console.WriteLine($"    linear: {model.Intercept:0.###} + " + string.Join(" + ", model.Linear.Select(t => $"{t.Weight:0.###}·{t.Feature}")));
            double gsum = gains.Sum();
            if (gsum > 0)
                Console.WriteLine("    trees, gain share: " + string.Join(", ", gains.Select((g, j) => (g, j)).OrderByDescending(x => x.g).Take(8)
                                      .Select(x => $"{features[x.j]} {100 * x.g / gsum:0}%")));
        }
        return file;
    }

    /// <summary>
    /// Hand-model check: the ONE-HAND / HAND-TRIP base statistics under alternative lane→hand assignments
    /// (correlation with the official values, and CV of the linear+trees model).
    /// </summary>
    public static void HandModels(List<Sample> all, int folds, int seed)
    {
        var data = RadarFit.WithRadar(all);
        var models = new[]
        {
            HandModel.Default,
            new HandModel("fx-swapped", new[] { 0, 0, 1, 1, 1, 0 }),
            new HandModel("bc-swapped", new[] { 0, 1, 0, 1, 0, 1 }),
            new HandModel("fx-to-bt-hand-inverted", new[] { 1, 1, 0, 0, 1, 0 }),
        };
        Console.WriteLine();
        Console.WriteLine("--- hand model check (r of the base statistic with the official axis; CV MAE of linear+trees)");
        var foldOf = Dataset.FoldAssignment(data.Select(t => t.S.SongId), folds, seed);
        var features = PatternAnalysis.FeatureNames.Where(n => !IsExcluded(n)).ToList();
        foreach (var hm in models)
        {
            var patterns = new Dictionary<string, double>[data.Count];
            Parallel.For(0, data.Count, i => patterns[i] = PatternAnalysis.Analyze(data[i].S.Chart, hm).ToDictionary());
            var swapped = data.Select((t, i) => (t.S with { Patterns = patterns[i] }, t.Radar)).ToList();
            string line = $"    {hm.Name,-24}";
            foreach (var (a, stat) in new[] { (5, "onehand_ps"), (4, "handtrip_ps") })
            {
                double r = RadarFit.Pearson(patterns.Select(p => p[stat]).ToArray(), data.Select(t => (double)t.Radar[a]).ToArray());
                var pred = RadarFit.CrossValidate(swapped, a, "linear+trees", features, foldOf, folds);
                var m = RadarFit.Score(pred, data.Select(t => t.Radar[a]).ToArray());
                line += $"  {SkillRadar.Label((RadarAxis)a)}: r({stat}) {r:0.000}, CV MAE {m.Mae:0.00}";
            }
            Console.WriteLine(line);
        }
    }

    /// <summary>
    /// Fan charts (no official radar): the new radar beside the classic one for every .ksh / .kson, so ksh/kson
    /// twins and implausible values are visible, plus the per-axis correlation between the two.
    /// </summary>
    public static void Nautica(string root, RadarModelFile file)
    {
        if (!Directory.Exists(root)) return;
        var model = new SkillRadarModel(file);
        var rows = new List<(string Name, SkillRadar New, SkillRadar Classic)>();
        foreach (string path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                                         .Where(f => f.EndsWith(".ksh", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".kson", StringComparison.OrdinalIgnoreCase))
                                         .OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                rows.Add((Path.GetRelativePath(root, path), model.EstimateFile(path).Radar, RadarCalculator.ComputeFile(path)));
            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or IOException)
            {
                Console.WriteLine($"    skipped {path}: {e.Message}");
            }
        }
        if (rows.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine($"--- fan charts ({root}): new radar [classic radar.py]; no official values exist for these");
        foreach (var r in rows)
            Console.WriteLine($"    {r.Name,-58} {r.New}  [{r.Classic}]");
        Console.WriteLine("    r(new, classic) per axis: " + string.Join(", ", SkillRadar.Axes.Select(a =>
            $"{SkillRadar.Label(a)} {RadarFit.Pearson(rows.Select(x => (double)x.New[a]).ToArray(), rows.Select(x => (double)x.Classic[a]).ToArray()):0.00}")));
        Console.WriteLine("    max per axis (new): " + string.Join(", ", SkillRadar.Axes.Select(a => $"{SkillRadar.Label(a)} {rows.Max(x => x.New[a])}")));
    }
}
