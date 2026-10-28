using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Library;

public enum SongSource
{
    /// <summary>The editor's own songs folder — writable.</summary>
    Editor,

    /// <summary>The launcher's <c>nautica_charts_path</c> — read-only by default; edits fork the song.</summary>
    Launcher,
}

/// <summary>One library root folder; every sub-folder holding a .ksh/.kson is a song.</summary>
public sealed record LibraryRoot(string Path, SongSource Source, bool ReadOnly);

/// <summary>Per-difficulty summary for the song wheel (no full chart kept in memory).</summary>
public sealed record LibraryChart
{
    public string FileName { get; init; } = "";
    public ChartFormat Format { get; init; }
    public int Slot { get; init; }
    public string DifficultyName { get; init; } = "";
    public double Level { get; init; }
    public string ChartAuthor { get; init; } = "";

    /// <summary>BT + FX notes, chips and holds each counted once.</summary>
    public int NoteCount { get; init; }

    public int BtCount { get; init; }
    public int FxCount { get; init; }
    public int HoldCount { get; init; }
    public int LaserSections { get; init; }
    public int Slams { get; init; }
    public string? JacketPath { get; init; }
}

/// <summary>Metadata of one song folder, cheap to keep for a whole library and cacheable as JSON.</summary>
public sealed record LibrarySong
{
    public string FolderPath { get; init; } = "";
    public string FolderName => Path.GetFileName(FolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public SongSource Source { get; init; }

    /// <summary>True for launcher songs (by default): saving must go through <see cref="SongLibrary.EnsureWritable"/>.</summary>
    public bool IsReadOnly { get; init; }

    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";

    /// <summary>Distinct chart authors of all difficulties, joined with " / ".</summary>
    public string Charter { get; init; } = "";

    public string Illustrator { get; init; } = "";
    public double BpmMin { get; init; }
    public double BpmMax { get; init; }

    /// <summary>Display BPM as the chart states it (<c>disp_bpm</c>), else derived from the tempo map.</summary>
    public string BpmDisplay { get; init; } = "";

    /// <summary>Chart length in ms (end of the longest chart, offset included).</summary>
    public double LengthMs { get; init; }

    public string? JacketPath { get; init; }

    /// <summary>
    /// Background image of the song: the first <c>bg</c> entry (kson <c>bg.filename</c>, ksh <c>bg=a;b</c>) that names
    /// an image file in the folder. Null when the charts use a built-in preset (<c>desert</c>, <c>ocean</c> …) or none.
    /// </summary>
    public string? BackgroundPath { get; init; }

    /// <summary>Newest write time (UTC) of the song's chart files — "last edited".</summary>
    public DateTime ModifiedUtc { get; init; }

    public string? AudioPath { get; init; }
    public long PreviewOffsetMs { get; init; }
    public long PreviewDurationMs { get; init; }
    public IReadOnlyList<LibraryChart> Charts { get; init; } = Array.Empty<LibraryChart>();

    /// <summary>The song's sources as stored (<see cref="SongFolder.SongSources"/>); <see cref="MusicLinks.Openable"/> filters them.</summary>
    public IReadOnlyList<string> SongSources { get; init; } = Array.Empty<string>();

    /// <summary>Chart files that failed to load, "file: reason".</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>ksh2vox-compatible fingerprint of the folder's inputs (names + sizes + mtimes).</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>For a fork: the launcher folder it was copied from.</summary>
    public string? ForkedFrom { get; init; }

    public LibraryChart? this[int slot] => Charts.FirstOrDefault(c => c.Slot == slot);

    /// <summary>Builds the summary from a loaded folder.</summary>
    public static LibrarySong FromFolder(SongFolder folder, string fingerprint, SongSource source, bool readOnly, string? forkedFrom = null)
    {
        var charts = new List<LibraryChart>();
        double bpmMin = double.MaxValue, bpmMax = 0, length = 0;
        string dispBpm = "";
        foreach (var sc in folder.Charts)
        {
            var c = sc.Chart;
            int bt = c.Notes.Bt.Sum(l => l.Count), fx = c.Notes.Fx.Sum(l => l.Count);
            int holds = c.Notes.Bt.Concat(c.Notes.Fx).Sum(l => l.Count(n => n.IsHold));
            charts.Add(new LibraryChart
            {
                FileName = Path.GetFileName(sc.FilePath),
                Format = sc.Format,
                Slot = sc.Slot,
                DifficultyName = c.Meta.DifficultyDisplayName,
                Level = c.Meta.Level,
                ChartAuthor = c.Meta.ChartAuthor,
                BtCount = bt,
                FxCount = fx,
                NoteCount = bt + fx,
                HoldCount = holds,
                LaserSections = c.Notes.Lasers.Sum(l => l.Count),
                Slams = c.Notes.Lasers.Sum(l => l.Sum(s => s.SlamCount)),
                JacketPath = sc.JacketPath,
            });
            foreach (var b in c.Beat.Bpm)
            {
                bpmMin = Math.Min(bpmMin, b.Bpm);
                bpmMax = Math.Max(bpmMax, b.Bpm);
            }
            if (dispBpm.Length == 0) dispBpm = c.Meta.DispBpm;
            try
            {
                var tm = TimingMap.FromChart(c);
                length = Math.Max(length, tm.PulseToMs(c.EndPulse ?? c.LastEventPulse()));
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                // a broken tempo map must not hide the song; length stays unknown
            }
        }
        if (bpmMin == double.MaxValue) bpmMin = 0;
        if (dispBpm.Length == 0 && bpmMax > 0)
            dispBpm = bpmMin == bpmMax ? fmt(bpmMax) : $"{fmt(bpmMin)}-{fmt(bpmMax)}";

        var first = folder.Charts.FirstOrDefault();
        // Jacket: the first chart that names one it really has; a fallback image only if nobody does.
        string? jacket = folder.Charts.FirstOrDefault(c => c.JacketPath != null && !c.JacketIsFallback)?.JacketPath
                         ?? folder.Charts.FirstOrDefault(c => c.JacketPath != null)?.JacketPath;
        string meta(Func<ChartMeta, string> f) => folder.Charts.Select(c => f(c.Chart.Meta)).FirstOrDefault(s => s.Length > 0) ?? "";
        DateTime modified = DateTime.MinValue;
        foreach (var sc in folder.Charts)
        {
            try { modified = Max(modified, File.GetLastWriteTimeUtc(sc.FilePath)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return new LibrarySong
        {
            FolderPath = Path.GetFullPath(folder.FolderPath),
            Source = source,
            IsReadOnly = readOnly,
            Title = folder.Title,
            Artist = meta(m => m.Artist),
            Charter = string.Join(" / ", folder.Charts.Select(c => c.Chart.Meta.ChartAuthor.Trim()).Where(s => s.Length > 0).Distinct()),
            Illustrator = meta(m => m.JacketAuthor),
            BpmMin = bpmMin,
            BpmMax = bpmMax,
            BpmDisplay = dispBpm,
            LengthMs = Math.Round(length),
            JacketPath = jacket,
            BackgroundPath = folder.Charts.Select(c => ResolveBackground(folder.FolderPath, c.Chart.Bg)).FirstOrDefault(p => p != null),
            ModifiedUtc = modified,
            AudioPath = folder.Charts.Select(c => c.AudioPath).FirstOrDefault(a => a != null),
            PreviewOffsetMs = first?.Chart.Audio.Bgm.PreviewOffsetMs ?? 0,
            PreviewDurationMs = first?.Chart.Audio.Bgm.PreviewDurationMs ?? 0,
            Charts = charts,
            SongSources = folder.SongSources.ToArray(),
            Errors = folder.Errors.Select(e => $"{Path.GetFileName(e.File)}: {e.Error}").ToList(),
            Fingerprint = fingerprint,
            ForkedFrom = forkedFrom,
        };
    }

    /// <summary>The first background entry of <paramref name="bg"/> that is an existing image in <paramref name="folder"/>, else null.</summary>
    public static string? ResolveBackground(string folder, BgInfo bg)
    {
        foreach (string raw in new[] { bg.Filename }.Concat(bg.LegacyBg))
        {
            string name = Path.GetFileName((raw ?? "").Trim().Trim('"').Replace('\\', '/'));
            if (name.Length == 0 || !SongFolder.ImageExtensions.Contains(Path.GetExtension(name).ToLowerInvariant())) continue;
            string p = Path.Combine(folder, name);
            if (File.Exists(p)) return Path.GetFullPath(p);
            try
            {
                var hit = Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return Path.GetFullPath(hit);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static string fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
