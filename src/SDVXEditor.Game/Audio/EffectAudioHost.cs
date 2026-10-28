using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Fx;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using SDVXEditor.Core.Audio.Effects;
using SDVXEditor.Core.Audio.Effects.Dsp;
using SDVXEditor.Game.Screens.Edit;

namespace SDVXEditor.Game.Audio;

/// <summary>
/// Plays the chart's audio effects (FX hold effects, laser filters) on the song while it plays: a BASS
/// <c>DSPPROC</c> on the song's <b>source</b> decode stream (before BASS_FX's tempo stream, so the data is in song time at
/// any playback rate) runs the Core <see cref="EffectRack"/>. The DSP finds the song time of every buffer from the
/// stream's decode position (in a DSPPROC <c>BASS_POS_DECODE</c> is the end of the buffer — measured), so effects sit
/// sample-accurately on the music whatever the output latency; the hitsound lead and the universal audio offset are
/// untouched (they act on the clock, not on the samples).
/// <para>
/// Off = no DSP on the channel at all (removed, zero cost). The rack's timeline is rebuilt (on this thread) after chart
/// edits, at most every <see cref="REBUILD_MS"/> ms. The playtest switches <see cref="Input"/> to the player's buttons and
/// laser cursors (<see cref="LiveEffectInput.ChartDriven"/> false) — there an effect is heard as soon as the audio
/// thread renders the next buffer, i.e. about <see cref="LeadMs"/> after the press, like in K-Shoot Mania.
/// </para>
/// </summary>
public partial class EffectAudioHost : Component
{
    public const double REBUILD_MS = 60;

    /// <summary>The effects tab checkbox / editor.ini <c>PlayEffects</c>.</summary>
    public readonly BindableBool Enabled = new BindableBool();

    /// <summary>What the player does (the playtest writes it every frame); chart-driven for editor playback.</summary>
    public LiveEffectInput Input { get; } = new LiveEffectInput();

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly Func<Track?> trackSource;

    private EffectRack? rack;
    private int source, dspHandle;
    private bool isFloat;
    private int bytesPerFrame;
    private DSPProcedure? procedure; // held: BASS only keeps a native pointer to it
    private float[] floatScratch = Array.Empty<float>();
    private short[] shortScratch = Array.Empty<short>();

    private object? builtChart;
    private object? builtTiming;
    private int builtVersion = -1;
    private double lastBuild = double.NegativeInfinity;

    private bool previewStartedPlayback;
    private readonly ConcurrentDictionary<string, float[]?> switchAudio = new ConcurrentDictionary<string, float[]?>(StringComparer.OrdinalIgnoreCase);

    // ---- instrumentation (smoke test, options) ----

    /// <summary>DSP callbacks that reached the rack.</summary>
    public long DspCalls;

    /// <summary>Exceptions caught inside the DSP callback (must stay 0).</summary>
    public long DspErrors;

    public Exception? LastDspError { get; private set; }

    /// <summary>Attach / detach count (smoke test: toggling really removes the DSP).</summary>
    public int Attaches { get; private set; }

    public int Detaches { get; private set; }

    /// <summary>True while the DSP is on the song's channel.</summary>
    public bool Attached => dspHandle != 0;

    /// <summary>The running rack (null while detached or before the track is ready).</summary>
    public EffectRack? Rack => rack;

    /// <summary>How far ahead of the clock the audio thread renders, in song ms (the playtest's press-to-effect latency).</summary>
    public double LeadMs { get; private set; }

    /// <summary>Timeline rebuilds (after edits).</summary>
    public int Rebuilds { get; private set; }

    public EffectAudioHost(EditorSession session, EditorClock clock, Func<Track?> trackSource)
    {
        this.session = session;
        this.clock = clock;
        this.trackSource = trackSource;
    }

    protected override void Update()
    {
        base.Update();

        var track = trackSource();
        int src = Enabled.Value && track is { IsLoaded: true } ? sourceHandle(track) : 0;

        if (src != source)
        {
            detach();
            if (src != 0) attach(src);
        }

        if (dspHandle == 0) return;

        rebuildIfNeeded();

        if (rack != null && track != null && track.IsRunning && !double.IsNaN(rack.LastEndMs))
            LeadMs = Math.Clamp(rack.LastEndMs - track.CurrentTime, 0, 1000);

        // the preview ends on its own; playback the preview started stops with it
        if (Input.PreviewName != null && clock.TrackTime >= Input.PreviewEndMs + 50)
        {
            Input.PreviewName = null;
            if (previewStartedPlayback) clock.IsPlaying.Value = false;
            previewStartedPlayback = false;
        }
    }

    /// <summary>
    /// Plays <paramref name="name"/> (an effect of the FX bus, or with <paramref name="laser"/> a laser filter swept
    /// 0 → 1 → 0) on the song from the cursor for <paramref name="ms"/> ms of song time. Turns playback on when it
    /// was off (and off again afterwards). False when effects are switched off or there is no audio.
    /// </summary>
    public bool Preview(string name, bool laser, double ms = 2000)
    {
        if (!Enabled.Value || trackSource() == null) return false;
        double start = clock.TrackTime;
        Input.PreviewStartMs = start;
        Input.PreviewEndMs = start + ms;
        Input.PreviewLaser = laser;
        Input.PreviewName = name;
        if (!clock.IsPlaying.Value)
        {
            previewStartedPlayback = true;
            clock.IsPlaying.Value = true;
        }
        return true;
    }

    // ---- timeline ------------------------------------------------------------------------------

    private void rebuildIfNeeded()
    {
        if (rack == null) return;
        bool changed = !ReferenceEquals(builtChart, session.Chart) || !ReferenceEquals(builtTiming, session.Timing) || builtVersion != session.Version;
        if (!changed || Time.Current - lastBuild < REBUILD_MS) return;

        builtChart = session.Chart;
        builtTiming = session.Timing;
        builtVersion = session.Version;
        lastBuild = Time.Current;
        try
        {
            var timeline = new EffectTimeline(session.Chart, session.Timing);
            rack.Timeline = timeline;
            Rebuilds++;
            foreach (string file in timeline.SwitchAudioFiles) loadSwitchAudio(file);
        }
        catch (Exception e)
        {
            Logger.Log($"effects: could not read the chart's effects: {e.Message}");
            rack.Timeline = null;
        }
    }

    // ---- BASS ----------------------------------------------------------------------------------

    private static FieldInfo? streamField;

    /// <summary>
    /// The BASS handle of the track's source (decode) stream: osu-framework's <c>TrackBass.activeStream</c> is the BASS_FX
    /// tempo stream; its source is where the DSP goes.
    /// </summary>
    private static int sourceHandle(Track track)
    {
        try
        {
            streamField ??= track.GetType().GetField("activeStream", BindingFlags.Instance | BindingFlags.NonPublic);
            if (streamField?.GetValue(track) is not int handle || handle == 0) return 0;
            int src = BassFx.TempoGetSource(handle);
            return src != 0 ? src : handle;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or FieldAccessException)
        {
            return 0;
        }
    }

    private void attach(int src)
    {
        if (!Bass.ChannelGetInfo(src, out var info) || info.Channels <= 0 || info.Frequency <= 0)
        {
            Logger.Log($"effects: no channel info for the song ({Bass.LastError}); effects stay off");
            source = src;
            return;
        }

        isFloat = info.Resolution == Resolution.Float || info.Flags.HasFlag(BassFlags.Float);
        bool is16 = !isFloat && info.Resolution != Resolution.Byte && !info.Flags.HasFlag(BassFlags.Byte);
        if (!isFloat && !is16)
        {
            Logger.Log("effects: 8-bit song streams are not supported; effects stay off");
            source = src;
            return;
        }

        bytesPerFrame = info.Channels * (isFloat ? 4 : 2);
        if (rack == null || rack.SampleRate != info.Frequency || rack.Channels != info.Channels)
        {
            rack = new EffectRack(info.Frequency, info.Channels) { Input = Input, SwitchAudio = name => switchAudio.TryGetValue(name, out var pcm) ? pcm : null };
            builtVersion = -1;
            builtChart = null;
        }
        else
            rack.Reset();

        rebuildIfNeeded();

        procedure ??= dsp;
        dspHandle = Bass.ChannelSetDSP(src, procedure, IntPtr.Zero, 0);
        source = src;
        if (dspHandle == 0)
        {
            Logger.Log($"effects: BASS refused the DSP ({Bass.LastError}); effects stay off");
            return;
        }

        Attaches++;
        Logger.Log($"effects: DSP on the song ({info.Frequency} Hz, {info.Channels} ch, {(isFloat ? "float" : "16-bit")})");
    }

    private void detach()
    {
        if (dspHandle != 0)
        {
            Bass.ChannelRemoveDSP(source, dspHandle);
            Detaches++;
        }
        dspHandle = 0;
        source = 0;
    }

    /// <summary>The DSPPROC (BASS's mixer thread). Never throws.</summary>
    private void dsp(int handle, int channel, IntPtr buffer, int length, IntPtr user)
    {
        try
        {
            var r = rack;
            if (r == null || length <= 0 || bytesPerFrame == 0) return;
            long end = Bass.ChannelGetPosition(channel, PositionFlags.Bytes | PositionFlags.Decode);
            if (end < 0) return;
            long startFrame = (end - length) / bytesPerFrame;

            if (isFloat)
            {
                int n = length / 4;
                if (floatScratch.Length < n) floatScratch = new float[n];
                Marshal.Copy(buffer, floatScratch, 0, n);
                r.Process(floatScratch.AsSpan(0, n), startFrame);
                Marshal.Copy(floatScratch, 0, buffer, n);
            }
            else
            {
                int n = length / 2;
                if (shortScratch.Length < n) shortScratch = new short[n];
                if (floatScratch.Length < n) floatScratch = new float[n];
                Marshal.Copy(buffer, shortScratch, 0, n);
                for (int i = 0; i < n; i++) floatScratch[i] = shortScratch[i] / 32768f;
                r.Process(floatScratch.AsSpan(0, n), startFrame);
                for (int i = 0; i < n; i++) shortScratch[i] = (short)Math.Clamp(floatScratch[i] * 32768f, short.MinValue, short.MaxValue);
                Marshal.Copy(shortScratch, 0, buffer, n);
            }

            Interlocked.Increment(ref DspCalls);
        }
        catch (Exception e)
        {
            LastDspError = e;
            Interlocked.Increment(ref DspErrors);
        }
    }

    /// <summary>Decodes a <c>switch_audio</c> file of the song folder to PCM in the track's format, off the update thread.</summary>
    private void loadSwitchAudio(string file)
    {
        if (rack == null || switchAudio.ContainsKey(file) || session.Song == null) return;
        switchAudio[file] = null;
        string path = Path.Combine(session.Song.FolderPath, file);
        int rate = rack.SampleRate, channels = rack.Channels;
        Task.Run(() =>
        {
            int h = 0;
            try
            {
                if (!File.Exists(path)) return;
                h = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float);
                if (h == 0 || !Bass.ChannelGetInfo(h, out var info)) return;
                var data = new List<float>();
                var buf = new float[16384];
                long limit = (long)info.Frequency * info.Channels * 600; // 10 minutes
                while (data.Count < limit)
                {
                    int got = Bass.ChannelGetData(h, buf, buf.Length * 4 | (int)DataFlags.Float);
                    if (got <= 0) break;
                    data.AddRange(buf.AsSpan(0, got / 4).ToArray());
                }
                switchAudio[file] = convert(data, info.Frequency, info.Channels, rate, channels);
                Logger.Log($"effects: switch_audio {file} loaded");
            }
            catch (Exception e)
            {
                Logger.Log($"effects: switch_audio {file} not loaded: {e.Message}");
            }
            finally
            {
                if (h != 0) Bass.StreamFree(h);
            }
        });
    }

    /// <summary>Linear resampling and channel mapping (mono ↔ stereo) to the song's format.</summary>
    private static float[] convert(List<float> src, int srcRate, int srcCh, int rate, int ch)
    {
        int srcFrames = src.Count / srcCh;
        long frames = (long)Math.Floor((double)srcFrames * rate / srcRate);
        var dst = new float[frames * ch];
        for (long f = 0; f < frames; f++)
        {
            double pos = (double)f * srcRate / rate;
            int a = (int)pos;
            double t = pos - a;
            int b = Math.Min(a + 1, srcFrames - 1);
            for (int c = 0; c < ch; c++)
            {
                int sc = Math.Min(c, srcCh - 1);
                dst[f * ch + c] = (float)(src[a * srcCh + sc] * (1 - t) + src[b * srcCh + sc] * t);
            }
        }
        return dst;
    }

    protected override void Dispose(bool isDisposing)
    {
        detach();
        base.Dispose(isDisposing);
    }
}
