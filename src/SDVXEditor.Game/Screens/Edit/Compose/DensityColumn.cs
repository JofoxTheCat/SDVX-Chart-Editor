using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Compose;

/// <summary>
/// Whole-chart minimap left of the playfield (osu!mania density bar, reference 02): one row per
/// few pixels, the song start at the bottom so the position marker climbs like the playfield
/// scrolls (osu!mania). Pink bars = note density of the row (BT, FX and laser
/// points), yellow = rows inside holds or with selected objects, a white frame = the part of the
/// chart the playfield currently shows. Click or drag to seek.
/// </summary>
public partial class DensityColumn : CompositeDrawable, osu.Framework.Graphics.Cursor.IHasTooltip
{
    /// <summary>What the column is (audit P2: it was unexplained, and an empty chart looked like a render glitch).</summary>
    public osu.Framework.Localisation.LocalisableString TooltipText =>
        "Whole-chart overview (start at the bottom): pink = how many notes, yellow = holds and the selection, "
        + "bright frame = what the playfield shows. Click or drag to jump.";

    /// <summary>"no notes yet" while the chart is empty (smoke test).</summary>
    public bool EmptyHintShown => emptyHint.Alpha > 0;

    private osu.Framework.Graphics.Sprites.SpriteText emptyHint = null!;
    private const float row_height = 3;

    private static readonly Colour4 pink = Colour4.FromHex("f6a5c8");
    private static readonly Colour4 yellow = Colour4.FromHex("f2ea12");

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly Func<(double FromMs, double ToMs)> visibleRange;

    private QuadBatch bars = null!;
    private Box viewport = null!;
    private Box now = null!;

    private int builtVersion = -1;
    private double builtLength;
    private float builtHeight;
    private bool selectionDirty = true;

    public DensityColumn(EditorSession session, EditorClock clock, Func<(double, double)> visibleRange)
    {
        this.session = session;
        this.clock = clock;
        this.visibleRange = visibleRange;
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.6f) },
            bars = new QuadBatch(),
            viewport = new Box { RelativeSizeAxes = Axes.X, Colour = Colour4.White.Opacity(0.18f) },
            now = new Box { RelativeSizeAxes = Axes.X, Height = 2, Origin = Anchor.CentreLeft, Colour = Colour4.White },
            emptyHint = new osu.Framework.Graphics.Sprites.SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Rotation = -90,
                Text = "overview - no notes yet",
                Font = EditorFont.Regular(16),
                Colour = Colour4.White.Opacity(0.6f),
                Alpha = 0,
            },
        };

        session.Selection.Changed += onSelectionChanged;
    }

    private void onSelectionChanged() => selectionDirty = true;

    protected override void Dispose(bool isDisposing)
    {
        session.Selection.Changed -= onSelectionChanged;
        base.Dispose(isDisposing);
    }

    private double length => Math.Max(1, clock.LengthMs);

    private float yOf(double ms) => (1 - (float)(ms / length)) * DrawHeight;

    protected override void Update()
    {
        base.Update();

        if (builtVersion != session.Version || builtLength != length || builtHeight != DrawHeight || selectionDirty)
        {
            rebuild();
            var notes = session.Chart.Notes;
            emptyHint.Alpha = notes.Bt.All(l => l.Count == 0) && notes.Fx.All(l => l.Count == 0) && notes.Lasers.All(l => l.Count == 0) ? 1 : 0;
        }

        var (from, to) = visibleRange();
        float y1 = yOf(Math.Min(length, to)), y2 = yOf(Math.Max(0, from));
        viewport.Y = y1;
        viewport.Height = Math.Max(2, y2 - y1);
        now.Y = yOf(clock.CurrentTime.Value);
    }

    private void rebuild()
    {
        builtVersion = session.Version;
        builtLength = length;
        builtHeight = DrawHeight;
        selectionDirty = false;
        bars.Clear();

        int rows = Math.Max(1, (int)(DrawHeight / row_height));
        var density = new double[rows];
        var hold = new bool[rows];
        var selected = new bool[rows];

        int rowOf(long pulse) => Math.Clamp((int)(session.PulseToMs(pulse) / length * rows), 0, rows - 1);

        var notes = session.Chart.Notes;
        foreach (var lane in notes.Bt.Concat(notes.Fx))
        {
            foreach (var n in lane)
            {
                int r = rowOf(n.Y);
                density[r] += 1;
                if (n.IsHold)
                    for (int k = r; k <= rowOf(n.EndY); k++) hold[k] = true;
            }
        }

        foreach (var lane in notes.Lasers)
            foreach (var s in lane)
                foreach (var p in s.Points)
                    density[rowOf(s.Y + p.Ry)] += 0.5;

        foreach (var n in session.Selection.Notes) selected[rowOf(n.Note.Y)] = true;
        foreach (var p in session.Selection.TouchedLaserPoints) selected[rowOf(p.Section.Y + p.Point.Ry)] = true;

        double max = density.DefaultIfEmpty(0).Max();
        float w = DrawWidth - 4;

        for (int r = 0; r < rows; r++)
        {
            float y = DrawHeight - (r + 1) * row_height;

            if (density[r] > 0)
            {
                float f = (float)Math.Sqrt(density[r] / max);
                bars.AddRect(2, y, Math.Max(6, w * f), row_height - 1, pink);
            }

            if (hold[r])
                bars.AddRect(2, y, w * 0.3f, row_height - 1, yellow.Opacity(0.8f));

            if (selected[r])
                bars.AddRect(2, y, w, row_height - 1, yellow);
        }
    }

    // ---- seek --------------------------------------------------------------

    private void seek(Vector2 screenSpace)
    {
        if (DrawHeight <= 0) return;
        float f = 1 - Math.Clamp(ToLocalSpace(screenSpace).Y / DrawHeight, 0, 1);
        clock.CurrentTime.Value = f * length;
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        seek(e.ScreenSpaceMousePosition);
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e) => true;

    protected override void OnDrag(DragEvent e) => seek(e.ScreenSpaceMousePosition);
}
