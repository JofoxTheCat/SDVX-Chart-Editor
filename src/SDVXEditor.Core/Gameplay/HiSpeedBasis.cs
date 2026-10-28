using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Gameplay;

/// <summary>Where the playtest's hi-speed basis comes from.</summary>
public enum HiSpeedBasisSource
{
    /// <summary>The chart's own standard BPM (ksh <c>to=</c>, kson <c>meta.std_bpm</c>).</summary>
    StdBpm,

    /// <summary>The tempo the chart spends the most time in (<see cref="TimingMap.ModeBpm"/>).</summary>
    ModeBpm,
}

/// <summary>
/// The BPM a mod speed is divided by: hi-speed = mod speed ÷ basis, and the lane shows
/// <c>8 / hi-speed</c> beats (unnamed-sdvx-clone, <c>Game.cpp</c>: <c>SetViewRange(8 / hispeed)</c>), so at the basis
/// tempo a mod speed of 600 shows 0.8 s of notes and faster sections scroll faster in proportion, like the game.
/// <list type="bullet">
/// <item>A declared standard BPM wins — K-Shoot defines <c>to=</c> as exactly this ("standard BPM for hi-speed").</item>
/// <item>Otherwise the most common tempo by duration up to the last note, as in USC's M-mod
/// (<c>Beatmap::GetModeBPM</c>). Not the tempo at the playtest's start — that made the speed depend on where the test
/// began, so a test from a slow intro raced through the main part — and never the ksh <c>t=</c> header, which is
/// only the display range (<c>t=35-321</c>; libkson ignores a range there too).</item>
/// </list>
/// </summary>
public readonly record struct HiSpeedBasis(double Bpm, HiSpeedBasisSource Source)
{
    public static HiSpeedBasis Of(Chart chart, TimingMap timing)
    {
        if (chart.Meta.StdBpm is > 0 and double std)
            return new HiSpeedBasis(std, HiSpeedBasisSource.StdBpm);
        return new HiSpeedBasis(timing.ModeBpm(LastObjectPulse(chart)), HiSpeedBasisSource.ModeBpm);
    }

    /// <summary>Hi-speed for a mod speed ("BPM × hi-speed").</summary>
    public double HiSpeedFor(double modSpeed) => modSpeed / Math.Max(1, Bpm);

    /// <summary>Short label for the HUD.</summary>
    public string Label => Source == HiSpeedBasisSource.StdBpm ? "std BPM" : "mode BPM";

    /// <summary>End pulse of the last note or laser section (0 for an empty chart).</summary>
    public static long LastObjectPulse(Chart chart)
    {
        long last = 0;
        foreach (var lane in chart.Notes.Bt.Concat(chart.Notes.Fx))
            foreach (var n in lane)
                last = Math.Max(last, n.EndY);
        foreach (var lane in chart.Notes.Lasers)
            foreach (var s in lane)
                last = Math.Max(last, s.EndY);
        return last;
    }
}
