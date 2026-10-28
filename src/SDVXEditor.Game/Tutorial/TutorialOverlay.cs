using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Camera;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// Draws the tutorial over everything (above the dialogs, in window pixels): the dimmed spotlight around the step's
/// target with an animated outline and an arrow, the ghost objects in the compose playfield, and the instruction card.
/// Only the card takes clicks — unless a step asks for it, the dim lets every click through.
/// </summary>
public partial class TutorialOverlay : CompositeDrawable
{
    public const float DIM = 0.5f;

    private readonly TutorialManager manager;

    private readonly DimBox[] dims = new DimBox[4];
    private Container outline = null!;
    private Container arrow = null!;
    private SpriteIcon arrowIcon = null!;
    private Container ghostArea = null!;
    private Box zone = null!;
    private ZoneMarks zoneMarks = null!;
    private Container camArea = null!;
    private ZoneMarks camZone = null!;

    public TutorialPanel Panel { get; private set; } = null!;

    /// <summary>The spotlight rectangle in this overlay's space (empty when nothing is highlighted).</summary>
    public RectangleF Spotlight => hasTarget ? hole : RectangleF.Empty;

    /// <summary>
    /// Where the spotlight is heading this frame, in screen space (the hole glides there within a few frames): the
    /// target's rectangle plus the outline margin. Null when nothing is highlighted. The smoke test checks that every
    /// click of a step's scripted user lands inside it.
    /// </summary>
    public RectangleF? SpotlightGoal { get; private set; }

    /// <summary>The current step (or task) names a target, but it is not on screen (smoke test).</summary>
    public bool TargetMissing { get; private set; }

    /// <summary>Screen-space rectangle of the arrow (smoke test, screenshots).</summary>
    public bool ArrowShown => arrow.Alpha > 0.5f;

    /// <summary>Ghost drawables shown right now (smoke test).</summary>
    public int GhostCount => ghostArea.Alpha > 0 ? ghostArea.Children.OfType<GhostDrawable>().Count(g => g.Scale.X > 0) : 0;

    public TutorialOverlay(TutorialManager manager)
    {
        this.manager = manager;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        for (int i = 0; i < 4; i++) dims[i] = new DimBox { Alpha = 0 };

        InternalChildren = new Drawable[]
        {
            ghostArea = new Container { Masking = true, Alpha = 0 },
            camArea = new Container { Masking = true, Alpha = 0 },
            dims[0], dims[1], dims[2], dims[3],
            outline = new Container
            {
                Masking = true,
                CornerRadius = 8,
                BorderThickness = 3,
                BorderColour = TutorialPanel.Accent,
                Alpha = 0,
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Colour = TutorialPanel.Accent.Opacity(0.45f),
                    Radius = 14,
                    Hollow = true,
                },
                Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
            },
            arrow = new Container
            {
                Size = new Vector2(46),
                Origin = Anchor.Centre,
                Alpha = 0,
                Child = arrowIcon = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(40),
                    Icon = FontAwesome.Solid.ArrowDown,
                    Colour = TutorialPanel.Accent,
                    Shadow = true,
                },
            },
            Panel = new TutorialPanel(manager) { Alpha = 0 },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        outline.FadeTo(1, 500, Easing.InOutSine).Then().FadeTo(0.55f, 500, Easing.InOutSine).Loop();
        arrowIcon.MoveToY(-9, 380, Easing.InOutSine).Then().MoveToY(5, 380, Easing.InOutSine).Loop();
    }

    // ---- per frame -----------------------------------------------------------------------

    private RectangleF hole;
    private bool hasTarget;
    private int ghostsFor = -1;
    private Anchor corner = Anchor.BottomRight;
    private bool gliding;

    protected override void Update()
    {
        base.Update();

        bool active = manager.IsRunning || manager.Hint != null;
        Panel.Alpha = active ? 1 : 0;

        if (!active)
        {
            hideSpotlight();
            ghostArea.Alpha = 0;
            return;
        }

        Panel.Refresh(manager.Version);

        // ---- spotlight
        var goal = resolveTarget(out bool declared);
        TargetMissing = declared && goal == null;
        if (goal is { } screenRect)
        {
            var quad = ToLocalSpace(screenRect).AABBFloat.Inflate(6);
            quad = RectangleF.Intersect(quad, new RectangleF(0, 0, DrawWidth, DrawHeight));
            SpotlightGoal = ToScreenSpace(quad).AABBFloat;
            if (!hasTarget || hole.Width <= 0)
                hole = quad;
            else
            {
                float k = (float)Math.Clamp(1 - Math.Exp(-Time.Elapsed / 70), 0, 1);
                hole = new RectangleF(lerp(hole.X, quad.X, k), lerp(hole.Y, quad.Y, k), lerp(hole.Width, quad.Width, k), lerp(hole.Height, quad.Height, k));
            }

            hasTarget = true;
            showSpotlight(manager.Step?.BlockOutside == true);
        }
        else
        {
            SpotlightGoal = null;
            hasTarget = false;
            hideSpotlight();
        }

        // ---- panel position: a corner clear of the target
        placePanel();

        // ---- ghosts
        updateGhosts();
    }

    /// <summary>
    /// The screen-space rectangle to highlight: the current task's target, else the step's (area, then drawable, then
    /// anchor). <paramref name="declared"/> says whether the step names one at all (null then means "not on screen").
    /// </summary>
    private RectangleF? resolveTarget(out bool declared)
    {
        declared = false;
        if (manager.Hint is { Anchor: string hintAnchor } && !manager.IsRunning)
        {
            declared = true;
            return rectOf(TutorialAnchors.Resolve(hintAnchor));
        }

        if (manager.Step is not { } step || manager.Context is not { } ctx || manager.OffTrack != null || manager.StepSolved) return null;

        try
        {
            if (manager.CurrentTask is { HasTarget: true } task)
            {
                declared = true;
                return pick(task.Area, task.Target, task.Anchor, ctx);
            }

            if (step.Area == null && step.Target == null && step.Anchor == null) return null;
            declared = true;
            return pick(step.Area, step.Target, step.Anchor, ctx);
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or KeyNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    private static RectangleF? pick(Func<TutorialContext, RectangleF?>? area, Func<TutorialContext, Drawable?>? target, string? anchor, TutorialContext ctx)
    {
        if (area != null) return area(ctx) is { Width: > 0.5f, Height: > 0.5f } r ? r : null;
        if (target != null) return rectOf(target(ctx));
        return anchor != null ? rectOf(TutorialAnchors.Resolve(anchor)) : null;
    }

    private static RectangleF? rectOf(Drawable? d) => d != null && TutorialAnchors.IsShown(d) ? d.ScreenSpaceDrawQuad.AABBFloat : null;

    private static float lerp(float a, float b, float k) => a + (b - a) * k;

    private void showSpotlight(bool block)
    {
        var r = hole;
        set(dims[0], 0, 0, DrawWidth, r.Top);
        set(dims[1], 0, r.Bottom, DrawWidth, DrawHeight - r.Bottom);
        set(dims[2], 0, r.Top, r.Left, r.Height);
        set(dims[3], r.Right, r.Top, DrawWidth - r.Right, r.Height);
        foreach (var d in dims)
        {
            d.Blocks = block;
            d.Alpha = DIM;
        }

        outline.Position = r.Location;
        outline.Size = r.Size;
        outline.Show();

        // arrow: above the target pointing down, else below pointing up; beside very tall targets
        Vector2 pos;
        float rotation;
        if (r.Height > DrawHeight * 0.45f)
        {
            bool left = r.Left > 70;
            pos = new Vector2(left ? r.Left - 30 : r.Right + 30, r.Centre.Y);
            rotation = left ? -90 : 90;
        }
        else if (r.Top > 64)
        {
            pos = new Vector2(r.Centre.X, r.Top - 30);
            rotation = 0;
        }
        else
        {
            pos = new Vector2(r.Centre.X, r.Bottom + 30);
            rotation = 180;
        }

        arrow.Position = pos;
        arrow.Rotation = rotation;
        arrow.Alpha = 1;
    }

    private static void set(Drawable d, float x, float y, float w, float h)
    {
        d.Position = new Vector2(x, y);
        d.Size = new Vector2(Math.Max(0, w), Math.Max(0, h));
    }

    private void hideSpotlight()
    {
        foreach (var d in dims)
        {
            d.Alpha = 0;
            d.Blocks = false;
        }

        outline.Hide();
        arrow.Alpha = 0;
    }

    private void placePanel()
    {
        const float margin = 16, top = 56, bottom = 60;
        var size = Panel.DrawSize;
        if (size.Y < 10) return;

        RectangleF rectAt(Anchor a) => new RectangleF(
            a.HasFlag(Anchor.x0) ? margin : DrawWidth - size.X - margin,
            a.HasFlag(Anchor.y0) ? top : DrawHeight - size.Y - bottom,
            size.X, size.Y);

        var candidates = new[] { Anchor.BottomRight, Anchor.TopRight, Anchor.BottomLeft, Anchor.TopLeft };
        var avoid = hasTarget ? hole.Inflate(14) : RectangleF.Empty;
        float overlap(Anchor a) => hasTarget ? area(RectangleF.Intersect(rectAt(a), avoid)) : 0;

        // keep the current corner while it is clear (no jumping around); a new corner is reached with a short glide,
        // otherwise the card stays put so its buttons never move under the mouse
        if (overlap(corner) > 1)
        {
            corner = candidates.OrderBy(overlap).First();
            gliding = true;
        }

        var rect = rectAt(corner);
        if (gliding && Panel.Position != Vector2.Zero)
        {
            Panel.Position = new Vector2(lerp(Panel.X, rect.X, 0.3f), lerp(Panel.Y, rect.Y, 0.3f));
            if (Vector2.Distance(Panel.Position, rect.Location) < 1) gliding = false;
        }
        else
        {
            Panel.Position = rect.Location;
            gliding = false;
        }
    }

    private static float area(RectangleF r) => r.Width <= 0 || r.Height <= 0 ? 0 : r.Width * r.Height;

    /// <summary>The panel's corner (smoke test: it must not cover the spotlight).</summary>
    public RectangleF PanelRect => new RectangleF(Panel.Position, Panel.DrawSize);

    // ---- ghosts ------------------------------------------------------------------------

    private int camGhostsFor = -1;

    private void updateGhosts()
    {
        var ctx = manager.Context;
        var step = manager.Step;
        var editor = ctx?.Editor;
        bool noDialog = ctx != null && !ctx.Dialogs.HasOpenDialog;

        updateCameraGhosts(ctx, step, editor, noDialog);

        if (step == null || ctx == null || editor == null || editor.CurrentTab != EditorTab.Compose || !noDialog
            || (step.Ghosts == null && step.DeleteMarks == null && step.Zone == null))
        {
            ghostArea.Alpha = 0;
            return;
        }

        var pf = editor.Playfield;
        if (!pf.IsLoaded || pf.DrawHeight < 1) return;

        int key = manager.Version;
        if (ghostsFor != key)
        {
            ghostsFor = key;
            ghostArea.Clear();
            ghostArea.Add(zone = new Box { Colour = TutorialPanel.Accent, Alpha = 0 });
            ghostArea.Add(zoneMarks = new ZoneMarks());
            foreach (var t in step.Ghosts?.Invoke(ctx) ?? Array.Empty<TutorialTarget>()) ghostArea.Add(new GhostDrawable(t, false));
            foreach (var t in step.DeleteMarks?.Invoke(ctx) ?? Array.Empty<TutorialTarget>()) ghostArea.Add(new GhostDrawable(t, true));
        }

        // the area: the playfield plus its wide-laser overhang, in this overlay's space
        float ov = PlayfieldLayout.OVERHANG;
        var tl = ToLocalSpace(pf.ToScreenSpace(new Vector2(-ov, 0)));
        var br = ToLocalSpace(pf.ToScreenSpace(new Vector2(pf.DrawWidth + ov, pf.DrawHeight)));
        ghostArea.Position = tl;
        ghostArea.Size = br - tl;
        float sx = (br.X - tl.X) / (pf.DrawWidth + 2 * ov);
        float sy = (br.Y - tl.Y) / pf.DrawHeight;
        Vector2 map(float x, float y) => new Vector2((x + ov) * sx, y * sy);
        float yOf(long pulse) => pf.YOfPulse(pulse);

        ghostArea.Alpha = 1;

        if (step.Zone is { } zoneOf)
        {
            var (from, to) = zoneOf(ctx);
            var a = map(PlayfieldLayout.LANES_LEFT, yOf(to));
            var b = map(PlayfieldLayout.LANES_RIGHT, yOf(from));
            zone.Position = a;
            zone.Size = b - a;
            zone.Alpha = 0.13f;
            zoneMarks.Show(a, b - a, label(ctx, step));
        }
        else
        {
            zone.Alpha = 0;
            zoneMarks.Hide();
        }

        var chart = ctx.Chart;
        foreach (var g in ghostArea.Children.OfType<GhostDrawable>())
            g.Layout(map, yOf, sx, chart);
    }

    private static string? label(TutorialContext ctx, TutorialStep step)
    {
        try
        {
            return step.ZoneLabel?.Invoke(ctx);
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>The zone band and the numbered camera anchors over the camera tab's lanes.</summary>
    private void updateCameraGhosts(TutorialContext? ctx, TutorialStep? step, EditorScreen? editor, bool noDialog)
    {
        if (step == null || ctx == null || editor == null || editor.CurrentTab != EditorTab.Camera || !noDialog
            || (step.CameraGhosts == null && step.Zone == null) || editor.CameraContent.View.Value != 0)
        {
            camArea.Alpha = 0;
            return;
        }

        var lanes = editor.CameraContent.Lanes;
        if (!lanes.IsLoaded || lanes.DrawHeight < 1) return;

        int key = manager.Version;
        if (camGhostsFor != key)
        {
            camGhostsFor = key;
            camArea.Clear();
            camArea.Add(camZone = new ZoneMarks { Fill = true });
            foreach (var g in step.CameraGhosts?.Invoke(ctx) ?? Array.Empty<CameraGhost>()) camArea.Add(new CameraGhostDrawable(g));
        }

        var tl = ToLocalSpace(lanes.ScreenSpaceDrawQuad.TopLeft);
        var br = ToLocalSpace(lanes.ScreenSpaceDrawQuad.BottomRight);
        camArea.Position = tl;
        camArea.Size = br - tl;
        float sx = (br.X - tl.X) / Math.Max(1, lanes.DrawWidth);
        float sy = (br.Y - tl.Y) / Math.Max(1, lanes.DrawHeight);
        camArea.Alpha = 1;

        if (step.Zone is { } zoneOf)
        {
            var (from, to) = zoneOf(ctx);
            float y0 = lanes.YOfPulse(to) * sy, y1 = lanes.YOfPulse(from) * sy;
            camZone.Show(new Vector2(0, y0), new Vector2(camArea.Width, y1 - y0), label(ctx, step));
        }
        else camZone.Hide();

        var chart = ctx.Chart;
        foreach (var g in camArea.Children.OfType<CameraGhostDrawable>())
        {
            var gh = g.Ghost;
            g.Position = new Vector2((CameraLanes.LaneLeft(gh.Lane) + (float)gh.Fx * CameraLaneEditor.LANE_WIDTH) * sx, lanes.YOfPulse(gh.Pulse) * sy);
            g.SetPlaced(chart != null && safePlaced(gh, chart));
        }
    }

    private static bool safePlaced(CameraGhost g, Chart chart)
    {
        try
        {
            return g.Placed(chart);
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or KeyNotFoundException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Top and bottom edge lines of a zone band plus its label (the band itself is drawn by the caller, or <see cref="Fill"/>).</summary>
    private partial class ZoneMarks : Container
    {
        public bool Fill;
        private readonly Box fill;
        private readonly Container tag;
        private readonly SpriteText tagText;

        public ZoneMarks()
        {
            Alpha = 0;
            Children = new Drawable[]
            {
                fill = new Box { RelativeSizeAxes = Axes.Both, Colour = TutorialPanel.Accent, Alpha = 0 },
                new Box { RelativeSizeAxes = Axes.X, Height = 2, Colour = TutorialPanel.Accent },
                new Box { RelativeSizeAxes = Axes.X, Height = 2, Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft, Colour = TutorialPanel.Accent },
                tag = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Masking = true,
                    CornerRadius = 4,
                    Margin = new MarginPadding { Left = 4, Bottom = 4 },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.75f) },
                        tagText = new SpriteText { Font = SDVXEditor.Game.Graphics.EditorFont.Bold(14), Colour = TutorialPanel.Accent, Margin = new MarginPadding { Horizontal = 6, Vertical = 2 } },
                    },
                },
            };
        }

        public void Show(Vector2 position, Vector2 size, string? text)
        {
            Position = position;
            Size = new Vector2(size.X, Math.Max(2, size.Y));
            fill.Alpha = Fill ? 0.13f : 0;
            Alpha = 1;
            tagText.Text = text ?? "";
            tag.Alpha = string.IsNullOrEmpty(text) ? 0 : 1;
        }

        public new void Hide() => Alpha = 0;
    }

    /// <summary>Dim around the spotlight; blocks clicks only when the step asks for it.</summary>
    private partial class DimBox : Box
    {
        public bool Blocks;

        public DimBox()
        {
            Colour = Colour4.Black;
        }

        public override bool HandlePositionalInput => Blocks;
        protected override bool OnMouseDown(MouseDownEvent e) => Blocks;
        protected override bool OnClick(ClickEvent e) => Blocks;
        protected override bool OnScroll(ScrollEvent e) => Blocks;
    }
}

/// <summary>A translucent target object in the playfield (green once the chart has it, red when it has to go).</summary>
public partial class GhostDrawable : CompositeDrawable
{
    public TutorialTarget Target { get; }
    public bool Delete { get; }

    private readonly List<Drawable> parts = new List<Drawable>();
    private SpriteIcon? cross;

    public GhostDrawable(TutorialTarget target, bool delete)
    {
        Target = target;
        Delete = delete;
        RelativeSizeAxes = Axes.Both;

        if (!target.IsLaser)
        {
            parts.Add(box());
            if (delete) AddInternal(cross = new SpriteIcon { Icon = FontAwesome.Solid.Times, Colour = Colour4.FromHex("ff4d4d"), Origin = Anchor.Centre, Size = new Vector2(18) });
            return;
        }

        int n = target.Points.Count;
        for (int i = 0; i < n; i++)
        {
            if (target.Points[i].IsSlam) parts.Add(line());
            if (i + 1 < n) parts.Add(line());
            if (target.Curve) parts.Add(dot());
        }
    }

    private Drawable box()
    {
        var c = new Container
        {
            Masking = true,
            CornerRadius = 3,
            BorderThickness = 2.5f,
            BorderColour = Colour4.White,
            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0.35f },
        };
        AddInternal(c);
        return c;
    }

    private Drawable line()
    {
        var b = new Box { Origin = Anchor.CentreLeft, Alpha = 0.45f };
        AddInternal(b);
        return b;
    }

    private Drawable dot()
    {
        var c = new Circle { Origin = Anchor.Centre, Size = new Vector2(20), Alpha = 0.85f };
        AddInternal(c);
        return c;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        this.FadeTo(1, 450, Easing.InOutSine).Then().FadeTo(0.55f, 450, Easing.InOutSine).Loop();
    }

    /// <summary>Positions every part for the current scroll position.</summary>
    public void Layout(Func<float, float, Vector2> map, Func<long, float> yOf, float sx, Chart? chart)
    {
        bool placed = chart != null && TutorialCharts.IsPlaced(chart, Target);
        bool gone = chart != null && Delete && !Target.IsLaser && !chart.Notes.Lane(Target.Kind, Target.Lane).Any(n => n.Y == Target.Y);
        Colour4 colour = Delete ? Colour4.FromHex("ff4d4d") : placed ? TutorialPanel.Good : Target.IsLaser ? (Target.Lane == 0 ? Colour4.FromHex("4de1ff") : Colour4.FromHex("ff5ad1")) : Colour4.White;
        Scale = gone ? Vector2.Zero : Vector2.One; // (Alpha pulses through a loop transform)

        if (!Target.IsLaser)
        {
            float left = PlayfieldLayout.LaneLeft(Target.Kind, Target.Lane) + 3;
            float right = left + PlayfieldLayout.LaneWidth(Target.Kind) - 6;
            float h = (Target.Kind == NoteKind.Fx ? PlayfieldLayout.FX_CHIP_HEIGHT : PlayfieldLayout.CHIP_HEIGHT) + 6;
            float yStart = yOf(Target.Y), yEnd = yOf(Target.Y + Target.Length);
            var a = map(left, yEnd - h / 2);
            var b = map(right, yStart + h / 2);
            var c = (Container)parts[0];
            c.Position = a;
            c.Size = b - a;
            c.BorderColour = colour;
            c.Child.Colour = colour;
            if (cross != null) cross.Position = (a + b) / 2;
            return;
        }

        float thickness = Math.Max(4, PlayfieldLayout.LASER_HALF_WIDTH * 1.2f * sx);
        int k = 0;
        Vector2 pointAt(long ry, double v) => map(PlayfieldLayout.LaserX(v, Target.Wide), yOf(Target.Y + ry));

        void segment(Vector2 from, Vector2 to, float width)
        {
            var d = parts[k++];
            var delta = to - from;
            d.Position = from;
            d.Size = new Vector2(delta.Length, width);
            d.Rotation = MathHelper.RadiansToDegrees(MathF.Atan2(delta.Y, delta.X));
            d.Colour = colour;
        }

        var pts = Target.Points;
        for (int i = 0; i < pts.Count; i++)
        {
            var p = pts[i];
            if (p.IsSlam) segment(pointAt(p.Ry, p.V), pointAt(p.Ry, p.ValueAfter), thickness);
            if (i + 1 < pts.Count) segment(pointAt(p.Ry, p.ValueAfter), pointAt(pts[i + 1].Ry, pts[i + 1].V), Target.Curve ? 3 : thickness);
            if (Target.Curve)
            {
                var dotPart = parts[k++];
                dotPart.Position = pointAt(p.Ry, p.V);
                dotPart.Colour = colour;
            }
        }
    }
}

/// <summary>A numbered ring in a camera lane: where to click (green once the chart has that anchor).</summary>
public partial class CameraGhostDrawable : CompositeDrawable
{
    public CameraGhost Ghost { get; }

    private readonly Circle ring;
    private readonly SpriteText number;

    public CameraGhostDrawable(CameraGhost ghost)
    {
        Ghost = ghost;
        Size = new Vector2(28);
        Origin = Anchor.Centre;
        InternalChildren = new Drawable[]
        {
            ring = new Circle { RelativeSizeAxes = Axes.Both, Colour = TutorialPanel.Accent, Alpha = 0.85f },
            new Circle { RelativeSizeAxes = Axes.Both, Size = new Vector2(0.74f), Anchor = Anchor.Centre, Origin = Anchor.Centre, Colour = Colour4.FromHex("1c1c26") },
            number = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = ghost.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Font = SDVXEditor.Game.Graphics.EditorFont.Bold(14),
                Colour = TutorialPanel.Accent,
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        this.FadeTo(1, 450, Easing.InOutSine).Then().FadeTo(0.6f, 450, Easing.InOutSine).Loop();
    }

    public void SetPlaced(bool placed)
    {
        var c = placed ? TutorialPanel.Good : TutorialPanel.Accent;
        ring.Colour = c;
        number.Colour = c;
    }
}
