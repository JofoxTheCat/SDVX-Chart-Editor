using ManagedBass;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using SDVXEditor.Core.Audio;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Configuration;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// Plays a hitsound for every note, slam (and optionally laser start) that reaches the judgement
/// line while the song plays. Which sample each event plays is the <see cref="HitsoundMap"/> setting;
/// the samples come from the user's own osu! skin (<see cref="OsuSkinLocator"/>, never copied into
/// the repo) or from a configured folder, with the editor's synthesised tick/thud (<see cref="SynthSamples"/>)
/// as the fallback for a missing file. Notes on one pulse are layered like osu!mania chords (one sample
/// per note, capped by <see cref="EditorSetting.HitsoundChordLayers"/>), so chords are louder.
/// <para>
/// Samples are started <see cref="LeadMs"/> before their note: measured with a WASAPI loopback on the
/// real editor (2026-10-25), a sample started when the clock reached a note was heard ~60 ms
/// (median 55–70 ms over five runs) after the music's note — the track position runs ~27 ms behind the
/// audio that is already on its way to the device (BASS latency 17 ms + device buffer 10 ms) and a
/// sample needs ~33 ms from <c>Play()</c> to the device.
/// </para>
/// </summary>
public partial class HitsoundPlayer : Component
{
    public readonly BindableBool Enabled = new BindableBool(true);

    /// <summary>
    /// Slams play at the chart's slam volume (kson <c>key_sound.laser.vol</c>, ksh <c>chokkakuvol</c>): the default 50 %
    /// is the sample's own volume, lower values are quieter (×2, capped at 1 — a sample cannot be louder than itself).
    /// Bound to the effects checkbox.
    /// </summary>
    public readonly BindableBool ApplySlamVolume = new BindableBool();

    /// <summary>Slam samples played at a chart slam volume below 100 % (smoke test).</summary>
    public int QuietSlams { get; private set; }

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly IBindable<double> effectsVolume;

    private ISampleStore? samples;
    private ISampleStore? skinSamples;
    private AudioManager? audioManager;
    private double lastMs = double.NaN;

    private readonly Bindable<string> skinFolderSetting = new Bindable<string>(string.Empty);
    private readonly Bindable<string> mappingSetting = new Bindable<string>(HitsoundMap.DefaultText);
    private readonly BindableInt chordLayers = new BindableInt(4) { MinValue = 1, MaxValue = 8 };

    private readonly Dictionary<HitsoundSlot, Sample?> slotSamples = new Dictionary<HitsoundSlot, Sample?>();
    private readonly Dictionary<HitsoundSlot, string> resolution = new Dictionary<HitsoundSlot, string>();
    private readonly HashSet<string> warned = new HashSet<string>();

    /// <summary>Update frames that played at least one hitsound (smoke test).</summary>
    public int Played { get; private set; }

    /// <summary>Sample channels started for notes, slams and laser starts, chord layers included (smoke test).</summary>
    public int ChannelsPlayed { get; private set; }

    /// <summary>Largest number of layers played for one chord so far (smoke test).</summary>
    public int MaxChordLayers { get; private set; }

    /// <summary>FX chip key sounds played (custom files or built-ins), incl. previews (smoke test).</summary>
    public int KeySoundsPlayed { get; private set; }

    /// <summary>The hitsound skin folder in use (null = synthesised samples only).</summary>
    public string? SkinFolder { get; private set; }

    /// <summary>Per slot: which file (or fallback) is used, for the log and the smoke test.</summary>
    public IReadOnlyDictionary<HitsoundSlot, string> Resolution => resolution;

    /// <summary>How far ahead of its note a hitsound is started, in ms of audio time (see the class remarks).</summary>
    public double LeadMs { get; private set; } = double.NaN;

    private ISampleStore? songSamples;
    private string? songSamplesFolder;
    private readonly Dictionary<string, Sample> keySounds = new Dictionary<string, Sample>();

    public HitsoundPlayer(EditorSession session, EditorClock clock, IBindable<double> effectsVolume)
    {
        this.session = session;
        this.clock = clock;
        this.effectsVolume = effectsVolume;
    }

    [BackgroundDependencyLoader]
    private void load(AudioManager audio, EditorConfigManager config)
    {
        audioManager = audio;
        samples = audio.GetSampleStore(new ResourceStore<byte[]>(new SynthSamples()));
        samples.AddAdjustment(AdjustableProperty.Volume, effectsVolume);
        samples.PlaybackConcurrency = 16;

        config.BindWith(EditorSetting.HitsoundSkinFolder, skinFolderSetting);
        config.BindWith(EditorSetting.HitsoundMapping, mappingSetting);
        config.BindWith(EditorSetting.HitsoundChordLayers, chordLayers);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        skinFolderSetting.BindValueChanged(_ => resolveSamples());
        mappingSetting.BindValueChanged(_ => resolveSamples(), true);
    }

    // ---- sample resolution -------------------------------------------------

    private void resolveSamples()
    {
        string setting = skinFolderSetting.Value?.Trim() ?? OsuSkinLocator.AutoSetting;
        bool builtIn = string.Equals(setting, OsuSkinLocator.BuiltInSetting, StringComparison.OrdinalIgnoreCase);
        string? folder = OsuSkinLocator.ResolveSkinSetting(setting);
        if (folder == null && !builtIn)
            warnOnce($"folder:{setting}", setting.Length == 0
                ? "hitsounds: no osu! skin found, using the built-in sounds"
                : $"hitsounds: hitsound skin \"{setting}\" not found, using the built-in sounds");

        if (folder != SkinFolder || (folder != null && skinSamples == null))
        {
            skinSamples?.Dispose();
            skinSamples = null;
            SkinFolder = folder;
            if (folder != null && audioManager != null)
            {
                skinSamples = audioManager.GetSampleStore(new ResourceStore<byte[]>(new StorageBackedResourceStore(new NativeStorage(folder))));
                skinSamples.AddAdjustment(AdjustableProperty.Volume, effectsVolume);
                skinSamples.PlaybackConcurrency = 16;
                Logger.Log($"hitsounds: skin folder {folder}" + (setting.Length == 0 ? " (active osu! skin)" : ""));
            }
        }

        var map = HitsoundMap.Parse(mappingSetting.Value);
        string skinName = folder != null ? Path.GetFileName(folder.TrimEnd('\\', '/')) : "";
        foreach (var slot in Enum.GetValues<HitsoundSlot>())
        {
            string name = map[slot];
            Sample? sample;
            string how;

            if (name == HitsoundMap.None)
            {
                sample = null;
                how = "none";
            }
            else if (name != HitsoundMap.Synth && OsuSkinLocator.ResolveSample(SkinFolder, name) is string file && skinSamples?.Get(Path.GetFileName(file)) is Sample s)
            {
                sample = s;
                how = Path.Combine(skinName, Path.GetFileName(file));
            }
            else
            {
                string synth = slot is HitsoundSlot.Slam ? "slam" : "chip";
                sample = samples?.Get(synth);
                bool fallback = name != HitsoundMap.Synth && !builtIn;
                how = fallback ? $"built-in {synth} (fallback: {name} not found)" : $"built-in {synth}";
                if (fallback)
                    warnOnce($"missing:{SkinFolder}:{name}", $"hitsounds: {name}(.wav/.ogg/.mp3) not in {SkinFolder ?? "any skin folder"}, {HitsoundMap.KeyOf(slot)} uses the built-in {synth}");
            }

            slotSamples[slot] = sample;
            resolution[slot] = how;
        }

        Logger.Log("hitsounds: " + string.Join(", ", resolution.Select(r => $"{HitsoundMap.KeyOf(r.Key)} -> {r.Value}")));
        SamplesResolved?.Invoke();
    }

    /// <summary>Raised after the samples were (re)resolved from the settings.</summary>
    public event Action? SamplesResolved;

    private void warnOnce(string key, string message)
    {
        if (warned.Add(key)) Logger.Log(message);
    }

    // ---- playback ----------------------------------------------------------

    /// <summary>
    /// Trigger lead from the output device: twice BASS's latency + device buffer (the track position
    /// lags the sound by about that much, and a sample needs about that much to get out) plus a few ms
    /// for the hop to the audio thread. 60 ms on the machine it was measured on.
    /// </summary>
    private static double estimateLead()
    {
        double devBuffer = 10, latency = 17;
        try
        {
            if (Bass.GetInfo(out var info) && info.Latency > 0) latency = info.Latency;
            int b = Bass.GetConfig(ManagedBass.Configuration.DeviceBufferLength);
            if (b > 0) devBuffer = b;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
        }

        return Math.Clamp(2 * (latency + devBuffer) + 6, 0, 200);
    }

    protected override void Update()
    {
        base.Update();

        if (double.IsNaN(LeadMs)) LeadMs = estimateLead();

        // look ahead: a sample started now is heard together with the music LeadMs (of song time) later
        double now = clock.CurrentTime.Value + LeadMs * clock.Rate.Value;
        double prev = lastMs;
        lastMs = now;

        if (!Enabled.Value || !clock.IsPlaying.Value || double.IsNaN(prev) || now <= prev || now - prev > 250)
            return;

        long from = (long)Math.Floor(session.MsToPulse(prev)) + 1;
        long to = (long)Math.Floor(session.MsToPulse(now));
        if (to < from) return;

        var notes = session.Chart.Notes;
        var counts = new Dictionary<(HitsoundSlot, long), int>();
        void add(HitsoundSlot slot, long pulse) => counts[(slot, pulse)] = counts.GetValueOrDefault((slot, pulse)) + 1;

        foreach (var lane in notes.Bt)
        {
            for (int i = firstAtOrAfter(lane, from); i < lane.Count && lane[i].Y <= to; i++)
                add(lane[i].IsHold ? HitsoundSlot.BtHold : HitsoundSlot.BtChip, lane[i].Y);
        }

        // FX chips with a key sound play that sound instead of the hitsound
        foreach (var lane in notes.Fx)
        {
            for (int i = firstAtOrAfter(lane, from); i < lane.Count && lane[i].Y <= to; i++)
            {
                if (lane[i].KeySound is { } ks && !lane[i].IsHold) PlayKeySound(ks.Name, ks.Volume);
                else add(lane[i].IsHold ? HitsoundSlot.FxHold : HitsoundSlot.FxChip, lane[i].Y);
            }
        }

        foreach (var lane in notes.Lasers)
        {
            foreach (var s in lane)
            {
                if (s.Y > to) break;
                if (s.EndY < from) continue;
                if (s.Y >= from) add(HitsoundSlot.LaserStart, s.Y);
                foreach (var p in s.Points)
                {
                    if (p.IsSlam && s.Y + p.Ry >= from && s.Y + p.Ry <= to)
                        add(HitsoundSlot.Slam, s.Y + p.Ry);
                }
            }
        }

        bool any = false;
        foreach (var ((slot, pulse), count) in counts)
        {
            if (slotSamples.GetValueOrDefault(slot) is not Sample sample) continue;

            // a chord: one sample per note, layered (louder), capped against clipping
            int layers = Math.Min(count, chordLayers.Value);
            double volume = slot == HitsoundSlot.Slam ? SlamVolumeAt(pulse) : 1;
            for (int k = 0; k < layers; k++)
            {
                if (volume >= 0.999)
                {
                    sample.Play();
                    continue;
                }

                var channel = sample.GetChannel();
                channel.Volume.Value = volume;
                channel.Play();
                QuietSlams++;
            }
            ChannelsPlayed += layers;
            MaxChordLayers = Math.Max(MaxChordLayers, layers);
            any = true;
        }

        if (any) Played++;
    }

    /// <summary>
    /// Plays the sample of one slot right now (the playtest's on-hit sounds; the clock-driven playback above is
    /// separate). Returns false when the slot is silent.
    /// </summary>
    public bool PlaySlot(HitsoundSlot slot, long? pulse = null)
    {
        if (slotSamples.GetValueOrDefault(slot) is not Sample sample) return false;
        double volume = slot == HitsoundSlot.Slam && pulse is { } p ? SlamVolumeAt(p) : 1;
        if (volume >= 0.999) sample.Play();
        else
        {
            var channel = sample.GetChannel();
            channel.Volume.Value = volume;
            channel.Play();
            QuietSlams++;
        }
        ChannelsPlayed++;
        return true;
    }

    /// <summary>Playback volume of a slam at <paramref name="pulse"/> (1 unless <see cref="ApplySlamVolume"/>).</summary>
    public double SlamVolumeAt(long pulse)
    {
        if (!ApplySlamVolume.Value) return 1;
        double v = Core.Editing.EffectCommands.ValueAt(session.Chart.Audio.KeySound.LaserVolume, pulse, 0.5);
        return Math.Clamp(v * 2, 0, 1);
    }

    /// <summary>
    /// Plays an FX chip key sound: a built-in name (synthesised stand-in) or an audio file in the song
    /// folder. Returns false when the sound cannot be found.
    /// </summary>
    public bool PlayKeySound(string name, double? volume = null)
    {
        var sample = keySound(name);
        if (sample == null) return false;
        var channel = sample.GetChannel();
        channel.Volume.Value = Math.Clamp(volume ?? 1, 0, 1);
        channel.Play();
        KeySoundsPlayed++;
        return true;
    }

    private Sample? keySound(string name)
    {
        if (keySounds.TryGetValue(name, out var cached)) return cached;

        Sample? s = name switch
        {
            "clap" or "clap_impact" or "clap_punchy" => samples?.Get("clap"),
            "snare" or "snare_lo" => samples?.Get("snare"),
            _ => songStore()?.Get(name),
        };

        // a file imported later is found on the next call: misses are not cached
        if (s != null) keySounds[name] = s;
        return s;
    }

    private ISampleStore? songStore()
    {
        string? folder = session.Song?.FolderPath;
        if (folder == null || audioManager == null) return null;
        if (songSamples != null && songSamplesFolder == folder) return songSamples;

        songSamples?.Dispose();
        songSamples = audioManager.GetSampleStore(new ResourceStore<byte[]>(new StorageBackedResourceStore(new NativeStorage(folder))));
        songSamples.AddAdjustment(AdjustableProperty.Volume, effectsVolume);
        songSamplesFolder = folder;
        return songSamples;
    }

    private static int firstAtOrAfter(List<Note> lane, long from)
    {
        int lo = 0, hi = lane.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (lane[mid].Y < from) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }

    protected override void Dispose(bool isDisposing)
    {
        samples?.Dispose();
        skinSamples?.Dispose();
        songSamples?.Dispose();
        base.Dispose(isDisposing);
    }
}

/// <summary>
/// Synthesised hitsounds as 16-bit mono WAV files: "chip" (a 3 kHz click, 35 ms) and "slam"
/// (a falling 180→60 Hz thump with a little noise, 90 ms). Own work, generated at runtime.
/// </summary>
public sealed class SynthSamples : IResourceStore<byte[]>
{
    private const int rate = 44100;

    private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>
    {
        ["chip"] = wav(chipSamples()),
        ["slam"] = wav(slamSamples()),
        ["clap"] = wav(noiseHit(140, 1800, 0.0)),
        ["snare"] = wav(noiseHit(160, 0, 190)),
    };

    /// <summary>A noise burst (clap: a few short repeats; snare: noise plus a body tone).</summary>
    private static float[] noiseHit(int lengthMs, double highPassHz, double toneHz)
    {
        int n = rate * lengthMs / 1000;
        var s = new float[n];
        var rng = new Random(7);
        double prev = 0, prevOut = 0;
        double rc = highPassHz > 0 ? 1 / (2 * Math.PI * highPassHz) : 0, dt = 1.0 / rate, alpha = rc / (rc + dt);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double noise = rng.NextDouble() * 2 - 1;
            double hp = highPassHz > 0 ? alpha * (prevOut + noise - prev) : noise;
            prev = noise;
            prevOut = hp;
            double env = toneHz > 0 ? Math.Exp(-t * 30) : Math.Exp(-(t % 0.012) * 300) * Math.Exp(-t * 25);
            double tone = toneHz > 0 ? 0.6 * Math.Sin(2 * Math.PI * toneHz * t) * Math.Exp(-t * 40) : 0;
            s[i] = (float)(env * (0.7 * hp + tone));
        }

        return s;
    }

    private static float[] chipSamples()
    {
        int n = rate * 35 / 1000;
        var s = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double env = Math.Exp(-t * 140);
            s[i] = (float)(env * (0.7 * Math.Sin(2 * Math.PI * 3000 * t) + 0.3 * Math.Sin(2 * Math.PI * 1500 * t)));
        }

        return s;
    }

    private static float[] slamSamples()
    {
        int n = rate * 90 / 1000;
        var s = new float[n];
        var rng = new Random(573);
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double f = 60 + 120 * Math.Exp(-t * 40);
            phase += 2 * Math.PI * f / rate;
            double env = Math.Exp(-t * 35);
            s[i] = (float)(env * (0.85 * Math.Sin(phase) + 0.15 * (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 120)));
        }

        return s;
    }

    private static byte[] wav(float[] samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataBytes = samples.Length * 2;
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)1); // mono
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(dataBytes);
        foreach (float f in samples)
            w.Write((short)Math.Clamp(f * 0.8f * short.MaxValue, short.MinValue, short.MaxValue));
        w.Flush();
        return ms.ToArray();
    }

    private byte[]? find(string name) =>
        files.TryGetValue(Path.GetFileNameWithoutExtension(name), out var b) ? b : null;

    public byte[] Get(string name) => find(name)!;

    public Task<byte[]> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(Get(name));

    public Stream? GetStream(string name) => find(name) is { } b ? new MemoryStream(b) : null;

    public IEnumerable<string> GetAvailableResources() => files.Keys.Select(k => k + ".wav");

    public void Dispose()
    {
    }
}
