namespace SDVXEditor.Core.Charts;

/// <summary>
/// Editor-side data that only a VOX export can use (kson <c>impl["sdvx-editor"].vox</c>, ksh <c>//#vox_…</c> comment
/// lines, see docs/VOX_EFFECTS.md). Neither kson nor ksh has a field for any of it; ksh2vox 0.20.0 writes all of it,
/// 0.17.0–0.19.0 only <c>bg_no</c>, the post effects (stage 0, instance 0) and the <c>sp_controller</c> rows.
/// </summary>
public sealed class VoxExtrasInfo
{
    /// <summary>music_db <c>&lt;bg_no&gt;</c> (game background). Null = converter default (0).</summary>
    public int? BgNo { get; set; }

    /// <summary><c>#POSTEFFECT</c> rows. Empty = the converter template's neutral rows.</summary>
    public List<PostEffectRow> PostEffects { get; set; } = new();

    /// <summary>
    /// Raw <c>#SPCONTROLER</c> base rows (Realize, AIRL_ScaX …) that <b>replace</b> the template's rows. Empty = the
    /// template defaults. Game effects go to <see cref="Commands"/> instead, which are appended.
    /// </summary>
    public List<SpControllerRow> SpController { get; set; } = new();

    /// <summary>
    /// Game commands appended to <c>#SPCONTROLER</c> (LaneY, BIL_RotZ, Morphing2, BAR, BAROFF, SpecialN), values as VOX
    /// tokens. Only commands the game reads (<see cref="VoxCommands"/>); ksh2vox 0.20.0+ writes them.
    /// </summary>
    public List<VoxCommandRow> Commands { get; set; } = new();

    /// <summary><c>#BPM OPTION RepresentativeBpm</c>: the tempo the game's hi-speed is based on. Null = not written.</summary>
    public double? RepresentativeBpm { get; set; }

    /// <summary><c>#BPM OPTION ConstantScroll</c> (0/1; what the game does with it is unknown). Null = not written.</summary>
    public bool? ConstantScroll { get; set; }

    /// <summary>Note scripts (<c>#SCRIPT_DEFINE</c>), by id.</summary>
    public List<VoxScript> Scripts { get; set; } = new();

    /// <summary>Script assignments (<c>#SCRIPTED_TRACKn</c>): the note starting at Y on a VOX track runs these scripts.</summary>
    public List<ScriptedNote> Scripted { get; set; } = new();

    public bool IsEmpty => BgNo == null && PostEffects.Count == 0 && SpController.Count == 0 && Commands.Count == 0
                           && RepresentativeBpm == null && ConstantScroll == null && Scripts.Count == 0 && Scripted.Count == 0;

    /// <summary>True when there is anything a .ksh needs <c>//#vox_…</c> lines (or lane_toggle / bg_no) for.</summary>
    public bool HasGameEffects => PostEffects.Count > 0 || Commands.Count > 0 || RepresentativeBpm != null || ConstantScroll != null
                                  || Scripts.Count > 0 || Scripted.Count > 0;

    public VoxExtrasInfo Clone() => new()
    {
        BgNo = BgNo,
        PostEffects = PostEffects.Select(p => p.Clone()).ToList(),
        SpController = SpController.Select(r => r.Clone()).ToList(),
        Commands = Commands.Select(r => r.Clone()).ToList(),
        RepresentativeBpm = RepresentativeBpm,
        ConstantScroll = ConstantScroll,
        Scripts = Scripts.Select(s => s.Clone()).ToList(),
        Scripted = Scripted.Select(s => s.Clone()).ToList(),
    };

    /// <summary>The eight post effects the game knows, in its id order (<see cref="PostEffects"/> catalogue).</summary>
    public static IReadOnlyList<string> PostEffectNames => Charts.PostEffects.Names;

    /// <summary>The converter template's default SPCONTROLER rows (CLAUDE.md §6), offered as a preset.</summary>
    public static IReadOnlyList<SpControllerRow> TemplateSpController => new[]
    {
        new SpControllerRow(0, "AIRL_ScaX", 1, 0, 0, 1, 0, 0),
        new SpControllerRow(0, "AIRR_ScaX", 1, 0, 0, 2, 0, 0),
        new SpControllerRow(0, "Realize", 3, 0, 17.12, 60.12, 110.12, 0),
        new SpControllerRow(0, "Realize", 4, 0, 0.28, 0.72, 1.57, 0),
    };

    /// <summary>Sorts every row list by time (stable), as the formats store them.</summary>
    public void Sort()
    {
        PostEffects = PostEffects.OrderBy(p => p.Y).ToList();
        Commands = Commands.OrderBy(c => c.Y).ToList();
        Scripted = Scripted.OrderBy(s => s.Y).ThenBy(s => s.Track).ToList();
        Scripts = Scripts.OrderBy(s => s.Id).ToList();
    }
}

/// <summary>
/// One <c>#POSTEFFECT</c> row: <c>pos type len Effect stage index Param from to</c>. <see cref="Name"/>'s
/// <see cref="Param"/> goes from <see cref="From"/> to <see cref="To"/> over <see cref="Length"/> pulses
/// (0 = instant), or over <see cref="LengthMs"/> milliseconds when that is set.
/// </summary>
public sealed class PostEffectRow
{
    public long Y { get; set; }

    private string name = "ChromaticAberration";

    /// <summary>Effect name; the official misspelling <c>ChromaticAbberation</c> is normalised on assignment.</summary>
    public string Name
    {
        get => name;
        set => name = Charts.PostEffects.Normalise(value);
    }

    public string Param { get; set; } = "Strength";
    public double From { get; set; }
    public double To { get; set; }
    public long Length { get; set; }

    /// <summary>Post-effect stage of the song's background: 0 = background ("BgEffect"), 1 = "Overall" (the lane).</summary>
    public int Stage { get; set; }

    /// <summary>Instance of this effect type inside the stage (official 2087: two ColorConversions, index 0 and 1).</summary>
    public int Index { get; set; }

    /// <summary>Length in ms instead of pulses (VOX <c>500ms</c>); null = <see cref="Length"/>.</summary>
    public double? LengthMs { get; set; }

    public PostEffectRow() { }

    public PostEffectRow(long y, string name, string param, double from, double to, long length, int stage = 0, int index = 0, double? lengthMs = null)
    {
        Y = y;
        Name = name;
        Param = param;
        From = from;
        To = to;
        Length = length;
        Stage = stage;
        Index = index;
        LengthMs = lengthMs;
    }

    public bool IsInstant => LengthMs is { } ms ? ms <= 0 : Length <= 0;

    public PostEffectRow Clone() => (PostEffectRow)MemberwiseClone();
}

/// <summary>One <c>#SPCONTROLER</c> row: <c>pos \t NAME \t type \t durationTicks \t p1 \t p2 \t p3 \t p4</c> (length in pulses here).</summary>
public sealed class SpControllerRow
{
    public long Y { get; set; }
    public string Name { get; set; } = "Realize";
    public int Type { get; set; } = 3;
    public long Length { get; set; }
    public double P1 { get; set; }
    public double P2 { get; set; }
    public double P3 { get; set; }
    public double P4 { get; set; }

    public SpControllerRow() { }
    public SpControllerRow(long y, string name, int type, long length, double p1, double p2, double p3, double p4)
    {
        Y = y;
        Name = name;
        Type = type;
        Length = length;
        P1 = p1;
        P2 = p2;
        P3 = p3;
        P4 = p4;
    }

    public SpControllerRow Clone() => (SpControllerRow)MemberwiseClone();
}

/// <summary>
/// One game command row of <c>#SPCONTROLER</c>: <c>pos Name type dur p1 p2 p3 p4</c>. The four values are VOX tokens
/// written verbatim (<c>1024</c>, <c>d-45</c>, <c>LANECOL</c>, <c>xFFFFFFFF</c>, <c>i0</c> …): the prefix decides how
/// the game stores the value (<see cref="VoxToken"/>). <see cref="VoxCommands"/> knows which names the game reads.
/// </summary>
public sealed class VoxCommandRow
{
    public long Y { get; set; }
    public string Name { get; set; } = "LaneY";

    /// <summary>Low 4 bits: 2 = tween from P1 to P2, anything else = from the current value (type field, 0..2^32-1).</summary>
    public long Type { get; set; } = 2;

    /// <summary>Tween length in pulses (VOX ticks × 5); 0 = instant.</summary>
    public long Length { get; set; }

    public string P1 { get; set; } = "0";
    public string P2 { get; set; } = "0";
    public string P3 { get; set; } = "0";
    public string P4 { get; set; } = "0";

    public VoxCommandRow() { }

    public VoxCommandRow(long y, string name, long type, long length, string p1 = "0", string p2 = "0", string p3 = "0", string p4 = "0")
    {
        Y = y;
        Name = name;
        Type = type;
        Length = length;
        P1 = p1;
        P2 = p2;
        P3 = p3;
        P4 = p4;
    }

    /// <summary>For SpecialN: the kind (P1), else null.</summary>
    public string? Kind => Name == "SpecialN" ? P1 : null;

    /// <summary>The catalogue key: <c>SpecialN:LANECOL</c> for SpecialN, else the name.</summary>
    public string Key => Kind is { } k ? "SpecialN:" + k : Name;

    public string this[int i]
    {
        get => i switch { 0 => P1, 1 => P2, 2 => P3, _ => P4 };
        set
        {
            switch (i)
            {
                case 0: P1 = value; break;
                case 1: P2 = value; break;
                case 2: P3 = value; break;
                default: P4 = value; break;
            }
        }
    }

    public VoxCommandRow Clone() => (VoxCommandRow)MemberwiseClone();
}

/// <summary>A note script: <c>@SCRIPTSTART id</c>, one statement per line, <c>@SCRIPTEND</c>.</summary>
public sealed class VoxScript
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<string> Lines { get; set; } = new();

    public VoxScript() { }

    public VoxScript(int id, IEnumerable<string> lines, string name = "")
    {
        Id = id;
        Lines = lines.ToList();
        Name = name;
    }

    public VoxScript Clone() => new(Id, Lines, Name);
}

/// <summary>
/// The note starting at <see cref="Y"/> on VOX track <see cref="Track"/> (1 laser L, 2 FX-L, 3..6 BT-A..D, 7 FX-R,
/// 8 laser R) runs scripts <see cref="Ids"/>.
/// </summary>
public sealed class ScriptedNote
{
    public long Y { get; set; }
    public int Track { get; set; }
    public List<int> Ids { get; set; } = new();

    public ScriptedNote() { }

    public ScriptedNote(long y, int track, IEnumerable<int> ids)
    {
        Y = y;
        Track = track;
        Ids = ids.ToList();
    }

    public ScriptedNote Clone() => new(Y, Track, Ids);
}
