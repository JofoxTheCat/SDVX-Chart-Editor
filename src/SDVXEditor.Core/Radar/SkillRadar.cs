namespace SDVXEditor.Core.Radar;

/// <summary>
/// The six axes of the SDVX skill radar, in the game's clockwise order starting at the top
/// (NOTES top, PEAK upper right, TSUMAMI lower right, TRICKY bottom, HAND-TRIP lower left,
/// ONE-HAND upper left — the layout of the game's own radar artwork).
/// </summary>
public enum RadarAxis
{
    Notes,
    Peak,
    Tsumami,
    Tricky,
    HandTrip,
    OneHand,
}

/// <summary>
/// Skill-radar values of one chart, on the music_db scale (u16, the formulas clamp to 10..1000; an
/// axis without any contribution is 0 like in real music_db entries, converter CLAUDE.md §5/§10).
/// </summary>
public readonly record struct SkillRadar(int Notes, int Peak, int Tsumami, int Tricky, int HandTrip, int OneHand)
{
    public const int AxisCount = 6;

    /// <summary>Upper bound of the clamp in radar.ts / radar.py.</summary>
    public const int Max = 1000;

    /// <summary>Lower bound of the clamp (the hexagon's floor); 0 still means "no contribution".</summary>
    public const int Floor = 10;

    public static IReadOnlyList<RadarAxis> Axes { get; } = Enum.GetValues<RadarAxis>();

    public int this[RadarAxis axis] => axis switch
    {
        RadarAxis.Notes => Notes,
        RadarAxis.Peak => Peak,
        RadarAxis.Tsumami => Tsumami,
        RadarAxis.Tricky => Tricky,
        RadarAxis.HandTrip => HandTrip,
        _ => OneHand,
    };

    /// <summary>Label as the game prints it.</summary>
    public static string Label(RadarAxis axis) => axis switch
    {
        RadarAxis.Notes => "NOTES",
        RadarAxis.Peak => "PEAK",
        RadarAxis.Tsumami => "TSUMAMI",
        RadarAxis.Tricky => "TRICKY",
        RadarAxis.HandTrip => "HAND-TRIP",
        _ => "ONE-HAND",
    };

    /// <summary>music_db / radar.py key (<c>hand_trip</c>, <c>one_hand</c>).</summary>
    public static string Key(RadarAxis axis) => axis switch
    {
        RadarAxis.Notes => "notes",
        RadarAxis.Peak => "peak",
        RadarAxis.Tsumami => "tsumami",
        RadarAxis.Tricky => "tricky",
        RadarAxis.HandTrip => "hand_trip",
        _ => "one_hand",
    };

    public override string ToString() => $"notes {Notes}, peak {Peak}, tsumami {Tsumami}, tricky {Tricky}, hand-trip {HandTrip}, one-hand {OneHand}";
}
