using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Performance;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using SDVXEditor.Core.Audio;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit.Dialogs;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Songs;

namespace SDVXEditor.Game.Overlays.Options;

/// <summary>The sections of the options and the settings behind them.</summary>
public partial class OptionsOverlay
{
    [Resolved]
    private EditorConfigManager config { get; set; } = null!;

    [Resolved]
    private FrameworkConfigManager frameworkConfig { get; set; } = null!;

    [Resolved(canBeNull: true)]
    private DialogOverlay? dialogs { get; set; }

    [Resolved(canBeNull: true)]
    private LibraryManager? library { get; set; }

    [Resolved(canBeNull: true)]
    private VoxConverterDetector? converter { get; set; }

    /// <summary>The playtest bindings the Input section edits; written back to editor.ini on every change.</summary>
    public PlayBindings PlayBindings { get; } = new PlayBindings();

    private Bindable<string> playBindingSetting = null!;
    private bool syncingPlayBindings;

    /// <summary>Converter version line of the Export section (smoke test).</summary>
    public OptionsNote ConverterStatus { get; private set; } = null!;

    /// <summary>The converter detection the Export section shows (null until the options were first opened).</summary>
    public Task? ConverterDetection { get; private set; }

    private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Bindables the controls bind to. Bindings are weak in both directions, so every bound copy and derived value
    /// is held here for the overlay's lifetime (otherwise a GC silently cuts the control off its setting).
    /// </summary>
    private readonly List<object> keepAlive = new List<object>();

    private T keep<T>(T bindable) where T : class
    {
        keepAlive.Add(bindable);
        return bindable;
    }

    private Bindable<T> bind<T>(EditorSetting setting) => keep(config.GetBindable<T>(setting));

    private Bindable<T> fbind<T>(FrameworkSetting setting) => keep(frameworkConfig.GetBindable<T>(setting));

    private IEnumerable<OptionsSection> createSections()
    {
        yield return general();
        yield return graphics();
        yield return audio();
        yield return input();
        yield return playtest();
        yield return export();
    }

    private void onOpened()
    {
        // nothing is expensive enough to defer, except the converter (a Python process): detect it once
        if (ConverterDetection == null) ensureConverterDetected();
    }

    // ---- General ---------------------------------------------------------------

    private OptionsSection general()
    {
        string launcher = library?.Launcher is { } l
            ? l.Found ? $"In use: {l.Config!.Path}" + (l.Config.NauticaChartsPath is string n ? $" (Nautica songs: {n})" : string.Empty)
            : $"No launcher config in use: {library.LauncherProblem}"
            : string.Empty;

        return new OptionsSection("General", FontAwesome.Solid.Cog,
            new OptionsSubsection("Song library").Add(
                new OptionsTextBox("Songs folder", bind<string>(EditorSetting.SongsFolder), "an additional song folder", "library", "path", "directory"),
                new OptionsNote("Scanned in addition to the editor's own songs and the launcher's Nautica folder. --songs on the command line wins. Applies on the next start."),
                new OptionsTextBox("Launcher config", bind<string>(EditorSetting.LauncherConfigPath), "sdvx_rpc_config.json (empty = search)", "launcher", "sdvx_rpc_config", "nautica"),
                new OptionsNote("The launcher's sdvx_rpc_config.json, read for the Nautica song folder. Empty = the usual places. Applies on the next start."
                                + (launcher.Length > 0 ? "\n" + launcher : string.Empty))),
            new OptionsSubsection("Language").Add(
                new OptionsDropdown<string>("Language", bind<string>(EditorSetting.Language), new[] { "en" }, v => v == "en" ? "English" : v, "locale", "translation"),
                new OptionsNote("Only English for now; more languages are planned.")));
    }

    // ---- Graphics --------------------------------------------------------------

    private OptionsSection graphics() => new OptionsSection("Graphics", FontAwesome.Solid.Desktop,
        new OptionsSubsection("Layout").Add(
            new OptionsDropdown<WindowMode>("Screen mode", fbind<WindowMode>(FrameworkSetting.WindowMode),
                new[] { WindowMode.Windowed, WindowMode.Borderless, WindowMode.Fullscreen },
                m => m switch
                {
                    WindowMode.Borderless => "Borderless (fullscreen window)",
                    WindowMode.Fullscreen => "Fullscreen (exclusive)",
                    _ => "Windowed",
                }, "window", "fullscreen", "borderless", "resolution"),
            new OptionsNote("Alt+Enter (or F11) switches between Windowed and Borderless. Borderless covers the screen "
                            + "without a frame and stays Alt+Tab friendly; exclusive fullscreen is only reachable here."),
            new OptionsDropdown<osu.Framework.Input.ConfineMouseMode>("Confine mouse cursor", bind<osu.Framework.Input.ConfineMouseMode>(EditorSetting.ConfineMouse),
                Enum.GetValues<osu.Framework.Input.ConfineMouseMode>(),
                m => m switch
                {
                    osu.Framework.Input.ConfineMouseMode.Never => "Never (default)",
                    osu.Framework.Input.ConfineMouseMode.Fullscreen => "In Borderless / Fullscreen",
                    _ => "Always",
                }, "mouse", "cursor", "confine", "monitor", "lock"),
            new OptionsNote("Never lets the cursor leave the editor for other monitors, also in Borderless.")),
        new OptionsSubsection("Renderer").Add(
            new OptionsDropdown<FrameSync>("Frame limiter", fbind<FrameSync>(FrameworkSetting.FrameSync),
                Enum.GetValues<FrameSync>(),
                f => f switch
                {
                    FrameSync.VSync => "VSync",
                    FrameSync.Limit2x => "2x refresh rate",
                    FrameSync.Limit4x => "4x refresh rate",
                    FrameSync.Limit8x => "8x refresh rate",
                    _ => "Unlimited",
                }, "fps", "vsync", "frame rate"),
            new OptionsCheckbox("Show FPS", bind<bool>(EditorSetting.ShowFps), "frame time", "counter", "performance"),
            new OptionsNote("Ctrl+F11 cycles the frame statistics too.")),
        new OptionsSubsection("Editor").Add(
            new OptionsCheckbox("Show laser anchors on hover", bind<bool>(EditorSetting.ShowLaserAnchorsOnHover), "laser", "anchor", "slider", "hover", "compose"),
            new OptionsNote("Hovering a laser in select or laser mode shows its anchors and their types (osu!stable slider anchors) without selecting it."),
            new OptionsDropdown<WaveformStyle>("Waveform style", bind<WaveformStyle>(EditorSetting.WaveformStyle), Enum.GetValues<WaveformStyle>(),
                w => w == WaveformStyle.Spectral ? "Spectral (default)" : "Classic green", "waveform", "timeline", "spectrum", "colour", "kick", "drums"),
            new OptionsSlider("Waveform opacity", (BindableNumber<double>)bind<double>(EditorSetting.WaveformOpacity), v => string.Format(inv, "{0:0}%", v * 100), "waveform", "timeline", "alpha", "transparency"),
            new OptionsNote("Spectral colours each bar by its sound like DJ software: bass and kicks red, vocals, leads and snares green, hats and cymbals blue; mixtures blend, quiet parts are dimmer.")));

    // ---- Audio -----------------------------------------------------------------

    private OptionsSection audio()
    {
        var skin = bind<string>(EditorSetting.HitsoundSkinFolder);
        var choices = new List<string> { OsuSkinLocator.AutoSetting, OsuSkinLocator.BuiltInSetting };
        choices.AddRange(OsuSkinLocator.ListSkins());
        if (!choices.Contains(skin.Value)) choices.Add(skin.Value);
        string? autoSkin = OsuSkinLocator.FindSkinFolder() is string f ? Path.GetFileName(f) : null;

        static string percent(double v) => string.Format(inv, "{0:0}%", v * 100);

        var mapping = new OptionsSubsection("Hitsound mapping");
        var mappingSetting = bind<string>(EditorSetting.HitsoundMapping);
        foreach (var slot in Enum.GetValues<HitsoundSlot>())
        {
            var value = keep(new Bindable<string>(HitsoundMap.Parse(mappingSetting.Value)[slot]));
            var samples = new List<string> { HitsoundMap.Synth, HitsoundMap.None };
            foreach (string set in new[] { "soft", "normal", "drum" })
                foreach (string s in new[] { "hitnormal", "hitclap", "hitwhistle", "hitfinish", "slidertick" })
                    samples.Add($"{set}-{s}");
            if (!samples.Contains(value.Value)) samples.Add(value.Value);

            var s0 = slot;
            value.BindValueChanged(e =>
            {
                var map = HitsoundMap.Parse(mappingSetting.Value);
                mappingSetting.Value = string.Join(", ", Enum.GetValues<HitsoundSlot>().Select(x => $"{HitsoundMap.KeyOf(x)}={(x == s0 ? e.NewValue : map[x])}"));
            });
            mappingSetting.BindValueChanged(e => value.Value = HitsoundMap.Parse(e.NewValue)[s0]);

            mapping.Add(new OptionsDropdown<string>(slotLabel(slot), value, samples,
                v => v == HitsoundMap.Synth ? "synth (built-in tick)" : v == HitsoundMap.None ? "none (silent)" : v, "hitsound", "sample", HitsoundMap.KeyOf(slot)));
        }

        mapping.Add(new OptionsButton("Reset mapping", () => mappingSetting.SetDefault(), Colour4.FromHex("555555"), "hitsound", "default"));

        return new OptionsSection("Audio", FontAwesome.Solid.VolumeUp,
            new OptionsSubsection("Volume").Add(
                new OptionsSlider("Master", (BindableNumber<double>)bind<double>(EditorSetting.VolumeMaster), percent, "volume", "sound"),
                new OptionsSlider("Music", (BindableNumber<double>)bind<double>(EditorSetting.VolumeMusic), percent, "volume", "song", "track"),
                new OptionsSlider("Effects", (BindableNumber<double>)bind<double>(EditorSetting.VolumeEffects), percent, "volume", "hitsound", "metronome")),
            new OptionsSubsection("Offset").Add(
                new OptionsStepper("Universal offset", (BindableNumber<double>)bind<double>(EditorSetting.AudioOffset), 1,
                    v => string.Format(inv, "{0:+0;-0;0} ms", v), "latency", "audio offset", "delay"),
                new OptionsNote("Positive: the sound reaches you later than the picture (e.g. Bluetooth). Notes, judgement and playtest wait for it."),
                new OptionsButton("Offset & hitsound dialog…", openOffsetDialog, Colour4.FromHex("3f6fb5"), "offset", "wizard", "calibrate")),
            new OptionsSubsection("Hitsounds").Add(
                new OptionsCheckbox("Editor hitsounds", bind<bool>(EditorSetting.Hitsounds), "tick", "sound"),
                new OptionsDropdown<string>("Hitsound skin", skin, choices,
                    v => v == OsuSkinLocator.AutoSetting ? $"Auto (osu! skin{(autoSkin != null ? ": " + autoSkin : ", none found")})"
                        : v == OsuSkinLocator.BuiltInSetting ? "Built-in" : v, "osu", "skin", "samples"),
                new OptionsStepper("Chord layers", toDouble(bind<int>(EditorSetting.HitsoundChordLayers), 1, 8), 1,
                    v => v.ToString("0", inv), "chord", "louder", "layers"),
                new OptionsNote("Samples are read from your own osu! install, never copied.")),
            mapping);
    }

    private static string slotLabel(HitsoundSlot slot) => slot switch
    {
        HitsoundSlot.BtChip => "BT chip",
        HitsoundSlot.BtHold => "BT hold",
        HitsoundSlot.FxChip => "FX chip",
        HitsoundSlot.FxHold => "FX hold",
        HitsoundSlot.Slam => "Slam",
        _ => "Laser start",
    };

    private void openOffsetDialog()
    {
        if (dialogs == null) return;
        Close();
        if (dialogs.Current is not AudioOffsetDialog)
            dialogs.Push(new AudioOffsetDialog(null));
    }

    // ---- Input -----------------------------------------------------------------

    private OptionsSection input()
    {
        var section = new OptionsSection("Input", FontAwesome.Solid.Keyboard);
        var editorScope = new EditorKeybindScope();

        section.AddSubsection(new OptionsSubsection("Compose").Add(
            new OptionsDropdown<HoldModifierKey>("Hold modifier", bind<HoldModifierKey>(EditorSetting.HoldModifier), Enum.GetValues<HoldModifierKey>(),
                k => k == HoldModifierKey.Ctrl ? "Ctrl (default)" : "Alt", "hold", "chip", "modifier", "ctrl", "alt", "compose", "fx"),
            new OptionsNote("Held with the BT or FX Chip tool, a click or drag on the playfield places a hold instead (with Shift as well: FX hold). "
                            + "It only counts on its own: used for a shortcut (Ctrl+Z, Alt+wheel …) it does nothing until it is released.", null, "hold", "modifier"),
            new OptionsDropdown<DragSnapKey>("Snap while dragging laser anchors", bind<DragSnapKey>(EditorSetting.DragSnapModifier), Enum.GetValues<DragSnapKey>(),
                k => k == DragSnapKey.Shift ? "Shift (default)" : k.ToString(), "snap", "drag", "anchor", "laser", "grid", "shift"),
            new OptionsNote("Dragging an existing laser point or anchor is free (any time, any value). Hold this key while dragging to snap it to the beat divisor "
                            + "and the laser value grid. New points keep snapping. The classic-laser key (F by default, Editor · Compose below) places K-Shoot style.", null, "snap", "drag")));

        foreach (string category in EditorKeyBindings.Categories)
        {
            var actions = EditorKeyBindings.ActionsIn(category);
            var sub = new OptionsSubsection("Editor · " + category, resetButton(() => EditorKeyBindings.Table.Reset(actions.Where(a => !EditorKeyBindings.IsFixed(a)))));

            foreach (var action in actions)
            {
                sub.Add(new KeybindRow<EditorAction>(action, EditorKeyBindings.InfoOf(action).Description, EditorKeyBindings.Table, editorScope,
                    a => EditorKeyBindings.InfoOf(a).Description, EditorKeyBindings.IsFixed(action), "editor", "keybind", "shortcut", category));
            }

            if (actions.Any(EditorKeyBindings.IsFixed))
                sub.Add(new OptionsNote("Alt+wheel zooms over the timeline or playfield and changes the volume elsewhere; it depends on what the cursor is over and is not rebindable.", null, "alt", "wheel", "zoom"));

            section.AddSubsection(sub);
        }

        // ---- playtest controls: own scope, stored in PlaytestBindings
        playBindingSetting = bind<string>(EditorSetting.PlaytestBindings);
        PlayBindings.Table.LoadOverrides(playBindingSetting.Value);
        PlayBindings.Table.Changed += () =>
        {
            if (syncingPlayBindings) return;
            playBindingSetting.Value = PlayBindings.ToString();
        };
        playBindingSetting.BindValueChanged(e =>
        {
            if (e.NewValue == PlayBindings.ToString()) return;
            syncingPlayBindings = true;
            PlayBindings.Table.LoadOverrides(e.NewValue);
            syncingPlayBindings = false;
        });

        var playScope = new PlaytestKeybindScope();
        var play = new OptionsSubsection("Playtest controls", resetButton(() => PlayBindings.Table.ResetAll()));
        foreach (var action in BindingTable<PlayAction>.Actions)
            play.Add(new KeybindRow<PlayAction>(action, playLabel(action), PlayBindings.Table, playScope, playLabel, false, "playtest", "keybind", "controller", "joystick"));
        play.Add(new OptionsNote("Keys and controller buttons (JoyN). Tab, F3/F4, -/+ and ` are fixed in the playtest; Esc always leaves it.", null, "playtest"));
        section.AddSubsection(play);

        section.AddSubsection(new OptionsSubsection("All bindings").Add(
            new OptionsButton("Reset all editor bindings", () => EditorKeyBindings.Table.ResetAll(), Colour4.FromHex("555555"), "keybind", "default"),
            new OptionsButton("Reset all playtest controls", () => PlayBindings.Table.ResetAll(), Colour4.FromHex("555555"), "keybind", "default")));

        return section;
    }

    private static Drawable resetButton(Action action) =>
        new Graphics.UserInterface.FlatButton("Reset to default", Colour4.FromHex("4a4a4a"), null, 15) { Size = new osuTK.Vector2(150, 24), Action = action };

    public static string playLabel(PlayAction a) => a switch
    {
        PlayAction.BtA => "BT-A",
        PlayAction.BtB => "BT-B",
        PlayAction.BtC => "BT-C",
        PlayAction.BtD => "BT-D",
        PlayAction.FxL => "FX-L",
        PlayAction.FxR => "FX-R",
        PlayAction.LaserLLeft => "Laser L left",
        PlayAction.LaserLRight => "Laser L right",
        PlayAction.LaserRLeft => "Laser R left",
        PlayAction.LaserRRight => "Laser R right",
        PlayAction.Start => "Start / pause",
        _ => "Back",
    };

    // ---- Playtest --------------------------------------------------------------

    private OptionsSection playtest()
    {
        var windows = bind<string>(EditorSetting.PlaytestWindows);
        var axes = bind<string>(EditorSetting.PlaytestKnobAxes);
        string ms(double v) => v.ToString("0", inv) + " ms";

        return new OptionsSection("Playtest", FontAwesome.Solid.Gamepad,
            new OptionsSubsection("Gameplay").Add(
                new OptionsSlider("Mod speed", (BindableNumber<double>)bind<double>(EditorSetting.PlaytestModSpeed), v => v.ToString("0", inv), "scroll", "hi-speed", "bpm"),
                new OptionsCheckbox("Start with autoplay", bind<bool>(EditorSetting.PlaytestAutoplay), "auto"),
                new OptionsDropdown<SDVXEditor.Core.Gameplay.GaugeType>("Gauge", bind<SDVXEditor.Core.Gameplay.GaugeType>(EditorSetting.PlaytestGauge),
                    Enum.GetValues<SDVXEditor.Core.Gameplay.GaugeType>(), g => g switch
                    {
                        SDVXEditor.Core.Gameplay.GaugeType.Easy => "Easy (halved damage)",
                        SDVXEditor.Core.Gameplay.GaugeType.Hard => "Hard (excessive, fails at 0 %)",
                        _ => "Normal (effective rate)",
                    }, "gauge", "hard", "easy", "excessive", "fail"),
                new OptionsCheckbox("Hitsounds", bind<bool>(EditorSetting.PlaytestHitsounds), "sound", "hit"),
                new OptionsCheckbox("Assist tick", bind<bool>(EditorSetting.PlaytestAssistTick), "metronome", "tick", "sound")),
            new OptionsSubsection("Judgement windows").Add(
                new OptionsStepper("CRITICAL", part(windows, 0, 46, 5, 200), 1, ms, "timing", "window", "judgement"),
                new OptionsStepper("NEAR", part(windows, 1, 92, 10, 300), 1, ms, "timing", "window", "judgement"),
                new OptionsStepper("ERROR", part(windows, 2, 138, 20, 400), 1, ms, "timing", "window", "judgement", "miss"),
                new OptionsStepper("Slam", part(windows, 3, 84, 10, 300), 1, ms, "timing", "window", "judgement", "laser"),
                new OptionsNote("Real-time milliseconds either side of the note (SDVX: 46 / 92 / 138, slams 84)."),
                new OptionsButton("Reset windows", () => windows.SetDefault(), Colour4.FromHex("555555"), "judgement", "default")),
            new OptionsSubsection("Knobs").Add(
                new OptionsSlider("Knob sensitivity", (BindableNumber<double>)bind<double>(EditorSetting.PlaytestKnobSensitivity), v => v.ToString("0.00", inv) + "x", "laser", "knob", "invert"),
                new OptionsStepper("Joystick axis (left knob)", part(axes, 0, 0, 0, 15), 1, v => v.ToString("0", inv), "controller", "axis", "joystick"),
                new OptionsStepper("Joystick axis (right knob)", part(axes, 1, 1, 0, 15), 1, v => v.ToString("0", inv), "controller", "axis", "joystick"),
                new OptionsCheckbox("Knobs as mouse (mouse mode)", bind<bool>(EditorSetting.PlaytestMouseKnobs), "mouse", "knob", "controller"),
                new OptionsSlider("Laser key speed", (BindableNumber<double>)bind<double>(EditorSetting.PlaytestKeyLaserSpeed), v => v.ToString("0.0", inv) + "/s", "laser", "keyboard"),
                new OptionsNote("Negative sensitivity inverts the knobs. Most SDVX controllers report the knobs on axes 0 and 1.")));
    }

    /// <summary>One number of a comma-separated setting ("46,92,138,84"), kept in sync both ways.</summary>
    private BindableDouble part(Bindable<string> source, int index, double fallback, double min, double max)
    {
        double read(string? text)
        {
            var parts = (text ?? string.Empty).Split(',');
            return index < parts.Length && double.TryParse(parts[index].Trim(), NumberStyles.Float, inv, out double v) ? v : fallback;
        }

        var b = keep(new BindableDouble(Math.Clamp(read(source.Value), min, max)) { MinValue = min, MaxValue = max, Precision = 1 });
        b.BindValueChanged(e =>
        {
            var parts = (source.Value ?? string.Empty).Split(',').Select(p => p.Trim()).ToList();
            while (parts.Count <= index) parts.Add(fallback.ToString(inv));
            parts[index] = e.NewValue.ToString("0", inv);
            source.Value = string.Join(",", parts);
        });
        source.BindValueChanged(e =>
        {
            double v = Math.Clamp(read(e.NewValue), min, max);
            if (Math.Abs(v - b.Value) > 1e-9) b.Value = v;
        });
        return b;
    }

    private BindableDouble toDouble(Bindable<int> source, int min, int max)
    {
        var b = keep(new BindableDouble(source.Value) { MinValue = min, MaxValue = max, Precision = 1 });
        b.BindValueChanged(e => source.Value = (int)Math.Round(e.NewValue));
        source.BindValueChanged(e => b.Value = e.NewValue);
        return b;
    }

    // ---- Export ----------------------------------------------------------------

    private OptionsSection export()
    {
        ConverterStatus = new OptionsNote("Converter: not checked yet.", null, "ksh2vox", "version");

        // a converter / Python change re-runs the shared detection (VoxConverterDetector watches both settings)
        var converterDir = bind<string>(EditorSetting.VoxConverterDir);
        var python = bind<string>(EditorSetting.VoxPythonPath);
        if (converter != null)
            keep(converter.Current.GetBoundCopy()).BindValueChanged(e => showConverter(e.NewValue), true);

        return new OptionsSection("Export", FontAwesome.Solid.FileExport,
            new OptionsSubsection("VOX converter").Add(
                new OptionsTextBox("Converter folder", converterDir, "folder with ksh2vox.py (empty = bundled)", "ksh2vox", "vox", "sdvx-chart-converter"),
                new OptionsTextBox("Python", python, "python.exe (empty = bundled, then PATH)", "python", "interpreter", "vox"),
                ConverterStatus,
                new OptionsButton("Detect converter", DetectConverter, Colour4.FromHex("3f6fb5"), "ksh2vox", "version", "check")),
            new OptionsSubsection("Output").Add(
                new OptionsTextBox("Export folder", bind<string>(EditorSetting.ExportFolder),
                    library != null ? $"empty = {SDVXEditor.Core.Editing.UiFormat.MiddleEllipsis(library.DefaultExportRoot, 34)}" : "empty = Exports beside the editor songs", "kson", "ksh", "zip", "svz", "vox", "folder"),
                new OptionsNote("Where .kson, .ksh, .zip, .svz and VOX exports go. Never inside a game's data_mods.")));
    }

    /// <summary>Starts the converter host again and reports its version (ksh2vox below the recommended version is flagged).</summary>
    public void DetectConverter()
    {
        if (converter == null) return;
        ConverterDetection = converter.Detect();
    }

    /// <summary>Uses the session's detection (the export badges may have run it already), else starts it.</summary>
    private void ensureConverterDetected()
    {
        if (converter == null) return;
        ConverterDetection = converter.EnsureDetected();
    }

    private void showConverter(ConverterDetection d)
    {
        string text;
        Colour4 colour;

        switch (d.State)
        {
            case ConverterDetectState.NotStarted:
                text = "Converter: not checked yet.";
                colour = OptionsStyle.Muted;
                break;

            case ConverterDetectState.Checking:
                text = "Converter: checking…";
                colour = OptionsStyle.Muted;
                break;

            case ConverterDetectState.Failed when d.Info != null:
                text = $"Converter not loaded: {d.Error ?? "unknown reason"}";
                colour = OptionsStyle.Error;
                break;

            case ConverterDetectState.Failed:
                text = $"Converter could not be started: {d.Error}";
                colour = OptionsStyle.Error;
                break;

            default:
            {
                var info = d.Info!;
                bool outdated = VoxExporter.IsOutdated(info.ConverterVersion);
                text = $"ksh2vox {info.ConverterVersion ?? "?"} ({info.ConverterDir ?? "?"}), Python {info.Python ?? "?"}";
                if (outdated) text += $"\nOlder than {VoxExporter.RecommendedConverterVersion}: update the converter, 6/8 bars and chart audio offsets come out wrong.";
                foreach (string w in info.Warnings.Where(w => !w.Contains("older than", StringComparison.Ordinal)))
                    text += "\n" + w;
                colour = outdated || info.Warnings.Count > 0 ? OptionsStyle.Warning : OptionsStyle.Ok;
                break;
            }
        }

        ConverterStatus.Text = text;
        ConverterStatus.TextColour = colour;
    }

}
