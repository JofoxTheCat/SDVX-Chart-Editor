using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Patterns;

/// <summary>Named, ordered pattern features of one chart (raw values, no transform).</summary>
public sealed class PatternFeatureSet
{
    public required IReadOnlyList<KeyValuePair<string, double>> Features { get; init; }

    /// <summary>Chart length in seconds (<see cref="PatternChart.Duration"/>).</summary>
    public double Duration { get; init; }

    public int NoteCount { get; init; }
    public int LaserSectionCount { get; init; }

    public bool IsEmpty => NoteCount == 0 && LaserSectionCount == 0;

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
/// Pattern analysis: time-aware features of a chart for the skill-radar model and the level model. One
/// implementation for the editor and the calibrator. Every feature is specified in <c>docs/RADAR_MODEL.md</c>
/// (§2) precisely enough to port; the order of <see cref="FeatureNames"/> is part of that spec.
/// <para>
/// Conventions: times are seconds through the BPM map (<see cref="PatternChart"/>), "per second" divides by
/// <see cref="PatternChart.Duration"/>, sliding windows are half-open <c>(t − w, t]</c> evaluated at every event
/// time, chords are notes with the same pulse, hands follow <see cref="HandModel"/>.
/// </para>
/// </summary>
public static class PatternAnalysis
{
    /// <summary>Same-lane repeat no further apart than this is a jack (s), as in radar.py.</summary>
    public const double JackGap = 0.12;

    /// <summary>Largest gap inside a trill / stream (s).</summary>
    public const double StreamGap = 0.15;

    /// <summary>Gap between chords that counts as a burst (s; a 1/16 at ~214 BPM).</summary>
    public const double BurstGap = 0.07;

    /// <summary>Reversal needed before a laser direction change counts (value units 0..1).</summary>
    public const double DirectionHysteresis = 0.05;

    /// <summary>A slam this close to a note (pulses, a 1/16 note) makes the knob "moving" at that note.</summary>
    public const long SlamNearPulses = 60;

    private const double Eps = 1e-6;

    /// <summary>Feature names in extraction order (from a tiny synthetic chart, so it never drifts).</summary>
    public static IReadOnlyList<string> FeatureNames { get; } = BuildNames();

    private static IReadOnlyList<string> BuildNames()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(0));
        return Analyze(c).Features.Select(kv => kv.Key).ToList();
    }

    public static PatternFeatureSet Analyze(Chart chart, HandModel? hands = null)
        => Analyze(PatternChart.From(chart), hands);

    public static PatternFeatureSet Analyze(PatternChart pc, HandModel? hands = null)
    {
        hands ??= HandModel.Default;
        var f = new List<KeyValuePair<string, double>>(96);
        void Add(string name, double v) => f.Add(new(name, double.IsFinite(v) ? v : 0));

        var notes = pc.Notes;
        double dur = pc.Duration;
        double perSec(double count) => count / dur;

        // ------------------------------------------------------------ 1. volume
        int chips = 0, holds = 0, btChips = 0, fxChips = 0, btHolds = 0, fxHolds = 0;
        double holdTime = 0;
        foreach (var n in notes)
        {
            bool fx = n.Lane >= 4;
            if (n.IsHold)
            {
                holds++;
                if (fx) fxHolds++; else btHolds++;
                holdTime += n.End - n.T;
            }
            else
            {
                chips++;
                if (fx) fxChips++; else btChips++;
            }
        }
        Add("dur", dur);
        Add("chips_ps", perSec(chips));
        Add("holds_ps", perSec(holds));
        Add("bt_chips_ps", perSec(btChips));
        Add("fx_chips_ps", perSec(fxChips));
        Add("bt_holds_ps", perSec(btHolds));
        Add("fx_holds_ps", perSec(fxHolds));
        Add("hold_time_frac", holdTime / dur);
        Add("chain_ps", perSec(pc.UnitTimes.Length));
        Add("hold_ticks_ps", perSec(pc.HoldTicks));
        Add("laser_ticks_ps", perSec(pc.LaserTicks));

        // ------------------------------------------------------------ 2. chords and peaks
        var chords = Chords(notes);
        var chordT = chords.Select(c => c.T).ToArray();
        int chord2 = 0, chord3 = 0, split = 0, btfx = 0;
        foreach (var c in chords)
        {
            int k = System.Numerics.BitOperations.PopCount((uint)c.Mask);
            if (k >= 2) chord2++;
            if (k >= 3) chord3++;
            bool l = false, r = false, bt = (c.Mask & 0x0F) != 0, fx = (c.Mask & 0x30) != 0;
            for (int lane = 0; lane < 6; lane++)
                if ((c.Mask & (1 << lane)) != 0)
                {
                    if (hands.HandOf(lane) == 0) l = true; else r = true;
                }
            if (l && r) split++;
            if (bt && fx) btfx++;
        }
        double nChords = Math.Max(1, chords.Count);
        Add("chord_ps", perSec(chords.Count));
        Add("chord2_frac", chord2 / nChords);
        Add("chord3_frac", chord3 / nChords);
        Add("split_chord_frac", split / nChords);
        Add("btfx_chord_frac", btfx / nChords);

        var noteT = notes.Select(n => n.T).ToArray();
        foreach (double w in new[] { 0.5, 1.25, 2.5, 5.0 })
            Add($"peak_chords_{Fmt(w)}", WindowMax(chordT, w) / w);
        foreach (double w in new[] { 1.25, 2.5 })
            Add($"peak_notes_{Fmt(w)}", WindowMax(noteT, w) / w);
        Add("peak_units_2.5", WindowMax(pc.UnitTimes, 2.5) / 2.5);
        var counts = WindowCounts(chordT, 1.25);
        Add("chords_1.25_p90", Percentile(counts, 0.90) / 1.25);
        Add("chords_1.25_p75", Percentile(counts, 0.75) / 1.25);
        Add("chords_1.25_p50", Percentile(counts, 0.50) / 1.25);
        Add("kde_chords_0.5", KdeMax(chordT, 0.5));
        Add("span_chords_8", MinSpanRate(chordT, 8));
        Add("span_chords_16", MinSpanRate(chordT, 16));

        // ------------------------------------------------------------ 3. hands, jacks, trills, streams
        var handT = new[] { new List<double>(), new List<double>() };
        foreach (var c in chords)
        {
            bool l = false, r = false;
            for (int lane = 0; lane < 6; lane++)
                if ((c.Mask & (1 << lane)) != 0)
                {
                    if (hands.HandOf(lane) == 0) l = true; else r = true;
                }
            if (l) handT[0].Add(c.T);
            if (r) handT[1].Add(c.T);
        }
        double hp0 = WindowMax(handT[0].ToArray(), 1.25) / 1.25, hp1 = WindowMax(handT[1].ToArray(), 1.25) / 1.25;
        Add("hand_peak_max", Math.Max(hp0, hp1));
        Add("hand_peak_min", Math.Min(hp0, hp1));
        int handNotes0 = notes.Count(n => hands.HandOf(n.Lane) == 0), handNotes1 = notes.Count - handNotes0;
        Add("hand_balance", Math.Min(handNotes0, handNotes1) / (double)Math.Max(1, Math.Max(handNotes0, handNotes1)));

        int jackInner = 0, jackOuter = 0, jackFx = 0;
        var lastByLane = new double[6];
        Array.Fill(lastByLane, double.NegativeInfinity);
        foreach (var n in notes)
        {
            double gap = n.T - lastByLane[n.Lane];
            if (gap > Eps && gap <= JackGap)
            {
                if (n.Lane is 1 or 2) jackInner++;
                else if (n.Lane is 0 or 3) jackOuter++;
                else jackFx++;
            }
            lastByLane[n.Lane] = n.T;
        }
        Add("jack_ps", perSec(jackInner + jackOuter + jackFx));
        Add("jack_inner_ps", perSec(jackInner));
        Add("jack_outer_ps", perSec(jackOuter));
        Add("jack_fx_ps", perSec(jackFx));

        Add("trill_ps", perSec(Trills(chords, hands, 0) + Trills(chords, hands, 1)));

        int inStream = 0, run = 1, bursts = 0, irregular = 0, gapPairs = 0;
        for (int i = 1; i <= chordT.Length; i++)
        {
            bool cont = i < chordT.Length && chordT[i] - chordT[i - 1] <= StreamGap;
            if (cont) { run++; continue; }
            if (run >= 8) inStream += run;
            run = 1;
        }
        for (int i = 1; i < chordT.Length; i++)
            if (chordT[i] - chordT[i - 1] <= BurstGap) bursts++;
        for (int i = 1; i + 1 < chordT.Length; i++)
        {
            double g1 = chordT[i] - chordT[i - 1], g2 = chordT[i + 1] - chordT[i];
            if (g1 > 1 || g2 > 1 || g1 <= Eps || g2 <= Eps) continue;
            gapPairs++;
            double lr = Math.Log2(Math.Max(g1, g2) / Math.Min(g1, g2));
            if (Math.Abs(lr - Math.Round(lr)) > 0.1) irregular++;
        }
        Add("stream_frac", inStream / nChords);
        Add("burst_frac", bursts / nChords);
        Add("irregular_frac", irregular / (double)Math.Max(1, gapPairs));

        // ------------------------------------------------------------ 4. rhythm grid
        int off16 = 0, triplet = 0, odd = 0;
        foreach (var n in notes)
        {
            bool on16 = n.Pulse % 60 == 0, on24 = n.Pulse % 40 == 0;
            if (!on16) off16++;
            if (!on16 && on24) triplet++;
            if (!on16 && !on24) odd++;
        }
        double nNotes = Math.Max(1, notes.Count);
        Add("offgrid16_frac", off16 / nNotes);
        Add("triplet_frac", triplet / nNotes);
        Add("odd_frac", odd / nNotes);

        // ------------------------------------------------------------ 5. holds and nesting
        var holdList = notes.Where(n => n.IsHold).ToList();
        int nested = 0, nestedSameHand = 0, nestedFxHold = 0;
        foreach (var n in notes)
        {
            if (n.IsHold) continue;
            bool any = false, same = false, fxh = false;
            foreach (var h in holdList)
            {
                if (h.T > n.T) break;
                if (h.Lane == n.Lane || !(h.T < n.T - 0.001 && h.End > n.T + 0.001)) continue;
                any = true;
                if (hands.HandOf(h.Lane) == hands.HandOf(n.Lane)) same = true;
                if (h.Lane >= 4) fxh = true;
            }
            if (any) nested++;
            if (same) nestedSameHand++;
            if (fxh) nestedFxHold++;
        }
        Add("nested_ps", perSec(nested));
        Add("nested_same_hand_ps", perSec(nestedSameHand));
        Add("nested_fx_hold_ps", perSec(nestedFxHold));
        Add("nested_frac", nested / (double)Math.Max(1, chips));

        // ------------------------------------------------------------ 6. lasers
        var allSections = pc.Lasers[0].Concat(pc.Lasers[1]).ToList();
        double laserUnion = UnionLength(allSections.Select(s => (s.Start, s.End)));
        double both = IntersectionLength(Merge(pc.Lasers[0].Select(s => (s.Start, s.End))), Merge(pc.Lasers[1].Select(s => (s.Start, s.End))));
        var moving = new List<(double, double)>();
        var travelEvents = new List<(double T, double V)>();
        var slamTimes = new List<double>();
        double travel = 0, slamTravel = 0, wideTime = 0, laserTime = 0;
        int dirChanges = 0, slams = 0;
        foreach (var s in allSections)
        {
            laserTime += s.End - s.Start;
            if (s.Wide) wideTime += s.End - s.Start;
            var seq = new List<double>();
            for (int i = 0; i < s.Points.Count; i++)
            {
                var p = s.Points[i];
                seq.Add(p.V);
                if (p.Slam)
                {
                    slams++;
                    slamTravel += Math.Abs(p.After - p.V);
                    slamTimes.Add(p.T);
                    seq.Add(p.After);
                }
                if (i + 1 >= s.Points.Count) continue;
                var q = s.Points[i + 1];
                double dv = Math.Abs(q.V - p.After);
                if (dv > Eps)
                {
                    travel += dv;
                    travelEvents.Add((q.T, dv));
                    if (q.T > p.T) moving.Add((p.T, q.T));
                }
            }
            dirChanges += DirectionChanges(seq);
        }
        travelEvents.Sort((a, b) => a.T.CompareTo(b.T));
        slamTimes.Sort();
        Add("laser_frac", laserUnion / dur);
        Add("laser_both_frac", both / dur);
        Add("laser_moving_frac", UnionLength(moving) / dur);
        Add("laser_wide_frac", laserTime > 0 ? wideTime / laserTime : 0);
        Add("laser_sections_ps", perSec(allSections.Count));
        Add("laser_travel_ps", perSec(travel));
        Add("laser_dirchg_ps", perSec(dirChanges));
        Add("slam_ps", perSec(slams));
        Add("slam_travel_ps", perSec(slamTravel));
        Add("laser_speed_peak", WindowSumMax(travelEvents, 1.0));
        Add("slam_peak_2", WindowMax(slamTimes.ToArray(), 2.0) / 2.0);

        double far = 0, crossed = 0;
        foreach (var s in allSections)
            far += FarTime(pc, s);
        crossed = CrossedTime(pc);
        Add("laser_far_frac", far / dur);
        Add("laser_crossed_frac", crossed / dur);

        // ------------------------------------------------------------ 7. knob + buttons
        int ohOpp = 0, ohOppChip = 0, ohOppMov = 0, ohOppHold = 0, ohSame = 0, ohSameChip = 0, ohSameMov = 0, ohSameFar = 0, ohBoth = 0;
        var oppChordPulses = new HashSet<long>();
        var sameChordPulses = new HashSet<long>();
        var oppTimes = new List<double>();
        var sameTimes = new List<double>();
        var ptr = new int[2];
        foreach (var n in notes)
        {
            bool opp = false, oppMov = false, same = false, sameMov = false, sameFar = false;
            int active = 0;
            for (int side = 0; side < 2; side++)
            {
                var lane = pc.Lasers[side];
                while (ptr[side] < lane.Count && lane[ptr[side]].End < n.T - Eps) ptr[side]++;
                // sections may overlap in malformed charts: check the next few
                for (int k = ptr[side]; k < lane.Count && lane[k].Start <= n.T + Eps; k++)
                {
                    var s = lane[k];
                    if (s.End < n.T - Eps) continue;
                    active |= 1 << side;
                    bool mov = s.MovingAt(n.Pulse, SlamNearPulses);
                    if (hands.HandOf(n.Lane) == side)
                    {
                        same = true;
                        if (mov) sameMov = true;
                        double v = s.ValueAt(n.Pulse);
                        if (side == 0 ? v > 0.5 + Eps : v < 0.5 - Eps) sameFar = true;
                    }
                    else
                    {
                        opp = true;
                        if (mov) oppMov = true;
                    }
                    break;
                }
            }
            if (active == 3) ohBoth++;
            if (opp)
            {
                ohOpp++;
                if (n.IsHold) ohOppHold++; else ohOppChip++;
                if (oppMov) ohOppMov++;
                oppChordPulses.Add(n.Pulse);
                oppTimes.Add(n.T);
            }
            if (same)
            {
                ohSame++;
                if (!n.IsHold) ohSameChip++;
                if (sameMov) ohSameMov++;
                if (sameFar) ohSameFar++;
                sameChordPulses.Add(n.Pulse);
                sameTimes.Add(n.T);
            }
        }
        Add("onehand_ps", perSec(ohOpp));
        Add("onehand_chip_ps", perSec(ohOppChip));
        Add("onehand_hold_ps", perSec(ohOppHold));
        Add("onehand_moving_ps", perSec(ohOppMov));
        Add("onehand_chord_ps", perSec(oppChordPulses.Count));
        Add("onehand_peak_2.5", WindowMax(oppTimes.ToArray(), 2.5) / 2.5);
        Add("handtrip_ps", perSec(ohSame));
        Add("handtrip_chip_ps", perSec(ohSameChip));
        Add("handtrip_moving_ps", perSec(ohSameMov));
        Add("handtrip_far_ps", perSec(ohSameFar));
        Add("handtrip_chord_ps", perSec(sameChordPulses.Count));
        Add("handtrip_peak_2.5", WindowMax(sameTimes.ToArray(), 2.5) / 2.5);
        Add("knob_both_notes_ps", perSec(ohBoth));
        Add("knob_chord_frac", oppChordPulses.Union(sameChordPulses).Count() / nChords);

        // ------------------------------------------------------------ 8. tempo, stops, scroll, camera
        var tempo = Tempo(pc);
        Add("bpm_changes", tempo.Changes);
        Add("bpm_logratio", tempo.LogRatio);
        Add("bpm_beatlen_pm", tempo.BeatLenDelta / dur * 60);
        Add("bpm_offmain_frac", tempo.OffMainFrac);
        var chart = pc.Chart;
        double stopSec = chart.Beat.Stops.Sum(s => Math.Max(0, pc.Seconds(s.Y + s.Length) - pc.Seconds(s.Y)));
        Add("stops_pm", chart.Beat.Stops.Count / dur * 60);
        Add("stop_frac", stopSec / dur);
        Add("scroll_changes_pm", ScrollChanges(chart) / dur * 60);
        var (zoom, tilt, spins) = Camera(chart);
        Add("cam_travel_ps", perSec(zoom + tilt));
        Add("cam_zoom_ps", perSec(zoom));
        Add("cam_tilt_ps", perSec(tilt));
        Add("spins_pm", spins / dur * 60);

        return new PatternFeatureSet
        {
            Features = f,
            Duration = dur,
            NoteCount = notes.Count,
            LaserSectionCount = allSections.Count,
        };
    }

    private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    // ================================================================ building blocks

    /// <summary>A chord: the notes that start on the same pulse (bit per lane).</summary>
    public readonly record struct ChordEvent(double T, long Pulse, int Mask);

    public static List<ChordEvent> Chords(IReadOnlyList<PatternNote> notes)
    {
        var list = new List<ChordEvent>();
        int i = 0;
        while (i < notes.Count)
        {
            int j = i, mask = 0;
            while (j < notes.Count && notes[j].Pulse == notes[i].Pulse) mask |= 1 << notes[j++].Lane;
            list.Add(new ChordEvent(notes[i].T, notes[i].Pulse, mask));
            i = j;
        }
        return list;
    }

    /// <summary>Single-lane events of one hand alternating x, y, x with every gap ≤ <see cref="StreamGap"/>.</summary>
    private static int Trills(List<ChordEvent> chords, HandModel hands, int hand)
    {
        int count = 0;
        int prevLane = -1, prevPrevLane = -1;
        double prevT = double.NegativeInfinity, prevPrevT = double.NegativeInfinity;
        foreach (var c in chords)
        {
            int lane = -1, k = 0;
            for (int l = 0; l < 6; l++)
                if ((c.Mask & (1 << l)) != 0 && hands.HandOf(l) == hand) { lane = l; k++; }
            if (k == 0) continue;
            if (k > 1) lane = -2; // a same-hand chord breaks the alternation
            if (lane >= 0 && prevLane >= 0 && prevPrevLane >= 0 && lane == prevPrevLane && lane != prevLane
                && c.T - prevT <= StreamGap && prevT - prevPrevT <= StreamGap)
                count++;
            prevPrevLane = prevLane;
            prevPrevT = prevT;
            prevLane = lane;
            prevT = c.T;
        }
        return count;
    }

    /// <summary>Direction reversals of a value sequence with <see cref="DirectionHysteresis"/>.</summary>
    public static int DirectionChanges(IReadOnlyList<double> seq)
    {
        if (seq.Count == 0) return 0;
        int dir = 0, changes = 0;
        double anchor = seq[0], ext = seq[0];
        for (int i = 1; i < seq.Count; i++)
        {
            double x = seq[i];
            if (dir == 0)
            {
                if (Math.Abs(x - anchor) >= DirectionHysteresis)
                {
                    dir = Math.Sign(x - anchor);
                    ext = x;
                }
            }
            else if (dir > 0)
            {
                if (x > ext) ext = x;
                else if (ext - x >= DirectionHysteresis) { changes++; dir = -1; ext = x; }
            }
            else
            {
                if (x < ext) ext = x;
                else if (x - ext >= DirectionHysteresis) { changes++; dir = 1; ext = x; }
            }
        }
        return changes;
    }

    /// <summary>Seconds a laser spends on the far half (left knob right of centre, right knob left of it).</summary>
    private static double FarTime(PatternChart pc, PatternLaser s)
    {
        double total = 0;
        for (int i = 0; i + 1 < s.Points.Count; i++)
        {
            var p = s.Points[i];
            var q = s.Points[i + 1];
            double dt = q.T - p.T;
            if (dt <= 0) continue;
            // fraction of the (linear) segment on the far half
            double a = s.Side == 0 ? p.After - 0.5 : 0.5 - p.After;
            double b = s.Side == 0 ? q.V - 0.5 : 0.5 - q.V;
            total += dt * FractionPositive(a, b);
        }
        return total;
    }

    /// <summary>Fraction of a linear segment from a to b that is &gt; 0.</summary>
    private static double FractionPositive(double a, double b)
    {
        if (a > Eps && b > Eps) return 1;
        if (a <= Eps && b <= Eps) return 0;
        double z = a / (a - b); // crossing point
        return a > b ? z : 1 - z;
    }

    /// <summary>Seconds where both lasers are active and the left one is right of the right one.</summary>
    private static double CrossedTime(PatternChart pc)
    {
        double total = 0;
        foreach (var l in pc.Lasers[0])
            foreach (var r in pc.Lasers[1])
            {
                double a = Math.Max(l.Start, r.Start), b = Math.Min(l.End, r.End);
                if (b <= a) continue;
                const int steps = 32;
                double dt = (b - a) / steps;
                for (int k = 0; k < steps; k++)
                {
                    double t = a + (k + 0.5) * dt;
                    double pulse = pc.PulseAt(t);
                    if (l.ValueAt(pulse) > r.ValueAt(pulse) + Eps) total += dt;
                }
            }
        return total;
    }

    private readonly record struct TempoStats(int Changes, double LogRatio, double BeatLenDelta, double OffMainFrac);

    private static TempoStats Tempo(PatternChart pc)
    {
        var bpms = pc.Chart.Beat.Bpm.Where(b => b.Bpm > 0).OrderBy(b => b.Y).ToList();
        if (bpms.Count == 0) return new TempoStats(0, 0, 0, 0);
        int changes = 0;
        double beatLen = 0;
        for (int i = 1; i < bpms.Count; i++)
        {
            if (Math.Abs(bpms[i].Bpm - bpms[i - 1].Bpm) <= 0.01) continue;
            changes++;
            beatLen += Math.Abs(60.0 / bpms[i].Bpm - 60.0 / bpms[i - 1].Bpm);
        }
        // time per BPM value inside [0, duration]
        var time = new Dictionary<double, double>();
        double end = pc.Duration;
        for (int i = 0; i < bpms.Count; i++)
        {
            double a = Math.Min(end, pc.Seconds(bpms[i].Y));
            double b = i + 1 < bpms.Count ? Math.Min(end, pc.Seconds(bpms[i + 1].Y)) : end;
            if (b <= a) continue;
            double key = Math.Round(bpms[i].Bpm, 2);
            time[key] = time.GetValueOrDefault(key) + (b - a);
        }
        double main = time.Count > 0 ? time.Values.Max() : end;
        double offMain = time.Count > 0 ? 1 - main / Math.Max(Eps, time.Values.Sum()) : 0;
        var used = time.Count > 0 ? time.Keys.ToList() : bpms.Select(b => b.Bpm).ToList();
        double ratio = Math.Log(used.Max() / used.Min());
        return new TempoStats(changes, ratio, beatLen, offMain);
    }

    private static int ScrollChanges(Chart chart)
    {
        int n = 0;
        double prev = 1;
        foreach (var p in chart.Beat.ScrollSpeed.OrderBy(p => p.Y))
        {
            if (Math.Abs(p.V - prev) > Eps) n++;
            if (Math.Abs(p.ValueAfter - p.V) > Eps) n++;
            prev = p.ValueAfter;
        }
        return n;
    }

    /// <summary>
    /// Camera movement: Σ|Δ| over zoom top / bottom / side (units of 100) and rotation (units of 90°) plus manual
    /// tilt (units of 1), and the number of spins / half spins / swings.
    /// </summary>
    private static (double Zoom, double Tilt, int Spins) Camera(Chart chart)
    {
        double travel = 0;
        void graph(List<GraphPoint> g, double unit)
        {
            for (int i = 0; i < g.Count; i++)
            {
                travel += Math.Abs(g[i].ValueAfter - g[i].V) / unit;
                if (i + 1 < g.Count) travel += Math.Abs(g[i + 1].V - g[i].ValueAfter) / unit;
            }
        }
        var body = chart.Camera.Body;
        graph(body.ZoomTop, 100);
        graph(body.ZoomBottom, 100);
        graph(body.ZoomSide, 100);
        graph(body.RotationDeg, 90);
        double zoom = travel;
        travel = 0;
        double? prev = null;
        foreach (var t in chart.Camera.Tilt.OrderBy(t => t.Y))
        {
            if (t.Value.IsAuto) { prev = null; continue; }
            if (prev is double p) travel += Math.Abs(t.Value.Manual - p);
            double after = t.Final is { IsAuto: false } fin ? fin.Manual : t.Value.Manual;
            travel += Math.Abs(after - t.Value.Manual);
            prev = after;
        }
        int spins = chart.Camera.Spin.Count + chart.Camera.HalfSpin.Count + chart.Camera.Swing.Count;
        return (zoom, travel, spins);
    }

    // ================================================================ windows and intervals

    /// <summary>Events in (t − w, t] at every event time, sorted ascending.</summary>
    public static double[] WindowCounts(double[] sorted, double w)
    {
        var vals = new double[sorted.Length];
        int j = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            while (sorted[j] <= sorted[i] - w) j++;
            vals[i] = i - j + 1;
        }
        Array.Sort(vals);
        return vals;
    }

    public static double WindowMax(double[] sorted, double w)
    {
        int best = 0, j = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            while (sorted[j] <= sorted[i] - w) j++;
            best = Math.Max(best, i - j + 1);
        }
        return best;
    }

    /// <summary>Largest sum of values inside (t − w, t].</summary>
    public static double WindowSumMax(List<(double T, double V)> sorted, double w)
    {
        double best = 0, sum = 0;
        int j = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            sum += sorted[i].V;
            while (sorted[j].T <= sorted[i].T - w) sum -= sorted[j++].V;
            best = Math.Max(best, sum);
        }
        return best;
    }

    /// <summary>Value at rank floor(p·(n−1)) of an ascending array (0 when empty).</summary>
    public static double Percentile(double[] sortedAscending, double p)
        => sortedAscending.Length == 0 ? 0 : sortedAscending[(int)Math.Floor(p * (sortedAscending.Length - 1))];

    /// <summary>Maximum of a Gaussian kernel density (σ seconds) evaluated at the event times, in events per second.</summary>
    public static double KdeMax(double[] sorted, double sigma)
    {
        if (sorted.Length == 0) return 0;
        double best = 0;
        int lo = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            double x = sorted[i], s = 0;
            while (sorted[lo] < x - 4 * sigma) lo++;
            for (int k = lo; k < sorted.Length && sorted[k] <= x + 4 * sigma; k++)
            {
                double d = (sorted[k] - x) / sigma;
                s += Math.Exp(-0.5 * d * d);
            }
            best = Math.Max(best, s);
        }
        return best / (sigma * Math.Sqrt(2 * Math.PI));
    }

    /// <summary>K divided by the shortest time spanned by K + 1 consecutive events (at least 0.05 s).</summary>
    public static double MinSpanRate(double[] sorted, int k)
    {
        if (sorted.Length < 2) return 0;
        if (sorted.Length <= k) return (sorted.Length - 1) / Math.Max(0.05, sorted[^1] - sorted[0]);
        double best = double.MaxValue;
        for (int i = 0; i + k < sorted.Length; i++) best = Math.Min(best, sorted[i + k] - sorted[i]);
        return k / Math.Max(best, 0.05);
    }

    public static List<(double Start, double End)> Merge(IEnumerable<(double Start, double End)> source)
    {
        var list = new List<(double Start, double End)>();
        foreach (var iv in source.Where(x => x.End > x.Start).OrderBy(x => x.Start))
        {
            if (list.Count > 0 && iv.Start <= list[^1].End) list[^1] = (list[^1].Start, Math.Max(list[^1].End, iv.End));
            else list.Add(iv);
        }
        return list;
    }

    public static double UnionLength(IEnumerable<(double Start, double End)> source) => Merge(source).Sum(x => x.End - x.Start);

    public static double IntersectionLength(List<(double Start, double End)> a, List<(double Start, double End)> b)
    {
        double total = 0;
        int i = 0, j = 0;
        while (i < a.Count && j < b.Count)
        {
            double lo = Math.Max(a[i].Start, b[j].Start), hi = Math.Min(a[i].End, b[j].End);
            if (hi > lo) total += hi - lo;
            if (a[i].End < b[j].End) i++; else j++;
        }
        return total;
    }
}
