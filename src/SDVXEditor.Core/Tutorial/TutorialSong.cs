using System.Text.Json;
using System.Text.Json.Serialization;
using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Tutorial;

/// <summary>
/// The one timing definition of the tutorial song (<c>tutorial.json</c> next to the audio in
/// SDVXEditor.Resources/Tutorial). Swapping the placeholder for the final song = replace the audio file and edit
/// this file (docs/TUTORIAL.md); every chapter's chart, ghost and check is computed from it in measures/beats, so
/// nothing else hard-codes a time.
/// </summary>
public sealed record TutorialSongDefinition : ITutorialSong
{
    public string Title { get; init; } = "Tutorial Song";
    public string Artist { get; init; } = "SDVX Chart Editor";

    /// <summary>Audio file name inside the template (and in every prepared song folder).</summary>
    public string Audio { get; init; } = "tutorial.ogg";

    /// <summary>Where measure 1 starts in the audio, ms (kson <c>audio.bgm.offset</c>).</summary>
    public long OffsetMs { get; init; } = 300;

    /// <summary>Tempo from the start.</summary>
    public double Bpm { get; init; } = 150;

    /// <summary>The one tempo change: 0-based measure index and the new BPM.</summary>
    public TutorialTempoChange SpeedUp { get; init; } = new();

    /// <summary>The quieter part (0-based measures, end exclusive).</summary>
    public TutorialRange Breakdown { get; init; } = new() { From = 6, To = 8 };

    /// <summary>Number of 4/4 measures the charts use (the audio may ring on after them).</summary>
    public int Measures { get; init; } = 16;

    /// <summary>Song select preview start, ms.</summary>
    public long PreviewMs { get; init; } = 13100;

    /// <summary>Licence / source line shown in Song Setup's Information field.</summary>
    public string License { get; init; } = "";

    private static readonly JsonSerializerOptions json_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static TutorialSongDefinition Parse(string json)
    {
        var def = JsonSerializer.Deserialize<TutorialSongDefinition>(json, json_options) ?? new TutorialSongDefinition();
        def.Validate();
        return def;
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(json_options) { WriteIndented = true });

    /// <summary>Refuses a definition the chapters cannot work with (the exercises need 16 measures, a speed-up after the breakdown).</summary>
    public void Validate()
    {
        if (Bpm is < 30 or > 400 || SpeedUp.Bpm is < 30 or > 400) throw new FormatException("tutorial.json: BPM out of range");
        if (Measures < 16) throw new FormatException("tutorial.json: the chapters need at least 16 measures");
        if (Breakdown.From < 6 || Breakdown.To <= Breakdown.From || SpeedUp.Measure < Breakdown.To || SpeedUp.Measure + 8 > Measures)
            throw new FormatException("tutorial.json: expected breakdown from measure 6 and the speed-up after it with 8 measures left");
    }

    // ---- time math (4/4 throughout, measure = 960 pulses) ---------------------------------

    public const long Measure = Timebase.PulsesPerWholeNote;
    public const long Beat = Timebase.PulsesPerQuarter;

    /// <summary>Pulse of a 0-based measure + beat (+ fraction of a beat).</summary>
    public static long At(int measure, double beat = 0) => measure * Measure + (long)Math.Round(beat * Beat);

    /// <summary>Song time (ms, audio clock) of a pulse.</summary>
    public double MsAt(long pulse)
    {
        long change = At(SpeedUp.Measure);
        double ms = OffsetMs + Math.Min(pulse, change) * 60000.0 / (Bpm * Beat);
        if (pulse > change) ms += (pulse - change) * 60000.0 / (SpeedUp.Bpm * Beat);
        return ms;
    }

    public double LengthMs => MsAt(At(Measures));
}

public sealed record TutorialTempoChange
{
    public int Measure { get; init; } = 8;
    public double Bpm { get; init; } = 175;
}

public sealed record TutorialRange
{
    public int From { get; init; }
    public int To { get; init; }
}
