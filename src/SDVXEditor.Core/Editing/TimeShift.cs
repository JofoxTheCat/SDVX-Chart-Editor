using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// K-Shoot Editor's "Insert Blank" and "Delete Selected Range" in the pulse model: everything at or after a pulse moves
/// later / earlier, the audio does not. Built on <see cref="RemapPulsesCommand"/> (every chart part, bookmarks
/// included) plus the tempo map, so each operation is one undo step.
/// <list type="bullet">
/// <item><b>Insert</b> <c>length</c> at <c>at</c>: objects starting at or after <c>at</c> move by <c>length</c>; holds,
/// laser sections and graph segments that span <c>at</c> get longer. BPM points after <c>at</c> move, one exactly on
/// <c>at</c> stays (the blank plays at the tempo of the cursor).</item>
/// <item><b>Delete</b> [<c>from</c>, <c>to</c>): notes starting inside and laser sections lying entirely inside are
/// removed, everything after moves back by <c>to − from</c>; holds and lasers crossing the range are shortened (a
/// laser crossing it gets a slam at <c>from</c>, graphs a jump). The tempo that was in effect at <c>to</c> continues
/// at <c>from</c>.</item>
/// <item><b>Time signatures</b> are per measure: they move only when the inserted / deleted time is a whole number of
/// measures starting on a bar line (<see cref="TimeShiftResult.SignaturesMoved"/>). Otherwise they stay where they
/// are, and the content after the cursor moves against the bar lines — what "insert half a beat" means.</item>
/// </list>
/// </summary>
public static class TimeShift
{
    public static TimeShiftResult? InsertBlank(Chart chart, long at, long length)
    {
        if (length <= 0 || at < 0) return null;
        var timing = TimingMap.FromChart(chart);
        long Map(long y) => y >= at ? y + length : y;

        int? measures = WholeMeasures(timing, at, length);
        var cmds = new List<IEditCommand>
        {
            new RemapPulsesCommand("Insert blank time", Map, RetimeTargets.All),
            new TempoShiftCommand(at, length, null, measures is { } k ? (timing.MeasureAt(at), k) : null),
        };
        return new TimeShiftResult(new CompositeCommand($"Insert blank time ({length} pulses)", cmds), measures != null, 0);
    }

    public static TimeShiftResult? DeleteRange(Chart chart, long from, long to)
    {
        if (to <= from || from < 0) return null;
        long length = to - from;
        var timing = TimingMap.FromChart(chart);
        long Map(long y) => y < from ? y : y < to ? from : y - length;

        var cmds = new List<IEditCommand>();
        var notes = new List<NoteRef>();
        foreach (var kind in new[] { NoteKind.Bt, NoteKind.Fx })
            for (int lane = 0; lane < NoteData.LaneCount(kind); lane++)
                notes.AddRange(chart.Notes.Lane(kind, lane).Where(n => n.Y >= from && n.Y < to).Select(n => new NoteRef(kind, lane, n)));
        if (notes.Count > 0) cmds.Add(new RemoveNotesCommand(notes, "Delete notes in range"));

        int removed = notes.Count;
        for (int lane = 0; lane < 2; lane++)
        {
            foreach (var s in chart.Notes.Lasers[lane].Where(s => s.Y >= from && s.EndY <= to).ToList())
            {
                cmds.Add(new RemoveLaserSectionCommand(lane, s));
                removed++;
            }
        }

        // bookmarks inside the range would all land on `from`; keep the one already there (or the first)
        var marks = chart.Editor.Bookmarks.Where(b => b.Y > from && b.Y <= to).ToList();
        if (chart.Editor.Bookmarks.All(b => b.Y != from) && marks.Count > 0 && marks[0].Y < to) marks.RemoveAt(0);
        foreach (var b in marks)
            cmds.Add(new RemoveItemCommand<PulseValue<Bookmark>>("Delete bookmark", c => c.Editor.Bookmarks, b));

        int? measures = WholeMeasures(timing, from, length);
        cmds.Add(new RemapPulsesCommand("Delete range", Map, RetimeTargets.All));
        cmds.Add(new TempoShiftCommand(from, length, timing.BpmAt(to), measures is { } k ? (timing.MeasureAt(from), k) : null, delete: true));
        return new TimeShiftResult(new CompositeCommand($"Delete range ({length} pulses)", cmds), measures != null, removed);
    }

    /// <summary>The number of whole measures [<paramref name="at"/>, at + length) covers when <paramref name="at"/> is a bar line, else null.</summary>
    public static int? WholeMeasures(TimingMap timing, long at, long length)
    {
        int m = timing.MeasureAt(at);
        if (timing.MeasureStart(m) != at) return null;
        long y = at;
        int k = 0;
        while (y < at + length)
        {
            y += timing.MeasureLength(m + k);
            k++;
        }

        return y == at + length ? k : null;
    }
}

/// <summary>The command of a time shift, whether time signatures moved, and how many objects a delete removed.</summary>
public sealed record TimeShiftResult(IEditCommand Command, bool SignaturesMoved, int RemovedObjects);

/// <summary>
/// The tempo-map half of <see cref="TimeShift"/>: BPM points and (for whole measures) time signatures. Plans on the
/// first execute and keeps the point objects, so undo restores the exact list.
/// </summary>
public sealed class TempoShiftCommand : IEditCommand
{
    private readonly long at;
    private readonly long length;
    private readonly double? bpmAfter;
    private readonly (int Measure, int Count)? measures;
    private readonly bool delete;

    private List<BpmChange>? oldBpm, newBpm;
    private List<(BpmChange Point, long Old, long New)>? bpmMoves;
    private List<TimeSignature>? oldSigs, newSigs;
    private List<(TimeSignature Sig, int Old, int New)>? sigMoves;

    /// <param name="bpmAfter">Delete only: the tempo in effect at the range end, kept at the range start.</param>
    public TempoShiftCommand(long at, long length, double? bpmAfter, (int Measure, int Count)? measures, bool delete = false)
    {
        this.at = at;
        this.length = length;
        this.bpmAfter = bpmAfter;
        this.measures = measures;
        this.delete = delete;
    }

    public string Description => delete ? "Delete range (tempo)" : "Insert blank time (tempo)";

    public void Execute(Chart chart)
    {
        if (oldBpm == null) plan(chart);
        chart.Beat.Bpm.Clear();
        chart.Beat.Bpm.AddRange(newBpm!);
        foreach (var (p, _, n) in bpmMoves!) p.Y = n;
        chart.Beat.TimeSignatures.Clear();
        chart.Beat.TimeSignatures.AddRange(newSigs!);
        foreach (var (s, _, n) in sigMoves!) s.MeasureIndex = n;
    }

    public void Undo(Chart chart)
    {
        foreach (var (p, o, _) in bpmMoves!) p.Y = o;
        chart.Beat.Bpm.Clear();
        chart.Beat.Bpm.AddRange(oldBpm!);
        foreach (var (s, o, _) in sigMoves!) s.MeasureIndex = o;
        chart.Beat.TimeSignatures.Clear();
        chart.Beat.TimeSignatures.AddRange(oldSigs!);
    }

    private void plan(Chart chart)
    {
        oldBpm = chart.Beat.Bpm.ToList();
        oldSigs = chart.Beat.TimeSignatures.ToList();
        bpmMoves = new();
        sigMoves = new();
        newBpm = new();
        newSigs = new();

        if (!delete)
        {
            foreach (var b in oldBpm)
            {
                if (b.Y > at) bpmMoves.Add((b, b.Y, b.Y + length));
                newBpm.Add(b);
            }

            foreach (var s in oldSigs)
            {
                if (measures is { } m && s.MeasureIndex > m.Measure) sigMoves.Add((s, s.MeasureIndex, s.MeasureIndex + m.Count));
                newSigs.Add(s);
            }

            return;
        }

        long to = at + length;
        var timing = new TimingMap(chart.Beat);
        double before = at > 0 ? timing.BpmAt(at - 1) : double.NaN;
        foreach (var b in oldBpm)
        {
            if (b.Y >= at && b.Y < to) continue; // inside the deleted range
            if (b.Y >= to) bpmMoves.Add((b, b.Y, b.Y - length));
            newBpm.Add(b);
        }

        // the tempo at the range end continues at its start (unless a point on `to` already says so)
        if (bpmAfter is { } after && !oldBpm.Any(b => b.Y == to) && (at == 0 || Math.Abs(after - before) > 1e-9))
        {
            int i = newBpm.FindIndex(b => b.Y > at);
            newBpm.Insert(i < 0 ? newBpm.Count : i, new BpmChange(at, after));
        }

        if (measures is not { } range)
        {
            newSigs.AddRange(oldSigs);
            return;
        }

        int mFrom = range.Measure, mTo = range.Measure + range.Count;
        var sigTo = timing.SignatureOfMeasure(mTo);
        var sigBefore = mFrom > 0 ? timing.SignatureOfMeasure(mFrom - 1) : null;
        foreach (var s in oldSigs)
        {
            if (s.MeasureIndex >= mFrom && s.MeasureIndex < mTo) continue;
            if (s.MeasureIndex >= mTo) sigMoves.Add((s, s.MeasureIndex, s.MeasureIndex - range.Count));
            newSigs.Add(s);
        }

        bool same = sigBefore != null && sigBefore.Numerator == sigTo.Numerator && sigBefore.Denominator == sigTo.Denominator;
        if (!oldSigs.Any(s => s.MeasureIndex == mTo) && !same)
        {
            int i = newSigs.FindIndex(s => s.MeasureIndex > mFrom);
            newSigs.Insert(i < 0 ? newSigs.Count : i, new TimeSignature(mFrom, sigTo.Numerator, sigTo.Denominator));
        }
    }
}
