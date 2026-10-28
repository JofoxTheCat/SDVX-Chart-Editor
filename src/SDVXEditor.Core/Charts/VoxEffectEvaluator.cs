namespace SDVXEditor.Core.Charts;

/// <summary>The value of one post-effect parameter (key: stage, effect, instance, parameter).</summary>
public readonly record struct PostEffectKey(int Stage, string Effect, int Index, string Param);

/// <summary>
/// Post effects and lane commands at a song time, the way the game's consumers read the rows (converter CLAUDE.md §6
/// "What the ∇ DLL actually reads"): a #POSTEFFECT row tweens its parameter from → to over its length (ticks or ms;
/// the type field is never read), then holds; before any row the parameter has its default. SPCONTROLER tweens start
/// from p1 when <c>type &amp; 0xF == 2</c>, else from the current value. LANECOL / LANECLEARCOL are instant.
/// Pure and time-based, so the playtest and the camera-tab preview share it.
/// </summary>
public sealed class VoxEffectEvaluator
{
    private readonly record struct Span(double Start, double End, double From, double To);

    private readonly Dictionary<PostEffectKey, List<Span>> post = new();
    private readonly Dictionary<string, List<Span>> tweens = new();
    private readonly List<(double Ms, uint Argb)> laneCol = new();
    private readonly List<(double Ms, uint Argb)> laneClearCol = new();
    private readonly List<(long From, long To)> barOff = new();
    private readonly List<long> bars = new();

    public Chart Chart { get; }

    /// <summary>Rows of the chart, timed with <paramref name="pulseToMs"/> (the chart's tempo map).</summary>
    public VoxEffectEvaluator(Chart chart, Func<double, double> pulseToMs)
    {
        Chart = chart;
        var vox = chart.VoxExtras;

        foreach (var r in vox.PostEffects.OrderBy(r => r.Y))
        {
            double start = pulseToMs(r.Y);
            double end = r.LengthMs is { } ms ? start + Math.Max(0, ms) : pulseToMs(r.Y + Math.Max(0, r.Length));
            var key = new PostEffectKey(r.Stage, r.Name, r.Index, r.Param);
            if (!post.TryGetValue(key, out var list)) post[key] = list = new List<Span>();
            list.Add(new Span(start, end, r.From, r.To));
        }

        foreach (string name in VoxCommands.TweenKeys)
        {
            var list = new List<Span>();
            double current = 0;
            foreach (var r in vox.Commands.Where(c => c.Name == name).OrderBy(c => c.Y))
            {
                double start = pulseToMs(r.Y), end = pulseToMs(r.Y + Math.Max(0, r.Length));
                double to = VoxToken.Number(r.P2) ?? 0;
                double from = (r.Type & 0xF) == 2 ? VoxToken.Number(r.P1) ?? 0 : valueBefore(list, start, current);
                list.Add(new Span(start, end, from, to));
                current = to;
            }

            if (list.Count > 0) tweens[name] = list;
        }

        foreach (var r in vox.Commands.Where(c => c.Name == "SpecialN").OrderBy(c => c.Y))
        {
            if (VoxToken.Argb(r.P2) is not { } argb) continue;
            if (r.P1 == "LANECOL") laneCol.Add((pulseToMs(r.Y), argb));
            else if (r.P1 == "LANECLEARCOL") laneClearCol.Add((pulseToMs(r.Y), argb));
        }

        // BAROFF works per measure: ON opens a range at its measure, OFF closes it at its measure
        long? open = null;
        foreach (var r in vox.Commands.Where(c => c.Name is "BAROFF").OrderBy(c => c.Y))
        {
            long m = chart.Beat.Bpm.Count >= 0 ? r.Y : 0;
            if (r.P1 == "ON") open ??= m;
            else if (r.P1 == "OFF" && open is { } o)
            {
                barOff.Add((o, m));
                open = null;
            }
        }

        if (open is { } still) barOff.Add((still, long.MaxValue));
        bars.AddRange(vox.Commands.Where(c => c.Name == "BAR").Select(c => c.Y));
    }

    private static double valueBefore(List<Span> list, double t, double fallback)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var s = list[i];
            if (s.Start > t) continue;
            return valueIn(s, t);
        }

        return fallback;
    }

    private static double valueIn(Span s, double t)
    {
        if (t >= s.End || s.End <= s.Start) return s.To;
        return s.From + (s.To - s.From) * Math.Clamp((t - s.Start) / (s.End - s.Start), 0, 1);
    }

    private static double? at(List<Span> list, double t)
    {
        double? v = null;
        foreach (var s in list)
        {
            if (s.Start > t) break;
            v = valueIn(s, t);
        }

        return v;
    }

    /// <summary>A post-effect parameter at <paramref name="ms"/> (its default before the first row).</summary>
    public double Post(int stage, string effect, string param, double ms, int index = 0)
        => post.TryGetValue(new PostEffectKey(stage, PostEffects.Normalise(effect), index, param), out var list) && at(list, ms) is { } v
            ? v
            : PostEffects.DefaultOf(effect, param);

    /// <summary>Every parameter that has rows, with its value at <paramref name="ms"/>.</summary>
    public IEnumerable<(PostEffectKey Key, double Value)> PostValues(double ms)
    {
        foreach (var (key, list) in post)
            yield return (key, at(list, ms) ?? PostEffects.DefaultOf(key.Effect, key.Param));
    }

    public bool HasPostEffects => post.Count > 0;

    /// <summary>LaneY / BIL_RotZ / Morphing2 value at <paramref name="ms"/> in the row's own units (0 before any row).</summary>
    public double Command(string name, double ms) => tweens.TryGetValue(name, out var list) ? at(list, ms) ?? 0 : 0;

    /// <summary>The last LANECOL at or before <paramref name="ms"/> (ARGB), or null.</summary>
    public uint? LaneColour(double ms) => lastAt(laneCol, ms);

    /// <summary>The last LANECLEARCOL at or before <paramref name="ms"/> (ARGB), or null.</summary>
    public uint? LaneClearColour(double ms) => lastAt(laneClearCol, ms);

    private static uint? lastAt(List<(double Ms, uint Argb)> list, double ms)
    {
        uint? v = null;
        foreach (var (t, c) in list)
        {
            if (t > ms) break;
            v = c;
        }

        return v;
    }

    /// <summary>
    /// True when the bar line of the measure starting at <paramref name="measureStartPulse"/> is hidden by BAROFF
    /// (ranges are whole measures: ON at measure m … OFF at measure n hides m..n-1) and no BAR forces one there.
    /// </summary>
    public bool BarHidden(long measureStartPulse, Func<long, long> measureStartOf)
    {
        if (bars.Contains(measureStartPulse)) return false;
        foreach (var (from, to) in barOff)
            if (measureStartPulse >= measureStartOf(from) && (to == long.MaxValue || measureStartPulse < measureStartOf(to))) return true;
        return false;
    }

    /// <summary>Extra bar lines (BAR rows), pulses.</summary>
    public IReadOnlyList<long> ForcedBars => bars;
}

/// <summary>
/// The ColorConversion shaders of the game (decompiled HLSL, converter CLAUDE.md §6) as a function of one colour, so the
/// preview can apply them per vertex: <c>out = raw·(1−p) + dest·p</c>, p = Strength/100 (mode 4: Strength = hue degrees).
/// </summary>
public static class ColorConversionMath
{
    /// <summary>Applies mode <paramref name="mode"/> at <paramref name="strength"/> to an sRGB colour (0..1 channels).</summary>
    public static (float R, float G, float B) Apply(int mode, double strength, float r, float g, float b)
    {
        if (strength <= 0) return (r, g, b);
        mode = Math.Clamp(mode, 0, 4);
        if (mode == 4) return hue(r, g, b, Math.Clamp(strength, 0, 360));

        double p = Math.Clamp(strength, 0, 100) * 0.01;
        double dr, dg, db;
        switch (mode)
        {
            case 0:
            {
                double avg = 0.298912 * r + 0.586611 * g + 0.114478 * b;
                dr = dg = db = avg;
                break;
            }
            case 1:
                dr = Math.Min(1, 0.393 * r + 0.769 * g + 0.189 * b);
                dg = Math.Min(1, 0.349 * r + 0.686 * g + 0.168 * b);
                db = Math.Min(1, 0.272 * r + 0.534 * g + 0.131 * b);
                break;
            case 2:
                dr = 1 - r;
                dg = 1 - g;
                db = 1 - b;
                break;
            default:
            {
                double nr = 1 - r, ng = 1 - g, nb = 1 - b;
                double mx = Math.Max(nr, Math.Max(ng, nb)), mn = Math.Min(nr, Math.Min(ng, nb));
                dr = mx + mn - nr;
                dg = mx + mn - ng;
                db = mx + mn - nb;
                break;
            }
        }

        return ((float)(r * (1 - p) + dr * p), (float)(g * (1 - p) + dg * p), (float)(b * (1 - p) + db * p));
    }

    /// <summary>RGB → HLS, H += degrees, back (the shader's mode 4).</summary>
    private static (float, float, float) hue(float r, float g, float b, double degrees)
    {
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
        double l = (mx + mn) / 2;
        if (mx - mn < 1e-6) return (r, g, b);
        double d = mx - mn;
        double s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
        double h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = (h * 60 + degrees) % 360;
        if (h < 0) h += 360;
        h /= 360;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, pp = 2 * l - q;
        return ((float)channel(pp, q, h + 1.0 / 3), (float)channel(pp, q, h), (float)channel(pp, q, h - 1.0 / 3));
    }

    private static double channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }
}
