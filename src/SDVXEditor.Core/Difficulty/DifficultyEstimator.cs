using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Patterns;

namespace SDVXEditor.Core.LevelEstimation;

public sealed class DifficultyEstimateOptions
{
    /// <summary>
    /// Use the chart's difficulty slot (NOV/ADV/EXH/INF/MXM) as an input. Worth ~0.1–0.2 levels of
    /// accuracy on official charts, but it pulls the estimate toward the slot's usual range.
    /// </summary>
    public bool UseDifficultySlot { get; set; } = true;
}

/// <summary>Result of a level estimate. Never written back to the chart automatically.</summary>
public sealed class DifficultyResult
{
    /// <summary>Estimated level, one decimal (∇ levels carry tenths). Null for a chart without notes.</summary>
    public double? EstimatedLevel { get; init; }

    /// <summary>Half-width of the confidence band (±), from out-of-fold residuals at this level.</summary>
    public double Band { get; init; }

    public double? Low => EstimatedLevel is { } l ? Math.Round(l - Band, 1) : null;
    public double? High => EstimatedLevel is { } l ? Math.Round(l + Band, 1) : null;

    /// <summary>Unrounded calibrated value (before clamping to the level range), for tooling.</summary>
    public double RawLevel { get; init; }

    /// <summary>True when a reason below applies; the band is then widened.</summary>
    public bool LowConfidence => LowConfidenceReasons.Count > 0;

    public IReadOnlyList<string> LowConfidenceReasons { get; init; } = Array.Empty<string>();

    /// <summary>400 ms sections for an osu!-style strain graph (peak strain per section).</summary>
    public IReadOnlyList<StrainSection> PerSectionStrain { get; init; } = Array.Empty<StrainSection>();

    /// <summary>Raw (untransformed) features by name.</summary>
    public IReadOnlyDictionary<string, double> Features { get; init; } = new Dictionary<string, double>();

    /// <summary>Largest contributors to the estimate (standardised weight × value), descending.</summary>
    public IReadOnlyList<(string Feature, double Contribution)> TopContributors { get; init; } = Array.Empty<(string, double)>();

    /// <summary>Which model produced the estimate: "slot_aware", "slot_free" or "fallback".</summary>
    public string ModelName { get; init; } = "";

    public bool UsedFallback { get; init; }

    public override string ToString()
        => EstimatedLevel is { } l
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Est. {l:0.0} (±{Band:0.0}){(LowConfidence ? " ?" : "")}")
            : "Est. –";
}

/// <summary>
/// Level estimation: <see cref="DifficultyFeatures"/> → ridge terms → isotonic post-map → band.
/// Cheap enough to recompute on every edit (a full chart is a few milliseconds).
/// </summary>
public sealed class DifficultyEstimator
{
    private static readonly Lazy<DifficultyEstimator> defaultInstance = new(CreateDefault);

    /// <summary>The estimator with the embedded coefficients, or the built-in fallback if there are none.</summary>
    public static DifficultyEstimator Default => defaultInstance.Value;

    public DifficultyCoefficients Coefficients { get; }
    public bool IsFallback { get; }

    /// <summary>Why the fallback is in use (null when the embedded file loaded).</summary>
    public string? FallbackReason { get; }

    public DifficultyEstimator(DifficultyCoefficients coefficients, bool isFallback = false, string? fallbackReason = null)
    {
        Coefficients = coefficients;
        IsFallback = isFallback;
        FallbackReason = fallbackReason;
    }

    private static DifficultyEstimator CreateDefault()
    {
        var c = DifficultyCoefficients.LoadEmbedded(out string? error);
        return c != null ? new DifficultyEstimator(c) : new DifficultyEstimator(DifficultyCoefficients.Fallback, true, error);
    }

    /// <summary>Prefix of <see cref="PatternAnalysis"/> features among the level model's inputs.</summary>
    public const string PatternPrefix = "pat.";

    /// <summary>True when a model of this estimator reads pattern features (trees over <c>pat.*</c> names).</summary>
    public bool UsesPatterns =>
        new[] { Coefficients.SlotAware, Coefficients.SlotFree }.Any(m => m?.TreeFeatures?.Any(n => n.StartsWith(PatternPrefix, StringComparison.Ordinal)) == true);

    public DifficultyResult Estimate(Chart chart, DifficultyEstimateOptions? options = null)
        => Estimate(DifficultyFeatures.Extract(chart), UsesPatterns ? PatternAnalysis.Analyze(chart) : null, chart.Meta.DifficultySlot, options);

    public DifficultyResult Estimate(ChartFeatureSet set, int slot, DifficultyEstimateOptions? options = null)
        => Estimate(set, null, slot, options);

    /// <param name="patterns">Pattern features; required for a model with pattern trees (else they count as missing).</param>
    public DifficultyResult Estimate(ChartFeatureSet set, PatternFeatureSet? patterns, int slot, DifficultyEstimateOptions? options = null)
    {
        options ??= new DifficultyEstimateOptions();
        bool slotAware = options.UseDifficultySlot && Coefficients.SlotAware != null;
        var model = slotAware ? Coefficients.SlotAware! : Coefficients.SlotFree ?? Coefficients.SlotAware!;
        string name = IsFallback ? "fallback" : model == Coefficients.SlotAware ? "slot_aware" : "slot_free";

        var features = set.ToDictionary();
        if (set.IsEmpty)
        {
            return new DifficultyResult
            {
                EstimatedLevel = null,
                LowConfidenceReasons = new[] { "chart has no notes" },
                Features = features,
                ModelName = name,
                UsedFallback = IsFallback,
            };
        }

        var inputs = new Dictionary<string, double>(features);
        if (patterns != null)
            foreach (var kv in patterns.Features) inputs[PatternPrefix + kv.Key] = kv.Value;
        if (model == Coefficients.SlotAware)
            for (int s = 1; s <= 4; s++) inputs["slot_" + s] = Math.Clamp(slot, 0, 4) == s ? 1 : 0;

        var missing = new List<string>();
        double raw = model.Raw(inputs, missing);
        double level = model.PostMap(raw);
        double clamped = Math.Clamp(level, Coefficients.MinLevel, Coefficients.MaxLevel);
        double band = model.HalfWidthAt(clamped);

        var reasons = new List<string>();
        if (IsFallback) reasons.Add("uncalibrated fallback model" + (FallbackReason != null ? $" ({FallbackReason})" : ""));
        if (missing.Count > 0) reasons.Add("features missing from this build: " + string.Join(", ", missing));
        if (set.DurationSeconds < 30) reasons.Add("very short chart (< 30 s)");
        if (set.EventCount < 100) reasons.Add("very few events (< 100)");
        if (set.BpmMax > 400) reasons.Add("extreme BPM (> 400)");
        if (set.BpmChangeCount > 20) reasons.Add("many BPM changes (> 20)");
        if (level > Coefficients.MaxLevel + 0.5 || level < Coefficients.MinLevel - 0.5) reasons.Add("estimate outside the level range");
        var outOfRange = new List<string>();
        foreach (var t in model.Terms)
        {
            if (!model.Ranges.TryGetValue(t.Name, out var r) || r.Length < 2 || !inputs.TryGetValue(t.Name, out double v)) continue;
            double x = t.Apply(v), span = r[1] - r[0];
            if (x < r[0] - 0.25 * span || x > r[1] + 0.25 * span) outOfRange.Add(t.Name);
        }
        if (outOfRange.Count > 0) reasons.Add("features far outside the training range: " + string.Join(", ", outOfRange));
        if (reasons.Count > 0 && !IsFallback) band *= 1.5;

        var contributions = model.Terms
            .Where(t => inputs.ContainsKey(t.Name))
            .Select(t => (t.Name, t.Weight * (t.Apply(inputs[t.Name]) - t.Mean) / (t.Sd == 0 ? 1 : t.Sd)))
            .Append(("pattern trees", model.TreeContribution(inputs)))
            .Where(x => x.Item2 != 0)
            .OrderByDescending(x => Math.Abs(x.Item2))
            .Take(5)
            .ToList();

        return new DifficultyResult
        {
            EstimatedLevel = Math.Round(clamped, 1, MidpointRounding.AwayFromZero),
            Band = Math.Round(band, 1, MidpointRounding.AwayFromZero),
            RawLevel = level,
            LowConfidenceReasons = reasons,
            PerSectionStrain = set.Sections,
            Features = features,
            TopContributors = contributions,
            ModelName = name,
            UsedFallback = IsFallback,
        };
    }
}
