using SDVXEditor.Core.Library;
using CoreSource = SDVXEditor.Core.Library.SongSource;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>One chart of a real library song as a wheel item.</summary>
public sealed class LibraryChartItem : ISongListItem
{
    public LibrarySong Song { get; }
    public LibraryChart Chart { get; }

    private readonly Lazy<DateTime> created;

    public LibraryChartItem(LibrarySong song, LibraryChart chart, string rootName)
    {
        Song = song;
        Chart = chart;
        RootName = rootName;
        Slot = DifficultySlotExtensions.FromCore(chart.Slot, chart.DifficultyName);
        Hue = (uint)StringComparer.OrdinalIgnoreCase.GetHashCode(song.FolderPath) % 1000 / 1000f;
        created = new Lazy<DateTime>(() =>
        {
            try
            {
                return Directory.GetCreationTime(song.FolderPath);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return DateTime.MinValue;
            }
        });
    }

    /// <summary>All charts of all songs, one item per chart, in library order.</summary>
    public static List<ISongListItem> FromLibrary(IEnumerable<LibrarySong> songs, IReadOnlyList<LibraryRoot> roots)
    {
        var list = new List<ISongListItem>();

        foreach (var song in songs)
        {
            string root = rootName(song, roots);
            foreach (var chart in song.Charts)
                list.Add(new LibraryChartItem(song, chart, root));
        }

        return list;
    }

    private static string rootName(LibrarySong song, IReadOnlyList<LibraryRoot> roots)
    {
        string? parent = Path.GetDirectoryName(song.FolderPath.TrimEnd('\\', '/'));
        var root = roots.FirstOrDefault(r => string.Equals(r.Path.TrimEnd('\\', '/'), parent?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
        string name = root != null ? Path.GetFileName(root.Path.TrimEnd('\\', '/')) : parent ?? "";
        return song.Source == CoreSource.Launcher ? $"{name} (launcher)" : name;
    }

    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public string Charter => Chart.ChartAuthor.Trim().Length > 0 ? Chart.ChartAuthor.Trim() : Song.Charter;
    public string Illustrator => Song.Illustrator;
    public string? JacketPath => Chart.JacketPath ?? Song.JacketPath;
    public DifficultySlot Slot { get; }
    public int Level => (int)Math.Floor(Chart.Level);
    public double DeclaredLevel => Chart.Level;

    /// <summary>Computed lazily by the library manager; the header asks for it on selection.</summary>
    public double? EstimatedLevel { get; set; }

    public double BpmMin => Song.BpmMin;
    public double BpmMax => Song.BpmMax;
    public double LengthMs => Song.LengthMs;
    public int Chips => Math.Max(0, Chart.NoteCount - Chart.HoldCount);
    public int Holds => Chart.HoldCount;
    public int Lasers => Chart.LaserSections;
    public string Folder => Song.FolderName;
    public SongSource Source => Song.Source == CoreSource.Launcher ? SongSource.Launcher : SongSource.Editor;
    public DateTime Created => created.Value;
    public DateTime Updated => Song.ModifiedUtc == default ? DateTime.MinValue : Song.ModifiedUtc.ToLocalTime();
    public string Tags => string.Join(" ", new[] { Chart.DifficultyName, Song.FolderName, Chart.FileName }.Where(s => s.Length > 0));
    public string ChartFile => Chart.FileName;
    public float Hue { get; }

    public string SongKey => Song.FolderPath;
    public string ChartKey => Song.FolderPath + "|" + Chart.FileName;
    public string? FolderPath => Song.FolderPath;
    public string RootName { get; }
    public string? BackgroundPath => Song.BackgroundPath;
    public string? AudioPath => Song.AudioPath;
    public double PreviewOffsetMs => Song.PreviewOffsetMs;
    public double PreviewDurationMs => Song.PreviewDurationMs;
    public bool IsReadOnly => Song.IsReadOnly || Song.Source == CoreSource.Launcher;
    public IReadOnlyList<string> SongSources => Song.SongSources;

    public override string ToString() => $"{Title} [{Slot.ShortName()} {SDVXEditor.Core.Charts.Difficulty.FormatLevel(DeclaredLevel)}]";
}
