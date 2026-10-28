using System.Text.Json.Serialization;

namespace SDVXEditor.Core.Patterns;

/// <summary>
/// One regression tree in array form. Node <c>i</c> is a split when <see cref="Feature"/>[i] ≥ 0: go to
/// <see cref="Left"/>[i] when <c>x[feature] ≤ threshold</c>, else <see cref="Right"/>[i]; otherwise it is a leaf
/// with <see cref="Value"/>[i]. Node 0 is the root. A missing feature counts as 0.
/// </summary>
public sealed class RegressionTree
{
    [JsonPropertyName("f")] public int[] Feature { get; set; } = Array.Empty<int>();
    [JsonPropertyName("t")] public double[] Threshold { get; set; } = Array.Empty<double>();
    [JsonPropertyName("l")] public int[] Left { get; set; } = Array.Empty<int>();
    [JsonPropertyName("r")] public int[] Right { get; set; } = Array.Empty<int>();
    [JsonPropertyName("v")] public double[] Value { get; set; } = Array.Empty<double>();

    public double Evaluate(double[] x)
    {
        int i = 0;
        for (int guard = 0; guard < 64; guard++)
        {
            int f = Feature[i];
            if (f < 0) return Value[i];
            i = x[f] <= Threshold[i] ? Left[i] : Right[i];
        }
        throw new FormatException("regression tree too deep or cyclic");
    }

    public void Validate(int featureCount)
    {
        int n = Feature.Length;
        if (Threshold.Length != n || Left.Length != n || Right.Length != n || Value.Length != n || n == 0)
            throw new FormatException("regression tree arrays differ in length");
        for (int i = 0; i < n; i++)
        {
            if (Feature[i] >= featureCount) throw new FormatException($"tree feature index {Feature[i]} out of range");
            if (Feature[i] >= 0 && (Left[i] <= i || Right[i] <= i || Left[i] >= n || Right[i] >= n))
                throw new FormatException("tree children must point forward");
        }
    }
}

/// <summary>A linear term <c>weight · transform(x)</c>; transforms <c>id</c> and <c>log1p</c> (of max(0, x)).</summary>
public sealed class LinearTerm
{
    [JsonPropertyName("feature")] public string Feature { get; set; } = "";
    [JsonPropertyName("transform")] public string Transform { get; set; } = "id";
    [JsonPropertyName("weight")] public double Weight { get; set; }

    public double Apply(double x) => Transform == "log1p" ? Math.Log(1 + Math.Max(0, x)) : x;
}

/// <summary>
/// An additive model: <c>intercept + Σ linear terms + Σ trees</c>, then clamped to [<see cref="Min"/>,
/// <see cref="Max"/>]. Either part may be empty. Format and evaluation order are specified in
/// <c>docs/RADAR_MODEL.md</c> §3 so a Python port gives the same number.
/// </summary>
public sealed class AdditiveModel
{
    /// <summary>Feature names the trees index into (positions = tree feature indices).</summary>
    [JsonPropertyName("features")] public List<string> Features { get; set; } = new();

    [JsonPropertyName("intercept")] public double Intercept { get; set; }
    [JsonPropertyName("linear")] public List<LinearTerm> Linear { get; set; } = new();
    [JsonPropertyName("trees")] public List<RegressionTree> Trees { get; set; } = new();
    [JsonPropertyName("min")] public double Min { get; set; } = double.NegativeInfinity;
    [JsonPropertyName("max")] public double Max { get; set; } = double.PositiveInfinity;

    /// <summary>Model form chosen by the calibrator (informational): "linear", "trees" or "linear+trees".</summary>
    [JsonPropertyName("form")] public string Form { get; set; } = "";

    public double[] Vector(IReadOnlyDictionary<string, double> features, ICollection<string>? missing = null)
    {
        var x = new double[Features.Count];
        for (int i = 0; i < x.Length; i++)
        {
            if (features.TryGetValue(Features[i], out double v)) x[i] = v;
            else missing?.Add(Features[i]);
        }
        return x;
    }

    /// <summary>Unclamped sum.</summary>
    public double Raw(IReadOnlyDictionary<string, double> features, ICollection<string>? missing = null)
    {
        double y = Intercept;
        foreach (var t in Linear)
        {
            if (features.TryGetValue(t.Feature, out double v)) y += t.Weight * t.Apply(v);
            else missing?.Add(t.Feature);
        }
        if (Trees.Count > 0)
        {
            var x = Vector(features, missing);
            foreach (var tree in Trees) y += tree.Evaluate(x);
        }
        return y;
    }

    public double Predict(IReadOnlyDictionary<string, double> features, ICollection<string>? missing = null)
        => Math.Clamp(Raw(features, missing), Min, Max);

    public void Validate()
    {
        foreach (var t in Trees) t.Validate(Features.Count);
        foreach (var t in Linear)
            if (t.Transform is not ("id" or "log1p")) throw new FormatException($"unknown transform {t.Transform}");
    }
}
