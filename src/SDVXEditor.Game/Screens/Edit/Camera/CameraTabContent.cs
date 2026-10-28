using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit.Components;
using SDVXEditor.Game.Screens.Edit.Compose;
using static SDVXEditor.Game.Screens.Edit.Components.PanelControls;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>
/// The camera tab (replaces osu!'s "design" tab): left a read-only compose playfield for musical
/// context, centre the camera curve lanes (same time axis), right the live camera preview with the
/// point inspector, or the background / post-effect settings, or the export matrix.
/// </summary>
public partial class CameraTabContent : CompositeDrawable
{
    public const float PREVIEW_SCALE = 0.55f;
    public const float LEFT_WIDTH = PlayfieldLayout.TOTAL_WIDTH * PREVIEW_SCALE;
    public const float PANEL_WIDTH = 760;

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly ComposeEditor compose;
    private readonly EditorToolState tools;
    private readonly IBindable<float> zoom;

    public CameraLaneEditor LaneEditor { get; }
    public CameraLanes Lanes { get; private set; } = null!;
    public CameraPreview Preview { get; private set; } = null!;
    public ComposePlayfield ContextPlayfield { get; private set; } = null!;

    /// <summary>0 camera, 1 background, 2 export, 3 effects (<see cref="EFFECTS_VIEW"/>; the effect lanes replace the camera lanes).</summary>
    public readonly BindableInt View = new BindableInt();

    public Action<string>? Message { get; set; }

    private readonly Dictionary<string, PanelField> fields = new Dictionary<string, PanelField>();
    private VisibilityContainer[] views = null!;
    private Container cameraLaneArea = null!;
    private Container effectLaneArea = null!;
    private FlatButton[]? viewButtons;
    private SpriteText inspectorTitle = null!;
    private FillFlowContainer graphInspector = null!;
    private FillFlowContainer patternInspector = null!;
    private FillFlowContainer postEffectList = null!;
    private FillFlowContainer spList = null!;
    private PinkCheckbox keepTilt = null!;
    private PinkCheckbox layerTilt = null!;
    private PinkCheckbox layerSpin = null!;
    private FlatButton[] patternKindButtons = null!;
    private FlatButton directionButton = null!;
    private OsuDropdown<string> postEffectName = null!;
    private bool dirty = true;

    public CameraTabContent(EditorSession session, EditorClock clock, ComposeEditor compose, EditorToolState tools, IBindable<float> zoom)
    {
        this.session = session;
        this.clock = clock;
        this.compose = compose;
        this.tools = tools;
        this.zoom = zoom.GetBoundCopy();
        LaneEditor = new CameraLaneEditor(session) { Message = m => Message?.Invoke(m) };
        LaneEditor.Divisor.BindTo(compose.Divisor);
        LaneEditor.GridSnap.BindTo(compose.GridSnap);
        createEffectEditor();
        RelativeSizeAxes = Axes.Both;
    }

    /// <summary>A named side-panel field (smoke test drives them like a user).</summary>
    public PanelField Field(string name) => fields[name];

    private PanelField field(string key, string label, Func<string> get, Action<string> commit, float width = 240, float labelWidth = 90)
    {
        var f = new PanelField(label, get, commit, width, labelWidth);
        fields[key] = f;
        return f;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var laneHeaders = new Container { Width = CameraLanes.TotalWidth, Height = 46 };
        foreach (var lane in CameraLanes.Lanes)
        {
            string badge = lane switch
            {
                CamLane.Tilt => "tilt_manual",
                CamLane.Patterns => "spin",
                _ => CameraGraphs.Name(CameraLaneEditor.GraphOf(lane)),
            };
            laneHeaders.Add(new Container
            {
                X = CameraLanes.LaneLeft(lane),
                Width = CameraLaneEditor.LANE_WIDTH,
                RelativeSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("202024").Opacity(0.92f) },
                    new SpriteText { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Y = 3, Text = CameraLaneEditor.LaneName(lane), Font = EditorFont.Bold(14), Colour = Colour4.White },
                    new ExportBadge(badge, 9) { Anchor = Anchor.BottomCentre, Origin = Anchor.BottomCentre, Y = -4 },
                },
            });
        }

        InternalChildren = new Drawable[]
        {
            // left: compose playfield, read-only, horizontally compressed
            new Container
            {
                X = 8,
                Y = 6,
                Width = LEFT_WIDTH,
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
                Masking = true,
                Child = new Container
                {
                    Width = PlayfieldLayout.TOTAL_WIDTH,
                    RelativeSizeAxes = Axes.Y,
                    Scale = new Vector2(PREVIEW_SCALE, 1),
                    Child = ContextPlayfield = new ComposePlayfield(session, clock, compose, tools, zoom) { ReadOnly = true },
                },
            },
            // centre: curve lanes (camera view) or effect lanes (effects view)
            cameraLaneArea = new LaneArea
            {
                X = 8 + LEFT_WIDTH + 12,
                Y = 6,
                Width = CameraLanes.TotalWidth,
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
                Masking = true,
                Children = new Drawable[]
                {
                    Lanes = new CameraLanes(session, clock, LaneEditor, zoom),
                    laneHeaders,
                },
            },
            effectLaneArea = new LaneArea
            {
                X = 8 + LEFT_WIDTH + 12,
                Y = 6,
                Width = EffectLaneEditor.TotalWidth,
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
                Masking = true,
                Alpha = 0,
                Children = new Drawable[]
                {
                    FxLanes = new EffectLanes(session, clock, FxEditor, zoom),
                    effectLaneHeaders(),
                },
            },
            // right: preview / background / export
            new Container
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                X = -8,
                Y = 6,
                Width = PANEL_WIDTH,
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
                Masking = true,
                CornerRadius = 6,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.PanelDark },
                    new FillFlowContainer
                    {
                        Position = new Vector2(10, 8),
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        // shown in this order; viewButtons stays indexed by view (0 camera, 1 background, 2 export, 3 effects)
                        Children = new Drawable[] { viewButtonsFor(0), viewButtonsFor(3), viewButtonsFor(1), viewButtonsFor(2) },
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = 42, Left = 10, Right = 10, Bottom = 8 },
                        Children = views = new[] { cameraView(), backgroundView(), exportView(), effectsView() },
                    },
                },
            },
        };

        View.BindValueChanged(e =>
        {
            for (int i = 0; i < views.Length; i++)
            {
                views[i].Alpha = i == e.NewValue ? 1 : 0;
                views[i].State = i == e.NewValue ? Visibility.Visible : Visibility.Hidden;
                viewButtons![i].Alpha = i == e.NewValue ? 1 : 0.55f;
            }

            bool fx = e.NewValue == EFFECTS_VIEW;
            effectLaneArea.Alpha = fx ? 1 : 0;
            cameraLaneArea.Alpha = fx ? 0 : 1;
            dirty = true;
        }, true);

        session.ChartChanged += onChartChanged;
        LaneEditor.SelectionChanged += () => dirty = true;
    }

    private void onChartChanged()
    {
        LaneEditor.Resync();
        FxEditor.Resync();
        dirty = true;
    }

    private FlatButton viewButtonsFor(int view)
    {
        viewButtons ??= new FlatButton[4];
        // "screen fx", not "effects": the top bar's effects tab is the audio FX page, a second "effects" here made two
        // routes look like one panel (audit U10)
        string[] names = { "camera", "background", "export", "screen fx" };
        return viewButtons[view] = Button(names[view], () => View.Value = view, 120);
    }

    protected override void Update()
    {
        base.Update();
        if (!dirty) return;
        dirty = false;
        refreshInspector();
        refreshBackground();
        refreshEffects();
    }

    protected override void Dispose(bool isDisposing)
    {
        session.ChartChanged -= onChartChanged;
        base.Dispose(isDisposing);
    }

    private string positionText(long pulse)
    {
        var p = session.Timing.PositionOf(pulse);
        return $"{p.Measure + 1:000}:{p.Beat + 1}:{p.PulseInBeat:000} (pulse {pulse})";
    }

    private long cursorPulse => LaneEditor.PulseAt(clock.CurrentTime.Value);

    // ---- camera view ---------------------------------------------------------

    private VisibilityContainer cameraView()
    {
        var kinds = Enum.GetValues<CamPatternKind>();
        patternKindButtons = kinds.Select(k => Button(CameraCommands.PatternName(k), () =>
        {
            LaneEditor.PatternKind.Value = k;
            dirty = true;
        }, 96)).ToArray();
        directionButton = Button("dir R", () =>
        {
            LaneEditor.PatternDirection.Value = -LaneEditor.PatternDirection.Value;
            dirty = true;
        }, 70);

        return new VisibilityContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    Preview = new CameraPreview(session, clock) { RelativeSizeAxes = Axes.X, Height = 330 },
                    Header("selected anchor"),
                    inspectorTitle = new SpriteText { Font = EditorFont.Regular(15), Colour = EditorColours.Text },
                    graphInspector = Row(
                        field("value", "value", () => selValue(), s => { if (TryNum(s, out double v)) LaneEditor.EditSelected(v: v); }, 180, 50),
                        field("slam", "jump to", () => selVf(), s =>
                        {
                            if (string.IsNullOrWhiteSpace(s)) LaneEditor.EditSelected(clearVf: true);
                            else if (TryNum(s, out double v)) LaneEditor.EditSelected(vf: v);
                        }, 180, 60),
                        field("curve_a", "curve a", () => selCurve()?.A is { } a ? Num(a) : "", s => setCurve(s, null), 160, 60),
                        field("curve_b", "curve b", () => selCurve()?.B is { } b ? Num(b) : "", s => setCurve(null, s), 160, 60),
                        Button("linear", () => LaneEditor.EditSelected(clearCurve: true), 70),
                        Button("delete", () => LaneEditor.DeleteSelected(), 70, EditorColours.Osu.MenuRed)),
                    patternInspector = Row(
                        Button("flip L/R", () => LaneEditor.EditSelectedPattern(p => p.Direction = -p.Direction), 80),
                        field("pat_length", "length", () => LaneEditor.SelectedPattern is { } sp ? Num(sp.Pattern.Length / (double)Timebase.PulsesPerWholeNote * 4) : "",
                            s => { if (TryNum(s, out double beats) && beats > 0) LaneEditor.EditSelectedPattern(p => p.Length = (long)Math.Round(beats * Timebase.PulsesPerWholeNote / 4)); }, 150, 50),
                        field("pat_scale", "scale", () => LaneEditor.SelectedPattern?.Pattern.SwingScale is { } v ? Num(v) : "",
                            s => LaneEditor.EditSelectedPattern(p => p.SwingScale = TryNum(s, out double v) ? v : null), 130, 45),
                        field("pat_repeat", "repeat", () => LaneEditor.SelectedPattern?.Pattern.SwingRepeat is { } v ? v.ToString(Inv) : "",
                            s => LaneEditor.EditSelectedPattern(p => p.SwingRepeat = TryNum(s, out double v) ? (int)v : null), 130, 50),
                        field("pat_decay", "decay", () => LaneEditor.SelectedPattern?.Pattern.SwingDecayOrder is { } v ? v.ToString(Inv) : "",
                            s => LaneEditor.EditSelectedPattern(p => p.SwingDecayOrder = TryNum(s, out double v) ? (int)v : null), 130, 45),
                        Button("delete", () => LaneEditor.DeleteSelected(), 70, EditorColours.Osu.MenuRed)),
                    Header("tilt mode at the cursor", "tilt_auto"),
                    Row(
                        Button("normal", () => setTiltMode(TiltMode.Normal), 84),
                        Button("bigger", () => setTiltMode(TiltMode.Bigger), 84),
                        Button("biggest", () => setTiltMode(TiltMode.Biggest), 84),
                        Button("zero", () => setTiltMode(TiltMode.Zero), 84),
                        keepTilt = new PinkCheckbox("keep", 16)),
                    Header("new spin / swing (snaps to a slam)", "swing"),
                    Row(patternKindButtons.Cast<Drawable>().Append(directionButton).ToArray()),
                    Note("Click a lane: add an anchor at the snapped time (value from x) and drag it. Clicks in a row are curved like an osu! slider: "
                         + "2 anchors straight, 3 a perfect curve, 4+ a Bezier; while adding, a click on an anchor changes its type, right click on empty space finishes. "
                         + "Right click an anchor: Straight / Perfect curve / Bezier / Inherit / Delete; right click a curve: its type. Ctrl+click a curve: insert an anchor. "
                         + "Drag an anchor: time + value. Shift: instant jump. Alt-drag in a straight two-anchor segment: quick ease curve. "
                         + "Tilt lane: manual values; auto modes come from the buttons above and show as a coloured strip. "
                         + "A pattern framed red has no matching slam and is ignored by the game.", 13),
                },
            },
        };
    }

    private LaserAnchor? selAnchor => LaneEditor.SelectedAnchor is { } a ? a.Path.Anchors[a.Index] : null;

    private string selValue()
    {
        if (selAnchor is { } a) return Num(a.V);
        if (LaneEditor.SelectedAutoTilt is { } t) return TiltValue.NameOf(t.Value.Auto!.Value);
        return "";
    }

    private string selVf()
    {
        if (selAnchor is { } a) return a.IsSlam ? Num(a.Vf) : "";
        if (LaneEditor.SelectedAutoTilt is { Final: { IsAuto: false } f }) return Num(f.Manual);
        return "";
    }

    private Curve? selCurve() => selAnchor is { } a ? a.Ease : LaneEditor.SelectedAutoTilt?.Curve;

    private void setCurve(string? a, string? b)
    {
        var c = selCurve() ?? new Curve(0.5, 0.5);
        double na = c.A, nb = c.B;
        if (a != null && TryNum(a, out double va)) na = Math.Clamp(va, 0, 1);
        if (b != null && TryNum(b, out double vb)) nb = Math.Clamp(vb, 0, 1);
        if (na == nb) LaneEditor.EditSelected(clearCurve: true);
        else LaneEditor.EditSelected(curve: new Curve(na, nb));
    }

    private void setTiltMode(TiltMode mode)
    {
        if (keepTilt.Current.Value)
        {
            mode = mode switch
            {
                TiltMode.Normal => TiltMode.KeepNormal,
                TiltMode.Bigger => TiltMode.KeepBigger,
                TiltMode.Biggest => TiltMode.KeepBiggest,
                _ => mode,
            };
        }

        LaneEditor.SetTiltMode(mode, cursorPulse);
    }

    private void refreshInspector()
    {
        string title;
        bool graph = false, pattern = false;
        if (LaneEditor.SelectedAnchor is { } sa)
        {
            var a = sa.Path.Anchors[sa.Index];
            string type = a.Type is { } ty ? ty switch { LaserCurveType.PerfectCurve => "perfect curve", LaserCurveType.Bezier => "Bezier", _ => "straight" } : "inherit";
            title = $"{CameraPaths.Name(sa.Run.Lane)} anchor {sa.Index + 1} of {sa.Path.Count} ({type}{(a.Auto ? ", auto" : "")}) at {positionText(sa.Run.Y + a.Ry)}";
            graph = true;
        }
        else if (LaneEditor.SelectedAutoTilt is { } t)
        {
            title = $"tilt {TiltValue.NameOf(t.Value.Auto!.Value)} at {positionText(t.Y)}";
            graph = true;
        }
        else if (LaneEditor.SelectedPattern is { } sp)
        {
            bool ok = CameraCommands.HasMatchingSlam(session.Chart, sp.Pattern);
            title = $"{CameraCommands.PatternName(sp.Kind)} {(sp.Pattern.Direction < 0 ? "left" : "right")} at {positionText(sp.Pattern.Y)}{(ok ? "" : "  - no matching slam!")}";
            pattern = true;
        }
        else
            title = "nothing selected - click a point in the lanes";

        inspectorTitle.Text = title;
        graphInspector.Alpha = graph ? 1 : 0.35f;
        patternInspector.Alpha = pattern ? 1 : 0.35f;
        foreach (var f in fields.Values) f.Refresh();

        for (int i = 0; i < patternKindButtons.Length; i++)
            patternKindButtons[i].Alpha = (int)LaneEditor.PatternKind.Value == i ? 1 : 0.5f;
        directionButton.Alpha = 1;
        // FlatButton text is fixed at construction: show the direction through the colour
        directionButton.Colour = LaneEditor.PatternDirection.Value < 0 ? EditorColours.Lanes.LaserLeft : EditorColours.Lanes.LaserRight;
    }

    // ---- background view ------------------------------------------------------

    private void editBg(Action<BgInfo> change, string description)
    {
        var bg = session.Chart.Bg.Clone();
        change(bg);
        session.Commands.Execute(CameraCommands.SetBg(bg, description));
    }

    private void editVox(Action<VoxExtrasInfo> change, string description)
    {
        var v = session.Chart.VoxExtras.Clone();
        change(v);
        session.Commands.Execute(CameraCommands.SetVoxExtras(v, description));
    }

    /// <summary>Adds a #POSTEFFECT row (Strength 0 → 1 over a quarter measure) at the cursor.</summary>
    public void AddPostEffectAtCursor(string name) => editVox(v =>
    {
        v.PostEffects.Add(new PostEffectRow(cursorPulse, name, "Strength", 0, 1, Timebase.PulsesPerWholeNote / 4));
        v.PostEffects.Sort((a, b) => a.Y.CompareTo(b.Y));
    }, "Add post effect");

    private static void setBgEntry(BgInfo bg, int index, string name)
    {
        name = name.Trim();
        while (bg.LegacyBg.Count <= index) bg.LegacyBg.Add("");
        bg.LegacyBg[index] = name;
        while (bg.LegacyBg.Count > 0 && bg.LegacyBg[^1].Length == 0) bg.LegacyBg.RemoveAt(bg.LegacyBg.Count - 1);
    }

    private VisibilityContainer backgroundView()
    {
        postEffectName = new OsuDropdown<string>(VoxExtrasInfo.PostEffectNames) { Width = 200, FontSize = 16, Height = 26 };

        return new VisibilityContainer
        {
            RelativeSizeAxes = Axes.Both,
            Child = new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                ScrollbarVisible = false,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Children = new Drawable[]
                    {
                        BgPreview = new BackgroundPreview(session),
                        Note("K-Shoot shows a picture, an animation layer and a movie behind the lanes; the game instead plays one of its own "
                             + "backgrounds, chosen by the VOX bg_no. kson and ksh keep all of it, the VOX export only bg_no and the post effects.", 13),
                        Header("background image (kson bg.legacy.bg / ksh bg=)", "bg_image"),
                        Note("The picture behind the lanes in K-Shoot: an image file in the song folder, or the name of a K-Shoot preset. "
                             + "The second one replaces it once the gauge reaches 70 %.", 12),
                        Row(
                            field("bg1", "image", () => session.Chart.Bg.LegacyBg.ElementAtOrDefault(0) ?? "", s => editBg(b => setBgEntry(b, 0, s), "Change background"), 360)
                                .With(f => f.TooltipText = "File name in the song folder (e.g. bg.jpg) or a preset name"),
                            field("bg2", "gauge ≥70%", () => session.Chart.Bg.LegacyBg.ElementAtOrDefault(1) ?? "", s => editBg(b => setBgEntry(b, 1, s), "Change background"), 360)
                                .With(f => f.TooltipText = "Shown instead of the first once the gauge reaches 70 % (empty = keep the first)")),
                        Note("Presets: " + string.Join(" ", CameraCommands.BgPresets) + ".", 12),
                        Header("animation layer (bg.legacy.layer / ksh layer=)", "bg_layer"),
                        Note("An animated sprite sheet K-Shoot plays over the picture (a file in the song folder or a preset); it can turn with the lane's tilt and spins.", 12),
                        Row(
                            field("layer", "layer", () => session.Chart.Bg.Layer?.Filename ?? "", s => editBg(b =>
                            {
                                if (string.IsNullOrWhiteSpace(s)) b.Layer = null;
                                else (b.Layer ??= new BgLayer()).Filename = s.Trim();
                            }, "Change layer"), 360).With(f => f.TooltipText = "Sprite sheet in the song folder or a preset name; empty = no layer"),
                            field("layer_ms", "loop ms", () => session.Chart.Bg.Layer is { } l ? l.DurationMs.ToString(Inv) : "", s =>
                            {
                                if (session.Chart.Bg.Layer != null && TryNum(s, out double v)) editBg(b => b.Layer!.DurationMs = (long)v, "Change layer");
                            }, 200, 70).With(f => f.TooltipText = "Length of one animation loop in ms; < 0 plays it backwards, 0 follows the tempo"),
                            layerTilt = new PinkCheckbox("rotate with tilt", 15) { TooltipText = "The layer tilts with the lane" },
                            layerSpin = new PinkCheckbox("rotate with spin", 15) { TooltipText = "The layer turns with spins and swings" }),
                        Note("Presets: " + string.Join(" ", CameraCommands.LayerPresets) + ".", 12),
                        Header("movie (bg.legacy.movie / ksh v=, vo=)", "bg_movie"),
                        Note("A video K-Shoot plays behind the lanes. offset = where the movie starts relative to the chart (negative: the chart starts that far into it).", 12),
                        Row(
                            field("movie", "movie", () => session.Chart.Bg.Movie?.Filename ?? "", s => editBg(b =>
                            {
                                if (string.IsNullOrWhiteSpace(s)) b.Movie = null;
                                else (b.Movie ??= new BgMovie()).Filename = s.Trim();
                            }, "Change movie"), 360).With(f => f.TooltipText = "Video file in the song folder; empty = no movie"),
                            field("movie_ms", "offset ms", () => session.Chart.Bg.Movie is { } m ? m.OffsetMs.ToString(Inv) : "", s =>
                            {
                                if (session.Chart.Bg.Movie != null && TryNum(s, out double v)) editBg(b => b.Movie!.OffsetMs = (long)v, "Change movie");
                            }, 200, 70).With(f => f.TooltipText = "Movie start relative to the chart start in ms")),
                        Header("game background (VOX bg_no)", "bg_no"),
                        Note("The background the game plays behind the lanes: a number into the game's own background set (music_db <bg_no>). "
                             + "The preview above names a song that uses it. Empty = 0, the default background.", 12),
                        Row(field("bg_no", "bg_no", () => session.Chart.VoxExtras.BgNo?.ToString(Inv) ?? "", s => editVox(v =>
                        {
                            v.BgNo = TryNum(s, out double n) ? (int)Math.Clamp(n, 0, 65535) : null;
                        }, "Change bg_no"), 200, 60).With(f => f.TooltipText = "0-65535; real songs use e.g. 2, 7, 10, 12, 60, 63")),
                        bgNoExport = Note("", 12),
                        Header("post effects (VOX #POSTEFFECT)", "post_effect"),
                        Note("Screen effects the game puts over everything at a point in time (chromatic aberration, blur, …), each with a parameter "
                             + "that moves from one value to another. Many backgrounds have no effect stages of their own, so test it in the game.", 12),
                        Row(postEffectName, Button("add at cursor", () => AddPostEffectAtCursor(postEffectName.Current.Value), 120,
                            tip: "Adds the effect at the cursor: Strength 0 → 1 over a quarter measure")),
                        postEffectList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 3) },
                        Header("SPCONTROLER rows (VOX)", "sp_controller"),
                        Note("Raw VOX camera / background control rows, written as they are. \"add template defaults\" adds the converter template's rows to edit.", 12),
                        Row(Button("add template defaults", () => editVox(v => v.SpController.AddRange(VoxExtrasInfo.TemplateSpController), "Add SPCONTROLER defaults"), 190,
                                tip: "Adds the Realize / AIRL / AIRR rows of the converter's template"),
                            Button("clear", () => editVox(v => v.SpController.Clear(), "Clear SPCONTROLER rows"), 70, EditorColours.Osu.MenuRed, "Removes every SPCONTROLER row")),
                        spList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 3) },
                        vxNote = Note("", 12),
                    },
                },
            },
        };
    }

    private int postEffectVersion = -1;
    private TextFlowContainer bgNoExport = null!;
    private TextFlowContainer vxNote = null!;

    /// <summary>The export line under bg_no and under the post effect / SPCONTROLER rows (smoke test).</summary>
    public string BgNoExportText { get; private set; } = "";

    public string VoxRowsText { get; private set; } = "";

    /// <summary>The background view's preview (image / preset / layer / movie still and the bg_no line).</summary>
    public BackgroundPreview BgPreview { get; private set; } = null!;

    [Resolved(CanBeNull = true)]
    private VoxConverterDetector? converter { get; set; }

    private IBindable<ConverterDetection>? detection;

    /// <summary>
    /// The export lines of the background view, for the detected ksh2vox version (they said "a later VOX export" and
    /// "ksh2vox 0.17.0 and later" whatever converter was installed).
    /// </summary>
    public static (string BgNo, string Vox) VoxNotes(ConverterDetection d)
    {
        if (d.State == ConverterDetectState.Detected && d.Version is { } version)
        {
            string name = $"ksh2vox {version}";
            return ExportSupport.MapsToVox(version)
                ? ($"kson and ksh keep it; the VOX export ({name}) writes it to music_db <bg_no>.",
                   $"Post effects and SPCONTROLER rows are editor data in the kson (ksh cannot hold them); the VOX export ({name}) writes them in place of the template's rows.")
                : ($"kson and ksh keep it; {name} writes bg_no 0 (needs ksh2vox {ExportSupport.VoxMappingVersion} or newer).",
                   $"Post effects and SPCONTROLER rows are editor data in the kson (ksh cannot hold them); {name} writes its template rows instead (needs ksh2vox {ExportSupport.VoxMappingVersion} or newer).");
        }

        string unknown = d.State == ConverterDetectState.Failed
            ? "the VOX converter was not found (Options → Export shows why)"
            : "the VOX converter's version is being detected";
        return ($"kson and ksh keep it; {unknown}: ksh2vox {ExportSupport.VoxMappingVersion} or newer writes it to music_db <bg_no>.",
                $"Post effects and SPCONTROLER rows are editor data in the kson (ksh cannot hold them); {unknown}: ksh2vox {ExportSupport.VoxMappingVersion} or newer writes them into the VOX.");
    }

    private void showVoxNotes(ConverterDetection d)
    {
        var (bgNo, vox) = VoxNotes(d);
        bgNoExport.Text = BgNoExportText = bgNo;
        vxNote.Text = VoxRowsText = vox;
    }

    private void refreshBackground()
    {
        var layer = session.Chart.Bg.Layer;
        layerTilt.Current.Value = layer?.RotateTilt ?? true;
        layerSpin.Current.Value = layer?.RotateSpin ?? true;
        layerTilt.Alpha = layerSpin.Alpha = layer != null ? 1 : 0.4f;

        if (postEffectVersion == session.Version) return;
        postEffectVersion = session.Version;

        postEffectList.Clear();
        var vox = session.Chart.VoxExtras;
        for (int i = 0; i < vox.PostEffects.Count; i++)
        {
            int index = i;
            var r = vox.PostEffects[i];
            postEffectList.Add(Row(
                new SpriteText { Text = positionText(r.Y), Font = EditorFont.Regular(13), Colour = EditorColours.TextMuted, Width = 170, Truncate = true },
                new SpriteText { Text = r.Name, Font = EditorFont.Bold(14), Colour = Colour4.White, Width = 140, Truncate = true },
                new PanelField("param", () => r.Param, s => editVox(v => v.PostEffects[index].Param = s.Trim(), "Edit post effect"), 130, 44),
                new PanelField("from", () => Num(r.From), s => { if (TryNum(s, out double d)) editVox(v => v.PostEffects[index].From = d, "Edit post effect"); }, 90, 36),
                new PanelField("to", () => Num(r.To), s => { if (TryNum(s, out double d)) editVox(v => v.PostEffects[index].To = d, "Edit post effect"); }, 80, 24),
                Button("x", () => editVox(v => v.PostEffects.RemoveAt(index), "Delete post effect"), 26, EditorColours.Osu.MenuRed, "Deletes this post effect")));
        }

        spList.Clear();
        foreach (var (r, i) in vox.SpController.Select((r, i) => (r, i)))
        {
            int index = i;
            spList.Add(Row(
                new SpriteText
                {
                    Text = $"{positionText(r.Y)}  {r.Name}  {r.Type}  {r.Length}  {Num(r.P1)} {Num(r.P2)} {Num(r.P3)} {Num(r.P4)}",
                    Font = EditorFont.Regular(13),
                    Colour = EditorColours.Text,
                    Width = 650,
                    Truncate = true,
                },
                Button("x", () => editVox(v => v.SpController.RemoveAt(index), "Delete SPCONTROLER row"), 26, EditorColours.Osu.MenuRed, "Deletes this row")));
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        bindEffectsView();
        showVoxNotes(ConverterDetection.NotStarted);
        if (converter != null)
        {
            detection = converter.Current.GetBoundCopy();
            detection.BindValueChanged(e => showVoxNotes(e.NewValue), true);
            converter.EnsureDetected();
        }

        layerTilt.Current.BindValueChanged(e =>
        {
            if (session.Chart.Bg.Layer is { } l && l.RotateTilt != e.NewValue) editBg(b => b.Layer!.RotateTilt = e.NewValue, "Change layer");
        });
        layerSpin.Current.BindValueChanged(e =>
        {
            if (session.Chart.Bg.Layer is { } l && l.RotateSpin != e.NewValue) editBg(b => b.Layer!.RotateSpin = e.NewValue, "Change layer");
        });
    }

    // ---- export view ----------------------------------------------------------

    /// <summary>The export matrix of the export view (its header names the detected ksh2vox version).</summary>
    public ExportMatrixPanel ExportMatrix { get; private set; } = null!;

    private VisibilityContainer exportView() => new VisibilityContainer
    {
        RelativeSizeAxes = Axes.Both,
        Child = new BasicScrollContainer
        {
            RelativeSizeAxes = Axes.Both,
            ScrollbarVisible = false,
            Child = ExportMatrix = new ExportMatrixPanel(null, PANEL_WIDTH - 24),
        },
    };

    /// <summary>A lane area that takes no input while hidden (camera and effect lanes share the space).</summary>
    private partial class LaneArea : Container
    {
        public override bool PropagatePositionalInputSubTree => Alpha > 0 && base.PropagatePositionalInputSubTree;
    }

    private partial class VisibilityContainer : Container
    {
        public Visibility State { get; set; } = Visibility.Visible;

        // a hidden view must not take clicks
        public override bool PropagatePositionalInputSubTree => State == Visibility.Visible && base.PropagatePositionalInputSubTree;
        public override bool PropagateNonPositionalInputSubTree => State == Visibility.Visible && base.PropagateNonPositionalInputSubTree;
    }
}
