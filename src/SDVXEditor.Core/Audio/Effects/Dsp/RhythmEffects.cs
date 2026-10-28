namespace SDVXEditor.Core.Audio.Effects.Dsp;

/// <summary>
/// Gate (ksmaudio <c>GateDSP</c>): the cycle restarts at the last trigger (a bar line); the first <c>rate</c> of each
/// <c>wave_length</c> is open, then a 12-frame fade-out (gain (12 − k) / 13) and silence; gain = lerp(1, gate, mix).
/// <c>wave_length</c> 0 bypasses.
/// </summary>
public sealed class GateDsp : EffectDsp
{
    private const int declick = 12;

    public override void Reset()
    {
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        long period = (long)(p[ParamId.WaveLength] * Rate);
        double mix = p[ParamId.Mix];
        if (period <= 0 || mix <= 0) return;
        long open = (long)(period * Math.Clamp(p[ParamId.Rate], 0, 1));
        long since = (long)Math.Floor(time.FramesSince(slot.TriggerMs));

        for (int i = 0; i < frames; i++)
        {
            long c = since + i;
            long ph = ((c % period) + period) % period;
            double g = ph < open ? 1 : open > declick && ph < open + declick ? (declick - (ph - open)) / (double)(declick + 1) : 0;
            float gain = (float)(1 + (g - 1) * mix);
            for (int ch = 0; ch < Channels; ch++) s[i * Channels + ch] *= gain;
        }
    }
}

/// <summary>
/// Retrigger (ksmaudio <c>RetriggerEchoDSP</c>, non-echo): from each trigger (the <c>update_period</c> grid, an
/// <c>update_trigger</c> switch, or the press) the audio from that trigger on is looped every <c>wave_length</c>;
/// within a loop only the first <c>rate</c> sounds (12-frame fade-out, then silence). Reads the continuous input
/// history, so a loop can start at a grid point before the hold. <c>wave_length</c> 0 bypasses.
/// </summary>
public sealed class RetriggerDsp : EffectDsp
{
    private const int declick = 12;

    public override void Reset()
    {
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        long loop = (long)(p[ParamId.WaveLength] * Rate);
        double mix = p[ParamId.Mix];
        if (loop <= 0 || mix <= 0) return;
        long open = (long)(loop * Math.Clamp(p[ParamId.Rate], 0, 1));

        long trigger = time.FrameOf(slot.TriggerMs);
        // a trigger older than the history (a long grid period, a seek) falls back to the press
        if (!history.Has(trigger)) trigger = Math.Max(time.FrameOf(slot.ActivatedMs), history.ValidFrom);

        for (int i = 0; i < frames; i++)
        {
            long f = time.StartFrame + i;
            long d = f - trigger;
            if (d < 0) continue;
            long ph = d % loop;
            double g = ph < open ? 1 : open > declick && ph < open + declick ? (declick - (ph - open)) / (double)(declick + 1) : 0;
            long src = trigger + ph;
            for (int ch = 0; ch < Channels; ch++)
            {
                int k = i * Channels + ch;
                float wet = (float)(history.Read(src, ch, s[k]) * g);
                s[k] = Lerp(s[k], wet, mix);
            }
        }
    }
}

/// <summary>
/// Echo (ksmaudio <c>RetriggerEchoDSP</c> with <c>isEcho</c>): a feedback delay of <c>wave_length</c>,
/// y = x + feedback_level · y[−wave_length]; the delay line is cleared at every trigger (the press with the default
/// <c>update_trigger</c> off&gt;on). Output lerp(x, y, mix). <c>wave_length</c> 0 bypasses.
/// </summary>
public sealed class EchoDsp : EffectDsp
{
    private float[] line = Array.Empty<float>();
    private int length, pos;
    private double lastTrigger = double.NaN;

    public override void Init(int sampleRate, int channels)
    {
        base.Init(sampleRate, channels);
        line = new float[sampleRate * 4 * channels];
    }

    public override void Reset()
    {
        Array.Clear(line);
        pos = 0;
        lastTrigger = double.NaN;
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        int len = (int)Math.Min(p[ParamId.WaveLength] * Rate, line.Length / Channels);
        double mix = p[ParamId.Mix];
        if (len <= 0) return;
        float fb = (float)Math.Clamp(p[ParamId.FeedbackLevel], 0, 1);

        if (len != length || slot.TriggerMs != lastTrigger)
        {
            Array.Clear(line, 0, Math.Min(line.Length, Math.Max(len, length) * Channels));
            length = len;
            pos = 0;
            lastTrigger = slot.TriggerMs;
        }

        for (int i = 0; i < frames; i++)
        {
            int at = pos * Channels;
            for (int ch = 0; ch < Channels; ch++)
            {
                int k = i * Channels + ch;
                float x = s[k];
                float y = x + fb * line[at + ch];
                line[at + ch] = y;
                s[k] = Lerp(x, y, mix);
            }
            if (++pos >= length) pos = 0;
        }
    }
}

/// <summary>
/// Side chain (ksmaudio <c>SidechainDSP</c>): from each trigger (the <c>period</c> grid) the envelope falls linearly to
/// 0 over <c>attack_time</c>, stays for <c>hold_time</c>, rises back over <c>release_time</c>; gain = lerp(env, 1, 1 / ratio).
/// </summary>
public sealed class SideChainDsp : EffectDsp
{
    public override void Reset()
    {
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        double ratio = Math.Max(1, p[ParamId.Ratio]);
        if (ratio <= 1) return;
        long a = (long)(p[ParamId.AttackTime] * Rate), h = (long)(p[ParamId.HoldTime] * Rate), r = (long)(p[ParamId.ReleaseTime] * Rate);
        long since = (long)Math.Floor(time.FramesSince(slot.TriggerMs));

        for (int i = 0; i < frames; i++)
        {
            long c = since + i;
            double env;
            if (c < 0) env = 1;
            else if (c < a) env = 1 - (double)c / a;
            else if (c < a + h) env = 0;
            else if (c < a + h + r) env = (double)(c - a - h) / r;
            else env = 1;
            float gain = (float)(env + (1 - env) / ratio);
            for (int ch = 0; ch < Channels; ch++) s[i * Channels + ch] *= gain;
        }
    }
}

/// <summary>
/// Tape stop (ksmaudio <c>TapestopDSP</c>): from the press the playback speed falls by
/// (speed·100 + 1) · (2.5^(−n / 8000) + 0.5) / 500000 per 44.1 kHz frame (n = frames since the press), the input is read
/// from a delay line at that speed (linear interpolation here) and fades out below speed 0.25 (×4).
/// <c>trigger</c> off = dry. Output lerp(x, wet, mix).
/// </summary>
public sealed class TapeStopDsp : EffectDsp
{
    private const double max_seconds = 8;

    private float[]? line;
    private int capacity;
    private long write;
    private double read, speed = 1;
    private long n;
    private double lastTrigger = double.NaN;

    public override void Reset()
    {
        speed = 1;
        n = 0;
        write = 0;
        read = 0;
        lastTrigger = double.NaN;
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        if (line == null)
        {
            capacity = (int)(Rate * max_seconds);
            line = new float[capacity * Channels];
        }

        if (slot.TriggerMs != lastTrigger)
        {
            lastTrigger = slot.TriggerMs;
            speed = 1;
            n = 0;
            read = write;
        }

        bool on = p[ParamId.Trigger] >= 0.5;
        double mix = p[ParamId.Mix];
        double scale = Math.Max(p[ParamId.Speed] * 100, 0) + 1;
        double rs = RateScale;

        for (int i = 0; i < frames; i++)
        {
            int w = (int)(write % capacity) * Channels;
            for (int ch = 0; ch < Channels; ch++) line[w + ch] = s[i * Channels + ch];

            if (on)
            {
                double rate = Math.Pow(2.5, -n / 8000.0 / rs) + 0.5;
                speed = Math.Max(0, speed - scale * rate / 500000 / rs);
                n++;
                // never fall more than the line behind
                if (write - read > capacity - 2) read = write - (capacity - 2);
                long r0 = (long)Math.Floor(read);
                double f = read - r0;
                int ia = (int)(r0 % capacity) * Channels, ib = (int)((r0 + 1) % capacity) * Channels;
                float fade = (float)(speed < 0.25 ? speed * 4 : 1);
                for (int ch = 0; ch < Channels; ch++)
                {
                    int k = i * Channels + ch;
                    float wet = (float)((line[ia + ch] + (line[ib + ch] - line[ia + ch]) * f) * fade);
                    s[k] = Lerp(s[k], wet, mix);
                }
                read += speed;
            }
            else
                read = write + 1;

            write++;
        }
    }
}

/// <summary>
/// Bit crusher (ksmaudio / unnamed-sdvx-clone <c>BitCrusherDSP</c>; really a sample-rate reducer): a sample-and-hold
/// that takes a new sample every <c>reduction</c> samples (44.1 kHz units, fractional). 0 = dry. Output lerp(x, held, mix).
/// </summary>
public sealed class BitCrusherDsp : EffectDsp
{
    private double counter;
    private readonly float[] held = new float[8];

    public override void Reset()
    {
        counter = 0;
        Array.Clear(held);
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        double period = p[ParamId.Reduction] * RateScale;
        double mix = p[ParamId.Mix];
        if (period < 1 || mix <= 0)
        {
            counter = 0;
            return;
        }

        for (int i = 0; i < frames; i++)
        {
            counter += 1;
            bool take = counter >= period;
            if (take) counter -= period;
            for (int ch = 0; ch < Channels && ch < held.Length; ch++)
            {
                int k = i * Channels + ch;
                if (take) held[ch] = s[k];
                s[k] = Lerp(s[k], held[ch], mix);
            }
        }
    }
}

/// <summary>
/// Pitch shift: our own two-tap delay-line shifter (unnamed-sdvx-clone uses SoundTouch, ksmaudio its own; both
/// chunk-based). Two read taps half a <c>chunk_size</c> apart move through the last chunk at 2^(pitch/12) and are
/// cross-faded with sin² / cos² windows (constant gain), so the latency is at most one chunk (700 samples ≈ 16 ms).
/// <c>overlap</c> is not used (the windows always overlap by half). Output lerp(x, shifted, mix).
/// </summary>
public sealed class PitchShiftDsp : EffectDsp
{
    private float[] line = Array.Empty<float>();
    private int size;
    private long write;
    private double delay;

    public override void Init(int sampleRate, int channels)
    {
        base.Init(sampleRate, channels);
        size = 8192;
        while (size < sampleRate / 4) size *= 2;
        line = new float[size * channels];
    }

    public override void Reset()
    {
        Array.Clear(line);
        write = 0;
        delay = 0;
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        var p = slot.Params;
        double semis = Math.Clamp(p[ParamId.Pitch], -48, 48);
        double mix = p[ParamId.Mix];
        double chunk = Math.Clamp(p[ParamId.ChunkSize] * RateScale, 64, size / 2 - 4);
        double ratio = Math.Pow(2, semis / 12);
        bool shifting = Math.Abs(semis) > 1e-3 && mix > 0;

        for (int i = 0; i < frames; i++)
        {
            int w = (int)(write & (size - 1)) * Channels;
            for (int ch = 0; ch < Channels; ch++) line[w + ch] = s[i * Channels + ch];

            if (shifting)
            {
                delay -= ratio - 1;
                delay %= chunk;
                if (delay < 0) delay += chunk;
                double d2 = (delay + chunk / 2) % chunk;
                double w1 = Math.Sin(Math.PI * delay / chunk);
                w1 *= w1;
                double w2 = 1 - w1;
                for (int ch = 0; ch < Channels; ch++)
                {
                    int k = i * Channels + ch;
                    float wet = (float)(w1 * tap(delay, ch) + w2 * tap(d2, ch));
                    s[k] = Lerp(s[k], wet, mix);
                }
            }

            write++;
        }
    }

    private float tap(double d, int ch)
    {
        double pos = write - d;
        long a = (long)Math.Floor(pos);
        double f = pos - a;
        float x0 = line[(int)(a & (size - 1)) * Channels + ch];
        float x1 = line[(int)((a + 1) & (size - 1)) * Channels + ch];
        return (float)(x0 + (x1 - x0) * f);
    }
}

/// <summary>
/// Switch audio: plays another audio file of the song folder in place of the song while active (K-Shoot mutes the
/// song and unmutes the switchable track at the same position). The PCM comes from <see cref="EffectRack.SwitchAudio"/>
/// (decoded by the audio side, same sample rate and channel count); until it is there the song plays.
/// </summary>
public sealed class SwitchAudioDsp : EffectDsp
{
    public Func<string, float[]?>? Source;

    public override void Reset()
    {
    }

    public override void Process(Span<float> s, int frames, EffectSlotState slot, in DspTime time, EffectHistory history)
    {
        string? file = slot.Effect?.Filename;
        if (file == null || Source?.Invoke(file) is not { } pcm) return;
        for (int i = 0; i < frames; i++)
        {
            long f = time.StartFrame + i;
            for (int ch = 0; ch < Channels; ch++)
            {
                long k = f * Channels + ch;
                s[i * Channels + ch] = k >= 0 && k < pcm.Length ? pcm[k] : 0;
            }
        }
    }
}
