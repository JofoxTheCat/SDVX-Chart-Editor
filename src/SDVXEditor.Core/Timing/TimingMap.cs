using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Timing;

/// <summary>Measure / beat / offset of a pulse. Measure and beat are 0-based; the beat is the time-signature denominator note.</summary>
public readonly record struct MusicalPosition(int Measure, int Beat, long PulseInBeat);

/// <summary>
/// Immutable tempo and grid map of a chart.
/// <list type="bullet">
/// <item>pulse ↔ audio milliseconds from BPM changes plus the audio offset;</item>
/// <item>measure / beat lookup from time signatures;</item>
/// <item>snap helpers;</item>
/// <item>scroll integration (scroll_speed graph and stops) for renderers.</item>
/// </list>
/// Stops follow kson semantics ("equivalent to scroll_speed 0 for a duration"): they freeze the
/// visual note stream but do not move audio time, so they only affect <see cref="VisualPosition"/>.
/// Build a new map after editing timing data.
/// </summary>
public sealed class TimingMap
{
    /// <summary>
    /// Snap divisors of one beat offered by the editor. Up to 1/48 every grid line is a whole VOX tick; 1/64, 1/96 and
    /// 1/192 are finer than kson's 240 pulses per beat allows exactly, so their grid lines are rounded to the nearest
    /// pulse (see <see cref="Snap"/>).
    /// </summary>
    public static readonly IReadOnlyList<int> SnapDivisors = new[] { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 192 };

    private readonly long[] bpmY;
    private readonly double[] bpmValue;
    private readonly double[] bpmStartMs; // ms (without offset) at each bpm change

    private readonly int[] sigMeasure;
    private readonly int[] sigNum;
    private readonly int[] sigDen;
    private readonly long[] sigStartPulse;

    private readonly List<GraphPoint> scroll;
    private readonly double[] breakY;      // scroll integration breakpoints
    private readonly double[] breakCum;    // visual position at each breakpoint
    private readonly (long Start, long End)[] stops;

    public double OffsetMs { get; }

    public TimingMap(BeatInfo beat, double offsetMs = 0)
    {
        OffsetMs = offsetMs;

        var bpms = beat.Bpm.OrderBy(b => b.Y).Where(b => b.Bpm > 0).ToList();
        if (bpms.Count == 0 || bpms[0].Y > 0)
            bpms.Insert(0, new BpmChange(0, bpms.Count > 0 ? bpms[0].Bpm : 120));
        // Last value wins on duplicate pulses.
        bpms = bpms.GroupBy(b => b.Y).Select(g => g.Last()).ToList();
        bpmY = bpms.Select(b => b.Y).ToArray();
        bpmValue = bpms.Select(b => b.Bpm).ToArray();
        bpmStartMs = new double[bpmY.Length];
        for (int i = 1; i < bpmY.Length; i++)
            bpmStartMs[i] = bpmStartMs[i - 1] + PulsesToMs(bpmY[i] - bpmY[i - 1], bpmValue[i - 1]);

        var sigs = beat.TimeSignatures.Where(s => s.Numerator > 0 && s.Denominator > 0)
                       .OrderBy(s => s.MeasureIndex).GroupBy(s => s.MeasureIndex).Select(g => g.Last()).ToList();
        if (sigs.Count == 0 || sigs[0].MeasureIndex > 0)
            sigs.Insert(0, new TimeSignature(0, 4, 4));
        sigMeasure = sigs.Select(s => s.MeasureIndex).ToArray();
        sigNum = sigs.Select(s => s.Numerator).ToArray();
        sigDen = sigs.Select(s => s.Denominator).ToArray();
        sigStartPulse = new long[sigs.Count];
        for (int i = 1; i < sigs.Count; i++)
            sigStartPulse[i] = sigStartPulse[i - 1]
                               + (sigMeasure[i] - sigMeasure[i - 1]) * Timebase.MeasureLength(sigNum[i - 1], sigDen[i - 1]);

        scroll = beat.ScrollSpeed.OrderBy(p => p.Y).ToList();
        stops = MergeStops(beat.Stops);
        (breakY, breakCum) = BuildScrollTable();
    }

    public static TimingMap FromChart(Chart chart) => new(chart.Beat, chart.Audio.Bgm.OffsetMs);

    private static double PulsesToMs(double pulses, double bpm) => pulses * 60000.0 / (bpm * Timebase.PulsesPerQuarter);

    // ---------------------------------------------------------------- tempo

    public double BpmAt(double pulse) => bpmValue[BpmIndex(pulse)];

    private int BpmIndex(double pulse)
    {
        int i = Array.BinarySearch(bpmY, (long)Math.Floor(pulse));
        if (i < 0) i = ~i - 1;
        return Math.Max(0, i);
    }

    /// <summary>
    /// The BPM the chart spends the most time in between pulse 0 and <paramref name="endPulse"/> (summed per tempo
    /// value, audio time; stops do not count). A tie goes to the tempo heard first. With no range (end ≤ 0) it is the
    /// tempo at pulse 0. This is the hi-speed basis of unnamed-sdvx-clone's M-mod (<c>Beatmap::GetModeBPM</c>, which
    /// sums from the audio offset to the last object).
    /// </summary>
    public double ModeBpm(double endPulse)
    {
        if (endPulse <= 0) return bpmValue[0];
        var total = new Dictionary<double, double>();
        var order = new List<double>();
        for (int i = 0; i < bpmY.Length && bpmY[i] < endPulse; i++)
        {
            double to = i + 1 < bpmY.Length ? Math.Min(bpmY[i + 1], endPulse) : endPulse;
            double ms = PulsesToMs(to - bpmY[i], bpmValue[i]);
            if (!total.ContainsKey(bpmValue[i])) order.Add(bpmValue[i]);
            total[bpmValue[i]] = total.GetValueOrDefault(bpmValue[i]) + ms;
        }

        double best = bpmValue[0], bestMs = -1;
        foreach (double bpm in order)
        {
            if (total[bpm] > bestMs)
            {
                best = bpm;
                bestMs = total[bpm];
            }
        }

        return best;
    }

    /// <summary>Audio time in ms of a pulse, including the audio offset. Extrapolates outside the chart.</summary>
    public double PulseToMs(double pulse)
    {
        int i = BpmIndex(pulse);
        return OffsetMs + bpmStartMs[i] + PulsesToMs(pulse - bpmY[i], bpmValue[i]);
    }

    /// <summary>Inverse of <see cref="PulseToMs"/>; fractional pulses.</summary>
    public double MsToPulse(double ms)
    {
        double t = ms - OffsetMs;
        int lo = 0, hi = bpmStartMs.Length - 1, i = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (bpmStartMs[mid] <= t) { i = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return bpmY[i] + (t - bpmStartMs[i]) * bpmValue[i] * Timebase.PulsesPerQuarter / 60000.0;
    }

    // ---------------------------------------------------------------- measures

    private int SigIndexForMeasure(int measure)
    {
        int i = Array.BinarySearch(sigMeasure, measure);
        if (i < 0) i = ~i - 1;
        return Math.Max(0, i);
    }

    private int SigIndexForPulse(long pulse)
    {
        int i = Array.BinarySearch(sigStartPulse, pulse);
        if (i < 0) i = ~i - 1;
        return Math.Max(0, i);
    }

    public TimeSignature SignatureOfMeasure(int measure)
    {
        int i = SigIndexForMeasure(measure);
        return new TimeSignature(measure, sigNum[i], sigDen[i]);
    }

    public long MeasureLength(int measure)
    {
        int i = SigIndexForMeasure(measure);
        return Timebase.MeasureLength(sigNum[i], sigDen[i]);
    }

    public long BeatLength(int measure) => Timebase.BeatLength(sigDen[SigIndexForMeasure(measure)]);

    public long MeasureStart(int measure)
    {
        if (measure <= 0) return measure * Timebase.MeasureLength(sigNum[0], sigDen[0]);
        int i = SigIndexForMeasure(measure);
        return sigStartPulse[i] + (measure - sigMeasure[i]) * Timebase.MeasureLength(sigNum[i], sigDen[i]);
    }

    /// <summary>0-based measure containing <paramref name="pulse"/>.</summary>
    public int MeasureAt(long pulse)
    {
        if (pulse < 0) return (int)Math.Floor((double)pulse / Timebase.MeasureLength(sigNum[0], sigDen[0]));
        int i = SigIndexForPulse(pulse);
        long len = Timebase.MeasureLength(sigNum[i], sigDen[i]);
        return sigMeasure[i] + (int)((pulse - sigStartPulse[i]) / len);
    }

    public MusicalPosition PositionOf(long pulse)
    {
        int m = MeasureAt(pulse);
        long within = pulse - MeasureStart(m);
        long beatLen = BeatLength(m);
        return new MusicalPosition(m, (int)(within / beatLen), within % beatLen);
    }

    public long ToPulse(int measure, int beat, long pulseInBeat) => MeasureStart(measure) + beat * BeatLength(measure) + pulseInBeat;

    /// <summary>Measure start pulses from 0 up to and including <paramref name="endPulse"/> (bar lines for a renderer).</summary>
    public IEnumerable<long> BarLines(long endPulse)
    {
        for (int m = 0; ; m++)
        {
            long p = MeasureStart(m);
            if (p > endPulse) yield break;
            yield return p;
        }
    }

    // ---------------------------------------------------------------- snapping

    /// <summary>
    /// Nearest grid line for 1/<paramref name="divisor"/> of the measure's beat, anchored at the measure
    /// start (every bar line is a grid line). Grid lines that fall between pulses (1/32 of a quarter =
    /// 7.5 pulses) are rounded to the nearest pulse.
    /// </summary>
    public long Snap(long pulse, int divisor)
    {
        long prev = pulse, next = pulse;
        if (!IsGridLine(pulse, divisor))
        {
            prev = PreviousSnap(pulse, divisor);
            next = NextSnap(pulse, divisor);
        }
        return pulse - prev < next - pulse ? prev : next;
    }

    /// <summary>The first grid line strictly after <paramref name="pulse"/>.</summary>
    public long NextSnap(long pulse, int divisor)
    {
        var (start, end, beatLen) = MeasureFrame(pulse, divisor);
        long k = (long)Math.Floor((double)(pulse - start) * divisor / beatLen);
        for (; ; k++)
        {
            long line = start + GridOffset(k, beatLen, divisor);
            if (line >= end) return end;
            if (line > pulse) return line;
        }
    }

    /// <summary>The last grid line strictly before <paramref name="pulse"/>.</summary>
    public long PreviousSnap(long pulse, int divisor)
    {
        var (start, _, beatLen) = MeasureFrame(pulse, divisor);
        if (pulse == start)
        {
            // last line of the previous measure
            var (ps, pe, pb) = MeasureFrame(pulse - 1, divisor);
            long kk = (long)Math.Ceiling((double)(pe - ps) * divisor / pb);
            for (; kk >= 0; kk--)
            {
                long line = ps + GridOffset(kk, pb, divisor);
                if (line < pulse) return line;
            }
            return ps;
        }
        long k = (long)Math.Ceiling((double)(pulse - start) * divisor / beatLen);
        for (; k >= 0; k--)
        {
            long line = start + GridOffset(k, beatLen, divisor);
            if (line < pulse) return line;
        }
        return start;
    }

    public bool IsGridLine(long pulse, int divisor)
    {
        var (start, _, beatLen) = MeasureFrame(pulse, divisor);
        long k = (long)Math.Round((double)(pulse - start) * divisor / beatLen);
        return start + GridOffset(k, beatLen, divisor) == pulse;
    }

    private (long Start, long End, long BeatLen) MeasureFrame(long pulse, int divisor)
    {
        if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
        int m = MeasureAt(pulse);
        long start = MeasureStart(m);
        return (start, start + MeasureLength(m), BeatLength(m));
    }

    private static long GridOffset(long k, long beatLen, int divisor)
        => (long)Math.Round((double)k * beatLen / divisor, MidpointRounding.AwayFromZero);

    // ---------------------------------------------------------------- scroll

    /// <summary>Scroll speed multiplier at a pulse (0 inside a stop).</summary>
    public double ScrollSpeedAt(double pulse) => InStop(pulse) ? 0 : Graph.ValueAt(scroll, pulse, 1.0);

    /// <summary>
    /// Visual position of a pulse: ∫ scroll speed d(pulse), stops contributing 0. In pulses at speed 1,
    /// so a renderer draws a note at (VisualPosition(note) − VisualPosition(now)) × pixelsPerPulse.
    /// Measured in beats rather than time, as SDVX scrolls a constant distance per beat.
    /// </summary>
    public double VisualPosition(double pulse)
    {
        if (pulse <= breakY[0])
            return breakCum[0] - (breakY[0] - pulse) * BaseSpeed(pulse);
        int i = Array.BinarySearch(breakY, pulse);
        if (i >= 0) return breakCum[i];
        i = ~i - 1;
        return breakCum[i] + Integrate(breakY[i], pulse);
    }

    private static (long, long)[] MergeStops(IEnumerable<Stop> source)
    {
        var list = new List<(long Start, long End)>();
        foreach (var s in source.Where(s => s.Length > 0).OrderBy(s => s.Y))
        {
            if (list.Count > 0 && s.Y <= list[^1].End)
                list[^1] = (list[^1].Start, Math.Max(list[^1].End, s.Y + s.Length));
            else
                list.Add((s.Y, s.Y + s.Length));
        }
        return list.ToArray();
    }

    private bool InStop(double pulse)
    {
        foreach (var (start, end) in stops)
        {
            if (pulse < start) return false;
            if (pulse < end) return true;
        }
        return false;
    }

    private double BaseSpeed(double pulse) => Graph.ValueAt(scroll, pulse, 1.0);

    private (double[], double[]) BuildScrollTable()
    {
        var pts = new SortedSet<double> { 0 };
        foreach (var p in scroll) pts.Add(p.Y);
        foreach (var (s, e) in stops) { pts.Add(s); pts.Add(e); }
        var ys = pts.ToArray();
        var cum = new double[ys.Length];
        // Anchor: visual position 0 at pulse 0.
        int zero = Array.IndexOf(ys, 0.0);
        for (int i = zero + 1; i < ys.Length; i++) cum[i] = cum[i - 1] + Integrate(ys[i - 1], ys[i]);
        for (int i = zero - 1; i >= 0; i--) cum[i] = cum[i + 1] - Integrate(ys[i], ys[i + 1]);
        return (ys, cum);
    }

    // Integral over [a, b] where no breakpoint lies strictly inside.
    private double Integrate(double a, double b)
    {
        if (b <= a) return 0;
        double mid = (a + b) / 2;
        if (InStop(mid)) return 0;
        if (scroll.Count == 0) return b - a;
        if (mid < scroll[0].Y) return scroll[0].V * (b - a);
        int i = Graph.LastIndexAtOrBefore(scroll, mid);
        var p = scroll[i];
        if (i == scroll.Count - 1) return p.ValueAfter * (b - a);
        var q = scroll[i + 1];
        if (p.Curve is not { IsLinear: false })
            return (Graph.Interpolate(p, q, a) + Graph.Interpolate(p, q, b)) / 2 * (b - a);
        // Curved segment: composite Simpson.
        const int n = 64;
        double h = (b - a) / n, sum = Graph.Interpolate(p, q, a) + Graph.Interpolate(p, q, b);
        for (int k = 1; k < n; k++) sum += (k % 2 == 1 ? 4 : 2) * Graph.Interpolate(p, q, a + k * h);
        return sum * h / 3;
    }
}
