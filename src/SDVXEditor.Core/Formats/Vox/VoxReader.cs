using System.Globalization;
using System.Text;
using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Formats.Vox;

/// <summary>
/// Read-only VOX reader (format versions 10, 12 and 13, the ones the official data uses).
/// Reads timing (BPM incl. <c>4-</c> stops, time signatures, end position), BT/FX chips and holds
/// and lasers incl. slams and wide lasers. For analysis (pattern / radar features) the camera is read
/// coarsely: <c>SPCONTROLER</c> <c>CAM_RotX</c> / <c>CAM_Radi</c> tweens become zoom_top / zoom_bottom graph
/// points (x100 / x-100, the units VoxCharger uses), <c>Tilt</c> tweens become manual tilt points, and a laser
/// point carrying a spin id (laser field 4) adds a nominal one-beat spin. Effects, lyrics and the other
/// controller rows are ignored: the reader exists for analysis (difficulty, radar) and import previews, not
/// for a lossless round trip.
/// <para>
/// Position <c>MMM,BB,OO</c>: measure 1-based, beat 1-based, offset in ticks. A beat is the
/// time-signature denominator note and has <c>192 / den</c> ticks (24 for /8); one tick is 5 pulses.
/// </para>
/// <para>
/// Tracks: 1 = laser L, 2 = FX L, 3..6 = BT A..D, 7 = FX R, 8 = laser R. Laser fields:
/// <c>pos, value, seg(1 start / 0 middle / 2 end), ?, ?, width(1|2), …</c>; values are floats 0..1
/// from version 12 on and integers 0..127 before. A slam is two points on the same tick.
/// </para>
/// </summary>
public static class VoxReader
{
    public const long PulsesPerTick = Timebase.PulsesPerKsh192Unit; // 5

    /// <summary>Ticks per quarter note when a file has no <c>#BEAT RESOLUTION</c> (192 per 4/4 measure).</summary>
    public const int DefaultResolution = 48;

    public static Chart ReadFile(string path)
    {
        var chart = Read(Encoding.Latin1.GetString(File.ReadAllBytes(path)));
        if (SlotFromFileName(path) is { } slot) chart.Meta.DifficultySlot = slot;
        return chart;
    }

    /// <summary>Slot from the official file suffix (<c>_1n</c> … <c>_5m</c>, <c>_6u</c> = ultimate → MXM slot), or null.</summary>
    public static int? SlotFromFileName(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        if (stem.Length < 3 || stem[^3] != '_') return null;
        return stem[^2..] switch
        {
            "1n" => Charts.Difficulty.Novice,
            "2a" => Charts.Difficulty.Advanced,
            "3e" => Charts.Difficulty.Exhaust,
            "4i" => Charts.Difficulty.Infinite,
            "5m" or "6u" => Charts.Difficulty.Maximum,
            _ => null,
        };
    }

    public static Chart Read(string text)
    {
        var sections = SplitSections(text);
        var chart = new Chart();

        int version = 13;
        if (sections.TryGetValue("FORMAT VERSION", out var fv) && fv.Count > 0
            && int.TryParse(fv[0][0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            version = v;

        // ---- time signatures (needed before any position can be converted)
        if (sections.TryGetValue("BEAT INFO", out var beatRows))
        {
            foreach (var r in beatRows)
            {
                if (r.Length < 3 || !TryPos(r[0], out var p)) continue;
                if (!int.TryParse(r[1].Trim(), out int n) || !int.TryParse(r[2].Trim(), out int d) || n <= 0 || d <= 0) continue;
                chart.Beat.TimeSignatures.RemoveAll(s => s.MeasureIndex == p.Measure - 1);
                chart.Beat.TimeSignatures.Add(new TimeSignature(Math.Max(0, p.Measure - 1), n, d));
            }
        }
        chart.Beat.TimeSignatures.Sort((a, b) => a.MeasureIndex.CompareTo(b.MeasureIndex));
        if (chart.Beat.TimeSignatures.Count == 0 || chart.Beat.TimeSignatures[0].MeasureIndex != 0)
            chart.Beat.TimeSignatures.Insert(0, new TimeSignature(0, 4, 4));
        int resolution = DefaultResolution;
        if (sections.TryGetValue("BEAT RESOLUTION", out var res) && res.Count > 0
            && int.TryParse(res[0][0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rv) && rv > 0)
            resolution = rv;
        var grid = new Grid(chart.Beat.TimeSignatures, resolution);

        // ---- BPM; a third field ending in '-' starts a stop that lasts until the next BPM row
        if (sections.TryGetValue("BPM INFO", out var bpmRows))
        {
            long? stopStart = null;
            foreach (var r in bpmRows)
            {
                if (r.Length < 2 || !TryPos(r[0], out var p)) continue;
                if (!double.TryParse(r[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double bpm) || bpm <= 0) continue;
                long y = grid.ToPulse(p);
                if (stopStart is { } s && y > s) chart.Beat.Stops.Add(new Stop(s, y - s));
                stopStart = r.Length >= 3 && r[2].Trim().EndsWith('-') ? y : null;
                chart.Beat.Bpm.RemoveAll(b => b.Y == y);
                chart.Beat.Bpm.Add(new BpmChange(y, bpm));
            }
        }
        chart.Beat.Bpm.Sort((a, b) => a.Y.CompareTo(b.Y));
        if (chart.Beat.Bpm.Count == 0) chart.Beat.Bpm.Add(new BpmChange(0, 120));
        else if (chart.Beat.Bpm[0].Y > 0) chart.Beat.Bpm.Insert(0, new BpmChange(0, chart.Beat.Bpm[0].Bpm));

        if (sections.TryGetValue("END POSITION", out var endRows) && endRows.Count > 0 && TryPos(endRows[0][0], out var endPos))
            chart.EndPulse = grid.ToPulse(endPos);

        // ---- buttons
        ReadButtons(sections, "TRACK3", chart.Notes.Bt[0], grid);
        ReadButtons(sections, "TRACK4", chart.Notes.Bt[1], grid);
        ReadButtons(sections, "TRACK5", chart.Notes.Bt[2], grid);
        ReadButtons(sections, "TRACK6", chart.Notes.Bt[3], grid);
        ReadButtons(sections, "TRACK2", chart.Notes.Fx[0], grid);
        ReadButtons(sections, "TRACK7", chart.Notes.Fx[1], grid);

        // ---- lasers
        ReadLaser(sections, "TRACK1", chart.Notes.Lasers[0], grid, version);
        ReadLaser(sections, "TRACK8", chart.Notes.Lasers[1], grid, version);
        ReadSpins(sections, "TRACK1", chart, grid);
        ReadSpins(sections, "TRACK8", chart, grid);
        ReadCamera(sections, chart, grid);

        chart.Notes.SortAll();
        return chart;
    }

    // ------------------------------------------------------------------ sections

    private static Dictionary<string, List<string[]>> SplitSections(string text)
    {
        var result = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        List<string[]>? cur = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r', ' ');
            if (line.StartsWith('#'))
            {
                if (line.StartsWith("#END", StringComparison.Ordinal) && line.Length == 4) { cur = null; continue; }
                string name = line[1..].Trim();
                if (!result.TryGetValue(name, out cur)) result[name] = cur = new List<string[]>();
                continue;
            }
            if (cur == null || line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)) continue;
            cur.Add(line.Split('\t'));
        }
        return result;
    }

    private readonly record struct VoxPos(int Measure, int Beat, int Offset);

    private static bool TryPos(string s, out VoxPos pos)
    {
        pos = default;
        var a = s.Trim().Split(',');
        if (a.Length != 3) return false;
        if (!int.TryParse(a[0], out int m) || !int.TryParse(a[1], out int b) || !int.TryParse(a[2], out int o)) return false;
        pos = new VoxPos(m, b, o);
        return true;
    }

    /// <summary>Measure starts for 1-based VOX measures with the <c>192/den</c> ticks-per-beat rule.</summary>
    private sealed class Grid
    {
        private readonly List<TimeSignature> sigs;
        private readonly long[] startPulse;
        private readonly int resolution;

        public Grid(List<TimeSignature> sigs, int resolution)
        {
            this.sigs = sigs;
            this.resolution = resolution;
            startPulse = new long[sigs.Count];
            for (int i = 1; i < sigs.Count; i++)
                startPulse[i] = startPulse[i - 1]
                                + (sigs[i].MeasureIndex - sigs[i - 1].MeasureIndex) * Timebase.MeasureLength(sigs[i - 1].Numerator, sigs[i - 1].Denominator);
        }

        public long ToPulse(VoxPos p)
        {
            int measure = Math.Max(0, p.Measure - 1);
            int i = sigs.Count - 1;
            while (i > 0 && sigs[i].MeasureIndex > measure) i--;
            var s = sigs[i];
            long start = startPulse[i] + (measure - s.MeasureIndex) * Timebase.MeasureLength(s.Numerator, s.Denominator);
            return start + (long)(p.Beat - 1) * Timebase.BeatLength(s.Denominator) + TicksToPulses(p.Offset);
        }

        /// <summary>Ticks of this file's resolution → pulses (exact at the default 48, rounded otherwise).</summary>
        public long TicksToPulses(long ticks)
            => resolution == DefaultResolution
                ? ticks * PulsesPerTick
                : (long)Math.Round(ticks * (double)Timebase.PulsesPerQuarter / resolution, MidpointRounding.AwayFromZero);
    }

    // ------------------------------------------------------------------ tracks

    private static void ReadButtons(Dictionary<string, List<string[]>> sections, string track, List<Note> lane, Grid grid)
    {
        if (!sections.TryGetValue(track, out var rows)) return;
        foreach (var r in rows)
        {
            if (r.Length < 2 || !TryPos(r[0], out var p)) continue;
            if (!long.TryParse(r[1].Trim(), out long len) || len < 0) len = 0;
            lane.Add(new Note(grid.ToPulse(p), grid.TicksToPulses(len)));
        }
    }

    private static void ReadLaser(Dictionary<string, List<string[]>> sections, string track, List<LaserSection> lane, Grid grid, int version)
    {
        if (!sections.TryGetValue(track, out var rows)) return;
        double scale = version >= 12 ? 1.0 : 1.0 / 127.0;

        List<(long Y, double V)>? cur = null;
        int width = 1;

        void Flush()
        {
            if (cur is { Count: > 0 }) lane.Add(BuildSection(cur, width));
            cur = null;
        }

        foreach (var r in rows)
        {
            if (r.Length < 3 || !TryPos(r[0], out var p)) continue;
            if (!double.TryParse(r[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double raw)) continue;
            int seg = int.TryParse(r[2].Trim(), out int sg) ? sg : 0;
            double value = Math.Clamp(raw * scale, 0, 1);
            long y = grid.ToPulse(p);

            if (seg == 1)
            {
                // A start right after an end on the same tick is a fresh section; a start without an
                // end before it closes the open one.
                Flush();
                cur = new List<(long, double)>();
                width = r.Length > 5 && int.TryParse(r[5].Trim(), out int w) && w == 2 ? 2 : 1;
            }
            cur ??= new List<(long, double)>(); // tolerate a missing start marker
            cur.Add((y, value));
            if (seg == 2) Flush();
        }
        Flush();
    }

    /// <summary>Laser field 4 = spin id on the first point of a slam pair: one nominal spin (one beat) per such point.</summary>
    private static void ReadSpins(Dictionary<string, List<string[]>> sections, string track, Chart chart, Grid grid)
    {
        if (!sections.TryGetValue(track, out var rows)) return;
        foreach (var r in rows)
        {
            if (r.Length < 4 || !TryPos(r[0], out var p)) continue;
            if (!int.TryParse(r[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int spin) || spin == 0) continue;
            chart.Camera.Spin.Add(new CamPattern(grid.ToPulse(p), 1, Timebase.PulsesPerQuarter));
        }
    }

    /// <summary><c>pos name type duration from to …</c>: CAM_RotX / CAM_Radi / Tilt tweens as graph points.</summary>
    private static void ReadCamera(Dictionary<string, List<string[]>> sections, Chart chart, Grid grid)
    {
        if (!sections.TryGetValue("SPCONTROLER", out var rows)) return;
        foreach (var r in rows)
        {
            if (r.Length < 6 || !TryPos(r[0], out var p)) continue;
            string name = r[1].Trim();
            double unit = name switch { "CAM_RotX" => 100, "CAM_Radi" => -100, "Tilt" => 1, _ => 0 };
            if (unit == 0) continue;
            if (!long.TryParse(r[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long dur) || dur < 0) continue;
            if (!double.TryParse(r[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double from)) continue;
            if (!double.TryParse(r[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double to)) continue;
            long y = grid.ToPulse(p), y1 = y + grid.TicksToPulses(dur);
            if (name == "Tilt")
            {
                chart.Camera.Tilt.Add(new TiltPoint(y, TiltValue.Of(from)));
                chart.Camera.Tilt.Add(new TiltPoint(y1, TiltValue.Of(to)));
                continue;
            }
            var graph = name == "CAM_RotX" ? chart.Camera.Body.ZoomTop : chart.Camera.Body.ZoomBottom;
            AddGraphPoint(graph, y, from * unit);
            AddGraphPoint(graph, y1, to * unit);
        }
        chart.Camera.Tilt.Sort((a, b) => a.Y.CompareTo(b.Y));
    }

    /// <summary>Inserts a point in pulse order; a second point on the same pulse becomes an instant jump (Vf).</summary>
    private static void AddGraphPoint(List<GraphPoint> graph, long y, double v)
    {
        int i = graph.FindLastIndex(g => g.Y <= y);
        if (i >= 0 && graph[i].Y == y)
        {
            if (graph[i].ValueAfter != v) graph[i].Vf = v;
            return;
        }
        graph.Insert(i + 1, new GraphPoint(y, v));
    }

    /// <summary>Points on the same tick collapse into one slam point (first value → last value).</summary>
    private static LaserSection BuildSection(List<(long Y, double V)> pts, int width)
    {
        long y0 = pts[0].Y;
        var section = new LaserSection(y0, null, width);
        int i = 0;
        while (i < pts.Count)
        {
            int j = i;
            while (j + 1 < pts.Count && pts[j + 1].Y == pts[i].Y) j++;
            double v = pts[i].V, vf = pts[j].V;
            section.Points.Add(new LaserPoint(pts[i].Y - y0, v, j > i && vf != v ? vf : null));
            i = j + 1;
        }
        return section;
    }
}
