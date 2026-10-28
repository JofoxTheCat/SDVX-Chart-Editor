using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Screens.Edit.Dialogs;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// Markers of the bottom-bar summary timeline (osu-editor-spec 1.7): BPM changes red and scroll
/// speed points green (ticks up), bookmarks in their own colour (default blue, ticks down, label on
/// hover), the preview point yellow (long tick).
/// </summary>
public partial class SummaryMarkers : CompositeDrawable, osu.Framework.Graphics.Cursor.IHasTooltip
{
    /// <summary>What the marks mean (audit P3: the red blocks had no legend); hidden while a bookmark label shows.</summary>
    public osu.Framework.Localisation.LocalisableString TooltipText => HoverLabel != null ? default
        : overNoAudio ? "No audio here: the chart goes on after the song's end"
        : "Song overview - click or drag to jump. Above the line: red = BPM change, green = scroll speed; below: bookmarks; yellow = preview start";

    private float lastMouseX = -1;

    private bool overNoAudio => clock.ChartBeyondAudio && clock.LengthMs > 0 && DrawWidth > 0
                                && lastMouseX / DrawWidth > clock.AudioLengthMs / clock.LengthMs;

    private static readonly Colour4 bpm_red = Colour4.FromHex("ff3b3b");
    private static readonly Colour4 scroll_green = Colour4.FromHex("4fdb4f");
    private static readonly Colour4 preview_yellow = Colour4.FromHex("ffd400");

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly IBindableList<double> bookmarks;

    private Container markers = null!;
    private Container hoverTag = null!;
    private SpriteText hoverText = null!;

    /// <summary>(relative x, hover text) of every bookmark marker, for the hover label.</summary>
    private readonly List<(float X, string Text)> bookmarkSpots = new List<(float, string)>();

    private int builtVersion = -1;
    private double builtLength;
    private bool dirty = true;

    public SummaryMarkers(EditorSession session, EditorClock clock, IBindableList<double> bookmarks)
    {
        this.session = session;
        this.clock = clock;
        this.bookmarks = bookmarks.GetBoundCopy();
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            markers = new Container { RelativeSizeAxes = Axes.Both },
            hoverTag = new Container
            {
                AutoSizeAxes = Axes.Both,
                Origin = Anchor.BottomCentre,
                Masking = true,
                CornerRadius = 3,
                BorderThickness = 1,
                BorderColour = Colour4.White.Opacity(0.35f),
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.85f) },
                    hoverText = new SpriteText { Font = EditorFont.Regular(16), Colour = Colour4.White, Margin = new MarginPadding { Horizontal = 5, Vertical = 1 } },
                },
            },
        };
        bookmarks.BindCollectionChanged((_, _) => dirty = true);
    }

    private Panels.LayerVisibility? layers;

    /// <summary>Layer visibility (bookmarks can be hidden); null = everything shown.</summary>
    public Panels.LayerVisibility? Layers
    {
        get => layers;
        set
        {
            if (layers != null) layers.Changed -= markDirty;
            layers = value;
            if (layers != null) layers.Changed += markDirty;
            dirty = true;
        }
    }

    private void markDirty() => dirty = true;

    /// <summary>Number of markers currently shown (smoke test).</summary>
    public int MarkerCount => markers.Count;

    /// <summary>The hover label (smoke test: it must sit above the timeline).</summary>
    public Drawable HoverTag => hoverTag;

    /// <summary>Text of the hover label, or null while none shows (smoke test).</summary>
    public string? HoverLabel => hoverTag.Alpha > 0 ? hoverText.Text.ToString() : null;

    protected override void Update()
    {
        base.Update();

        double length = Math.Max(1, clock.LengthMs);
        if (!dirty && builtVersion == session.Version && builtLength == length) return;

        dirty = false;
        builtVersion = session.Version;
        builtLength = length;
        markers.Clear();
        bookmarkSpots.Clear();

        foreach (var b in session.Chart.Beat.Bpm)
            add(session.PulseToMs(b.Y), bpm_red, up: true, 0.45f);

        foreach (var p in session.Chart.Beat.ScrollSpeed)
            add(session.PulseToMs(p.Y), scroll_green, up: true, 0.35f);

        // the chart is the truth (label, colour); the ms list only tells us when Ctrl+B changed it
        foreach (var b in layers?.IsVisible(Panels.EditorLayer.Bookmarks) == false ? new List<SDVXEditor.Core.Charts.PulseValue<SDVXEditor.Core.Charts.Bookmark>>() : session.Chart.Editor.Bookmarks)
        {
            double ms = session.PulseToMs(b.Y);
            if (add(ms, BookmarkTools.ColourOf(b.Value), up: false, 0.45f))
                bookmarkSpots.Add(((float)(ms / length), BookmarkTools.HoverText(b.Value, ms)));
        }

        var bgm = session.Chart.Audio.Bgm;
        if (bgm.PreviewOffsetMs > 0)
        {
            markers.Add(new Box
            {
                RelativePositionAxes = Axes.X,
                X = (float)(bgm.PreviewOffsetMs / length),
                RelativeSizeAxes = Axes.Y,
                Width = 2,
                Origin = Anchor.TopCentre,
                Colour = preview_yellow,
            });
        }

        bool add(double ms, Colour4 colour, bool up, float height)
        {
            if (ms < 0 || ms > length) return false;

            markers.Add(new Box
            {
                RelativePositionAxes = Axes.X,
                X = (float)(ms / length),
                RelativeSizeAxes = Axes.Y,
                Height = height,
                Width = up ? 2 : 3,
                Anchor = Anchor.CentreLeft,
                Origin = up ? Anchor.BottomCentre : Anchor.TopCentre,
                Colour = colour,
            });
            return true;
        }
    }

    /// <summary>Shows the label of the bookmark nearest to local x (within 6 px), or hides it.</summary>
    public void ShowHoverAt(float localX)
    {
        float best = 6;
        string? text = null;
        float at = 0;
        foreach (var (x, t) in bookmarkSpots)
        {
            float d = Math.Abs(x * DrawWidth - localX);
            if (d > best) continue;
            best = d;
            text = t;
            at = x * DrawWidth;
        }

        if (text == null)
        {
            hoverTag.Alpha = 0;
            return;
        }

        if (hoverText.Text.ToString() != text) hoverText.Text = text;
        // above the timeline, not on it (audit U21: crossed by marker lines and touching the time readout), and kept
        // inside the timeline's width so it never reaches the readout on the left
        float half = hoverTag.DrawWidth / 2;
        hoverTag.X = DrawWidth > 2 * half ? Math.Clamp(at, half, DrawWidth - half) : at;
        hoverTag.Y = -8;
        hoverTag.Alpha = 1;
    }

    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        lastMouseX = ToLocalSpace(e.ScreenSpaceMousePosition).X;
        ShowHoverAt(lastMouseX);
        return base.OnMouseMove(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        hoverTag.Alpha = 0;
        base.OnHoverLost(e);
    }
}
