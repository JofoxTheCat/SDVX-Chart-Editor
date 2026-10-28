using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Patterns;

/// <summary>
/// Which hand plays which lane. Lanes are 0–3 = BT A–D, 4 = FX L, 5 = FX R; knobs: side 0 = left hand,
/// side 1 = right hand. The default is the usual SDVX split (BT A/B + FX L + knob L left, BT C/D + FX R +
/// knob R right); <c>tools/DifficultyCalibrator</c> compares it against alternatives on the official charts.
/// </summary>
public sealed record HandModel(string Name, int[] LaneHand)
{
    public static HandModel Default { get; } = new("default", new[] { 0, 0, 1, 1, 0, 1 });

    /// <summary>Hand (0 left, 1 right) of a button lane.</summary>
    public int HandOf(int lane) => LaneHand[lane];
}

/// <summary>A button note on the time axis (seconds from pulse 0, BPM map only; stops do not move time).</summary>
public readonly record struct PatternNote(double T, double End, int Lane, bool IsHold, long Pulse, long EndPulse);

/// <summary>
/// A laser section on the time axis. <see cref="Points"/> are the section's own points (pulse, time, value,
/// value after a slam); values 0..1, 0 = left edge.
/// </summary>
public sealed class PatternLaser
{
    public required int Side { get; init; }
    public required bool Wide { get; init; }
    public required double Start { get; init; }
    public required double End { get; init; }
    public required LaserSection Section { get; init; }
    public required IReadOnlyList<(long Pulse, double T, double V, double After, bool Slam)> Points { get; init; }

    /// <summary>Value at an absolute pulse (after a slam on that pulse), see <see cref="CameraEvaluator.ValueAt"/>.</summary>
    public double ValueAt(double pulse) => CameraEvaluator.ValueAt(Section, pulse);

    /// <summary>
    /// True when the knob has to turn at <paramref name="pulse"/>: the segment containing it changes value, or a
    /// slam lies within <paramref name="slamWindowPulses"/> of it.
    /// </summary>
    public bool MovingAt(double pulse, long slamWindowPulses)
    {
        for (int i = 0; i < Points.Count; i++)
        {
            var p = Points[i];
            if (p.Slam && Math.Abs(p.Pulse - pulse) <= slamWindowPulses) return true;
        }
        for (int i = Points.Count - 2; i >= 0; i--)
        {
            if (Points[i].Pulse > pulse) continue;
            return Math.Abs(Points[i + 1].V - Points[i].After) > 1e-6;
        }
        return false;
    }
}

/// <summary>
/// The time-based view of a chart that <see cref="PatternAnalysis"/> works on. Built from any
/// <see cref="Chart"/> (ksh, kson, VOX), so the editor and the calibrator see the same numbers.
/// </summary>
public sealed class PatternChart
{
    public required IReadOnlyList<PatternNote> Notes { get; init; }
    public required IReadOnlyList<PatternLaser>[] Lasers { get; init; }
    public required TimingMap Timing { get; init; }
    public required Chart Chart { get; init; }

    /// <summary>
    /// Chart length in seconds: the explicit end (ksh last bar line, VOX <c>END POSITION</c>) when the chart has one
    /// and it is not before half of the last object, else the end of the last object; at least 1.
    /// </summary>
    public required double Duration { get; init; }

    public required double FirstObject { get; init; }
    public required double LastObject { get; init; }

    /// <summary>Judged units after <see cref="TickRules"/>: hold ticks, laser ticks, slams.</summary>
    public required int HoldTicks { get; init; }
    public required int LaserTicks { get; init; }
    public required int Slams { get; init; }

    /// <summary>Times of every judged unit (chips, hold ticks, laser ticks, slams), sorted.</summary>
    public required double[] UnitTimes { get; init; }

    public double Seconds(double pulse) => Timing.PulseToMs(pulse) / 1000.0;
    public double PulseAt(double seconds) => Timing.MsToPulse(seconds * 1000.0);

    public static PatternChart From(Chart chart)
    {
        var timing = new TimingMap(chart.Beat, 0);
        double sec(double p) => timing.PulseToMs(p) / 1000.0;

        var notes = new List<PatternNote>();
        for (int lane = 0; lane < 6; lane++)
        {
            var src = lane < 4 ? chart.Notes.Bt[lane] : chart.Notes.Fx[lane - 4];
            foreach (var n in src)
                notes.Add(new PatternNote(sec(n.Y), sec(n.EndY), lane, n.IsHold, n.Y, n.EndY));
        }
        notes.Sort((a, b) => a.Pulse != b.Pulse ? a.Pulse.CompareTo(b.Pulse) : a.Lane.CompareTo(b.Lane));

        var lasers = new[] { new List<PatternLaser>(), new List<PatternLaser>() };
        for (int side = 0; side < 2; side++)
        {
            foreach (var s in chart.Notes.Lasers[side])
            {
                if (s.Points.Count == 0) continue;
                var pts = s.Points.Select(p => (s.Y + p.Ry, sec(s.Y + p.Ry), p.V, p.ValueAfter, p.IsSlam)).ToList();
                lasers[side].Add(new PatternLaser
                {
                    Side = side, Wide = s.Width == 2, Start = pts[0].Item2, End = pts[^1].Item2, Section = s, Points = pts,
                });
            }
            lasers[side].Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        // judged units (USC / RemyWiki tick rules)
        var units = new List<double>(notes.Count * 2);
        int holdTicks = 0, laserTicks = 0, slams = 0;
        foreach (var n in notes)
        {
            if (!n.IsHold)
            {
                units.Add(n.T);
                continue;
            }
            foreach (long t in TickRules.HoldTicks(n.Pulse, n.EndPulse - n.Pulse, TickRules.IntervalPulses(timing.BpmAt(n.Pulse))))
            {
                units.Add(sec(t));
                holdTicks++;
            }
        }
        foreach (var lane in lasers)
            foreach (var l in lane)
            {
                var (ticks, sl) = TickRules.LaserTicks(l.Section, TickRules.IntervalPulses(timing.BpmAt(l.Section.Y)));
                foreach (long t in ticks) units.Add(sec(t));
                foreach (var s in sl) units.Add(sec(s.Pulse));
                laserTicks += ticks.Count;
                slams += sl.Count;
            }
        units.Sort();

        double first = double.MaxValue, last = 0;
        foreach (var n in notes)
        {
            first = Math.Min(first, n.T);
            last = Math.Max(last, n.End);
        }
        foreach (var lane in lasers)
            foreach (var l in lane)
            {
                first = Math.Min(first, l.Start);
                last = Math.Max(last, l.End);
            }
        if (first == double.MaxValue) first = 0;

        double duration = last;
        if (chart.EndPulse is long end)
        {
            double te = sec(end);
            if (te >= 0.5 * last) duration = te;
        }

        return new PatternChart
        {
            Notes = notes,
            Lasers = lasers,
            Timing = timing,
            Chart = chart,
            Duration = Math.Max(duration, 1.0),
            FirstObject = first,
            LastObject = last,
            HoldTicks = holdTicks,
            LaserTicks = laserTicks,
            Slams = slams,
            UnitTimes = units.ToArray(),
        };
    }
}
