using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit.Components;
using static SDVXEditor.Game.Screens.Edit.Components.PanelControls;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>
/// The effects view of the camera tab: brushes and presets for the effect lanes, the inspector of the selected row,
/// the game background (bg_no) and #BPM OPTION. Every edit is one undo step (<see cref="CameraCommands.SetVoxExtras"/>).
/// docs/VOX_EFFECTS.md has the formats.
/// </summary>
public partial class CameraTabContent
{
    /// <summary>View index of the effects view (0 camera, 1 background, 2 export stay as they were).</summary>
    public const int EFFECTS_VIEW = 3;

    public EffectLaneEditor FxEditor { get; private set; } = null!;
    public EffectLanes FxLanes { get; private set; } = null!;

    private SpriteText fxTitle = null!;
    private SpriteText fxPreview = null!;
    private TextFlowContainer bgWarning = null!;
    private FillFlowContainer fxPostInspector = null!;
    private FillFlowContainer fxCommandInspector = null!;
    private FillFlowContainer fxCommandParams = null!;
    private OsuDropdown<string> fxBrushEffect = null!;
    private OsuDropdown<string> fxBrushParam = null!;
    private OsuDropdown<string> fxPreset = null!;
    private OsuDropdown<string> fxAssetKind = null!;
    private OsuDropdown<int> fxBgNo = null!;
    private OsuDropdown<string> fxRowParam = null!;
    private PinkCheckbox fxClearColour = null!;
    private PinkCheckbox fxConstantScroll = null!;
    private FlatButton[] fxModeButtons = null!;
    private FlatButton[] fxStageButtons = null!;
    private FlatButton[] fxPresetStageButtons = null!;
    private readonly BindableInt presetStage = new BindableInt(1);
    private bool fxRefreshing;
    private OsuDropdown<string> fxScriptTemplate = null!;
    private FillFlowContainer fxScriptList = null!;
    private int fxScriptVersion = -1;

    /// <summary>Script templates (shapes of the official scripts 1751 / 2330).</summary>
    public static readonly (string Name, string[] Lines)[] ScriptTemplates =
    {
        ("slide in from the right", new[] { "$offsetX = $offsetX + 0.178 * ( $targetStep - $currentStep - 48 ) / 48 when ( $targetStep - $currentStep ) > 48" }),
        ("slide in from the left", new[] { "$offsetX = $offsetX - 0.178 * ( $targetStep - $currentStep - 48 ) / 48 when ( $targetStep - $currentStep ) > 48" }),
        ("appear 2 beats before", new[] { "$targetStep = $currentStep + 48000 when ( $targetStep - $currentStep ) > 96" }),
        ("half scroll speed", new[] { "$targetStep = ( $targetStep - $currentStep ) * 0.5f + $currentStep" }),
        ("double scroll speed", new[] { "$targetStep = ( $targetStep - $currentStep ) * 2.0f + $currentStep" }),
        ("empty", Array.Empty<string>()),
    };

    /// <summary>Adds a script from a template; returns its id.</summary>
    public int AddScript(string template)
    {
        var t = ScriptTemplates.FirstOrDefault(x => x.Name == template);
        int id = VoxEffectEditing.NextScriptId(session.Chart.VoxExtras);
        FxEditor.Edit(v => v.Scripts.Add(new VoxScript(id, t.Lines ?? Array.Empty<string>(), t.Name ?? "")), "Add note script");
        dirty = true;
        return id;
    }

    /// <summary>Assigns script <paramref name="id"/> to every selected BT / FX note (VOX TRACK 3..6 / 2, 7); returns how many.</summary>
    public int AssignScriptToSelection(int id)
    {
        if (session.Chart.VoxExtras.Scripts.All(s => s.Id != id))
        {
            Message?.Invoke($"no script {id}");
            return 0;
        }

        var targets = session.Selection.Notes.Select(n => (Y: n.Note.Y, Track: n.Kind == NoteKind.Bt ? 3 + n.Lane : n.Lane == 0 ? 2 : 7)).Distinct().ToList();
        if (targets.Count == 0)
        {
            Message?.Invoke("select BT / FX notes in compose first");
            return 0;
        }

        FxEditor.Edit(v =>
        {
            foreach (var (y, track) in targets)
            {
                var existing = v.Scripted.FirstOrDefault(s => s.Y == y && s.Track == track);
                if (existing == null) v.Scripted.Add(new ScriptedNote(y, track, new[] { id }));
                else if (!existing.Ids.Contains(id)) existing.Ids.Add(id);
            }
            v.Sort();
        }, "Assign note script");
        Message?.Invoke($"script {id} on {targets.Count} note(s)");
        return targets.Count;
    }

    private void refreshScripts()
    {
        if (fxScriptVersion == session.Version) return;
        fxScriptVersion = session.Version;
        foreach (string k in fields.Keys.Where(k => k.StartsWith("script_", StringComparison.Ordinal)).ToList()) fields.Remove(k);
        fxScriptList.Clear();
        foreach (var script in session.Chart.VoxExtras.Scripts)
        {
            int id = script.Id;
            string text = string.Join(" ; ", script.Lines);
            var errors = script.Lines.Select(VoxScriptSyntax.Check).Where(e => e != null).ToList();
            int uses = session.Chart.VoxExtras.Scripted.Count(s => s.Ids.Contains(id));
            fxScriptList.Add(Row(
                new SpriteText { Text = $"#{id} ({uses}x)", Font = EditorFont.Bold(14), Colour = Colour4.White, Width = 70 },
                field("script_name_" + id, "name", () => session.Chart.VoxExtras.Scripts.FirstOrDefault(x => x.Id == id)?.Name ?? "",
                    v => FxEditor.Edit(x => x.Scripts.First(y => y.Id == id).Name = v.Trim(), "Rename note script"), 200, 44),
                Button("x", () => FxEditor.Edit(x => { x.Scripts.RemoveAll(y => y.Id == id); foreach (var sn in x.Scripted) sn.Ids.Remove(id); x.Scripted.RemoveAll(sn => sn.Ids.Count == 0); }, "Delete note script"), 26, EditorColours.Osu.MenuRed)));
            fxScriptList.Add(field("script_lines_" + id, "lines", () => string.Join(" ; ", session.Chart.VoxExtras.Scripts.FirstOrDefault(x => x.Id == id)?.Lines ?? new List<string>()),
                v => FxEditor.Edit(x => x.Scripts.First(y => y.Id == id).Lines = v.Split(';').Select(l => l.Trim()).Where(l => l.Length > 0).ToList(), "Edit note script"),
                PANEL_WIDTH - 30, 44));
            if (errors.Count > 0)
                fxScriptList.Add(new SpriteText { Text = "  " + errors[0] + " - ksh2vox will drop this script", Font = EditorFont.Regular(13), Colour = EditorColours.Nabla.Danger });
        }
    }
    private int fxVersion = -1;
    private VoxRowRef? fxShownRow;

    private void createEffectEditor()
    {
        FxEditor = new EffectLaneEditor(session) { Message = m => Message?.Invoke(m) };
        FxEditor.Divisor.BindTo(compose.Divisor);
        FxEditor.GridSnap.BindTo(compose.GridSnap);
        FxEditor.SelectionChanged += () => dirty = true;
    }

    private Container effectLaneHeaders()
    {
        var headers = new Container { Width = EffectLaneEditor.TotalWidth, Height = 46 };
        foreach (var lane in EffectLaneEditor.Lanes)
        {
            headers.Add(new Container
            {
                X = EffectLaneEditor.LaneLeft(lane),
                Width = EffectLaneEditor.WidthOf(lane),
                RelativeSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new osu.Framework.Graphics.Shapes.Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("202024").Opacity(0.92f) },
                    new SpriteText
                    {
                        Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre, Y = 3, Text = EffectLaneEditor.LaneName(lane),
                        Font = EditorFont.Bold(lane <= FxLane.PostLane ? 14 : 12), Colour = Colour4.White,
                    },
                    new ExportBadge(EffectLaneEditor.BadgeOf(lane), 8) { Anchor = Anchor.BottomCentre, Origin = Anchor.BottomCentre, Y = -4 },
                },
            });
        }

        return headers;
    }

    /// <summary>Adds preset <paramref name="name"/> at the cursor over <paramref name="beats"/> beats on <paramref name="stage"/>.</summary>
    public void AddPresetAtCursor(string name, double beats, int stage)
    {
        var preset = PostEffectPreset.All.FirstOrDefault(p => p.Name == name);
        if (preset == null) return;
        long span = Math.Max(1, (long)Math.Round(beats * Timebase.PulsesPerQuarter));
        long y = cursorPulse;
        FxEditor.Edit(v => VoxEffectEditing.AddPostEffects(v, preset.At(y, span, stage)), "Add preset " + preset.Name);
        var vox = session.Chart.VoxExtras;
        int i = vox.PostEffects.FindIndex(r => r.Y == y && r.Stage == stage);
        FxEditor.Select(i >= 0 ? new VoxRowRef(VoxRowKind.PostEffect, i) : null);
        dirty = true;
    }

    private static string bgLabel(int n) => n < 0 ? "converter default (0)" : GameBackgrounds.Find(n)?.Label ?? $"bg {n}";

    private VisibilityContainer effectsView()
    {
        var effectNames = PostEffects.Names.ToList();
        fxBrushEffect = new OsuDropdown<string>(effectNames) { Width = 190, FontSize = 15, Height = 26 };
        fxBrushParam = new OsuDropdown<string>(new[] { "Strength" })
        {
            Width = 170, FontSize = 15, Height = 26,
            ItemSource = () => paramsOf(fxBrushEffect.Current.Value),
        };
        fxPreset = new OsuDropdown<string>(PostEffectPreset.All.Select(p => p.Name)) { Width = 190, FontSize = 15, Height = 26 };
        var assetKeys = VoxCommands.All.Where(c => c.IsSpecial && c.Kind is not ("LANECOL" or "LANECLEARCOL")).Select(c => c.Key).ToList();
        fxAssetKind = new OsuDropdown<string>(assetKeys) { Width = 240, FontSize = 15, Height = 26, Formatter = k => VoxCommands.Find(k)?.Label ?? k };
        var bgIds = new[] { -1 }.Concat(GameBackgrounds.All.Select(b => b.Id)).ToList();
        fxBgNo = new OsuDropdown<int>(bgIds) { Width = 420, FontSize = 15, Height = 26, Formatter = bgLabel };
        fxRowParam = new OsuDropdown<string>(new[] { "Strength" })
        {
            Width = 170, FontSize = 15, Height = 26,
            ItemSource = () => FxEditor.Selected is { Kind: VoxRowKind.PostEffect } r && r.Index < session.Chart.VoxExtras.PostEffects.Count
                ? paramsOf(session.Chart.VoxExtras.PostEffects[r.Index].Name)
                : new[] { "Strength" },
        };
        fxModeButtons = PostEffects.ColorModeNames.Select((n, i) => Button(n, () =>
        {
            FxEditor.ColorMode.Value = i;
            dirty = true;
        }, i == 3 ? 150 : 100)).ToArray();
        fxStageButtons = new[]
        {
            Button("background (0)", () => FxEditor.EditSelected((v, r) => v.PostEffects[r.Index].Stage = 0, "Change post-effect stage"), 130),
            Button("lane (1)", () => FxEditor.EditSelected((v, r) => v.PostEffects[r.Index].Stage = 1, "Change post-effect stage"), 90),
        };
        fxPresetStageButtons = new[]
        {
            Button("background", () => { presetStage.Value = 0; dirty = true; }, 110),
            Button("lane", () => { presetStage.Value = 1; dirty = true; }, 70),
        };

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
                    Spacing = new Vector2(0, 7),
                    Children = new Drawable[]
                    {
                        Header("game background (music_db bg_no)", "bg_no"),
                        Row(fxBgNo),
                        bgWarning = new TextFlowContainer(t =>
                        {
                            t.Font = EditorFont.Regular(13);
                            t.Colour = EditorColours.Nabla.Danger;
                        }) { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y },
                        Header("selected row"),
                        fxTitle = new SpriteText { Font = EditorFont.Regular(15), Colour = EditorColours.Text, Truncate = true, Width = PANEL_WIDTH - 30 },
                        fxPreview = new SpriteText { Font = EditorFont.Regular(13), Colour = EditorColours.TextMuted },
                        fxPostInspector = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 5),
                            Children = new Drawable[]
                            {
                                Row(fxRowParam,
                                    field("fx_from", "from", () => selPost()?.From is { } a ? Num(a) : "", s => { if (TryNum(s, out double d)) FxEditor.EditSelected((v, r) => v.PostEffects[r.Index].From = d, "Edit post effect"); }, 140, 40),
                                    field("fx_to", "to", () => selPost()?.To is { } b ? Num(b) : "", s => { if (TryNum(s, out double d)) FxEditor.EditSelected((v, r) => v.PostEffects[r.Index].To = d, "Edit post effect"); }, 130, 30),
                                    field("fx_index", "inst.", () => selPost()?.Index.ToString(Inv) ?? "", s => { if (TryNum(s, out double d)) FxEditor.EditSelected((v, r) => v.PostEffects[r.Index].Index = Math.Clamp((int)d, 0, 15), "Change post-effect instance"); }, 100, 40)),
                                Row(field("fx_len", "beats", () => selPost() is { LengthMs: null } p ? Num(p.Length / (double)Timebase.PulsesPerQuarter) : "",
                                        s => { if (TryNum(s, out double d) && d >= 0) FxEditor.EditSelected((v, r) => { v.PostEffects[r.Index].Length = (long)Math.Round(d * Timebase.PulsesPerQuarter); v.PostEffects[r.Index].LengthMs = null; }, "Change post-effect length"); }, 150, 50),
                                    field("fx_ms", "ms", () => selPost()?.LengthMs is { } ms ? Num(ms) : "",
                                        s => FxEditor.EditSelected((v, r) => v.PostEffects[r.Index].LengthMs = TryNum(s, out double d) && d >= 0 ? d : null, "Change post-effect length"), 120, 30),
                                    Button("instant", () => FxEditor.EditSelected((v, r) => { v.PostEffects[r.Index].Length = 0; v.PostEffects[r.Index].LengthMs = null; }, "Make post effect instant"), 70)),
                                Row(new SpriteText { Text = "stage", Font = EditorFont.Regular(14), Colour = EditorColours.Text, Width = 50 }, fxStageButtons[0], fxStageButtons[1]),
                            },
                        },
                        fxCommandInspector = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 5),
                            Children = new Drawable[]
                            {
                                fxCommandParams = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Full, Spacing = new Vector2(6, 4) },
                                Row(field("fx_cmd_len", "beats", () => selCommand() is { } c ? Num(c.Length / (double)Timebase.PulsesPerQuarter) : "",
                                        s => { if (TryNum(s, out double d) && d >= 0) FxEditor.EditSelected((v, r) => v.Commands[r.Index].Length = (long)Math.Round(d * Timebase.PulsesPerQuarter), "Change command length"); }, 150, 50),
                                    field("fx_type", "type", () => selCommand()?.Type.ToString(Inv) ?? "",
                                        s => { if (long.TryParse(s.Trim(), out long t) && t is >= 0 and <= 0xFFFFFFFFL) FxEditor.EditSelected((v, r) => v.Commands[r.Index].Type = t, "Change command type"); }, 160, 40)),
                            },
                        },
                        Row(Button("delete", () => { FxEditor.DeleteSelected(); dirty = true; }, 80, EditorColours.Osu.MenuRed)),
                        Header("post effects: click a column of a post lane (drag = length)", "post_effect"),
                        Row(new SpriteText { Text = "parameter", Font = EditorFont.Regular(14), Colour = EditorColours.Text, Width = 70 }, fxBrushEffect, fxBrushParam),
                        Row(fxModeButtons.Cast<Drawable>().ToArray()),
                        Row(fxPreset, field("preset_beats", "beats", null, _ => { }, 110, 44), fxPresetStageButtons[0], fxPresetStageButtons[1],
                            Button("add preset", () => AddPresetAtCursor(fxPreset.Current.Value, TryNum(Field("preset_beats").Box.Text, out double b) && b > 0 ? b : 4, presetStage.Value), 100)),
                        Header("lane colours / assets brushes", "special_n"),
                        Row(fxClearColour = new PinkCheckbox("fill (LANECLEARCOL)", 14),
                            field("colour_token", "colour", () => FxEditor.ColourToken.Value, s =>
                            {
                                string t = s.Trim();
                                if (!t.StartsWith('x') && !t.StartsWith('i')) t = "x" + t;
                                if (VoxToken.Argb(t) != null) FxEditor.ColourToken.Value = t.ToUpperInvariant().Replace("X", "x").Replace("I", "i");
                            }, 190, 50)),
                        Row(new SpriteText { Text = "asset", Font = EditorFont.Regular(14), Colour = EditorColours.Text, Width = 50 }, fxAssetKind),
                        Header("#BPM OPTION", "bpm_option"),
                        Row(field("rep_bpm", "hi-speed BPM", () => session.Chart.VoxExtras.RepresentativeBpm is { } rb ? Num(rb) : "",
                                s => FxEditor.Edit(v => v.RepresentativeBpm = TryNum(s, out double d) && d > 0 ? d : null, "Change representative BPM"), 220, 100),
                            fxConstantScroll = new PinkCheckbox("ConstantScroll", 14)),
                        Header("note scripts (#SCRIPT_DEFINE)", "note_scripts"),
                        Row(fxScriptTemplate = new OsuDropdown<string>(ScriptTemplates.Select(t => t.Name)) { Width = 240, FontSize = 15, Height = 26 },
                            Button("new script", () => AddScript(fxScriptTemplate.Current.Value), 100),
                            field("assign_id", "id", null, _ => { }, 90, 24),
                            Button("assign to selected notes", () => AssignScriptToSelection(TryNum(Field("assign_id").Box.Text, out double sid) ? (int)sid : session.Chart.VoxExtras.Scripts.LastOrDefault()?.Id ?? 0), 190)),
                        fxScriptList = new FillFlowContainer { RelativeSizeAxes = Axes.X, AutoSizeAxes = Axes.Y, Direction = FillDirection.Vertical, Spacing = new Vector2(0, 3) },
                        Note("A script runs for the notes it is assigned to (BT / FX in compose: select them, then assign). Statements one per ';': "
                             + "$targetStep / $offsetX / $offsetY = expression [when a op b]; $currentStep is the play position, steps are 1/48 beat, "
                             + "$offsetX 0.178 ≈ one BT lane. The playtest previews $targetStep and $offsetX.", 12),
                        Note("Lanes (left): post effects of the background stage and the lane stage, one column per effect (CA chromatic aberration, CC colour conversion, "
                             + "RB radial blur, SN noise, CRT, LUM glow, FL frame label, SH shake); LaneY / BIL_RotZ / Morphing2 tweens (click = from the value there to the "
                             + "value under the pointer, drag = length and end value; drag a handle to move it); colours (left half lane tint, right half fill); "
                             + "bar lines (click = BAROFF ON/OFF per measure, Shift+click = a BAR line); assets (the brush above); scripts (assignments; edit in the scripts panel). "
                             + "Right click deletes a row. Only effects the game actually reads are offered: LaneX, GEO_RotZ/RotX, Tilt_Mag, 3D_Scroll, Morphing0/1/3, "
                             + "LANEMODEL*, LANEDISP, HudY and AIRL/AIRR_ScaX are parsed by the game and then ignored, so they would be invisible.", 12),
                    },
                },
            },
        };
    }

    private static IReadOnlyList<string> paramsOf(string effect) =>
        PostEffects.Find(effect) is { } info
            ? (info.ParamIsLabel ? info.Labels! : info.Params.Select(p => p.Name).ToList())
            : new[] { "Strength" };

    private PostEffectRow? selPost() =>
        FxEditor.Selected is { Kind: VoxRowKind.PostEffect } r && r.Index < session.Chart.VoxExtras.PostEffects.Count ? session.Chart.VoxExtras.PostEffects[r.Index] : null;

    private VoxCommandRow? selCommand() =>
        FxEditor.Selected is { Kind: VoxRowKind.Command } r && r.Index < session.Chart.VoxExtras.Commands.Count ? session.Chart.VoxExtras.Commands[r.Index] : null;

    private void bindEffectsView()
    {
        fxBrushEffect.Current.BindValueChanged(e =>
        {
            FxEditor.PostEffect.Value = e.NewValue;
            if (!paramsOf(e.NewValue).Contains(fxBrushParam.Current.Value))
                fxBrushParam.Current.Value = EffectLaneEditor.MainParam(PostEffects.Find(e.NewValue)!);
        });
        FxEditor.PostEffect.BindValueChanged(e => fxBrushEffect.Current.Value = e.NewValue);
        fxBrushParam.Current.BindValueChanged(e => FxEditor.PostParam.Value = e.NewValue);
        fxAssetKind.Current.Value = FxEditor.AssetKey.Value;
        fxAssetKind.Current.BindValueChanged(e => FxEditor.AssetKey.Value = e.NewValue);
        fxClearColour.Current.BindValueChanged(e => FxEditor.ClearColour.Value = e.NewValue);
        fxBgNo.Current.BindValueChanged(e =>
        {
            if (fxRefreshing) return;
            int? bg = e.NewValue < 0 ? null : e.NewValue;
            if (bg != session.Chart.VoxExtras.BgNo) FxEditor.Edit(v => v.BgNo = bg, "Change bg_no");
        });
        fxConstantScroll.Current.BindValueChanged(e =>
        {
            if (fxRefreshing) return;
            bool? cs = e.NewValue ? true : null;
            if (cs != session.Chart.VoxExtras.ConstantScroll) FxEditor.Edit(v => v.ConstantScroll = cs, "Change ConstantScroll");
        });
        fxRowParam.Current.BindValueChanged(e =>
        {
            if (fxRefreshing || selPost() is not { } p || p.Param == e.NewValue) return;
            FxEditor.EditSelected((v, r) => v.PostEffects[r.Index].Param = e.NewValue, "Change post-effect parameter");
        });
    }

    private void refreshEffects()
    {
        refreshScripts();
        fxRefreshing = true;
        try
        {
            var vox = session.Chart.VoxExtras;
            fxBgNo.Current.Value = vox.BgNo ?? -1;
            fxConstantScroll.Current.Value = vox.ConstantScroll == true;
            bool hasPost = vox.PostEffects.Count > 0;
            bgWarning.Text = hasPost && !GameBackgrounds.DefinesPostEffects(vox.BgNo)
                ? $"Post effects with {(vox.BgNo is { } b ? "bg_no " + b : "the default bg_no 0")} may not show in game: effect stages are defined per background, "
                  + "and every official post-effect song uses its own (bg_no 92-118, one on 5). Not play-tested yet."
                : "";

            for (int i = 0; i < fxModeButtons.Length; i++) fxModeButtons[i].Alpha = FxEditor.ColorMode.Value == i ? 1 : 0.5f;
            for (int i = 0; i < fxPresetStageButtons.Length; i++) fxPresetStageButtons[i].Alpha = presetStage.Value == i ? 1 : 0.5f;

            var post = selPost();
            var cmd = selCommand();
            if (post != null)
            {
                var info = PostEffects.Find(post.Name);
                fxTitle.Text = $"{post.Name}.{post.Param}  {Num(post.From)} → {Num(post.To)}  stage {post.Stage} ({PostEffects.StageName(post.Stage)}), instance {post.Index} at {positionText(post.Y)}";
                fxPreview.Text = previewText(info?.Preview ?? PreviewSupport.GameOnly)
                                 + (info?.Param(post.Param) is { } prm ? "  -  " + prm.Help : info?.ParamIsLabel == true ? "  -  label of the background's clip" : "");
                fxRowParam.Current.Value = post.Param;
                for (int i = 0; i < fxStageButtons.Length; i++) fxStageButtons[i].Alpha = Math.Min(post.Stage, 1) == i ? 1 : 0.5f;
            }
            else if (cmd != null)
            {
                var info = VoxCommands.Of(cmd);
                fxTitle.Text = $"{info?.Label ?? cmd.Key}: {cmd.Name} {cmd.Type} {cmd.P1} {cmd.P2} {cmd.P3} {cmd.P4} at {positionText(cmd.Y)}";
                fxPreview.Text = previewText(info?.Preview ?? PreviewSupport.GameOnly) + (info?.Experimental == true ? "  -  experimental" : "")
                                 + (info != null ? "  -  " + info.Description : "");
            }
            else if (FxEditor.SelectedScripted is { } si && si < vox.Scripted.Count)
            {
                var s = vox.Scripted[si];
                fxTitle.Text = $"note on TRACK{s.Track} at {positionText(s.Y)} runs script {string.Join(", ", s.Ids)}";
                fxPreview.Text = "edit scripts in the scripts panel below";
            }
            else
            {
                fxTitle.Text = "nothing selected - click a row in the effect lanes";
                fxPreview.Text = "";
            }

            fxPostInspector.Alpha = post != null ? 1 : 0;
            fxPostInspector.AutoSizeAxes = post != null ? Axes.Y : Axes.None;
            if (post == null) fxPostInspector.Height = 0;
            fxCommandInspector.Alpha = cmd != null ? 1 : 0;
            fxCommandInspector.AutoSizeAxes = cmd != null ? Axes.Y : Axes.None;
            if (cmd == null) fxCommandInspector.Height = 0;

            // command value slots: rebuilt when another row is shown
            if (fxShownRow != FxEditor.Selected || fxVersion != session.Version)
            {
                fxShownRow = FxEditor.Selected;
                fxVersion = session.Version;
                // the slot fields of the previous row go with their drawables (refreshInspector refreshes every field)
                foreach (string k in fields.Keys.Where(k => k.StartsWith("fx_p", StringComparison.Ordinal)).ToList()) fields.Remove(k);
                fxCommandParams.Clear();
                if (cmd != null && VoxCommands.Of(cmd) is { } ci)
                {
                    for (int k = 0; k < ci.Params.Count; k++)
                    {
                        var prm = ci.Params[k];
                        if (prm.Kind == VoxValueKind.Unused) continue;
                        int slot = ci.SlotOf(k);
                        string key = "fx_p" + (slot + 1);
                        fxCommandParams.Add(field(key, prm.Label, () => selCommand() is { } c ? c[slot] : "", s => setSlot(slot, s, prm.Kind), 200, 70));
                        if (prm.Choices is { Count: > 0 } choices)
                        {
                            var dd = new OsuDropdown<string>(choices) { Width = 230, FontSize = 14, Height = 26 };
                            dd.Current.Value = choices.Contains(cmd[slot]) ? cmd[slot] : choices[0];
                            dd.Current.BindValueChanged(e =>
                            {
                                if (!fxRefreshing && selCommand() is { } c && c[slot] != e.NewValue) setSlot(slot, e.NewValue, prm.Kind);
                            });
                            fxCommandParams.Add(dd);
                        }
                    }
                }
            }
        }
        finally
        {
            fxRefreshing = false;
        }
    }

    /// <summary>Writes a value slot of the selected command; plain numbers get the slot's prefix (d, i, x).</summary>
    private void setSlot(int slot, string s, VoxValueKind kind)
    {
        string t = s.Trim();
        if (t.Length == 0) return;
        if (TryNum(t, out double num) && kind is VoxValueKind.Degrees or VoxValueKind.Int or VoxValueKind.Hex) t = VoxToken.Format(num, kind);
        if (!VoxToken.IsValid(t))
        {
            Message?.Invoke($"'{s}' is not one VOX field (letters, digits, _ . + - only)");
            return;
        }

        FxEditor.EditSelected((v, r) => v.Commands[r.Index][slot] = t, "Edit command value");
    }

    private static string previewText(PreviewSupport s) => s switch
    {
        PreviewSupport.Exact => "preview: exact (playtest + camera preview)",
        PreviewSupport.Approximate => "preview: approximate",
        _ => "game only - not previewed",
    };
}
