using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Editing;

/// <summary>The lanes a timestamp item can name, in the order items are written.</summary>
public enum TimestampLane
{
    BtA,
    BtB,
    BtC,
    BtD,
    FxL,
    FxR,
    LaserL,
    LaserR,
}

/// <summary>
/// One object of a timestamp: its time (ms, as written: whole milliseconds), lane and form. <see cref="Hold"/> is
/// informational for BT/FX (a chip and a hold cannot start on the same pulse of one lane). Laser points carry their
/// value, a slam also <see cref="ValueTo"/>; both may be missing in a hand-written timestamp.
/// </summary>
public sealed record TimestampItem(double TimeMs, TimestampLane Lane, bool Hold = false, double? Value = null, double? ValueTo = null)
{
    public bool IsLaser => Lane is TimestampLane.LaserL or TimestampLane.LaserR;

    public override string ToString() => ChartTimestamp.FormatItem(this);
}

/// <summary>
/// osu!-style modding timestamp for SDVX charts. osu! writes <c>00:12:345 (12345|2,12500|3) - </c>; the SDVX form
/// keeps that shape and spells every object out so it reads in a forum post and resolves back to exactly those
/// objects:
/// <code>01:23:456 (01:23:456|BT-A,01:23:581|FX-L hold,01:24:000|LASER-L 0.25&gt;0.75) - </code>
/// <list type="bullet">
/// <item>The leading time is the earliest object (where the editor jumps).</item>
/// <item>Item = <c>time|lane[ hold][ value[&gt;value]]</c>; lanes <c>BT-A..BT-D</c>, <c>FX-L</c>, <c>FX-R</c>,
/// <c>LASER-L</c>, <c>LASER-R</c>; <c>hold</c> marks a BT/FX hold; a laser point carries its value (0..1, up to three
/// decimals), a slam <c>from&gt;to</c>. Items are ordered by time, then lane.</item>
/// <item>Times are <c>mm:ss:mmm</c> (minutes may exceed 59).</item>
/// </list>
/// Parsing is tolerant: <c>mm:ss.mmm</c>, <c>ss.mmm</c>, plain milliseconds (osu!'s item form), lane spellings
/// <c>BTA</c>, <c>bt a</c>, <c>FXL</c>, <c>LASER L</c>, <c>VOL-L</c>, <c>KNOB-R</c>, <c>-&gt;</c> for slams, any text
/// after the closing bracket or the <c>-</c>, a timestamp inside a longer line, and <c>mm:ss:mmm -</c> alone.
/// Items it cannot read are kept in <see cref="Unparsed"/>; the rest still resolve.
/// </summary>
public sealed class ChartTimestamp
{
    /// <summary>How far (ms) a written time may be from an object's exact time: written times are rounded to whole ms.</summary>
    public const double TimeToleranceMs = 1.0;

    /// <summary>How far a written laser value may be from the point's value (three decimals are written).</summary>
    public const double ValueTolerance = 0.001;

    public ChartTimestamp(double timeMs, IReadOnlyList<TimestampItem>? items = null, IReadOnlyList<string>? unparsed = null)
    {
        TimeMs = timeMs;
        Items = items ?? Array.Empty<TimestampItem>();
        Unparsed = unparsed ?? Array.Empty<string>();
    }

    /// <summary>Where the timestamp points (the earliest object, or the bare time).</summary>
    public double TimeMs { get; }

    public IReadOnlyList<TimestampItem> Items { get; }

    /// <summary>Items between the brackets that could not be read (shown to the user, ignored otherwise).</summary>
    public IReadOnlyList<string> Unparsed { get; }

    public override string ToString() => Format(TimeMs, Items);

    // ---- writing ---------------------------------------------------------------

    /// <summary><c>mm:ss:mmm</c> of a time, rounded to whole ms; negative times get a leading minus.</summary>
    public static string FormatTime(double ms)
    {
        long total = (long)Math.Round(ms, MidpointRounding.AwayFromZero);
        string sign = total < 0 ? "-" : "";
        total = Math.Abs(total);
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{total / 60000:00}:{total / 1000 % 60:00}:{total % 1000:000}");
    }

    public static string LaneName(TimestampLane lane) => lane switch
    {
        TimestampLane.BtA => "BT-A",
        TimestampLane.BtB => "BT-B",
        TimestampLane.BtC => "BT-C",
        TimestampLane.BtD => "BT-D",
        TimestampLane.FxL => "FX-L",
        TimestampLane.FxR => "FX-R",
        TimestampLane.LaserL => "LASER-L",
        _ => "LASER-R",
    };

    private static string value(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatItem(TimestampItem item)
    {
        var sb = new StringBuilder(FormatTime(item.TimeMs)).Append('|').Append(LaneName(item.Lane));
        if (item.Hold && !item.IsLaser) sb.Append(" hold");
        if (item.Value is double v)
        {
            sb.Append(' ').Append(value(v));
            if (item.ValueTo is double to) sb.Append('>').Append(value(to));
        }

        return sb.ToString();
    }

    /// <summary>The text form: <c>time (item,item) - </c>, or <c>time - </c> without items.</summary>
    public static string Format(double timeMs, IEnumerable<TimestampItem> items)
    {
        var list = items.ToList();
        return list.Count == 0
            ? FormatTime(timeMs) + " - "
            : $"{FormatTime(timeMs)} ({string.Join(",", list.Select(FormatItem))}) - ";
    }

    /// <summary>The items of a selection, ordered by time then lane.</summary>
    public static List<TimestampItem> ItemsOf(TimingMap timing, IEnumerable<NoteRef> notes, IEnumerable<LaserPointRef> points)
    {
        var items = new List<(long Pulse, TimestampItem Item)>();
        foreach (var n in notes)
        {
            var lane = n.Kind == NoteKind.Bt ? TimestampLane.BtA + n.Lane : TimestampLane.FxL + n.Lane;
            items.Add((n.Note.Y, new TimestampItem(Math.Round(timing.PulseToMs(n.Note.Y)), lane, n.Note.IsHold)));
        }

        foreach (var p in points)
        {
            long y = p.Section.Y + p.Point.Ry;
            items.Add((y, new TimestampItem(Math.Round(timing.PulseToMs(y)), TimestampLane.LaserL + p.Lane, false,
                p.Point.V, p.Point.IsSlam ? p.Point.Vf : null)));
        }

        return items.OrderBy(i => i.Pulse).ThenBy(i => i.Item.Lane).ThenBy(i => i.Item.Value ?? 0).Select(i => i.Item).ToList();
    }

    /// <summary>
    /// The timestamp of a selection (osu!'s Ctrl+C text). An empty selection gives <c>time - </c> at
    /// <paramref name="emptyTimeMs"/>.
    /// </summary>
    public static ChartTimestamp Of(TimingMap timing, IEnumerable<NoteRef> notes, IEnumerable<LaserPointRef> points, double emptyTimeMs = 0)
    {
        var items = ItemsOf(timing, notes, points);
        return new ChartTimestamp(items.Count > 0 ? items.Min(i => i.TimeMs) : Math.Round(emptyTimeMs), items);
    }

    /// <summary>
    /// The timestamp of the editor selection. A selected slam end is written as its slam (<c>from&gt;to</c>, once even
    /// when both ends are selected): the format addresses points, and reading it back selects the slam point.
    /// </summary>
    public static ChartTimestamp Of(TimingMap timing, Selection selection, double emptyTimeMs = 0) =>
        Of(timing, selection.Notes, selection.TouchedLaserPoints, emptyTimeMs);

    // ---- reading ---------------------------------------------------------------

    private const string time_pattern = @"-?\d+:\d{1,2}[:.]\d{1,3}";

    private static readonly Regex full_time = new(@"^(?<neg>-)?(?<m>\d+):(?<s>\d{1,2})(?:[:.](?<f>\d{1,3}))?$", RegexOptions.CultureInvariant);
    private static readonly Regex seconds_time = new(@"^(?<neg>-)?(?<s>\d+)[.,](?<f>\d{1,3})$", RegexOptions.CultureInvariant);
    private static readonly Regex ms_time = new(@"^(?<neg>-)?(?<ms>\d+)(?:\s*ms)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex embedded = new(@"(?<time>" + time_pattern + @")\s*(?:\((?<items>[^)]*)\)?)?", RegexOptions.CultureInvariant);

    private static readonly Regex item_pattern = new(
        @"^(?<t>-?[\d:.]+)\s*\|\s*(?<kind>BT|FX|LASER|VOL|KNOB)[\s_-]*(?<lane>[A-D]|[LR])(?:\s+(?<form>hold|long|chip))?" +
        @"(?:\s+(?<v>\d*\.?\d+)(?:\s*(?:>|->|→)\s*(?<vf>\d*\.?\d+))?)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Reads a time: <c>mm:ss:mmm</c>, <c>mm:ss.mmm</c>, <c>mm:ss</c>, <c>ss.mmm</c> or plain ms.</summary>
    public static bool TryParseTime(string? text, out double ms)
    {
        ms = 0;
        string t = (text ?? "").Trim();
        if (t.Length == 0) return false;

        static double fraction(Group g) => g.Success ? int.Parse(g.Value.PadRight(3, '0'), CultureInfo.InvariantCulture) : 0;

        Match m;
        if ((m = full_time.Match(t)).Success)
        {
            int seconds = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
            if (seconds > 59) return false;
            ms = long.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) * 60000 + seconds * 1000 + fraction(m.Groups["f"]);
        }
        else if ((m = seconds_time.Match(t)).Success)
            ms = long.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture) * 1000 + fraction(m.Groups["f"]);
        else if ((m = ms_time.Match(t)).Success)
            ms = long.Parse(m.Groups["ms"].Value, CultureInfo.InvariantCulture);
        else
            return false;

        if (m.Groups["neg"].Success) ms = -ms;
        return true;
    }

    /// <summary>
    /// Reads a timestamp or a bare time (see the class summary for the accepted forms). False when no time is found.
    /// </summary>
    public static bool TryParse(string? text, out ChartTimestamp timestamp)
    {
        timestamp = new ChartTimestamp(0);
        string t = (text ?? "").Trim();
        if (t.Length == 0) return false;

        // a bare time in any of the accepted forms, optionally followed by " -"
        string bare = t.EndsWith('-') ? t[..^1].TrimEnd() : t;
        if (TryParseTime(bare, out double plain))
        {
            timestamp = new ChartTimestamp(plain);
            return true;
        }

        var m = embedded.Match(t);
        if (!m.Success || !TryParseTime(m.Groups["time"].Value, out double at)) return false;

        var items = new List<TimestampItem>();
        var unparsed = new List<string>();
        if (m.Groups["items"].Success)
        {
            foreach (string raw in m.Groups["items"].Value.Split(','))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                if (TryParseItem(part, out var item)) items.Add(item);
                else unparsed.Add(part);
            }
        }

        timestamp = new ChartTimestamp(at, items, unparsed);
        return true;
    }

    /// <summary>One item (<c>time|lane[ hold][ value[&gt;value]]</c>).</summary>
    public static bool TryParseItem(string text, out TimestampItem item)
    {
        item = null!;
        var m = item_pattern.Match(text.Trim());
        if (!m.Success || !TryParseTime(m.Groups["t"].Value, out double ms)) return false;

        string kind = m.Groups["kind"].Value.ToUpperInvariant();
        char lane = char.ToUpperInvariant(m.Groups["lane"].Value[0]);
        TimestampLane l;
        switch (kind)
        {
            case "BT" when lane is >= 'A' and <= 'D':
                l = TimestampLane.BtA + (lane - 'A');
                break;

            case "FX" when lane is 'L' or 'R':
                l = lane == 'L' ? TimestampLane.FxL : TimestampLane.FxR;
                break;

            case "LASER" or "VOL" or "KNOB" when lane is 'L' or 'R':
                l = lane == 'L' ? TimestampLane.LaserL : TimestampLane.LaserR;
                break;

            default:
                return false;
        }

        bool laser = l is TimestampLane.LaserL or TimestampLane.LaserR;
        double? v = null, vf = null;
        if (m.Groups["v"].Success)
        {
            if (!laser) return false; // a value on a BT/FX item is not something we wrote
            v = double.Parse(m.Groups["v"].Value, CultureInfo.InvariantCulture);
            if (m.Groups["vf"].Success) vf = double.Parse(m.Groups["vf"].Value, CultureInfo.InvariantCulture);
        }

        if (m.Groups["form"].Success && laser) return false;

        bool hold = m.Groups["form"].Success && !m.Groups["form"].Value.Equals("chip", StringComparison.OrdinalIgnoreCase);
        item = new TimestampItem(ms, l, hold, v, vf);
        return true;
    }

    // ---- resolving -------------------------------------------------------------

    /// <summary>
    /// Finds the objects the items name. Every item resolves to the closest object in its lane within
    /// <see cref="TimeToleranceMs"/> (laser points also within <see cref="ValueTolerance"/> of a written value, a slam's
    /// target included). An item with no such object is <c>Missing</c>; one with several equally good candidates is
    /// <c>Ambiguous</c> and selects all of them.
    /// </summary>
    public TimestampResolution Resolve(Chart chart, TimingMap timing)
    {
        var result = new TimestampResolution();

        foreach (var item in Items)
        {
            if (item.IsLaser)
            {
                int lane = item.Lane - TimestampLane.LaserL;
                var candidates = new List<(LaserPointRef Ref, double Dt)>();
                foreach (var s in chart.Notes.Lasers[lane])
                {
                    foreach (var p in s.Points)
                    {
                        double dt = Math.Abs(timing.PulseToMs(s.Y + p.Ry) - item.TimeMs);
                        if (dt > TimeToleranceMs) continue;
                        if (item.Value is double v && Math.Abs(p.V - v) > ValueTolerance) continue;
                        if (item.ValueTo is double vf && !(p.IsSlam && Math.Abs(p.Vf!.Value - vf) <= ValueTolerance)) continue;
                        candidates.Add((new LaserPointRef(lane, s, p), dt));
                    }
                }

                // a written plain point prefers a plain point over a slam on the same spot (and the other way round)
                if (item.Value != null && candidates.Count > 1)
                {
                    var sameForm = candidates.Where(c => c.Ref.Point.IsSlam == (item.ValueTo != null)).ToList();
                    if (sameForm.Count > 0) candidates = sameForm;
                }

                var best = closest(candidates, c => c.Dt);
                if (best.Count == 0) result.Missing.Add(item);
                else
                {
                    if (best.Count > 1) result.Ambiguous.Add(item);
                    foreach (var c in best)
                        if (!result.LaserPoints.Contains(c.Ref)) result.LaserPoints.Add(c.Ref);
                }
            }
            else
            {
                var kind = item.Lane <= TimestampLane.BtD ? NoteKind.Bt : NoteKind.Fx;
                int lane = kind == NoteKind.Bt ? item.Lane - TimestampLane.BtA : item.Lane - TimestampLane.FxL;
                var candidates = chart.Notes.Lane(kind, lane)
                                      .Select(n => (Ref: new NoteRef(kind, lane, n), Dt: Math.Abs(timing.PulseToMs(n.Y) - item.TimeMs)))
                                      .Where(c => c.Dt <= TimeToleranceMs)
                                      .ToList();

                var best = closest(candidates, c => c.Dt);
                if (best.Count == 0) result.Missing.Add(item);
                else
                {
                    if (best.Count > 1) result.Ambiguous.Add(item);
                    foreach (var c in best)
                        if (!result.Notes.Contains(c.Ref)) result.Notes.Add(c.Ref);
                }
            }
        }

        return result;
    }

    private static List<T> closest<T>(List<T> candidates, Func<T, double> distance)
    {
        if (candidates.Count == 0) return candidates;
        double min = candidates.Min(distance);
        return candidates.Where(c => distance(c) - min < 1e-6).ToList();
    }
}

/// <summary>What a timestamp resolved to in a chart.</summary>
public sealed class TimestampResolution
{
    public List<NoteRef> Notes { get; } = new();
    public List<LaserPointRef> LaserPoints { get; } = new();

    /// <summary>Items with no matching object (deleted, moved or never there).</summary>
    public List<TimestampItem> Missing { get; } = new();

    /// <summary>Items that matched several objects equally well (all of them are selected).</summary>
    public List<TimestampItem> Ambiguous { get; } = new();

    public int Count => Notes.Count + LaserPoints.Count;

    /// <summary>Replaces <paramref name="selection"/> with the resolved objects.</summary>
    public void ApplyTo(Selection selection)
    {
        selection.Clear();
        foreach (var n in Notes) selection.Add(n);
        foreach (var p in LaserPoints) selection.Add(p);
    }
}
