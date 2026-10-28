using System.Globalization;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// The one way the UI writes numbers, times and dates (UI audit U1/U2). Decision 2026-10-28:
/// <list type="bullet">
/// <item>Numbers: invariant culture — decimal <b>dot</b>, whatever the Windows locale says (the UI is English; a German
/// system used to leak "327,42" into some widgets next to "327.42bpm" in others). The game process runs with the
/// "en" locale for the same reason (<c>EditorGame</c>), so plain interpolations agree. Input still accepts a comma.</item>
/// <item>Large integers: thin groups with a space ("10 000 000"), like the score.</item>
/// <item>Positions in a song: <c>mm:ss:mmm</c> (osu!), <see cref="ChartTimestamp.FormatTime"/>.</item>
/// <item>Durations (song length): <c>m:ss</c>.</item>
/// <item>Dates: <c>yyyy-MM-dd HH:mm</c> (unambiguous in every locale).</item>
/// </list>
/// </summary>
public static class UiFormat
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Fixed decimals with a dot: 327.42.</summary>
    public static string Number(double value, int decimals) => value.ToString("F" + decimals, Culture);

    /// <summary>Up to <paramref name="maxDecimals"/> decimals, trailing zeros dropped: 1.5, 120.</summary>
    public static string Trim(double value, int maxDecimals = 2)
        => value.ToString("0." + new string('#', Math.Max(0, maxDecimals)), Culture);

    /// <summary>An integer with space-separated thousands: 10 000 000.</summary>
    public static string Grouped(long value)
    {
        var nf = (NumberFormatInfo)Culture.NumberFormat.Clone();
        nf.NumberGroupSeparator = " ";
        return value.ToString("#,0", nf);
    }

    /// <summary>A position in the song: mm:ss:mmm.</summary>
    public static string Time(double ms) => ChartTimestamp.FormatTime(ms);

    /// <summary>A length: m:ss (minutes may exceed 59); "--:--" when unknown.</summary>
    public static string Duration(double ms)
    {
        if (!(ms > 0)) return "--:--";
        long s = (long)Math.Round(ms / 1000);
        return string.Create(Culture, $"{s / 60}:{s % 60:00}");
    }

    /// <summary>A local date and time: 2026-10-27 23:28.</summary>
    public static string DateTime(System.DateTime value) => value.ToString("yyyy-MM-dd HH:mm", Culture);

    /// <summary>
    /// A path cut in the middle so both ends stay readable: "C:\Users\…\Exports" (audit P6: the placeholder showed
    /// "…\Temp\cla"). Keeps the start and as much of the tail as fits.
    /// </summary>
    public static string MiddleEllipsis(string path, int maxChars)
    {
        if (path.Length <= maxChars || maxChars < 8) return path;

        int head = Math.Max(3, maxChars / 3);
        int tail = maxChars - head - 1;
        return path[..head] + "…" + path[^tail..];
    }

    /// <summary>A percentage with one decimal: 5.0%.</summary>
    public static string Percent(double fraction, int decimals = 1) => (fraction * 100).ToString("F" + decimals, Culture) + "%";
}
