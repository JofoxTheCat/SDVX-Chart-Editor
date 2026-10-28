using SDVXEditor.Core.Editing;

namespace SDVXEditor.Core.Audio.Effects;

/// <summary>The kson effect types the preview engine knows.</summary>
public enum EffectKind
{
    None,
    Retrigger,
    Gate,
    Flanger,
    PitchShift,
    BitCrusher,
    Phaser,
    Wobble,
    TapeStop,
    Echo,
    SideChain,
    SwitchAudio,
    PeakingFilter,
    HighPassFilter,
    LowPassFilter,
}

/// <summary>Every parameter name of every effect type (kson snake_case), as an index into <see cref="EffectParams"/>.</summary>
public enum ParamId
{
    UpdatePeriod,
    WaveLength,
    Rate,
    UpdateTrigger,
    Mix,
    Period,
    Delay,
    Depth,
    Feedback,
    StereoWidth,
    Vol,
    Pitch,
    ChunkSize,
    Overlap,
    Reduction,
    Stage,
    Freq1,
    Freq2,
    Q,
    HiCutGain,
    Speed,
    Trigger,
    FeedbackLevel,
    HoldTime,
    AttackTime,
    ReleaseTime,
    Ratio,
    V,
    Freq,
    FreqMax,
    Bandwidth,
    Gain,
}

/// <summary>One parameter of an effect type: id, value kind and the default value set.</summary>
public sealed record EffectParamSpec(ParamId Id, string Name, ParamKind Kind, EffectValueSet Default);

/// <summary>
/// Parameters and defaults per effect type. Defaults are the kson spec's (via <see cref="EffectCatalog"/>); the
/// parameters the spec leaves "implementation-dependent" use K-Shoot Mania v2's values (ksmaudio
/// <c>Params/*Params.hpp</c>): peaking filter 50–9000 Hz, bandwidth 1.2 oct; low-pass 15000–800 Hz, q 3.6; high-pass
/// 100–2200 Hz, q 5.0 (these q are exactly <c>pfiltergain</c> 50 % of the spec's filter-gain mapping).
/// </summary>
public static class EffectSpecs
{
    public static readonly int ParamCount = Enum.GetValues<ParamId>().Length;

    private static readonly Dictionary<string, ParamId> ids = Enum.GetValues<ParamId>().ToDictionary(snake, p => p);

    private static readonly Lazy<Dictionary<EffectKind, EffectParamSpec[]>> lazySpecs = new(build);

    private static Dictionary<EffectKind, EffectParamSpec[]> specs => lazySpecs.Value;

    private static string snake(ParamId id)
    {
        var s = id.ToString();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsUpper(c) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().Replace("freq1", "freq_1").Replace("freq2", "freq_2");
    }

    /// <summary>kson parameter name → id (null for a name no effect has).</summary>
    public static ParamId? IdOf(string name) => ids.TryGetValue(name, out var id) ? id : null;

    public static string NameOf(ParamId id) => snake(id);

    /// <summary>kson type name (<c>pitch_shift</c>) → kind; also accepts the ksh PascalCase names.</summary>
    public static EffectKind KindOf(string? type) => (type ?? "").Replace("_", "").ToLowerInvariant() switch
    {
        "retrigger" => EffectKind.Retrigger,
        "gate" => EffectKind.Gate,
        "flanger" => EffectKind.Flanger,
        "pitchshift" => EffectKind.PitchShift,
        "bitcrusher" => EffectKind.BitCrusher,
        "phaser" => EffectKind.Phaser,
        "wobble" => EffectKind.Wobble,
        "tapestop" => EffectKind.TapeStop,
        "echo" => EffectKind.Echo,
        "sidechain" => EffectKind.SideChain,
        "switchaudio" => EffectKind.SwitchAudio,
        "peakingfilter" => EffectKind.PeakingFilter,
        "highpassfilter" => EffectKind.HighPassFilter,
        "lowpassfilter" => EffectKind.LowPassFilter,
        _ => EffectKind.None,
    };

    public static IReadOnlyList<EffectParamSpec> Of(EffectKind kind) => specs.TryGetValue(kind, out var s) ? s : Array.Empty<EffectParamSpec>();

    public static EffectParamSpec? Find(EffectKind kind, ParamId id) => Of(kind).FirstOrDefault(p => p.Id == id);

    /// <summary>Engine defaults for the parameters the spec leaves open (K-Shoot Mania v2 values).</summary>
    private static readonly Dictionary<(EffectKind, string), string> engineDefaults = new()
    {
        [(EffectKind.PeakingFilter, "freq")] = "50Hz",
        [(EffectKind.PeakingFilter, "freq_max")] = "9000Hz",
        [(EffectKind.PeakingFilter, "bandwidth")] = "1.2",
        [(EffectKind.LowPassFilter, "freq")] = "15000Hz",
        [(EffectKind.LowPassFilter, "freq_max")] = "800Hz",
        [(EffectKind.LowPassFilter, "q")] = "3.6",
        [(EffectKind.HighPassFilter, "freq")] = "100Hz",
        [(EffectKind.HighPassFilter, "freq_max")] = "2200Hz",
        [(EffectKind.HighPassFilter, "q")] = "5.0",
    };

    private static ParamKind kindOf(string k) => k switch
    {
        "length" => ParamKind.Length,
        "sample" => ParamKind.Sample,
        "switch" => ParamKind.Switch,
        "rate" => ParamKind.Rate,
        "freq" => ParamKind.Freq,
        "pitch" => ParamKind.Pitch,
        "int" => ParamKind.Int,
        "float" => ParamKind.Float,
        "dB" => ParamKind.Db,
        _ => ParamKind.Filename,
    };

    private static Dictionary<EffectKind, EffectParamSpec[]> build()
    {
        var result = new Dictionary<EffectKind, EffectParamSpec[]>();
        foreach (var t in EffectCatalog.Types)
        {
            var kind = KindOf(t.Type);
            var list = new List<EffectParamSpec>();
            foreach (var p in t.Params)
            {
                var pk = kindOf(p.Kind);
                if (pk == ParamKind.Filename || IdOf(p.Name) is not { } id) continue;
                string text = p.Default.Length > 0 ? p.Default : engineDefaults.GetValueOrDefault((kind, p.Name), "");
                if (!EffectValueSet.TryParse(text, pk, out var set))
                    set = EffectValueSet.Constant(EffectValue.Zero);
                list.Add(new EffectParamSpec(id, p.Name, pk, set));
            }
            result[kind] = list.ToArray();
        }
        return result;
    }

    /// <summary>The built-in effect names (presets) of each bus, K-Shoot Mania v2's registration list.</summary>
    public static bool IsPreset(string name, bool laser) => laser
        ? name is "peaking_filter" or "high_pass_filter" or "low_pass_filter" or "bitcrusher"
        : name is "retrigger" or "gate" or "flanger" or "pitch_shift" or "bitcrusher" or "phaser" or "wobble" or "tapestop" or "echo" or "sidechain";
}

/// <summary>Resolved numbers of one active effect slot (lengths in seconds, rates 0..1, Hz, samples at 44.1 kHz, dB, 0/1).</summary>
public sealed class EffectParams
{
    public readonly double[] Values = new double[EffectSpecs.ParamCount];

    public double this[ParamId id]
    {
        get => Values[(int)id];
        set => Values[(int)id] = value;
    }

    public void CopyFrom(EffectParams other) => Array.Copy(other.Values, Values, Values.Length);
}
