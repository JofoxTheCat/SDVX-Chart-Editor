namespace SDVXEditor.Core.Library;

/// <summary>
/// The song counter in song select's footer (UI audit B4: it showed the scan's last count, stale after a drop import
/// and blind to the search). Built from the listed charts on every refresh.
/// </summary>
public static class LibraryCountText
{
    /// <param name="visibleSongs">Songs the wheel lists (after search / filter).</param>
    /// <param name="totalSongs">Songs in the library.</param>
    public static string Format(int visibleSongs, int visibleCharts, int totalSongs, int totalCharts)
    {
        static string songs(int n) => n == 1 ? "1 song" : $"{n} songs";
        static string charts(int n) => n == 1 ? "1 chart" : $"{n} charts";

        if (visibleSongs == totalSongs && visibleCharts == totalCharts)
            return $"{songs(totalSongs)} · {charts(totalCharts)}";

        return $"{visibleSongs} of {songs(totalSongs)} · {charts(visibleCharts)} shown";
    }
}
