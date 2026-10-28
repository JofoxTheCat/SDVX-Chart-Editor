using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

// Namespace deliberately not "SDVXEditor.Core.Difficulty": a namespace of that name would shadow the
// SDVXEditor.Core.Charts.Difficulty type for every file in SDVXEditor.Core.* (namespace members are
// looked up before using directives) and break e.g. KshReader.
namespace SDVXEditor.Core.LevelEstimation;

/// <summary>One 400 ms section of the strain graph. Values are the peak strain inside the section.</summary>
public readonly record struct StrainSection(
    double StartMs,
    long StartPulse,
    double Total,
    double Left,
    double Right,
    double Buttons,
    double Knobs);

/// <summary>Everything the estimator derives from a chart before calibration.</summary>
public sealed class ChartFeatureSet
{
    /// <summary>Named raw features (no transform applied), in a stable order.</summary>
    public required IReadOnlyList<KeyValuePair<string, double>> Features { get; init; }

    public required IReadOnlyList<StrainSection> Sections { get; init; }

    /// <summary>Seconds between the first and last playable event (at least 1).</summary>
    public double DurationSeconds { get; init; }

    /// <summary>Chord-merged playable events (buttons + laser events).</summary>
    public int EventCount { get; init; }

    public int NoteCount { get; init; }
    public int BpmChangeCount { get; init; }
    public double BpmMin { get; init; }
    public double BpmMax { get; init; }

    public bool IsEmpty => NoteCount == 0 && EventCount == 0;

    public double this[string name]
    {
        get
        {
            foreach (var kv in Features)
                if (kv.Key == name) return kv.Value;
            throw new KeyNotFoundException(name);
        }
    }

    public Dictionary<string, double> ToDictionary() => Features.ToDictionary(kv => kv.Key, kv => kv.Value);
}

/// <summary>
/// Feature extraction for level estimation, on the Core <see cref="Chart"/> (any source format).
/// One implementation, used by the editor and by the calibration tool.
/// <para>
/// Event model: 8 columns (BT A–D = 0–3, FX L/R = 4/5, knob L/R = 6/7). Button events are chips and
/// hold starts; knob events are laser section starts, slams and direction reversals. Events within
/// 2 ms merge into one chord. All times are seconds through the BPM map (stops do not move time).
/// </para>
/// <para>
/// Strain (osu!mania style): per chord, <c>overall = overall·ob^dt + 1 + 0.3·(|chord|−1)</c> and per
/// column <c>indiv[c] = indiv[c]·ib^dt_c + w_c</c>; the note strain is <c>max(indiv) + overall</c>.
/// The chart is cut into 400 ms sections that keep their peak; aggregates are the maximum, top 10 %
/// and top 25 % mean, mean, and the osu 0.9-weighted sum. A family of decay bases is computed and
/// the calibration picks the useful ones.
/// </para>
/// </summary>
public static class DifficultyFeatures
{
    public const double SectionSeconds = 0.4;
    public const double ChordEpsilonSeconds = 0.002;

    /// <summary>Individual (per column) decay bases per second.</summary>
    public static readonly double[] IndividualDecays = { 0.05, 0.125, 0.3 };

    /// <summary>Overall decay bases per second.</summary>
    public static readonly double[] OverallDecays = { 0.1, 0.3, 0.5 };

    /// <summary>Decay pair used for the per-hand streams and the strain graph.</summary>
    public const double GraphIndividualDecay = 0.125, GraphOverallDecay = 0.3;

    private const int Columns = 8;

    // ------------------------------------------------------------------ event model

    private readonly struct Chord
    {
        public readonly double T;
        public readonly int Mask;      // bit c = column c
        public readonly int SlamMask;  // knob columns whose event is a slam
        public readonly int NestedMask; // button columns hit while another button hold is active

        public Chord(double t, int mask, int slamMask, int nestedMask)
        {
            T = t; Mask = mask; SlamMask = slamMask; NestedMask = nestedMask;
        }

        public int Count => System.Numerics.BitOperations.PopCount((uint)Mask);
        public int ButtonCount => System.Numerics.BitOperations.PopCount((uint)(Mask & 0x3F));
    }

    private readonly record struct Ev(double T, int Col, bool Slam);

    private sealed class Model
    {
        public readonly List<(double T, int Col, double HoldSeconds)> Notes = new();
        public readonly List<(double Start, double End)> Holds = new();
        public readonly List<Ev> KnobEvents = new();
        public readonly List<(double Start, double End, int Lane, bool Wide)> LaserIntervals = new();
        public readonly List<(double T, double Travel, bool Slam)> LaserMoves = new(); // per segment end
        public int Slams, Reversals, Sections;
        public double Travel, WideTime, LaserTime;
        public int Bt, Fx, BtHolds, FxHolds;
    }

    public static ChartFeatureSet Extract(Chart chart)
    {
        var timing = new TimingMap(chart.Beat, 0);
        double Sec(double pulse) => timing.PulseToMs(pulse) / 1000.0;

        var m = new Model();

        // ---- buttons
        for (int lane = 0; lane < 4; lane++) AddButtons(m, chart.Notes.Bt[lane], lane, Sec, isFx: false);
        for (int lane = 0; lane < 2; lane++) AddButtons(m, chart.Notes.Fx[lane], 4 + lane, Sec, isFx: true);

        // ---- lasers
        for (int lane = 0; lane < 2; lane++)
            foreach (var section in chart.Notes.Lasers[lane])
                AddLaserSection(m, section, 6 + lane, Sec);

        var bpms = chart.Beat.Bpm.Where(b => b.Bpm > 0).Select(b => b.Bpm).DefaultIfEmpty(120).ToList();

        // ---- chords
        var events = new List<Ev>(m.Notes.Count + m.KnobEvents.Count);
        foreach (var n in m.Notes) events.Add(new Ev(n.T, n.Col, false));
        events.AddRange(m.KnobEvents);
        events.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Col.CompareTo(b.Col));

        var holdsByStart = m.Holds.OrderBy(h => h.Start).ToList();
        var chords = new List<Chord>();
        {
            int i = 0;
            while (i < events.Count)
            {
                double t0 = events[i].T;
                int mask = 0, slam = 0;
                int j = i;
                while (j < events.Count && events[j].T - t0 < ChordEpsilonSeconds)
                {
                    mask |= 1 << events[j].Col;
                    if (events[j].Slam) slam |= 1 << events[j].Col;
                    j++;
                }
                chords.Add(new Chord(t0, mask, slam, 0));
                i = j;
            }
        }
        chords = MarkNested(chords, m);

        if (events.Count == 0)
            return Empty(bpms);

        double start = events[0].T, end = events[^1].T;
        double active = Math.Max(end - start, 1.0);

        var f = new List<KeyValuePair<string, double>>();
        void Add(string name, double value) => f.Add(new(name, double.IsFinite(value) ? value : 0));

        // ---- counts and density
        Add("n_events", chords.Count);
        Add("n_notes", m.Notes.Count);
        Add("dur", active);
        Add("dens", chords.Count / active);
        Add("notesps", m.Notes.Count / active);

        var chordTimes = chords.Select(c => c.T).ToArray();
        var w1 = WindowCounts(chordTimes, 1.0);
        var w4 = WindowCounts(chordTimes, 4.0);
        var noteTimes = m.Notes.Select(n => n.T).OrderBy(t => t).ToArray();
        var nw1 = WindowCounts(noteTimes, 1.0);
        Add("pk1", w1.Length > 0 ? w1[^1] : 0);
        Add("p95_1", Pct(w1, 0.95));
        Add("p80_1", Pct(w1, 0.80));
        Add("p50_1", Pct(w1, 0.50));
        Add("pk4", (w4.Length > 0 ? w4[^1] : 0) / 4.0);
        Add("p90_4", Pct(w4, 0.90) / 4.0);
        Add("npk1", nw1.Length > 0 ? nw1[^1] : 0);

        // ---- holds, lasers
        double holdTime = m.Holds.Sum(h => h.End - h.Start);
        Add("hold_frac", holdTime / active);
        Add("laser_frac", m.LaserTime / active);
        Add("wide_frac", m.LaserTime > 0 ? m.WideTime / m.LaserTime : 0);
        Add("lsec_ps", m.Sections / active);
        Add("slam_ps", m.Slams / active);
        Add("travel_ps", m.Travel / active);
        Add("rev_ps", m.Reversals / active);
        Add("slams", m.Slams);
        Add("travel", m.Travel);
        Add("both_lasers_frac", BothLasersTime(m) / active);

        // Laser load peaks: knob travel inside 2 s windows, slams inside 2 s windows.
        var moves = m.LaserMoves.OrderBy(x => x.T).ToList();
        Add("travel_pk2", WindowPeakSum(moves.Select(x => (x.T, x.Travel)).ToList(), 2.0) / 2.0);
        Add("slam_pk2", WindowPeakSum(moves.Where(x => x.Slam).Select(x => (x.T, 1.0)).ToList(), 2.0));

        // ---- interaction
        Add("simult", Simultaneity(chords, m.LaserIntervals) / Math.Max(1, chords.Count));
        Add("ch2", chords.Count(c => c.ButtonCount >= 2) / (double)Math.Max(1, chords.Count));
        Add("ch3", chords.Count(c => c.ButtonCount >= 3) / (double)Math.Max(1, chords.Count));
        Add("nested_frac", chords.Count(c => c.NestedMask != 0) / (double)Math.Max(1, chords.Count));
        Add("fx_frac", m.Notes.Count > 0 ? m.Fx / (double)m.Notes.Count : 0);
        Add("jacks_ps", Jacks(m) / active);

        // ---- tempo
        Add("nbpm", bpms.Count);
        Add("bpm_max", bpms.Max());
        Add("bpm_min", bpms.Min());
        Add("bpm_ratio", bpms.Max() / bpms.Min());

        // ---- strain family (global stream, unit weights)
        foreach (double ib in IndividualDecays)
            foreach (double ob in OverallDecays)
            {
                var peaks = StrainPeaks(chords, ib, ob, StreamKind.All, weighted: false);
                AddAggregates(f, $"s_{Fmt(ib)}_{Fmt(ob)}", peaks.Values);
            }

        // ---- per-hand streams (weighted: FX 1.2, knob 1.5, slam 2.0, nested button ×1.25)
        var left = StrainPeaks(chords, GraphIndividualDecay, GraphOverallDecay, StreamKind.Left, weighted: true);
        var right = StrainPeaks(chords, GraphIndividualDecay, GraphOverallDecay, StreamKind.Right, weighted: true);
        var knobs = StrainPeaks(chords, GraphIndividualDecay, GraphOverallDecay, StreamKind.Knobs, weighted: true);
        var buttons = StrainPeaks(chords, GraphIndividualDecay, GraphOverallDecay, StreamKind.Buttons, weighted: true);
        double lT10 = TopMean(left.Values, 0.10), rT10 = TopMean(right.Values, 0.10);
        double lW = WeightedSum(left.Values), rW = WeightedSum(right.Values);
        Add("hand_max_t10", Math.Max(lT10, rT10));
        Add("hand_min_t10", Math.Min(lT10, rT10));
        Add("hand_max_w", Math.Max(lW, rW));
        Add("hand_min_w", Math.Min(lW, rW));
        Add("knob_t10", TopMean(knobs.Values, 0.10));
        Add("knob_w", WeightedSum(knobs.Values));
        Add("button_t10", TopMean(buttons.Values, 0.10));
        Add("button_w", WeightedSum(buttons.Values));

        // ---- graph
        var total = StrainPeaks(chords, GraphIndividualDecay, GraphOverallDecay, StreamKind.All, weighted: true);
        var sections = BuildSections(timing, chordTimes, total, left, right, buttons, knobs, end);

        return new ChartFeatureSet
        {
            Features = f,
            Sections = sections,
            DurationSeconds = active,
            EventCount = chords.Count,
            NoteCount = m.Notes.Count,
            BpmChangeCount = bpms.Count,
            BpmMin = bpms.Min(),
            BpmMax = bpms.Max(),
        };
    }

    /// <summary>Feature names in extraction order (from a tiny synthetic chart, so it never drifts).</summary>
    public static IReadOnlyList<string> FeatureNames { get; } = BuildFeatureNames();

    private static IReadOnlyList<string> BuildFeatureNames()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[1].Add(new Note(480));
        return Extract(c).Features.Select(kv => kv.Key).ToList();
    }

    private static ChartFeatureSet Empty(List<double> bpms) => new()
    {
        Features = Array.Empty<KeyValuePair<string, double>>(),
        Sections = Array.Empty<StrainSection>(),
        DurationSeconds = 0,
        EventCount = 0,
        NoteCount = 0,
        BpmChangeCount = bpms.Count,
        BpmMin = bpms.Min(),
        BpmMax = bpms.Max(),
    };

    private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ building the model

    private static void AddButtons(Model m, List<Note> lane, int col, Func<double, double> sec, bool isFx)
    {
        foreach (var n in lane)
        {
            double t = sec(n.Y);
            double hs = 0;
            if (n.IsHold)
            {
                hs = sec(n.EndY) - t;
                m.Holds.Add((t, t + hs));
            }
            m.Notes.Add((t, col, hs));
            if (isFx) { m.Fx++; if (n.IsHold) m.FxHolds++; }
            else { m.Bt++; if (n.IsHold) m.BtHolds++; }
        }
    }

    private static void AddLaserSection(Model m, LaserSection s, int col, Func<double, double> sec)
    {
        if (s.Points.Count == 0) return;
        var pts = NormalizeQuickMoves(s.Points).Select(p => (T: sec(s.Y + p.Ry), p.V, p.After, p.IsSlam)).ToList();
        double t0 = pts[0].T, t1 = pts[^1].T;
        m.Sections++;
        m.KnobEvents.Add(new Ev(t0, col, pts[0].IsSlam));
        m.LaserIntervals.Add((t0, t1, col - 6, s.Width == 2));
        m.LaserTime += t1 - t0;
        if (s.Width == 2) m.WideTime += t1 - t0;

        int lastDir = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            if (p.IsSlam)
            {
                double d = Math.Abs(p.After - p.V) * 1.5;
                m.Slams++;
                m.Travel += d;
                m.LaserMoves.Add((p.T, d, true));
                if (i > 0) m.KnobEvents.Add(new Ev(p.T, col, true));
                lastDir = 0;
            }
            if (i + 1 >= pts.Count) break;
            var q = pts[i + 1];
            double dv = q.V - p.After;
            m.Travel += Math.Abs(dv);
            if (dv != 0) m.LaserMoves.Add((q.T, Math.Abs(dv), false));
            int dir = dv > 1e-6 ? 1 : dv < -1e-6 ? -1 : 0;
            if (dir != 0 && lastDir != 0 && dir != lastDir)
            {
                m.Reversals++;
                m.KnobEvents.Add(new Ev(p.T, col, false));
            }
            if (dir != 0) lastDir = dir;
        }
    }

    /// <summary>Smallest value change of a 1/32-note movement that counts as a slam (see <see cref="NormalizeQuickMoves"/>).</summary>
    public const double QuickMoveSlamDelta = 0.25;

    /// <summary>
    /// Slams smaller than this count as plain movement. Introduced because the ksh reader used to turn
    /// densely sampled movement into runs of 0.02–0.06 "slams" (ABYSS MOTION: 29 its .kson twin does
    /// not have); the reader no longer does (KshReader.IsSlam), and no chart in the library has a slam
    /// below 0.1 any more. Kept anyway because the coefficients were fitted with it: in floating point
    /// a 5/50 ksh slam (e.g. 0.62 − 0.52 = 0.0999…) falls below 0.1, so it demotes 103 genuine 0.1 slams
    /// (AureoLe EXH 33, MXM 69, Aegleseeker 1); removing it moves AureoLe EXH from 20.2 to 20.5. Drop it
    /// together with the next recalibration.
    /// </summary>
    public const double MinSlamDelta = 0.1;

    /// <summary>
    /// A movement of at least <see cref="QuickMoveSlamDelta"/> within 1/32 note is played like a slam.
    /// ksh cannot tell the two apart (the reader collapses an isolated such pair into a slam, like
    /// ksh2vox), whereas kson and VOX keep them as separate points; folding them here makes the features
    /// of a .ksh and its .kson twin agree. Pairs are consumed two at a time, like the ksh reader. Slams
    /// below <see cref="MinSlamDelta"/> are demoted to plain movement.
    /// </summary>
    private static List<(long Ry, double V, double After, bool IsSlam)> NormalizeQuickMoves(List<LaserPoint> points)
    {
        var result = new List<(long, double, double, bool)>(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            var p = points[i];
            if (!p.IsSlam && i + 1 < points.Count)
            {
                var q = points[i + 1];
                if (!q.IsSlam && q.Ry - p.Ry <= Timebase.SlamMaxPulses && Math.Abs(q.V - p.V) >= QuickMoveSlamDelta)
                {
                    result.Add((p.Ry, p.V, q.V, true));
                    i++;
                    continue;
                }
            }
            // A tiny "slam" is a densely sampled curve that the ksh slam rule collapsed, not a slam.
            bool slam = p.IsSlam && Math.Abs(p.ValueAfter - p.V) >= MinSlamDelta;
            result.Add((p.Ry, p.V, p.ValueAfter, slam));
        }
        return result;
    }

    /// <summary>Marks button columns hit while a hold in another button column is active.</summary>
    private static List<Chord> MarkNested(List<Chord> chords, Model m)
    {
        if (m.Holds.Count == 0) return chords;
        var holds = m.Notes.Where(n => n.HoldSeconds > 0).Select(n => (Start: n.T, End: n.T + n.HoldSeconds, n.Col))
                           .OrderBy(h => h.Start).ToList();
        var result = new List<Chord>(chords.Count);
        var active = new List<(double Start, double End, int Col)>();
        int hi = 0;
        foreach (var c in chords)
        {
            while (hi < holds.Count && holds[hi].Start < c.T - ChordEpsilonSeconds) active.Add(holds[hi++]);
            active.RemoveAll(h => h.End <= c.T);
            int nested = 0;
            int buttons = c.Mask & 0x3F;
            if (buttons != 0)
                foreach (var h in active)
                    nested |= buttons & ~(1 << h.Col);
            result.Add(new Chord(c.T, c.Mask, c.SlamMask, nested));
        }
        return result;
    }

    // ------------------------------------------------------------------ strain

    private enum StreamKind { All, Left, Right, Buttons, Knobs }

    // Hands: left = BT A, BT B, FX L, knob L; right = BT C, BT D, FX R, knob R.
    private const int LeftMask = (1 << 0) | (1 << 1) | (1 << 4) | (1 << 6);
    private const int RightMask = (1 << 2) | (1 << 3) | (1 << 5) | (1 << 7);
    private const int ButtonMask = 0x3F;
    private const int KnobMask = 0xC0;

    private static int MaskOf(StreamKind k) => k switch
    {
        StreamKind.Left => LeftMask,
        StreamKind.Right => RightMask,
        StreamKind.Buttons => ButtonMask,
        StreamKind.Knobs => KnobMask,
        _ => 0xFF,
    };

    private static double ColumnWeight(int col, bool slam, bool nested, bool weighted)
    {
        if (!weighted) return 1.0;
        double w = col < 4 ? 1.0 : col < 6 ? 1.2 : slam ? 2.0 : 1.5;
        if (nested) w *= 1.25;
        return w;
    }

    /// <summary>Section index → peak strain, only for sections that contain an event of the stream.</summary>
    private static SortedDictionary<int, double> StrainPeaks(List<Chord> chords, double ib, double ob, StreamKind kind, bool weighted)
    {
        int streamMask = MaskOf(kind);
        var ind = new double[Columns];
        var last = new double[Columns];
        Array.Fill(last, double.NaN);
        double overall = 0, lastT = double.NaN;
        var peaks = new SortedDictionary<int, double>();
        foreach (var c in chords)
        {
            int mask = c.Mask & streamMask;
            if (mask == 0) continue;
            int count = System.Numerics.BitOperations.PopCount((uint)mask);
            if (!double.IsNaN(lastT)) overall *= Math.Pow(ob, c.T - lastT);
            overall += 1.0 + 0.3 * (count - 1);
            double best = 0;
            for (int col = 0; col < Columns; col++)
            {
                if ((mask & (1 << col)) == 0) continue;
                if (!double.IsNaN(last[col])) ind[col] *= Math.Pow(ib, c.T - last[col]);
                ind[col] += ColumnWeight(col, (c.SlamMask & (1 << col)) != 0, (c.NestedMask & (1 << col)) != 0, weighted);
                last[col] = c.T;
                best = Math.Max(best, ind[col]);
            }
            double s = best + overall;
            int k = (int)Math.Floor(c.T / SectionSeconds);
            if (!peaks.TryGetValue(k, out double prev) || s > prev) peaks[k] = s;
            lastT = c.T;
        }
        return peaks;
    }

    private static void AddAggregates(List<KeyValuePair<string, double>> f, string tag, IEnumerable<double> values)
    {
        var pk = values.OrderByDescending(v => v).ToList();
        f.Add(new(tag + "_max", pk.Count > 0 ? pk[0] : 0));
        f.Add(new(tag + "_t10", TopMean(pk, 0.10)));
        f.Add(new(tag + "_t25", TopMean(pk, 0.25)));
        f.Add(new(tag + "_mean", pk.Count > 0 ? pk.Average() : 0));
        f.Add(new(tag + "_w", WeightedSum(pk)));
    }

    /// <summary>Mean of the top fraction (at least one value).</summary>
    private static double TopMean(IEnumerable<double> values, double fraction)
    {
        var pk = values as List<double> ?? values.ToList();
        if (pk.Count == 0) return 0;
        var sorted = pk.OrderByDescending(v => v).ToList();
        int n = Math.Max(1, (int)(sorted.Count * fraction));
        return sorted.Take(n).Average();
    }

    /// <summary>osu! aggregation: peaks sorted descending, weight ×0.9 per rank.</summary>
    private static double WeightedSum(IEnumerable<double> values)
    {
        double w = 1, total = 0;
        foreach (double v in values.OrderByDescending(v => v))
        {
            total += v * w;
            w *= 0.9;
        }
        return total;
    }

    private static IReadOnlyList<StrainSection> BuildSections(
        TimingMap timing, double[] chordTimes,
        SortedDictionary<int, double> total, SortedDictionary<int, double> left, SortedDictionary<int, double> right,
        SortedDictionary<int, double> buttons, SortedDictionary<int, double> knobs, double endSeconds)
    {
        if (chordTimes.Length == 0) return Array.Empty<StrainSection>();
        int first = Math.Min(0, (int)Math.Floor(chordTimes[0] / SectionSeconds));
        int lastK = (int)Math.Floor(endSeconds / SectionSeconds);
        var list = new List<StrainSection>(lastK - first + 1);
        static double Get(SortedDictionary<int, double> d, int k) => d.TryGetValue(k, out double v) ? v : 0;
        for (int k = first; k <= lastK; k++)
        {
            double ms = k * SectionSeconds * 1000.0;
            list.Add(new StrainSection(ms, (long)Math.Round(timing.MsToPulse(ms)),
                Get(total, k), Get(left, k), Get(right, k), Get(buttons, k), Get(knobs, k)));
        }
        return list;
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>For each event i, the number of events in (t_i − w, t_i]; returned sorted ascending.</summary>
    private static double[] WindowCounts(double[] sortedTimes, double w)
    {
        var vals = new double[sortedTimes.Length];
        int j = 0;
        for (int i = 0; i < sortedTimes.Length; i++)
        {
            while (sortedTimes[j] < sortedTimes[i] - w) j++;
            vals[i] = i - j + 1;
        }
        Array.Sort(vals);
        return vals;
    }

    private static double Pct(double[] sorted, double p)
        => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(p * sorted.Length))];

    private static double WindowPeakSum(List<(double T, double V)> sorted, double w)
    {
        double best = 0, sum = 0;
        int j = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            sum += sorted[i].V;
            while (sorted[j].T < sorted[i].T - w) sum -= sorted[j++].V;
            best = Math.Max(best, sum);
        }
        return best;
    }

    private static double BothLasersTime(Model m)
    {
        var l = MergeIntervals(m.LaserIntervals.Where(x => x.Lane == 0).Select(x => (x.Start, x.End)));
        var r = MergeIntervals(m.LaserIntervals.Where(x => x.Lane == 1).Select(x => (x.Start, x.End)));
        double total = 0;
        int i = 0, j = 0;
        while (i < l.Count && j < r.Count)
        {
            double a = Math.Max(l[i].Start, r[j].Start), b = Math.Min(l[i].End, r[j].End);
            if (b > a) total += b - a;
            if (l[i].End < r[j].End) i++; else j++;
        }
        return total;
    }

    private static List<(double Start, double End)> MergeIntervals(IEnumerable<(double Start, double End)> source)
    {
        var list = new List<(double Start, double End)>();
        foreach (var iv in source.OrderBy(x => x.Start))
        {
            if (list.Count > 0 && iv.Start <= list[^1].End) list[^1] = (list[^1].Start, Math.Max(list[^1].End, iv.End));
            else list.Add(iv);
        }
        return list;
    }

    /// <summary>Chords that fall inside (±50 ms) any laser section.</summary>
    private static int Simultaneity(List<Chord> chords, List<(double Start, double End, int Lane, bool Wide)> lasers)
    {
        var merged = MergeIntervals(lasers.Select(x => (x.Start - 0.05, x.End + 0.05)));
        int count = 0, k = 0;
        foreach (var c in chords)
        {
            while (k < merged.Count && merged[k].End < c.T) k++;
            if (k < merged.Count && merged[k].Start <= c.T) count++;
        }
        return count;
    }

    /// <summary>Same button column hit again within 150 ms.</summary>
    private static int Jacks(Model m)
    {
        var last = new double[6];
        Array.Fill(last, double.NegativeInfinity);
        int jacks = 0;
        foreach (var n in m.Notes.OrderBy(n => n.T))
        {
            if (n.T - last[n.Col] < 0.15) jacks++;
            last[n.Col] = n.T;
        }
        return jacks;
    }
}
