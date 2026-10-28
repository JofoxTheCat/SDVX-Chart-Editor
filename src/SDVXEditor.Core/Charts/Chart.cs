using System.Text.Json.Nodes;

namespace SDVXEditor.Core.Charts;

/// <summary>
/// One chart (one difficulty). A superset of kson 1.0: every kson object has a home here,
/// ksh-only data lives in <see cref="Compat"/>, editor-only data in <see cref="Editor"/>, and
/// unknown kson keys round-trip through <see cref="Extra"/>.
/// </summary>
public sealed class Chart
{
    public int FormatVersion { get; set; } = 1;
    public ChartMeta Meta { get; set; } = new();
    public BeatInfo Beat { get; set; } = new();

    /// <summary>Explicit chart end (ksh: the last bar line). Null = derived from the last event.</summary>
    public long? EndPulse { get; set; }

    /// <summary>kson <c>gauge.total</c> / ksh <c>total=</c>; 0 = automatic.</summary>
    public int GaugeTotal { get; set; }

    public NoteData Notes { get; set; } = new();
    public AudioInfo Audio { get; set; } = new();
    public CameraInfo Camera { get; set; } = new();
    public BgInfo Bg { get; set; } = new();

    /// <summary>VOX-only editor data (bg_no, post effects, SPCONTROLER rows), kson <c>impl["sdvx-editor"].vox</c>.</summary>
    public VoxExtrasInfo VoxExtras { get; set; } = new();
    public EditorInfo Editor { get; set; } = new();
    public CompatInfo Compat { get; set; } = new();

    /// <summary>
    /// Unknown kson keys, stored as a sparse tree mirroring the kson document (e.g.
    /// <c>{"meta": {"foo": 1}, "impl": {...}}</c>). Merged back verbatim by the kson writer.
    /// </summary>
    public JsonObject Extra { get; set; } = new();

    /// <summary>Laser paths in the file's <c>impl</c> data that no longer matched their section's points (dropped on read).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int StaleLaserPaths { get; set; }

    /// <summary>Camera anchor paths in the file's <c>impl</c> data that no longer matched their run's points (dropped on read).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int StaleCameraPaths { get; set; }

    /// <summary>Last pulse that carries an event (notes, laser points, timing, camera).</summary>
    public long LastEventPulse()
    {
        long last = 0;
        foreach (var lane in Notes.Bt.Concat(Notes.Fx))
            foreach (var n in lane)
                last = Math.Max(last, n.EndY);
        foreach (var lane in Notes.Lasers)
            foreach (var s in lane)
                last = Math.Max(last, s.EndY);
        foreach (var b in Beat.Bpm) last = Math.Max(last, b.Y);
        foreach (var s in Beat.Stops) last = Math.Max(last, s.Y + s.Length);
        foreach (var p in Beat.ScrollSpeed) last = Math.Max(last, p.Y);
        foreach (var g in Camera.Body.All)
            foreach (var p in g) last = Math.Max(last, p.Y);
        foreach (var t in Camera.Tilt) last = Math.Max(last, t.Y);
        return last;
    }
}

public sealed class ChartMeta
{
    public string Title { get; set; } = "";
    public string TitleTranslit { get; set; } = "";
    public string TitleImgFilename { get; set; } = "";
    public string Artist { get; set; } = "";
    public string ArtistTranslit { get; set; } = "";
    public string ArtistImgFilename { get; set; } = "";

    /// <summary>Effector credit (ksh <c>effect=</c>, VOX <c>effected_by</c>).</summary>
    public string ChartAuthor { get; set; } = "";

    /// <summary>0 NOV, 1 ADV, 2 EXH, 3 INF-class, 4 MXM. kson/ksh can only store 0..3.</summary>
    public int DifficultySlot { get; set; } = 3;

    /// <summary>Optional display name of the slot (e.g. "GRV" for an infinite variant). Null = default.</summary>
    public string? DifficultyName { get; set; }

    /// <summary>Level 1..20; decimals allowed (kson stores an integer when whole).</summary>
    public double Level { get; set; } = 1;

    public string DispBpm { get; set; } = "";
    public double? StdBpm { get; set; }
    public string JacketFilename { get; set; } = "";
    public string JacketAuthor { get; set; } = "";
    public string IconFilename { get; set; } = "";
    public string Information { get; set; } = "";

    public string DifficultyDisplayName => DifficultyName ?? Difficulty.ShortName(DifficultySlot);
}

public sealed class BpmChange
{
    public long Y { get; set; }
    public double Bpm { get; set; }
    public BpmChange() { }
    public BpmChange(long y, double bpm) { Y = y; Bpm = bpm; }
    public BpmChange Clone() => new(Y, Bpm);
}

/// <summary>Time signature starting at a (0-based) measure index.</summary>
public sealed class TimeSignature
{
    public int MeasureIndex { get; set; }
    public int Numerator { get; set; } = 4;
    public int Denominator { get; set; } = 4;
    public TimeSignature() { }
    public TimeSignature(int measureIndex, int numerator, int denominator)
    {
        MeasureIndex = measureIndex;
        Numerator = numerator;
        Denominator = denominator;
    }
    public TimeSignature Clone() => new(MeasureIndex, Numerator, Denominator);
}

/// <summary>Scroll stop: the note stream freezes for <see cref="Length"/> pulses. Audio time is unaffected.</summary>
public sealed class Stop
{
    public long Y { get; set; }
    public long Length { get; set; }
    public Stop() { }
    public Stop(long y, long length) { Y = y; Length = length; }
    public Stop Clone() => new(Y, Length);
}

public sealed class BeatInfo
{
    public List<BpmChange> Bpm { get; set; } = new();
    public List<TimeSignature> TimeSignatures { get; set; } = new();

    /// <summary>Scroll speed multiplier graph (osu! "SV"). Empty = constant 1.0. Never changes timing.</summary>
    public List<GraphPoint> ScrollSpeed { get; set; } = new();

    public List<Stop> Stops { get; set; } = new();
}

public sealed class EditorInfo
{
    public string AppName { get; set; } = "";
    public string AppVersion { get; set; } = "";

    /// <summary>kson <c>editor.comment</c> / ksh <c>//</c> lines.</summary>
    public List<PulseValue<string>> Comments { get; set; } = new();

    /// <summary>
    /// Bookmarks with optional label and colour, for collab coordination ("Part B – mapper2"). kson:
    /// <c>impl["sdvx-editor"].bookmarks</c>; ksh: <c>//#bookmark|colour|label</c> comment lines
    /// (<see cref="Bookmark.KshPrefix"/>); VOX cannot carry them.
    /// </summary>
    public BookmarkList Bookmarks { get; set; } = new();

    /// <summary>
    /// Song sources: links where the song can be listened to or bought, song-level (Song Setup writes the same list
    /// into every difficulty file it saves; readers take the first chart with a non-empty list, see
    /// <see cref="SongFolder.SongSources"/>). Stored verbatim — <c>Library.MusicLinks</c> decides what may be opened.
    /// kson: <c>impl["sdvx-editor"].song_sources</c> (array of strings); ksh: <c>//#source|&lt;url&gt;</c> comment
    /// lines (<see cref="SongSourceLine"/>). Replaced as a whole (never mutated), so a set-value command can undo it.
    /// </summary>
    public IReadOnlyList<string> SongSources { get; set; } = Array.Empty<string>();
}

/// <summary>ksh representation of a song source: a comment line <c>//#source|&lt;url&gt;</c>, which KSM and ksh2vox skip.</summary>
public static class SongSourceLine
{
    public const string KshPrefix = "//#source|";

    public static string ToKshLine(string url) => KshPrefix + url;

    /// <summary>The URL of a source line (verbatim after the prefix), or null when the line is not one.</summary>
    public static string? FromKshLine(string line) => line.StartsWith(KshPrefix, StringComparison.Ordinal) ? line[KshPrefix.Length..] : null;
}

/// <summary>
/// One bookmark's payload. <see cref="Label"/> may be empty (the UI then shows the time);
/// <see cref="Colour"/> is <c>#rrggbb</c> or null for the default bookmark blue.
/// </summary>
public sealed record Bookmark(string Label = "", string? Colour = null)
{
    private readonly string label = CleanLabel(Label);
    private readonly string? colour = NormaliseColour(Colour);

    /// <summary>One line, no control characters (also through <c>with</c>).</summary>
    public string Label { get => label; init => label = CleanLabel(value); }

    /// <summary><c>#rrggbb</c> lower case, or null (an invalid colour text becomes null).</summary>
    public string? Colour { get => colour; init => colour = NormaliseColour(value); }

    /// <summary>Default marker colour (bottom summary / timeline) when <see cref="Colour"/> is null.</summary>
    public const string DefaultColour = "#3d8bff";

    /// <summary>ksh representation: a comment line <c>//#bookmark|#rrggbb|label</c> (colour may be empty). KSM and ksh2vox skip <c>//</c> lines.</summary>
    public const string KshPrefix = "//#bookmark|";

    public static implicit operator Bookmark(string label) => new(label);

    /// <summary>Label without line breaks or control characters (ksh lines, one-line UI).</summary>
    public static string CleanLabel(string? label)
    {
        if (string.IsNullOrEmpty(label)) return "";
        var chars = label.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray();
        return new string(chars).Trim();
    }

    /// <summary><c>#rrggbb</c> (lower case) for a valid colour text (with or without <c>#</c>, also <c>#rgb</c>), otherwise null.</summary>
    public static string? NormaliseColour(string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour)) return null;
        string c = colour.Trim().TrimStart('#');
        if (c.Length == 3) c = string.Concat(c.Select(ch => $"{ch}{ch}"));
        if (c.Length != 6 || !c.All(Uri.IsHexDigit)) return null;
        return "#" + c.ToLowerInvariant();
    }

    /// <summary>The ksh comment line for a bookmark (without the position: options apply at the next chart line).</summary>
    public string ToKshLine() => KshPrefix + (NormaliseColour(Colour) ?? "") + "|" + CleanLabel(Label);

    /// <summary>Parses a ksh line written by <see cref="ToKshLine"/>; null when the line is not a bookmark.</summary>
    public static Bookmark? FromKshLine(string line)
    {
        if (!line.StartsWith(KshPrefix, StringComparison.Ordinal)) return null;
        string rest = line[KshPrefix.Length..];
        int bar = rest.IndexOf('|');
        if (bar < 0) return new Bookmark(CleanLabel(rest)); // "//#bookmark|label" (no colour field)
        return new Bookmark(CleanLabel(rest[(bar + 1)..]), NormaliseColour(rest[..bar]));
    }
}

/// <summary>
/// The chart's bookmarks, sorted by pulse. A plain list of <see cref="PulseValue{T}"/> so timing
/// commands remap them like every other pulse value; <see cref="Add(PulseValue{string})"/> keeps
/// label-only call sites (older code and tests) compiling.
/// </summary>
public sealed class BookmarkList : List<PulseValue<Bookmark>>
{
    public BookmarkList() { }

    public BookmarkList(IEnumerable<PulseValue<Bookmark>> items) : base(items) { }

    /// <summary>A label-only bookmark.</summary>
    public void Add(PulseValue<string> labelOnly) => Add(new PulseValue<Bookmark>(labelOnly.Y, new Bookmark(labelOnly.Value ?? "")));

    /// <summary>The bookmark on exactly <paramref name="y"/>, or null.</summary>
    public PulseValue<Bookmark>? At(long y) => Find(b => b.Y == y);

    /// <summary>Puts <paramref name="value"/> on <paramref name="y"/> (replacing one already there), keeping the list sorted.</summary>
    public void Set(long y, Bookmark value)
    {
        int i = FindIndex(b => b.Y == y);
        if (i >= 0)
        {
            this[i] = new PulseValue<Bookmark>(y, value);
            return;
        }

        int at = FindIndex(b => b.Y > y);
        Insert(at < 0 ? Count : at, new PulseValue<Bookmark>(y, value));
    }

    /// <summary>Removes the bookmark on <paramref name="y"/>; false when there was none.</summary>
    public bool RemoveOn(long y)
    {
        int i = FindIndex(b => b.Y == y);
        if (i < 0) return false;
        base.RemoveAt(i);
        return true;
    }
}

/// <summary>Data that only exists to round-trip ksh (kson <c>compat</c>).</summary>
public sealed class CompatInfo
{
    public string KshVersion { get; set; } = "";
    public Dictionary<string, string> KshUnknownMeta { get; set; } = new();
    public Dictionary<string, List<PulseValue<string>>> KshUnknownOption { get; set; } = new();
    public List<PulseValue<string>> KshUnknownLine { get; set; } = new();
}
