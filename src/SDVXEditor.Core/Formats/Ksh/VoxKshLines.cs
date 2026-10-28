using System.Globalization;
using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Formats.Ksh;

/// <summary>
/// The VOX game effects (<see cref="VoxExtrasInfo"/>) in a .ksh: comment lines after the first <c>--</c>, which K-Shoot
/// skips and ksh2vox 0.20.0 reads (docs/VOX_EFFECTS.md):
/// <code>
/// //#vox_pe|stage|index|len|Effect|Param|from|to      len = ticks (1/192 of a measure) or "&lt;n&gt;ms"
/// //#vox_sp|Name|type|len|p1|p2|p3|p4
/// //#vox_bpm|RepresentativeBpm|174                    //#vox_bpm|ConstantScroll|1
/// //#vox_script|id|statement                          one line per statement, at the chart start
/// //#vox_scripted|track|id[,id…]                      at the scripted note's line
/// </code>
/// A LaneY row that is a plain hide/show toggle is written as K-Shoot's own <c>lane_toggle=&lt;len&gt;</c> instead, and
/// <c>bg_no=</c> is a header key. Never in the header: ksh2vox's header loop reads a <c>//…=…</c> line as a key.
/// </summary>
public static class VoxKshLines
{
    public const string Prefix = "//#vox_";
    private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

    private static long ticks(long pulses) => (long)Math.Round(pulses / (double)Timebase.PulsesPerKsh192Unit);
    private static long pulses(long ticks) => ticks * Timebase.PulsesPerKsh192Unit;
    private static string num(double v) => v.ToString("R", inv);

    /// <summary>Reads one <c>//#vox_…</c> line at pulse <paramref name="y"/> into <paramref name="vox"/>. False = not ours.</summary>
    public static bool TryRead(string line, long y, VoxExtrasInfo vox)
    {
        if (!line.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int bar = line.IndexOf('|');
        if (bar < 0) return false;
        string tag = line[3..bar];
        string rest = line[(bar + 1)..];
        try
        {
            switch (tag)
            {
                case "vox_pe":
                {
                    var f = rest.Split('|');
                    if (f.Length != 7) return false;
                    double? ms = null;
                    long len = 0;
                    if (f[2].EndsWith("ms", StringComparison.Ordinal)) ms = double.Parse(f[2][..^2], inv);
                    else len = pulses(long.Parse(f[2], inv));
                    vox.PostEffects.Add(new PostEffectRow(y, f[3], f[4], double.Parse(f[5], inv), double.Parse(f[6], inv), len,
                        int.Parse(f[0], inv), int.Parse(f[1], inv), ms));
                    return true;
                }
                case "vox_sp":
                {
                    var f = rest.Split('|');
                    if (f.Length != 7) return false;
                    vox.Commands.Add(new VoxCommandRow(y, f[0], long.Parse(f[1], inv), pulses(long.Parse(f[2], inv)), f[3], f[4], f[5], f[6]));
                    return true;
                }
                case "vox_bpm":
                {
                    var f = rest.Split('|');
                    if (f.Length != 2) return false;
                    if (f[0] == "RepresentativeBpm") vox.RepresentativeBpm = double.Parse(f[1], inv);
                    else if (f[0] == "ConstantScroll") vox.ConstantScroll = f[1].Trim() != "0";
                    else return false;
                    return true;
                }
                case "vox_script":
                {
                    int b = rest.IndexOf('|');
                    if (b < 0) return false;
                    int id = int.Parse(rest[..b], inv);
                    var script = vox.Scripts.FirstOrDefault(s => s.Id == id);
                    if (script == null) vox.Scripts.Add(script = new VoxScript { Id = id });
                    script.Lines.Add(rest[(b + 1)..]);
                    return true;
                }
                case "vox_scripted":
                {
                    var f = rest.Split('|');
                    if (f.Length != 2) return false;
                    vox.Scripted.Add(new ScriptedNote(y, int.Parse(f[0], inv), f[1].Split(',').Select(s => int.Parse(s, inv))));
                    return true;
                }
                default:
                    return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// ksh <c>lane_toggle=&lt;len&gt;</c> (len in 1/192 of a measure): alternately hides and shows the lane, i.e. LaneY
    /// 0 → 1024 / 1024 → 0 (official rows, VoxCharger and VOX2KSH map it the same way). The direction follows the
    /// LaneY rows already read: after a row ending away from 0 the toggle shows the lane again.
    /// </summary>
    public static bool LaneToggle(VoxExtrasInfo vox, long y, string value)
    {
        if (!double.TryParse(value.Trim(), NumberStyles.Float, inv, out double len) || len < 0) return false;
        bool hidden = vox.Commands.LastOrDefault(c => c.Name == "LaneY" && c.Y <= y) is { } last && Math.Abs(VoxToken.Number(last.P2) ?? 0) > 1e-9;
        string zero = VoxToken.Format(0, VoxValueKind.Float), full = VoxToken.Format(1024, VoxValueKind.Float);
        vox.Commands.Add(new VoxCommandRow(y, "LaneY", 2, pulses((long)Math.Round(len)), hidden ? full : zero, hidden ? zero : full, zero, zero));
        return true;
    }

    /// <summary>True when <paramref name="row"/> is exactly what <see cref="LaneToggle"/> reads from a lane_toggle line.</summary>
    public static bool IsToggle(VoxCommandRow row, bool hidden)
    {
        if (row.Name != "LaneY" || row.Type != 2 || row.Length % Timebase.PulsesPerKsh192Unit != 0) return false;
        if (VoxToken.Number(row.P3) != 0 || VoxToken.Number(row.P4) != 0) return false;
        double? a = VoxToken.Number(row.P1), b = VoxToken.Number(row.P2);
        return hidden ? a == 1024 && b == 0 : a == 0 && b == 1024;
    }

    /// <summary>
    /// The lines a .ksh needs for <paramref name="vox"/>: (pulse, line) for the body. A LaneY row is a lane_toggle when it
    /// toggles in turn (shown → 0..1024, hidden → 1024..0); everything a ksh line cannot hold (a token with '|' or a line
    /// break) is left out — <see cref="Losses"/> names it.
    /// </summary>
    public static IEnumerable<(long Y, string Line)> Lines(VoxExtrasInfo vox)
    {
        foreach (var p in vox.PostEffects)
        {
            if (!clean(p.Name) || !clean(p.Param)) continue;
            string len = p.LengthMs is { } ms ? num(ms) + "ms" : ticks(p.Length).ToString(inv);
            yield return (p.Y, $"{Prefix}pe|{p.Stage}|{p.Index}|{len}|{p.Name}|{p.Param}|{num(p.From)}|{num(p.To)}");
        }

        bool hidden = false;
        foreach (var c in vox.Commands.OrderBy(c => c.Y))
        {
            if (c.Name == "LaneY")
            {
                if (IsToggle(c, hidden))
                {
                    hidden = !hidden;
                    yield return (c.Y, "lane_toggle=" + ticks(c.Length).ToString(inv));
                    continue;
                }

                hidden = Math.Abs(VoxToken.Number(c.P2) ?? 0) > 1e-9;
            }

            if (!clean(c.Name) || !clean(c.P1) || !clean(c.P2) || !clean(c.P3) || !clean(c.P4)) continue;
            yield return (c.Y, $"{Prefix}sp|{c.Name}|{c.Type.ToString(inv)}|{ticks(c.Length)}|{c.P1}|{c.P2}|{c.P3}|{c.P4}");
        }

        if (vox.RepresentativeBpm is { } rb) yield return (0, $"{Prefix}bpm|RepresentativeBpm|{num(rb)}");
        if (vox.ConstantScroll is { } cs) yield return (0, $"{Prefix}bpm|ConstantScroll|{(cs ? 1 : 0)}");
        foreach (var s in vox.Scripts)
            foreach (string l in s.Lines)
                if (!l.Any(char.IsControl)) yield return (0, $"{Prefix}script|{s.Id}|{l}");
        foreach (var s in vox.Scripted)
            yield return (s.Y, $"{Prefix}scripted|{s.Track}|{string.Join(",", s.Ids)}");
    }

    private static bool clean(string s) => s.Length > 0 && !s.Contains('|') && !s.Any(char.IsControl);

    /// <summary>What a .ksh loses of <paramref name="vox"/> (empty = nothing): script names, unwritable values.</summary>
    public static IEnumerable<string> Losses(VoxExtrasInfo vox)
    {
        if (vox.Scripts.Any(s => s.Name.Length > 0)) yield return "note script names";
        if (vox.PostEffects.Any(p => !clean(p.Name) || !clean(p.Param)) || vox.Commands.Any(c => !clean(c.P1) || !clean(c.P2) || !clean(c.P3) || !clean(c.P4)))
            yield return "VOX rows with a '|' or a line break in a value";
        if (vox.PostEffects.Any(p => p.LengthMs == null && p.Length % Timebase.PulsesPerKsh192Unit != 0)
            || vox.Commands.Any(c => c.Length % Timebase.PulsesPerKsh192Unit != 0))
            yield return "VOX row lengths finer than 1/192 of a measure (rounded)";
    }
}
