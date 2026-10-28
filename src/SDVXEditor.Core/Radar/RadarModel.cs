using System.Text.Json;
using System.Text.Json.Serialization;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Patterns;

namespace SDVXEditor.Core.Radar;

/// <summary>Validation numbers the calibrator recorded for one axis (informational).</summary>
public sealed class RadarAxisMetrics
{
    [JsonPropertyName("cv_mae")] public double CvMae { get; set; }
    [JsonPropertyName("cv_r")] public double CvR { get; set; }
    [JsonPropertyName("holdout_mae")] public double HoldoutMae { get; set; }
    [JsonPropertyName("holdout_r")] public double HoldoutR { get; set; }
    [JsonPropertyName("classic_mae")] public double ClassicMae { get; set; }
    [JsonPropertyName("classic_r")] public double ClassicR { get; set; }
}

/// <summary>
/// The fitted radar model file (<c>Radar/radar_model.json</c>, embedded in Core): one <see cref="AdditiveModel"/>
/// per axis over <see cref="PatternAnalysis"/> features. Aggregate numbers only (coefficients, tree splits, leaf
/// values), written by <c>tools/DifficultyCalibrator</c> from the user's own install. Spec: docs/RADAR_MODEL.md.
/// </summary>
public sealed class RadarModelFile
{
    public const int CurrentSchema = 1;
    public const string ResourceName = "SDVXEditor.Core.Radar.radar_model.json";

    [JsonPropertyName("schema")] public int Schema { get; set; } = CurrentSchema;
    [JsonPropertyName("generated")] public string Generated { get; set; } = "";
    [JsonPropertyName("generator")] public string Generator { get; set; } = "";
    [JsonPropertyName("training_charts")] public int TrainingCharts { get; set; }
    [JsonPropertyName("hand_model")] public string HandModel { get; set; } = "default";
    [JsonPropertyName("note")] public string Note { get; set; } = "";

    /// <summary>Axis key (<see cref="SkillRadar.Key"/>: notes, peak, tsumami, tricky, hand_trip, one_hand) → model.</summary>
    [JsonPropertyName("axes")] public Dictionary<string, AdditiveModel> Axes { get; set; } = new();

    [JsonPropertyName("metrics")] public Dictionary<string, RadarAxisMetrics> Metrics { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static RadarModelFile Parse(string json)
    {
        var m = JsonSerializer.Deserialize<RadarModelFile>(json, JsonOptions) ?? throw new FormatException("empty radar model");
        if (m.Schema != CurrentSchema) throw new FormatException($"radar model schema {m.Schema}, expected {CurrentSchema}");
        foreach (var axis in SkillRadar.Axes)
        {
            if (!m.Axes.TryGetValue(SkillRadar.Key(axis), out var model)) throw new FormatException($"radar model lacks axis {SkillRadar.Key(axis)}");
            model.Validate();
        }
        return m;
    }

    /// <summary>Indented, with every tree and number array on one line (<see cref="ModelJson"/>).</summary>
    public string ToJson() => ModelJson.Serialize(this, JsonOptions);

    /// <summary>The embedded model, or null when the build carried none (or it is unreadable).</summary>
    public static RadarModelFile? LoadEmbedded(out string? error)
    {
        error = null;
        try
        {
            using var stream = typeof(RadarModelFile).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                error = "no embedded radar model";
                return null;
            }
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception e) when (e is JsonException or FormatException or IOException)
        {
            error = "embedded radar model unreadable: " + e.Message;
            return null;
        }
    }
}

/// <summary>Result of <see cref="SkillRadarModel.Estimate"/>.</summary>
public sealed class RadarEstimate
{
    public SkillRadar Radar { get; init; }

    /// <summary>True when the classic radar (radar.py port) was used because no fitted model is available.</summary>
    public bool IsClassic { get; init; }

    public string? FallbackReason { get; init; }
    public IReadOnlyDictionary<string, double> Features { get; init; } = new Dictionary<string, double>();
}

/// <summary>
/// The data-driven skill radar: <see cref="PatternAnalysis"/> features → per-axis <see cref="AdditiveModel"/> →
/// rounded integer on the music_db scale (0..200). Falls back to the classic <see cref="RadarCalculator"/> when the
/// embedded model is missing. An empty chart is all zero, like the classic radar.
/// </summary>
public sealed class SkillRadarModel
{
    private static readonly Lazy<SkillRadarModel> defaultInstance = new(() =>
    {
        var m = RadarModelFile.LoadEmbedded(out string? error);
        return new SkillRadarModel(m, error);
    });

    public static SkillRadarModel Default => defaultInstance.Value;

    public RadarModelFile? Model { get; }
    public string? FallbackReason { get; }
    public bool IsClassic => Model == null;

    public SkillRadarModel(RadarModelFile? model, string? fallbackReason = null)
    {
        Model = model;
        FallbackReason = model == null ? fallbackReason ?? "no radar model" : null;
    }

    /// <summary>Radar of a chart file (.ksh, .kson, .vox), read the way the classic radar reads it.</summary>
    public RadarEstimate EstimateFile(string path)
    {
        var (chart, source) = RadarCalculator.ReadForRadar(path);
        return Estimate(chart, source);
    }

    /// <param name="classicSource">How the classic fallback sees the chart (ksh slam expansion etc.).</param>
    public RadarEstimate Estimate(Chart chart, RadarSource classicSource = RadarSource.Kson)
    {
        if (Model == null)
            return new RadarEstimate { Radar = RadarCalculator.Compute(chart, classicSource), IsClassic = true, FallbackReason = FallbackReason };

        var set = PatternAnalysis.Analyze(chart);
        var features = set.ToDictionary();
        if (set.IsEmpty) return new RadarEstimate { Radar = default, Features = features };

        int axis(RadarAxis a) => (int)Math.Round(Model.Axes[SkillRadar.Key(a)].Predict(features), MidpointRounding.AwayFromZero);
        var radar = new SkillRadar(axis(RadarAxis.Notes), axis(RadarAxis.Peak), axis(RadarAxis.Tsumami),
                                   axis(RadarAxis.Tricky), axis(RadarAxis.HandTrip), axis(RadarAxis.OneHand));
        return new RadarEstimate { Radar = radar, Features = features };
    }
}
