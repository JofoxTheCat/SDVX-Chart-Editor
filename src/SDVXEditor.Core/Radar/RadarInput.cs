using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Radar;

/// <summary>Where a chart came from; decides how pulses map back onto radar.py's tick grid.</summary>
public enum RadarSource
{
    /// <summary>
    /// A .ksh. radar.py sees the raw ksh lines: positions are floored to 48 ticks per beat, a slam is two
    /// points one ksh line (1/32 note = 6 ticks) apart, and a plain (not ranged) header <c>t=</c> is the BPM at 0.
    /// </summary>
    Ksh,

    /// <summary>A .kson (or a VOX / an in-memory chart): positions are rounded, slams are two points on one tick.</summary>
    Kson,
}

/// <summary>
/// The chart as radar.py's <c>parsed</c> dict sees it: 48 ticks per quarter note (ksh2vox <c>TPB</c>),
/// BT/FX lanes as (tick, length), laser sections as lists of (tick, value 0..50), BPM events and the end tick.
/// Built from the editor model so that the C# port gives radar.py's numbers on the same file.
/// </summary>
public sealed class RadarInput
{
    /// <summary>ksh2vox <c>TPB</c>.</summary>
    public const int TicksPerBeat = 48;

    /// <summary>One tick in kson pulses (240 pulses per quarter / 48).</summary>
    public const long PulsesPerTick = Timebase.PulsesPerQuarter / TicksPerBeat;

    /// <summary>radar.py <c>SLAM_MAX_TICKS</c>: a value change within 1/32 note.</summary>
    public const int SlamMaxTicks = TicksPerBeat / 8;

    /// <summary>ksh2vox <c>END_HINT_MAX_MEASURES</c>.</summary>
    public const int EndHintMaxMeasures = 64;

    /// <summary>BT A–D.</summary>
    public List<(long Tick, long Length)>[] Bt { get; } = { new(), new(), new(), new() };

    /// <summary>FX L/R.</summary>
    public List<(long Tick, long Length)>[] Fx { get; } = { new(), new() };

    /// <summary>Per laser lane: sections, each a list of (tick, value 0..50) in authored order.</summary>
    public List<List<(long Tick, double Value)>>[] Laser { get; } = { new(), new() };

    /// <summary>BPM events (tick, bpm), unsorted like ksh2vox builds them.</summary>
    public List<(long Tick, double Bpm)> Bpm { get; } = new();

    public long EndTick { get; set; }

    /// <summary>Builds the input from an editor chart read from a file of <paramref name="source"/>'s format.</summary>
    public static RadarInput FromChart(Chart chart, RadarSource source)
    {
        var input = new RadarInput();
        bool ksh = source == RadarSource.Ksh;

        // ksh: Python counts lines (floor(i·192/n)); the reader rounds i·960/n pulses → floor(pulse/5)
        // gives the same tick. kson: ksh2vox _kson_tick = round(pulse·48/240), Python round = half-to-even.
        long tick(long pulse) => ksh ? FloorDiv(pulse, PulsesPerTick) : (long)Math.Round((double)pulse / PulsesPerTick, MidpointRounding.ToEven);

        for (int lane = 0; lane < 4; lane++)
            foreach (var n in chart.Notes.Bt[lane])
                input.Bt[lane].Add(note(n));
        for (int lane = 0; lane < 2; lane++)
            foreach (var n in chart.Notes.Fx[lane])
                input.Fx[lane].Add(note(n));

        (long, long) note(Note n)
        {
            long t = tick(n.Y);
            // ksh: end line minus start line; kson: _kson_tick(len) on its own
            long len = n.Length <= 0 ? 0 : ksh ? tick(n.Y + n.Length) - t : tick(n.Length);
            return (t, len);
        }

        for (int lane = 0; lane < 2; lane++)
        {
            foreach (var sec in chart.Notes.Lasers[lane])
            {
                var pts = new List<(long, double)>();
                for (int i = 0; i < sec.Points.Count; i++)
                {
                    var p = sec.Points[i];
                    long t = tick(sec.Y + p.Ry);
                    pts.Add((t, value(p.V)));
                    if (p.Vf is not double vf || vf == p.V) continue;

                    long t2 = t;
                    if (ksh)
                    {
                        // the reader merged two ksh lines into one slam on the first line; radar.py still sees
                        // the second line, 1/32 note later (every library slam is exactly that long)
                        t2 = t + SlamMaxTicks;
                        if (i + 1 < sec.Points.Count)
                            t2 = Math.Min(t2, Math.Max(t, tick(sec.Y + sec.Points[i + 1].Ry) - 1));
                    }
                    pts.Add((t2, value(vf)));
                }
                if (pts.Count > 0) input.Laser[lane].Add(pts);
            }
        }

        // ksh values are line characters 0..50; undo the /50 exactly so equality and "< 25" match Python
        double value(double v) => ksh ? Math.Round(v * 50) : v * 50.0;

        // convert_chart (ksh2vox ≥ 0.17.1): a plain header t= is the tempo at tick 0, listed before the inline
        // tempos so an inline t= on the first line wins the same-tick dedupe; a ranged one (t=35-321) is display
        // only, the first inline tempo then applies from tick 0, and only a chart without any inline tempo falls
        // back to the range's first number.
        var header = ksh ? HeaderBpm(chart.Meta.DispBpm) : null;
        if (header is { Ranged: false } plain)
            input.Bpm.Add((0, plain.Bpm));
        foreach (var b in chart.Beat.Bpm.OrderBy(b => b.Y))
            input.Bpm.Add((tick(b.Y), b.Bpm));
        if (header is { Ranged: true } && input.Bpm.Count > 0 && input.Bpm[0].Tick > 0)
            input.Bpm[0] = (0, input.Bpm[0].Bpm);
        if (input.Bpm.Count == 0) input.Bpm.Add((0, header?.Bpm ?? 120));

        input.EndTick = ksh ? kshEnd(chart, tick) : ksonEnd(chart, input, tick);
        return input;
    }

    /// <summary>ksh: the end of the last measure (the reader's <see cref="Chart.EndPulse"/>).</summary>
    private static long kshEnd(Chart chart, Func<long, long> tick)
        => tick(chart.EndPulse ?? measureEndAfter(chart, chart.LastEventPulse()));

    /// <summary>
    /// kson (convert_kson): measures are added until one ends after the last note/laser tick; an editor
    /// <c>end_pulse</c> at or after that tick replaces it (bounded to 64 more measures).
    /// </summary>
    private static long ksonEnd(Chart chart, RadarInput input, Func<long, long> tick)
    {
        long last = 0;
        foreach (var lane in input.Bt.Concat(input.Fx))
            foreach (var (t, len) in lane)
                last = Math.Max(last, t + len);
        foreach (var lane in input.Laser)
            foreach (var s in lane)
                last = Math.Max(last, s.Max(p => p.Tick));

        long end = 0;
        int mi = 0;
        while (true)
        {
            end += measureTicks(chart, mi++);
            if (end > last) break;
        }

        if (chart.EndPulse is long hint && tick(hint) >= last)
            end = Math.Min(tick(hint), last + TicksPerBeat * 4 * EndHintMaxMeasures);
        return end;
    }

    private static long measureEndAfter(Chart chart, long pulse)
    {
        long end = 0;
        int mi = 0;
        while (end * PulsesPerTick <= pulse) end += measureTicks(chart, mi++);
        return end * PulsesPerTick;
    }

    /// <summary><c>int(TPB·4·num/den)</c> of measure <paramref name="index"/>.</summary>
    private static long measureTicks(Chart chart, int index)
    {
        var sig = chart.Beat.TimeSignatures.LastOrDefault(s => s.MeasureIndex <= index);
        int n = sig?.Numerator ?? 4, d = sig?.Denominator ?? 4;
        if (n <= 0 || d <= 0) { n = 4; d = 4; }
        return (long)((double)TicksPerBeat * 4 * n / d);
    }

    /// <summary>
    /// ksh2vox <c>_ksh_header_bpm</c>: <c>float(t.split("-")[0])</c> of the header and whether it is a range
    /// (<c>t=35-321</c> → (35, true)); null when that is not a number.
    /// </summary>
    public static (double Bpm, bool Ranged)? HeaderBpm(string? disp)
    {
        if (string.IsNullOrWhiteSpace(disp)) return null;
        string[] parts = disp.Split('-');
        return double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
            ? (v, parts.Length > 1)
            : null;
    }

    private static long FloorDiv(long a, long b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
