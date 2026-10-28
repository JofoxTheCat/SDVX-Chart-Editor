namespace SDVXEditor.Game;

/// <summary>
/// Command line options handed from the desktop entry point to the game.
/// </summary>
/// <param name="LauncherConfigPath">--launcher-config: path to sdvx_rpc_config.json.</param>
/// <param name="SongsDirectory">--songs: extra song library root.</param>
/// <param name="SmokeTest">--smoke-test: open the window, walk song select and every screen, tab and dialog, exit (0 = passed, 1 = failed; with SDVXEDITOR_SCREENSHOT_DIR set it also saves screenshots). With --open it also edits, saves and reloads copies of that song (about a minute).</param>
/// <param name="OpenSongFolder">--open: a song folder to open in the editor right away.</param>
/// <param name="OpenChartSlot">--chart: difficulty slot 0-4 of the song to open (null = the highest).</param>
/// <param name="FirstRun">No framework settings existed yet: the window starts borderless fullscreen, like osu!.</param>
public sealed record StartupOptions(
    string? LauncherConfigPath = null,
    string? SongsDirectory = null,
    bool SmokeTest = false,
    string? OpenSongFolder = null,
    int? OpenChartSlot = null,
    bool FirstRun = false)
{
    public static readonly StartupOptions Default = new();

    /// <summary>--import: a .svz / .zip / song folder / chart / audio file imported into the editor songs on start (song select asks Replace / Keep both on a name clash).</summary>
    public string? ImportPath { get; init; }
}
