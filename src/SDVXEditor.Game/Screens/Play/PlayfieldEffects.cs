using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Play;

/// <summary>
/// The VOX game effects of one frame, as far as the editor previews them (docs/VOX_EFFECTS.md "Preview"). Stage 1
/// ("Overall", the lane) applies to the playfield, stage 0 (the background's stage) to the background; the HUD is never
/// touched — like in game, where it is drawn by other views. ColorConversion is exact (the game's shader per vertex
/// colour); blur, chromatic aberration, glow, noise, CRT, shake, lane slide and lane colours are approximations;
/// SetFrameLabel, BIL_RotZ, Morphing2 and the asset rows are game only.
/// </summary>
public sealed class PlayfieldEffects
{
    public readonly record struct Conversion(int Mode, double Strength);

    /// <summary>Per stage (0 background, 1 lane): the ColorConversion instances in index order, and the other effects.</summary>
    public sealed class Stage
    {
        public List<Conversion> Conversions { get; } = new();
        public double Chromatic { get; set; }
        public double Radial { get; set; }
        public Vector2 RadialCentre { get; set; } = new(0.5f, 0.5f);
        public double Noise { get; set; }
        public double NoisePercent { get; set; } = 50;
        public bool Crt { get; set; }
        public double CrtDarkEdge { get; set; }
        public double CrtScanLines { get; set; }
        public double Glow { get; set; }
        public double Shake { get; set; }

        public bool Any => Conversions.Any(c => c.Strength > 0) || Chromatic > 0 || Radial > 0 || Noise > 0 || Crt || Glow > 0 || Shake > 0;
    }

    public Stage Background { get; } = new();
    public Stage Lane { get; } = new();

    /// <summary>LaneY in its own units (1024 = moved fully out).</summary>
    public double LaneY { get; private set; }

    public uint? LaneColour { get; private set; }
    public uint? LaneClearColour { get; private set; }

    public static readonly PlayfieldEffects None = new();

    /// <summary>The effects at song time <paramref name="ms"/>.</summary>
    public static PlayfieldEffects At(VoxEffectEvaluator e, double ms)
    {
        var fx = new PlayfieldEffects
        {
            LaneY = e.Command("LaneY", ms),
            LaneColour = e.LaneColour(ms),
            LaneClearColour = e.LaneClearColour(ms),
        };
        if (!e.HasPostEffects) return fx;

        var values = e.PostValues(ms).ToList();
        foreach (var group in values.GroupBy(v => v.Key.Stage))
        {
            var st = group.Key == 0 ? fx.Background : fx.Lane;
            double get(string effect, string param, int index = 0) =>
                group.FirstOrDefault(v => v.Key.Effect == effect && v.Key.Param == param && v.Key.Index == index) is { Key.Effect: not null } kv
                    ? kv.Value
                    : PostEffects.DefaultOf(effect, param);

            foreach (int index in group.Where(v => v.Key.Effect == PostEffects.ColorConversion).Select(v => v.Key.Index).Distinct().Order())
                st.Conversions.Add(new Conversion((int)Math.Clamp(get(PostEffects.ColorConversion, "Mode", index), 0, 4), get(PostEffects.ColorConversion, "Strength", index)));
            st.Chromatic = Math.Max(0, get(PostEffects.ChromaticAberration, "Strength"));
            st.Radial = Math.Max(0, get(PostEffects.RadicalBlur, "Strength"));
            st.RadialCentre = new Vector2((float)get(PostEffects.RadicalBlur, "CenterPositionX"), (float)get(PostEffects.RadicalBlur, "CenterPositionY"));
            st.Noise = Math.Max(0, get(PostEffects.SimpleNoise, "WholeNoiseStrength"));
            st.NoisePercent = get(PostEffects.SimpleNoise, "RandomNoisePercent");
            st.Crt = get(PostEffects.CrtMonitorEffect, "Active") >= 0.5;
            st.CrtDarkEdge = get(PostEffects.CrtMonitorEffect, "DarkEdge");
            st.CrtScanLines = get(PostEffects.CrtMonitorEffect, "ScanLineStrength");
            st.Glow = Math.Max(0, get(PostEffects.LuminousEffect, "Strength")) * Math.Max(0.1, get(PostEffects.LuminousEffect, "RenderStrengthAdd"));
            st.Shake = Math.Max(0, get(PostEffects.RandomShake, "none"));
        }

        return fx;
    }

    // ---- colour ---------------------------------------------------------------------

    /// <summary>A colour through the stage's ColorConversion instances in order (the game's shader maths).</summary>
    public static Colour4 Convert(Stage st, Colour4 c)
    {
        float r = c.R, g = c.G, b = c.B;
        foreach (var cv in st.Conversions)
            (r, g, b) = ColorConversionMath.Apply(cv.Mode, cv.Strength, r, g, b);
        return new Colour4(r, g, b, c.A);
    }

    public static ColourInfo Convert(Stage st, ColourInfo c)
    {
        if (st.Conversions.Count == 0) return c;
        return new ColourInfo
        {
            TopLeft = Convert(st, (Colour4)c.TopLeft.SRGB),
            TopRight = Convert(st, (Colour4)c.TopRight.SRGB),
            BottomLeft = Convert(st, (Colour4)c.BottomLeft.SRGB),
            BottomRight = Convert(st, (Colour4)c.BottomRight.SRGB),
        };
    }

    public static Colour4 Argb(uint c) => new((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24));

    /// <summary>LANECOL as a multiplier of a lane colour (xFFFFFFFF = unchanged, alpha 0 = lane gone).</summary>
    public static Colour4 Tint(Colour4 c, uint? laneColour)
    {
        if (laneColour is not { } lc) return c;
        var t = Argb(lc);
        return new Colour4(c.R * t.R, c.G * t.G, c.B * t.B, c.A * t.A);
    }

    // ---- ghost passes -----------------------------------------------------------------

    /// <summary>
    /// Approximated screen effects of a stage, drawn into <paramref name="ghost"/> (additive) and <paramref name="overlay"/>
    /// (alpha) from the quads of <paramref name="main"/>: radial blur = copies scaled about the centre, chromatic
    /// aberration = red / green copies scaled like the shader's UVs (1/(1−2s), 1/(1−s)), glow = a brightened copy,
    /// noise = random grey specks, CRT = scan lines and dark edges.
    /// </summary>
    public static void DrawGhosts(Stage st, QuadBatch main, QuadBatch ghost, QuadBatch overlay, Vector2 size, Random rng, int count)
    {
        var quads = main.Quads;
        int n = Math.Min(count, quads.Count);
        if (st.Radial > 0)
        {
            var centre = new Vector2(size.X * st.RadialCentre.X, size.Y * st.RadialCentre.Y);
            const int copies = 4;
            for (int k = 1; k <= copies; k++)
            {
                float scale = 1 + (float)(st.Radial * 0.35 * k / copies);
                float alpha = (float)Math.Min(0.5, st.Radial * 1.2) / (k + 1);
                for (int i = 0; i < n; i++) ghost.Add(scaled(quads[i].Quad, centre, scale), quads[i].Colour.MultiplyAlpha(alpha));
            }
        }

        if (st.Chromatic > 0)
        {
            var centre = size / 2;
            float s = (float)Math.Min(st.Chromatic, 0.2);
            for (int i = 0; i < n; i++)
            {
                var c = (Colour4)quads[i].Colour.AverageColour.SRGB;
                ghost.Add(scaled(quads[i].Quad, centre, 1 / Math.Max(0.5f, 1 - 2 * s)), new Colour4(c.R, 0, 0, c.A * 0.55f));
                ghost.Add(scaled(quads[i].Quad, centre, 1 / Math.Max(0.5f, 1 - s)), new Colour4(0, c.G, 0, c.A * 0.4f));
            }
        }

        if (st.Glow > 0)
            for (int i = 0; i < n; i++) ghost.Add(quads[i].Quad, quads[i].Colour.MultiplyAlpha((float)Math.Min(0.8, st.Glow * 1.5)));

        if (st.Noise > 0)
        {
            int specks = (int)Math.Min(1500, st.Noise / 32768 * 900 * Math.Max(0.2, st.NoisePercent / 50));
            for (int i = 0; i < specks; i++)
            {
                float x = (float)rng.NextDouble() * size.X, y = (float)rng.NextDouble() * size.Y, v = (float)rng.NextDouble();
                overlay.AddRect(x, y, 2 + v * 3, 2 + v * 2, new Colour4(v, v, v, 0.55f));
            }
        }

        if (st.Crt)
        {
            float scan = (float)Math.Clamp(st.CrtScanLines, 0, 1);
            if (scan > 0)
                for (float y = 0; y < size.Y; y += 4) overlay.AddRect(0, y, size.X, 1.5f, Colour4.Black.Opacity(scan * 0.6f));
            float dark = (float)Math.Clamp(st.CrtDarkEdge, 0, 1);
            if (dark > 0)
            {
                float edge = size.X * 0.18f;
                overlay.Add(new Quad(0, 0, edge, size.Y), ColourInfo.GradientHorizontal(Colour4.Black.Opacity(dark), Colour4.Black.Opacity(0)));
                overlay.Add(new Quad(size.X - edge, 0, edge, size.Y), ColourInfo.GradientHorizontal(Colour4.Black.Opacity(0), Colour4.Black.Opacity(dark)));
                overlay.Add(new Quad(0, 0, size.X, edge * 0.6f), ColourInfo.GradientVertical(Colour4.Black.Opacity(dark), Colour4.Black.Opacity(0)));
                overlay.Add(new Quad(0, size.Y - edge * 0.6f, size.X, edge * 0.6f), ColourInfo.GradientVertical(Colour4.Black.Opacity(0), Colour4.Black.Opacity(dark)));
            }
        }
    }

    private static Quad scaled(Quad q, Vector2 c, float s) =>
        new(c + (q.TopLeft - c) * s, c + (q.TopRight - c) * s, c + (q.BottomLeft - c) * s, c + (q.BottomRight - c) * s);

    /// <summary>A random shake offset of amplitude <paramref name="amplitude"/> (official values 15..20, read as design pixels).</summary>
    public static Vector2 ShakeOffset(double amplitude, Random rng) =>
        amplitude <= 0 ? Vector2.Zero : new Vector2((float)((rng.NextDouble() * 2 - 1) * amplitude), (float)((rng.NextDouble() * 2 - 1) * amplitude));
}
