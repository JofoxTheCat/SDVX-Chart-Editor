namespace SDVXEditor.Core.Charts;

public sealed class AudioInfo
{
    public BgmInfo Bgm { get; set; } = new();
    public KeySoundInfo KeySound { get; set; } = new();
    public FxEffectInfo FxEffects { get; set; } = new();
    public LaserEffectInfo LaserEffects { get; set; } = new();
}

public sealed class BgmInfo
{
    public string Filename { get; set; } = "";

    /// <summary>1.0 = 100 % (ksh <c>mvol=100</c>).</summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>Audio offset in ms: chart pulse 0 plays at this audio time (ksh <c>o=</c>).</summary>
    public long OffsetMs { get; set; }

    public long PreviewOffsetMs { get; set; }
    public long PreviewDurationMs { get; set; } = 15000;

    /// <summary>ksh <c>m=a;a_f;a_p;a_fp</c> pre-rendered effect tracks (everything after the first name).</summary>
    public List<string> LegacyFpFilenames { get; set; } = new();
}

public sealed class KeySoundInfo
{
    /// <summary>kson chip events whose pulse matches no FX chip (kept so nothing is lost).</summary>
    public List<OrphanKeySound> OrphanFxChipEvents { get; set; } = new();

    /// <summary>Slam sound volume, 0..1 (ksh <c>chokkakuvol</c>/100).</summary>
    public List<PulseValue<double>> LaserVolume { get; set; } = new();

    /// <summary>slam_up / slam_down / slam_swing / slam_mute / custom filename → pulses.</summary>
    public Dictionary<string, List<long>> LaserSlamEvents { get; set; } = new();

    /// <summary>ksh <c>chokkakuautovol</c> (kson <c>key_sound.laser.legacy.vol_auto</c>).</summary>
    public bool? LaserVolumeAuto { get; set; }
}

public sealed record OrphanKeySound(string Name, int Lane, long Y, double? Volume);

/// <summary>An effect definition; parameter values use the kson "Off&gt;OnMin-OnMax" string syntax.</summary>
public sealed class AudioEffectDef
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public Dictionary<string, string> Params { get; set; } = new();

    public AudioEffectDef() { }
    public AudioEffectDef(string name, string type, Dictionary<string, string>? parameters = null)
    {
        Name = name;
        Type = type;
        Params = parameters ?? new();
    }
}

public sealed class FxEffectInfo
{
    public List<AudioEffectDef> Defs { get; set; } = new();

    /// <summary>effect name → parameter → changes over time.</summary>
    public Dictionary<string, Dictionary<string, List<PulseValue<string>>>> ParamChanges { get; set; } = new();

    /// <summary>kson long events that lie on no FX hold.</summary>
    public List<OrphanFxEvent> OrphanLongEvents { get; set; } = new();
}

public sealed record OrphanFxEvent(string Name, int Lane, long Y, Dictionary<string, string> Params);

public sealed class LaserEffectInfo
{
    public List<AudioEffectDef> Defs { get; set; } = new();
    public Dictionary<string, Dictionary<string, List<PulseValue<string>>>> ParamChanges { get; set; } = new();

    /// <summary>Filter switches: effect name → pulses (ksh <c>filtertype=</c>).</summary>
    public Dictionary<string, List<long>> PulseEvents { get; set; } = new();

    /// <summary>ms, 0..160 (ksh <c>pfilterdelay</c>). Null = not stated (default 40).</summary>
    public int? PeakingFilterDelay { get; set; }

    /// <summary>0..1 (ksh <c>pfiltergain</c>/100).</summary>
    public List<PulseValue<double>> LegacyFilterGain { get; set; } = new();
}
