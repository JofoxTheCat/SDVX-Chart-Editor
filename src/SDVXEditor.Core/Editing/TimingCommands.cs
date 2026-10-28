using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Editing;

// =====================================================================================
// Timing edits: BPM / time-signature / scroll-speed / stop points, the audio offset and
// preview, and the osu! "When applying timing changes…" operations.
//
// The model is pulse based (240 per quarter), so a note's *musical* position never moves
// when the tempo changes: "move placed notes with the BPM/offset" is free. The opposite
// ("keep notes at their time in ms") re-quantises every object onto the new tempo map,
// optionally snapped to a beat divisor. "Scale" stretches the objects between the
// neighbouring BPM points when a BPM point is moved.
// =====================================================================================

/// <summary>Which pulse-carrying parts of a chart <see cref="RemapPulsesCommand"/> touches.</summary>
[Flags]
public enum RetimeTargets
{
    None = 0,

    /// <summary>BT/FX notes (with their FX effect events).</summary>
    Notes = 1 << 0,

    /// <summary>Laser sections and their control points.</summary>
    Lasers = 1 << 1,

    /// <summary>Things bound to notes or slams: camera spin/swing patterns, slam sound events, orphan key sounds / FX events.</summary>
    NoteBound = 1 << 2,

    ScrollSpeed = 1 << 3,
    Stops = 1 << 4,

    /// <summary>Camera graphs (zoom, centre split, rotation) and tilt.</summary>
    Camera = 1 << 5,

    /// <summary>Audio-effect parameter changes, laser filter switches, slam volume, filter gain.</summary>
    EffectCurves = 1 << 6,

    /// <summary>VOX-only rows (post effects, SPCONTROLER).</summary>
    VoxExtras = 1 << 7,

    Bookmarks = 1 << 8,

    /// <summary>Editor comments and unknown ksh lines/options.</summary>
    Comments = 1 << 9,

    /// <summary>The explicit chart end.</summary>
    EndPulse = 1 << 10,

    /// <summary>Everything a player hits plus what is bound to it.</summary>
    NoteObjects = Notes | Lasers | NoteBound | EndPulse,

    /// <summary>Everything else that lives on the time line (no BPM / time signatures).</summary>
    Extras = ScrollSpeed | Stops | Camera | EffectCurves | VoxExtras | Comments,

    All = NoteObjects | Extras | Bookmarks,
}

/// <summary>
/// Moves every pulse of the chosen chart parts through <c>map</c> (which must be monotonic
/// non-decreasing). Holds keep a positive length; laser points or graph points that collapse onto
/// one pulse merge into a slam (first V, last value-after). The plan is computed on the first
/// execute, so redo re-applies exactly the same values and undo restores the old ones.
/// </summary>
public sealed class RemapPulsesCommand : IEditCommand
{
    private readonly Func<long, long> map;
    private readonly RetimeTargets targets;

    private List<(Action<long> Set, long Old, long New)>? values;
    private List<(Action<object> Set, object Old, object New)>? swaps;

    public RemapPulsesCommand(string description, Func<long, long> map, RetimeTargets targets)
    {
        Description = description;
        this.map = map;
        this.targets = targets;
    }

    public string Description { get; }

    /// <summary>How many values / structures changed (known after the first execute).</summary>
    public int ChangedCount => (values?.Count ?? 0) + (swaps?.Count ?? 0);

    public void Execute(Chart chart)
    {
        if (values == null) plan(chart);
        foreach (var (set, _, n) in values!) set(n);
        foreach (var (set, _, n) in swaps!) set(n);
    }

    public void Undo(Chart chart)
    {
        for (int i = swaps!.Count - 1; i >= 0; i--) swaps[i].Set(swaps[i].Old);
        for (int i = values!.Count - 1; i >= 0; i--) values[i].Set(values[i].Old);
    }

    private bool has(RetimeTargets t) => (targets & t) != 0;

    private void plan(Chart c)
    {
        values = new List<(Action<long>, long, long)>();
        swaps = new List<(Action<object>, object, object)>();

        void value(long old, Action<long> set)
        {
            long n = map(old);
            if (n != old) values.Add((set, old, n));
        }

        // a span (y, length): the end goes through the map too, a positive length stays positive
        void span(long y, long length, Action<long> setY, Action<long> setLength)
        {
            long ny = map(y);
            long nl = length;
            if (length > 0)
            {
                nl = map(y + length) - ny;
                if (nl <= 0) nl = length;
            }

            if (ny != y) values.Add((setY, y, ny));
            if (nl != length) values.Add((setLength, length, nl));
        }

        void pulseValues<T>(List<PulseValue<T>> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int k = i;
                // bookmarks are edited outside the undo stack: a list that shrank since is skipped, not thrown on
                value(list[k].Y, v => { if (k < list.Count) list[k] = list[k] with { Y = v }; });
            }
        }

        void longs(List<long> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int k = i;
                value(list[k], v => list[k] = v);
            }
        }

        void graph(Func<List<GraphPoint>> get, Action<List<GraphPoint>> set)
        {
            var old = get();
            var remapped = RemapGraph(old, map);
            if (remapped != null) swaps.Add((o => set((List<GraphPoint>)o), old, remapped));
        }

        if (has(RetimeTargets.Notes))
        {
            foreach (var lane in c.Notes.Bt.Concat(c.Notes.Fx))
            {
                foreach (var n in lane)
                {
                    var note = n;
                    long newY = map(note.Y);
                    foreach (var e in note.Effects)
                    {
                        var ev = e;
                        long rel = Math.Max(0, map(note.Y + ev.RelY) - newY);
                        if (rel != ev.RelY) values.Add((v => ev.RelY = v, ev.RelY, rel));
                    }

                    span(note.Y, note.Length, v => note.Y = v, v => note.Length = v);
                }
            }
        }

        if (has(RetimeTargets.Lasers))
        {
            foreach (var lane in c.Notes.Lasers)
            {
                foreach (var s in lane)
                {
                    var section = s;
                    var remapped = RemapSection(section, map);
                    if (remapped == null) continue;
                    var old = (section.Y, section.Points);
                    swaps.Add((o =>
                    {
                        var (y, pts) = ((long, List<LaserPoint>))o;
                        section.Y = y;
                        section.Points = pts;
                    }, old, remapped.Value));
                }

                // sections the map pushed closer than the placement gap (OverlapRule.MinLaserGap) join into one laser;
                // runs after the per-section swaps above (undo: the lane list first, then the sections)
                var after = lane.Select(x => RemapSection(x, map) is { } r ? (r.Y, r.Points, x.Width) : (x.Y, x.Points, x.Width)).ToList();
                if (JoinClose(after) is { } joined)
                {
                    var laneList = lane;
                    var oldList = lane.ToList();
                    var newList = joined.Select(j => j.Source >= 0 ? oldList[j.Source] : j.Joined!).ToList();
                    swaps.Add((o =>
                    {
                        laneList.Clear();
                        laneList.AddRange((List<LaserSection>)o);
                    }, oldList, newList));
                }
            }
        }

        if (has(RetimeTargets.NoteBound))
        {
            foreach (var list in new[] { c.Camera.Spin, c.Camera.HalfSpin, c.Camera.Swing })
            {
                foreach (var p in list)
                {
                    var pattern = p;
                    span(pattern.Y, pattern.Length, v => pattern.Y = v, v => pattern.Length = v);
                }
            }

            foreach (var list in c.Audio.KeySound.LaserSlamEvents.Values) longs(list);

            var chips = c.Audio.KeySound.OrphanFxChipEvents;
            for (int i = 0; i < chips.Count; i++)
            {
                int k = i;
                value(chips[k].Y, v => chips[k] = chips[k] with { Y = v });
            }

            var longEvents = c.Audio.FxEffects.OrphanLongEvents;
            for (int i = 0; i < longEvents.Count; i++)
            {
                int k = i;
                value(longEvents[k].Y, v => longEvents[k] = longEvents[k] with { Y = v });
            }
        }

        if (has(RetimeTargets.EndPulse) && c.EndPulse is long end)
            value(end, v => c.EndPulse = v);

        if (has(RetimeTargets.ScrollSpeed))
            graph(() => c.Beat.ScrollSpeed, l => c.Beat.ScrollSpeed = l);

        if (has(RetimeTargets.Stops))
        {
            foreach (var s in c.Beat.Stops)
            {
                var stop = s;
                span(stop.Y, stop.Length, v => stop.Y = v, v => stop.Length = v);
            }
        }

        if (has(RetimeTargets.Camera))
        {
            var body = c.Camera.Body;
            graph(() => body.ZoomTop, l => body.ZoomTop = l);
            graph(() => body.ZoomBottom, l => body.ZoomBottom = l);
            graph(() => body.ZoomSide, l => body.ZoomSide = l);
            graph(() => body.CenterSplit, l => body.CenterSplit = l);
            graph(() => body.RotationDeg, l => body.RotationDeg = l);
            foreach (var t in c.Camera.Tilt)
            {
                var tilt = t;
                value(tilt.Y, v => tilt.Y = v);
            }
        }

        if (has(RetimeTargets.EffectCurves))
        {
            foreach (var info in new[] { c.Audio.FxEffects.ParamChanges, c.Audio.LaserEffects.ParamChanges })
                foreach (var byParam in info.Values)
                    foreach (var list in byParam.Values)
                        pulseValues(list);

            foreach (var list in c.Audio.LaserEffects.PulseEvents.Values) longs(list);
            pulseValues(c.Audio.KeySound.LaserVolume);
            pulseValues(c.Audio.LaserEffects.LegacyFilterGain);
        }

        if (has(RetimeTargets.VoxExtras))
        {
            foreach (var p in c.VoxExtras.PostEffects)
            {
                var row = p;
                span(row.Y, row.Length, v => row.Y = v, v => row.Length = v);
            }

            foreach (var r in c.VoxExtras.SpController)
            {
                var row = r;
                span(row.Y, row.Length, v => row.Y = v, v => row.Length = v);
            }

            foreach (var r in c.VoxExtras.Commands)
            {
                var row = r;
                span(row.Y, row.Length, v => row.Y = v, v => row.Length = v);
            }
        }

        // a script assignment belongs to its note: it moves with what is bound to notes
        if (has(RetimeTargets.NoteBound))
        {
            foreach (var s in c.VoxExtras.Scripted)
            {
                var row = s;
                span(row.Y, 0, v => row.Y = v, _ => { });
            }
        }

        if (has(RetimeTargets.Bookmarks))
            pulseValues(c.Editor.Bookmarks);

        if (has(RetimeTargets.Comments))
        {
            pulseValues(c.Editor.Comments);
            pulseValues(c.Compat.KshUnknownLine);
            foreach (var list in c.Compat.KshUnknownOption.Values) pulseValues(list);
        }
    }

    /// <summary>
    /// Joins neighbouring sections of one lane (sorted, as remapped: start, points, width) that lie closer than
    /// <see cref="OverlapRule.MinLaserGap"/> — a retime (delete range, resnap, keep notes in time) can push them together,
    /// which the placement rule never allows. Same pulse: the earlier end and the later start become one slam; a small gap
    /// stays a short segment. Sections of different widths cannot be one laser and stay apart (the chart check reports
    /// them). Returns per resulting section the index of an unchanged input (Source ≥ 0) or a new joined section
    /// (Source = −1), or null when nothing joins.
    /// </summary>
    public static List<(int Source, LaserSection? Joined)>? JoinClose(IReadOnlyList<(long Y, List<LaserPoint> Points, int Width)> lane)
    {
        var result = new List<(int Source, LaserSection? Joined)>();
        bool any = false;
        LaserSection? current = null;
        int currentSource = -1;
        long currentEnd = 0;

        for (int i = 0; i < lane.Count; i++)
        {
            var (y, pts, width) = lane[i];
            if (current != null && pts.Count > 0 && current.Points.Count > 0 && width == current.Width && y >= currentEnd && y - currentEnd < OverlapRule.MinLaserGap)
            {
                if (currentSource >= 0)
                {
                    current = current.Clone();
                    current.Path = null;
                    currentSource = -1;
                }

                foreach (var p in pts)
                {
                    long ry = y + p.Ry - current.Y;
                    var last = current.Points[^1];
                    if (ry <= last.Ry)
                    {
                        double v = p.ValueAfter;
                        last.Vf = v == last.V ? null : v;
                        last.Curve = p.Curve;
                        continue;
                    }

                    current.Points.Add(new LaserPoint(ry, p.V, p.Vf, p.Curve));
                }

                currentEnd = current.EndY;
                any = true;
                continue;
            }

            if (current != null) result.Add((currentSource, currentSource >= 0 ? null : current));
            current = new LaserSection(y, pts.Select(p => p.Clone()), width);
            currentSource = i;
            currentEnd = current.EndY;
        }

        if (current != null) result.Add((currentSource, currentSource >= 0 ? null : current));
        return any ? result : null;
    }

    /// <summary>The section remapped as (new start, new point list), or null when nothing moves.</summary>
    public static (long Y, List<LaserPoint> Points)? RemapSection(LaserSection s, Func<long, long> map)
    {
        if (s.Points.Count == 0)
        {
            long y = map(s.Y);
            return y == s.Y ? null : (y, new List<LaserPoint>());
        }

        long newY = map(s.Y + s.Points[0].Ry);
        var points = new List<LaserPoint>(s.Points.Count);
        bool changed = newY != s.Y + s.Points[0].Ry || s.Points[0].Ry != 0;

        foreach (var p in s.Points)
        {
            long ry = map(s.Y + p.Ry) - newY;
            if (ry != p.Ry) changed = true;

            if (points.Count > 0 && ry <= points[^1].Ry)
            {
                // collapsed onto the previous point: one slam from its V to this value-after
                var prev = points[^1];
                double after = p.ValueAfter;
                prev.Vf = after == prev.V ? null : after;
                prev.Curve = p.Curve;
                changed = true;
                continue;
            }

            points.Add(new LaserPoint(ry, p.V, p.Vf, p.Curve));
        }

        return changed ? (newY, points) : null;
    }

    /// <summary>The graph remapped (collapsed points merge into a jump), or null when nothing moves.</summary>
    public static List<GraphPoint>? RemapGraph(List<GraphPoint> graph, Func<long, long> map)
    {
        var result = new List<GraphPoint>(graph.Count);
        bool changed = false;

        foreach (var p in graph)
        {
            long y = map(p.Y);
            if (y != p.Y) changed = true;

            if (result.Count > 0 && y <= result[^1].Y)
            {
                var prev = result[^1];
                double after = p.ValueAfter;
                prev.Vf = after == prev.V ? null : after;
                prev.Curve = p.Curve;
                changed = true;
                continue;
            }

            result.Add(new GraphPoint(y, p.V, p.Vf, p.Curve));
        }

        return changed ? result : null;
    }
}

/// <summary>Inserts an item into a list kept sorted by a key (after equal keys).</summary>
public sealed class InsertSortedCommand<T> : IEditCommand where T : class
{
    private readonly Func<Chart, List<T>> list;
    private readonly Func<T, long> key;

    public InsertSortedCommand(string description, Func<Chart, List<T>> list, Func<T, long> key, T item)
    {
        Description = description;
        this.list = list;
        this.key = key;
        Item = item;
    }

    public string Description { get; }
    public T Item { get; }

    public void Execute(Chart chart)
    {
        var l = list(chart);
        long k = key(Item);
        int i = l.FindLastIndex(x => key(x) <= k) + 1;
        l.Insert(i, Item);
    }

    public void Undo(Chart chart) => list(chart).Remove(Item);
}

/// <summary>Removes an item (by reference) and puts it back at the same index on undo.</summary>
public sealed class RemoveItemCommand<T> : IEditCommand where T : class
{
    private readonly Func<Chart, List<T>> list;
    private int index = -1;

    public RemoveItemCommand(string description, Func<Chart, List<T>> list, T item)
    {
        Description = description;
        this.list = list;
        Item = item;
    }

    public string Description { get; }
    public T Item { get; }

    public void Execute(Chart chart)
    {
        var l = list(chart);
        index = l.IndexOf(Item);
        if (index < 0) throw new InvalidOperationException("item is not in the list");
        l.RemoveAt(index);
    }

    public void Undo(Chart chart) => list(chart).Insert(index, Item);
}

/// <summary>
/// Changes an item in place (identity kept, so references stay valid) and re-sorts the list by its
/// key; undo restores both the old field values and the old order.
/// </summary>
public sealed class ChangeItemCommand<T> : IEditCommand where T : class
{
    private readonly Func<Chart, List<T>> list;
    private readonly Func<T, long> key;
    private readonly Func<T, T> clone;
    private readonly Action<T, T> copyInto;
    private readonly T target;
    private T? old;
    private List<T>? oldOrder;

    /// <param name="target">An unattached object holding the new values.</param>
    /// <param name="copyInto">Copies every edited field from the first argument into the second.</param>
    public ChangeItemCommand(string description, Func<Chart, List<T>> list, Func<T, long> key, T item, T target, Func<T, T> clone, Action<T, T> copyInto)
    {
        Description = description;
        this.list = list;
        this.key = key;
        Item = item;
        this.target = target;
        this.clone = clone;
        this.copyInto = copyInto;
    }

    public string Description { get; }
    public T Item { get; }

    public void Execute(Chart chart)
    {
        var l = list(chart);
        old = clone(Item);
        oldOrder = l.ToList();
        copyInto(target, Item);
        var sorted = l.OrderBy(key).ToList(); // stable
        l.Clear();
        l.AddRange(sorted);
    }

    public void Undo(Chart chart)
    {
        copyInto(old!, Item);
        var l = list(chart);
        l.Clear();
        l.AddRange(oldOrder!);
    }
}

/// <summary>Replaces the whole scroll-speed graph (Property Transformer tools, base speed); undo puts the old list back.</summary>
public sealed class SetScrollSpeedGraphCommand : IEditCommand
{
    private readonly List<GraphPoint> graph;
    private List<GraphPoint>? old;

    public SetScrollSpeedGraphCommand(string description, List<GraphPoint> graph)
    {
        Description = description;
        this.graph = graph;
    }

    public string Description { get; }

    public void Execute(Chart chart)
    {
        old = chart.Beat.ScrollSpeed;
        chart.Beat.ScrollSpeed = graph;
    }

    public void Undo(Chart chart) => chart.Beat.ScrollSpeed = old!;
}

/// <summary>How notes and the other timed objects follow a timing change (osu! "When applying timing changes…").</summary>
public sealed class TimingChangeOptions
{
    /// <summary>
    /// True (default): notes keep their musical position (measure/beat) and move in time with the
    /// grid — free in a pulse model. False: notes keep their time in ms and are re-quantised onto the
    /// new tempo map (snapped to <see cref="SnapDivisor"/> when <see cref="SnapNotes"/> is set).
    /// </summary>
    public bool MoveNotesWithGrid { get; set; } = true;

    /// <summary>A moved BPM point stretches/compresses the objects between its neighbouring BPM points.</summary>
    public bool ScaleNotes { get; set; }

    /// <summary>Notes that a timing change moves are snapped to 1/<see cref="SnapDivisor"/> of the new grid.</summary>
    public bool SnapNotes { get; set; }

    public int SnapDivisor { get; set; } = 4;

    /// <summary>
    /// True (default): scroll-speed points, stops, camera/effect curves, bookmarks and the audio
    /// preview keep their musical position. False: they keep their time in ms.
    /// </summary>
    public bool MoveExtrasWithGrid { get; set; } = true;

    public TimingChangeOptions Clone() => (TimingChangeOptions)MemberwiseClone();
}

/// <summary>
/// A working copy of a chart's timing (BPM, time signatures, scroll speed, stops, offset, preview,
/// volume) that a dialog edits freely. Every cloned point remembers its original, so
/// <see cref="TimingCommands.Apply"/> can turn the differences into commands on the original objects.
/// </summary>
public sealed class TimingDraft
{
    private readonly Dictionary<object, object> origin = new(ReferenceEqualityComparer.Instance);

    public BeatInfo Beat { get; } = new();
    public long OffsetMs { get; set; }
    public long PreviewOffsetMs { get; set; }
    public long PreviewDurationMs { get; set; }
    public double Volume { get; set; }

    public static TimingDraft From(Chart chart)
    {
        var d = new TimingDraft
        {
            OffsetMs = chart.Audio.Bgm.OffsetMs,
            PreviewOffsetMs = chart.Audio.Bgm.PreviewOffsetMs,
            PreviewDurationMs = chart.Audio.Bgm.PreviewDurationMs,
            Volume = chart.Audio.Bgm.Volume,
        };

        foreach (var b in chart.Beat.Bpm) d.Beat.Bpm.Add(d.track(b.Clone(), b));
        foreach (var t in chart.Beat.TimeSignatures) d.Beat.TimeSignatures.Add(d.track(t.Clone(), t));
        foreach (var p in chart.Beat.ScrollSpeed) d.Beat.ScrollSpeed.Add(d.track(p.Clone(), p));
        foreach (var s in chart.Beat.Stops) d.Beat.Stops.Add(d.track(s.Clone(), s));
        return d;
    }

    private T track<T>(T clone, T original) where T : class
    {
        origin[clone] = original;
        return clone;
    }

    /// <summary>The chart object a draft point was cloned from; null for a point added to the draft.</summary>
    public T? OriginOf<T>(T clone) where T : class => origin.TryGetValue(clone, out var o) ? (T)o : null;

    public TimingMap BuildMap() => new(Beat, OffsetMs);

    /// <summary>Keeps every list sorted (after edits that move points).</summary>
    public void Sort()
    {
        sortStable(Beat.Bpm, b => b.Y);
        sortStable(Beat.TimeSignatures, t => t.MeasureIndex);
        sortStable(Beat.ScrollSpeed, p => p.Y);
        sortStable(Beat.Stops, s => s.Y);
    }

    private static void sortStable<T>(List<T> list, Func<T, long> key)
    {
        var sorted = list.OrderBy(key).ToList();
        list.Clear();
        list.AddRange(sorted);
    }
}

/// <summary>Factories for every timing edit. All results are <see cref="IEditCommand"/>s for the <see cref="CommandStack"/>.</summary>
public static class TimingCommands
{
    // ---------------------------------------------------------------- primitive point edits

    public static IEditCommand AddBpm(BpmChange point) =>
        new InsertSortedCommand<BpmChange>("Add BPM point", c => c.Beat.Bpm, b => b.Y, point);

    public static IEditCommand RemoveBpm(BpmChange point) =>
        new RemoveItemCommand<BpmChange>("Delete BPM point", c => c.Beat.Bpm, point);

    /// <summary>Moves and/or re-values a BPM point (identity kept).</summary>
    public static IEditCommand ChangeBpm(BpmChange point, long y, double bpm)
    {
        if (bpm <= 0) throw new ArgumentOutOfRangeException(nameof(bpm));
        return new ChangeItemCommand<BpmChange>("Change BPM point", c => c.Beat.Bpm, b => b.Y, point, new BpmChange(y, bpm),
            b => b.Clone(), (from, to) => { to.Y = from.Y; to.Bpm = from.Bpm; });
    }

    public static IEditCommand AddTimeSignature(TimeSignature sig) =>
        new InsertSortedCommand<TimeSignature>("Add time signature", c => c.Beat.TimeSignatures, t => t.MeasureIndex, sig);

    public static IEditCommand RemoveTimeSignature(TimeSignature sig) =>
        new RemoveItemCommand<TimeSignature>("Delete time signature", c => c.Beat.TimeSignatures, sig);

    public static IEditCommand ChangeTimeSignature(TimeSignature sig, int measure, int numerator, int denominator)
    {
        if (numerator <= 0 || denominator <= 0) throw new ArgumentOutOfRangeException(nameof(numerator));
        return new ChangeItemCommand<TimeSignature>("Change time signature", c => c.Beat.TimeSignatures, t => t.MeasureIndex, sig,
            new TimeSignature(measure, numerator, denominator), t => t.Clone(),
            (from, to) => { to.MeasureIndex = from.MeasureIndex; to.Numerator = from.Numerator; to.Denominator = from.Denominator; });
    }

    /// <summary>
    /// The time signature from <paramref name="measure"/> on: changes the one starting there or adds
    /// one. Null when that signature is already in effect and nothing would change.
    /// </summary>
    public static IEditCommand? SetTimeSignature(Chart chart, int measure, int numerator, int denominator)
    {
        var existing = chart.Beat.TimeSignatures.LastOrDefault(t => t.MeasureIndex == measure);
        if (existing != null)
        {
            return existing.Numerator == numerator && existing.Denominator == denominator
                ? null
                : ChangeTimeSignature(existing, measure, numerator, denominator);
        }

        var current = new TimingMap(chart.Beat).SignatureOfMeasure(measure);
        if (current.Numerator == numerator && current.Denominator == denominator) return null;
        return AddTimeSignature(new TimeSignature(measure, numerator, denominator));
    }

    public static IEditCommand AddScrollSpeed(GraphPoint point) =>
        new InsertSortedCommand<GraphPoint>("Add scroll speed point", c => c.Beat.ScrollSpeed, p => p.Y, point);

    public static IEditCommand RemoveScrollSpeed(GraphPoint point) =>
        new RemoveItemCommand<GraphPoint>("Delete scroll speed point", c => c.Beat.ScrollSpeed, point);

    /// <summary>Moves / re-values a scroll-speed point, including its slam target (<paramref name="vf"/>) and curve.</summary>
    public static IEditCommand ChangeScrollSpeed(GraphPoint point, long y, double v, double? vf, Curve? curve) =>
        new ChangeItemCommand<GraphPoint>("Change scroll speed point", c => c.Beat.ScrollSpeed, p => p.Y, point, new GraphPoint(y, v, vf, curve),
            p => p.Clone(), (from, to) => { to.Y = from.Y; to.V = from.V; to.Vf = from.Vf; to.Curve = from.Curve; });

    public static IEditCommand AddStop(Stop stop) =>
        new InsertSortedCommand<Stop>("Add stop", c => c.Beat.Stops, s => s.Y, stop);

    public static IEditCommand RemoveStop(Stop stop) =>
        new RemoveItemCommand<Stop>("Delete stop", c => c.Beat.Stops, stop);

    public static IEditCommand ChangeStop(Stop stop, long y, long length) =>
        new ChangeItemCommand<Stop>("Change stop", c => c.Beat.Stops, s => s.Y, stop, new Stop(y, length),
            s => s.Clone(), (from, to) => { to.Y = from.Y; to.Length = from.Length; });

    // ---------------------------------------------------------------- audio

    public static IEditCommand SetOffset(long offsetMs) =>
        new SetValueCommand<long>("Change audio offset", c => c.Audio.Bgm.OffsetMs, (c, v) => c.Audio.Bgm.OffsetMs = v, offsetMs);

    public static IEditCommand SetPreview(long offsetMs, long durationMs) =>
        new SetValueCommand<(long, long)>("Change preview", c => (c.Audio.Bgm.PreviewOffsetMs, c.Audio.Bgm.PreviewDurationMs),
            (c, v) => (c.Audio.Bgm.PreviewOffsetMs, c.Audio.Bgm.PreviewDurationMs) = v, (offsetMs, durationMs));

    public static IEditCommand SetBgmVolume(double volume) =>
        new SetValueCommand<double>("Change music volume", c => c.Audio.Bgm.Volume, (c, v) => c.Audio.Bgm.Volume = v, volume);

    public static IEditCommand SetBgmFilename(string filename) =>
        new SetValueCommand<string>("Change music file", c => c.Audio.Bgm.Filename, (c, v) => c.Audio.Bgm.Filename = v, filename);

    // ---------------------------------------------------------------- retiming maps

    /// <summary>
    /// "Keep time": an object at pulse y stays at the same audio time. Its new pulse is found on
    /// <paramref name="newMap"/>; when it moves, it is snapped to 1/<paramref name="snapDivisor"/> of the
    /// new grid (0 = nearest pulse). Objects whose time maps back onto their own pulse are untouched,
    /// so off-grid notes before a changed BPM point are never re-quantised.
    /// </summary>
    public static Func<long, long> KeepTimeMap(TimingMap oldMap, TimingMap newMap, int snapDivisor = 0) => y =>
    {
        long p = (long)Math.Round(newMap.MsToPulse(oldMap.PulseToMs(y)));
        if (p == y) return y;
        if (y >= 0) p = Math.Max(0, p);
        return snapDivisor > 0 ? newMap.Snap(p, snapDivisor) : p;
    };

    /// <summary>
    /// "Scale": a piecewise-linear pulse map through <paramref name="anchors"/> (old pulse → new pulse,
    /// e.g. every BPM point before/after an edit). Outside the anchors objects shift with the nearest one.
    /// When <paramref name="grid"/> is given, moved objects snap to 1/<paramref name="snapDivisor"/> of it.
    /// </summary>
    public static Func<long, long> ScaleMap(IEnumerable<(long Old, long New)> anchors, TimingMap? grid = null, int snapDivisor = 0)
    {
        var a = anchors.GroupBy(x => x.Old).Select(g => g.First()).OrderBy(x => x.Old).ToArray();
        if (a.Length == 0) return y => y;

        return y =>
        {
            double r;
            if (y <= a[0].Old) r = y + (a[0].New - a[0].Old);
            else if (y >= a[^1].Old) r = y + (a[^1].New - a[^1].Old);
            else
            {
                int i = Array.FindLastIndex(a, x => x.Old <= y);
                var (o0, n0) = a[i];
                var (o1, n1) = a[i + 1];
                r = n0 + (double)(y - o0) * (n1 - n0) / (o1 - o0);
            }

            long p = (long)Math.Round(r);
            if (p == y) return y;
            if (y >= 0) p = Math.Max(0, p);
            return grid != null && snapDivisor > 0 ? grid.Snap(p, snapDivisor) : p;
        };
    }

    /// <summary>Snaps every object of <paramref name="targets"/> to 1/<paramref name="divisor"/> of the chart's grid ("Resnap all notes").</summary>
    public static IEditCommand Resnap(Chart chart, int divisor, RetimeTargets targets = RetimeTargets.NoteObjects)
    {
        var map = TimingMap.FromChart(chart);
        return new RemapPulsesCommand($"Resnap to 1/{divisor}", y => map.Snap(y, divisor), targets);
    }

    // ---------------------------------------------------------------- whole timing edit

    /// <summary>
    /// Turns an edited <see cref="TimingDraft"/> into one command: BPM / signature / scroll / stop point
    /// changes, offset / preview / volume, and — depending on <paramref name="options"/> — the
    /// remapping of notes and other objects. Null when the draft equals the chart.
    /// </summary>
    public static IEditCommand? Apply(Chart chart, TimingDraft draft, TimingChangeOptions options, string description = "Timing changes")
    {
        var cmds = new List<IEditCommand>();
        var oldMap = TimingMap.FromChart(chart);
        var newMap = draft.BuildMap();

        bool tempoChanged = draft.OffsetMs != chart.Audio.Bgm.OffsetMs || !sameBpms(chart.Beat.Bpm, draft.Beat.Bpm);

        // scale anchors: every BPM point that survived the edit
        var anchors = new List<(long, long)>();
        bool moved = false;
        foreach (var b in draft.Beat.Bpm)
        {
            if (draft.OriginOf(b) is not { } o) continue;
            anchors.Add((o.Y, b.Y));
            moved |= o.Y != b.Y;
        }

        Func<long, long>? noteMap = null, extrasMap = null;
        int snap = options.SnapNotes ? Math.Max(1, options.SnapDivisor) : 0;

        if (options.ScaleNotes && moved)
        {
            noteMap = ScaleMap(anchors, newMap, snap);
            extrasMap = ScaleMap(anchors);
        }
        else if (tempoChanged)
        {
            if (!options.MoveNotesWithGrid) noteMap = KeepTimeMap(oldMap, newMap, snap);
            if (!options.MoveExtrasWithGrid) extrasMap = KeepTimeMap(oldMap, newMap);
        }

        // ---- points
        diff(chart.Beat.Bpm, draft.Beat.Bpm, draft, null,
            (o, d) => o.Y == d.Y && o.Bpm == d.Bpm,
            d => AddBpm(d.Clone()), RemoveBpm, (o, d) => ChangeBpm(o, d.Y, d.Bpm), cmds);

        diff(chart.Beat.TimeSignatures, draft.Beat.TimeSignatures, draft, null,
            (o, d) => o.MeasureIndex == d.MeasureIndex && o.Numerator == d.Numerator && o.Denominator == d.Denominator,
            d => AddTimeSignature(d.Clone()), RemoveTimeSignature, (o, d) => ChangeTimeSignature(o, d.MeasureIndex, d.Numerator, d.Denominator), cmds);

        diff(chart.Beat.ScrollSpeed, draft.Beat.ScrollSpeed, draft,
            extrasMap == null ? null : (o => new GraphPoint(extrasMap(o.Y), o.V, o.Vf, o.Curve)),
            (o, d) => o.Y == d.Y && o.V == d.V && o.Vf == d.Vf && o.Curve == d.Curve,
            d => AddScrollSpeed(d.Clone()), RemoveScrollSpeed, (o, d) => ChangeScrollSpeed(o, d.Y, d.V, d.Vf, d.Curve), cmds);

        diff(chart.Beat.Stops, draft.Beat.Stops, draft,
            extrasMap == null ? null : (o =>
            {
                long y = extrasMap(o.Y);
                long len = extrasMap(o.Y + o.Length) - y;
                return new Stop(y, len > 0 ? len : o.Length);
            }),
            (o, d) => o.Y == d.Y && o.Length == d.Length,
            d => AddStop(d.Clone()), RemoveStop, (o, d) => ChangeStop(o, d.Y, d.Length), cmds);

        // ---- audio
        var bgm = chart.Audio.Bgm;
        if (draft.OffsetMs != bgm.OffsetMs) cmds.Add(SetOffset(draft.OffsetMs));

        long preview = draft.PreviewOffsetMs;
        if (preview == bgm.PreviewOffsetMs && tempoChanged)
        {
            // the preview window follows the grid like the extras, or keeps its time (unchanged)
            if (options.ScaleNotes && moved)
                preview = Math.Max(0, (long)Math.Round(newMap.PulseToMs(extrasMap!((long)Math.Round(oldMap.MsToPulse(preview))))));
            else if (options.MoveExtrasWithGrid)
                preview = Math.Max(0, (long)Math.Round(newMap.PulseToMs(oldMap.MsToPulse(preview))));
        }

        if (preview != bgm.PreviewOffsetMs || draft.PreviewDurationMs != bgm.PreviewDurationMs)
            cmds.Add(SetPreview(preview, draft.PreviewDurationMs));
        if (draft.Volume != bgm.Volume) cmds.Add(SetBgmVolume(draft.Volume));

        // ---- objects
        if (noteMap != null)
            cmds.Add(new RemapPulsesCommand(options.ScaleNotes ? "Scale notes" : "Keep notes in time", noteMap, RetimeTargets.NoteObjects));
        if (extrasMap != null)
            cmds.Add(new RemapPulsesCommand("Move extras", extrasMap,
                RetimeTargets.Camera | RetimeTargets.EffectCurves | RetimeTargets.VoxExtras | RetimeTargets.Comments | RetimeTargets.Bookmarks));

        return cmds.Count == 0 ? null : cmds.Count == 1 ? cmds[0] : new CompositeCommand(description, cmds);
    }

    private static bool sameBpms(List<BpmChange> a, List<BpmChange> b)
        => a.Count == b.Count && a.OrderBy(x => x.Y).Zip(b.OrderBy(x => x.Y)).All(p => p.First.Y == p.Second.Y && p.First.Bpm == p.Second.Bpm);

    /// <summary>
    /// Original vs draft list: originals no draft point came from are removed, changed ones changed,
    /// new draft points added. <paramref name="follow"/> (optional) moves untouched originals.
    /// </summary>
    private static void diff<T>(List<T> originals, List<T> drafts, TimingDraft draft, Func<T, T>? follow,
                                Func<T, T, bool> equal, Func<T, IEditCommand> add, Func<T, IEditCommand> remove,
                                Func<T, T, IEditCommand> change, List<IEditCommand> cmds) where T : class
    {
        var kept = new HashSet<T>(ReferenceEqualityComparer.Instance);
        var changes = new List<IEditCommand>();
        var adds = new List<IEditCommand>();

        foreach (var d in drafts)
        {
            if (draft.OriginOf(d) is { } o && originals.Contains(o))
            {
                kept.Add(o);
                if (!equal(o, d)) changes.Add(change(o, d));
                else if (follow != null && follow(o) is var f && !equal(o, f)) changes.Add(change(o, f));
            }
            else
            {
                adds.Add(add(d));
            }
        }

        foreach (var o in originals.ToList())
            if (!kept.Contains(o)) cmds.Add(remove(o));
        cmds.AddRange(changes);
        cmds.AddRange(adds);
    }

    /// <summary>
    /// "Base scroll speed" (osu! slider velocity): the value at pulse 0. Changing it multiplies the
    /// whole graph so the relative changes stay; an empty graph gets a single point at 0.
    /// </summary>
    public static double BaseScrollSpeed(Chart chart) => Graph.ValueAt(chart.Beat.ScrollSpeed, 0, 1.0);

    public static IEditCommand? SetBaseScrollSpeed(Chart chart, double value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
        var graph = chart.Beat.ScrollSpeed;
        double current = BaseScrollSpeed(chart);
        if (Math.Abs(current - value) < 1e-12) return null;

        List<GraphPoint> next;
        if (graph.Count == 0 || current <= 0)
        {
            next = Graph.CloneAll(graph);
            next.RemoveAll(p => p.Y == 0);
            next.Insert(0, new GraphPoint(0, value));
        }
        else
        {
            double f = value / current;
            next = graph.Select(p => new GraphPoint(p.Y, p.V * f, p.Vf * f, p.Curve)).ToList();
        }

        return new SetScrollSpeedGraphCommand("Change base scroll speed", next);
    }
}

public enum RampShape
{
    Linear,
    Exponential,
}

/// <summary>
/// Scroll-speed line tools of the F6 "Property Transformer". Pure: each returns a new graph and never
/// mutates the input points (untouched points are passed through by reference). Wrap the result in a
/// <see cref="SetScrollSpeedGraphCommand"/>, or apply it to a <see cref="TimingDraft"/>.
/// </summary>
public static class ScrollSpeedTools
{
    /// <summary>Value of a ramp at fraction t (0..1). Exponential needs positive ends, otherwise it is linear.</summary>
    public static double RampValue(double v0, double v1, double t, RampShape shape)
        => shape == RampShape.Exponential && v0 > 0 && v1 > 0 ? v0 * Math.Pow(v1 / v0, t) : v0 + (v1 - v0) * t;

    /// <summary>
    /// A ramp from <paramref name="v0"/> at <paramref name="from"/> to <paramref name="v1"/> at
    /// <paramref name="to"/>, replacing the points in [from, to]. Points sit either on
    /// <paramref name="steps"/>+1 evenly spaced pulses, or (when <paramref name="grid"/> and
    /// <paramref name="snapDivisor"/> are given) on every snap tick. <paramref name="stepped"/> holds each
    /// value until the next point (instant jumps, like osu! green lines); otherwise the graph
    /// interpolates linearly between the points.
    /// </summary>
    public static List<GraphPoint> Ramp(IReadOnlyList<GraphPoint> graph, long from, long to, double v0, double v1, RampShape shape,
                                        int steps = 1, bool stepped = false, TimingMap? grid = null, int snapDivisor = 0)
    {
        if (to <= from) throw new ArgumentException("the ramp needs from < to");

        var ticks = new List<long>();
        if (grid != null && snapDivisor > 0)
        {
            ticks.Add(from);
            for (long p = grid.NextSnap(from, snapDivisor); p < to; p = grid.NextSnap(p, snapDivisor))
                ticks.Add(p);
            ticks.Add(to);
        }
        else
        {
            steps = Math.Max(1, steps);
            for (int k = 0; k <= steps; k++)
            {
                long p = from + (long)Math.Round((double)(to - from) * k / steps);
                if (ticks.Count == 0 || p > ticks[^1]) ticks.Add(p);
            }
        }

        double before = Graph.ValueAt(graph.Where(p => p.Y < from).ToList(), from, 1.0);
        var result = graph.Where(p => p.Y < from || p.Y > to).ToList();

        double prevLevel = before;
        foreach (long p in ticks)
        {
            double v = RampValue(v0, v1, (double)(p - from) / (to - from), shape);
            if (stepped)
            {
                result.Add(new GraphPoint(p, prevLevel, prevLevel == v ? null : v));
                prevLevel = v;
            }
            else
            {
                result.Add(new GraphPoint(p, v));
            }
        }

        return sorted(result);
    }

    /// <summary>Multiplies the values (and slam targets) of the points in [from, to].</summary>
    public static List<GraphPoint> Scale(IReadOnlyList<GraphPoint> graph, long from, long to, double factor)
        => graph.Select(p => p.Y >= from && p.Y <= to ? new GraphPoint(p.Y, p.V * factor, p.Vf * factor, p.Curve) : p).ToList();

    /// <summary>
    /// Moves the points in [from, to] by <paramref name="delta"/> pulses. A moved point replaces an
    /// unmoved one on the same pulse.
    /// </summary>
    public static List<GraphPoint> Shift(IReadOnlyList<GraphPoint> graph, long from, long to, long delta)
    {
        var moved = graph.Where(p => p.Y >= from && p.Y <= to).Select(p => new GraphPoint(p.Y + delta, p.V, p.Vf, p.Curve)).ToList();
        var targets = moved.Select(p => p.Y).ToHashSet();
        var rest = graph.Where(p => (p.Y < from || p.Y > to) && !targets.Contains(p.Y));
        return sorted(rest.Concat(moved));
    }

    /// <summary>The points in [from, to] relative to <paramref name="from"/> (clipboard content).</summary>
    public static List<GraphPoint> Copy(IReadOnlyList<GraphPoint> graph, long from, long to)
        => graph.Where(p => p.Y >= from && p.Y <= to).Select(p => new GraphPoint(p.Y - from, p.V, p.Vf, p.Curve)).ToList();

    /// <summary>Pastes copied points at <paramref name="at"/>, replacing the points inside the pasted span.</summary>
    public static List<GraphPoint> Paste(IReadOnlyList<GraphPoint> graph, IReadOnlyList<GraphPoint> clip, long at)
    {
        if (clip.Count == 0) return graph.ToList();
        long span = clip.Max(p => p.Y);
        var rest = graph.Where(p => p.Y < at || p.Y > at + span);
        return sorted(rest.Concat(clip.Select(p => new GraphPoint(at + p.Y, p.V, p.Vf, p.Curve))));
    }

    private static List<GraphPoint> sorted(IEnumerable<GraphPoint> points) => points.OrderBy(p => p.Y).ToList();
}
