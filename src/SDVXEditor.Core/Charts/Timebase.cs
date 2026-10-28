namespace SDVXEditor.Core.Charts;

/// <summary>
/// The chart time base. Every time value in the model is an integer pulse count
/// (<see cref="long"/>), 240 pulses per quarter note, exactly like kson.
/// ksh lines and VOX ticks (48 per quarter) are derived from it on export.
/// </summary>
public static class Timebase
{
    public const long PulsesPerQuarter = 240;

    /// <summary>Length of a 4/4 measure (960). ksh `stop=`/spin lengths use 192 units per such measure.</summary>
    public const long PulsesPerWholeNote = 4 * PulsesPerQuarter;

    /// <summary>One ksh "192 per measure" unit in pulses (5). VOX ticks use the same factor.</summary>
    public const long PulsesPerKsh192Unit = PulsesPerWholeNote / 192;

    /// <summary>1/32 note (30 pulses). ksh writes a slam as two lines at most this far apart.</summary>
    public const long SlamMaxPulses = PulsesPerWholeNote / 32;

    /// <summary>Measure length for a time signature n/d. Rounded when 960*n/d is not integral (d &gt; 64).</summary>
    public static long MeasureLength(int numerator, int denominator)
        => (long)Math.Round((double)PulsesPerWholeNote * numerator / denominator);

    /// <summary>Length of one beat of a time signature: the denominator note (a 6/8 beat is an eighth).</summary>
    public static long BeatLength(int denominator)
        => (long)Math.Round((double)PulsesPerWholeNote / denominator);
}
