using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Formats.Vox;

namespace SDVXEditor.Core.Radar;

/// <summary>Raw per-chart statistics, radar.py <c>raw_stats</c>.</summary>
public sealed record RadarRawStats
{
    public double Duration { get; init; }
    public int Chips { get; init; }
    public int Holds { get; init; }
    public int NotesTotal { get; init; }
    public int PeakNoteDensity { get; init; }
    public int Slams { get; init; }
    public int MovingLasers { get; init; }
    public int OneHandNotes { get; init; }
    public int WrongSideNotes { get; init; }
    public int BcJacks { get; init; }
    public int AdlrJacks { get; init; }
    public double BpmInverseDifferences { get; init; }
}

/// <summary>
/// SDVX skill radar (NOTES / PEAK / TSUMAMI / TRICKY / HAND-TRIP / ONE-HAND), a line-by-line port of
/// ksh2vox's <c>radar.py</c> 0.1.0, which in turn ports the scaling formulas and the 10..1000 clamp verbatim from
/// kshoot-tools' <c>src/radar.ts</c> (MIT, rhythm-gaming/kshoot-tools) and reimplements the raw statistics of
/// kshoot.js from their documented output. Upstream itself calls TRICKY and others approximate; these numbers
/// approximate the game's radar, they are not Konami's values.
/// <para>
/// Kept numerically equal to radar.py on the same file (tests: <c>RadarTests</c>, ±1 on all library charts):
/// same tick grid, same iteration order, same float operations, Python's half-to-even rounding.
/// </para>
/// </summary>
public static class RadarCalculator
{
    /// <summary>radar.py <c>__version__</c> this port follows.</summary>
    public const string PortedVersion = "0.1.0";

    /// <summary>Sliding window for peak note density (s).</summary>
    public const double PeakWindowSeconds = 1.0;

    /// <summary>A jack is a same-lane repeat within this wall-clock gap (s).</summary>
    public const double JackMaxGapSeconds = 0.12;

    /// <summary>Radar of a chart file (.ksh, .kson or .vox), read the way radar.py sees it.</summary>
    public static SkillRadar ComputeFile(string path)
    {
        var (chart, source) = ReadForRadar(path);
        return Compute(chart, source);
    }

    /// <summary>
    /// Reads a chart for the radar. ksh: baked curve points are kept (radar.py sees every ksh line), the dense-movement
    /// slam rule stays on so densely authored steps keep their real positions.
    /// </summary>
    public static (Chart Chart, RadarSource Source) ReadForRadar(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".ksh" => (KshReader.ReadFile(path, new KshReadOptions { CollapseBakedCurves = false }), RadarSource.Ksh),
            ".vox" => (VoxReader.ReadFile(path), RadarSource.Kson),
            _ => (KsonReader.ReadFile(path), RadarSource.Kson),
        };
    }

    public static SkillRadar Compute(Chart chart, RadarSource source) => Compute(RadarInput.FromChart(chart, source));

    /// <summary>radar.py <c>compute</c>.</summary>
    public static SkillRadar Compute(RadarInput input)
    {
        var s = RawStats(input);
        double d = s.Duration;
        double notes = s.NotesTotal / d;
        double peak = s.PeakNoteDensity;
        double tsumami = (s.MovingLasers + s.Slams) / d;
        double oneHand = s.OneHandNotes / d;
        double handTrip = s.WrongSideNotes / d;
        double tricky = (s.BcJacks + 2 * s.AdlrJacks + 4000 * s.BpmInverseDifferences) / d;

        // radar.ts RadarStat.fromRaw, then clamp; an axis without raw contribution is 0 (music_db convention)
        return new SkillRadar(
            final(notes, (16.663 * notes + 38.798) / 1.5),
            final(peak, (2.1644 * peak + 42.113) / 1.5),
            final(tsumami, (20 * tsumami + 18) / 1.5),
            final(tricky, (177.18 * tricky) / 1.5 + 10),
            final(handTrip, (168.25 * handTrip) / 1.5 + 10),
            final(oneHand, (64.817 * oneHand) / 1.5 + 10));

        static int final(double raw, double scaled) => raw == 0 ? 0 : (int)Math.Round(Clamp(scaled), MidpointRounding.ToEven);
    }

    /// <summary>radar.ts <c>RadarStat.clamp</c>.</summary>
    public static double Clamp(double v) => v >= 1000 ? 1000.0 : v < 10 ? 10.0 : v;

    /// <summary>radar.py <c>raw_stats</c>.</summary>
    public static RadarRawStats RawStats(RadarInput p)
    {
        var toSeconds = TickToSeconds(p.Bpm);
        double duration = Math.Max(toSeconds(p.EndTick), 1e-6);

        // button lanes in kshoot order: 0-3 = BT A-D, 4-5 = FX L/R
        var lanes = new List<(long Tick, long Length)>[] { p.Bt[0], p.Bt[1], p.Bt[2], p.Bt[3], p.Fx[0], p.Fx[1] };

        int chips = 0, holds = 0;
        var noteTimes = new List<double>();
        foreach (var lane in lanes)
        {
            foreach (var (t, length) in lane)
            {
                if (length > 0) holds++;
                else chips++;
                noteTimes.Add(toSeconds(t));
            }
        }

        // peak density: most notes inside any PEAK_WINDOW_S sliding window
        noteTimes.Sort();
        int peak = 0, j = 0;
        for (int i = 0; i < noteTimes.Count; i++)
        {
            double t = noteTimes[i];
            while (noteTimes[j] < t - PeakWindowSeconds) j++;
            peak = Math.Max(peak, i - j + 1);
        }

        // lasers: slams (near-instant value changes) and slanted chains (a run of moving segments counts once)
        int slams = 0, moving = 0;
        foreach (var lane in p.Laser)
        {
            foreach (var sec in lane)
            {
                bool inChain = false;
                for (int i = 0; i + 1 < sec.Count; i++)
                {
                    var (t0, v0) = sec[i];
                    var (t1, v1) = sec[i + 1];
                    if (v1 == v0)
                    {
                        inChain = false;
                        continue;
                    }
                    if (t1 - t0 <= RadarInput.SlamMaxTicks)
                    {
                        slams++;
                        inChain = false;
                    }
                    else if (!inChain)
                    {
                        moving++;
                        inChain = true;
                    }
                }
            }
        }

        // one-hand / hand-trip: notes played while a knob is being held
        var sections = new[] { extents(p.Laser[0]), extents(p.Laser[1]) };
        int oneHand = 0, wrongSide = 0;
        for (int laneIdx = 0; laneIdx < lanes.Length; laneIdx++)
        {
            bool laneLeft = laneIdx is 0 or 1 or 4; // BT A,B + FX L = left; BT C,D + FX R = right
            foreach (var (t, _) in lanes[laneIdx])
            {
                int active = -1;
                for (int li = 0; li < 2; li++)
                {
                    if (sections[li].Any(s => s.Start <= t && t <= s.End))
                    {
                        active = li;
                        break;
                    }
                }
                if (active < 0) continue;
                oneHand++;
                double? pos = LaserValueAt(p.Laser[active], t);
                if (pos is not double v) continue;
                bool knobLeft = v < 25;
                if (knobLeft == laneLeft) wrongSide++;
            }
        }

        // jacks: rapid same-lane repeats (wall-clock, so BPM-independent)
        int bcJacks = 0, adlrJacks = 0;
        for (int laneIdx = 0; laneIdx < lanes.Length; laneIdx++)
        {
            var times = lanes[laneIdx].Select(n => toSeconds(n.Tick)).ToList();
            times.Sort();
            int n = 0;
            for (int i = 0; i + 1 < times.Count; i++)
            {
                double gap = times[i + 1] - times[i];
                if (0 < gap && gap <= JackMaxGapSeconds) n++;
            }
            if (laneIdx is 1 or 2) bcJacks += n; // BT B/C = inner lanes
            else adlrJacks += n;                 // BT A/D + FX L/R = outer lanes
        }

        // sofulan: how strongly note spacing is distorted by BPM changes
        var ev = sortedBpm(p.Bpm);
        double inverse = 0;
        for (int i = 0; i + 1 < ev.Count; i++)
            inverse += Math.Abs(1.0 / orDefault(ev[i + 1].Bpm) - 1.0 / orDefault(ev[i].Bpm));

        return new RadarRawStats
        {
            Duration = duration,
            Chips = chips,
            Holds = holds,
            NotesTotal = chips + holds,
            PeakNoteDensity = peak,
            Slams = slams,
            MovingLasers = moving,
            OneHandNotes = oneHand,
            WrongSideNotes = wrongSide,
            BcJacks = bcJacks,
            AdlrJacks = adlrJacks,
            BpmInverseDifferences = inverse,
        };

        static List<(long Start, long End)> extents(List<List<(long Tick, double Value)>> lane)
            => lane.Where(s => s.Count > 0).Select(s => (s.Min(x => x.Tick), s.Max(x => x.Tick))).ToList();
    }

    /// <summary>radar.py <c>_laser_value_at</c>: value (0..50) at <paramref name="tick"/> inside the covering section, else null.</summary>
    internal static double? LaserValueAt(List<List<(long Tick, double Value)>> lane, long tick)
    {
        foreach (var sec in lane)
        {
            if (sec.Count == 0) continue;
            if (sec.Min(x => x.Tick) <= tick && tick <= sec.Max(x => x.Tick))
            {
                double? prev = null;
                foreach (var (t, v) in sec)
                {
                    if (t > tick) break;
                    prev = v;
                }
                return prev;
            }
        }
        return null;
    }

    /// <summary>radar.py <c>_tick_to_seconds_fn</c>.</summary>
    public static Func<long, double> TickToSeconds(IEnumerable<(long Tick, double Bpm)> bpmEvents)
    {
        var ev = sortedBpm(bpmEvents);
        if (ev.Count == 0) ev.Add((0, 120.0));
        if (ev[0].Tick != 0) ev.Insert(0, (0, ev[0].Bpm));

        var cum = new double[ev.Count];
        for (int i = 1; i < ev.Count; i++)
        {
            long dTicks = ev[i].Tick - ev[i - 1].Tick;
            double bpm = orDefault(ev[i - 1].Bpm);
            cum[i] = cum[i - 1] + dTicks / (double)RadarInput.TicksPerBeat * 60.0 / bpm;
        }

        return tick =>
        {
            int lo = 0, hi = ev.Count - 1;
            while (lo < hi) // last change at or before `tick`
            {
                int mid = (lo + hi + 1) / 2;
                if (ev[mid].Tick <= tick) lo = mid;
                else hi = mid - 1;
            }
            double bpm = orDefault(ev[lo].Bpm);
            return cum[lo] + (tick - ev[lo].Tick) / (double)RadarInput.TicksPerBeat * 60.0 / bpm;
        };
    }

    /// <summary>
    /// radar.py <c>_bpm_changes</c>: one BPM per tick, the later event in the list wins (what ksh2vox writes to
    /// #BPM INFO), sorted by tick.
    /// </summary>
    private static List<(long Tick, double Bpm)> sortedBpm(IEnumerable<(long Tick, double Bpm)> events)
    {
        var uniq = new Dictionary<long, double>();
        foreach (var (t, b) in events) uniq[t] = b;
        return uniq.OrderBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    private static double orDefault(double bpm) => bpm == 0 ? 120.0 : bpm;
}
