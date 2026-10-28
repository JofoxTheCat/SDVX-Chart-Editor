using System.Diagnostics;

namespace SDVXEditor.Core.Audio.Effects.Dsp;

/// <summary>
/// The effect chain applied to the song audio: FX-L, FX-R, then the laser bus, each a slot running one
/// <see cref="EffectDsp"/>. <see cref="Process"/> takes a block of interleaved float samples at an absolute frame
/// position of the song file (frame / sample rate = song ms, the chart offset included), evaluates the
/// <see cref="EffectTimeline"/> every <see cref="SUB_BLOCK"/> frames and runs the active effects. A slot that turns on or
/// off is cross-faded over <see cref="RAMP_MS"/> ms (declick). Called from the audio thread only; the timeline and the
/// input are swapped in from the update thread.
/// </summary>
public sealed class EffectRack
{
    /// <summary>Frames between two timeline evaluations (≈ 1.5 ms at 44.1 kHz).</summary>
    public const int SUB_BLOCK = 64;

    public const double RAMP_MS = 3;

    private sealed class Slot
    {
        public readonly Dictionary<EffectKind, EffectDsp> Dsps = new();
        public readonly EffectSlotState State = new();
        public EffectDsp? Current;
        public ResolvedEffect? Effect;
        public double Envelope;
    }

    public int SampleRate { get; }
    public int Channels { get; }

    private volatile EffectTimeline? timeline;
    private readonly Slot[] slots = { new(), new(), new() };
    private readonly EffectFrame frame = new();
    private readonly EffectHistory history;
    private float[] dry = new float[4096];
    private long expected = long.MinValue;
    private readonly SwitchAudioDsp[] switchDsps = new SwitchAudioDsp[3];

    /// <summary>The chart's effects (null = pass everything through).</summary>
    public EffectTimeline? Timeline
    {
        get => timeline;
        set => timeline = value;
    }

    /// <summary>What the player does; <see cref="LiveEffectInput.ChartDriven"/> for editor playback.</summary>
    public LiveEffectInput Input { get; set; } = new();

    /// <summary>PCM (same rate and channels, interleaved) of a <c>switch_audio</c> file, or null while it is not loaded.</summary>
    public Func<string, float[]?>? SwitchAudio
    {
        get => switchDsps[0].Source;
        set
        {
            foreach (var d in switchDsps) d.Source = value;
        }
    }

    // ---- instrumentation (read from any thread; approximate) ----

    /// <summary>Blocks processed.</summary>
    public long Blocks;

    /// <summary>Frames processed with at least one slot active.</summary>
    public long ActiveFrames;

    /// <summary>Frames processed per effect kind (index = (int)<see cref="EffectKind"/>), summed over the slots.</summary>
    public readonly long[] FramesByKind = new long[Enum.GetValues<EffectKind>().Length];

    /// <summary>Slot activations (a slot turning on or switching effect).</summary>
    public long Activations;

    /// <summary>Discontinuities that reset the effects (seeks, loops).</summary>
    public long Seeks;

    /// <summary>Small position slips (≤ <see cref="MAX_SLIP_FRAMES"/>) that only re-anchored the time.</summary>
    public long Slips;

    /// <summary>A block this close to the expected position continues the stream (≈ 93 ms at 44.1 kHz).</summary>
    public const int MAX_SLIP_FRAMES = 4096;

    /// <summary>The last discontinuities: (expected frame, frame that came), newest at <c>(Seeks + Slips - 1) % 16</c> (diagnostics).</summary>
    public readonly (long Expected, long Got)[] SeekLog = new (long, long)[16];

    /// <summary>Song ms at the end of the last block.</summary>
    public double LastEndMs = double.NaN;

    /// <summary>Total and worst time spent in <see cref="Process"/>, in <see cref="Stopwatch"/> ticks.</summary>
    public long ProcessTicks, MaxBlockTicks;

    /// <summary>Frames processed in total.</summary>
    public long Frames;

    /// <summary>The last laser cutoff in Hz, or NaN (instrumentation).</summary>
    public double LastLaserFreq = double.NaN;

    public EffectRack(int sampleRate, int channels)
    {
        SampleRate = sampleRate;
        Channels = Math.Max(1, channels);
        history = new EffectHistory(sampleRate, Channels);

        for (int i = 0; i < slots.Length; i++)
        {
            var d = slots[i].Dsps;
            d[EffectKind.Retrigger] = new RetriggerDsp();
            d[EffectKind.Gate] = new GateDsp();
            d[EffectKind.Flanger] = new FlangerDsp();
            d[EffectKind.PitchShift] = new PitchShiftDsp();
            d[EffectKind.BitCrusher] = new BitCrusherDsp();
            d[EffectKind.Phaser] = new PhaserDsp();
            d[EffectKind.Wobble] = new WobbleDsp();
            d[EffectKind.TapeStop] = new TapeStopDsp();
            d[EffectKind.Echo] = new EchoDsp();
            d[EffectKind.SideChain] = new SideChainDsp();
            d[EffectKind.SwitchAudio] = switchDsps[i] = new SwitchAudioDsp();
            d[EffectKind.PeakingFilter] = new PeakingFilterDsp();
            d[EffectKind.HighPassFilter] = new HighPassFilterDsp();
            d[EffectKind.LowPassFilter] = new LowPassFilterDsp();
            foreach (var dsp in d.Values) dsp.Init(sampleRate, Channels);
        }
    }

    /// <summary>Forgets every effect's state (seek); the next block starts clean.</summary>
    public void Reset()
    {
        foreach (var s in slots)
        {
            foreach (var d in s.Dsps.Values) d.Reset();
            s.Current = null;
            s.Effect = null;
            s.Envelope = 0;
        }
        expected = long.MinValue;
    }

    /// <summary>Processes <paramref name="samples"/> (interleaved, <see cref="Channels"/> per frame) starting at absolute frame <paramref name="startFrame"/>.</summary>
    public void Process(Span<float> samples, long startFrame)
    {
        long t0 = Stopwatch.GetTimestamp();
        int frames = samples.Length / Channels;
        if (frames == 0) return;
        Blocks++;
        Frames += frames;

        if (startFrame != expected)
        {
            // a decoder may hand over a block a few frames off (MP3 right after a seek: ~50-frame overlaps); only a real
            // jump (seek, loop) resets the effects — a small slip just re-anchors the time
            bool jump = expected == long.MinValue || Math.Abs(startFrame - expected) > MAX_SLIP_FRAMES;
            if (expected != long.MinValue)
            {
                SeekLog[(Seeks + Slips) % SeekLog.Length] = (expected, startFrame);
                if (jump) Seeks++;
                else Slips++;
            }
            if (jump)
            {
                Reset();
                history.Invalidate(startFrame);
            }
        }
        expected = startFrame + frames;
        history.Write(samples, startFrame);

        var tl = timeline;
        LastEndMs = (startFrame + frames) * 1000.0 / SampleRate;
        if (tl == null && slots[0].Envelope <= 0 && slots[1].Envelope <= 0 && slots[2].Envelope <= 0) return;

        for (int off = 0; off < frames; off += SUB_BLOCK)
        {
            int n = Math.Min(SUB_BLOCK, frames - off);
            long f0 = startFrame + off;
            var time = new DspTime(f0 * 1000.0 / SampleRate, f0, SampleRate);
            if (tl != null) tl.Evaluate(time.StartMs, Input, frame);
            else
                foreach (var st in frame.Slots) st.Active = false;

            var block = samples.Slice(off * Channels, n * Channels);
            bool any = false;
            for (int i = 0; i < slots.Length; i++)
                any |= run(slots[i], frame.Slots[i], block, n, time);
            if (any) ActiveFrames += n;
        }

        long dt = Stopwatch.GetTimestamp() - t0;
        ProcessTicks += dt;
        if (dt > MaxBlockTicks) MaxBlockTicks = dt;
    }

    private bool run(Slot slot, EffectSlotState now, Span<float> block, int n, in DspTime time)
    {
        double target = 0;
        if (now.Active && now.Effect != null && slot.Dsps.TryGetValue(now.Kind, out var dsp))
        {
            if (slot.Effect != now.Effect || slot.Current == null || slot.Envelope <= 0)
            {
                if (slot.Current != dsp || slot.Envelope <= 0) dsp.Reset();
                slot.Current = dsp;
                slot.Effect = now.Effect;
                Activations++;
            }

            copy(now, slot.State);
            target = 1;
        }

        if (slot.Current == null || (target <= 0 && slot.Envelope <= 0)) return false;

        double step = 1000.0 / (RAMP_MS * SampleRate);
        bool ramping = slot.Envelope != target || target < 1;
        if (ramping)
        {
            if (dry.Length < block.Length) dry = new float[block.Length * 2];
            block.CopyTo(dry);
        }

        slot.Current.Process(block, n, slot.State, time, history);
        FramesByKind[(int)slot.State.Kind] += n;
        if (slot.State.Kind is EffectKind.PeakingFilter or EffectKind.LowPassFilter or EffectKind.HighPassFilter)
            LastLaserFreq = ((LaserFilterDsp)slot.Current).CurrentFreq;

        if (ramping)
        {
            double env = slot.Envelope;
            for (int i = 0; i < n; i++)
            {
                env = target > env ? Math.Min(target, env + step) : Math.Max(target, env - step);
                for (int c = 0; c < Channels; c++)
                {
                    int k = i * Channels + c;
                    block[k] = (float)(dry[k] + (block[k] - dry[k]) * env);
                }
            }
            slot.Envelope = env;
        }

        if (slot.Envelope <= 0 && target <= 0)
        {
            slot.Current = null;
            slot.Effect = null;
        }
        return true;
    }

    private static void copy(EffectSlotState from, EffectSlotState to)
    {
        to.Active = from.Active;
        to.Kind = from.Kind;
        to.Effect = from.Effect;
        to.Params.CopyFrom(from.Params);
        to.ActivatedMs = from.ActivatedMs;
        to.TriggerMs = from.TriggerMs;
        to.V = from.V;
        to.Bpm = from.Bpm;
    }
}
