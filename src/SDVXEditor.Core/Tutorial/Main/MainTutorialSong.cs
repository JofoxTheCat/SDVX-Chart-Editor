using System.Text.Json;
using System.Text.Json.Serialization;

namespace SDVXEditor.Core.Tutorial;

/// <summary>
/// The main tutorial's song (<c>SDVXEditor.Resources/Tutorial/Main/tutorial.json</c> beside the audio): timing, names,
/// the song's sections and the measure windows of every exercise. Constant tempo, 4/4 throughout. The chart the
/// tutorial rebuilds is <c>solution.kson</c> in the same folder when it ships (the mapper's chart), otherwise the
/// placeholder of <see cref="MainTutorialCharts"/>; the exercises take their targets from that chart inside these
/// windows, so swapping the chart = dropping in the file and checking the windows (docs/TUTORIAL.md).
/// </summary>
public sealed record MainTutorialSong : ITutorialSong
{
    public string Title { get; init; } = "[REDACTED] (Cut Ver.)";
    public string Artist { get; init; } = "Fax";

    /// <summary>Who charted the chart the tutorial rebuilds (kson <c>meta.chart_author</c> of the prepared songs).</summary>
    public string Charter { get; init; } = "Tutorial";

    public string Audio { get; init; } = "tutorial.mp3";

    /// <summary>First downbeat, ms (kson <c>audio.bgm.offset</c>).</summary>
    public long OffsetMs { get; init; } = 682;

    public double Bpm { get; init; } = 235;

    /// <summary>Measures the chart uses (the audio rings on after them).</summary>
    public int Measures { get; init; } = 48;

    /// <summary>Song select preview start, ms.</summary>
    public long PreviewMs { get; init; } = 16001;

    /// <summary>Licence line shown in Song Setup's Information field.</summary>
    public string License { get; init; } = "";

    /// <summary>Where the song can be found (kson <c>impl["sdvx-editor"].song_sources</c>).</summary>
    public IReadOnlyList<string> SongSources { get; init; } = Array.Empty<string>();

    /// <summary>Named sections: bookmarks in every prepared chart (0-based measures).</summary>
    public IReadOnlyList<MainTutorialSection> Sections { get; init; } = Array.Empty<MainTutorialSection>();

    /// <summary>The drop (0-based measure): chapter 0's scroll speed jump, chapter 4's camera.</summary>
    public int Drop { get; init; } = 15;

    /// <summary>The scroll speed the notes jump to at the drop (chapter 0, F6).</summary>
    public double DropSpeed { get; init; } = 1.25;

    public MainTutorialExercises Exercises { get; init; } = new();

    private static readonly JsonSerializerOptions json_options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static MainTutorialSong Parse(string json)
    {
        var def = JsonSerializer.Deserialize<MainTutorialSong>(json, json_options) ?? new MainTutorialSong();
        def.Validate();
        return def;
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions(json_options) { WriteIndented = true });

    /// <summary>Refuses a definition the chapters cannot work with.</summary>
    public void Validate()
    {
        if (Bpm is < 30 or > 400) throw new FormatException("tutorial.json: BPM out of range");
        if (Measures < 24) throw new FormatException("tutorial.json: the chapters need at least 24 measures");
        if (Drop < 1 || Drop >= Measures) throw new FormatException("tutorial.json: drop outside the song");
        if (DropSpeed is <= 0 or > 10) throw new FormatException("tutorial.json: dropSpeed out of range");

        foreach (var (name, w) in Exercises.All())
        {
            if (w.From < 0 || w.To <= w.From || w.To > Measures)
                throw new FormatException($"tutorial.json: exercise window '{name}' ({w.From}..{w.To}) is empty or outside the {Measures} measures");
        }

        if (Exercises.Chips.To - Exercises.Chips.From != 1 || Exercises.Chips.From != Math.Floor(Exercises.Chips.From))
            throw new FormatException("tutorial.json: 'chips' must be one whole measure (the paste + mirror exercise copies it into the next one)");
    }

    // ---- time math (constant tempo, 4/4: a measure is 960 pulses) ---------------------------

    public const long Measure = TutorialSongDefinition.Measure;
    public const long Beat = TutorialSongDefinition.Beat;

    /// <summary>Pulse of a 0-based measure + beat.</summary>
    public static long At(int measure, double beat = 0) => TutorialSongDefinition.At(measure, beat);

    /// <summary>Pulse of a fractional measure (exercise windows: 6.5 = beat 3 of measure 7).</summary>
    public static long AtMeasure(double measure) => (long)Math.Round(measure * Measure);

    public double MsAt(long pulse) => OffsetMs + pulse * 60000.0 / (Bpm * Beat);

    public double LengthMs => MsAt(At(Measures));

    /// <summary>The ms of a measure's first beat (texts: "type 00:16:001").</summary>
    public double MsOfMeasure(int measure) => MsAt(At(measure));
}

public sealed record MainTutorialSection
{
    public string Name { get; init; } = "";
    public int Measure { get; init; }
    public string? Colour { get; init; }
}

/// <summary>A stretch of the song, in 0-based measures (fractions allowed), end exclusive.</summary>
public sealed record TutorialWindow
{
    public double From { get; init; }
    public double To { get; init; }

    public TutorialWindow() { }

    public TutorialWindow(double from, double to)
    {
        From = from;
        To = to;
    }

    public long FromPulse => MainTutorialSong.AtMeasure(From);
    public long ToPulse => MainTutorialSong.AtMeasure(To);
    public bool Contains(long pulse) => pulse >= FromPulse && pulse < ToPulse;

    /// <summary>"bar 6" / "bars 12–13" (1-based, as the editor's ruler shows them).</summary>
    public string BarsText
    {
        get
        {
            int first = (int)Math.Floor(From) + 1, last = (int)Math.Ceiling(To);
            return first >= last ? $"bar {first}" : $"bars {first}–{last}";
        }
    }

    public override string ToString() => $"{From}..{To}";
}

/// <summary>
/// The exercise windows of the main tutorial. A guided exercise takes its targets from the solution chart inside its
/// window (e.g. every BT chip of <see cref="Chips"/>); a free-mapping window is cleared and checked by rules.
/// </summary>
public sealed record MainTutorialExercises
{
    // chapter 1
    public TutorialWindow Chips { get; init; } = new(0, 1);
    public TutorialWindow Holds { get; init; } = new(2, 3);
    public TutorialWindow FxChips { get; init; } = new(3, 4);
    public TutorialWindow FxHold { get; init; } = new(4, 5);
    public TutorialWindow ChipPractice { get; init; } = new(5, 6);
    public TutorialWindow HoldModifier { get; init; } = new(6, 6.5);
    public TutorialWindow HoldResize { get; init; } = new(6.5, 7);

    // chapter 2
    public TutorialWindow Slam { get; init; } = new(7, 8);
    public TutorialWindow StraightLaser { get; init; } = new(8, 9);
    public TutorialWindow CurveLaser { get; init; } = new(9, 10);
    public TutorialWindow WideLaser { get; init; } = new(10, 11);
    public TutorialWindow LaserPractice { get; init; } = new(11, 13);

    // chapter 6
    public TutorialWindow FinalPractice { get; init; } = new(15, 19);

    /// <summary>The paste + mirror exercise: the measure after <see cref="Chips"/>.</summary>
    [JsonIgnore]
    public TutorialWindow Paste => new(Chips.From + 1, Chips.To + 1);

    public IEnumerable<(string Name, TutorialWindow Window)> All()
    {
        yield return ("chips", Chips);
        yield return ("paste", Paste);
        yield return ("holds", Holds);
        yield return ("fxChips", FxChips);
        yield return ("fxHold", FxHold);
        yield return ("chipPractice", ChipPractice);
        yield return ("holdModifier", HoldModifier);
        yield return ("holdResize", HoldResize);
        yield return ("slam", Slam);
        yield return ("straightLaser", StraightLaser);
        yield return ("curveLaser", CurveLaser);
        yield return ("wideLaser", WideLaser);
        yield return ("laserPractice", LaserPractice);
        yield return ("finalPractice", FinalPractice);
    }
}
