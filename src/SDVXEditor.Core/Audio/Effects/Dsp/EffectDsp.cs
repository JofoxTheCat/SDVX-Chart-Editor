namespace SDVXEditor.Core.Audio.Effects.Dsp;

/// <summary>Where a block of samples sits in the song: first frame's song ms and absolute frame index.</summary>
public readonly record struct DspTime(double StartMs, long StartFrame, int SampleRate)
{
    public double FrameMs => 1000.0 / SampleRate;

    /// <summary>Frames from <paramref name="ms"/> to the block's first frame (negative when <paramref name="ms"/> lies after it).</summary>
    public double FramesSince(double ms) => (StartMs - ms) * SampleRate / 1000.0;

    /// <summary>Absolute frame index of a song time.</summary>
    public long FrameOf(double ms) => StartFrame + (long)Math.Round((ms - StartMs) * SampleRate / 1000.0);
}

/// <summary>
/// One effect's sample processing, in place on interleaved float samples. Instances are owned by one slot of an
/// <see cref="EffectRack"/> and only ever called from one thread. Parameters come from the slot state every
/// sub-block (<see cref="EffectRack.SUB_BLOCK"/> frames).
/// </summary>
public abstract class EffectDsp
{
    protected int Rate = 44100;
    protected int Channels = 2;

    /// <summary>Sample-count parameters are given at 44.1 kHz (kson spec); this scales them.</summary>
    protected double RateScale => Rate / 44100.0;

    public virtual void Init(int sampleRate, int channels)
    {
        Rate = sampleRate;
        Channels = channels;
        Reset();
    }

    public abstract void Reset();

    public abstract void Process(Span<float> samples, int frames, EffectSlotState slot, in DspTime time, EffectHistory history);

    protected static float Lerp(float a, float b, double t) => (float)(a + ((double)b - a) * t);

    /// <summary>Triangle 0 → 1 → 0 over one cycle.</summary>
    protected static double Triangle(double phase)
    {
        phase -= Math.Floor(phase);
        return phase < 0.5 ? phase * 2 : 2 - phase * 2;
    }
}

/// <summary>
/// The dry input of the last seconds, indexed by absolute frame: retrigger loops read from it, so a loop may start at
/// a grid point before the hold (K-Shoot captures continuously too). Invalidated by a seek.
/// </summary>
public sealed class EffectHistory
{
    private readonly float[] buffer;
    private readonly int capacity, channels;
    private long validFrom, end;

    public EffectHistory(int sampleRate, int channels, double seconds = 12)
    {
        this.channels = channels;
        capacity = Math.Max(1024, (int)(sampleRate * seconds));
        buffer = new float[capacity * channels];
    }

    public long End => end;

    public void Invalidate(long frame)
    {
        validFrom = frame;
        end = frame;
    }

    public void Write(ReadOnlySpan<float> samples, long startFrame)
    {
        int frames = samples.Length / channels;
        if (Math.Abs(startFrame - end) > EffectRack.MAX_SLIP_FRAMES) Invalidate(startFrame);
        else if (startFrame < validFrom) validFrom = startFrame;
        for (int i = 0; i < frames; i++)
        {
            long f = startFrame + i;
            int at = slot(f) * channels;
            for (int c = 0; c < channels; c++) buffer[at + c] = samples[i * channels + c];
        }
        end = Math.Max(end, startFrame + frames);
        if (end - validFrom > capacity) validFrom = end - capacity;
    }

    public bool Has(long frame) => frame >= validFrom && frame < end;

    public float Read(long frame, int channel, float fallback) =>
        Has(frame) ? buffer[slot(frame) * channels + channel] : fallback;

    private int slot(long f) => (int)(((f % capacity) + capacity) % capacity);

    /// <summary>The oldest frame still held.</summary>
    public long ValidFrom => validFrom;
}
