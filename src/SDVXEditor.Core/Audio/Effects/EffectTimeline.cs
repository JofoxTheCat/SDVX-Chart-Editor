using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Audio.Effects;

/// <summary>
/// One named effect with its parameters resolved down to value sets: type defaults (<see cref="EffectSpecs"/>), then
/// the chart's definition, plus the parameter changes over time (kson <c>param_change</c>; for the built-in laser
/// filters also the legacy <c>filter_gain</c>, see <see cref="EffectTimeline"/>).
/// </summary>
public sealed class ResolvedEffect
{
    public string Name { get; }
    public EffectKind Kind { get; }
    public bool Laser { get; }

    /// <summary><c>switch_audio</c>: the audio file (definition only).</summary>
    public string? Filename { get; }

    internal readonly EffectParamSpec[] Specs;
    internal readonly EffectValueSet[] Base;
    internal readonly (long Y, EffectValueSet Value)[][] Changes;

    internal ResolvedEffect(string name, EffectKind kind, bool laser, string? filename, EffectParamSpec[] specs, EffectValueSet[] @base,
                            (long, EffectValueSet)[][] changes)
    {
        Name = name;
        Kind = kind;
        Laser = laser;
        Filename = filename;
        Specs = specs;
        Base = @base;
        Changes = changes;
    }

    internal int IndexOf(ParamId id)
    {
        for (int i = 0; i < Specs.Length; i++)
            if (Specs[i].Id == id) return i;
        return -1;
    }

    /// <summary>The value set of parameter <paramref name="i"/> at <paramref name="pulse"/> (last change at or before it, else the base).</summary>
    internal EffectValueSet At(int i, double pulse)
    {
        var list = Changes[i];
        if (list.Length == 0 || list[0].Y > pulse) return Base[i];
        int lo = 0, hi = list.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (list[mid].Y <= pulse) lo = mid;
            else hi = mid - 1;
        }
        return list[lo].Value;
    }

    /// <summary>Pulses where <c>update_trigger</c> is changed to a value whose Off part is <c>on</c> (K-Shoot's extra retrigger/echo triggers).</summary>
    internal long[] UpdateTriggerPulses = Array.Empty<long>();
}

/// <summary>What the player does (playtest). Written by the update thread, read by the audio thread.</summary>
public sealed class LiveEffectInput
{
    /// <summary>True = the chart decides (editor playback, autoplay): every FX hold and laser section is active.</summary>
    public volatile bool ChartDriven = true;

    /// <summary>FX-L / FX-R button down.</summary>
    public readonly bool[] FxDown = new bool[2];

    /// <summary>Laser cursor L / R on its laser.</summary>
    public readonly bool[] LaserOn = new bool[2];

    /// <summary>
    /// An FX hold counts as pressed this long after its start even when the button is not down (yet): K-Shoot Mania
    /// v2's <c>kLongFXNoteAudioEffectAutoPlaySec</c> (30 ms) plus how far ahead of the speakers the audio is rendered,
    /// so an on-time press is heard from the hold start.
    /// </summary>
    public double GraceMs = 30;

    /// <summary>Effect preview (effects tab "preview"): effect name, bus, and the song-time window.</summary>
    public volatile string? PreviewName;
    public volatile bool PreviewLaser;
    public double PreviewStartMs, PreviewEndMs;
}

/// <summary>Evaluated state of one effect slot (FX-L, FX-R or the laser bus) at one instant.</summary>
public sealed class EffectSlotState
{
    public bool Active;
    public EffectKind Kind;
    public ResolvedEffect? Effect;
    public readonly EffectParams Params = new();

    /// <summary>When this activation began (hold / effect event start, press, laser section start), song ms.</summary>
    public double ActivatedMs;

    /// <summary>
    /// The effect's last trigger, song ms: the time origin of its cycle. Gate / wobble: the last bar line (K-Shoot
    /// triggers both on bar lines only); retrigger / echo: the last <c>update_period</c> grid point, an
    /// <c>update_trigger</c> switch or the activation; sidechain: the last <c>period</c> grid point; flanger / phaser /
    /// tape stop / pitch shift: the activation.
    /// </summary>
    public double TriggerMs;

    /// <summary>Laser value 0..1 that drives this slot (laser bus; 0 for FX holds).</summary>
    public double V;

    /// <summary>BPM at this instant (tempo-synced values are already resolved with it).</summary>
    public double Bpm;

    internal void Clear()
    {
        Active = false;
        Kind = EffectKind.None;
        Effect = null;
    }
}

/// <summary>The three slots of one instant. Reused by the audio thread (no allocation per evaluation).</summary>
public sealed class EffectFrame
{
    /// <summary>[0] FX-L, [1] FX-R, [2] laser bus.</summary>
    public readonly EffectSlotState[] Slots = { new(), new(), new() };

    public EffectSlotState FxL => Slots[0];
    public EffectSlotState FxR => Slots[1];
    public EffectSlotState Laser => Slots[2];
}

/// <summary>
/// Immutable snapshot of a chart's audio effects for the preview engine: which effect is active at a song time and with
/// which parameters, given what the player does. Pure and thread-safe (built on the update thread, evaluated on the
/// audio thread); rebuild it after an edit.
/// <para>
/// Rules (K-Shoot Mania v2, <c>kshootmania/src/MusicGame/Audio/AudioEffectMain.cpp</c> and the kson spec):
/// an FX hold activates the effect named by its last <c>long_event</c> at or before the time (none = no effect;
/// the empty name = no effect); its parameters are the definition's, the latest <c>param_change</c> and the event's own
/// overrides, in that order of precedence (override wins); values are the OnMin part. Two lanes with the same effect
/// share one instance (left wins). The laser bus uses the filter named by the last <c>filtertype</c> switch
/// (default <c>peaking_filter</c>); its value v = max(left value, 1 − right value) over the lanes with a section at
/// the time, sampled <c>peaking_filter_delay</c> ms earlier for the peaking filter; parameters run OnMin → OnMax
/// with v. Legacy <c>filter_gain</c> (ksh <c>pfiltergain</c>) acts as parameter changes of the built-in filters:
/// peaking gain = value, high-pass q = 2..8, low-pass q = 2..5.2.
/// </para>
/// </summary>
public sealed class EffectTimeline
{
    /// <summary>ksh <c>pfilterdelay</c> default (the editor's model: an unstated delay is 40 ms).</summary>
    public const int DEFAULT_PEAKING_DELAY_MS = 40;

    private sealed class FxEvent
    {
        public long Y;
        public ResolvedEffect? Effect;
        public EffectValueSet?[] Overrides = Array.Empty<EffectValueSet?>();
    }

    private sealed class FxHold
    {
        public long Y, End;
        public double StartMs;
        public FxEvent[] Events = Array.Empty<FxEvent>();
    }

    private readonly TimingMap timing;
    private readonly FxHold[][] holds = new FxHold[2][];
    private readonly LaserSection[][] lasers = new LaserSection[2][];
    private readonly (long Y, ResolvedEffect? Effect)[] filterSwitches;
    private readonly ResolvedEffect? defaultFilter;
    private readonly Dictionary<string, ResolvedEffect> fxEffects = new();
    private readonly Dictionary<string, ResolvedEffect> laserEffects = new();

    public double PeakingDelayMs { get; }

    /// <summary>The audio files <c>switch_audio</c> definitions name (the audio side loads them).</summary>
    public IReadOnlyList<string> SwitchAudioFiles { get; }

    /// <summary>True when the chart has anything this engine can play (FX holds with an effect or laser sections).</summary>
    public bool HasEffects { get; }

    public EffectTimeline(Chart chart, TimingMap? timing = null)
    {
        this.timing = timing ?? TimingMap.FromChart(chart);
        var audio = chart.Audio;
        PeakingDelayMs = Math.Clamp(audio.LaserEffects.PeakingFilterDelay ?? DEFAULT_PEAKING_DELAY_MS, 0, 160);

        // ---- effects by name (definitions override presets of the same name)
        foreach (string preset in EffectCatalog.PresetFx)
            if (resolve(preset, preset, null, audio.FxEffects.ParamChanges, false, null) is { } e) fxEffects[preset] = e;
        foreach (var def in audio.FxEffects.Defs)
            if (def.Name.Length > 0 && resolve(def.Name, def.Type, def.Params, audio.FxEffects.ParamChanges, false, null) is { } e) fxEffects[def.Name] = e;

        var gain = audio.LaserEffects.LegacyFilterGain.OrderBy(g => g.Y).ToList();
        bool gainUsed = gain.Any(g => Math.Abs(g.Value - 0.5) > 1e-9);
        var userLaser = audio.LaserEffects.Defs.Select(d => d.Name).ToHashSet();
        foreach (string preset in EffectCatalog.PresetLaser)
            if (resolve(preset, preset, null, audio.LaserEffects.ParamChanges, true, gainUsed && !userLaser.Contains(preset) ? gain : null) is { } e)
                laserEffects[preset] = e;
        foreach (var def in audio.LaserEffects.Defs)
            if (def.Name.Length > 0 && resolve(def.Name, def.Type, def.Params, audio.LaserEffects.ParamChanges, true, null) is { } e) laserEffects[def.Name] = e;

        SwitchAudioFiles = fxEffects.Values.Concat(laserEffects.Values).Where(e => e.Kind == EffectKind.SwitchAudio && !string.IsNullOrEmpty(e.Filename))
                                    .Select(e => e.Filename!).Distinct().ToList();

        // ---- FX holds
        bool any = false;
        for (int lane = 0; lane < 2; lane++)
        {
            var list = new List<FxHold>();
            foreach (var n in chart.Notes.Fx[lane].Where(n => n.IsHold).OrderBy(n => n.Y))
            {
                var events = n.Effects.OrderBy(e => e.RelY).Select(e => new FxEvent
                {
                    Y = n.Y + Math.Max(0, e.RelY),
                    Effect = e.Name.Length == 0 ? null : fxEffects.GetValueOrDefault(e.Name),
                    Overrides = overrides(fxEffects.GetValueOrDefault(e.Name), e.Params),
                }).ToArray();
                if (events.Any(e => e.Effect != null)) any = true;
                list.Add(new FxHold { Y = n.Y, End = n.EndY, StartMs = this.timing.PulseToMs(n.Y), Events = events });
            }
            holds[lane] = list.ToArray();
        }

        // ---- lasers and the filter switches
        for (int lane = 0; lane < 2; lane++)
        {
            lasers[lane] = chart.Notes.Lasers[lane].Where(s => s.Points.Count > 0).OrderBy(s => s.Y).Select(s => s.Clone()).ToArray();
            if (lasers[lane].Length > 0) any = true;
        }

        filterSwitches = audio.LaserEffects.PulseEvents
                              .SelectMany(kv => kv.Value.Select(y => (Y: y, Name: kv.Key)))
                              .OrderBy(e => e.Y)
                              .Select(e => (e.Y, e.Name.Length == 0 ? null : laserEffects.GetValueOrDefault(e.Name)))
                              .ToArray();
        defaultFilter = laserEffects.GetValueOrDefault("peaking_filter");
        HasEffects = any;
    }

    public TimingMap Timing => timing;

    /// <summary>An effect by name on a bus (definitions and presets), or null.</summary>
    public ResolvedEffect? Find(string name, bool laser) => (laser ? laserEffects : fxEffects).GetValueOrDefault(name);

    private static ResolvedEffect? resolve(string name, string type, Dictionary<string, string>? defParams,
                                           Dictionary<string, Dictionary<string, List<PulseValue<string>>>> paramChanges,
                                           bool laser, List<PulseValue<double>>? filterGain)
    {
        var kind = EffectSpecs.KindOf(type);
        if (kind == EffectKind.None) return null;
        var specs = EffectSpecs.Of(kind).ToArray();
        var @base = new EffectValueSet[specs.Length];
        var changes = new (long, EffectValueSet)[specs.Length][];
        paramChanges.TryGetValue(name, out var pc);

        for (int i = 0; i < specs.Length; i++)
        {
            var s = specs[i];
            @base[i] = s.Default;
            if (defParams != null && defParams.TryGetValue(s.Name, out var text) && EffectValueSet.TryParse(text, s.Kind, out var v))
                @base[i] = v;

            var list = new List<(long, EffectValueSet)>();
            if (pc != null && pc.TryGetValue(s.Name, out var raw))
                foreach (var p in raw)
                    if (EffectValueSet.TryParse(p.Value, s.Kind, out var cv)) list.Add((p.Y, cv));

            // legacy filter gain: K-Shoot writes it as parameter changes of the built-in filters (overwriting a user change on the same pulse)
            if (filterGain != null)
            {
                Func<double, EffectValue>? map = (kind, s.Id) switch
                {
                    (EffectKind.PeakingFilter, ParamId.Gain) => g => new EffectValue(Math.Round(g * 100) / 100),
                    (EffectKind.HighPassFilter, ParamId.Q) => g => new EffectValue(2.0 + (8.0 - 2.0) * g),
                    (EffectKind.LowPassFilter, ParamId.Q) => g => new EffectValue(2.0 + (5.2 - 2.0) * g),
                    _ => null,
                };
                if (map != null)
                    foreach (var g in filterGain)
                    {
                        list.RemoveAll(c => c.Item1 == g.Y);
                        list.Add((g.Y, EffectValueSet.Constant(map(Math.Clamp(g.Value, 0, 1)))));
                    }
            }

            changes[i] = list.OrderBy(c => c.Item1).ToArray();
        }

        string? filename = defParams != null && defParams.TryGetValue("filename", out var f) ? f : null;
        var result = new ResolvedEffect(name, kind, laser, filename, specs, @base, changes);

        int ut = result.IndexOf(ParamId.UpdateTrigger);
        if (ut >= 0)
            result.UpdateTriggerPulses = changes[ut].Where(c => c.Item2.Off.Number >= 0.5).Select(c => c.Item1).ToArray();
        return result;
    }

    private static EffectValueSet?[] overrides(ResolvedEffect? effect, Dictionary<string, string> p)
    {
        if (effect == null || p.Count == 0) return Array.Empty<EffectValueSet?>();
        var result = new EffectValueSet?[effect.Specs.Length];
        for (int i = 0; i < effect.Specs.Length; i++)
            if (p.TryGetValue(effect.Specs[i].Name, out var text) && EffectValueSet.TryParse(text, effect.Specs[i].Kind, out var v))
                result[i] = v;
        return result;
    }

    // ---- evaluation ------------------------------------------------------------------------------

    /// <summary>Fills <paramref name="frame"/> with the slots at song time <paramref name="ms"/> (audio ms, the chart's offset included).</summary>
    public void Evaluate(double ms, LiveEffectInput? input, EffectFrame frame)
    {
        double pulse = timing.MsToPulse(ms);
        double bpm = timing.BpmAt(Math.Max(0, pulse));
        foreach (var s in frame.Slots)
        {
            s.Clear();
            s.Bpm = bpm;
        }

        bool chart = input == null || input.ChartDriven;
        string? preview = input?.PreviewName;
        bool previewing = preview != null && ms >= input!.PreviewStartMs && ms < input.PreviewEndMs;

        if (previewing && !input!.PreviewLaser)
        {
            if (fxEffects.GetValueOrDefault(preview!) is { } pe)
                fill(frame.Slots[0], pe, null, pulse, ms, input.PreviewStartMs, 0, bpm);
        }
        else
        {
            for (int lane = 0; lane < 2; lane++)
            {
                var h = holdAt(holds[lane], pulse);
                if (h == null) continue;
                var ev = eventAt(h, pulse);
                if (ev?.Effect == null || ev.Effect.Kind == EffectKind.None) continue;
                if (!chart && !input!.FxDown[lane] && ms - h.StartMs >= input.GraceMs) continue;
                if (lane == 1 && frame.Slots[0].Active && frame.Slots[0].Effect == ev.Effect) continue; // one instance per effect
                double startMs = Math.Max(h.StartMs, timing.PulseToMs(ev.Y));
                fill(frame.Slots[lane], ev.Effect, ev.Overrides, pulse, ms, startMs, 0, bpm);
            }
        }

        // ---- laser bus
        if (previewing && input!.PreviewLaser)
        {
            if (laserEffects.GetValueOrDefault(preview!) is { } le)
            {
                double len = Math.Max(1, input.PreviewEndMs - input.PreviewStartMs);
                double x = (ms - input.PreviewStartMs) / len;
                double v = x < 0.5 ? x * 2 : 2 - x * 2; // a sweep 0 → 1 → 0
                fill(frame.Slots[2], le, null, pulse, ms, input.PreviewStartMs, v, bpm);
            }
            return;
        }

        var filter = filterAt(pulse);
        if (filter == null || filter.Kind == EffectKind.None) return;
        double samplePulse = filter.Kind == EffectKind.PeakingFilter && PeakingDelayMs > 0 ? timing.MsToPulse(ms - PeakingDelayMs) : pulse;

        double best = -1, activated = double.PositiveInfinity;
        for (int lane = 0; lane < 2; lane++)
        {
            if (!chart && !input!.LaserOn[lane]) continue;
            var s = sectionAt(lasers[lane], samplePulse);
            if (s == null) continue;
            double v = CameraEvaluator.ValueAt(s, samplePulse);
            if (lane == 1) v = 1 - v;
            best = Math.Max(best, Math.Clamp(v, 0, 1));
            activated = Math.Min(activated, s.Y);
        }

        if (best < 0) return;
        fill(frame.Slots[2], filter, null, pulse, ms, timing.PulseToMs(activated), best, bpm);
    }

    private void fill(EffectSlotState slot, ResolvedEffect e, EffectValueSet?[]? over, double pulse, double ms, double activatedMs, double v, double bpm)
    {
        slot.Active = true;
        slot.Kind = e.Kind;
        slot.Effect = e;
        slot.ActivatedMs = activatedMs;
        slot.V = v;
        slot.Bpm = bpm;

        var values = slot.Params.Values;
        Array.Clear(values);
        for (int i = 0; i < e.Specs.Length; i++)
        {
            var set = over != null && i < over.Length && over[i] is { } o ? o : e.At(i, pulse);
            double x = set.Sample(true, v, bpm);
            if (e.Specs[i].Kind == ParamKind.Int) x = Math.Round(x);
            values[(int)e.Specs[i].Id] = x;
        }

        slot.TriggerMs = triggerOf(e, over, pulse, ms, activatedMs, bpm);
    }

    private double triggerOf(ResolvedEffect e, EffectValueSet?[]? over, double pulse, double ms, double activatedMs, double bpm)
    {
        switch (e.Kind)
        {
            case EffectKind.Gate:
            case EffectKind.Wobble:
            {
                // K-Shoot: gate and wobble trigger on bar lines only
                long p = (long)Math.Floor(pulse);
                return timing.PulseToMs(timing.MeasureStart(timing.MeasureAt(p)));
            }

            case EffectKind.Retrigger:
            case EffectKind.Echo:
            {
                double t = double.NegativeInfinity;
                int up = e.IndexOf(ParamId.UpdatePeriod);
                if (up >= 0 && gridTrigger(valueSet(e, over, up, pulse).OnMin, pulse, bpm) is { } g) t = g;

                int ut = e.IndexOf(ParamId.UpdateTrigger);
                if (ut >= 0)
                {
                    var set = valueSet(e, over, ut, pulse);
                    if (set.OnMin.Number >= 0.5 && set.Off.Number < 0.5) t = Math.Max(t, activatedMs); // off>on: the press triggers
                    foreach (long y in e.UpdateTriggerPulses)
                        if (y <= pulse) t = Math.Max(t, timing.PulseToMs(y));
                }

                return double.IsNegativeInfinity(t) ? activatedMs : t;
            }

            case EffectKind.SideChain:
            {
                int pi = e.IndexOf(ParamId.Period);
                return pi >= 0 && gridTrigger(valueSet(e, over, pi, pulse).OnMin, pulse, bpm) is { } g ? g : activatedMs;
            }

            default:
                return activatedMs;
        }
    }

    private static EffectValueSet valueSet(ResolvedEffect e, EffectValueSet?[]? over, int i, double pulse)
        => over != null && i < over.Length && over[i] is { } o ? o : e.At(i, pulse);

    /// <summary>
    /// The last trigger of a period grid at or before <paramref name="pulse"/> (K-Shoot's
    /// <c>PrecalculateUpdateTriggerTimingByUpdatePeriodKey</c>): a period below one whole note restarts at every bar line
    /// and repeats every period; a longer one counts whole periods of measures from the song start. 0 = no grid.
    /// </summary>
    private double? gridTrigger(EffectValue period, double pulse, double bpm)
    {
        double frac = period.Unit switch
        {
            ValueUnit.WholeNotes => period.Number,
            ValueUnit.Seconds => period.Number * bpm / 240.0,
            _ => period.Number,
        };
        if (frac <= 0 || pulse < 0) return null;

        long p = (long)Math.Floor(pulse);
        int m = timing.MeasureAt(p);
        long start = timing.MeasureStart(m);
        if (frac < 1)
        {
            double step = frac * Timebase.PulsesPerWholeNote;
            if (step < 1) return null;
            double k = Math.Floor((pulse - start) / step);
            return timing.PulseToMs(start + k * step);
        }

        double measureValue = m + (pulse - start) / Math.Max(1, timing.MeasureLength(m));
        double tm = Math.Floor(measureValue / frac) * frac;
        int mi = (int)Math.Floor(tm);
        return timing.PulseToMs(timing.MeasureStart(mi) + (tm - mi) * timing.MeasureLength(mi));
    }

    private static FxHold? holdAt(FxHold[] list, double pulse)
    {
        int lo = 0, hi = list.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (list[mid].Y <= pulse) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found >= 0 && pulse < list[found].End ? list[found] : null;
    }

    private static FxEvent? eventAt(FxHold h, double pulse)
    {
        FxEvent? e = null;
        foreach (var ev in h.Events)
        {
            if (ev.Y > pulse) break;
            e = ev;
        }
        return e;
    }

    private static LaserSection? sectionAt(LaserSection[] list, double pulse)
    {
        int lo = 0, hi = list.Length - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (list[mid].Y <= pulse) { found = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return found >= 0 && pulse <= list[found].EndY ? list[found] : null;
    }

    private ResolvedEffect? filterAt(double pulse)
    {
        ResolvedEffect? f = defaultFilter;
        foreach (var (y, e) in filterSwitches)
        {
            if (y > pulse) break;
            f = e;
        }
        return f;
    }
}
