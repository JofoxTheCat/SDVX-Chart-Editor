using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.IO.Stores;
using osu.Framework.Platform;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Library;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// The song being edited: its folder and charts, the active chart with its undo history,
/// tempo map, selection and clipboard, plus the song audio. One session per editor screen.
/// <para>
/// Saving writes the active chart as <c>.kson</c>. A chart read from <c>.ksh</c> is never
/// overwritten: it is saved as <c>&lt;stem&gt;.kson</c> beside it, which the song folder then
/// prefers over the ksh (<see cref="SongFolder.PickChartFiles"/>).
/// </para>
/// </summary>
public sealed class EditorSession : IDisposable
{
    /// <summary>Copied content, shared by every session (copy in one difficulty, paste in another).</summary>
    public static ClipboardData? Clipboard { get; set; }

    /// <summary>The song folder, or null for an empty scratch chart.</summary>
    public SongFolder? Song { get; private set; }

    public IReadOnlyList<SongChart> Charts => Song?.Charts ?? Array.Empty<SongChart>();

    /// <summary>The chart file being edited (null for a scratch chart).</summary>
    public SongChart? ActiveChart { get; private set; }

    /// <summary>Where the active chart lives on disk now (changes from .ksh to .kson after the first save).</summary>
    public string? ChartPath { get; private set; }

    public Chart Chart { get; private set; } = null!;
    public CommandStack Commands { get; private set; } = null!;
    public TimingMap Timing { get; private set; } = null!;
    public Selection Selection { get; } = new Selection();

    /// <summary>Increments on every change of the chart content (edit, undo, chart switch); views cache on it.</summary>
    public int Version { get; private set; }

    /// <summary>Raised after every chart change (edit, undo/redo, chart switch, revert).</summary>
    public event Action? ChartChanged;

    public bool IsDirty => Commands.IsDirty;

    public string? AudioPath => ActiveChart?.AudioPath ?? (Song != null ? SongFolder.ResolveAudio(Song.FolderPath, Chart.Audio.Bgm.Filename) : null);

    private ITrackStore? trackStore;
    private ResourceStore<byte[]>? audioResources;

    private EditorSession()
    {
    }

    /// <summary>An empty 120 BPM chart without a folder (editor opened without a song).</summary>
    public static EditorSession CreateEmpty()
    {
        var chart = new Chart();
        chart.Beat.Bpm.Add(new BpmChange(0, 120));
        chart.Meta.Title = "Untitled";
        var session = new EditorSession();
        session.setChart(null, chart, null);
        return session;
    }

    /// <summary>Loads a song folder and activates the chart of <paramref name="slot"/> (or the highest one).</summary>
    public static EditorSession Load(string folder, int? slot = null)
    {
        var song = SongFolder.Load(folder);
        if (song.Charts.Count == 0)
        {
            string why = song.Errors.Count > 0 ? string.Join("; ", song.Errors.Select(e => $"{Path.GetFileName(e.File)}: {e.Error}")) : "no .ksh/.kson in the folder";
            throw new InvalidOperationException($"cannot open {folder}: {why}");
        }

        var session = new EditorSession { Song = song };
        var chart = (slot is int s ? song[s] : null) ?? song.Charts[^1];
        session.setChart(chart, chart.Chart, chart.FilePath);
        return session;
    }

    /// <summary>
    /// A view-only session over another chart of <paramref name="song"/> ("open difficulty for reference"): its own
    /// tempo map and selection, no audio. The chart object is shared with the song, never saved from here.
    /// </summary>
    public static EditorSession CreateReference(SongFolder song, SongChart chart)
    {
        var session = new EditorSession { Song = song };
        session.setChart(chart, chart.Chart, chart.FilePath);
        return session;
    }

    /// <summary>Activates another chart of the song. Undo history starts fresh.</summary>
    public void SwitchChart(SongChart chart)
    {
        setChart(chart, chart.Chart, chart.FilePath);
    }

    private void setChart(SongChart? songChart, Chart chart, string? path)
    {
        if (Commands != null) Commands.Changed -= onCommandsChanged;

        ActiveChart = songChart;
        ChartPath = path;
        Chart = chart;

        // song sources are song-level: a difficulty without its own list shows (and saves) the song's
        if (chart.Editor.SongSources.Count == 0 && Song?.SongSources is { Count: > 0 } songSources)
            chart.Editor.SongSources = songSources.ToArray();
        savedSongSources = chart.Editor.SongSources;
        Commands = new CommandStack(chart);
        Commands.Changed += onCommandsChanged;
        Selection.Clear();
        rebuild();
    }

    private void onCommandsChanged()
    {
        resyncSelection();
        rebuild();
    }

    private void rebuild()
    {
        Timing = TimingMap.FromChart(Chart);
        Version++;
        ChartChanged?.Invoke();
    }

    /// <summary>Tells views that the chart changed outside the command stack (timing/meta edits that rebuild the tempo map).</summary>
    public void NotifyChanged() => rebuild();

    // ---- time --------------------------------------------------------------

    public double PulseToMs(double pulse) => Timing.PulseToMs(pulse);

    public double MsToPulse(double ms) => Timing.MsToPulse(ms);

    /// <summary>Audio time of the last chart event plus one measure.</summary>
    public double ChartEndMs
    {
        get
        {
            long last = Chart.EndPulse ?? Chart.LastEventPulse();
            return Timing.PulseToMs(last + Timebase.PulsesPerWholeNote);
        }
    }

    // ---- saving ------------------------------------------------------------

    /// <summary>The file Ctrl+S writes: the .kson itself, or &lt;stem&gt;.kson next to a .ksh.</summary>
    public string? SaveTarget
    {
        get
        {
            if (ChartPath == null) return null;
            if (ChartPath.EndsWith(".kson", StringComparison.OrdinalIgnoreCase)) return ChartPath;

            return Path.Combine(Path.GetDirectoryName(ChartPath)!, Path.GetFileNameWithoutExtension(ChartPath) + ".kson");
        }
    }

    /// <summary>Saves the active chart as kson (see <see cref="SaveTarget"/>). Returns the written path.</summary>
    public string Save()
    {
        EnsureWritableFolder();
        return SaveAs(SaveTarget ?? throw new InvalidOperationException("this chart has no song folder to save into"));
    }

    /// <summary>
    /// Forks a read-only (launcher) song if that has not happened yet and returns the folder that may be
    /// written. Call this before copying any file (jacket, background, key sound…) into the song folder —
    /// <see cref="Song"/>'s folder before the call may be the launcher's.
    /// </summary>
    public string EnsureWritableFolder()
    {
        if (ForkBeforeSave is { } fork)
        {
            moveToFolder(fork());
            ForkBeforeSave = null;
        }

        return Song?.FolderPath ?? throw new InvalidOperationException("no song is loaded");
    }

    /// <summary>
    /// Set for a read-only (launcher) song: called once before the first save and returns the folder of the
    /// writable copy (the library's fork-on-edit). The session then saves into that copy — the launcher's
    /// folder, and the import-map key that is its name, are never written.
    /// </summary>
    public Func<string>? ForkBeforeSave { get; set; }

    /// <summary>Audio position (ms in the file) the editor opens at — where the song select preview was playing.</summary>
    public double? StartTrackTimeMs { get; set; }

    /// <summary>True while saving would first create (or reuse) a copy of the song.</summary>
    public bool SavesAsCopy => ForkBeforeSave != null;

    /// <summary>Re-points the session at <paramref name="folder"/> (a copy of the song); the edited chart stays in memory.</summary>
    private void moveToFolder(string folder)
    {
        if (Song != null && string.Equals(Path.GetFullPath(Song.FolderPath), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
            return;

        string? file = ChartPath != null ? Path.GetFileName(ChartPath) : null;
        var song = SongFolder.Load(folder);
        Song = song;
        if (file != null)
        {
            ChartPath = Path.Combine(folder, file);
            ActiveChart = song.Charts.FirstOrDefault(c => string.Equals(Path.GetFileName(c.FilePath), file, StringComparison.OrdinalIgnoreCase)) ?? ActiveChart;
        }
    }

    /// <summary>The active chart's song sources as last loaded or saved (a save with another list updates the siblings).</summary>
    private IReadOnlyList<string> savedSongSources = Array.Empty<string>();

    /// <summary>Other difficulty files the last save rewrote because the song sources changed (smoke test / toast).</summary>
    public IReadOnlyList<string> LastSourceSyncFiles { get; private set; } = Array.Empty<string>();

    public string SaveAs(string path)
    {
        string tmp = path + ".tmp";
        KsonWriter.WriteFile(Chart, tmp);
        File.Move(tmp, path, true);
        ChartPath = path;
        syncSongSources(path);
        Commands.MarkSaved();
        Version++;
        ChartChanged?.Invoke();
        return path;
    }

    /// <summary>
    /// Song sources are song-level: when this save changed them, every other .kson difficulty of the song gets the same
    /// list (Core <see cref="SongSourceSync"/>; a .ksh is never rewritten). A failure there never fails the save itself.
    /// </summary>
    private void syncSongSources(string savedPath)
    {
        LastSourceSyncFiles = Array.Empty<string>();
        var now = Chart.Editor.SongSources;
        if (now.SequenceEqual(savedSongSources, StringComparer.Ordinal)) return;

        savedSongSources = now;
        if (Song == null) return;

        try
        {
            LastSourceSyncFiles = SongSourceSync.WriteToSiblings(Song, savedPath, now);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            osu.Framework.Logging.Logger.Log($"song sources: could not update the other difficulties: {e.Message}");
        }
    }

    /// <summary>
    /// File → Create a New Difficulty: writes <paramref name="chart"/> as a new chart file of the song (forking a
    /// launcher song first, <see cref="EnsureWritableFolder"/>), re-reads the folder and makes it the active chart.
    /// The current chart's unsaved edits are dropped — the caller asks first. Returns the new chart.
    /// </summary>
    public SongChart AddDifficulty(Chart chart, int slot)
    {
        string folder = EnsureWritableFolder();
        string path = Path.Combine(folder, NewDifficulty.FileName(slot, Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>()));

        string tmp = path + ".tmp";
        KsonWriter.WriteFile(chart, tmp);
        File.Move(tmp, path);

        Song = SongFolder.Load(folder);
        var added = Song.Charts.FirstOrDefault(c => string.Equals(Path.GetFullPath(c.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"the new chart {Path.GetFileName(path)} could not be read back");
        SwitchChart(added);
        return added;
    }

    /// <summary>
    /// Re-reads the song folder from disk (after a save, a discard or a new chart file) so the difficulty list and every
    /// other chart object are fresh; the active chart is matched by path. Call it only right before switching charts —
    /// the edited <see cref="Chart"/> object is not touched.
    /// </summary>
    public void ReloadSong()
    {
        if (Song == null) return;

        var song = SongFolder.Load(Song.FolderPath);
        Song = song;
        if (ChartPath != null)
            ActiveChart = song.Charts.FirstOrDefault(c => string.Equals(Path.GetFullPath(c.FilePath), Path.GetFullPath(ChartPath), StringComparison.OrdinalIgnoreCase)) ?? ActiveChart;
    }

    /// <summary>Re-reads the active chart from disk, dropping unsaved edits and history.</summary>
    public void RevertToSaved()
    {
        if (ChartPath == null) return;

        var chart = ChartPath.EndsWith(".kson", StringComparison.OrdinalIgnoreCase)
            ? KsonReader.ReadFile(ChartPath)
            : Core.Formats.Ksh.KshReader.ReadFile(ChartPath);
        chart.Meta.DifficultySlot = Chart.Meta.DifficultySlot;
        chart.Meta.DifficultyName = Chart.Meta.DifficultyName;
        setChart(ActiveChart, chart, ChartPath);
    }

    // ---- audio -------------------------------------------------------------

    /// <summary>
    /// Opens the song audio through the framework's track store, rooted at the song folder
    /// (the folder can be anywhere on disk). Null when the song has no audio.
    /// </summary>
    public Track? LoadTrack(AudioManager audio)
    {
        string? file = AudioPath;
        if (file == null || !File.Exists(file)) return null;

        if (trackStore == null)
        {
            audioResources = new ResourceStore<byte[]>(new StorageBackedResourceStore(new NativeStorage(Path.GetDirectoryName(file)!)));
            trackStore = audio.GetTrackStore(audioResources);
        }

        return trackStore.Get(Path.GetFileName(file));
    }

    /// <summary>Waveform of the song audio for the timeline, or null.</summary>
    public Waveform? LoadWaveform()
    {
        string? file = AudioPath;
        if (file == null || !File.Exists(file)) return null;

        return new Waveform(File.OpenRead(file));
    }

    public void Dispose()
    {
        trackStore?.Dispose();
        audioResources?.Dispose();
        trackStore = null;
        audioResources = null;
    }

    // ---- selection ---------------------------------------------------------

    /// <summary>
    /// After an edit, undo or redo: references in the selection may point at notes that moved lane
    /// (refs carry the lane) or objects that are gone. Re-point or drop them.
    /// </summary>
    private void resyncSelection()
    {
        if (Selection.IsEmpty) return;

        var notes = Selection.Notes.ToList();
        var points = Selection.LaserPoints.ToList();
        var ends = Selection.SlamEnds.ToList();
        Selection.Clear();

        foreach (var r in notes)
        {
            var lanes = r.Kind == NoteKind.Bt ? Chart.Notes.Bt : Chart.Notes.Fx;
            for (int l = 0; l < lanes.Length; l++)
            {
                if (lanes[l].Contains(r.Note))
                {
                    Selection.Add(r with { Lane = l });
                    break;
                }
            }
        }

        foreach (var p in points)
        {
            for (int l = 0; l < 2; l++)
            {
                if (Chart.Notes.Lasers[l].Contains(p.Section) && p.Section.Points.Contains(p.Point))
                {
                    Selection.Add(p with { Lane = l });
                    break;
                }
            }
        }

        // a slam end stays selected while its point is still a slam in the chart
        foreach (var e in ends)
        {
            for (int l = 0; l < 2; l++)
            {
                if (Chart.Notes.Lasers[l].Contains(e.Section) && e.Section.Points.Contains(e.Point) && e.Point.IsSlam)
                {
                    Selection.Add(e with { Lane = l });
                    break;
                }
            }
        }
    }

    /// <summary>Selects every point of the given sections (after a command replaced the selected ones).</summary>
    public void SelectSections(IEnumerable<(int Lane, LaserSection Section)> sections)
    {
        foreach (var (lane, s) in sections)
            foreach (var p in s.Points)
                Selection.Add(new LaserPointRef(lane, s, p));
    }
}
