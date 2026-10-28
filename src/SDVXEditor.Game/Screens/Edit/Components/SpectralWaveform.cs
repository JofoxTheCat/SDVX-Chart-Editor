using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using SDVXEditor.Core.Audio;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// The timeline waveform in the reference editor's look (DJ-software RGB): one bar per <see cref="COLUMN_PX"/>
/// columns, mirrored around the centre line. Height = the loudest 10 ms RMS the column covers
/// (<see cref="SpectralPalette.Height"/>: kicks and snares stand out as lobes with gaps between beats, quiet parts keep a
/// floor), colour = the column's band balance (<see cref="SpectralPalette.Hue"/>: low red, mid green, high blue),
/// dimmer when quiet. Columns are anchored to time (column k covers [k·w, (k+1)·w) ms), so they scroll smoothly
/// instead of shimmering; zoomed out a column takes the loudest window of its stretch (a kick stays a spike),
/// zoomed in single transients separate.
/// <para>
/// All columns go into one <see cref="QuadBatch"/> (one draw node). The batch is refilled only when the view
/// (time, zoom, size, opacity) or the analysis changes, into reused lists — nothing is allocated per frame.
/// The analysis comes from <see cref="SpectralAnalysisCache"/> (background, once per audio file).
/// </para>
/// </summary>
public partial class SpectralWaveform : CompositeDrawable
{
    /// <summary>Share of grey mixed into every column: the hue still reads, but no column is a fully saturated block.</summary>
    public const float DESATURATION = 0.4f;

    /// <summary>The drawn colour of a column: its hue mixed with grey of the same brightness.</summary>
    public static Colour4 Softened(SDVXEditor.Core.Audio.SpectralColumn c, float alpha)
    {
        float m = Math.Max(c.R, Math.Max(c.G, c.B)) * 0.92f;
        float k = 1 - DESATURATION;
        return new Colour4(c.R * k + m * DESATURATION, c.G * k + m * DESATURATION, c.B * k + m * DESATURATION, alpha);
    }

    /// <summary>Column width in design pixels.</summary>
    public const float COLUMN_PX = 3;

    /// <summary>Share of the half height a full-scale column uses.</summary>
    private const float height_fill = 0.95f;

    private readonly QuadBatch batch;

    /// <summary>The analysis drawn (null while loading, without audio, or when the file could not be decoded).</summary>
    public SpectralAnalysis? Analysis { get; private set; }

    /// <summary>The audio file asked for last.</summary>
    public string? AudioPath { get; private set; }

    /// <summary>True while the analysis of <see cref="AudioPath"/> runs.</summary>
    public bool IsLoading { get; private set; }

    private float opacity = 0.85f;

    /// <summary>Alpha of every bar (the options' waveform opacity).</summary>
    public float Opacity
    {
        get => opacity;
        set
        {
            if (opacity == value) return;
            opacity = value;
            dirty = true;
        }
    }

    private double centreMs;
    private float pxPerMs = 0.2f;
    private bool dirty = true;
    private osuTK.Vector2 lastSize;

    /// <summary>One drawn column (smoke test / diagnostics): left x, start time, value.</summary>
    public readonly record struct DrawnColumn(float X, double Ms, SpectralColumn Value);

    private DrawnColumn[] drawn = new DrawnColumn[256];
    private int drawnCount;

    /// <summary>The columns of the last refill, left to right.</summary>
    public ReadOnlySpan<DrawnColumn> DrawnColumns => drawn.AsSpan(0, drawnCount);

    public SpectralWaveform()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = batch = new QuadBatch();
    }

    /// <summary>Starts (or reuses) the analysis of <paramref name="path"/>; null clears the waveform.</summary>
    public void LoadAudio(string? path)
    {
        if (path == AudioPath && (Analysis != null || IsLoading)) return;

        AudioPath = path;
        Analysis = null;
        dirty = true;

        if (path == null || !File.Exists(path))
        {
            IsLoading = false;
            return;
        }

        IsLoading = true;
        SpectralAnalysisCache.Get(path).ContinueWith(t =>
        {
            var result = t.IsCompletedSuccessfully ? t.Result : null;
            Schedule(() =>
            {
                if (AudioPath != path) return; // another song was asked for meanwhile
                Analysis = result;
                IsLoading = false;
                dirty = true;
            });
        }, TaskScheduler.Default);
    }

    /// <summary>The time at the horizontal centre and the zoom (pixels per ms), set by the owner every frame.</summary>
    public void SetView(double centre, float pixelsPerMs)
    {
        if (centre == centreMs && pixelsPerMs == pxPerMs) return;
        centreMs = centre;
        pxPerMs = pixelsPerMs;
        dirty = true;
    }

    protected override void Update()
    {
        base.Update();

        if (DrawSize != lastSize)
        {
            lastSize = DrawSize;
            dirty = true;
        }

        if (!dirty) return;
        dirty = false;
        refill();
    }

    private void refill()
    {
        batch.Clear();
        drawnCount = 0;

        var a = Analysis;
        if (a == null || pxPerMs <= 0 || DrawWidth <= 0 || opacity <= 0) return;

        float half = DrawWidth / 2;
        float midY = DrawHeight / 2;
        float maxHalf = DrawHeight / 2 * height_fill;
        double w = COLUMN_PX / pxPerMs;

        double fromMs = centreMs - half / pxPerMs, toMs = centreMs + half / pxPerMs;
        long first = Math.Max(0, (long)Math.Floor(fromMs / w));
        long last = Math.Min((long)Math.Ceiling(a.LengthMs / w) - 1, (long)Math.Ceiling(toMs / w));

        for (long k = first; k <= last; k++)
        {
            double ms = k * w;
            var c = SpectralPalette.Column(a, ms, ms + w);
            // (Softened: a pure tone filled the strip with saturated #00E000, audit P7)
            if (c.Height <= 0) continue;

            float x = half + (float)((ms - centreMs) * pxPerMs);
            float h = Math.Max(1, c.Height * maxHalf);
            batch.AddRect(x, midY - h, COLUMN_PX, 2 * h, Softened(c, opacity));

            if (drawnCount == drawn.Length) Array.Resize(ref drawn, drawn.Length * 2);
            drawn[drawnCount++] = new DrawnColumn(x, ms, c);
        }
    }
}
