using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// kson audio effect types with their parameters and defaults (kson spec "Audio effects &amp;
/// parameter list", chart-format-model §5.1). Values use the kson <c>Off&gt;OnMin-OnMax</c> syntax.
/// </summary>
public static class EffectCatalog
{
    public sealed record ParamSpec(string Name, string Default, string Kind);

    public sealed record EffectType(string Type, string Label, IReadOnlyList<ParamSpec> Params, bool ForLaser);

    private static ParamSpec P(string name, string def, string kind) => new(name, def, kind);

    public static readonly IReadOnlyList<EffectType> Types = new[]
    {
        new EffectType("retrigger", "Retrigger", new[] { P("update_period", "1/2", "length"), P("wave_length", "0", "length"), P("rate", "70%", "rate"), P("update_trigger", "off", "switch"), P("mix", "0%>100%", "rate") }, false),
        new EffectType("gate", "Gate", new[] { P("wave_length", "0", "length"), P("rate", "50%", "rate"), P("mix", "0%>90%", "rate") }, false),
        new EffectType("flanger", "Flanger", new[] { P("period", "2.0", "length"), P("delay", "30samples", "sample"), P("depth", "45samples", "sample"), P("feedback", "60%", "rate"), P("stereo_width", "0%", "rate"), P("vol", "75%", "rate"), P("mix", "0%>80%", "rate") }, false),
        new EffectType("pitch_shift", "PitchShift", new[] { P("pitch", "0>12", "pitch"), P("chunk_size", "700samples", "sample"), P("overlap", "40%", "rate"), P("mix", "0%>100%", "rate") }, false),
        new EffectType("bitcrusher", "BitCrusher", new[] { P("reduction", "0samples-30samples", "sample"), P("mix", "0%>100%", "rate") }, true),
        new EffectType("phaser", "Phaser", new[] { P("period", "1/2", "length"), P("stage", "6", "int"), P("freq_1", "1500Hz", "freq"), P("freq_2", "20000Hz", "freq"), P("q", "0.707", "float"), P("feedback", "35%", "rate"), P("stereo_width", "75%", "rate"), P("hi_cut_gain", "-8dB", "dB"), P("mix", "0%>50%", "rate") }, false),
        new EffectType("wobble", "Wobble", new[] { P("wave_length", "0", "length"), P("freq_1", "500Hz", "freq"), P("freq_2", "20000Hz", "freq"), P("q", "1.414", "float"), P("mix", "0%>80%", "rate") }, false),
        new EffectType("tapestop", "TapeStop", new[] { P("speed", "50%", "rate"), P("trigger", "off>on", "switch"), P("mix", "0%>100%", "rate") }, false),
        new EffectType("echo", "Echo", new[] { P("update_period", "0", "length"), P("wave_length", "0", "length"), P("update_trigger", "off>on", "switch"), P("feedback_level", "60%", "rate"), P("mix", "0%>100%", "rate") }, false),
        new EffectType("sidechain", "SideChain", new[] { P("period", "1/4", "length"), P("hold_time", "50ms", "length"), P("attack_time", "10ms", "length"), P("release_time", "1/8", "length"), P("ratio", "1.0>5.0", "float") }, false),
        new EffectType("switch_audio", "SwitchAudio", new[] { P("filename", "", "filename") }, false),
        new EffectType("peaking_filter", "PeakingFilter", new[] { P("v", "0%-100%", "rate"), P("freq", "", "freq"), P("freq_max", "", "freq"), P("bandwidth", "", "float"), P("gain", "50%", "rate"), P("mix", "0%>100%", "rate") }, true),
        new EffectType("high_pass_filter", "HighPassFilter", new[] { P("v", "0%-100%", "rate"), P("freq", "", "freq"), P("freq_max", "", "freq"), P("q", "", "float"), P("mix", "0%>100%", "rate") }, true),
        new EffectType("low_pass_filter", "LowPassFilter", new[] { P("v", "0%-100%", "rate"), P("freq", "", "freq"), P("freq_max", "", "freq"), P("q", "", "float"), P("mix", "0%>100%", "rate") }, true),
    };

    public static EffectType? Find(string type) => Types.FirstOrDefault(t => string.Equals(t.Type, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>Effect names that work without a definition (kson presets, named after their type).</summary>
    public static readonly IReadOnlyList<string> PresetFx = Types.Where(t => !t.ForLaser || t.Type == "bitcrusher").Where(t => t.Type != "switch_audio").Select(t => t.Type).ToList();

    public static readonly IReadOnlyList<string> PresetLaser = new[] { "peaking_filter", "low_pass_filter", "high_pass_filter", "bitcrusher" };

    /// <summary>Built-in FX chip key sounds (kson <c>key_sound.fx.chip_event</c> names).</summary>
    public static readonly IReadOnlyList<string> BuiltinChipSounds = new[] { "clap", "clap_impact", "clap_punchy", "snare", "snare_lo" };

    /// <summary>A definition with every parameter at its default (empty defaults left out).</summary>
    public static AudioEffectDef NewDef(string name, string type)
    {
        var def = new AudioEffectDef(name, type);
        if (Find(type) is { } t)
            foreach (var p in t.Params.Where(p => p.Default.Length > 0))
                def.Params[p.Name] = p.Default;
        return def;
    }

    /// <summary>Effect names offered for an FX hold / laser section: user definitions first, then presets not redefined.</summary>
    public static IReadOnlyList<string> AvailableNames(Chart chart, bool laser)
    {
        var defs = (laser ? chart.Audio.LaserEffects.Defs : chart.Audio.FxEffects.Defs).Select(d => d.Name);
        return defs.Concat(laser ? PresetLaser : PresetFx).Where(n => n.Length > 0).Distinct().ToList();
    }

    /// <summary>The type an effect name resolves to (its definition, else the preset of that name).</summary>
    public static string? TypeOf(Chart chart, string name, bool laser)
    {
        var def = (laser ? chart.Audio.LaserEffects.Defs : chart.Audio.FxEffects.Defs).FirstOrDefault(d => d.Name == name);
        if (def != null) return def.Type;
        return Find(name)?.Type;
    }
}

/// <summary>Adds an effect definition (FX holds or laser filters).</summary>
public sealed class AddEffectDefCommand : IEditCommand
{
    public AddEffectDefCommand(bool laser, AudioEffectDef def)
    {
        Laser = laser;
        Def = def;
    }

    public bool Laser { get; }
    public AudioEffectDef Def { get; }
    public string Description => Laser ? "Add laser effect" : "Add FX effect";

    public void Execute(Chart chart)
    {
        var defs = EffectCommands.Defs(chart, Laser);
        if (defs.Any(d => d.Name == Def.Name)) throw new InvalidOperationException($"an effect named '{Def.Name}' exists");
        defs.Add(Def);
    }

    public void Undo(Chart chart) => EffectCommands.Defs(chart, Laser).Remove(Def);
}

public sealed class RemoveEffectDefCommand : IEditCommand
{
    private int index;

    public RemoveEffectDefCommand(bool laser, AudioEffectDef def)
    {
        Laser = laser;
        Def = def;
    }

    public bool Laser { get; }
    public AudioEffectDef Def { get; }
    public string Description => Laser ? "Delete laser effect" : "Delete FX effect";

    public void Execute(Chart chart)
    {
        var defs = EffectCommands.Defs(chart, Laser);
        index = defs.IndexOf(Def);
        if (index < 0) throw new InvalidOperationException("definition is not in the chart");
        defs.RemoveAt(index);
    }

    public void Undo(Chart chart) => EffectCommands.Defs(chart, Laser).Insert(index, Def);
}

/// <summary>
/// Changes name, type and parameters of a definition. A rename also renames every use: FX hold
/// events and orphan events, laser filter switches and the parameter changes of that effect.
/// </summary>
public sealed class EditEffectDefCommand : IEditCommand
{
    private readonly string name;
    private readonly string type;
    private readonly Dictionary<string, string> parameters;
    private (string Name, string Type, Dictionary<string, string> Params) old;

    public EditEffectDefCommand(bool laser, AudioEffectDef def, string name, string type, IReadOnlyDictionary<string, string> parameters)
    {
        Laser = laser;
        Def = def;
        this.name = name;
        this.type = type;
        this.parameters = new Dictionary<string, string>(parameters);
    }

    public bool Laser { get; }
    public AudioEffectDef Def { get; }
    public string Description => "Edit effect";

    public void Execute(Chart chart)
    {
        if (name != Def.Name && EffectCommands.Defs(chart, Laser).Any(d => d != Def && d.Name == name))
            throw new InvalidOperationException($"an effect named '{name}' exists");
        old = (Def.Name, Def.Type, Def.Params);
        Apply(chart, name, type, new Dictionary<string, string>(parameters));
    }

    public void Undo(Chart chart) => Apply(chart, old.Name, old.Type, old.Params);

    private void Apply(Chart chart, string n, string t, Dictionary<string, string> p)
    {
        if (n != Def.Name) EffectCommands.RenameUses(chart, Laser, Def.Name, n);
        Def.Name = n;
        Def.Type = t;
        Def.Params = p;
    }
}

/// <summary>Replaces the effect events of FX holds (empty list = no effect).</summary>
public sealed class SetHoldEffectsCommand : IEditCommand
{
    private readonly List<Note> holds;
    private readonly List<FxEffectEvent> effects;
    private readonly List<List<FxEffectEvent>> old = new();

    public SetHoldEffectsCommand(IEnumerable<Note> holds, IEnumerable<FxEffectEvent> effects, string description = "Assign FX effect")
    {
        this.holds = holds.Where(n => n.IsHold).ToList();
        this.effects = effects.Select(e => e.Clone()).ToList();
        Description = description;
    }

    public string Description { get; }
    public int Count => holds.Count;

    public void Execute(Chart chart)
    {
        old.Clear();
        foreach (var h in holds)
        {
            old.Add(h.Effects);
            h.Effects = effects.Select(e => e.Clone()).Where(e => e.RelY < h.Length).OrderBy(e => e.RelY).ToList();
        }
    }

    public void Undo(Chart chart)
    {
        for (int i = 0; i < holds.Count; i++) holds[i].Effects = old[i];
    }
}

/// <summary>Sets (or clears, with null) the key sound of FX chips.</summary>
public sealed class SetChipKeySoundCommand : IEditCommand
{
    private readonly List<Note> chips;
    private readonly KeySoundRef? sound;
    private readonly List<KeySoundRef?> old = new();

    public SetChipKeySoundCommand(IEnumerable<Note> chips, KeySoundRef? sound, string? description = null)
    {
        this.chips = chips.Where(n => !n.IsHold).ToList();
        this.sound = sound;
        Description = description ?? (sound == null ? "Clear chip sound" : "Set chip sound");
    }

    public string Description { get; }
    public int Count => chips.Count;

    public void Execute(Chart chart)
    {
        old.Clear();
        foreach (var c in chips)
        {
            old.Add(c.KeySound);
            c.KeySound = sound;
        }
    }

    public void Undo(Chart chart)
    {
        for (int i = 0; i < chips.Count; i++) chips[i].KeySound = old[i];
    }
}

/// <summary>Adds, replaces or (value null) removes a parameter change of an effect at a pulse.</summary>
public sealed class SetParamChangeCommand : IEditCommand
{
    private readonly string effect;
    private readonly string param;
    private readonly long y;
    private readonly string? value;
    private Dictionary<string, Dictionary<string, List<PulseValue<string>>>>? snapshot;

    public SetParamChangeCommand(bool laser, string effect, string param, long y, string? value)
    {
        Laser = laser;
        this.effect = effect;
        this.param = param;
        this.y = y;
        this.value = value;
    }

    public bool Laser { get; }
    public string Description => value == null ? "Delete parameter change" : "Set parameter change";

    public void Execute(Chart chart)
    {
        var changes = EffectCommands.ParamChanges(chart, Laser);
        snapshot = EffectCommands.CloneChanges(changes);
        if (!changes.TryGetValue(effect, out var prms)) changes[effect] = prms = new();
        if (!prms.TryGetValue(param, out var list)) prms[param] = list = new();
        list.RemoveAll(p => p.Y == y);
        if (value != null) list.Insert(list.FindLastIndex(p => p.Y <= y) + 1, new PulseValue<string>(y, value));
        if (list.Count == 0) prms.Remove(param);
        if (prms.Count == 0) changes.Remove(effect);
    }

    public void Undo(Chart chart) => EffectCommands.SetParamChanges(chart, Laser, snapshot!);
}

/// <summary>Sets the laser filter that switches on at <paramref name="y"/> (null = remove every switch there).</summary>
public sealed class SetLaserFilterCommand : IEditCommand
{
    private readonly string? name;
    private readonly long y;
    private Dictionary<string, List<long>>? snapshot;

    public SetLaserFilterCommand(string? name, long y)
    {
        this.name = name;
        this.y = y;
    }

    public string Description => name == null ? "Remove laser filter" : "Set laser filter";

    public void Execute(Chart chart)
    {
        var events = chart.Audio.LaserEffects.PulseEvents;
        snapshot = events.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        foreach (var list in events.Values) list.Remove(y);
        foreach (var key in events.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList()) events.Remove(key);
        if (name != null)
        {
            if (!events.TryGetValue(name, out var list)) events[name] = list = new();
            list.Insert(list.FindLastIndex(p => p <= y) + 1, y);
        }
    }

    public void Undo(Chart chart) => chart.Audio.LaserEffects.PulseEvents = snapshot!;
}

/// <summary>Sets or (null) removes a value of a <c>ByPulse&lt;double&gt;</c> list (filter gain, slam volume).</summary>
public sealed class SetPulseValueCommand : IEditCommand
{
    private readonly Func<Chart, List<PulseValue<double>>> list;
    private readonly long y;
    private readonly double? value;
    private List<PulseValue<double>>? snapshot;

    public SetPulseValueCommand(string description, Func<Chart, List<PulseValue<double>>> list, long y, double? value)
    {
        Description = description;
        this.list = list;
        this.y = y;
        this.value = value;
    }

    public string Description { get; }

    public void Execute(Chart chart)
    {
        var l = list(chart);
        snapshot = l.ToList();
        l.RemoveAll(p => p.Y == y);
        if (value is { } v) l.Insert(l.FindLastIndex(p => p.Y <= y) + 1, new PulseValue<double>(y, v));
    }

    public void Undo(Chart chart)
    {
        var l = list(chart);
        l.Clear();
        l.AddRange(snapshot!);
    }
}

public static class EffectCommands
{
    public static List<AudioEffectDef> Defs(Chart chart, bool laser) => laser ? chart.Audio.LaserEffects.Defs : chart.Audio.FxEffects.Defs;

    public static Dictionary<string, Dictionary<string, List<PulseValue<string>>>> ParamChanges(Chart chart, bool laser)
        => laser ? chart.Audio.LaserEffects.ParamChanges : chart.Audio.FxEffects.ParamChanges;

    internal static void SetParamChanges(Chart chart, bool laser, Dictionary<string, Dictionary<string, List<PulseValue<string>>>> value)
    {
        if (laser) chart.Audio.LaserEffects.ParamChanges = value;
        else chart.Audio.FxEffects.ParamChanges = value;
    }

    internal static Dictionary<string, Dictionary<string, List<PulseValue<string>>>> CloneChanges(Dictionary<string, Dictionary<string, List<PulseValue<string>>>> c)
        => c.ToDictionary(e => e.Key, e => e.Value.ToDictionary(p => p.Key, p => p.Value.ToList()));

    /// <summary>Slam sound volume (ksh <c>chokkakuvol</c>, 0..1).</summary>
    public static SetPulseValueCommand SetSlamVolume(long y, double? volume)
        => new("Set slam volume", c => c.Audio.KeySound.LaserVolume, y, volume);

    /// <summary>Peaking filter gain (ksh <c>pfiltergain</c>, 0..1).</summary>
    public static SetPulseValueCommand SetFilterGain(long y, double? gain)
        => new("Set filter gain", c => c.Audio.LaserEffects.LegacyFilterGain, y, gain);

    public static SetValueCommand<int?> SetPeakingFilterDelay(int? ms)
        => new("Set peaking filter delay", c => c.Audio.LaserEffects.PeakingFilterDelay, (c, v) => c.Audio.LaserEffects.PeakingFilterDelay = v, ms);

    /// <summary>Assigns one effect (with optional parameter overrides) to every hold, starting at the hold start; null clears.</summary>
    public static SetHoldEffectsCommand AssignToHolds(IEnumerable<Note> holds, string? effect, IReadOnlyDictionary<string, string>? parameters = null)
        => new(holds, effect == null ? Array.Empty<FxEffectEvent>() : new[] { new FxEffectEvent(effect, 0, parameters?.ToDictionary(kv => kv.Key, kv => kv.Value)) },
            effect == null ? "Clear FX effect" : $"Assign {effect}");

    /// <summary>How many FX holds use an effect name (for the "in use" count and delete warnings).</summary>
    public static int Uses(Chart chart, string name, bool laser)
    {
        if (laser) return chart.Audio.LaserEffects.PulseEvents.TryGetValue(name, out var ys) ? ys.Count : 0;
        return chart.Notes.Fx.Sum(l => l.Count(n => n.Effects.Any(e => e.Name == name)));
    }

    internal static void RenameUses(Chart chart, bool laser, string from, string to)
    {
        var changes = ParamChanges(chart, laser);
        if (changes.Remove(from, out var prms)) changes[to] = prms;

        if (laser)
        {
            var pe = chart.Audio.LaserEffects.PulseEvents;
            if (pe.Remove(from, out var ys)) pe[to] = ys;
            return;
        }

        foreach (var lane in chart.Notes.Fx)
            foreach (var n in lane)
                foreach (var e in n.Effects)
                    if (e.Name == from) e.Name = to;
        var orphans = chart.Audio.FxEffects.OrphanLongEvents;
        for (int i = 0; i < orphans.Count; i++)
            if (orphans[i].Name == from) orphans[i] = orphans[i] with { Name = to };
    }

    /// <summary>The laser filter in effect at <paramref name="pulse"/> (last switch at or before it), or null.</summary>
    public static string? FilterAt(Chart chart, long pulse)
    {
        string? name = null;
        long best = long.MinValue;
        foreach (var (n, ys) in chart.Audio.LaserEffects.PulseEvents)
            foreach (long y in ys)
                if (y <= pulse && y >= best)
                {
                    best = y;
                    name = n;
                }
        return name;
    }

    /// <summary>Value of a <c>ByPulse&lt;double&gt;</c> list at a pulse (last at or before), or the default.</summary>
    public static double ValueAt(IReadOnlyList<PulseValue<double>> list, long pulse, double fallback)
    {
        double v = fallback;
        foreach (var p in list)
        {
            if (p.Y > pulse) break;
            v = p.Value;
        }
        return v;
    }
}

/// <summary>Custom FX chip sounds: audio files in the song folder.</summary>
public static class KeySoundFiles
{
    public static readonly IReadOnlyList<string> Extensions = new[] { ".wav", ".ogg" };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Key-sound candidates in the folder (wav/ogg, excluding the song audio).</summary>
    public static IReadOnlyList<string> List(string folder, string? bgmFilename = null)
    {
        if (!Directory.Exists(folder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(folder)
                        .Where(IsSupported)
                        .Select(Path.GetFileName)
                        .Where(n => n != null && !string.Equals(n, bgmFilename, StringComparison.OrdinalIgnoreCase))
                        .Select(n => n!)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList();
    }

    /// <summary>
    /// Copies a .wav/.ogg into the song folder and returns the file name the chart refers to. A file of
    /// the same name and content is reused; a different file of the same name gets a numbered name.
    /// The copy is not part of the undo history (undo only removes the assignment).
    /// </summary>
    public static string Import(string folder, string source)
    {
        if (!IsSupported(source)) throw new ArgumentException($"only {string.Join("/", Extensions)} can be used as chip sounds", nameof(source));
        if (!File.Exists(source)) throw new FileNotFoundException("sound file not found", source);

        string name = Path.GetFileName(source);
        string stem = Path.GetFileNameWithoutExtension(source), ext = Path.GetExtension(source);
        var bytes = File.ReadAllBytes(source);
        for (int i = 1; ; i++)
        {
            string target = Path.Combine(folder, name);
            if (Path.GetFullPath(target).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) return name;
            if (!File.Exists(target))
            {
                File.WriteAllBytes(target, bytes);
                return name;
            }
            if (File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes)) return name;
            name = $"{stem}_{i + 1}{ext}";
        }
    }
}
