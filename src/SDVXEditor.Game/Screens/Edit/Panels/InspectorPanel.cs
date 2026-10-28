using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit.Components;

namespace SDVXEditor.Game.Screens.Edit.Panels;

/// <summary>
/// Inspector (Ctrl+Shift+I): exact values of the selection. Notes: start (pulse or measure:beat+pulse), length (pulses,
/// "1.5b" beats or "1/4" of a beat; 0 = chip), lane, the FX holds' effect. A multi-selection shows "(mixed)" where the
/// values differ; entering a value applies it to all (start moves the group so the earliest lands there). Every
/// change is one undo step (Core <see cref="NoteInspector"/>). Laser values are edited through
/// <see cref="LaserFields"/> (added by the laser part).
/// </summary>
public partial class InspectorPanel : SidePanel
{
    public const string MIXED = "(mixed)";

    private readonly EditorSession session;
    private readonly Action<string> message;

    private SpriteText summary = null!;
    private Container noteFields = null!;

    public PanelField StartField { get; private set; } = null!;
    public PanelField LengthField { get; private set; } = null!;
    public PanelField LaneField { get; private set; } = null!;
    private TooltipSpriteText timeText = null!, effectText = null!;

    /// <summary>The laser part of the panel.</summary>
    public FillFlowContainer LaserFields { get; private set; } = null!;

    public PanelField LaserPositionField { get; private set; } = null!;
    public PanelField LaserStartField { get; private set; } = null!;

    /// <summary>The slam's landing end (Vf); empty = no slam.</summary>
    public PanelField LaserLandingField { get; private set; } = null!;

    public PanelField LaserTypeField { get; private set; } = null!;
    public PanelField LaserWidthField { get; private set; } = null!;
    private TextFlowContainer laserNote = null!;

    public InspectorPanel(EditorSession session, Action<string> message)
        : base("Inspector")
    {
        this.session = session;
        this.message = message;
    }

    private IReadOnlyCollection<NoteRef> notes => session.Selection.Notes;

    protected override void LoadComplete()
    {
        base.LoadComplete();
        Content.AddRange(new Drawable[]
        {
            summary = new SpriteText { Font = EditorFont.Regular(15), Colour = EditorColours.Text },
            noteFields = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Child = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new osuTK.Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        PanelControls.Header("Notes"),
                        StartField = new PanelField("Start", startText, commitStart, SidePanel.WIDTH - 16, 70),
                        timeText = new TooltipSpriteText { Font = EditorFont.Regular(15), Colour = EditorColours.TextMuted, X = 70 },
                        LengthField = new PanelField("Length", lengthText, commitLength, SidePanel.WIDTH - 16, 70),
                        LaneField = new PanelField("Lane", laneText, commitLane, SidePanel.WIDTH - 16, 70),
                        effectText = new TooltipSpriteText { Font = EditorFont.Regular(15), Colour = EditorColours.Lanes.Fx },
                        PanelControls.Note("Start: measure:beat, e.g. 12:3 (12:3+120 = between beats). Length: 1.5b (beats), 1/4 (of a beat) or a number (240 = one beat); 0 = chip. Lane: A-D / L, R. Enter applies (one undo step)."),
                    },
                },
            },
            LaserFields = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new osuTK.Vector2(0, 4),
            },
        });

        LaserFields.AddRange(new Drawable[]
        {
            PanelControls.Header("Lasers"),
            LaserPositionField = new PanelField("Position", laserPositionText, commitLaserPosition, SidePanel.WIDTH - 16, 70),
            LaserStartField = new PanelField("Value", () => num(LaserInspector.Of(session.Selection).Start), t => commitValue(t, false), SidePanel.WIDTH - 16, 70),
            LaserLandingField = new PanelField("Slam to", landingText, t => commitValue(t, true), SidePanel.WIDTH - 16, 70),
            LaserTypeField = new PanelField("Curve", typeText, commitType, SidePanel.WIDTH - 16, 70),
            LaserWidthField = new PanelField("Width", () => LaserInspector.Of(session.Selection).Width is { Mixed: false, Value: int w } ? w.ToString(CultureInfo.InvariantCulture) : MIXED, commitWidth, SidePanel.WIDTH - 16, 70),
            laserNote = PanelControls.Note(""),
        });

        session.Selection.Changed += scheduleRefresh;
        session.ChartChanged += scheduleRefresh;
        Refresh();
    }

    protected override void OnShown()
    {
        if (IsLoaded) Refresh();
    }

    private void scheduleRefresh() => Schedule(Refresh);

    /// <summary>Re-reads the selection (fields being typed in keep their text).</summary>
    public void Refresh()
    {
        if (!IsLoaded) return;
        var s = NoteInspector.Of(notes);
        int points = session.Selection.TouchedLaserPoints.Count();
        summary.Text = session.Selection.IsEmpty
            ? "Nothing selected."
            : string.Join(" · ", new[]
            {
                s.Count > 0 ? $"{s.Count} note{(s.Count == 1 ? "" : "s")} ({s.Bt} BT, {s.Fx} FX)" : null,
                points > 0 ? $"{points} laser point{(points == 1 ? "" : "s")} in {session.Selection.Sections.Count()} section(s)" : null,
            }.Where(x => x != null));

        noteFields.Alpha = s.Count > 0 ? 1 : 0;
        StartField.Refresh();
        LengthField.Refresh();
        LaneField.Refresh();
        // the song time in the UI's one format; the raw pulse only as the tooltip (audit U22: "pulse 960" jargon, U1/U2)
        timeText.Text = s.Start is { Mixed: false, Value: long y } ? $"at {SDVXEditor.Core.Editing.UiFormat.Time(session.PulseToMs(y))}" : "";
        timeText.TooltipText = s.Start is { Mixed: false, Value: long y2 } ? $"position {y2} pulses (240 per beat)" : default;
        effectText.Text = s.Fx == 0 ? "" : s.EffectMixed ? "FX effect: (mixed) - set it with the toolbox FX Effect" : $"FX effect: {s.Effect ?? "none"}";
        var l = LaserInspector.Of(session.Selection);
        LaserFields.Alpha = l.Anchors > 0 || l.Samples > 0 ? 1 : 0;
        foreach (var f in new[] { LaserPositionField, LaserStartField, LaserLandingField, LaserTypeField, LaserWidthField }) f.Refresh();
        laserNote.Text = (l.Samples > 0 ? $"{l.Samples} selected point(s) are samples of a curve, not anchors: they are left alone. " : "")
                         + "Values 0..1 (wide: the 2x range). Slam to: the landing end, empty = no slam. Curve: straight / perfect / bezier / auto.";
        RefreshLaser?.Invoke();
    }

    private static string num(InspectorValue<double> v) => v.Mixed ? MIXED : v.Value is double d ? d.ToString("0.####", CultureInfo.InvariantCulture) : "";

    private string laserPositionText()
    {
        var v = LaserInspector.Of(session.Selection).Position;
        return v.Mixed ? MIXED : v.Value is long y ? InspectorText.Position(session.Timing, y) : "";
    }

    private string landingText()
    {
        var l = LaserInspector.Of(session.Selection);
        if (!l.AnySlam) return "";
        return l.AllSlams ? num(l.Landing) : MIXED;
    }

    private string typeText()
    {
        var t = LaserInspector.Of(session.Selection).Type;
        return t.Mixed ? MIXED : t.Value is int v ? LaserInspector.TypeName(v) : "";
    }

    private void commitLaserPosition(string text)
    {
        if (text.Trim() == MIXED) return;
        if (InspectorText.ParsePosition(text, session.Timing) is not long pulse)
        {
            message($"Inspector: \"{text}\" is no position");
            return;
        }

        apply(LaserInspector.SetPosition(session.Chart, session.Selection, pulse), "Inspector: the anchor cannot move there (a neighbour is in the way, or another laser)");
    }

    private void commitValue(string text, bool landing)
    {
        string t = text.Trim();
        if (t == MIXED) return;
        if (landing && t.Length == 0)
        {
            apply(LaserInspector.SetLanding(session.Chart, session.Selection, null), "Inspector: a lone slam keeps its slam (delete it instead), or nothing changes");
            return;
        }

        if (!PanelControls.TryNum(t, out double v) || v < 0 || v > 1)
        {
            message($"Inspector: \"{text}\" is no laser value (0..1)");
            return;
        }

        apply(landing ? LaserInspector.SetLanding(session.Chart, session.Selection, v) : LaserInspector.SetStart(session.Chart, session.Selection, v),
            "Inspector: nothing changes (or another laser is in the way)");
    }

    private void commitType(string text)
    {
        if (text.Trim() == MIXED) return;
        if (!LaserInspector.TryParseType(text, out var type))
        {
            message($"Inspector: \"{text}\" is no curve type (straight, perfect, bezier, auto)");
            return;
        }

        apply(LaserInspector.SetType(session.Chart, session.Selection, type), "Inspector: nothing changes");
    }

    private void commitWidth(string text)
    {
        string t = text.Trim().TrimEnd('x', 'X');
        if (t == MIXED) return;
        if (t is not ("1" or "2"))
        {
            message("Inspector: width is 1 (normal) or 2 (wide)");
            return;
        }

        apply(LaserInspector.SetWidth(session.Chart, session.Selection, t == "1" ? 1 : 2), "Inspector: nothing changes");
    }

    /// <summary>Called on every refresh (the laser inspector hooks in here).</summary>
    public Action? RefreshLaser { get; set; }

    private string startText()
    {
        var v = NoteInspector.Of(notes).Start;
        return v.Mixed ? MIXED : v.Value is long y ? InspectorText.Position(session.Timing, y) : "";
    }

    private string lengthText()
    {
        var v = NoteInspector.Of(notes).Length;
        return v.Mixed ? MIXED : v.Value is long l ? InspectorText.Length(l) : "";
    }

    private string laneText()
    {
        var s = NoteInspector.Of(notes);
        if (s.Lane.Mixed) return MIXED;
        if (s.Lane.Value is not int lane || notes.Count == 0) return "";
        return notes.First().Kind == NoteKind.Bt ? ((char)('A' + lane)).ToString() : lane == 0 ? "L" : "R";
    }

    private void apply(IEditCommand? cmd, string refused)
    {
        if (cmd == null)
        {
            message(refused);
            return;
        }

        session.Commands.Execute(cmd);
        Refresh();
    }

    private void commitStart(string text)
    {
        if (text.Trim() == MIXED || notes.Count == 0) return;
        if (InspectorText.ParsePosition(text, session.Timing) is not long pulse)
        {
            message($"Inspector: \"{text}\" is no position (pulse, or measure:beat+pulses)");
            return;
        }

        apply(NoteInspector.SetStart(session.Chart, notes, pulse), "Inspector: the notes cannot move there (overlap, or no change)");
    }

    private void commitLength(string text)
    {
        if (text.Trim() == MIXED || notes.Count == 0) return;
        if (InspectorText.ParseLength(text) is not long length)
        {
            message($"Inspector: \"{text}\" is no length (pulses, 1.5b or 1/4)");
            return;
        }

        apply(NoteInspector.SetLength(session.Chart, notes, length), "Inspector: that length would overlap another note (or nothing changes)");
    }

    private void commitLane(string text)
    {
        if (text.Trim() == MIXED || notes.Count == 0) return;
        string t = text.Trim().ToUpperInvariant();
        bool bt = notes.First().Kind == NoteKind.Bt;
        int lane = t switch
        {
            "A" or "1" => 0,
            "B" or "2" => 1,
            "C" or "3" => 2,
            "D" or "4" => 3,
            "L" => 0,
            "R" => 1,
            _ => -1,
        };
        if (!bt && t is "1" or "2") lane = int.Parse(t, CultureInfo.InvariantCulture) - 1;
        apply(lane < 0 ? null : NoteInspector.SetLane(session.Chart, notes, lane), "Inspector: no such lane, mixed BT/FX, or the lane is occupied");
    }

    protected override void Dispose(bool isDisposing)
    {
        session.Selection.Changed -= scheduleRefresh;
        session.ChartChanged -= scheduleRefresh;
        base.Dispose(isDisposing);
    }
}
