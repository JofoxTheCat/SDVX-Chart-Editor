namespace SDVXEditor.Core.Charts;

public enum NoteKind
{
    Bt,
    Fx,
}

/// <summary>
/// A BT or FX note. <see cref="Length"/> 0 = chip, &gt; 0 = hold.
/// FX notes may carry effect assignments (holds) or a key sound (chips).
/// </summary>
public sealed class Note
{
    public long Y { get; set; }
    public long Length { get; set; }

    /// <summary>FX holds only: effect invocations (kson <c>audio.audio_effect.fx.long_event</c>).</summary>
    public List<FxEffectEvent> Effects { get; set; } = new();

    /// <summary>FX chips only: custom key sound (kson <c>audio.key_sound.fx.chip_event</c>).</summary>
    public KeySoundRef? KeySound { get; set; }

    public Note() { }
    public Note(long y, long length = 0) { Y = y; Length = length; }

    public bool IsHold => Length > 0;
    public long EndY => Y + Length;

    public Note Clone() => new(Y, Length)
    {
        Effects = Effects.Select(e => e.Clone()).ToList(),
        KeySound = KeySound,
    };

    public override string ToString() => IsHold ? $"hold {Y}+{Length}" : $"chip {Y}";
}

/// <summary>An effect invoked on an FX hold. <see cref="RelY"/> is relative to the hold start (0 = at start).</summary>
public sealed class FxEffectEvent
{
    public string Name { get; set; } = "";
    public long RelY { get; set; }
    public Dictionary<string, string> Params { get; set; } = new();

    public FxEffectEvent() { }
    public FxEffectEvent(string name, long relY = 0, Dictionary<string, string>? parameters = null)
    {
        Name = name;
        RelY = relY;
        Params = parameters ?? new();
    }

    public FxEffectEvent Clone() => new(Name, RelY, new Dictionary<string, string>(Params));
}

/// <summary>A key sound on an FX chip; <see cref="Volume"/> null = default (1.0).</summary>
public sealed record KeySoundRef(string Name, double? Volume = null);

/// <summary>One laser control point. Values are 0..1; <see cref="Vf"/> set = slam from V to Vf.</summary>
public sealed class LaserPoint
{
    /// <summary>Pulse relative to the section start.</summary>
    public long Ry { get; set; }
    public double V { get; set; }
    public double? Vf { get; set; }
    public Curve? Curve { get; set; }

    public LaserPoint() { }
    public LaserPoint(long ry, double v, double? vf = null, Curve? curve = null)
    {
        Ry = ry;
        V = v;
        Vf = vf;
        Curve = curve;
    }

    public bool IsSlam => Vf.HasValue && Vf.Value != V;
    public double ValueAfter => Vf ?? V;

    public LaserPoint Clone() => new(Ry, V, Vf, Curve);
    public override string ToString() => IsSlam ? $"{Ry}: {V}->{Vf}" : $"{Ry}: {V}";
}

/// <summary>A continuous laser path. <see cref="Width"/> 2 = wide (2x) laser.</summary>
public sealed class LaserSection
{
    public long Y { get; set; }
    public List<LaserPoint> Points { get; set; } = new();
    public int Width { get; set; } = 1;

    public LaserSection() { }
    public LaserSection(long y, IEnumerable<LaserPoint>? points = null, int width = 1)
    {
        Y = y;
        Points = points?.ToList() ?? new();
        Width = width;
    }

    /// <summary>
    /// Editor-side anchors with curve types (osu!lazer slider path); <see cref="Points"/> are its bake. Only trusted
    /// while it matches the points — use <see cref="LaserPaths.ValidPath"/>, never this property directly.
    /// </summary>
    public LaserPath? Path { get; set; }

    public long EndY => Points.Count == 0 ? Y : Y + Points[^1].Ry;
    public int SlamCount => Points.Count(p => p.IsSlam);

    public LaserSection Clone() => new(Y, Points.Select(p => p.Clone()), Width) { Path = Path?.Clone() };
}

public sealed class NoteData
{
    public List<Note>[] Bt { get; set; } = { new(), new(), new(), new() };
    public List<Note>[] Fx { get; set; } = { new(), new() };
    public List<LaserSection>[] Lasers { get; set; } = { new(), new() };

    public List<Note> Lane(NoteKind kind, int lane) => kind == NoteKind.Bt ? Bt[lane] : Fx[lane];

    public static int LaneCount(NoteKind kind) => kind == NoteKind.Bt ? 4 : 2;

    /// <summary>Inserts keeping the lane sorted by Y (stable after equal Y).</summary>
    public static int InsertSorted(List<Note> lane, Note note)
    {
        int i = lane.FindLastIndex(n => n.Y <= note.Y) + 1;
        lane.Insert(i, note);
        return i;
    }

    public static int InsertSorted(List<LaserSection> lane, LaserSection section)
    {
        int i = lane.FindLastIndex(s => s.Y <= section.Y) + 1;
        lane.Insert(i, section);
        return i;
    }

    public void SortAll()
    {
        foreach (var l in Bt.Concat(Fx)) l.Sort((a, b) => a.Y.CompareTo(b.Y));
        foreach (var l in Lasers) l.Sort((a, b) => a.Y.CompareTo(b.Y));
    }
}
