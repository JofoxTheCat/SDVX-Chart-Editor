using SDVXEditor.Core.Formats.Vox;

namespace SDVXEditor.Core.Editing;

public enum Support
{
    Yes,
    Partial,
    No,
}

/// <summary>
/// Which export keeps a camera / background / effect setting (one row of the export loss matrix).
/// <see cref="Note"/> is about kson / ksh, <see cref="VoxNote"/> says what the VOX path keeps or loses (the badge tooltip).
/// </summary>
public sealed record FeatureSupport(string Id, string Label, Support Kson, Support Ksh, Support Vox, string Note, string VoxNote);

/// <summary>
/// The camera / background / effect rows of the export loss matrix
/// (reference/notes/chart-format-model.md §9.1), checked against what <c>KshWriter</c> writes and
/// what the VOX path (ksh2vox reading our kson) does. kson keeps everything: what it has no field for
/// is stored as editor data in <c>impl</c>.
/// <para>
/// The VOX column depends on the converter version: ksh2vox below <see cref="VoxMappingVersion"/> drops every one
/// of these settings (<see cref="Legacy"/>); from 0.17.0 on most of them are mapped, some with clamping or dropped
/// cases (<see cref="Mapped"/>, converter CHANGELOG [0.17.0]); from 0.19.0 on spins, swings and the Tilt block
/// marker are written too (<see cref="MappedSpins"/>, CHANGELOG [0.19.0]). An unknown version gets the conservative table.
/// </para>
/// </summary>
public static class ExportSupport
{
    /// <summary>First ksh2vox version that maps camera, tilt, FX effects, laser filters and the editor VOX extras.</summary>
    public const string VoxMappingVersion = "0.17.0";

    private const string vox_camera = "ksh2vox < 0.17.0 writes the template camera (Realize/AIRL/AIRR) and TILT MODE 0";
    private const string vox_effects = "ksh2vox < 0.17.0 writes effect index 0 for every FX hold and the template filter tables";
    private const string impl_only = "editor data in kson impl; ksh has no field";
    private const string ksh_vox = "kson impl; ksh //#vox_ comment lines (KSM skips them)";
    private const string vox_new = "ksh2vox < 0.20.0 does not write it";

    /// <summary>The table for ksh2vox below 0.17.0, or an unknown converter version (conservative).</summary>
    public static readonly IReadOnlyList<FeatureSupport> Legacy = new[]
    {
        new FeatureSupport("zoom_top", "zoom_top", Support.Yes, Support.Yes, Support.No, "", vox_camera),
        new FeatureSupport("zoom_bottom", "zoom_bottom", Support.Yes, Support.Yes, Support.No, "", vox_camera),
        new FeatureSupport("zoom_side", "zoom_side", Support.Yes, Support.Yes, Support.No, "", "no VOX counterpart exists"),
        new FeatureSupport("center_split", "center_split", Support.Yes, Support.Yes, Support.No, "", "no VOX counterpart exists"),
        new FeatureSupport("rotation_deg", "rotation_deg", Support.Yes, Support.Yes, Support.No, "", "no VOX counterpart exists"),
        new FeatureSupport("tilt_auto", "tilt (auto)", Support.Yes, Support.Yes, Support.No, "", vox_camera),
        new FeatureSupport("tilt_manual", "tilt (manual)", Support.Yes, Support.Yes, Support.No, "", vox_camera),
        new FeatureSupport("spin", "spin / half spin", Support.Yes, Support.Partial, Support.No, "ksh: one pattern per chart line; the game derives spins from slams itself", "no VOX counterpart"),
        new FeatureSupport("swing", "swing", Support.Yes, Support.Partial, Support.No, "ksh: one pattern per chart line", "no VOX counterpart"),
        new FeatureSupport("bg_image", "background image", Support.Yes, Support.Yes, Support.No, "", "the background is music_db bg_no"),
        new FeatureSupport("bg_layer", "animation layer", Support.Yes, Support.Yes, Support.No, "", "no VOX counterpart"),
        new FeatureSupport("bg_movie", "movie", Support.Yes, Support.Yes, Support.No, "", "no VOX counterpart"),
        new FeatureSupport("bg_no", "VOX bg_no", Support.Yes, Support.Yes, Support.No, "kson impl; ksh header bg_no=", "ksh2vox < 0.17.0 writes bg_no 0"),
        new FeatureSupport("post_effect", "post effects", Support.Yes, Support.Yes, Support.No, ksh_vox, "ksh2vox < 0.17.0 writes the template's neutral #POSTEFFECT rows"),
        new FeatureSupport("sp_controller", "SPCONTROLER rows", Support.Yes, Support.No, Support.No, impl_only, "ksh2vox < 0.17.0 writes the template's Realize/AIRL/AIRR rows"),
        new FeatureSupport("scroll_speed", "scroll speed", Support.Yes, Support.Yes, Support.No, "ksh scroll_speed=", "ksh2vox < 0.17.0 writes no ManualSpeed rows"),
        new FeatureSupport("stop", "stops", Support.Yes, Support.Yes, Support.No, "ksh stop=", "ksh2vox < 0.17.0 drops stops"),
        new FeatureSupport("fx_def", "FX effect definitions", Support.Yes, Support.Yes, Support.No, "ksh #define_fx", vox_effects),
        new FeatureSupport("fx_assign", "FX hold effect", Support.Yes, Support.Partial, Support.No, "ksh fx-l=Name;n keeps only short-form parameters", vox_effects),
        new FeatureSupport("fx_param_change", "parameter changes", Support.Yes, Support.Yes, Support.No, "ksh fx:<name>:<param>= lines", "no VOX counterpart"),
        new FeatureSupport("laser_filter", "laser filter", Support.Yes, Support.Yes, Support.No, "ksh filtertype=/#define_filter", "ksh2vox < 0.17.0 writes peak for every laser"),
        new FeatureSupport("filter_gain", "filter gain", Support.Yes, Support.Yes, Support.No, "ksh pfiltergain", "no VOX counterpart"),
        new FeatureSupport("filter_delay", "peaking filter delay", Support.Yes, Support.Yes, Support.No, "ksh pfilterdelay", "no VOX counterpart"),
        new FeatureSupport("slam_volume", "slam volume", Support.Yes, Support.Yes, Support.No, "ksh chokkakuvol", "no VOX counterpart"),
        new FeatureSupport("chip_sound", "FX chip sound", Support.Yes, Support.Yes, Support.No, "ksh fx-l_se=", "VOX has only built-in chip variants"),
        // game effects (docs/VOX_EFFECTS.md); VOX only through ksh2vox 0.20.0+
        new FeatureSupport("lane_y", "lane slide / hide (LaneY)", Support.Yes, Support.Yes, Support.No, "ksh lane_toggle= for plain hide/show toggles, else //#vox_sp lines", vox_new),
        new FeatureSupport("lane_colour", "lane colours (LANECOL / LANECLEARCOL)", Support.Yes, Support.Yes, Support.No, ksh_vox, vox_new),
        new FeatureSupport("bil_rotz", "billboard rotation (BIL_RotZ)", Support.Yes, Support.Yes, Support.No, ksh_vox, vox_new),
        new FeatureSupport("morphing2", "lane morph (Morphing2)", Support.Yes, Support.Yes, Support.No, ksh_vox, vox_new),
        new FeatureSupport("bar_lines", "bar lines (BAR / BAROFF)", Support.Yes, Support.Yes, Support.No, ksh_vox, vox_new),
        new FeatureSupport("bpm_option", "#BPM OPTION", Support.Yes, Support.Yes, Support.No, ksh_vox, vox_new),
        new FeatureSupport("special_n", "game assets (SpecialN)", Support.Yes, Support.Yes, Support.No, ksh_vox, vox_new),
        new FeatureSupport("note_scripts", "note scripts", Support.Yes, Support.Partial, Support.No, "ksh //#vox_script lines; script names are lost", vox_new),
    };

    /// <summary>
    /// What ksh2vox 0.17.0+ maps (converter CHANGELOG [0.17.0], README conversion table). "~" = mapped, but the
    /// mapping clamps values or drops cases; <see cref="FeatureSupport.VoxNote"/> says which.
    /// </summary>
    private static readonly Dictionary<string, (Support Vox, string Note)> mapped_vox = new()
    {
        ["zoom_top"] = (Support.Partial, "CAM_RotX tweens (zoom_top / 100); values beyond ±100 clamped"),
        ["zoom_bottom"] = (Support.Partial, "CAM_Radi tweens (zoom_bottom / -100); values beyond ±100 clamped"),
        ["zoom_side"] = (Support.No, "no VOX counterpart; ksh2vox warns"),
        ["center_split"] = (Support.No, "no VOX counterpart; ksh2vox warns"),
        ["rotation_deg"] = (Support.No, "no VOX counterpart; ksh2vox warns"),
        ["tilt_auto"] = (Support.Partial, "TILT MODE normal/bigger/keep_bigger = 0/1/2; zero falls back to normal, biggest to bigger (keep_biggest to keep_bigger)"),
        ["tilt_manual"] = (Support.Partial, "Tilt tweens in SPCONTROLER; values beyond ±1 clamped"),
        ["spin"] = (Support.No, "no VOX counterpart (the game spins on slams itself); ksh2vox warns"),
        ["swing"] = (Support.No, "no VOX counterpart; ksh2vox warns"),
        ["bg_image"] = (Support.No, "VOX keeps only music_db bg_no"),
        ["bg_layer"] = (Support.No, "VOX keeps only music_db bg_no"),
        ["bg_movie"] = (Support.No, "VOX keeps only music_db bg_no"),
        ["bg_no"] = (Support.Yes, "written to music_db <bg_no>"),
        ["post_effect"] = (Support.Yes, "replace the template's #POSTEFFECT rows"),
        ["sp_controller"] = (Support.Yes, "replace the template's base SPCONTROLER rows"),
        ["scroll_speed"] = (Support.Partial, "ManualSpeed rows in #LOCKED_SPCONTROLER; negative speeds become 0, above 5730 capped"),
        ["stop"] = (Support.Yes, "4- BPM rows (official stop form)"),
        ["fx_def"] = (Support.Partial, "12-block effect table; Retrigger, Echo, Gate, Phaser, TapeStop, SideChain, Wobble, BitCrusher, PitchShift, Flanger only, other types play without an effect; only the variant parameter is kept"),
        ["fx_assign"] = (Support.Partial, "block index 2..13 per hold; unmappable types get no effect; beyond 12 effects variants share the nearest block"),
        ["fx_param_change"] = (Support.No, "parameter changes over time are not representable; ksh2vox warns"),
        ["laser_filter"] = (Support.Partial, "peak / low-pass / high-pass / bitcrush only (others fall back to peak); one filter per section, changes inside a section are lost"),
        ["filter_gain"] = (Support.No, "no VOX counterpart; ksh2vox warns"),
        ["filter_delay"] = (Support.No, "no VOX counterpart"),
        ["slam_volume"] = (Support.No, "no VOX counterpart"),
        ["chip_sound"] = (Support.No, "custom chip sounds are not representable; ksh2vox warns"),
    };

    /// <summary>The table for ksh2vox 0.17.0 and later.</summary>
    public static readonly IReadOnlyList<FeatureSupport> Mapped =
        Legacy.Select(f => mapped_vox.TryGetValue(f.Id, out var m) ? f with { Vox = m.Vox, VoxNote = m.Note } : f).ToArray();

    /// <summary>First ksh2vox version that writes lane spins (laser field 4) and the Tilt block marker.</summary>
    public const string VoxSpinVersion = "0.19.0";

    /// <summary>
    /// What ksh2vox 0.19.0 changes on top of <see cref="Mapped"/> (converter CHANGELOG [0.19.0]): spins become the VOX
    /// spin type on the slam's first point, Tilt rows carry the official block marker. The spin types' meaning comes
    /// from the public converters, so both spin rows stay "~".
    /// </summary>
    private static readonly Dictionary<string, (Support Vox, string Note)> spin_vox = new()
    {
        ["tilt_manual"] = (Support.Partial, "Tilt rows in blocks like the official ones (field 7: 2 first, 0 middle, 3 last, 1 single row); a held value ends in a hold row; values beyond ±1 clamped"),
        ["spin"] = (Support.Partial, "VOX spin type on the slam at the spin's position: spin → 2 / 3 / 1 by length (under 68, under 136, longer, in 1/192 of a measure), half spin → 5; fixed lengths; the direction is the slam's; a spin without a slam is dropped; ksh2vox warns"),
        ["swing"] = (Support.Partial, "written as VOX spin type 5 like a half spin (no proven VOX swing type); scale / repeat / decay are lost; needs a slam at its position; ksh2vox warns"),
    };

    /// <summary>The table for ksh2vox 0.19.0 and later.</summary>
    public static readonly IReadOnlyList<FeatureSupport> MappedSpins =
        Mapped.Select(f => spin_vox.TryGetValue(f.Id, out var m) ? f with { Vox = m.Vox, VoxNote = m.Note } : f).ToArray();

    /// <summary>First ksh2vox version that writes the game effects (post effect stages, commands, BPM option, scripts).</summary>
    public const string VoxEffectsVersion = "0.20.0";

    /// <summary>
    /// What ksh2vox 0.20.0 changes on top of <see cref="MappedSpins"/> (converter CHANGELOG [0.20.0]): every game effect
    /// the ∇ DLL reads is written. Effects whose look in game is not verified yet stay "~".
    /// </summary>
    private static readonly Dictionary<string, (Support Vox, string Note)> effects_vox = new()
    {
        ["post_effect"] = (Support.Partial, "#POSTEFFECT rows with stage / instance / ms lengths; a background without its own effect stages (bg_no 0, most common backgrounds) may show nothing - official post-effect songs use bg_no 92-118"),
        ["lane_y"] = (Support.Yes, "LaneY tweens (value / 1024); direction to be play-tested"),
        ["lane_colour"] = (Support.Yes, "SpecialN LANECOL / LANECLEARCOL, instant"),
        ["bil_rotz"] = (Support.Partial, "BIL_RotZ tweens in degrees; what it rotates is not verified"),
        ["morphing2"] = (Support.Partial, "Morphing2 tweens (x0.125); shape unknown, experimental"),
        ["bar_lines"] = (Support.Yes, "BAROFF ON/OFF (per measure) and BAR rows"),
        ["bpm_option"] = (Support.Yes, "#BPM OPTION RepresentativeBpm / ConstantScroll"),
        ["special_n"] = (Support.Partial, "SpecialN rows written verbatim; they address game assets of the chosen background"),
        ["note_scripts"] = (Support.Yes, "#SCRIPT_DEFINE + #SCRIPTED_TRACKn; invalid statements and notes without a note at the position are dropped"),
    };

    /// <summary>The table for ksh2vox 0.20.0 and later.</summary>
    public static readonly IReadOnlyList<FeatureSupport> MappedEffects =
        MappedSpins.Select(f => effects_vox.TryGetValue(f.Id, out var m) ? f with { Vox = m.Vox, VoxNote = m.Note } : f).ToArray();

    /// <summary>True when <paramref name="converterVersion"/> is known and at least <see cref="VoxEffectsVersion"/>.</summary>
    public static bool MapsEffectsToVox(string? converterVersion) => AtLeast(converterVersion, VoxEffectsVersion);

    /// <summary>The conservative table (same as <see cref="Legacy"/>).</summary>
    public static IReadOnlyList<FeatureSupport> All => Legacy;

    /// <summary>True when <paramref name="converterVersion"/> is known and at least <see cref="VoxMappingVersion"/>.</summary>
    public static bool MapsToVox(string? converterVersion) => AtLeast(converterVersion, VoxMappingVersion);

    /// <summary>True when <paramref name="converterVersion"/> is known and at least <see cref="VoxSpinVersion"/>.</summary>
    public static bool MapsSpinsToVox(string? converterVersion) => AtLeast(converterVersion, VoxSpinVersion);

    private static bool AtLeast(string? version, string minimum) =>
        version != null && VoxExporter.ParseVersion(version) >= VoxExporter.ParseVersion(minimum);

    /// <summary>The rows for a converter version; null (unknown, not detected yet) = the conservative table.</summary>
    public static IReadOnlyList<FeatureSupport> For(string? converterVersion) =>
        MapsEffectsToVox(converterVersion) ? MappedEffects
        : MapsSpinsToVox(converterVersion) ? MappedSpins
        : MapsToVox(converterVersion) ? Mapped : Legacy;

    public static FeatureSupport Get(string id, string? converterVersion = null) =>
        For(converterVersion).FirstOrDefault(f => f.Id == id) ?? throw new KeyNotFoundException(id);

    public static string Mark(Support s) => s switch
    {
        Support.Yes => "✓",
        Support.Partial => "~",
        _ => "✗",
    };

    /// <summary>Short badge text, e.g. "kson ✓  ksh ~  vox ✗".</summary>
    public static string Badge(string id, string? converterVersion = null)
    {
        var f = Get(id, converterVersion);
        return $"kson {Mark(f.Kson)}  ksh {Mark(f.Ksh)}  vox {Mark(f.Vox)}";
    }
}
