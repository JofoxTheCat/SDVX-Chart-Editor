namespace SDVXEditor.Core.Charts;

public enum TiltMode
{
    Normal,
    Bigger,
    Biggest,
    KeepNormal,
    KeepBigger,
    KeepBiggest,
    Zero,
}

/// <summary>A tilt value: either an automatic mode or a manual number (-100..100, ±1 = laser at the edge).</summary>
public readonly record struct TiltValue(TiltMode? Auto, double Manual)
{
    public static TiltValue Of(TiltMode mode) => new(mode, 0);
    public static TiltValue Of(double manual) => new(null, manual);

    public bool IsAuto => Auto.HasValue;

    public static readonly IReadOnlyDictionary<string, TiltMode> Names = new Dictionary<string, TiltMode>
    {
        ["normal"] = TiltMode.Normal,
        ["bigger"] = TiltMode.Bigger,
        ["biggest"] = TiltMode.Biggest,
        ["keep_normal"] = TiltMode.KeepNormal,
        ["keep_bigger"] = TiltMode.KeepBigger,
        ["keep_biggest"] = TiltMode.KeepBiggest,
        ["zero"] = TiltMode.Zero,
    };

    public static string NameOf(TiltMode mode) => Names.First(kv => kv.Value == mode).Key;

    public override string ToString() => Auto is { } m ? NameOf(m) : Manual.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A tilt change. <see cref="Final"/> set = instant change at Y; <see cref="Curve"/> shapes the manual segment.</summary>
public sealed class TiltPoint
{
    public long Y { get; set; }
    public TiltValue Value { get; set; }
    public TiltValue? Final { get; set; }
    public Curve? Curve { get; set; }

    public TiltPoint() { }
    public TiltPoint(long y, TiltValue value, TiltValue? final = null, Curve? curve = null)
    {
        Y = y;
        Value = value;
        Final = final;
        Curve = curve;
    }

    public TiltPoint Clone() => new(Y, Value, Final, Curve);
}

/// <summary>Camera graphs (kson <c>camera.cam.body</c>).</summary>
public sealed class CamBody
{
    public List<GraphPoint> ZoomTop { get; set; } = new();
    public List<GraphPoint> ZoomBottom { get; set; } = new();
    public List<GraphPoint> ZoomSide { get; set; } = new();
    public List<GraphPoint> CenterSplit { get; set; } = new();
    public List<GraphPoint> RotationDeg { get; set; } = new();

    public IEnumerable<List<GraphPoint>> All => new[] { ZoomTop, ZoomBottom, ZoomSide, CenterSplit, RotationDeg };
}

/// <summary>
/// Spin / half spin / swing bound to a slam. Direction -1 left, 1 right; Length in pulses.
/// The swing-only parameters are null when not stated.
/// </summary>
public sealed class CamPattern
{
    public long Y { get; set; }
    public int Direction { get; set; } = 1;
    public long Length { get; set; }
    public double? SwingScale { get; set; }
    public int? SwingRepeat { get; set; }
    public int? SwingDecayOrder { get; set; }

    public CamPattern() { }
    public CamPattern(long y, int direction, long length)
    {
        Y = y;
        Direction = direction;
        Length = length;
    }

    public CamPattern Clone() => (CamPattern)MemberwiseClone();
}

public sealed class CameraInfo
{
    public List<TiltPoint> Tilt { get; set; } = new();
    public CamBody Body { get; set; } = new();
    public List<CamPattern> Spin { get; set; } = new();
    public List<CamPattern> HalfSpin { get; set; } = new();
    public List<CamPattern> Swing { get; set; } = new();

    /// <summary>
    /// Editor-side anchors of curved camera runs (kson <c>impl["sdvx-editor"].camera_paths</c>). Only trusted while they
    /// still bake to the run's points — read them through <see cref="CameraPaths.Runs"/>, never directly.
    /// </summary>
    public List<CameraAnchorPath> Paths { get; set; } = new();
}

/// <summary>Legacy KSM background data (kson <c>bg</c>).</summary>
public sealed class BgInfo
{
    /// <summary>kson <c>bg.filename</c> (reserved by the spec).</summary>
    public string Filename { get; set; } = "";

    /// <summary>1–2 background images/presets (ksh <c>bg=a;b</c>).</summary>
    public List<string> LegacyBg { get; set; } = new();

    public BgLayer? Layer { get; set; }
    public BgMovie? Movie { get; set; }
}

public sealed class BgLayer
{
    public string Filename { get; set; } = "";

    /// <summary>ms per loop; &lt; 0 reverse, 0 = tempo synced.</summary>
    public long DurationMs { get; set; }

    public bool RotateTilt { get; set; } = true;
    public bool RotateSpin { get; set; } = true;
}

public sealed class BgMovie
{
    public string Filename { get; set; } = "";
    public long OffsetMs { get; set; }
}
