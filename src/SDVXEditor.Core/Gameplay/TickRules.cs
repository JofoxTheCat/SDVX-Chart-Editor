using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Gameplay;

/// <summary>
/// Where holds and lasers are judged ("chain" ticks), after SOUND VOLTEX as documented and as implemented by
/// unnamed-sdvx-clone (USC, <c>Main/src/Scoring.cpp</c>, <c>m_CalculateTicks</c> / <c>m_CalculateHoldTicks</c> /
/// <c>m_CalculateLaserTicks</c>, read 2026-10-25).
/// <list type="bullet">
/// <item><b>Rate.</b> RemyWiki ("What is SOUND VOLTEX"): every long note and laser chain counts 4 chain per beat,
/// 2 per beat when the BPM is ≥ 256. USC generalises this to a 1/16 note interval halved per BPM octave above 255:
/// <c>16 / 2^max(floor(log2 bpm) − 7, 0)</c> → 1/16 below 256 BPM, 1/8 for 256–511, 1/4 from 512. The BPM is the
/// one in effect at the object's start (USC). The interval is counted in pulses here, so a BPM change inside a
/// hold keeps the musical spacing (USC uses the start tempo's ms interval — identical unless the tempo changes
/// mid-object).</item>
/// <item><b>Holds.</b> No tick on the start (RemyWiki: the chain starts one unit after the note begins and ends one
/// unit before it ends); ticks at start + k·interval while &lt; end − interval; a hold too short for any tick
/// gets one tick at its middle.</item>
/// <item><b>Lasers.</b> A section is split at its slams. Each part from <c>a</c> to <c>b</c> gets
/// <c>floor((b − a) / interval)</c> ticks at <c>a + i·interval</c>, i = 0.., except that the first one is dropped
/// when the part starts at a slam. Every slam is one judged object of its own (RemyWiki: "each slam counts as
/// 1 combo chain").</item>
/// </list>
/// </summary>
public static class TickRules
{
    /// <summary>Tick interval in pulses at a BPM (60 = 1/16 note below 256 BPM).</summary>
    public static long IntervalPulses(double bpm)
    {
        if (!(bpm > 0)) bpm = 120;
        int octave = Math.Max((int)Math.Floor(Math.Log2(bpm)) - 7, 0);
        double noteValue = 16 / Math.Pow(2, octave);
        return Math.Max(1, (long)Math.Round(Timebase.PulsesPerWholeNote / noteValue));
    }

    /// <summary>Tick pulses of a hold starting at <paramref name="y"/> of <paramref name="length"/> pulses.</summary>
    public static List<long> HoldTicks(long y, long length, long interval)
    {
        var ticks = new List<long>();
        long end = y + length;
        for (long t = y + interval; t < end - interval; t += interval)
            ticks.Add(t);
        if (ticks.Count == 0)
            ticks.Add(y + length / 2);
        return ticks;
    }

    /// <summary>Tick pulses and slams of one laser section (absolute pulses).</summary>
    public static (List<long> Ticks, List<SlamInfo> Slams) LaserTicks(LaserSection section, long interval)
    {
        var ticks = new List<long>();
        var slams = new List<SlamInfo>();
        if (section.Points.Count == 0) return (ticks, slams);

        long partStart = section.Y;
        bool afterSlam = false;

        void addPart(long a, long b, bool skipFirst)
        {
            long n = (b - a) / interval;
            for (long i = skipFirst ? 1 : 0; i < n; i++)
                ticks.Add(a + i * interval);
        }

        foreach (var p in section.Points)
        {
            if (!p.IsSlam) continue;
            long at = section.Y + p.Ry;
            addPart(partStart, at, afterSlam);
            slams.Add(new SlamInfo(at, p.V, p.Vf!.Value));
            partStart = at;
            afterSlam = true;
        }

        addPart(partStart, section.EndY, afterSlam);
        return (ticks, slams);
    }
}

/// <summary>A slam: at <see cref="Pulse"/> the laser jumps from <see cref="From"/> to <see cref="To"/> (0..1 values).</summary>
public readonly record struct SlamInfo(long Pulse, double From, double To)
{
    public int Direction => To > From ? 1 : -1;
}
