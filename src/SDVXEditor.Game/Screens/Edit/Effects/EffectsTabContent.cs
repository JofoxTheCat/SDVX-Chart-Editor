using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using static SDVXEditor.Game.Screens.Edit.Components.PanelControls;

namespace SDVXEditor.Game.Screens.Edit.Effects;

/// <summary>
/// The effects tab (replaces osu!'s "hitsound" tab): FX hold audio effects (definitions, parameters,
/// assignment, parameter changes), laser filters (switches, gain, slam volume) and custom FX chip
/// sounds (import into the song folder, assign, preview). The read-only playfield on the left shows
/// the shared selection; the note list selects FX notes around the cursor.
/// </summary>
public partial class EffectsTabContent : CompositeDrawable
{
    public const float COLUMN = 500;

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly ComposeEditor compose;
    private readonly EditorToolState tools;
    private readonly IBindable<float> zoom;
    private readonly HitsoundPlayer hitsounds;

    public Action<string>? Message { get; set; }

    /// <summary>Defs panel mode: false = FX hold effects, true = laser filters.</summary>
    public readonly BindableBool LaserDefs = new BindableBool();

    /// <summary>Play the chart's audio effects on the song (editor.ini <c>PlayEffects</c>; Game <c>Audio/EffectAudioHost</c>).</summary>
    public readonly BindableBool PlayEffects = new BindableBool();

    /// <summary>Tooltip of the "play effects" checkbox (the smoke test finds the checkbox by it).</summary>
    public const string PLAY_EFFECTS_TIP = "FX hold effects and laser filters on the song while it plays (editor and playtest); off costs nothing";

    /// <summary>Plays an effect (name, laser bus) on the song from the cursor for a moment; set by the editor screen.</summary>
    public Func<string, bool, bool>? PreviewEffect { get; set; }

    /// <summary>Previews the effect picked in the assign drop-down (FX) or the filter drop-down (laser).</summary>
    public bool PreviewPicked(bool laser)
    {
        string name = laser ? filterName.Current.Value : assignName.Current.Value;
        if (PreviewEffect?.Invoke(name, laser) == true) return true;
        Message?.Invoke(PlayEffects.Value ? "no song audio to preview on" : "turn on \"play effects\" first");
        return false;
    }

    public string? SelectedDef { get; private set; }

    /// <summary>The read-only playfield on the left (its visible range is what "select FX in view" takes).</summary>
    public ComposePlayfield Playfield { get; private set; } = null!;

    /// <summary>The collapsible export table at the bottom of the laser column.</summary>
    public CollapsibleSection ExportSection { get; private set; } = null!;

    /// <summary>The chip sound the assign buttons use.</summary>
    public string CurrentSound { get; private set; } = "clap";

    public double SoundVolume { get; private set; } = 1;

    private readonly Dictionary<string, PanelField> fields = new Dictionary<string, PanelField>();
    private FillFlowContainer noteList = null!;
    private FillFlowContainer defList = null!;
    private FillFlowContainer paramEditor = null!;
    private FillFlowContainer changeList = null!;
    private FillFlowContainer filterList = null!;
    private FillFlowContainer soundList = null!;
    private SpriteText selectionText = null!;
    private SpriteText laserNow = null!;
    private SpriteText soundText = null!;
    private OsuDropdown<string> newDefType = null!;
    private OsuDropdown<string> assignName = null!;
    private OsuDropdown<string> filterName = null!;
    private FlatButton[] defModeButtons = null!;
    private Container assignHolder = null!;
    private Container filterHolder = null!;
    private int builtVersion = -1;
    private string? builtDef;
    private bool builtLaser;
    private bool selectionDirty = true;
    private long lastCursorPulse = -1;

    public EffectsTabContent(EditorSession session, EditorClock clock, ComposeEditor compose, EditorToolState tools, IBindable<float> zoom, HitsoundPlayer hitsounds)
    {
        this.session = session;
        this.clock = clock;
        this.compose = compose;
        this.tools = tools;
        this.zoom = zoom.GetBoundCopy();
        this.hitsounds = hitsounds;
        RelativeSizeAxes = Axes.Both;
    }

    public PanelField Field(string name) => fields[name];

    private PanelField field(string key, string label, Func<string>? get, Action<string> commit, float width = 240, float labelWidth = 80)
    {
        var f = new PanelField(label, get, commit, width, labelWidth);
        fields[key] = f;
        return f;
    }

    private static Drawable column(float x, float width, params Drawable[] children) => new Container
    {
        X = x,
        Y = 6,
        Width = width,
        RelativeSizeAxes = Axes.Y,
        Height = 0.99f,
        Masking = true,
        CornerRadius = 6,
        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.PanelDark },
            new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ScrollbarVisible = false,
                Padding = new MarginPadding(10),
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 7),
                    Children = children,
                },
            },
        },
    };

    [BackgroundDependencyLoader]
    private void load()
    {
        float x0 = 8 + CameraTabContent.LEFT_WIDTH + 10;
        newDefType = new OsuDropdown<string>(EffectCatalog.Types.Where(t => !t.ForLaser || t.Type == "bitcrusher").Select(t => t.Type)) { Width = 170, FontSize = 16, Height = 26 };
        assignName = new OsuDropdown<string>(EffectCatalog.PresetFx) { Width = 200, FontSize = 16, Height = 26 };
        filterName = new OsuDropdown<string>(EffectCatalog.PresetLaser) { Width = 200, FontSize = 16, Height = 26 };
        defModeButtons = new[]
        {
            Button("FX effects", () => LaserDefs.Value = false, 120, tip: "Show the effect definitions for FX holds"),
            Button("laser filters", () => LaserDefs.Value = true, 120, tip: "Show the laser filter definitions"),
        };

        InternalChildren = new Drawable[]
        {
            new Container
            {
                X = 8,
                Y = 6,
                Width = CameraTabContent.LEFT_WIDTH,
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
                Masking = true,
                Child = new Container
                {
                    Width = PlayfieldLayout.TOTAL_WIDTH,
                    RelativeSizeAxes = Axes.Y,
                    Scale = new Vector2(CameraTabContent.PREVIEW_SCALE, 1),
                    Child = Playfield = new ComposePlayfield(session, clock, compose, tools, zoom) { ReadOnly = true },
                },
            },
            // which FX notes + their chip sounds
            column(x0, COLUMN,
                Header("FX notes near the cursor"),
                Note("Pick the FX notes the buttons below act on. The selection is shared with the compose tab and shows blue in the playfield on the left.", 13),
                Row(Button("select FX in view", () => SelectFxInView(), 150, tip: "Selects every FX chip and hold visible in the playfield on the left (replaces the selection)"),
                    Button("clear selection", () => session.Selection.Clear(), 130, tip: "Deselects everything")),
                selectionText = new SpriteText { Font = EditorFont.Bold(15), Colour = EditorColours.Text },
                noteList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 2) },
                Note("A row click adds / removes that note. Rows: a measure before to six after the cursor.", 12),

                Header("FX chip sounds", "chip_sound"),
                Note("The sound an FX chip plays when it is hit, instead of the normal tick.", 13),
                SubHeader("1 · pick a sound"),
                soundText = new SpriteText { Font = EditorFont.Regular(14), Colour = EditorColours.Text },
                soundList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Full, Spacing = new Vector2(4, 4) },
                Note("clap, clap_impact, clap_punchy, snare, snare_lo are K-Shoot's built-in chip sounds (the editor plays stand-ins for them); "
                     + "files you import follow. A number = chips using it. A click selects and plays it.", 12),
                Row(
                    field("sound_volume", "volume %", () => Num(Math.Round(SoundVolume * 100)), s => { if (TryNum(s, out double v)) SoundVolume = Math.Clamp(v / 100, 0, 1); }, 150, 70)
                        .With(f => f.TooltipText = "Volume the sound gets on the chips you assign it to (0-100 %)"),
                    Button("preview", () => PreviewSound(CurrentSound), 80, tip: "Plays the picked sound at that volume")),
                SubHeader("2 · give it to chips"),
                Row(
                    Button("to selected chips", () => AssignSoundToSelectedChips(), 150, tip: "Only the selected FX chips get the sound (holds are skipped)"),
                    Button("as default (chips without one)", () => AssignSoundAsDefault(), 230, tip: "Every FX chip of the chart that has no sound yet gets it (kson has no chart-wide default)"),
                    Button("clear", () => clearSoundOfSelected(), 60, EditorColours.Osu.MenuRed, "The selected chips go back to the normal tick")),
                SubHeader("your own sound"),
                Row(field("import_path", "import", null, _ => { }, 380, 54).With(f => f.TooltipText = "Full path of a .wav or .ogg file"),
                    Button("import", () => ImportSound(Field("import_path").Box.Text), 80, tip: "Copies the file into the song folder and picks it")),
                Note("Import copies the file into the song folder (that copy is not undoable; assigning it is).", 12),

                Header("editor playback"),
                new PinkCheckbox("play effects during playback (FX holds, laser filters, slam volume)", 14).With(c =>
                {
                    c.Current.BindTo(PlayEffects);
                    c.TooltipText = PLAY_EFFECTS_TIP;
                }),
                Row(Button("preview FX effect", () => PreviewPicked(false), 150, tip: "Plays the effect picked under \"assign\" on the song from the cursor for 2 s"),
                    Button("preview laser filter", () => PreviewPicked(true), 160, tip: "Sweeps the filter picked under \"laser filter switches\" from 0 to 1 and back, 2 s from the cursor")),
                Note("Also in the playtest (F5): effects follow your FX buttons and lasers. Presets like retrigger and gate need a wave_length (kson default 0 = silent).", 12)),
            // FX hold effects: definitions, assignment, parameter changes
            column(x0 + COLUMN + 10, COLUMN,
                Row(defModeButtons),
                Note("The two buttons switch the definition list between FX hold effects and laser filters.", 12),
                Header("FX hold effects", "fx_def"),
                Note("While an FX hold is held the game puts an effect on the music: retrigger, gate, flanger, phaser, wobble, bitcrusher …", 13),
                SubHeader("1 · definitions (optional)"),
                defList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Full, Spacing = new Vector2(4, 4) },
                Row(newDefType, field("new_name", "name", null, _ => { }, 180, 44).With(f => f.TooltipText = "Name of the new definition (empty = the type's name)"),
                    Button("add", () => AddDef(newDefType.Current.Value, Field("new_name").Box.Text), 60, tip: "Adds a definition of the chosen type with default parameters")),
                Note("A definition is a preset with its own parameter values under a name you choose. Without any, the presets play as they are.", 12),
                SubHeader("parameters of the selected definition"),
                paramEditor = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 4) },
                Note("kson value syntax: Off>OnMin-OnMax, e.g. 1/8 or 0%>100%. Empty = the default.", 12),
                SubHeader("2 · assign to selected FX holds", "fx_assign"),
                Row(assignHolder = new Container { Size = new Vector2(200, 26), Child = assignName },
                    Button("assign", () => AssignToSelectedHolds(assignName.Current.Value), 80, tip: "Every selected FX hold plays this effect"),
                    Button("clear", () => AssignToSelectedHolds(null), 60, EditorColours.Osu.MenuRed, "The selected FX holds play no effect")),
                Note("Select the holds first (\"select FX in view\", the list, or the compose tab).", 12),
                SubHeader("3 · parameter changes over time", "fx_param_change"),
                Row(field("pc_param", "param", null, _ => { }, 180, 50).With(f => f.TooltipText = "Parameter name of the selected definition, e.g. wave_length"),
                    field("pc_value", "value", null, _ => { }, 180, 50).With(f => f.TooltipText = "New value in kson syntax"),
                    Button("add at cursor", addParamChange, 110, tip: "From the cursor on, the selected definition uses this value")),
                changeList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 2) },
                Note("Each row is a change of the selected definition; x deletes it.", 12)),
            // laser effects
            column(x0 + 2 * (COLUMN + 10), COLUMN,
                Header("laser filter switches", "laser_filter"),
                Note("Turning a laser filters the music. Which filter the lasers use changes at switch points in time.", 13),
                Note("peaking_filter (default): boosts a band that moves with the knob · low_pass_filter: muffles the highs · "
                     + "high_pass_filter: thins out the lows · bitcrusher: lo-fi digital crunch.", 12),
                laserNow = new SpriteText { Font = EditorFont.Regular(14), Colour = EditorColours.Text },
                Row(filterHolder = new Container { Size = new Vector2(200, 26), Child = filterName },
                    Button("at cursor", () => SetFilterAtCursor(filterName.Current.Value), 90, tip: "A filter switch at the cursor: every laser from there on uses this filter"),
                    Button("at selected sections", setFilterAtSections, 170, tip: "A filter switch at the start of each selected laser")),
                SubHeader("switches in the chart"),
                // a long list scrolls on its own (at most FILTER_LIST_MAX high) so the controls below stay in reach
                FilterListHolder = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Masking = true,
                    Child = new BasicScrollContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        ScrollbarOverlapsContent = false,
                        Child = filterList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 2) },
                    },
                },
                Note("Each row is an existing switch (position, filter); x deletes it. A long list scrolls.", 12),
                Header("filter gain", "filter_gain"),
                Note("How strongly the filter acts (ksh pfiltergain), from the cursor on.", 12),
                Row(field("filter_gain", "gain %", () => Num(Math.Round(EffectCommands.ValueAt(session.Chart.Audio.LaserEffects.LegacyFilterGain, cursorPulse, 0.5) * 100, 2)),
                    s => { if (TryNum(s, out double v)) session.Commands.Execute(EffectCommands.SetFilterGain(cursorPulse, Math.Clamp(v / 100, 0, 1))); }, 200, 60)
                    .With(f => f.TooltipText = "0-100 %, default 50; a new value starts at the cursor")),
                Header("slam volume", "slam_volume"),
                Note("Volume of the slam sound (ksh chokkakuvol), from the cursor on.", 12),
                Row(field("slam_volume", "volume %", () => Num(Math.Round(EffectCommands.ValueAt(session.Chart.Audio.KeySound.LaserVolume, cursorPulse, 1) * 100, 2)),
                    s => { if (TryNum(s, out double v)) session.Commands.Execute(EffectCommands.SetSlamVolume(cursorPulse, Math.Clamp(v / 100, 0, 1))); }, 200, 70)
                    .With(f => f.TooltipText = "0-100 %, default 100; a new value starts at the cursor")),
                Header("peaking filter delay", "filter_delay"),
                Note("How late the peaking filter follows the knob, for the whole chart (ksh pfilterdelay).", 12),
                Row(field("filter_delay", "ms", () => session.Chart.Audio.LaserEffects.PeakingFilterDelay?.ToString(Inv) ?? "",
                    s => session.Commands.Execute(EffectCommands.SetPeakingFilterDelay(TryNum(s, out double v) ? (int)Math.Clamp(v, 0, 160) : null)), 160, 40)
                    .With(f => f.TooltipText = "0-160 ms, empty = default 40")),
                ExportSection = Collapsible("what each format keeps (kson / ksh / VOX)",
                    new ExportMatrixPanel(f => f.Id.StartsWith("fx_") || f.Id.StartsWith("laser") || f.Id.StartsWith("filter") || f.Id is "slam_volume" or "chip_sound", COLUMN - 20))),
        };

        LaserDefs.BindValueChanged(_ =>
        {
            SelectedDef = null;
            builtVersion = -1;
        });
        session.ChartChanged += onChanged;
        session.Selection.Changed += onSelection;
    }

    private void onChanged() => builtVersion = -1;
    private void onSelection() => selectionDirty = true;

    protected override void Dispose(bool isDisposing)
    {
        session.ChartChanged -= onChanged;
        session.Selection.Changed -= onSelection;
        base.Dispose(isDisposing);
    }

    private long cursorPulse => Math.Max(0, session.Timing.Snap((long)Math.Round(session.MsToPulse(clock.CurrentTime.Value)), compose.Divisor.Value));

    private IEnumerable<Note> selectedFx(bool holds) =>
        session.Selection.Notes.Where(r => r.Kind == NoteKind.Fx && r.Note.IsHold == holds).Select(r => r.Note);

    private string positionText(long pulse)
    {
        var p = session.Timing.PositionOf(pulse);
        return $"{p.Measure + 1:000}:{p.Beat + 1}:{p.PulseInBeat:000}";
    }

    // ---- actions (buttons and smoke test) --------------------------------------

    public void AddDef(string type, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? type : name.Trim();
        bool laser = LaserDefs.Value;
        if (EffectCommands.Defs(session.Chart, laser).Any(d => d.Name == name))
        {
            Message?.Invoke($"an effect named '{name}' exists");
            return;
        }

        session.Commands.Execute(new AddEffectDefCommand(laser, EffectCatalog.NewDef(name, type)));
        SelectedDef = name;
        builtVersion = -1;
    }

    public void SelectDef(string? name)
    {
        SelectedDef = name;
        builtVersion = -1;
    }

    public void SetDefParam(string param, string value)
    {
        var def = EffectCommands.Defs(session.Chart, LaserDefs.Value).FirstOrDefault(d => d.Name == SelectedDef);
        if (def == null) return;
        var p = new Dictionary<string, string>(def.Params);
        if (string.IsNullOrWhiteSpace(value)) p.Remove(param);
        else p[param] = value.Trim();
        session.Commands.Execute(new EditEffectDefCommand(LaserDefs.Value, def, def.Name, def.Type, p));
    }

    public void RenameDef(string newName)
    {
        var def = EffectCommands.Defs(session.Chart, LaserDefs.Value).FirstOrDefault(d => d.Name == SelectedDef);
        newName = newName.Trim();
        if (def == null || newName.Length == 0 || newName == def.Name) return;
        try
        {
            session.Commands.Execute(new EditEffectDefCommand(LaserDefs.Value, def, newName, def.Type, def.Params));
            SelectedDef = newName;
        }
        catch (InvalidOperationException e)
        {
            Message?.Invoke(e.Message);
        }
    }

    public int AssignToSelectedHolds(string? effect)
    {
        var holds = selectedFx(true).ToList();
        if (holds.Count == 0)
        {
            Message?.Invoke("select FX holds first (list, compose selection)");
            return 0;
        }

        session.Commands.Execute(EffectCommands.AssignToHolds(holds, effect));
        Message?.Invoke(effect == null ? $"cleared the effect of {holds.Count} holds" : $"{effect} on {holds.Count} holds");
        return holds.Count;
    }

    public string? ImportSound(string path)
    {
        if (session.Song == null)
        {
            Message?.Invoke("a scratch chart has no song folder to import into");
            return null;
        }

        try
        {
            // a launcher song is read-only: fork it first and copy into the writable folder
            string folder = session.EnsureWritableFolder();
            string name = KeySoundFiles.Import(folder, path.Trim().Trim('"'));
            CurrentSound = name;
            builtVersion = -1;
            Message?.Invoke($"imported {name}");
            return name;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            Message?.Invoke("import failed: " + e.Message);
            return null;
        }
    }

    public void SelectSound(string name)
    {
        CurrentSound = name;
        builtVersion = -1;
    }

    public bool PreviewSound(string name) => hitsounds.PlayKeySound(name, SoundVolume);

    private KeySoundRef currentRef => new KeySoundRef(CurrentSound, SoundVolume >= 0.999 ? null : Math.Round(SoundVolume, 3));

    public int AssignSoundToSelectedChips()
    {
        var chips = selectedFx(false).ToList();
        if (chips.Count == 0)
        {
            Message?.Invoke("select FX chips first");
            return 0;
        }

        session.Commands.Execute(new SetChipKeySoundCommand(chips, currentRef));
        return chips.Count;
    }

    /// <summary>kson has no per-chart default: "default" assigns the sound to every FX chip that has none.</summary>
    public int AssignSoundAsDefault()
    {
        var chips = session.Chart.Notes.Fx.SelectMany(l => l).Where(n => !n.IsHold && n.KeySound == null).ToList();
        if (chips.Count == 0) return 0;
        session.Commands.Execute(new SetChipKeySoundCommand(chips, currentRef, $"Set {CurrentSound} on {chips.Count} chips"));
        return chips.Count;
    }

    private void clearSoundOfSelected()
    {
        var chips = selectedFx(false).ToList();
        if (chips.Count > 0) session.Commands.Execute(new SetChipKeySoundCommand(chips, null));
    }

    public void SetFilterAtCursor(string? name) => session.Commands.Execute(new SetLaserFilterCommand(name, cursorPulse));

    private void setFilterAtSections()
    {
        var starts = session.Selection.Sections.Select(s => s.Section.Y).Distinct().ToList();
        if (starts.Count == 0)
        {
            Message?.Invoke("select laser points first");
            return;
        }

        session.Commands.BeginTransaction("Set laser filter");
        foreach (long y in starts) session.Commands.Execute(new SetLaserFilterCommand(filterName.Current.Value, y));
        session.Commands.Commit();
    }

    private void addParamChange()
    {
        if (SelectedDef == null)
        {
            Message?.Invoke("select an effect definition first");
            return;
        }

        string param = Field("pc_param").Box.Text.Trim(), value = Field("pc_value").Box.Text.Trim();
        if (param.Length == 0 || value.Length == 0) return;
        session.Commands.Execute(new SetParamChangeCommand(LaserDefs.Value, SelectedDef, param, cursorPulse, value));
    }

    /// <summary>
    /// "select FX in view": every FX chip / hold the playfield on the left shows (a hold reaching into the view counts),
    /// replacing the selection. Says what it selected (or that nothing is in view) and makes the selection flash in the
    /// playfield. Before, it took a measure before to six after the cursor: mostly notes off screen, and with the
    /// selection unchanged a click showed nothing at all (the 2026-10-27 report "nothing happens").
    /// </summary>
    public int SelectFxInView()
    {
        var (from, to) = Playfield.VisiblePulses;
        var picked = new List<NoteRef>();
        for (int l = 0; l < 2; l++)
            foreach (var n in session.Chart.Notes.Fx[l])
                if (n.EndY >= from && n.Y <= to) picked.Add(new NoteRef(NoteKind.Fx, l, n));

        if (picked.Count == 0)
        {
            // say that the old selection stays, or "selected: 1 FX …" above reads like a contradiction
            int kept = session.Selection.Notes.Count(n => n.Kind == NoteKind.Fx);
            Message?.Invoke(kept > 0
                ? $"No FX notes in view - kept the selection ({plural(kept, "FX note")}). Scroll the playfield to other FX notes first"
                : "No FX notes in view - scroll the playfield to them first");
            return 0;
        }

        session.Selection.Clear();
        foreach (var r in picked) session.Selection.Add(r);
        int holds = picked.Count(r => r.Note.IsHold);
        Message?.Invoke($"Selected {plural(holds, "FX hold")} and {plural(picked.Count - holds, "FX chip")} (blue in the playfield)");
        Playfield.FlashSelection();
        selectionText.FlashColour(EditorColours.Osu.Pink, 700);
        return picked.Count;
    }

    private static string plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    private (long From, long To) window()
    {
        long now = Math.Max(0, (long)session.MsToPulse(clock.CurrentTime.Value));
        int m = session.Timing.MeasureAt(now);
        return (session.Timing.MeasureStart(Math.Max(0, m - 1)), session.Timing.MeasureStart(m + 6));
    }

    // ---- refresh -----------------------------------------------------------------

    /// <summary>Highest the laser filter switch list grows before it scrolls.</summary>
    public const float FILTER_LIST_MAX = 240;

    /// <summary>The clipped, scrolling holder of the switch list (smoke test).</summary>
    public Container FilterListHolder { get; private set; } = null!;

    protected override void Update()
    {
        base.Update();
        FilterListHolder.Height = Math.Min(filterList.DrawHeight, FILTER_LIST_MAX);
        long cursor = cursorPulse;
        bool cursorMoved = cursor != lastCursorPulse;
        lastCursorPulse = cursor;

        if (builtVersion != session.Version || builtDef != SelectedDef || builtLaser != LaserDefs.Value)
        {
            builtVersion = session.Version;
            builtDef = SelectedDef;
            builtLaser = LaserDefs.Value;
            rebuildDefs();
            rebuildFilters();
            rebuildSounds();
            selectionDirty = true;
            cursorMoved = true;
        }

        if (cursorMoved)
        {
            laserNow.Text = $"at the cursor: {EffectCommands.FilterAt(session.Chart, cursor) ?? "peaking_filter (default)"}";
            foreach (var f in fields.Values) f.Refresh();
        }

        if (selectionDirty || cursorMoved && clock.IsPlaying.Value == false)
        {
            selectionDirty = false;
            rebuildNotes();
        }
    }

    private void rebuildNotes()
    {
        int holds = selectedFx(true).Count(), chips = selectedFx(false).Count();
        selectionText.Text = holds + chips == 0 ? "nothing selected" : $"selected: {plural(holds, "FX hold")}, {plural(chips, "FX chip")}";
        selectionText.Colour = holds + chips == 0 ? EditorColours.TextMuted : ComposePlayfield.SelectedColour;
        noteList.Clear();
        var (from, to) = window();
        int rows = 0;
        for (int l = 0; l < 2; l++)
        {
            foreach (var n in session.Chart.Notes.Fx[l])
            {
                if (n.Y < from || n.Y > to) continue;
                if (rows++ >= 24) break;
                var r = new NoteRef(NoteKind.Fx, l, n);
                string what = n.IsHold
                    ? n.Effects.Count > 0 ? string.Join(", ", n.Effects.Select(e => e.RelY == 0 ? e.Name : $"{e.Name}@+{e.RelY}")) : "no effect"
                    : n.KeySound is { } ks ? ks.Name + (ks.Volume is { } v ? $" {v * 100:0}%" : "") : "default tick";
                noteList.Add(new NoteRow($"FX-{(l == 0 ? 'L' : 'R')}  {(n.IsHold ? "hold" : "chip")}  {positionText(n.Y)}", what, session.Selection.Contains(r), () =>
                {
                    session.Selection.Toggle(r);
                }));
            }
        }

        if (rows == 0)
            noteList.Add(Note("No FX notes within a measure before and six after the cursor."));
    }

    private void rebuildDefs()
    {
        bool laser = LaserDefs.Value;
        defModeButtons[0].Alpha = laser ? 0.5f : 1;
        defModeButtons[1].Alpha = laser ? 1 : 0.5f;

        defList.Clear();
        var defs = EffectCommands.Defs(session.Chart, laser);
        foreach (var d in defs)
        {
            int uses = EffectCommands.Uses(session.Chart, d.Name, laser);
            var b = Button($"{d.Name} ({d.Type}, {uses}x)", () => SelectDef(d.Name), 230, d.Name == SelectedDef ? EditorColours.Osu.TabActive : EditorColours.Osu.MenuGrey,
                $"{d.Name}: a {d.Type} with its own parameters, used {uses}x. Click to edit its parameters below");
            defList.Add(b);
        }

        if (defs.Count == 0)
            defList.Add(Note(laser ? "No laser filter definitions: the presets (peaking_filter, low_pass_filter, high_pass_filter, bitcrusher) are used as they are." : "No definitions: the presets (retrigger, gate, flanger …) are used as they are. Add one to change parameters."));

        var names = EffectCatalog.AvailableNames(session.Chart, false);
        var laserNames = EffectCatalog.AvailableNames(session.Chart, true);
        replaceDropdown(assignHolder, ref assignName, names);
        replaceDropdown(filterHolder, ref filterName, laserNames);

        paramEditor.Clear();
        changeList.Clear();
        var def = defs.FirstOrDefault(x => x.Name == SelectedDef);
        if (def == null)
        {
            paramEditor.Add(Note("Select a definition to edit its parameters."));
            return;
        }

        paramEditor.Add(Row(
            new PanelField("name", () => def.Name, RenameDef, 260, 50),
            new SpriteText { Text = def.Type, Font = EditorFont.Bold(14), Colour = EditorColours.TextMuted },
            Button("delete", () =>
            {
                session.Commands.Execute(new RemoveEffectDefCommand(laser, def));
                SelectedDef = null;
            }, 70, EditorColours.Osu.MenuRed)));

        var spec = EffectCatalog.Find(def.Type);
        var keys = (spec?.Params.Select(p => p.Name) ?? Enumerable.Empty<string>()).Concat(def.Params.Keys).Distinct();
        foreach (string key in keys)
        {
            string k = key;
            string dflt = spec?.Params.FirstOrDefault(p => p.Name == k)?.Default ?? "";
            var f = new PanelField(k, () => def.Params.TryGetValue(k, out var v) ? v : "", s => SetDefParam(k, s), 330, 140);
            f.Box.PlaceholderText = dflt;
            fields["param_" + k] = f;
            paramEditor.Add(Row(f, new SpriteText { Text = dflt.Length > 0 ? "default " + dflt : "", Font = EditorFont.Regular(12), Colour = EditorColours.TextMuted }));
        }

        if (EffectCommands.ParamChanges(session.Chart, laser).TryGetValue(def.Name, out var prms))
        {
            foreach (var (param, list) in prms)
                foreach (var pv in list)
                {
                    string p = param;
                    long y = pv.Y;
                    changeList.Add(Row(
                        new SpriteText { Text = $"{positionText(y)}  {p} = {pv.Value}", Font = EditorFont.Regular(14), Colour = EditorColours.Text, Width = 400 },
                        Button("x", () => session.Commands.Execute(new SetParamChangeCommand(laser, def.Name, p, y, null)), 26, EditorColours.Osu.MenuRed, "Deletes this parameter change")));
                }
        }
    }

    private static void replaceDropdown(Container holder, ref OsuDropdown<string> dropdown, IReadOnlyList<string> items)
    {
        if (dropdown.Items.SequenceEqual(items)) return;
        string current = dropdown.Current.Value;
        var replacement = new OsuDropdown<string>(items) { Width = dropdown.Width, FontSize = 16, Height = 26 };
        if (items.Contains(current)) replacement.Current.Value = current;
        holder.Child = replacement;
        dropdown = replacement;
    }

    private void rebuildFilters()
    {
        filterList.Clear();
        var events = session.Chart.Audio.LaserEffects.PulseEvents.SelectMany(kv => kv.Value.Select(y => (Y: y, Name: kv.Key))).OrderBy(e => e.Y).ToList();
        foreach (var (y, name) in events.Take(30))
        {
            long py = y;
            filterList.Add(Row(
                new SpriteText { Text = $"{positionText(py)}  {name}", Font = EditorFont.Regular(14), Colour = EditorColours.Text, Width = 400 },
                Button("x", () => session.Commands.Execute(new SetLaserFilterCommand(null, py)), 26, EditorColours.Osu.MenuRed, "Deletes this filter switch (lasers keep the filter before it)")));
        }

        if (events.Count == 0) filterList.Add(Note("No filter switches: lasers use peaking_filter."));
        else if (events.Count > 30) filterList.Add(Note($"… {events.Count - 30} more"));
    }

    private void rebuildSounds()
    {
        soundList.Clear();
        var names = EffectCatalog.BuiltinChipSounds.ToList();
        if (session.Song != null)
            names.AddRange(KeySoundFiles.List(session.Song.FolderPath, Path.GetFileName(session.AudioPath ?? session.Chart.Audio.Bgm.Filename)));
        foreach (string n in names)
        {
            string name = n;
            int uses = session.Chart.Notes.Fx.Sum(l => l.Count(x => x.KeySound?.Name == name));
            soundList.Add(Button(uses > 0 ? $"{name} ({uses})" : name, () =>
            {
                SelectSound(name);
                PreviewSound(name);
            }, Math.Max(70, 9 * name.Length + 40), name == CurrentSound ? EditorColours.Osu.TabActive : EditorColours.Osu.MenuGrey,
                (EffectCatalog.BuiltinChipSounds.Contains(name) ? $"{name}: built-in K-Shoot chip sound" : $"{name}: imported file in the song folder")
                + (uses > 0 ? $", on {uses} chip(s)" : "") + ". Click: pick and play"));
        }

        soundText.Text = $"sound: {CurrentSound}";
    }

    private partial class NoteRow : ClickableContainer
    {
        private readonly Box hover;

        public NoteRow(string left, string right, bool selected, Action toggle)
        {
            RelativeSizeAxes = Axes.X;
            Height = 22;
            Action = toggle;
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = selected ? ComposePlayfield.SelectedColour.Opacity(0.45f) : Colour4.White.Opacity(0.04f) },
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 },
                new SpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, X = 6, Text = left, Font = EditorFont.Regular(14), Colour = Colour4.White },
                new SpriteText { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, X = 230, Text = right, Font = EditorFont.Regular(14), Colour = EditorColours.Lanes.Fx, Truncate = true, Width = 240 },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeTo(0.08f, 60);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(60);
            base.OnHoverLost(e);
        }
    }
}
