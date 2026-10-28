using osu.Framework.Bindables;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>The lanes of the effects view (VOX game effects, docs/VOX_EFFECTS.md).</summary>
public enum FxLane
{
    /// <summary>#POSTEFFECT rows of stage 0 (the background's effect stage).</summary>
    PostBackground,

    /// <summary>#POSTEFFECT rows of stage 1 and above ("Overall", the lane).</summary>
    PostLane,

    LaneY,
    BilRotZ,
    Morphing2,

    /// <summary>SpecialN LANECOL / LANECLEARCOL (instant).</summary>
    Colours,

    /// <summary>BAROFF ON/OFF ranges and BAR lines.</summary>
    Bars,

    /// <summary>The other SpecialN kinds (game assets by id).</summary>
    Assets,

    /// <summary>Script assignments on notes (markers; scripts are edited in the panel).</summary>
    Scripts,
}

public readonly record struct FxPointer(double Ms, FxLane Lane, double Fx);

/// <summary>Which end of a row a hit or drag is about.</summary>
public enum FxPart
{
    Start,
    End,
}

/// <summary>
/// Tools of the effect lanes, independent of drawables (the smoke test drives the same methods): click on empty space
/// adds a row of the lane's brush at the snapped time (tween lanes: from the value there to the value under the
/// pointer), drag right after the click sets its length (tween lanes: and its end value); a press on a row's start
/// moves it (tween lanes: and its start value), on its end changes its length (and end value); right click deletes.
/// Every gesture is one undo step (a transaction of <see cref="CameraCommands.SetVoxExtras"/>).
/// </summary>
public sealed class EffectLaneEditor
{
    public const float LANE_WIDTH = 60;
    public const float POST_WIDTH = 150;
    public const float GAP = 5;
    public const float HIT_PX = 8;

    public static readonly FxLane[] Lanes = Enum.GetValues<FxLane>();

    private readonly EditorSession session;

    public readonly BindableInt Divisor = new BindableInt(4);
    public readonly BindableBool GridSnap = new BindableBool(true);

    /// <summary>Post-effect brush: effect and parameter a click in a post lane adds.</summary>
    public readonly Bindable<string> PostEffect = new Bindable<string>(PostEffects.ColorConversion);

    public readonly Bindable<string> PostParam = new Bindable<string>("Strength");

    /// <summary>ColorConversion mode a new Strength row gets (0 gray, 1 sepia, 2 negative, 3 negative + complement, 4 hue).</summary>
    public readonly BindableInt ColorMode = new BindableInt(2) { MinValue = 0, MaxValue = 4 };

    /// <summary>The parameter a new row of <paramref name="info"/> drives when the brush names none of its own.</summary>
    public static string MainParam(PostEffectInfo info) => info.Name switch
    {
        PostEffects.CrtMonitorEffect => "Active",
        PostEffects.SimpleNoise => "WholeNoiseStrength",
        _ => info.ParamIsLabel ? info.Labels![0] : info.Params.FirstOrDefault(p => p.Name == "Strength")?.Name ?? info.Params[0].Name,
    };

    /// <summary>Colour brush: LANECOL (false) or LANECLEARCOL (true), and the xAARRGGBB token.</summary>
    public readonly BindableBool ClearColour = new BindableBool();

    public readonly Bindable<string> ColourToken = new Bindable<string>("xFFFFFFFF");

    /// <summary>Asset brush: the SpecialN catalogue key a click in the assets lane adds.</summary>
    public readonly Bindable<string> AssetKey = new Bindable<string>("SpecialN:LIVE2D");

    public float PxPerMs { get; set; } = PlayfieldLayout.PX_PER_MS;

    public Action<string>? Message { get; set; }

    public event Action? SelectionChanged;

    public VoxRowRef? Selected { get; private set; }

    /// <summary>The selected script assignment (scripts lane), index into <see cref="VoxExtrasInfo.Scripted"/>.</summary>
    public int? SelectedScripted { get; private set; }

    public FxPointer? Hover { get; private set; }
    public FxLane? DragLane { get; private set; }

    public EffectLaneEditor(EditorSession session) => this.session = session;

    private VoxExtrasInfo vox => session.Chart.VoxExtras;

    // ---- geometry ----------------------------------------------------------------

    public static float WidthOf(FxLane lane) => lane <= FxLane.PostLane ? POST_WIDTH : LANE_WIDTH;

    public static float LaneLeft(FxLane lane)
    {
        float x = 0;
        foreach (var l in Lanes)
        {
            if (l == lane) return x;
            x += WidthOf(l) + GAP;
        }

        return x;
    }

    public static float TotalWidth => LaneLeft(Lanes[^1]) + WidthOf(Lanes[^1]);

    public static string LaneName(FxLane lane) => lane switch
    {
        FxLane.PostBackground => "post · background",
        FxLane.PostLane => "post · lane",
        FxLane.LaneY => "LaneY",
        FxLane.BilRotZ => "BIL_RotZ",
        FxLane.Morphing2 => "Morph2",
        FxLane.Colours => "colours",
        FxLane.Bars => "bar lines",
        FxLane.Assets => "assets",
        _ => "scripts",
    };

    public static string BadgeOf(FxLane lane) => lane switch
    {
        FxLane.PostBackground or FxLane.PostLane => "post_effect",
        FxLane.LaneY => "lane_y",
        FxLane.BilRotZ => "bil_rotz",
        FxLane.Morphing2 => "morphing2",
        FxLane.Colours => "lane_colour",
        FxLane.Bars => "bar_lines",
        FxLane.Assets => "special_n",
        _ => "note_scripts",
    };

    public static string? TweenName(FxLane lane) => lane switch
    {
        FxLane.LaneY => "LaneY",
        FxLane.BilRotZ => "BIL_RotZ",
        FxLane.Morphing2 => "Morphing2",
        _ => null,
    };

    /// <summary>View range of a tween lane (values outside are drawn at the edge).</summary>
    public static (double Min, double Max) Range(FxLane lane) => lane switch
    {
        FxLane.LaneY => (-1024, 1024),
        FxLane.BilRotZ => (-180, 180),
        _ => (-3, 3),
    };

    public static double ValueOf(FxLane lane, double fx)
    {
        var (min, max) = Range(lane);
        double v = min + Math.Clamp(fx, 0, 1) * (max - min);
        return lane switch
        {
            FxLane.LaneY => Math.Round(v / 64) * 64,
            FxLane.BilRotZ => Math.Round(v / 5) * 5,
            _ => Math.Round(v * 10) / 10,
        };
    }

    public static double FxOf(FxLane lane, double v)
    {
        var (min, max) = Range(lane);
        return Math.Clamp((v - min) / (max - min), 0, 1);
    }

    /// <summary>Column of a post effect inside a post lane (its game id), 0..7.</summary>
    public static int PostColumn(string effect) => PostEffects.Find(effect)?.Id ?? 0;

    public long PulseAt(double ms, bool snap = true)
    {
        long p = (long)Math.Round(session.MsToPulse(ms));
        if (snap && GridSnap.Value) p = session.Timing.Snap(p, Divisor.Value);
        return Math.Max(0, p);
    }

    private long beat => session.Timing.BeatLength(session.Timing.MeasureAt(0));
    private double ms(long pulse) => session.PulseToMs(pulse);
    private double tolMs => HIT_PX / Math.Max(0.001f, PxPerMs);

    // ---- rows per lane --------------------------------------------------------------

    /// <summary>The rows drawn in <paramref name="lane"/>.</summary>
    public IEnumerable<VoxRowRef> RowsIn(FxLane lane)
    {
        switch (lane)
        {
            case FxLane.PostBackground:
            case FxLane.PostLane:
                for (int i = 0; i < vox.PostEffects.Count; i++)
                    if ((vox.PostEffects[i].Stage == 0) == (lane == FxLane.PostBackground))
                        yield return new VoxRowRef(VoxRowKind.PostEffect, i);
                break;
            case FxLane.Scripts:
                break;
            default:
                for (int i = 0; i < vox.Commands.Count; i++)
                    if (LaneOf(vox.Commands[i]) == lane)
                        yield return new VoxRowRef(VoxRowKind.Command, i);
                break;
        }
    }

    /// <summary>The lane a command row is drawn in.</summary>
    public static FxLane LaneOf(VoxCommandRow r) => r.Name switch
    {
        "LaneY" => FxLane.LaneY,
        "BIL_RotZ" => FxLane.BilRotZ,
        "Morphing2" => FxLane.Morphing2,
        "BAR" or "BAROFF" => FxLane.Bars,
        "SpecialN" when r.P1 is "LANECOL" or "LANECLEARCOL" => FxLane.Colours,
        _ => FxLane.Assets,
    };

    public static FxLane LaneOf(PostEffectRow r) => r.Stage == 0 ? FxLane.PostBackground : FxLane.PostLane;

    /// <summary>The row (and which end) under the pointer.</summary>
    public (VoxRowRef Row, FxPart Part)? HitAt(FxPointer p)
    {
        (VoxRowRef, FxPart)? best = null;
        double bestD = double.MaxValue;
        foreach (var r in RowsIn(p.Lane))
        {
            long y = VoxEffectEditing.Y(vox, r), len = VoxEffectEditing.Length(vox, r);
            double startMs = ms(y), endMs = r.Kind == VoxRowKind.PostEffect && vox.PostEffects[r.Index].LengthMs is { } lm ? startMs + lm : ms(y + len);
            if (p.Lane <= FxLane.PostLane)
            {
                // a post row is hit in its own column only
                int col = PostColumn(vox.PostEffects[r.Index].Name);
                if ((int)Math.Floor(p.Fx * 8) != col) continue;
            }
            else if (TweenName(p.Lane) != null)
            {
                // tween handles sit at (start, from) and (end, to)
                var row = vox.Commands[r.Index];
                double fs = FxOf(p.Lane, VoxToken.Number(row.P1) ?? 0), fe = FxOf(p.Lane, VoxToken.Number(row.P2) ?? 0);
                double ds = Math.Abs(p.Ms - startMs) * PxPerMs + Math.Abs(p.Fx - fs) * LANE_WIDTH;
                double de = Math.Abs(p.Ms - endMs) * PxPerMs + Math.Abs(p.Fx - fe) * LANE_WIDTH;
                if (len > 0 && de <= HIT_PX * 1.6 && de < bestD) { best = (r, FxPart.End); bestD = de; }
                if (ds <= HIT_PX * 1.6 && ds < bestD) { best = (r, FxPart.Start); bestD = ds; }
                continue;
            }

            double dStart = Math.Abs(p.Ms - startMs);
            double dEnd = Math.Abs(p.Ms - endMs);
            if (endMs - startMs > 2 * tolMs && dEnd <= tolMs && dEnd < bestD)
            {
                best = (r, FxPart.End);
                bestD = dEnd;
            }
            else if (dStart <= tolMs && dStart < bestD)
            {
                best = (r, FxPart.Start);
                bestD = dStart;
            }
            else if (p.Ms > startMs && p.Ms < endMs && bestD == double.MaxValue)
            {
                best = (r, FxPart.Start);
                bestD = tolMs;
            }
        }

        return best;
    }

    /// <summary>The script assignment under the pointer in the scripts lane.</summary>
    public int? ScriptedAt(FxPointer p)
    {
        for (int i = 0; i < vox.Scripted.Count; i++)
            if (Math.Abs(ms(vox.Scripted[i].Y) - p.Ms) <= tolMs) return i;
        return null;
    }

    // ---- input ----------------------------------------------------------------------

    private enum Drag
    {
        None,
        Move,
        Resize,
    }

    private Drag drag;
    private VoxExtrasInfo? original;
    private VoxRowRef dragRow;
    private FxLane dragLane;
    private double grabOffsetMs;
    private long originY;
    private string dragName = "";
    private bool dragMoved;

    public void PointerMove(FxPointer p) => Hover = p;

    public void PointerLeave() => Hover = null;

    /// <returns>true when a drag started.</returns>
    public bool PointerDown(FxPointer p, PointerButton button, bool shift)
    {
        Hover = p;
        if (p.Lane == FxLane.Scripts)
        {
            SelectedScripted = ScriptedAt(p);
            Selected = null;
            if (button == PointerButton.Right && SelectedScripted is { } si)
            {
                edit(v => v.Scripted.RemoveAt(si), "Remove note script");
                SelectedScripted = null;
            }
            SelectionChanged?.Invoke();
            return false;
        }

        SelectedScripted = null;
        var hit = HitAt(p);
        if (button == PointerButton.Right)
        {
            if (hit is { } h)
            {
                edit(v => VoxEffectEditing.Delete(v, h.Row), "Delete effect row");
                Selected = null;
                SelectionChanged?.Invoke();
            }
            return false;
        }

        if (hit is { } got)
        {
            Selected = got.Row;
            SelectionChanged?.Invoke();
            begin(got.Part == FxPart.End ? Drag.Resize : Drag.Move, got.Row, p, got.Part == FxPart.End ? "Change effect length" : "Move effect row");
            return true;
        }

        // empty space: add the lane's brush
        long y = PulseAt(p.Ms);
        var v0 = vox.Clone();
        VoxRowRef? added = null;
        string name = "Add effect";
        switch (p.Lane)
        {
            case FxLane.PostBackground:
            case FxLane.PostLane:
            {
                // the column under the pointer is the effect (game id order); the brush parameter if it has one
                int stage = p.Lane == FxLane.PostBackground ? 0 : 1;
                var info = PostEffects.All[Math.Clamp((int)Math.Floor(p.Fx * 8), 0, 7)];
                PostEffect.Value = info.Name;
                string param = info.ParamIsLabel ? info.Labels![0]
                    : info.Param(PostParam.Value) is { } pp ? pp.Name
                    : MainParam(info);
                var prm = info.Param(param);
                double to = prm == null ? 0 : info.Name == PostEffects.ColorConversion && param == "Strength" ? (ColorMode.Value == 4 ? 180 : 100) : prm.Max;
                var rows = new List<PostEffectRow>();
                // ColorConversion: a Strength row needs a Mode (0 = grayscale is the game's default); add the brush mode
                if (info.Name == PostEffects.ColorConversion && param == "Strength")
                    rows.Add(new PostEffectRow(y, info.Name, "Mode", ColorMode.Value, ColorMode.Value, 0, stage));
                var row = new PostEffectRow(y, info.Name, param, prm?.Default ?? 0, to, shift ? 0 : beat, stage);
                rows.Add(row);
                VoxEffectEditing.AddPostEffects(v0, rows);
                added = new VoxRowRef(VoxRowKind.PostEffect, v0.PostEffects.IndexOf(row));
                name = "Add post effect";
                break;
            }
            case FxLane.LaneY:
            case FxLane.BilRotZ:
            case FxLane.Morphing2:
            {
                string cmd = TweenName(p.Lane)!;
                var eval = new VoxEffectEvaluator(session.Chart, x => session.PulseToMs(x));
                double from = eval.Command(cmd, ms(y));
                int i = VoxEffectEditing.AddCommand(v0, VoxEffectEditing.Tween(cmd, y, shift ? 0 : beat, from, ValueOf(p.Lane, p.Fx)));
                added = new VoxRowRef(VoxRowKind.Command, i);
                name = "Add " + cmd;
                break;
            }
            case FxLane.Colours:
            {
                var info = VoxCommands.Find(ClearColour.Value ? "SpecialN:LANECLEARCOL" : "SpecialN:LANECOL")!;
                var row = info.Create(y);
                if (VoxToken.IsValid(ColourToken.Value)) row.P2 = ColourToken.Value;
                added = new VoxRowRef(VoxRowKind.Command, VoxEffectEditing.AddCommand(v0, row));
                name = "Add lane colour";
                break;
            }
            case FxLane.Bars:
            {
                // BAROFF works per measure: snap to the measure start; alternate ON / OFF, Shift = a BAR line
                long m = session.Timing.MeasureStart(session.Timing.MeasureAt(y));
                VoxCommandRow row;
                if (shift) row = new VoxCommandRow(y, "BAR", 0, 0);
                else
                {
                    var last = v0.Commands.LastOrDefault(c => c.Name == "BAROFF" && c.Y <= m);
                    row = new VoxCommandRow(m, "BAROFF", 0, 0, last?.P1 == "ON" ? "OFF" : "ON");
                }
                added = new VoxRowRef(VoxRowKind.Command, VoxEffectEditing.AddCommand(v0, row));
                name = "Add bar line command";
                break;
            }
            case FxLane.Assets:
            {
                var info = VoxCommands.Find(AssetKey.Value) ?? VoxCommands.Find("SpecialN:LIVE2D")!;
                added = new VoxRowRef(VoxRowKind.Command, VoxEffectEditing.AddCommand(v0, info.Create(y, info.Tween ? beat : 0)));
                name = "Add " + info.Label;
                break;
            }
        }

        if (added is not { } a) return false;
        session.Commands.BeginTransaction(name);
        session.Commands.Execute(CameraCommands.SetVoxExtras(v0, name));
        Selected = a;
        SelectionChanged?.Invoke();
        // a drag right after adding sets the length (instant rows and bar commands stay as they are)
        bool resizable = p.Lane is FxLane.PostBackground or FxLane.PostLane or FxLane.LaneY or FxLane.BilRotZ or FxLane.Morphing2 && !shift;
        if (!resizable)
        {
            session.Commands.Commit();
            return false;
        }

        drag = Drag.Resize;
        original = v0.Clone();
        dragRow = a;
        dragLane = p.Lane;
        DragLane = p.Lane;
        originY = y;
        grabOffsetMs = 0;
        dragName = name;
        dragMoved = false;
        return true;
    }

    private void begin(Drag mode, VoxRowRef row, FxPointer p, string name)
    {
        drag = mode;
        original = vox.Clone();
        dragRow = row;
        dragLane = p.Lane;
        DragLane = p.Lane;
        originY = VoxEffectEditing.Y(original, row);
        grabOffsetMs = mode == Drag.Move ? p.Ms - ms(originY) : 0;
        dragName = name;
        dragMoved = false;
        session.Commands.BeginTransaction(name);
    }

    public void PointerDrag(FxPointer p)
    {
        Hover = p;
        if (drag == Drag.None || original == null) return;
        var v = original.Clone();
        var row = dragRow;
        string? tween = TweenName(dragLane);
        if (drag == Drag.Move)
        {
            long y = PulseAt(p.Ms - grabOffsetMs);
            if (tween != null) v.Commands[row.Index].P1 = VoxToken.Format(ValueOf(dragLane, p.Fx), VoxCommands.Find(tween)!.Params[0].Kind);
            if (dragLane == FxLane.Bars && v.Commands[row.Index].Name == "BAROFF")
                y = session.Timing.MeasureStart(session.Timing.MeasureAt(y));
            row = row with { Index = VoxEffectEditing.Move(v, row, y) };
            dragMoved |= y != originY;
        }
        else
        {
            long end = PulseAt(p.Ms);
            long len = Math.Max(0, end - originY);
            if (row.Kind == VoxRowKind.PostEffect && v.PostEffects[row.Index].LengthMs != null)
                v.PostEffects[row.Index].LengthMs = Math.Max(0, Math.Round(p.Ms - ms(originY)));
            else
                VoxEffectEditing.SetLength(v, row, len);
            if (tween != null) v.Commands[row.Index].P2 = VoxToken.Format(ValueOf(dragLane, p.Fx), VoxCommands.Find(tween)!.Params[0].Kind);
            dragMoved = true;
        }

        session.Commands.Rollback();
        session.Commands.BeginTransaction(dragName);
        session.Commands.Execute(CameraCommands.SetVoxExtras(v, dragName));
        Selected = row;
    }

    public void PointerUp()
    {
        if (drag == Drag.None) return;
        drag = Drag.None;
        DragLane = null;
        original = null;
        session.Commands.Commit();
        SelectionChanged?.Invoke();
    }

    // ---- inspector edits ---------------------------------------------------------------

    /// <summary>Applies <paramref name="change"/> to a copy of the extras as one undo step.</summary>
    public void Edit(Action<VoxExtrasInfo> change, string description) => edit(change, description);

    private void edit(Action<VoxExtrasInfo> change, string description)
    {
        var v = vox.Clone();
        change(v);
        session.Commands.Execute(CameraCommands.SetVoxExtras(v, description));
    }

    /// <summary>Edits the selected row (re-selects it after a time change re-sorts the list).</summary>
    public void EditSelected(Action<VoxExtrasInfo, VoxRowRef> change, string description)
    {
        if (Selected is not { } r || !VoxEffectEditing.Valid(vox, r)) return;
        var v = vox.Clone();
        object row = r.Kind == VoxRowKind.PostEffect ? v.PostEffects[r.Index] : v.Commands[r.Index];
        change(v, r);
        v.Sort();
        int i = r.Kind == VoxRowKind.PostEffect ? v.PostEffects.IndexOf((PostEffectRow)row) : v.Commands.IndexOf((VoxCommandRow)row);
        session.Commands.Execute(CameraCommands.SetVoxExtras(v, description));
        Selected = i >= 0 ? r with { Index = i } : null;
        SelectionChanged?.Invoke();
    }

    public void DeleteSelected()
    {
        if (Selected is { } r && VoxEffectEditing.Valid(vox, r))
            edit(v => VoxEffectEditing.Delete(v, r), "Delete effect row");
        else if (SelectedScripted is { } si && si < vox.Scripted.Count)
            edit(v => v.Scripted.RemoveAt(si), "Remove note script");
        Selected = null;
        SelectedScripted = null;
        SelectionChanged?.Invoke();
    }

    public void Select(VoxRowRef? row)
    {
        Selected = row;
        SelectedScripted = null;
        SelectionChanged?.Invoke();
    }

    /// <summary>After an undo / redo or a chart switch: drop a selection that no longer points at a row.</summary>
    public void Resync()
    {
        if (Selected is { } r && !VoxEffectEditing.Valid(vox, r)) Selected = null;
        if (SelectedScripted is { } s && s >= vox.Scripted.Count) SelectedScripted = null;
    }
}
