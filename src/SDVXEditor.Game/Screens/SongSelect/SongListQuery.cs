using System.Globalization;

namespace SDVXEditor.Game.Screens.SongSelect;

public enum SongGrouping
{
    NoGrouping,

    /// <summary>By the library folder a song lives in (editor songs, <c>--songs</c>, the launcher's Nautica folder).</summary>
    BySongFolder,

    /// <summary>Editor vs launcher.</summary>
    BySource,

    ByCharter,

    /// <summary>By declared level (whole levels).</summary>
    ByLevel,
}

public enum SongSort
{
    ByTitle,
    ByArtist,
    ByCharter,
    ByLevel,
    ByDateEdited,
}

/// <summary>The dark-red tab row: quick filters on top of group/sort/search.</summary>
public enum QuickFilter
{
    None,

    /// <summary>Stub until collections exist: shows an empty list with a hint.</summary>
    Collections,

    /// <summary>Charts edited in the last <see cref="SongListQuery.RecentDays"/> days, newest first.</summary>
    RecentlyEdited,

    /// <summary>Editor songs only (no launcher songs).</summary>
    MyCharts,
}

/// <summary>The charts of one song inside one group (osu!'s beatmap set).</summary>
public sealed record WheelSet(string Key, IReadOnlyList<ISongListItem> Charts)
{
    public ISongListItem First => Charts[0];
}

public sealed record WheelGroup(string Key, string Title, IReadOnlyList<WheelSet> Sets)
{
    public int ChartCount => Sets.Sum(s => s.Charts.Count);
}

/// <summary>Filter → group → set → sort, without any drawable: the wheel only lays out the result.</summary>
public static class SongListQuery
{
    public const int RecentDays = 30;

    public static IReadOnlyList<WheelGroup> Build(IEnumerable<ISongListItem> all, SongGrouping grouping, SongSort sort, string search, QuickFilter filter,
                                                  DateTime? now = null)
    {
        var items = all.Where(i => Matches(i, search));

        switch (filter)
        {
            case QuickFilter.Collections:
                return Array.Empty<WheelGroup>();

            case QuickFilter.MyCharts:
                items = items.Where(i => i.Source == SongSource.Editor);
                break;

            case QuickFilter.RecentlyEdited:
                var since = (now ?? DateTime.Now).AddDays(-RecentDays);
                items = items.Where(i => i.Updated >= since);
                sort = SongSort.ByDateEdited;
                break;
        }

        var groups = new List<WheelGroup>();

        foreach (var g in items.GroupBy(i => groupKey(i, grouping)))
        {
            // Level sort splits sets, like osu!'s difficulty sort: every chart stands on its own.
            var sets = g.GroupBy(i => sort == SongSort.ByLevel ? i.ChartKey : i.SongKey)
                        .Select(s => new WheelSet(s.Key, s.OrderBy(c => c.Slot).ThenBy(c => c.DeclaredLevel).ToList()))
                        .ToList();

            groups.Add(new WheelGroup(g.Key, groupTitle(g.First(), grouping), sortSets(sets, sort)));
        }

        return sortGroups(groups, grouping);
    }

    /// <summary>All whitespace-separated terms must occur in title, artist, charter, illustrator, tags or folder.</summary>
    public static bool Matches(ISongListItem i, string search)
    {
        foreach (string term in search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!(contains(i.Title, term) || contains(i.Artist, term) || contains(i.Charter, term) || contains(i.Illustrator, term)
                  || contains(i.Tags, term) || contains(i.Folder, term) || contains(i.Slot.ShortName(), term)))
                return false;
        }

        return true;
    }

    private static bool contains(string? s, string term) => s != null && CultureInfo.InvariantCulture.CompareInfo.IndexOf(s, term,
        CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth | CompareOptions.IgnoreNonSpace) >= 0;

    private static string groupKey(ISongListItem i, SongGrouping g) => g switch
    {
        SongGrouping.BySongFolder => i.RootName,
        SongGrouping.BySource => i.Source.ToString(),
        SongGrouping.ByCharter => i.Charter.Trim().ToLowerInvariant(),
        SongGrouping.ByLevel => ((int)Math.Floor(i.DeclaredLevel)).ToString("00", CultureInfo.InvariantCulture),
        _ => "",
    };

    private static string groupTitle(ISongListItem i, SongGrouping g) => g switch
    {
        SongGrouping.BySongFolder => i.RootName,
        SongGrouping.BySource => i.Source == SongSource.Launcher ? "Launcher library" : "Editor songs",
        SongGrouping.ByCharter => i.Charter.Trim().Length > 0 ? i.Charter.Trim() : "Unknown charter",
        SongGrouping.ByLevel => i.DeclaredLevel > 0 ? $"Level {(int)Math.Floor(i.DeclaredLevel)}" : "No level",
        _ => "",
    };

    private static IReadOnlyList<WheelGroup> sortGroups(List<WheelGroup> groups, SongGrouping g) => g switch
    {
        SongGrouping.BySource => groups.OrderBy(x => x.Key == nameof(SongSource.Launcher) ? 1 : 0).ToList(),
        SongGrouping.ByLevel => groups.OrderBy(x => x.Key, StringComparer.Ordinal).ToList(),
        _ => groups.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToList(),
    };

    private static IReadOnlyList<WheelSet> sortSets(List<WheelSet> sets, SongSort sort)
    {
        var text = StringComparer.CurrentCultureIgnoreCase;

        IOrderedEnumerable<WheelSet> ordered = sort switch
        {
            SongSort.ByArtist => sets.OrderBy(s => s.First.Artist, text).ThenBy(s => s.First.Title, text),
            SongSort.ByCharter => sets.OrderBy(s => s.First.Charter, text).ThenBy(s => s.First.Title, text),
            SongSort.ByLevel => sets.OrderBy(s => s.Charts.Max(c => c.DeclaredLevel)).ThenBy(s => s.First.Title, text),
            SongSort.ByDateEdited => sets.OrderByDescending(s => s.Charts.Max(c => c.Updated)).ThenBy(s => s.First.Title, text),
            _ => sets.OrderBy(s => s.First.Title, text).ThenBy(s => s.First.Artist, text),
        };

        return ordered.ThenBy(s => s.Key, StringComparer.Ordinal).ToList();
    }
}
