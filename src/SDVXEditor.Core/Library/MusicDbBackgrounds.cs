using System.Text;
using System.Text.RegularExpressions;

namespace SDVXEditor.Core.Library;

/// <summary>
/// Which game background (music_db <c>&lt;bg_no&gt;</c>) the official songs use, read from the user's own
/// <c>data/others/music_db.xml</c> (Shift_JIS / cp932) — so the camera tab can say "game background 63, first used by
/// song …" for a chart's bg_no. Only ids, titles and numbers are read; nothing is copied or shipped.
/// </summary>
public sealed partial class MusicDbBackgrounds
{
    /// <summary>A background number: the lowest song id that uses it, that song's title, how many songs use it.</summary>
    public readonly record struct Use(int SongId, string Title, int Count);

    private readonly Dictionary<int, Use> byBgNo;

    public MusicDbBackgrounds(Dictionary<int, Use> byBgNo) => this.byBgNo = byBgNo;

    public IReadOnlyDictionary<int, Use> ByBgNo => byBgNo;

    public Use? Find(int bgNo) => byBgNo.TryGetValue(bgNo, out var u) ? u : null;

    [GeneratedRegex(@"<music id=""(\d+)"">(.*?)</music>", RegexOptions.Singleline)]
    private static partial Regex musicRx();

    [GeneratedRegex(@"<title_name>(.*?)</title_name>", RegexOptions.Singleline)]
    private static partial Regex titleRx();

    [GeneratedRegex(@"<bg_no[^>]*>(\d+)</bg_no>")]
    private static partial Regex bgRx();

    /// <summary>Parses the text of a music_db.xml (several difficulties of one song share its bg_no).</summary>
    public static MusicDbBackgrounds Parse(string xml)
    {
        var map = new Dictionary<int, Use>();
        foreach (Match m in musicRx().Matches(xml))
        {
            int id = int.Parse(m.Groups[1].Value);
            string body = m.Groups[2].Value;
            if (bgRx().Match(body) is not { Success: true } bg) continue;
            int no = int.Parse(bg.Groups[1].Value);
            string title = titleRx().Match(body) is { Success: true } t ? System.Net.WebUtility.HtmlDecode(t.Groups[1].Value.Trim()) : $"#{id}";
            map[no] = map.TryGetValue(no, out var u)
                ? u with { Count = u.Count + 1, SongId = Math.Min(u.SongId, id), Title = id < u.SongId ? title : u.Title }
                : new Use(id, title, 1);
        }

        return new MusicDbBackgrounds(map);
    }

    private static readonly object gate = new();
    private static (string Path, long Stamp, MusicDbBackgrounds? Value)? cached;

    /// <summary>The backgrounds of a music_db.xml file (cached per path and file time); null when it is missing or unreadable.</summary>
    public static MusicDbBackgrounds? Load(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            long stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;
            lock (gate)
            {
                if (cached is { } c && c.Path == info.FullName && c.Stamp == stamp) return c.Value;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var value = Parse(Encoding.GetEncoding(932).GetString(File.ReadAllBytes(info.FullName)));
            lock (gate) cached = (info.FullName, stamp, value);
            return value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// The description the background view shows for a bg_no: "game background 63, first used by song 1234 'Title'
    /// (17 songs)", or what is known without a music_db.
    /// </summary>
    public static string Describe(int? bgNo, MusicDbBackgrounds? db)
    {
        if (bgNo is not int n) return "bg_no not set: the converter writes 0 (the default background)";
        if (db == null) return $"game background {n} (no music_db.xml of the game found to name a song that uses it)";
        if (db.Find(n) is not { } u) return $"game background {n}: no official song uses it in this game version - it may not exist";
        return $"game background {n}, first used by song {u.SongId} \"{u.Title}\"" + (u.Count > 1 ? $" ({u.Count} songs use it)" : "");
    }
}
