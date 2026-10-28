namespace SDVXEditor.Core.Audio;

/// <summary>A waveform column: colour (0–1, full saturation scaled by brightness) and height (0–1 of the half height).</summary>
public readonly record struct SpectralColumn(float R, float G, float B, float Height)
{
    /// <summary>0 = red, 1 = green, 2 = blue: the strongest channel.</summary>
    public int Dominant => R >= G && R >= B ? 0 : G >= B ? 1 : 2;
}

/// <summary>
/// DJ-software style colouring of the timeline waveform (Serato / rekordbox RGB): low band → red, mid → green,
/// high → blue, so a kick or 808 reads red/magenta, vocals, leads and snare bodies green, hats, cymbals and snare
/// noise blue/cyan, mixtures blend. Height is the column's loudest 10 ms RMS against the song's loud reference (<see cref="SpectralAnalysis.LevelIn"/>).
/// <para>
/// The band levels are first moved halfway towards the song's own average balance (<see cref="SpectralAnalysis.Overall"/>,
/// each band against the mid band, ratio^<see cref="NormalisationExponent"/>, clamped to ±<see cref="MaxNormalisationDb"/> dB),
/// so a dark or bright master does not tint the whole song while a kick that owns the low band of a sparse mix still
/// reads red; the per-band gains then set the bias. Gains and edges were picked by measuring onsets in
/// the eight Nautica songs (CHANGELOG 0.1.0, "Spectral waveform").
/// </para>
/// </summary>
public static class SpectralPalette
{
    /// <summary>−1 dB: low-band dominance needs a real low-end event, so a steady bass line does not paint everything red.</summary>
    public const float LowGain = 0.891f;

    public const float MidGain = 1f;

    /// <summary>+1 dB: hats and cymbals sit 6–13 dB under the mids in the measured songs; a slight lift lets them read blue.</summary>
    public const float HighGain = 1.122f;

    /// <summary>
    /// How far the song balance is normalised: 0 = fixed gains only, 1 = fully relative to the song average. On the
    /// eight Nautica songs every exponent 0…1 scored within 5 % of each other; 1 turned the kicks of a drum-only loop
    /// (kick = all of the song's low end) yellow, 0.5 keeps them red.
    /// </summary>
    public const float NormalisationExponent = 0.5f;

    /// <summary>Song-balance normalisation is limited to this many dB per band (a song without highs must not amplify hiss).</summary>
    public const float MaxNormalisationDb = 12;

    /// <summary>Exponent on the max-normalised band weights: &gt; 1 pushes mixtures towards the dominant band (more saturated hues).</summary>
    public const float HueSharpness = 2f;

    /// <summary>
    /// Height = (level / reference)^gamma: above 1 expands the loud range so kicks and snares stand out as lobes with
    /// gaps between beats (−3 dB → 0.57). <see cref="QuietFloor"/> keeps quiet parts visible.
    /// </summary>
    public const float HeightGamma = 1.6f;

    /// <summary>Height floor on a dB scale: <c>QuietFloor × (1 + dB / QuietRangeDb)</c> (−20 dB → 0.13, −10 dB → 0.22).</summary>
    public const float QuietFloor = 0.3f;

    public const float QuietRangeDb = 36;

    /// <summary>Brightness of a silent column; loud columns reach 1.</summary>
    public const float MinBrightness = 0.4f;

    /// <summary>Colour and height for one column covering [fromMs, toMs).</summary>
    public static SpectralColumn Column(SpectralAnalysis a, double fromMs, double toMs)
    {
        float loud = Math.Clamp(a.LevelIn(fromMs, toMs) / a.ReferenceLevel, 0, 1);
        if (loud <= 0) return default;

        var (r, g, b) = Hue(a.BandsIn(fromMs, toMs), a.Overall);
        float brightness = MinBrightness + (1 - MinBrightness) * MathF.Sqrt(loud);
        return new SpectralColumn(r * brightness, g * brightness, b * brightness, Height(loud));
    }

    /// <summary>Column height (0–1) for a level relative to the song's loud reference.</summary>
    public static float Height(float relativeLevel)
    {
        if (relativeLevel <= 0) return 0;
        float x = Math.Min(1, relativeLevel);
        float db = 20 * MathF.Log10(x);
        return Math.Max(MathF.Pow(x, HeightGamma), QuietFloor * Math.Max(0, 1 + db / QuietRangeDb));
    }

    /// <summary>Full-saturation hue (max channel = 1) of band levels, normalised to the song balance <paramref name="overall"/>.</summary>
    public static (float R, float G, float B) Hue(BandLevels bands, BandLevels overall)
    {
        float l = bands.Low * LowGain * norm(overall.Mid, overall.Low);
        float m = bands.Mid * MidGain;
        float h = bands.High * HighGain * norm(overall.Mid, overall.High);

        float max = Math.Max(l, Math.Max(m, h));
        if (max <= 0) return (0, 0, 0);

        return (MathF.Pow(l / max, HueSharpness), MathF.Pow(m / max, HueSharpness), MathF.Pow(h / max, HueSharpness));
    }

    private static float norm(float mid, float band)
    {
        float limit = MathF.Pow(10, MaxNormalisationDb / 20);
        if (mid <= 0 || band <= 0) return 1;
        return Math.Clamp(MathF.Pow(mid / band, NormalisationExponent), 1 / limit, limit);
    }
}
