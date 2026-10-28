using System.Text.Json;
using System.Text.Json.Serialization;
using SDVXEditor.Core.Patterns;

namespace SDVXEditor.Core.LevelEstimation;

/// <summary>One standardised linear term: <c>weight · (transform(x) − mean) / sd</c>.</summary>
public sealed class FeatureTerm
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";

    /// <summary><c>"id"</c> or <c>"log1p"</c>.</summary>
    [JsonPropertyName("transform")] public string Transform { get; set; } = "id";

    [JsonPropertyName("mean")] public double Mean { get; set; }
    [JsonPropertyName("sd")] public double Sd { get; set; } = 1;
    [JsonPropertyName("weight")] public double Weight { get; set; }

    public double Apply(double raw) => Transform == "log1p" ? Math.Log(1 + Math.Max(0, raw)) : raw;
}

/// <summary>Half-width of the confidence band for estimates in [<see cref="From"/>, next bucket).</summary>
public sealed class BandBucket
{
    [JsonPropertyName("from")] public double From { get; set; }
    [JsonPropertyName("half_width")] public double HalfWidth { get; set; }
    [JsonPropertyName("n")] public int Count { get; set; }
}

/// <summary>
/// A calibrated level model: ridge terms (+ optional gradient-boosted trees on the ridge residual, schema 2),
/// then a monotone (isotonic) post-map, then a band.
/// </summary>
public sealed class LevelModel
{
    [JsonPropertyName("intercept")] public double Intercept { get; set; }
    [JsonPropertyName("terms")] public List<FeatureTerm> Terms { get; set; } = new();

    /// <summary>Feature names the trees index into (DifficultyFeatures, PatternAnalysis and slot_1..4 names).</summary>
    [JsonPropertyName("tree_features")] public List<string>? TreeFeatures { get; set; }

    /// <summary>Residual trees, summed onto the ridge output before the post-map (learning rate folded in).</summary>
    [JsonPropertyName("trees")] public List<RegressionTree>? Trees { get; set; }

    /// <summary>Isotonic breakpoints (raw ridge output → level), ascending in both coordinates.</summary>
    [JsonPropertyName("isotonic_x")] public List<double> IsotonicX { get; set; } = new();
    [JsonPropertyName("isotonic_y")] public List<double> IsotonicY { get; set; } = new();

    /// <summary>Residual band by estimated level (80 % of out-of-fold residuals inside).</summary>
    [JsonPropertyName("band")] public List<BandBucket> Band { get; set; } = new();

    /// <summary>Band used when <see cref="Band"/> is empty.</summary>
    [JsonPropertyName("default_half_width")] public double DefaultHalfWidth { get; set; } = 1.0;

    /// <summary>Cross-validated MAE recorded by the calibrator (informational).</summary>
    [JsonPropertyName("cv_mae")] public double CvMae { get; set; }

    /// <summary>Training range of every transformed feature, for the out-of-range flag.</summary>
    [JsonPropertyName("ranges")] public Dictionary<string, double[]> Ranges { get; set; } = new();

    public double Raw(IReadOnlyDictionary<string, double> features, ICollection<string>? missing = null)
    {
        double y = Intercept;
        foreach (var t in Terms)
        {
            if (!features.TryGetValue(t.Name, out double raw))
            {
                missing?.Add(t.Name);
                continue; // standardised term at its mean contributes 0
            }
            double sd = t.Sd == 0 ? 1 : t.Sd;
            y += t.Weight * (t.Apply(raw) - t.Mean) / sd;
        }
        if (Trees is { Count: > 0 } && TreeFeatures != null)
        {
            var x = new double[TreeFeatures.Count];
            for (int i = 0; i < x.Length; i++)
            {
                if (features.TryGetValue(TreeFeatures[i], out double v)) x[i] = v;
                else missing?.Add(TreeFeatures[i]);
            }
            foreach (var tree in Trees) y += tree.Evaluate(x);
        }
        return y;
    }

    /// <summary>Contribution of the trees alone (0 without trees), for explanations.</summary>
    public double TreeContribution(IReadOnlyDictionary<string, double> features)
    {
        if (Trees is not { Count: > 0 } || TreeFeatures == null) return 0;
        var x = TreeFeatures.Select(n => features.TryGetValue(n, out double v) ? v : 0).ToArray();
        return Trees.Sum(t => t.Evaluate(x));
    }

    /// <summary>Isotonic map with linear interpolation; slope 1 beyond the last breakpoints.</summary>
    public double PostMap(double raw)
    {
        int n = Math.Min(IsotonicX.Count, IsotonicY.Count);
        if (n == 0) return raw;
        if (raw <= IsotonicX[0]) return IsotonicY[0] + (raw - IsotonicX[0]);
        if (raw >= IsotonicX[n - 1]) return IsotonicY[n - 1] + (raw - IsotonicX[n - 1]);
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (IsotonicX[mid] <= raw) lo = mid; else hi = mid;
        }
        double x0 = IsotonicX[lo], x1 = IsotonicX[hi];
        double f = x1 > x0 ? (raw - x0) / (x1 - x0) : 0;
        return IsotonicY[lo] + f * (IsotonicY[hi] - IsotonicY[lo]);
    }

    public double HalfWidthAt(double level)
    {
        if (Band.Count == 0) return DefaultHalfWidth;
        var b = Band[0];
        foreach (var bucket in Band)
            if (level >= bucket.From) b = bucket;
        return b.HalfWidth;
    }
}

/// <summary>The coefficient file (<c>Difficulty/coefficients.json</c>, embedded in Core).</summary>
public sealed class DifficultyCoefficients
{
    /// <summary>2 = ridge + optional residual trees; schema 1 files (ridge only) still load.</summary>
    public const int CurrentSchema = 2;

    [JsonPropertyName("schema")] public int Schema { get; set; } = CurrentSchema;
    [JsonPropertyName("generated")] public string Generated { get; set; } = "";
    [JsonPropertyName("generator")] public string Generator { get; set; } = "";
    [JsonPropertyName("training_charts")] public int TrainingCharts { get; set; }
    [JsonPropertyName("training_songs")] public int TrainingSongs { get; set; }
    [JsonPropertyName("note")] public string Note { get; set; } = "";

    /// <summary>Uses the difficulty slot (one-hot <c>slot_1..slot_4</c>, NOV = baseline).</summary>
    [JsonPropertyName("slot_aware")] public LevelModel? SlotAware { get; set; }

    /// <summary>Chart content only.</summary>
    [JsonPropertyName("slot_free")] public LevelModel? SlotFree { get; set; }

    /// <summary>Lowest and highest level the post-map may return.</summary>
    [JsonPropertyName("min_level")] public double MinLevel { get; set; } = 1;
    [JsonPropertyName("max_level")] public double MaxLevel { get; set; } = 21;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static DifficultyCoefficients Parse(string json)
    {
        var c = JsonSerializer.Deserialize<DifficultyCoefficients>(json, JsonOptions)
                ?? throw new FormatException("empty coefficient file");
        if (c.Schema is < 1 or > CurrentSchema) throw new FormatException($"coefficient schema {c.Schema}, expected 1..{CurrentSchema}");
        foreach (var m in new[] { c.SlotAware, c.SlotFree })
            foreach (var t in m?.Trees ?? new List<RegressionTree>())
                t.Validate(m!.TreeFeatures?.Count ?? 0);
        if (c.SlotAware == null && c.SlotFree == null) throw new FormatException("coefficient file has no model");
        return c;
    }

    public string ToJson() => ModelJson.Serialize(this, JsonOptions);

    public const string ResourceName = "SDVXEditor.Core.Difficulty.coefficients.json";

    /// <summary>The embedded coefficient file, or null when the build carried none (or it is unreadable).</summary>
    public static DifficultyCoefficients? LoadEmbedded(out string? error)
    {
        error = null;
        try
        {
            using var stream = typeof(DifficultyCoefficients).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                error = "no embedded coefficient file";
                return null;
            }
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException)
        {
            error = "embedded coefficient file unreadable: " + e.Message;
            return null;
        }
    }

    /// <summary>
    /// Built-in fallback when no calibrated file exists: a three-term density/strain fit with a
    /// deliberately wide band. Numbers come from a compact calibrator fit (see the calibrator's
    /// "fallback" output) so the editor still shows a rough hint on a build without the resource.
    /// </summary>
    public static DifficultyCoefficients Fallback { get; } = new()
    {
        Generator = "built-in fallback",
        Note = "Uncalibrated fallback; run tools/DifficultyCalibrator against your own install for real coefficients.",
        SlotFree = new LevelModel
        {
            Intercept = FallbackConstants.Intercept,
            Terms = FallbackConstants.Terms.Select(t => new FeatureTerm
            {
                Name = t.Name, Transform = t.Transform, Mean = t.Mean, Sd = t.Sd, Weight = t.Weight,
            }).ToList(),
            DefaultHalfWidth = 2.0,
            CvMae = FallbackConstants.CvMae,
        },
    };
}
