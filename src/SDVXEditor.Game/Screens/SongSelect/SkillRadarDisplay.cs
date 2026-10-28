using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Radar;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// The chart's skill radar as a hexagon like the game's music select: NOTES on top, then clockwise PEAK, TSUMAMI,
/// TRICKY, HAND-TRIP, ONE-HAND. Redrawn from primitives — geometry and colours were measured on the game's own
/// radar artwork (pointy-top hexagon, black at 70 %, grey rim, half-white axes with ticks at quarters, one label
/// colour per axis); no game file is used. The rim is 200, the top of the official music_db range.
/// </summary>
public partial class SkillRadarDisplay : CompositeDrawable
{
    /// <summary>Value at the rim (official music_db radars run 0..200).</summary>
    public const float RimValue = 200;

    /// <summary>Label colours measured on the game's radar labels.</summary>
    public static Colour4 AxisColour(RadarAxis axis) => axis switch
    {
        RadarAxis.Notes => Colour4.FromHex("00fff2"),
        RadarAxis.Peak => Colour4.FromHex("ff003f"),
        RadarAxis.Tsumami => Colour4.FromHex("ff00ca"),
        RadarAxis.Tricky => Colour4.FromHex("f9ff00"),
        RadarAxis.HandTrip => Colour4.FromHex("ba66ff"),
        _ => Colour4.FromHex("00ff74"),
    };

    private readonly float radius;
    private readonly float[] shown = new float[SkillRadar.AxisCount];
    private readonly float[] target = new float[SkillRadar.AxisCount];
    private readonly SpriteText[] valueTexts = new SpriteText[SkillRadar.AxisCount];
    private readonly Drawable[] labelBlocks = new Drawable[SkillRadar.AxisCount];

    /// <summary>The six name + value blocks, NOTES first, clockwise (smoke test: all lie inside the bounds, none overlaps).</summary>
    public IReadOnlyList<Drawable> LabelBlocks => labelBlocks;

    /// <summary>The hexagon's screen-space corners (smoke test).</summary>
    public Vector2[] HexagonScreenCorners => Enumerable.Range(0, 6).Select(i => ToScreenSpace(centre + direction(i) * radius)).ToArray();
    private QuadBatch frame = null!;
    private QuadBatch shape = null!;
    private SpriteText status = null!;
    private bool dirty = true;

    /// <summary>Values on display; null while computing or without a chart file.</summary>
    public SkillRadar? Values { get; private set; }

    /// <summary>The chart file the values belong to (smoke test).</summary>
    public string? ChartPath { get; private set; }

    /// <summary>Gap between a hexagon tip and its label (name + value).</summary>
    public const float LABEL_GAP = 30;

    /// <summary>Room reserved for one label block: the widest name (HAND-TRIP, bold 20) and name + value stacked.</summary>
    private const float label_width = 118, label_height = 46;

    /// <summary>
    /// The bounds hold the hexagon and all six labels with their values (a spotlight around the radar encloses NOTES above
    /// and TRICKY below too; before, those two stuck out of the drawable).
    /// </summary>
    public SkillRadarDisplay(float radius = 124)
    {
        this.radius = radius;
        float side = MathF.Cos(MathF.PI / 6) * (radius + LABEL_GAP) + label_width;
        float vertical = radius + LABEL_GAP + label_height;
        Size = new Vector2(2 * side + 8, 2 * vertical + 8);
    }

    private Vector2 centre => new Vector2(DrawWidth / 2, DrawHeight / 2);

    private static Vector2 direction(int axis)
    {
        double a = (-90 + 60 * axis) * Math.PI / 180;
        return new Vector2((float)Math.Cos(a), (float)Math.Sin(a));
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        frame = new QuadBatch();
        shape = new QuadBatch();
        var children = new List<Drawable> { frame, shape };

        for (int i = 0; i < SkillRadar.AxisCount; i++)
        {
            var axis = (RadarAxis)i;
            var dir = direction(i);
            var pos = new Vector2(Size.X / 2, Size.Y / 2) + dir * (radius + LABEL_GAP);
            // top/bottom labels stack above/below the tip; side labels sit beside it
            Anchor origin = i switch
            {
                0 => Anchor.BottomCentre,
                3 => Anchor.TopCentre,
                1 or 2 => Anchor.CentreLeft,
                _ => Anchor.CentreRight,
            };

            children.Add(labelBlocks[i] = new FillFlowContainer
            {
                Position = pos,
                Origin = origin,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    new SpriteText
                    {
                        Anchor = flowAnchor(origin),
                        Origin = flowAnchor(origin),
                        Text = SkillRadar.Label(axis),
                        Font = EditorFont.Bold(20),
                        Colour = AxisColour(axis),
                        Shadow = true,
                    },
                    valueTexts[i] = new SpriteText
                    {
                        Anchor = flowAnchor(origin),
                        Origin = flowAnchor(origin),
                        Text = "-",
                        Font = EditorFont.Regular(18),
                        Colour = Colour4.White,
                        Shadow = true,
                    },
                },
            });
        }

        children.Add(status = new SpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Font = EditorFont.Regular(16),
            Colour = Colour4.White.Opacity(0.7f),
            Shadow = true,
        });

        InternalChildren = children;
    }

    private static Anchor flowAnchor(Anchor origin) => origin switch
    {
        Anchor.CentreLeft => Anchor.TopLeft,
        Anchor.CentreRight => Anchor.TopRight,
        _ => Anchor.TopCentre,
    };

    /// <summary>Shows <paramref name="values"/> for <paramref name="chartPath"/>; null values = computing / unavailable.</summary>
    public void Show(SkillRadar? values, string? chartPath, string? message = null)
    {
        Values = values;
        ChartPath = chartPath;
        for (int i = 0; i < SkillRadar.AxisCount; i++)
        {
            int v = values?[(RadarAxis)i] ?? 0;
            target[i] = Math.Clamp(v / RimValue, 0, 1);
            valueTexts[i].Text = values == null ? "-" : v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        status.Text = values == null ? message ?? "" : "";
        dirty = true;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        drawFrame();
    }

    protected override void Update()
    {
        base.Update();

        // ease toward the new values (~150 ms) like the game's radar morphing between charts
        float k = (float)Math.Clamp(Time.Elapsed / 60.0, 0, 1);
        bool moving = false;
        for (int i = 0; i < shown.Length; i++)
        {
            float d = target[i] - shown[i];
            if (Math.Abs(d) < 0.001f) shown[i] = target[i];
            else
            {
                shown[i] += d * k;
                moving = true;
            }
        }

        if (moving || dirty)
        {
            dirty = false;
            drawShape();
        }
    }

    private void drawFrame()
    {
        frame.Clear();
        var c = centre;
        var bg = Colour4.Black.Opacity(0.7f);
        var rim = Colour4.FromHex("adadad").Opacity(0.88f);
        var axisColour = Colour4.White.Opacity(0.5f);

        for (int i = 0; i < 6; i++)
        {
            var a = c + direction(i) * radius;
            var b = c + direction((i + 1) % 6) * radius;
            frame.Add(triangle(c, a, b), bg);
        }

        // quarter rings, faint, then the rim
        for (int ring = 1; ring <= 3; ring++)
            polygonOutline(frame, Enumerable.Range(0, 6).Select(i => c + direction(i) * radius * ring / 4f).ToArray(), 1, Colour4.White.Opacity(0.08f));
        polygonOutline(frame, Enumerable.Range(0, 6).Select(i => c + direction(i) * radius).ToArray(), 2.5f, rim);

        for (int i = 0; i < 6; i++)
        {
            var dir = direction(i);
            frame.Add(line(c, c + dir * radius, 1.2f), axisColour);
            var normal = new Vector2(-dir.Y, dir.X);
            for (int t = 1; t <= 3; t++)
            {
                var p = c + dir * radius * t / 4f;
                frame.Add(line(p - normal * 4, p + normal * 4, 1.2f), axisColour);
            }
        }
    }

    private void drawShape()
    {
        shape.Clear();
        var c = centre;
        var pts = Enumerable.Range(0, 6).Select(i => c + direction(i) * radius * shown[i]).ToArray();
        if (pts.All(p => (p - c).LengthSquared < 0.25f)) return;

        var centreColour = Colour4.White.Opacity(0.18f);
        for (int i = 0; i < 6; i++)
        {
            int j = (i + 1) % 6;
            var ci = AxisColour((RadarAxis)i).Opacity(0.55f);
            var cj = AxisColour((RadarAxis)j).Opacity(0.55f);
            shape.Add(triangle(c, pts[i], pts[j]), new ColourInfo { TopLeft = centreColour, TopRight = ci, BottomLeft = cj, BottomRight = cj });
        }

        polygonOutline(shape, pts, 2, Colour4.White.Opacity(0.95f));
        foreach (var p in pts)
            shape.Add(new Quad(p.X - 3, p.Y - 3, 6, 6), Colour4.White);
    }

    private static Quad triangle(Vector2 a, Vector2 b, Vector2 c) => new Quad(a, b, c, c);

    private static Quad line(Vector2 a, Vector2 b, float width)
    {
        var d = b - a;
        if (d.LengthSquared < 1e-6f) return new Quad(a, a, a, a);
        var n = new Vector2(-d.Y, d.X).Normalized() * (width / 2);
        return new Quad(a - n, a + n, b - n, b + n);
    }

    private static void polygonOutline(QuadBatch batch, Vector2[] pts, float width, ColourInfo colour)
    {
        for (int i = 0; i < pts.Length; i++)
            batch.Add(line(pts[i], pts[(i + 1) % pts.Length], width), colour);
    }
}
