using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Gameplay;

/// <summary>One judged unit (chip, hold tick, laser tick or slam) with its judgement state.</summary>
public sealed class ScoreObject
{
    public ScoreObjectKind Kind { get; init; }

    /// <summary><see cref="PlayLanes"/> index (0-3 BT, 4/5 FX, 6/7 laser).</summary>
    public int Lane { get; init; }

    public long Pulse { get; init; }

    /// <summary>Song time in ms (the chart's audio offset included).</summary>
    public double Ms { get; init; }

    /// <summary>The note of a chip or hold tick.</summary>
    public Note? Note { get; init; }

    /// <summary>The laser of a laser tick or slam.</summary>
    public LaserObject? Laser { get; init; }

    /// <summary>Slams only.</summary>
    public SlamInfo Slam { get; init; }

    /// <summary>Before the playtest start: never judged and not counted.</summary>
    public bool Skipped { get; internal set; }

    public bool Judged { get; internal set; }
    public HitResult Result { get; internal set; }

    /// <summary>Slams: an input in the slam's direction arrived inside the window (judged when the slam is reached).</summary>
    internal bool SlamInputSeen { get; set; }

    internal double SlamInputOffset { get; set; }

    public bool IsHit => Judged && !Skipped && Result != HitResult.Error;

    public override string ToString() => $"{Kind} lane {Lane} @{Pulse}";
}

/// <summary>A BT/FX hold with its time span.</summary>
public sealed class HoldObject
{
    public int Lane { get; init; }
    public Note Note { get; init; } = null!;
    public double Ms { get; init; }
    public double EndMs { get; init; }
}

/// <summary>A laser section with its time span (for the cursor and the renderer).</summary>
public sealed class LaserObject
{
    /// <summary>0 = left, 1 = right.</summary>
    public int Side { get; init; }

    public LaserSection Section { get; init; } = null!;
    public double Ms { get; init; }
    public double EndMs { get; init; }

    public double ValueAt(double pulse) => CameraEvaluator.ValueAt(Section, pulse);

    /// <summary>Direction (−1, 0, 1) of the segment containing <paramref name="pulse"/> (slams ignored).</summary>
    public int DirectionAt(double pulse)
    {
        var pts = Section.Points;
        for (int i = pts.Count - 2; i >= 0; i--)
        {
            double y = Section.Y + pts[i].Ry;
            if (y > pulse) continue;
            double d = pts[i + 1].V - pts[i].ValueAfter;
            return Math.Abs(d) < 1e-9 ? 0 : Math.Sign(d);
        }

        return 0;
    }
}

/// <summary>
/// A chart prepared for play: every judged unit (<see cref="TickRules"/>) with its song time, per lane and in
/// time order. Units before the playtest start are kept (the renderer draws them) but marked
/// <see cref="ScoreObject.Skipped"/> and never counted, like osu!'s test mode.
/// </summary>
public sealed class PlayableChart
{
    public Chart Chart { get; }
    public TimingMap Timing { get; }

    /// <summary>Chips per button lane (0-5), in time order.</summary>
    public IReadOnlyList<ScoreObject>[] Chips { get; }

    /// <summary>Hold ticks per button lane (0-5), in time order.</summary>
    public IReadOnlyList<ScoreObject>[] HoldTicks { get; }

    /// <summary>Holds per button lane (0-5).</summary>
    public IReadOnlyList<HoldObject>[] Holds { get; }

    /// <summary>Laser sections per side (0/1).</summary>
    public IReadOnlyList<LaserObject>[] Lasers { get; }

    /// <summary>Laser ticks per side.</summary>
    public IReadOnlyList<ScoreObject>[] LaserTicks { get; }

    /// <summary>Slams per side.</summary>
    public IReadOnlyList<ScoreObject>[] Slams { get; }

    public double StartMs { get; }

    /// <summary>Time of the last judged unit (or the start when there is none).</summary>
    public double LastObjectMs { get; }

    public IEnumerable<ScoreObject> All =>
        Chips.SelectMany(l => l).Concat(HoldTicks.SelectMany(l => l)).Concat(LaserTicks.SelectMany(l => l)).Concat(Slams.SelectMany(l => l));

    public int CountedChips => Chips.Sum(l => l.Count(o => !o.Skipped));
    public int CountedTicks => HoldTicks.Sum(l => l.Count(o => !o.Skipped)) + LaserTicks.Sum(l => l.Count(o => !o.Skipped));
    public int CountedSlams => Slams.Sum(l => l.Count(o => !o.Skipped));

    /// <summary>Maximum chain from the start = number of counted units.</summary>
    public int MaxChain => CountedChips + CountedTicks + CountedSlams;

    /// <param name="startMs">Units earlier than this are skipped (−∞ = play everything).</param>
    public PlayableChart(Chart chart, TimingMap? timing = null, double startMs = double.NegativeInfinity)
    {
        Chart = chart;
        Timing = timing ?? TimingMap.FromChart(chart);
        StartMs = startMs;

        var chips = Enumerable.Range(0, PlayLanes.Buttons).Select(_ => new List<ScoreObject>()).ToArray();
        var holdTicks = Enumerable.Range(0, PlayLanes.Buttons).Select(_ => new List<ScoreObject>()).ToArray();
        var holds = Enumerable.Range(0, PlayLanes.Buttons).Select(_ => new List<HoldObject>()).ToArray();
        var lasers = new[] { new List<LaserObject>(), new List<LaserObject>() };
        var laserTicks = new[] { new List<ScoreObject>(), new List<ScoreObject>() };
        var slams = new[] { new List<ScoreObject>(), new List<ScoreObject>() };

        double ms(long pulse) => Timing.PulseToMs(pulse);

        for (int lane = 0; lane < PlayLanes.Buttons; lane++)
        {
            var notes = lane < 4 ? chart.Notes.Bt[lane] : chart.Notes.Fx[lane - 4];
            foreach (var n in notes)
            {
                if (!n.IsHold)
                {
                    chips[lane].Add(new ScoreObject { Kind = ScoreObjectKind.Chip, Lane = lane, Pulse = n.Y, Ms = ms(n.Y), Note = n });
                    continue;
                }

                holds[lane].Add(new HoldObject { Lane = lane, Note = n, Ms = ms(n.Y), EndMs = ms(n.EndY) });
                foreach (long t in TickRules.HoldTicks(n.Y, n.Length, TickRules.IntervalPulses(Timing.BpmAt(n.Y))))
                    holdTicks[lane].Add(new ScoreObject { Kind = ScoreObjectKind.HoldTick, Lane = lane, Pulse = t, Ms = ms(t), Note = n });
            }
        }

        for (int side = 0; side < 2; side++)
        {
            foreach (var s in chart.Notes.Lasers[side])
            {
                if (s.Points.Count == 0) continue;
                var lo = new LaserObject { Side = side, Section = s, Ms = ms(s.Y), EndMs = ms(s.EndY) };
                lasers[side].Add(lo);
                var (ticks, sl) = TickRules.LaserTicks(s, TickRules.IntervalPulses(Timing.BpmAt(s.Y)));
                foreach (long t in ticks)
                    laserTicks[side].Add(new ScoreObject { Kind = ScoreObjectKind.LaserTick, Lane = PlayLanes.LaserL + side, Pulse = t, Ms = ms(t), Laser = lo });
                foreach (var slam in sl)
                    slams[side].Add(new ScoreObject { Kind = ScoreObjectKind.Slam, Lane = PlayLanes.LaserL + side, Pulse = slam.Pulse, Ms = ms(slam.Pulse), Laser = lo, Slam = slam });
            }
        }

        foreach (var list in chips.Concat(holdTicks).Concat(laserTicks).Concat(slams))
        {
            list.Sort((a, b) => a.Pulse.CompareTo(b.Pulse));
            foreach (var o in list)
            {
                if (o.Ms < startMs)
                {
                    o.Skipped = true;
                    o.Judged = true;
                }
            }
        }

        foreach (var l in holds) l.Sort((a, b) => a.Note.Y.CompareTo(b.Note.Y));
        foreach (var l in lasers) l.Sort((a, b) => a.Section.Y.CompareTo(b.Section.Y));

        Chips = chips;
        HoldTicks = holdTicks;
        Holds = holds;
        Lasers = lasers;
        LaserTicks = laserTicks;
        Slams = slams;

        double last = double.IsNegativeInfinity(startMs) ? 0 : startMs;
        foreach (var o in All)
            last = Math.Max(last, o.Ms);
        LastObjectMs = last;
    }
}
