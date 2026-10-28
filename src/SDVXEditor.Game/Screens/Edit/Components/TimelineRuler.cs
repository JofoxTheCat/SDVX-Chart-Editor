using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// Content of the top timeline (reference 02/03): the song waveform (default <see cref="SpectralWaveform"/>:
/// DJ-style colours per column, kicks red, hats blue; Options → Graphics → Editor can switch to the classic green
/// framework <see cref="WaveformGraph"/>) under a tick ruler (white measures,
/// divisor colours in between), the current time in the centre, "207bpm" tags at BPM points under
/// the ruler. Zoom = the editor's shared zoom (± buttons, Alt+wheel over timeline or playfield); click or drag seeks.
/// </summary>
public partial class TimelineRuler : CompositeDrawable
{
    /// <summary>Pixels per millisecond at zoom 1 (the playfield has its own factor; both follow the one shared zoom).</summary>
    public const float PX_PER_MS = 0.2f;

    private readonly EditorSession session;
    private readonly EditorClock clock;
    private readonly IBindable<float> zoom;
    private readonly IBindable<int> divisor;
    private readonly Container underRuler;
    private readonly Func<double, double> snapMs;

    private Container waveformHolder = null!;
    private WaveformGraph waveform = null!;

    /// <summary>The spectral waveform (smoke test).</summary>
    public SpectralWaveform Spectral { get; private set; } = null!;

    // held in fields: a bound copy is only weakly referenced by its source
    private readonly Bindable<WaveformStyle> waveformStyle = new Bindable<WaveformStyle>(WaveformStyle.Spectral);
    private readonly BindableDouble waveformOpacity = new BindableDouble(0.85);

    private string? audioPath;
    private Func<Waveform?>? loadClassic;
    private bool classicLoaded;
    private QuadBatch ticks = null!;
    private readonly List<Container> tags = new List<Container>();

    private double trackLength;

    /// <summary>Loop playback range in ms (shaded), null = none.</summary>
    public (double Start, double End)? LoopRange { get; set; }

    /// <summary>Layer visibility (bookmarks can be hidden); null = everything shown.</summary>
    public Panels.LayerVisibility? Layers { get; set; }

    public TimelineRuler(EditorSession session, EditorClock clock, IBindable<float> zoom, IBindable<int> divisor, Container underRuler, Func<double, double> snapMs)
    {
        this.session = session;
        this.clock = clock;
        this.zoom = zoom.GetBoundCopy();
        this.divisor = divisor.GetBoundCopy();
        this.underRuler = underRuler;
        this.snapMs = snapMs;
        RelativeSizeAxes = Axes.Both;
        Masking = true;
    }

    [BackgroundDependencyLoader]
    private void load(EditorConfigManager? config)
    {
        if (config != null)
        {
            config.BindWith(EditorSetting.WaveformStyle, waveformStyle);
            config.BindWith(EditorSetting.WaveformOpacity, waveformOpacity);
        }

        InternalChildren = new Drawable[]
        {
            waveformHolder = new Container
            {
                RelativeSizeAxes = Axes.Y,
                Child = waveform = new WaveformGraph
                {
                    RelativeSizeAxes = Axes.Both,
                    Resolution = 0.5f,
                    BaseColour = Colour4.FromHex("3fd13f"),
                    LowColour = Colour4.FromHex("2fb52f"),
                    MidColour = Colour4.FromHex("6be86b"),
                    HighColour = Colour4.FromHex("c9ffc9"),
                    Alpha = 0.85f,
                },
            },
            Spectral = new SpectralWaveform(),
            ticks = new QuadBatch(),
            bookmarkTag = new Container
            {
                AutoSizeAxes = Axes.Both,
                Origin = Anchor.TopLeft,
                Masking = true,
                CornerRadius = 3,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.85f) },
                    bookmarkTagText = new SpriteText { Font = EditorFont.Regular(18), Colour = Colour4.White, Margin = new MarginPadding { Horizontal = 6, Vertical = 1 } },
                },
            },
        };
    }

    private Container bookmarkTag = null!;
    private SpriteText bookmarkTagText = null!;
    private float? hoverX;

    /// <summary>Text of the bookmark label shown on hover, or null (smoke test).</summary>
    public string? BookmarkHoverLabel => bookmarkTag.Alpha > 0 ? bookmarkTagText.Text.ToString() : null;

    /// <summary>Hover position for the bookmark label (the playfield-style x of the ruler); null hides it. Also used by the smoke test.</summary>
    public void SetHover(float? localX) => hoverX = localX;

    /// <summary>
    /// Gives the ruler the song's audio file and length (null path = no audio). The spectral analysis starts in the
    /// background; the classic framework waveform is only decoded (through <paramref name="classicLoader"/>) while the
    /// classic style is selected.
    /// </summary>
    public void SetAudio(string? path, Func<Waveform?> classicLoader, double lengthMs)
    {
        audioPath = path;
        loadClassic = classicLoader;
        classicLoaded = false;
        trackLength = lengthMs;
        waveform.Waveform = null;
        applyWaveformStyle();
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        waveformStyle.BindValueChanged(_ => applyWaveformStyle());
        waveformOpacity.BindValueChanged(_ => applyWaveformStyle(), true);
    }

    private void applyWaveformStyle()
    {
        if (!IsLoaded) return;

        bool spectral = waveformStyle.Value == WaveformStyle.Spectral;
        float opacity = (float)waveformOpacity.Value;

        Spectral.Opacity = opacity;
        Spectral.Alpha = spectral ? 1 : 0;
        if (spectral) Spectral.LoadAudio(audioPath);

        if (!spectral && !classicLoaded && loadClassic != null)
        {
            waveform.Waveform = loadClassic();
            classicLoaded = true;
        }

        waveform.Alpha = opacity;
        waveformHolder.Alpha = !spectral && waveform.Waveform != null ? 1 : 0;
    }

    private float pxPerMs => PX_PER_MS * zoom.Value;

    private float xOf(double ms) => DrawWidth / 2 + (float)((ms - clock.CurrentTime.Value) * pxPerMs);

    protected override void Update()
    {
        base.Update();

        double now = clock.CurrentTime.Value;
        float half = DrawWidth / 2;

        double length = trackLength > 0 ? trackLength : clock.LengthMs;
        waveformHolder.X = xOf(0);
        waveformHolder.Width = (float)(length * pxPerMs);
        Spectral.SetView(now, pxPerMs);

        ticks.Clear();
        double fromMs = now - half / pxPerMs, toMs = now + half / pxPerMs;
        if (LoopRange is { } loop && loop.End > fromMs && loop.Start < toMs)
        {
            float x0 = Math.Max(0, xOf(loop.Start)), x1 = Math.Min(DrawWidth, xOf(loop.End));
            ticks.AddRect(x0, 0, x1 - x0, DrawHeight, Colour4.FromHex("4fdb4f").Opacity(0.18f));
            ticks.AddRect(xOf(loop.Start) - 1, 0, 2, DrawHeight, Colour4.FromHex("4fdb4f"));
            ticks.AddRect(xOf(loop.End) - 1, 0, 2, DrawHeight, Colour4.FromHex("4fdb4f"));
        }

        drawTicks(fromMs, toMs);
        drawTags(fromMs, toMs);
        drawBookmarks(fromMs, toMs);
    }

    /// <summary>Bookmarks: a full-height line in the bookmark's colour with a flag at the top; label when hovered (within 6 px).</summary>
    private void drawBookmarks(double fromMs, double toMs)
    {
        float h = DrawHeight;
        string? label = null;
        float labelX = 0, best = 6;

        if (Layers?.IsVisible(Panels.EditorLayer.Bookmarks) == false)
        {
            bookmarkTag.Alpha = 0;
            return;
        }

        foreach (var b in session.Chart.Editor.Bookmarks)
        {
            double ms = session.PulseToMs(b.Y);
            if (ms < fromMs - 50 || ms > toMs + 50) continue;

            float x = xOf(ms);
            var colour = Dialogs.BookmarkTools.ColourOf(b.Value);
            ticks.AddRect(x - 1.5f, 0, 3, h, colour.Opacity(0.9f));
            ticks.AddRect(x - 1.5f, 0, 12, 8, colour);

            if (hoverX is float hx && Math.Abs(hx - x) <= best)
            {
                best = Math.Abs(hx - x);
                label = Dialogs.BookmarkTools.HoverText(b.Value, ms);
                labelX = x;
            }
        }

        if (label == null)
        {
            bookmarkTag.Alpha = 0;
            return;
        }

        if (bookmarkTagText.Text.ToString() != label) bookmarkTagText.Text = label;
        bookmarkTag.X = Math.Min(labelX + 6, Math.Max(0, DrawWidth - bookmarkTag.DrawWidth));
        bookmarkTag.Y = 10;
        bookmarkTag.Alpha = 1;
    }

    protected override bool OnMouseMove(MouseMoveEvent e)
    {
        hoverX = ToLocalSpace(e.ScreenSpaceMousePosition).X;
        return base.OnMouseMove(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        hoverX = null;
        base.OnHoverLost(e);
    }

    private void drawTicks(double fromMs, double toMs)
    {
        var timing = session.Timing;
        long pLo = Math.Max(0, (long)Math.Floor(session.MsToPulse(fromMs)));
        long pHi = (long)Math.Ceiling(session.MsToPulse(toMs));
        if (pHi < 0) return;

        int div = Math.Max(1, divisor.Value);
        float h = DrawHeight;

        for (int m = timing.MeasureAt(pLo); ; m++)
        {
            long start = timing.MeasureStart(m);
            if (start > pHi) break;

            long len = timing.MeasureLength(m);
            long beatLen = timing.BeatLength(m);
            int count = (int)Math.Round((double)len * div / beatLen);
            double beatPx = (session.PulseToMs(start + beatLen) - session.PulseToMs(start)) * pxPerMs;

            for (int k = 0; k < count; k++)
            {
                bool beat = k % div == 0;
                // Zoomed out, the finest ticks go first: a tick stays while its own snap level
                // (1/2, 1/4, …) is still at least 4 px apart.
                if (!beat && beatPx * gcd(k % div, div) / div < 4) continue;

                long pulse = start + (long)Math.Round((double)k * beatLen / div, MidpointRounding.AwayFromZero);
                if (pulse < pLo) continue;

                // Ticks are 2 design px wide: the 1920x1080 design space is scaled down to the window,
                // and 1 px lines at fractional positions dropped out of the rasteriser (ticks "missing").
                float x = xOf(session.PulseToMs(pulse));
                if (k == 0)
                    ticks.AddRect(x - 1, 0, 2, h, Colour4.White);
                else if (beat)
                    ticks.AddRect(x - 1, h * 0.35f, 2, h * 0.65f, Colour4.White.Opacity(0.85f));
                else
                    ticks.AddRect(x - 1, h * 0.6f, 2, h * 0.4f, EditorColours.Divisor.ForTick(k, div));
            }
        }
    }

    private static int gcd(int a, int b) => b == 0 ? a : gcd(b, a % b);

    private void drawTags(double fromMs, double toMs)
    {
        int used = 0;

        foreach (var b in session.Chart.Beat.Bpm)
        {
            double ms = session.PulseToMs(b.Y);
            if (ms < fromMs - 2000 || ms > toMs) continue;

            if (used == tags.Count)
            {
                var tag = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    Y = 2,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White.Opacity(0.85f) },
                        new SpriteText { Font = EditorFont.Regular(20), Colour = Colour4.Black, Margin = new MarginPadding { Horizontal = 6, Vertical = 1 } },
                    },
                };
                tags.Add(tag);
                underRuler.Add(tag);
            }

            var t = tags[used++];
            t.Alpha = 1;
            // the ruler content and the under-ruler strip share their left edge
            t.X = xOf(ms);
            var text = (SpriteText)t.Children[1];
            string s = b.Bpm.ToString("0.##", CultureInfo.InvariantCulture) + "bpm";
            if (text.Text.ToString() != s) text.Text = s;
        }

        for (int i = used; i < tags.Count; i++) tags[i].Alpha = 0;
    }

    // ---- seek --------------------------------------------------------------

    private double dragBaseMs;

    private void seekTo(Vector2 screenSpace)
    {
        float x = ToLocalSpace(screenSpace).X;
        double ms = dragBaseMs + (x - DrawWidth / 2) / pxPerMs;
        ms = Math.Clamp(ms, 0, clock.LengthMs);
        clock.CurrentTime.Value = clock.IsPlaying.Value ? ms : snapMs(ms);
    }

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        dragBaseMs = clock.CurrentTime.Value;
        seekTo(e.ScreenSpaceMousePosition);
        return true;
    }

    protected override bool OnDragStart(DragStartEvent e) => true;

    protected override void OnDrag(DragEvent e) => seekTo(e.ScreenSpaceMousePosition);
}
