using System.Globalization;

namespace SDVXEditor.Core.Charts;

/// <summary>How far the editor's playtest / camera preview can show an effect.</summary>
public enum PreviewSupport
{
    /// <summary>The preview computes what the game's shader computes.</summary>
    Exact,

    /// <summary>The preview shows something close (blur, noise, shake, lane slide …).</summary>
    Approximate,

    /// <summary>Only visible in the game (needs a game asset, or the visual is unknown).</summary>
    GameOnly,
}

/// <summary>One parameter of a post effect: VOX name, sensible range (official values in <see cref="Help"/>), default.</summary>
public sealed record PostEffectParam(string Name, double Min, double Max, double Default, string Help, bool Integer = false);

/// <summary>One of the eight <c>#POSTEFFECT</c> effects (game id = <see cref="Id"/>).</summary>
public sealed record PostEffectInfo(string Name, int Id, IReadOnlyList<PostEffectParam> Params, PreviewSupport Preview, string Description,
    IReadOnlyList<string>? Labels = null)
{
    public PostEffectParam? Param(string name) => Params.FirstOrDefault(p => p.Name == name);

    /// <summary>SetFrameLabel / RandomShake: the "Param" field is a label, not a parameter name.</summary>
    public bool ParamIsLabel => Labels != null;
}

/// <summary>
/// The <c>#POSTEFFECT</c> catalogue, from the reverse-engineered ∇ DLL (converter CLAUDE.md §6 "What the ∇ DLL
/// actually reads") and the official charts' values. docs/VOX_EFFECTS.md has the row grammar.
/// </summary>
public static class PostEffects
{
    public const string ChromaticAberration = "ChromaticAberration";
    public const string ColorConversion = "ColorConversion";
    public const string RadicalBlur = "RadicalBlur";
    public const string SimpleNoise = "SimpleNoise";
    public const string CrtMonitorEffect = "CrtMonitorEffect";
    public const string LuminousEffect = "LuminousEffect";
    public const string SetFrameLabel = "SetFrameLabel";
    public const string RandomShake = "RandomShake";

    /// <summary>The official misspelling (109 rows); it works in game only because unknown names fall back to id 0.</summary>
    public const string OfficialTypo = "ChromaticAbberation";

    public static readonly IReadOnlyList<PostEffectInfo> All = new[]
    {
        new PostEffectInfo(ChromaticAberration, 0, new[]
        {
            new PostEffectParam("Strength", 0, 0.1, 0, "RGB split; official 0..0.025 (R channel scaled by 1-2s, G by 1-s)"),
        }, PreviewSupport.Approximate, "Colour fringes towards the edges."),
        new PostEffectInfo(ColorConversion, 1, new[]
        {
            new PostEffectParam("Mode", 0, 4, 0, "0 grayscale, 1 sepia, 2 negative, 3 negative + complement (brightness inverted, hue kept), 4 hue rotation", Integer: true),
            new PostEffectParam("Strength", 0, 360, 0, "percent 0..100 (mode 4: degrees 0..360); 0 or less skips the pass"),
        }, PreviewSupport.Exact, "Grayscale, sepia, negative, inverted brightness or hue rotation."),
        new PostEffectInfo(RadicalBlur, 2, new[]
        {
            new PostEffectParam("Strength", 0, 1, 0, "zoom blur; official 0..0.3"),
            new PostEffectParam("CenterPositionX", 0, 1, 0.5, "blur centre, 0..1 of the screen"),
            new PostEffectParam("CenterPositionY", 0, 1, 0.5, "blur centre, 0..1 of the screen (official 0.5 / 0.25 / 0.327)"),
        }, PreviewSupport.Approximate, "Radial (zoom) blur around a centre."),
        new PostEffectInfo(SimpleNoise, 3, new[]
        {
            new PostEffectParam("WholeNoiseStrength", 0, 32768, 0, "official 0..32768"),
            new PostEffectParam("RandomNoisePercent", 0, 100, 50, "official 0..50 (template 50)"),
            new PostEffectParam("StartMaskRegion", 0, 1, 0, "official always 0"),
            new PostEffectParam("EndMaskRegion", 0, 1, 0, "official always 0"),
        }, PreviewSupport.Approximate, "Static noise over the image."),
        new PostEffectInfo(CrtMonitorEffect, 4, new[]
        {
            new PostEffectParam("Active", 0, 1, 0, "0 off, 1 on", Integer: true),
            new PostEffectParam("Speed", 0, 20, 1.5, "scan speed; official 1.5 / 10"),
            new PostEffectParam("DarkEdge", 0, 1, 0, "vignette; official 0..0.5"),
            new PostEffectParam("Distortion", -1, 1, 0, "barrel distortion; official -1..0.15"),
            new PostEffectParam("ScanLineStrength", 0, 1, 0, "official 0.25"),
        }, PreviewSupport.Approximate, "Old CRT monitor: scan lines, dark edges, distortion."),
        new PostEffectInfo(LuminousEffect, 5, new[]
        {
            new PostEffectParam("Strength", 0, 1, 0, "official 0 / 1"),
            new PostEffectParam("RenderStrengthAdd", 0, 1, 0.25, "official 0.25"),
            new PostEffectParam("RenderStrengthMax", 0, 1, 0.175, "official 0.175"),
        }, PreviewSupport.Approximate, "Glow."),
        new PostEffectInfo(SetFrameLabel, 6, Array.Empty<PostEffectParam>(), PreviewSupport.GameOnly,
            "Plays a frame label of the background's mask/layer clip (background-specific game asset).",
            new[] { "in", "in1", "in2", "in3", "in4", "in5", "in6", "in6b" }),
        new PostEffectInfo(RandomShake, 7, new[]
        {
            new PostEffectParam("none", 0, 40, 0, "shake amplitude; official 15 / 20 tweened to 0 over 96 ticks / 500 ms"),
        }, PreviewSupport.Approximate, "Shakes the image (from → to).", new[] { "none" }),
    };

    public static IReadOnlyList<string> Names { get; } = All.Select(e => e.Name).ToArray();

    public static readonly IReadOnlyList<string> ColorModeNames = new[] { "grayscale", "sepia", "negative", "negative + complement", "hue rotation" };

    /// <summary>The correct effect name (the official typo becomes ChromaticAberration); unknown names stay as they are.</summary>
    public static string Normalise(string? name) => name == OfficialTypo ? ChromaticAberration : name ?? "";

    public static PostEffectInfo? Find(string name) => All.FirstOrDefault(e => e.Name == Normalise(name));

    /// <summary>The value an effect parameter has before any row sets it.</summary>
    public static double DefaultOf(string effect, string param) => Find(effect)?.Param(param)?.Default ?? 0;

    /// <summary>Background stages: 0 = the background's effect stage, 1 = "Overall" (the lane).</summary>
    public static string StageName(int stage) => stage switch { 0 => "background", 1 => "lane (overall)", _ => $"stage {stage}" };
}

/// <summary>A ready-made group of post-effect rows ("Negative flash", "Grayscale section" …).</summary>
public sealed record PostEffectPreset(string Name, string Description, Func<long, long, int, IReadOnlyList<PostEffectRow>> Build)
{
    /// <summary>The preset's rows at <paramref name="y"/> for a span of <paramref name="span"/> pulses on <paramref name="stage"/>.</summary>
    public IReadOnlyList<PostEffectRow> At(long y, long span, int stage) => Build(y, span, stage);

    public static readonly IReadOnlyList<PostEffectPreset> All = new[]
    {
        new PostEffectPreset("Negative flash", "Mode 2 (negative) at 100 %, fading back over the span",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.ColorConversion, "Mode", 2, 2, 0, st),
                new PostEffectRow(y, PostEffects.ColorConversion, "Strength", 100, 0, span, st),
            }),
        new PostEffectPreset("Negative section", "negative on at the start, off at the end of the span",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.ColorConversion, "Mode", 2, 2, 0, st),
                new PostEffectRow(y, PostEffects.ColorConversion, "Strength", 100, 100, 0, st),
                new PostEffectRow(y + span, PostEffects.ColorConversion, "Strength", 0, 0, 0, st),
            }),
        new PostEffectPreset("Grayscale section", "fade to grayscale, hold, fade back",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.ColorConversion, "Mode", 0, 0, 0, st),
                new PostEffectRow(y, PostEffects.ColorConversion, "Strength", 0, 100, Math.Max(1, span / 8), st),
                new PostEffectRow(y + span - Math.Max(1, span / 8), PostEffects.ColorConversion, "Strength", 100, 0, Math.Max(1, span / 8), st),
            }),
        new PostEffectPreset("Sepia section", "fade to sepia, hold, fade back",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.ColorConversion, "Mode", 1, 1, 0, st),
                new PostEffectRow(y, PostEffects.ColorConversion, "Strength", 0, 100, Math.Max(1, span / 8), st),
                new PostEffectRow(y + span - Math.Max(1, span / 8), PostEffects.ColorConversion, "Strength", 100, 0, Math.Max(1, span / 8), st),
            }),
        new PostEffectPreset("Hue sweep", "hue rotation 0 → 360° over the span",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.ColorConversion, "Mode", 4, 4, 0, st),
                new PostEffectRow(y, PostEffects.ColorConversion, "Strength", 0, 360, span, st),
                new PostEffectRow(y + span, PostEffects.ColorConversion, "Strength", 0, 0, 0, st),
            }),
        new PostEffectPreset("Blur hit", "zoom blur 0.3 → 0 (official 1766 style)",
            (y, span, st) => new[] { new PostEffectRow(y, PostEffects.RadicalBlur, "Strength", 0.3, 0, span, st) }),
        new PostEffectPreset("RGB split hit", "chromatic aberration 0.02 → 0",
            (y, span, st) => new[] { new PostEffectRow(y, PostEffects.ChromaticAberration, "Strength", 0.02, 0, span, st) }),
        new PostEffectPreset("Shake", "random shake 15 → 0 (official 1767 style)",
            (y, span, st) => new[] { new PostEffectRow(y, PostEffects.RandomShake, "none", 15, 0, span, st) }),
        new PostEffectPreset("Noise burst", "noise 32768 → 0",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.SimpleNoise, "RandomNoisePercent", 50, 50, 0, st),
                new PostEffectRow(y, PostEffects.SimpleNoise, "WholeNoiseStrength", 32768, 0, span, st),
            }),
        new PostEffectPreset("CRT section", "CRT monitor on for the span",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.CrtMonitorEffect, "ScanLineStrength", 0.25, 0.25, 0, st),
                new PostEffectRow(y, PostEffects.CrtMonitorEffect, "DarkEdge", 0, 0.4, Math.Max(1, span / 8), st),
                new PostEffectRow(y, PostEffects.CrtMonitorEffect, "Active", 1, 1, 0, st),
                new PostEffectRow(y + span, PostEffects.CrtMonitorEffect, "Active", 0, 0, 0, st),
            }),
        new PostEffectPreset("Glow fade", "luminous glow 1 → 0",
            (y, span, st) => new[]
            {
                new PostEffectRow(y, PostEffects.LuminousEffect, "RenderStrengthAdd", 0.25, 0.25, 0, st),
                new PostEffectRow(y, PostEffects.LuminousEffect, "RenderStrengthMax", 0.175, 0.175, 0, st),
                new PostEffectRow(y, PostEffects.LuminousEffect, "Strength", 1, 0, span, st),
            }),
    };
}

/// <summary>How a command value slot is written (VOX prefix) and edited.</summary>
public enum VoxValueKind
{
    /// <summary>Plain float (<c>0.50</c>).</summary>
    Float,

    /// <summary><c>i</c> decimal int, stored as raw bits (<c>i5</c>).</summary>
    Int,

    /// <summary><c>x</c> hex int (<c>xAARRGGBB</c>).</summary>
    Hex,

    /// <summary><c>d</c> degrees (<c>d-45</c>).</summary>
    Degrees,

    /// <summary>A name from the game's table (<c>ON</c>, <c>LIVE2D_ON</c>, <c>se_08_big008</c>).</summary>
    Name,

    /// <summary>Unused slot, written <c>0</c>.</summary>
    Unused,
}

/// <summary>One value slot of a command.</summary>
public sealed record VoxParamInfo(string Label, VoxValueKind Kind, string Default, IReadOnlyList<string>? Choices = null, string Help = "");

/// <summary>
/// A SPCONTROLER command the game reads. <see cref="Params"/> are the four value fields p1..p4 (for SpecialN p1 is the
/// kind, fixed). <see cref="Tween"/>: the row has a length (and for LaneY / BIL_RotZ / Morphing2 tweens p1 → p2).
/// </summary>
public sealed record VoxCommandInfo(string Key, string Name, string? Kind, string Label, bool Tween, IReadOnlyList<VoxParamInfo> Params,
    long DefaultType, PreviewSupport Preview, string Description, bool Experimental = false, bool NeedsGameAsset = false, string Unit = "")
{
    public bool IsSpecial => Kind != null;

    /// <summary>A new row of this command at <paramref name="y"/> with the default values.</summary>
    public VoxCommandRow Create(long y, long length = 0)
    {
        var r = new VoxCommandRow(y, Name, DefaultType, Tween ? length : 0);
        int first = 0;
        if (Kind != null)
        {
            r.P1 = Kind;
            first = 1;
        }

        for (int i = 0; i < Params.Count && first + i < 4; i++) r[first + i] = Params[i].Default;
        return r;
    }

    /// <summary>The value slot index (0..3) of parameter <paramref name="param"/>.</summary>
    public int SlotOf(int param) => (Kind != null ? 1 : 0) + param;
}

/// <summary>
/// The SPCONTROLER commands the ∇ DLL reads (CAM_Radi / CAM_RotX / Tilt / ManualSpeed come from the camera and timing
/// tabs). Everything the game parses but never reads is deliberately missing — see <see cref="Dead"/>.
/// </summary>
public static class VoxCommands
{
    private static readonly string[] i0 = { "i0" };

    /// <summary>Sound names from the game's name table (SE / SE2 must name one of them).</summary>
    public static readonly string[] Sounds = { "se_08_big008", "s50_tutorial_flame_in", "virtical_se", "2dnDisk", "EXTEND_SAMPLER" };

    /// <summary>EXBG labels seen in official charts (background-specific; a label the background lacks does nothing).</summary>
    public static readonly string[] ExbgLabels =
    {
        "IN", "IN1", "IN2", "IN3", "IN4", "IN5", "IN6", "IN7", "IN8", "IN9", "OUT", "OUT1", "OUT2", "OUT3", "OUT4", "ON", "OFF",
        "EX2_IN", "EX2_IN1", "EX2_IN2", "EX2_IN3", "EX2_IN4", "EX2_IN5", "EX2_IN6", "EX2_NON", "EX2_OFF", "EX2_ON", "EX2_OUT",
        "EX2_OUT1", "EX2_OUT2", "EX2_OUT3", "EX2_OUT4", "EX3_IN", "EX3_IN1", "EX3_IN2", "EX3_IN3", "EX3_OFF", "EX3_ON",
        "BASE_IN1", "BASE_IN2", "BASE_IN3", "BASE_IN4", "BASE_IN5", "BASE_IN6", "BASE_IN7", "BASE_IN8", "BASE_IN9", "BASE_OUT",
        "8THKAC_BLUE_IN", "8THKAC_BLUE_OUT", "8THKAC_BLUE_RED", "8THKAC_RED_BLUE", "8THKAC_RED_IN", "8THKAC_RED_OUT",
        "BPL2_MONITOR_GRACE_IN", "BPL2_MONITOR_IN", "EXCEEDMASK_IN", "EXCEEDMASK_IN_WITHOUT_RASIS", "HARUKA_IN", "HARUKA_IN1",
        "HARUKA_IN2", "REDBULL5G_FLY", "SOUL_APPEAR", "SOUL_ATTACK_OUT", "SOUL_BLUESCREEN", "SOUL_CHARA_OPEN", "SOUL_FINALATTACK",
        "SOUL_INTRO", "SOUL_PHASE_A", "SOUL_PHASE_B", "SOUL_REBOOT", "SOUL_SPEED_HALF", "SOUL_SPEED_NORMAL", "SOUL_WAVECHANGE",
    };

    /// <summary>Live2D labels seen in official charts.</summary>
    public static readonly string[] Live2DLabels =
    {
        "LIVE2D_ON", "LIVE2D_OFF", "LIVE2D_OUT",
        "LIVE2D_AKASHA_IN", "LIVE2D_AKASHA_INVERTION", "LIVE2D_AKASHA_LAST", "LIVE2D_BPL2_1ST", "LIVE2D_BPL2_1STEND",
        "LIVE2D_BPL2_2ND", "LIVE2D_BPL2_2NDEND", "LIVE2D_BPL2_EX", "LIVE2D_BPL2_FULL", "LIVE2D_BPL2_IN", "LIVE2D_BPL2_OUT",
        "LIVE2D_BPL3_EFFECT_IN1", "LIVE2D_BPL3_EFFECT_IN2", "LIVE2D_BPL3_EFFECT_LAST1", "LIVE2D_BPL3_EFFECT_LAST2",
        "LIVE2D_BPL3_LEFT_CHANGE1", "LIVE2D_BPL3_LEFT_CHANGE2", "LIVE2D_BPL3_LEFT_CHANGE3", "LIVE2D_BPL3_LEFT_CHANGE4",
        "LIVE2D_BPL3_LEFT_CHANGE5", "LIVE2D_BPL3_LEFT_CHANGE6", "LIVE2D_BPL3_LEFT_IN1", "LIVE2D_BPL3_LEFT_LAST",
        "LIVE2D_BPL3_RASIS_CHANGE1", "LIVE2D_BPL3_RASIS_CHANGE4", "LIVE2D_BPL3_RASIS_CHANGE5", "LIVE2D_BPL3_RASIS_CHANGE6",
        "LIVE2D_BPL3_RASIS_IN1", "LIVE2D_BPL3_RASIS_LAST", "LIVE2D_BPL3_RIGHT_CHANGE1", "LIVE2D_BPL3_RIGHT_CHANGE2",
        "LIVE2D_BPL3_RIGHT_CHANGE3", "LIVE2D_BPL3_RIGHT_CHANGE5", "LIVE2D_BPL3_RIGHT_CHANGE6", "LIVE2D_BPL3_RIGHT_IN1",
        "LIVE2D_BPL3_RIGHT_LAST", "LIVE2D_BPL5_EFFECT_IN", "LIVE2D_BPL5_EFFECT_IN2", "LIVE2D_BPL5_EFFECT_IN3",
        "LIVE2D_GRACE_FINAL", "LIVE2D_GRACE_FLY", "LIVE2D_GRACE_GUN1", "LIVE2D_GRACE_GUN2", "LIVE2D_GRACE_IN",
        "LIVE2D_KUREHA_IN", "LIVE2D_KUREHA_KEN", "LIVE2D_KUREHA_LAST", "LIVE2D_MION_CHANGE1", "LIVE2D_MION_CHANGE2",
        "LIVE2D_MION_CHANGE3", "LIVE2D_MION_CHANGE4", "LIVE2D_MION_CHANGE5", "LIVE2D_MION_IN", "LIVE2D_MION_IN2",
        "LIVE2D_MION_OUT", "LIVE2D_MION_OUT2", "LIVE2D_RASIS_I_IN", "LIVE2D_RASIS_I_OUT", "LIVE2D_REDBULL5G_DRINK",
        "LIVE2D_REDBULL5G_FLY", "LIVE2D_REDBULL5G_IN", "LIVE2D_REDBULL5G_OUT",
    };


    public static readonly IReadOnlyList<VoxCommandInfo> All = new[]
    {
        new VoxCommandInfo("LaneY", "LaneY", null, "lane slide / hide", true, new[]
        {
            new VoxParamInfo("from", VoxValueKind.Float, "0", Help: "1024 = moved fully out; sign = direction"),
            new VoxParamInfo("to", VoxValueKind.Float, "1024"),
        }, 2, PreviewSupport.Approximate, "Slides the lane out (±1024) and back (0); ksh lane_toggle. Direction unverified in game.", Unit: "/1024"),
        new VoxCommandInfo("BIL_RotZ", "BIL_RotZ", null, "billboard rotation", true, new[]
        {
            new VoxParamInfo("from", VoxValueKind.Degrees, "d0", Help: "degrees"),
            new VoxParamInfo("to", VoxValueKind.Degrees, "d-45"),
        }, 2, PreviewSupport.GameOnly, "Rotates the billboard in degrees (tutorial: 0° → -45°). What it rotates is unverified.", Unit: "°"),
        new VoxCommandInfo("Morphing2", "Morphing2", null, "lane morph (experimental)", true, new[]
        {
            new VoxParamInfo("from", VoxValueKind.Float, "0", Help: "× 0.125 in game; official 0 … 0.8, -2.82"),
            new VoxParamInfo("to", VoxValueKind.Float, "0.8"),
        }, 2, PreviewSupport.GameOnly, "Lane morph of unknown shape (only 5 official charts).", Experimental: true),
        new VoxCommandInfo("BAROFF", "BAROFF", null, "bar lines off/on", false, new[]
        {
            new VoxParamInfo("state", VoxValueKind.Name, "ON", new[] { "ON", "OFF" }, "ON starts a measure range without bar lines, OFF ends it"),
        }, 0, PreviewSupport.Exact, "Hides bar lines from this measure (ON) until OFF. Works per measure."),
        new VoxCommandInfo("BAR", "BAR", null, "bar line", false, Array.Empty<VoxParamInfo>(), 0, PreviewSupport.Exact,
            "Draws a bar line at this position (e.g. inside a BAROFF range)."),
        special("LANECOL", "lane colour", new[]
        {
            new VoxParamInfo("colour", VoxValueKind.Hex, "xFFFFFFFF", new[] { "xFFFFFFFF", "xDFFFFFFF", "xAFFFFFFF", "x8FFFFFFF", "x4FFFFFFF", "x00000000" }, "xAARRGGBB"),
            new VoxParamInfo("p3", VoxValueKind.Int, "i0", i0), new VoxParamInfo("p4", VoxValueKind.Int, "i0", i0),
        }, PreviewSupport.Approximate, "Lane tint, instant (official fades use 6-tick steps)."),
        special("LANECLEARCOL", "lane fill colour", new[]
        {
            new VoxParamInfo("colour", VoxValueKind.Hex, "x80202020", new[] { "x00000000", "x80202020", "x60202020", "x30202020", "xE0000000" }, "xAARRGGBB"),
            new VoxParamInfo("p3", VoxValueKind.Int, "i0", i0), new VoxParamInfo("p4", VoxValueKind.Int, "i0", i0),
        }, PreviewSupport.Approximate, "Lane background fill, instant."),
        special("FRAME", "frame variant", new[]
        {
            new VoxParamInfo("frame", VoxValueKind.Int, "i6", new[] { "i6", "i7", "i8", "i9", "i10", "i11" }, "1..11 (8↔10, 9↔11 swapped in game)"),
            new VoxParamInfo("p3", VoxValueKind.Unused, "0"), new VoxParamInfo("p4", VoxValueKind.Unused, "0"),
        }, PreviewSupport.GameOnly, "HUD frame variant (the ∇ replacement for HudY).", NeedsGameAsset: true),
        special("SE", "sound effect", new[]
        {
            new VoxParamInfo("sound", VoxValueKind.Name, "se_08_big008", Sounds),
            new VoxParamInfo("p3", VoxValueKind.Unused, "0"), new VoxParamInfo("p4", VoxValueKind.Unused, "0"),
        }, PreviewSupport.GameOnly, "Plays a system sound.", NeedsGameAsset: true),
        special("SE2", "sound effect (area)", new[]
        {
            new VoxParamInfo("sound", VoxValueKind.Name, "se_08_big008", Sounds),
            new VoxParamInfo("area", VoxValueKind.Name, "PLAY_IIDX_AREA_BIG", new[] { "PLAY_IIDX_AREA_BIG" }),
            new VoxParamInfo("volume", VoxValueKind.Float, "f0.4", new[] { "f0.4" }),
        }, PreviewSupport.GameOnly, "Plays a system sound in an area with a volume.", NeedsGameAsset: true),
        special("LIVE2D", "Live2D", new[]
        {
            new VoxParamInfo("label", VoxValueKind.Name, "LIVE2D_ON", Live2DLabels),
            new VoxParamInfo("p3", VoxValueKind.Int, "i0", new[] { "i0", "i1", "i2", "i3", "i4" }),
            new VoxParamInfo("p4", VoxValueKind.Unused, "0"),
        }, PreviewSupport.GameOnly, "Live2D character animation label.", NeedsGameAsset: true),
        special("NEMSYS", "NEMSYS", new[]
        {
            new VoxParamInfo("id", VoxValueKind.Int, "i0", new[] { "i0", "i5", "i9", "i10", "i11", "i22" }),
            new VoxParamInfo("flag", VoxValueKind.Int, "i0", new[] { "i0", "i1" }),
            new VoxParamInfo("p4", VoxValueKind.Unused, "0"),
        }, PreviewSupport.GameOnly, "NEMSYS display id.", NeedsGameAsset: true),
        special("SIDEBG", "side background", new[]
        {
            new VoxParamInfo("a", VoxValueKind.Int, "i1", new[] { "i0", "i1", "i2" }),
            new VoxParamInfo("b", VoxValueKind.Int, "i1", new[] { "i1", "i2" }),
            new VoxParamInfo("p4", VoxValueKind.Unused, "0"),
        }, PreviewSupport.GameOnly, "Side-panel background variant over the row's length.", NeedsGameAsset: true, tween: true),
        special("EXMC_LABEL", "EXMC label", new[]
        {
            new VoxParamInfo("group/id", VoxValueKind.Hex, "x00020000", new[] { "x00000000", "x00000001", "x00010000", "x00020000", "x00020001", "x00030000" }, "x<group:16><id:16>"),
            new VoxParamInfo("label", VoxValueKind.Int, "i0", new[] { "i0", "i1", "i2", "i3", "i4", "i5", "i6", "i7", "i8", "i9" }),
            new VoxParamInfo("p4", VoxValueKind.Unused, "0"),
        }, PreviewSupport.GameOnly, "Background movie-clip label.", NeedsGameAsset: true),
        special("EXMC_PARAM", "EXMC parameter", new[]
        {
            new VoxParamInfo("group/id", VoxValueKind.Hex, "x00020000", new[] { "x00020000", "x00020001", "x00020002", "x00020003", "x00020004", "x00020005" }, "x<group:16><id:16>"),
            new VoxParamInfo("from", VoxValueKind.Float, "0.25"),
            new VoxParamInfo("to", VoxValueKind.Float, "0.00"),
        }, PreviewSupport.GameOnly, "Tweens a background movie-clip parameter over the row's length.", NeedsGameAsset: true, tween: true),
        special("RENDER_PRESET", "render preset", new[]
        {
            new VoxParamInfo("preset", VoxValueKind.Int, "i1", new[] { "i0", "i1" }),
            new VoxParamInfo("p3", VoxValueKind.Int, "i0", i0), new VoxParamInfo("p4", VoxValueKind.Int, "i0", i0),
        }, PreviewSupport.GameOnly, "Render preset (2085 uses i1 during its inversion). Effect unknown."),
        special("DISABLE_PRE_EFFECT_LINE", "disable pre-effect line", new[]
        {
            new VoxParamInfo("mode", VoxValueKind.Int, "i1073741824", new[] { "i1065353216", "i1073741824", "i1077936128" }, "1.0f = for the row's length, 2.0f = start, 3.0f = end (official form: float bits)"),
            new VoxParamInfo("p3", VoxValueKind.Int, "i0", i0), new VoxParamInfo("p4", VoxValueKind.Int, "i0", i0),
        }, PreviewSupport.GameOnly, "Disables the 'pre effect line' (meaning unknown).", tween: true),
        special("EXBG", "EXBG label", new[]
        {
            new VoxParamInfo("label", VoxValueKind.Name, "IN", ExbgLabels),
            new VoxParamInfo("loop", VoxValueKind.Name, "RECURSIVE", new[] { "RECURSIVE", "NONRECURSIVE", "0", "i1" }, "RECURSIVE (0) = loop"),
            new VoxParamInfo("p4", VoxValueKind.Int, "i0", i0),
        }, PreviewSupport.GameOnly, "Plays a label of the background's extra layer (background-specific).", NeedsGameAsset: true),
        special("EXBGTEX", "EXBG texture", new[]
        {
            new VoxParamInfo("texture", VoxValueKind.Int, "i1", Enumerable.Range(1, 11).Select(i => "i" + i).ToArray()),
            new VoxParamInfo("p3", VoxValueKind.Int, "0", new[] { "0", "i1" }),
            new VoxParamInfo("p4", VoxValueKind.Unused, "0"),
        }, PreviewSupport.GameOnly, "Switches the extra layer's texture.", NeedsGameAsset: true),
    };

    /// <summary>Commands the game parses and never reads (offering them would author invisible effects).</summary>
    public static readonly IReadOnlyList<string> Dead = new[]
    {
        "LaneX", "GEO_RotZ", "GEO_RotX", "Tilt_Mag", "3D_Scroll", "Morphing0", "Morphing1", "Morphing3", "HudY",
        "AIRL_ScaX", "AIRR_ScaX", "AIRL_PosX", "AIRR_PosX", "CAM_PosY", "CAM_PosZ", "CAM_rotX", "SpecialF", "BgSpeed", "BGROT",
        "SpecialN:LANEMODEL0", "SpecialN:LANEMODEL1", "SpecialN:LANEMODEL2", "SpecialN:LANEMODEL3", "SpecialN:LANEDISP",
        "SpecialN:SIDE", "SpecialN:ADJRVZ", "SpecialN:EXBGROTF", "SpecialN:BGDISP", "SpecialN:BIL_H",
    };

    public static VoxCommandInfo? Find(string key) => All.FirstOrDefault(c => c.Key == key);

    public static VoxCommandInfo? Of(VoxCommandRow row) => Find(row.Key);

    /// <summary>The tweened commands drawn as graph lanes.</summary>
    public static readonly IReadOnlyList<string> TweenKeys = new[] { "LaneY", "BIL_RotZ", "Morphing2" };

    private static VoxCommandInfo special(string kind, string label, VoxParamInfo[] ps, PreviewSupport preview, string description,
        bool NeedsGameAsset = false, bool tween = false)
        => new("SpecialN:" + kind, "SpecialN", kind, label, tween, ps, 0, preview, description, NeedsGameAsset: NeedsGameAsset);

}

/// <summary>VOX value tokens: <c>f</c> float, <c>i</c> int bits, <c>x</c> hex, <c>d</c> degrees, table names.</summary>
public static class VoxToken
{
    private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

    /// <summary>One VOX field: no whitespace (the game splits rows on it), at most 64 characters.</summary>
    public static bool IsValid(string? s) =>
        !string.IsNullOrEmpty(s) && s.Length <= 64 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '+' or '-');

    /// <summary>The numeric value of a token as the game reads it into a float slot (null for names).</summary>
    public static double? Number(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        char c = s[0];
        string rest = s.Length > 1 ? s[1..] : "";
        if (c == 'f' && double.TryParse(rest, NumberStyles.Float, inv, out double f)) return f;
        if (c == 'd' && double.TryParse(rest, NumberStyles.Float, inv, out double d)) return d;
        if (c == 'i' && long.TryParse(rest, NumberStyles.Integer, inv, out long i)) return i;
        if (c == 'x' && uint.TryParse(rest, NumberStyles.HexNumber, inv, out uint x)) return x;
        return double.TryParse(s, NumberStyles.Float, inv, out double v) ? v : null;
    }

    /// <summary><c>xAARRGGBB</c> → ARGB, or null.</summary>
    public static uint? Argb(string? s) =>
        s is { Length: 9 } && s[0] == 'x' && uint.TryParse(s[1..], NumberStyles.HexNumber, inv, out uint v) ? v
        : s is { Length: > 1 } && s[0] == 'i' && long.TryParse(s[1..], NumberStyles.Integer, inv, out long i) ? unchecked((uint)i)
        : null;

    /// <summary>A number written for <paramref name="kind"/> (official precision: 2 decimals).</summary>
    public static string Format(double v, VoxValueKind kind)
    {
        string num = Math.Round(v, 4).ToString("0.00##", inv);
        if (num == "-0.00") num = "0.00";
        return kind switch
        {
            VoxValueKind.Degrees => "d" + num,
            VoxValueKind.Int => "i" + ((long)Math.Round(v)).ToString(inv),
            VoxValueKind.Hex => "x" + ((uint)Math.Clamp(Math.Round(v), 0, uint.MaxValue)).ToString("X8", inv),
            _ => num,
        };
    }

    public static string Hex(uint argb) => "x" + argb.ToString("X8", inv);
}

/// <summary>music_db backgrounds (<c>bg_no</c>) of the official songs: id, number of songs, the first song's id, post-effect stages.</summary>
public sealed record GameBackground(int Id, int Songs, int FirstSongId, bool HasPostEffects)
{
    public string Label => $"bg {Id} - first used by song {FirstSongId}{(Songs > 1 ? $" (+{Songs - 1})" : "")}{(HasPostEffects ? " - post effects" : "")}";
}

/// <summary>
/// Known <c>bg_no</c> values, gathered from the official music_db of game version 2026071400 (ids and counts only, no
/// game asset). <see cref="GameBackground.HasPostEffects"/>: an official chart on that background uses #POSTEFFECT —
/// post effects are defined per background, so on others (bg 0 in particular) they may show nothing.
/// </summary>
public static class GameBackgrounds
{
    private static readonly int[] post_effect_bgs = { 5, 92, 93, 99, 102, 105, 109, 111, 115, 116, 118 };

    private static readonly (int Id, int Songs, int First)[] table =
    {
        (0, 55, 7), (1, 58, 2), (2, 270, 1), (3, 75, 3), (4, 105, 5), (5, 150, 122), (6, 47, 171), (7, 177, 202), (8, 20, 201),
        (9, 27, 158), (10, 157, 217), (11, 9, 447), (12, 72, 183), (13, 9, 490), (14, 12, 303), (15, 140, 180), (16, 29, 306),
        (18, 4, 824), (19, 1, 525), (27, 4, 492), (29, 11, 523), (30, 125, 462), (31, 22, 470), (34, 1, 661), (36, 1, 636),
        (38, 6, 686), (39, 5, 610), (40, 2, 551), (41, 12, 527), (42, 1, 708), (43, 1, 791), (44, 1, 709), (45, 1, 816),
        (46, 1, 826), (47, 1, 840), (48, 1, 915), (49, 1, 803), (50, 1, 939), (51, 58, 886), (53, 1, 1028), (54, 1, 1036),
        (57, 4, 985), (58, 85, 894), (59, 3, 1065), (60, 107, 997), (61, 35, 1001), (63, 98, 937), (65, 16, 1101), (66, 1, 1100),
        (67, 1, 1099), (68, 1, 1148), (69, 1, 1185), (70, 1, 1176), (71, 27, 1177), (72, 1, 1219), (73, 1, 1269), (74, 1, 1270),
        (75, 38, 1297), (76, 4, 1259), (77, 1, 1362), (78, 1, 1361), (79, 18, 1363), (80, 1, 1462), (81, 1, 1495), (82, 37, 1581),
        (83, 1, 1580), (84, 1, 1584), (86, 12, 1596), (87, 1, 1660), (88, 12, 1530), (89, 1, 1751), (90, 1, 1811), (91, 24, 1771),
        (92, 1, 1766), (93, 1, 1767), (94, 8, 1742), (95, 1, 1889), (96, 10, 1856), (97, 1, 1888), (98, 1, 1902), (99, 1, 1938),
        (100, 9, 1919), (101, 4, 2014), (102, 1, 2037), (103, 1, 2038), (104, 6, 2079), (105, 1, 2085), (106, 1, 2152),
        (107, 9, 2121), (108, 1, 2161), (109, 1, 2087), (110, 5, 2200), (111, 1, 2198), (112, 7, 2225), (113, 1, 2199),
        (114, 6, 2254), (115, 1, 2253), (116, 1, 2330), (117, 9, 2331), (118, 1, 2393),
    };

    public static readonly IReadOnlyList<GameBackground> All =
        table.Select(t => new GameBackground(t.Id, t.Songs, t.First, post_effect_bgs.Contains(t.Id))).ToArray();

    public static GameBackground? Find(int id) => All.FirstOrDefault(b => b.Id == id);

    /// <summary>True when an official chart on background <paramref name="bgNo"/> uses post effects.</summary>
    public static bool DefinesPostEffects(int? bgNo) => bgNo is { } b && post_effect_bgs.Contains(b);
}
