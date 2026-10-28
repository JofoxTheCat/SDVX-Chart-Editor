using osu.Framework.Configuration;
using osu.Framework.Platform;

namespace SDVXEditor.Game.Configuration;

public enum EditorSetting
{
    /// <summary>0-1: master on the audio manager, music on the song track, effects on the hitsound samples.</summary>
    VolumeMaster,
    VolumeMusic,

    /// <summary>Effects and key sounds (FX samples, slam sounds, metronome).</summary>
    VolumeEffects,

    /// <summary>Tick on every note crossing the judgement line while playing.</summary>
    Hitsounds,

    /// <summary>
    /// Universal audio offset in ms (osu!-style, per machine): how much later the sound reaches the
    /// listener than the picture. Positive values delay what is drawn and judged (notes, judgement,
    /// timeline, playtest) against the audio. Set it with the offset wizard; 0 = trust the audio clock.
    /// </summary>
    AudioOffset,

    /// <summary>Hitsound skin: empty = Auto (the active skin of the user's own osu! install), "built-in" = the editor's own sounds, a skin folder name under osu!\Skins, or an absolute folder.</summary>
    HitsoundSkinFolder,

    /// <summary>Sample per chart event, see <see cref="SDVXEditor.Core.Audio.HitsoundMap"/>.</summary>
    HitsoundMapping,

    /// <summary>Notes on one pulse are layered (one sample each, louder chords like osu!mania) up to this many.</summary>
    HitsoundChordLayers,

    /// <summary>Folder holding ksh2vox.py for the VOX export; empty = the external/SDVX-Chart-Converter submodule.</summary>
    VoxConverterDir,

    /// <summary>Python for the VOX export; empty = bundled python/runtime, then PATH.</summary>
    VoxPythonPath,

    // ---- playtest (F5), defaults in EditorConfigManager.Playtest.cs

    /// <summary>Playtest key bindings, see <see cref="SDVXEditor.Core.Gameplay.PlayBindings"/>.</summary>
    PlaytestBindings,

    /// <summary>Playtest scroll speed as BPM × hi-speed ("mod speed"), adjusted with F3/F4 or -/+.</summary>
    PlaytestModSpeed,

    PlaytestAutoplay,

    /// <summary>Knob sensitivity multiplier (joystick axes, mouse mode); negative inverts.</summary>
    PlaytestKnobSensitivity,

    /// <summary>Joystick axes of the knobs, "left,right" (0-based axis indices; most SDVX controllers: 0,1).</summary>
    PlaytestKnobAxes,

    /// <summary>Knobs as mouse X (left) / Y (right) — the "mouse mode" of SDVX controllers.</summary>
    PlaytestMouseKnobs,

    /// <summary>Laser cursor speed of the laser keys, in laser widths per second.</summary>
    PlaytestKeyLaserSpeed,

    /// <summary>Judgement windows in real-time ms: "critical,near,error,slam".</summary>
    PlaytestWindows,

    /// <summary>Sounds on hit (chips, slams, FX key sounds).</summary>
    PlaytestHitsounds,

    /// <summary>Assist tick: a sound at every chip/slam regardless of input (and in autoplay).</summary>
    PlaytestAssistTick,

    /// <summary>Playtest gauge, Core <see cref="SDVXEditor.Core.Gameplay.GaugeType"/>: normal, easy (halved damage) or hard (EXCESSIVE, fails at 0).</summary>
    PlaytestGauge,

    // ---- options overlay (Ctrl+O), defaults in EditorConfigManager.Options.cs

    /// <summary>
    /// User overrides of the editor key bindings, see <see cref="SDVXEditor.Game.Screens.Edit.Input.EditorKeyBindings"/>:
    /// only actions that differ from the defaults, <c>tab-timing=F8, open-difficulty=none</c>. Empty = all defaults.
    /// </summary>
    EditorBindings,

    /// <summary>Frame time / FPS display in the corner (the framework's frame statistics, Ctrl+F11 cycles it too).</summary>
    ShowFps,

    /// <summary>An additional song folder scanned on start, like <c>--songs</c> (which wins when given). Empty = none.</summary>
    SongsFolder,

    /// <summary>The launcher config (<c>sdvx_rpc_config.json</c>) used on start when <c>--launcher-config</c> is not given. Empty = the usual lookup.</summary>
    LauncherConfigPath,

    /// <summary>Export folder for .kson/.ksh/.zip/.svz/VOX exports. Empty = <c>Exports</c> beside the editor songs.</summary>
    ExportFolder,

    /// <summary>UI language; only "en" exists so far (placeholder for the options overlay).</summary>
    Language,

    /// <summary>Compose: hovering a laser shows its anchors and their types without selecting it (osu!stable slider anchors).</summary>
    ShowLaserAnchorsOnHover,

    /// <summary>Timeline waveform: <see cref="Configuration.WaveformStyle.Spectral"/> (colour per column from the bands, default) or the framework's classic green one.</summary>
    WaveformStyle,

    /// <summary>Opacity of the timeline waveform, 0.2–1.</summary>
    WaveformOpacity,

    /// <summary>
    /// Compose: the key that turns the chip tools into their hold variant while held (<see cref="Core.Input.HoldModifierKey"/>,
    /// default Ctrl): BT Chip + it = BT Hold, + Shift = FX Hold, FX Chip + it = FX Hold.
    /// </summary>
    HoldModifier,

    /// <summary>
    /// Compose: the key that makes drags of existing laser points / anchors snap to the beat divisor and the value grid
    /// (<see cref="Core.Input.DragSnapKey"/>, default Shift); without it they follow the pointer freely.
    /// </summary>
    DragSnapModifier,

    /// <summary>
    /// Whether the cursor is kept inside the window. Mirrors the framework's ConfineMouseMode, whose default (Fullscreen)
    /// also traps it in Borderless; the editor defaults to Never so the cursor can move on to other monitors.
    /// </summary>
    ConfineMouse,

    /// <summary>Classic tutorial chapters completed, "0,1,3" (Help → Tutorial… shows the ticks); see EditorConfigManager.Tutorial.cs.</summary>
    TutorialProgress,

    /// <summary>The one-time "New to charting?" card on first start was shown.</summary>
    TutorialHintShown,

    /// <summary>Chapters of the main tutorial (Fax's song) completed, "0,1,3"; <see cref="TutorialProgress"/> keeps the classic one's.</summary>
    TutorialMainProgress,

    /// <summary>
    /// Play the chart's audio effects (FX hold effects, laser filters, slam volume) on the song during editor playback
    /// and the playtest (effects tab checkbox). Off = no DSP on the song at all.
    /// </summary>
    PlayEffects,
}

/// <summary>How the timeline draws the song waveform.</summary>
public enum WaveformStyle
{
    /// <summary>Bars mirrored around the centre, height = short-term loudness, colour = band balance (low red, mid green, high blue).</summary>
    Spectral,

    /// <summary>The framework's <c>WaveformGraph</c> in green.</summary>
    Classic,
}

/// <summary>Editor settings, persisted as <c>editor.ini</c> in the game storage.</summary>
public partial class EditorConfigManager : IniConfigManager<EditorSetting>
{
    protected override string Filename => "editor.ini";

    public EditorConfigManager(Storage storage)
        : base(storage)
    {
    }

    protected override void InitialiseDefaults()
    {
        SetDefault(EditorSetting.VolumeMaster, 0.8, 0, 1, 0.01);
        SetDefault(EditorSetting.VolumeMusic, 0.8, 0, 1, 0.01);
        SetDefault(EditorSetting.VolumeEffects, 0.8, 0, 1, 0.01);
        SetDefault(EditorSetting.Hitsounds, true);
        SetDefault(EditorSetting.AudioOffset, 0.0, -300, 300, 1);
        SetDefault(EditorSetting.HitsoundSkinFolder, string.Empty);
        SetDefault(EditorSetting.HitsoundMapping, Core.Audio.HitsoundMap.DefaultText);
        SetDefault(EditorSetting.HitsoundChordLayers, 4, 1, 8);
        SetDefault(EditorSetting.VoxConverterDir, string.Empty);
        SetDefault(EditorSetting.VoxPythonPath, string.Empty);
        SetDefault(EditorSetting.PlayEffects, false);
        initialisePlaytestDefaults();
        initialiseOptionsDefaults();
        initialiseTutorialDefaults();
    }
}
