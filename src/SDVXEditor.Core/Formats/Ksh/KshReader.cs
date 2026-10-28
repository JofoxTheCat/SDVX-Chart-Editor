using SDVXEditor.Core.Charts;
using static SDVXEditor.Core.Formats.Ksh.KshTables;

namespace SDVXEditor.Core.Formats.Ksh;

public sealed class KshReadOptions
{
    /// <summary>
    /// Remove the intermediate points K-Shoot Editor bakes into a curved laser segment when it exports
    /// ksh (KSM v1 compatibility). See <see cref="KshReader"/> for the detection rule.
    /// </summary>
    public bool CollapseBakedCurves { get; set; } = true;

    /// <summary>
    /// Do not read a value change within 1/32 note as a slam when it is part of densely sampled
    /// movement (points closer than 1/32 that keep changing) or a lone sub-1/32 step no ksh writer
    /// emits as a slam. Off = the plain ksh2vox/libkson rule: every such change is a slam. See
    /// <see cref="KshReader"/> for the rule and the evidence.
    /// </summary>
    public bool RecognizeDenseMovement { get; set; } = true;
}

/// <summary>
/// ksh reader. Rules ported from ksh2vox (converter CLAUDE.md §6/§9) and kept identical where it
/// had them verified: N lines per measure at <c>measureStart + i·len/N</c>, options apply at the
/// position of the next chart line, <c>-</c> closes a laser section, <c>:</c> continues it without a
/// point, BT <c>1</c>/<c>2</c> = chip/hold but FX <c>2</c>/<c>1</c> = chip/hold, and a laser value
/// change within 1/32 note becomes one slam on the FIRST point's pulse, consumed in pairs — except
/// inside densely sampled movement (<see cref="KshReadOptions.RecognizeDenseMovement"/>, rule at
/// <c>IsSlam</c>).
/// Unknown header keys, options and lines are kept in <see cref="Chart.Compat"/>.
/// </summary>
public static class KshReader
{
    public static Chart ReadFile(string path, KshReadOptions? options = null)
        => Read(Decode(File.ReadAllBytes(path)), options);

    /// <summary>Decodes ksh bytes: UTF-8 BOM → UTF-8, else strict UTF-8, else Shift_JIS (cp932).</summary>
    public static string DecodeText(byte[] bytes) => Decode(bytes);

    public static Chart Read(string text, KshReadOptions? options = null)
        => new Parser(text, options ?? new KshReadOptions()).Run();

    private sealed class RawPoint
    {
        public long Y;
        public double V;
        public double? Vf;
        public Curve? Curve;
        public double ValueAfter => Vf ?? V;
    }

    private sealed class Parser
    {
        private readonly string[] lines;
        private readonly KshReadOptions options;
        private readonly Chart chart = new();

        private readonly long?[] btHold = new long?[4];
        private readonly long?[] fxHold = new long?[2];
        private readonly List<RawPoint>?[] openLaser = new List<RawPoint>?[2];
        private readonly int[] openLaserWidth = { 1, 1 };
        private readonly int[] pendingWidth = { 1, 1 };
        private readonly (long Y, Curve C)?[] pendingLaserCurve = new (long, Curve)?[2];
        private readonly List<(List<RawPoint> Points, int Width)>[] rawSections = { new(), new() };

        private readonly List<(string Graph, long Y, Curve C)> graphCurves = new();
        private readonly List<(int Lane, long Y, string Name, Dictionary<string, string> Params)> fxEvents = new();
        private readonly List<(int Lane, long Y, string Name, double? Vol)> chipSounds = new();
        private readonly List<(int Lane, long Y, string Effect, string[] Args)> legacyFx = new();

        public Parser(string text, KshReadOptions options)
        {
            if (text.Length > 0 && text[0] == '﻿') text = text[1..];
            lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            this.options = options;
        }

        public Chart Run()
        {
            int i = ReadHeader();
            ReadBody(i);
            Finish();
            return chart;
        }

        // ------------------------------------------------------------ header

        private int ReadHeader()
        {
            int i = 0;
            for (; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line == "--") return i + 1;
                if (line.Length == 0) continue;
                if (Bookmark.FromKshLine(line) is { } headerBookmark)
                {
                    chart.Editor.Bookmarks.Set(0, headerBookmark);
                    continue;
                }
                if (SongSourceLine.FromKshLine(line) is { } headerSource)
                {
                    AddSongSource(headerSource);
                    continue;
                }
                if (VoxKshLines.TryRead(line, 0, chart.VoxExtras)) continue; // VOX game effects (docs/VOX_EFFECTS.md)
                if (line.StartsWith("//", StringComparison.Ordinal))
                {
                    chart.Editor.Comments.Add(new PulseValue<string>(0, line[2..]));
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    chart.Compat.KshUnknownLine.Add(new PulseValue<string>(0, line));
                    continue;
                }
                Header(line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
            return i;
        }

        private void Header(string key, string value)
        {
            var m = chart.Meta;
            var bgm = chart.Audio.Bgm;
            switch (key)
            {
                case "title": m.Title = value; break;
                case "title_img": m.TitleImgFilename = value; break;
                case "title_translit": m.TitleTranslit = value; break;
                case "artist": m.Artist = value; break;
                case "artist_img": m.ArtistImgFilename = value; break;
                case "artist_translit": m.ArtistTranslit = value; break;
                case "effect": m.ChartAuthor = value; break;
                case "jacket": m.JacketFilename = value; break;
                case "illustrator": m.JacketAuthor = value; break;
                // ksh spec: an unknown difficulty name means infinite.
                case "difficulty": m.DifficultySlot = Math.Min(Difficulty.FromName(value) ?? 3, 3); break;
                case "level": m.Level = TryNum(value, out double lv) ? lv : 1; break;
                case "t":
                    m.DispBpm = value;
                    if (TryNum(value, out double bpm)) SetBpm(0, bpm);
                    break;
                case "to": if (TryNum(value, out double to) && to != 0) m.StdBpm = to; break;
                case "m":
                {
                    var parts = value.Split(';');
                    bgm.Filename = parts[0];
                    bgm.LegacyFpFilenames = parts.Skip(1).ToList();
                    break;
                }
                case "mvol": if (TryNum(value, out double mv)) bgm.Volume = mv / 100.0; break;
                case "o": if (TryNum(value, out double o)) bgm.OffsetMs = (long)Math.Round(o); break;
                case "po": if (TryNum(value, out double po)) bgm.PreviewOffsetMs = (long)Math.Round(po); break;
                case "plength": if (TryNum(value, out double pl)) bgm.PreviewDurationMs = (long)Math.Round(pl); break;
                case "bg": chart.Bg.LegacyBg = value.Split(';').ToList(); break;
                case "layer":
                {
                    var parts = value.Split(value.Contains(';') ? ';' : '/');
                    var layer = new BgLayer { Filename = parts[0] };
                    if (parts.Length > 1 && TryNum(parts[1], out double dur)) layer.DurationMs = (long)dur;
                    if (parts.Length > 2 && int.TryParse(parts[2], out int flags))
                    {
                        layer.RotateTilt = (flags & 1) != 0;
                        layer.RotateSpin = (flags & 2) != 0;
                    }
                    chart.Bg.Layer = layer;
                    break;
                }
                case "v": (chart.Bg.Movie ??= new BgMovie()).Filename = value; break;
                case "vo": if (TryNum(value, out double vo)) (chart.Bg.Movie ??= new BgMovie()).OffsetMs = (long)vo; break;
                case "total": if (int.TryParse(value, out int total)) chart.GaugeTotal = total; break;
                case "chokkakuvol": if (TryNum(value, out double cv)) chart.Audio.KeySound.LaserVolume.Add(new PulseValue<double>(0, cv / 100.0)); break;
                case "chokkakuautovol": chart.Audio.KeySound.LaserVolumeAuto = value != "0"; break;
                case "pfiltergain": if (TryNum(value, out double pg)) chart.Audio.LaserEffects.LegacyFilterGain.Add(new PulseValue<double>(0, pg / 100.0)); break;
                case "pfilterdelay": if (int.TryParse(value, out int pd)) chart.Audio.LaserEffects.PeakingFilterDelay = pd; break;
                case "filtertype": AddPulseEvent(chart.Audio.LaserEffects.PulseEvents, FilterToKson(value), 0); break;
                case "ver": chart.Compat.KshVersion = value; break;
                case "information": m.Information = value; break;
                case "icon": m.IconFilename = value; break;
                case "beat": SetSignature(0, value); break;
                case "bg_no" when int.TryParse(value, out int bgNo) && bgNo is >= 0 and <= 65535:
                    chart.VoxExtras.BgNo = bgNo; // music_db background (ksh2vox reads the same header key)
                    break;
                default: chart.Compat.KshUnknownMeta[key] = value; break;
            }
        }

        // ------------------------------------------------------------ body

        private static bool IsChartLine(string line)
        {
            int bar = line.IndexOf('|');
            return bar == 4 && line.Length >= 8 && line.IndexOf('|', 5) == 7;
        }

        private void ReadBody(int start)
        {
            var measures = new List<List<string>>();
            var cur = new List<string>();
            for (int i = start; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd();
                if (line == "--")
                {
                    measures.Add(cur);
                    cur = new List<string>();
                }
                else cur.Add(line);
            }
            // Lines after the last bar line: an unterminated measure if they hold chart lines, otherwise
            // trailing definitions (#define_fx etc., where KSM writes them) at the chart end.
            var trailing = new List<string>();
            if (cur.Any(IsChartLine)) measures.Add(cur);
            else trailing = cur;

            long measureStart = 0;
            (int N, int D)? nextSig = null;
            for (int mi = 0; mi < measures.Count; mi++)
            {
                var measure = measures[mi];
                if (nextSig is { } ns) { SetSignature(mi, ns); nextSig = null; }
                int firstChart = measure.FindIndex(IsChartLine);
                // `beat=` before the first chart line applies to this measure, after it to the next one.
                for (int k = 0; k < measure.Count; k++)
                {
                    if (!measure[k].StartsWith("beat=", StringComparison.Ordinal)) continue;
                    if (ParseSig(measure[k][5..]) is not { } sig) continue;
                    if (firstChart < 0 || k < firstChart) SetSignature(mi, sig);
                    else nextSig = sig;
                }

                var ts = SignatureAt(mi);
                long len = Timebase.MeasureLength(ts.Numerator, ts.Denominator);
                int n = Math.Max(1, measure.Count(IsChartLine));
                int li = 0;
                long Pos(int index) => measureStart + (2L * index * len + n) / (2L * n); // round(index·len/n)

                foreach (string line in measure)
                {
                    long y = Pos(li);
                    if (IsChartLine(line))
                    {
                        ChartLine(line, y);
                        li++;
                    }
                    else BodyLine(line, y);
                }
                measureStart += len;
            }
            chart.EndPulse = measureStart;
            CloseAll(measureStart);
            foreach (string line in trailing) BodyLine(line, measureStart);
        }

        private static (int, int)? ParseSig(string s)
        {
            var p = s.Split('/');
            return p.Length == 2 && int.TryParse(p[0], out int n) && int.TryParse(p[1], out int d) && n > 0 && d > 0 ? (n, d) : null;
        }

        private void SetSignature(int measure, string value)
        {
            if (ParseSig(value) is { } sig) SetSignature(measure, sig);
        }

        private void SetSignature(int measure, (int N, int D) sig)
        {
            var list = chart.Beat.TimeSignatures;
            list.RemoveAll(t => t.MeasureIndex == measure);
            var prev = list.LastOrDefault(t => t.MeasureIndex < measure);
            if (prev != null && prev.Numerator == sig.N && prev.Denominator == sig.D) return; // keep changes only
            list.Add(new TimeSignature(measure, sig.N, sig.D));
            list.Sort((a, b) => a.MeasureIndex.CompareTo(b.MeasureIndex));
        }

        private TimeSignature SignatureAt(int measure)
            => chart.Beat.TimeSignatures.LastOrDefault(t => t.MeasureIndex <= measure) ?? new TimeSignature(0, 4, 4);

        private void SetBpm(long y, double bpm)
        {
            chart.Beat.Bpm.RemoveAll(b => b.Y == y);
            chart.Beat.Bpm.Add(new BpmChange(y, bpm));
            chart.Beat.Bpm.Sort((a, b) => a.Y.CompareTo(b.Y));
        }

        private void ChartLine(string line, long y)
        {
            var parts = line.Split('|');
            string bt = parts[0], fx = parts[1], laser = parts.Length > 2 ? parts[2] : "--";

            for (int k = 0; k < 4; k++)
            {
                char c = k < bt.Length ? bt[k] : '0';
                if (c != '2' && btHold[k] is { } hs)
                {
                    chart.Notes.Bt[k].Add(new Note(hs, y - hs));
                    btHold[k] = null;
                }
                if (c == '1') chart.Notes.Bt[k].Add(new Note(y));
                else if (c == '2' && btHold[k] == null) btHold[k] = y;
            }

            for (int k = 0; k < 2; k++)
            {
                char c = k < fx.Length ? fx[k] : '0';
                bool hold = c == '1' || LegacyFxChars.ContainsKey(c);
                if (!hold && fxHold[k] is { } hs)
                {
                    chart.Notes.Fx[k].Add(new Note(hs, y - hs));
                    fxHold[k] = null;
                }
                if (c == '2') chart.Notes.Fx[k].Add(new Note(y));
                else if (hold && fxHold[k] == null)
                {
                    fxHold[k] = y;
                    if (LegacyFxChars.TryGetValue(c, out var legacy)) legacyFx.Add((k, y, legacy.Effect, legacy.Args));
                }
            }

            for (int k = 0; k < 2; k++)
            {
                char c = k < laser.Length ? laser[k] : '-';
                if (c == '-')
                {
                    CloseLaser(k);
                    continue;
                }
                if (c == ':') continue;
                int v = LaserChar(c);
                if (v < 0) continue;
                if (openLaser[k] == null)
                {
                    openLaser[k] = new List<RawPoint>();
                    openLaserWidth[k] = pendingWidth[k];
                    pendingWidth[k] = 1;
                }
                var pt = new RawPoint { Y = y, V = v / 50.0 };
                if (pendingLaserCurve[k] is { } pc && pc.Y == y)
                {
                    pt.Curve = pc.C;
                    pendingLaserCurve[k] = null;
                }
                openLaser[k]!.Add(pt);
            }

            if (laser.Length > 2) LaneSuffix(laser[2..], y, line);
            for (int extra = 3; extra < parts.Length; extra++)
                chart.Compat.KshUnknownLine.Add(new PulseValue<string>(y, line));
        }

        /// <summary>Lane spin suffix: <c>@(n</c> <c>@)n</c> spin, <c>@&lt;n</c> <c>@&gt;n</c> half spin, <c>S&lt;n;scale;repeat;decay</c> swing.</summary>
        private void LaneSuffix(string s, long y, string line)
        {
            if (s.Length >= 3 && (s[0] == '@' || s[0] == 'S'))
            {
                int dir = s[1] is '(' or '<' ? -1 : s[1] is ')' or '>' ? 1 : 0;
                var args = s[2..].Split(';');
                if (dir != 0 && long.TryParse(args[0], out long len))
                {
                    var p = new CamPattern(y, dir, len * Timebase.PulsesPerKsh192Unit);
                    if (s[0] == 'S')
                    {
                        if (args.Length > 1 && TryNum(args[1], out double sc)) p.SwingScale = sc;
                        if (args.Length > 2 && int.TryParse(args[2], out int rp)) p.SwingRepeat = rp;
                        if (args.Length > 3 && int.TryParse(args[3], out int dc)) p.SwingDecayOrder = dc;
                        chart.Camera.Swing.Add(p);
                        return;
                    }
                    if (s[1] is '(' or ')') { chart.Camera.Spin.Add(p); return; }
                    chart.Camera.HalfSpin.Add(p);
                    return;
                }
            }
            chart.Compat.KshUnknownLine.Add(new PulseValue<string>(y, line));
        }

        private void AddSongSource(string url) => chart.Editor.SongSources = chart.Editor.SongSources.Append(url).ToArray();

        private void CloseLaser(int lane)
        {
            if (openLaser[lane] is { Count: > 0 } pts) rawSections[lane].Add((pts, openLaserWidth[lane]));
            openLaser[lane] = null;
        }

        private void CloseAll(long end)
        {
            for (int k = 0; k < 4; k++)
                if (btHold[k] is { } hs) chart.Notes.Bt[k].Add(new Note(hs, end - hs));
            for (int k = 0; k < 2; k++)
            {
                if (fxHold[k] is { } hs) chart.Notes.Fx[k].Add(new Note(hs, end - hs));
                CloseLaser(k);
            }
        }

        private void BodyLine(string line, long y)
        {
            if (line.Length == 0) return;
            if (Bookmark.FromKshLine(line) is { } bookmark)
            {
                chart.Editor.Bookmarks.Set(y, bookmark);
                return;
            }
            if (SongSourceLine.FromKshLine(line) is { } source)
            {
                AddSongSource(source); // song-level: the position does not matter
                return;
            }
            if (VoxKshLines.TryRead(line, y, chart.VoxExtras)) return; // VOX game effects (docs/VOX_EFFECTS.md)
            if (line.StartsWith("//", StringComparison.Ordinal))
            {
                chart.Editor.Comments.Add(new PulseValue<string>(y, line[2..]));
                return;
            }
            if (line.StartsWith("#define_fx ", StringComparison.Ordinal) || line.StartsWith("#define_filter ", StringComparison.Ordinal))
            {
                Define(line);
                return;
            }
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                chart.Compat.KshUnknownLine.Add(new PulseValue<string>(y, line));
                return;
            }
            Option(line[..eq], line[(eq + 1)..], y);
        }

        private void Define(string line)
        {
            bool fx = line.StartsWith("#define_fx ", StringComparison.Ordinal);
            string rest = line[(fx ? 11 : 15)..].Trim();
            int sp = rest.IndexOf(' ');
            string name = sp < 0 ? rest : rest[..sp];
            var def = new AudioEffectDef { Name = name };
            if (sp >= 0)
            {
                foreach (string kv in rest[(sp + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    if (eq < 0) continue;
                    string k = kv[..eq].Trim(), v = kv[(eq + 1)..].Trim();
                    if (k == "type") def.Type = EffectToKson(v);
                    else def.Params[ParamToKson(k)] = v;
                }
            }
            (fx ? chart.Audio.FxEffects.Defs : chart.Audio.LaserEffects.Defs).Add(def);
        }

        private static void AddPulseEvent(Dictionary<string, List<long>> dict, string name, long y)
        {
            if (!dict.TryGetValue(name, out var list)) dict[name] = list = new List<long>();
            if (!list.Contains(y)) list.Add(y);
        }

        private static void AddByPulse<T>(List<PulseValue<T>> list, long y, T value)
        {
            list.RemoveAll(p => p.Y == y);
            list.Add(new PulseValue<T>(y, value));
        }

        /// <summary>Graph option: two lines on one pulse form an instant change [v, vf].</summary>
        private static void AddGraphValue(List<GraphPoint> graph, long y, double v)
        {
            if (graph.Count > 0 && graph[^1].Y == y)
            {
                var last = graph[^1];
                last.Vf = v == last.V ? null : v;
            }
            else graph.Add(new GraphPoint(y, v));
        }

        private List<GraphPoint>? GraphFor(string key) => key switch
        {
            "scroll_speed" => chart.Beat.ScrollSpeed,
            "zoom_top" => chart.Camera.Body.ZoomTop,
            "zoom_bottom" => chart.Camera.Body.ZoomBottom,
            "zoom_side" => chart.Camera.Body.ZoomSide,
            "center_split" => chart.Camera.Body.CenterSplit,
            "rotation_deg" => chart.Camera.Body.RotationDeg,
            _ => null,
        };

        private static Curve? ParseCurve(string v)
        {
            var p = v.Split(';');
            return p.Length == 2 && TryNum(p[0], out double a) && TryNum(p[1], out double b) ? new Curve(a, b) : null;
        }

        private static TiltValue? ParseTilt(string v)
        {
            if (TiltValue.Names.TryGetValue(v, out var mode)) return TiltValue.Of(mode);
            if (v == "big") return TiltValue.Of(TiltMode.Bigger);
            if (v == "keep") return TiltValue.Of(TiltMode.KeepBigger);
            return TryNum(v, out double d) ? TiltValue.Of(d) : null;
        }

        private void Option(string key, string value, long y)
        {
            if (GraphFor(key) is { } graph)
            {
                if (TryNum(value, out double gv)) { AddGraphValue(graph, y, gv); return; }
            }
            else if (key.EndsWith("_curve", StringComparison.Ordinal) && (GraphFor(key[..^6]) != null || key == "tilt_curve")
                     && ParseCurve(value) is { } gc)
            {
                graphCurves.Add((key[..^6], y, gc));
                return;
            }

            switch (key)
            {
                case "t":
                    if (TryNum(value, out double bpm)) { SetBpm(y, bpm); return; }
                    break;
                case "beat": return; // handled per measure
                case "stop":
                    if (long.TryParse(value, out long st)) { chart.Beat.Stops.Add(new Stop(y, st * Timebase.PulsesPerKsh192Unit)); return; }
                    break;
                case "tilt":
                    if (ParseTilt(value) is { } tv)
                    {
                        var tilt = chart.Camera.Tilt;
                        if (tilt.Count > 0 && tilt[^1].Y == y)
                            tilt[^1].Final = tv == tilt[^1].Value ? null : tv;
                        else tilt.Add(new TiltPoint(y, tv));
                        return;
                    }
                    break;
                case "laserrange_l":
                case "laserrange_r":
                    pendingWidth[key[^1] == 'l' ? 0 : 1] = value == "2x" ? 2 : 1;
                    return;
                case "laser_l_curve":
                case "laser_r_curve":
                    if (ParseCurve(value) is { } lc) { pendingLaserCurve[key[6] == 'l' ? 0 : 1] = (y, lc); return; }
                    break;
                case "fx-l":
                case "fx-r":
                {
                    if (value.Length == 0) return; // "no effect"
                    var args = value.Split(';');
                    string name = EffectToKson(args[0]);
                    fxEvents.Add((key[3] == 'l' ? 0 : 1, y, name, new Dictionary<string, string>()) );
                    pendingArgs.Add((fxEvents.Count - 1, args.Skip(1).ToArray()));
                    return;
                }
                case "fx-l_se":
                case "fx-r_se":
                {
                    var args = value.Split(';');
                    double? vol = args.Length > 1 && TryNum(args[1], out double vv) ? vv / 100.0 : null;
                    chipSounds.Add((key[3] == 'l' ? 0 : 1, y, args[0], vol));
                    return;
                }
                case "filtertype":
                    AddPulseEvent(chart.Audio.LaserEffects.PulseEvents, FilterToKson(value), y);
                    return;
                case "pfiltergain":
                    if (TryNum(value, out double pg)) { AddByPulse(chart.Audio.LaserEffects.LegacyFilterGain, y, pg / 100.0); return; }
                    break;
                case "chokkakuvol":
                    if (TryNum(value, out double cv)) { AddByPulse(chart.Audio.KeySound.LaserVolume, y, cv / 100.0); return; }
                    break;
                case "chokkakuse":
                    AddPulseEvent(chart.Audio.KeySound.LaserSlamEvents, SlamSoundToKson(value), y);
                    return;
                case "lane_toggle":
                    if (VoxKshLines.LaneToggle(chart.VoxExtras, y, value)) return; // → VOX LaneY 0 ↔ 1024
                    break;
            }

            if (key.StartsWith("fx:", StringComparison.Ordinal) || key.StartsWith("filter:", StringComparison.Ordinal))
            {
                var p = key.Split(':');
                if (p.Length == 3)
                {
                    var target = p[0] == "fx" ? chart.Audio.FxEffects.ParamChanges : chart.Audio.LaserEffects.ParamChanges;
                    if (!target.TryGetValue(p[1], out var prms)) target[p[1]] = prms = new();
                    if (!prms.TryGetValue(ParamToKson(p[2]), out var list)) prms[ParamToKson(p[2])] = list = new();
                    list.Add(new PulseValue<string>(y, value));
                    return;
                }
            }

            if (!chart.Compat.KshUnknownOption.TryGetValue(key, out var unknown))
                chart.Compat.KshUnknownOption[key] = unknown = new List<PulseValue<string>>();
            unknown.Add(new PulseValue<string>(y, value));
        }

        private readonly List<(int Index, string[] Args)> pendingArgs = new();

        // ------------------------------------------------------------ finish

        private string EffectType(string name)
            => chart.Audio.FxEffects.Defs.LastOrDefault(d => d.Name == name)?.Type ?? name;

        private void Finish()
        {
            // fx-l=Name;args: the args mean different things per effect type, so resolve after all #define_fx.
            foreach (var (index, args) in pendingArgs)
            {
                var e = fxEvents[index];
                fxEvents[index] = (e.Lane, e.Y, e.Name, InvocationParams(EffectType(e.Name), args));
            }
            foreach (var (lane, y, effect, args) in legacyFx)
            {
                string name = EffectToKson(effect);
                if (!fxEvents.Any(e => e.Lane == lane && e.Y == y))
                    fxEvents.Add((lane, y, name, InvocationParams(name, args)));
            }

            chart.Notes.SortAll();

            foreach (var (lane, y, name, prm) in fxEvents)
            {
                var hold = chart.Notes.Fx[lane].FirstOrDefault(n => n.Length > 0 && n.Y <= y && y < n.EndY);
                if (hold != null) hold.Effects.Add(new FxEffectEvent(name, y - hold.Y, prm));
                else chart.Audio.FxEffects.OrphanLongEvents.Add(new OrphanFxEvent(name, lane, y, prm));
            }
            foreach (var (lane, y, name, vol) in chipSounds)
            {
                var chip = chart.Notes.Fx[lane].FirstOrDefault(n => n.Y == y && n.Length == 0 && n.KeySound == null);
                if (chip != null) chip.KeySound = new KeySoundRef(name, vol);
                else chart.Audio.KeySound.OrphanFxChipEvents.Add(new OrphanKeySound(name, lane, y, vol));
            }

            for (int lane = 0; lane < 2; lane++)
            {
                foreach (var (pts, width) in rawSections[lane])
                {
                    var collapsed = CollapseSlams(pts, options.RecognizeDenseMovement);
                    if (options.CollapseBakedCurves) collapsed = CollapseBakedCurves(collapsed);
                    long y0 = collapsed[0].Y;
                    chart.Notes.Lasers[lane].Add(new LaserSection(y0,
                        collapsed.Select(p => new LaserPoint(p.Y - y0, p.V, p.Vf, p.Curve)), width));
                }
            }

            foreach (var (graphName, y, c) in graphCurves)
            {
                if (graphName == "tilt")
                {
                    var t = chart.Camera.Tilt.FirstOrDefault(p => p.Y == y);
                    if (t != null) { t.Curve = c; continue; }
                }
                else if (GraphFor(graphName)!.FirstOrDefault(p => p.Y == y) is { } gp)
                {
                    gp.Curve = c;
                    continue;
                }
                string key = graphName + "_curve";
                if (!chart.Compat.KshUnknownOption.TryGetValue(key, out var list)) chart.Compat.KshUnknownOption[key] = list = new();
                list.Add(new PulseValue<string>(y, Fmt(c.A) + ";" + Fmt(c.B)));
            }

            if (chart.Beat.Bpm.Count == 0) chart.Beat.Bpm.Add(new BpmChange(0, 120));
            if (chart.Beat.TimeSignatures.Count == 0 || chart.Beat.TimeSignatures[0].MeasureIndex != 0)
                chart.Beat.TimeSignatures.Insert(0, new TimeSignature(0, 4, 4));
            chart.Beat.Stops.Sort((a, b) => a.Y.CompareTo(b.Y));
        }

        /// <summary>
        /// ksh2vox <c>_collapse_slams</c>: two consecutive points with different values at most 1/32 note
        /// apart are one slam on the first point's pulse; pairs are consumed two at a time. With
        /// <paramref name="denseRule"/> a candidate pair must also pass <see cref="IsSlam"/>.
        /// </summary>
        private static List<RawPoint> CollapseSlams(List<RawPoint> pts, bool denseRule)
        {
            var result = new List<RawPoint>();
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                if (i + 1 < pts.Count)
                {
                    var q = pts[i + 1];
                    if (q.V != p.V && q.Y - p.Y <= Timebase.SlamMaxPulses && (!denseRule || IsSlam(pts, i)))
                    {
                        // The curve of the segment after the slam may be stated on either line.
                        result.Add(new RawPoint { Y = p.Y, V = p.V, Vf = q.V, Curve = q.Curve ?? p.Curve });
                        i++;
                        continue;
                    }
                }
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Whether the candidate pair <c>pts[i]</c> → <c>pts[i+1]</c> (different values, at most 1/32 apart)
        /// is a slam. ksh itself cannot say: K-Shoot's own reader (libkson <c>KshIOIn.cpp</c>) also calls
        /// every such change a slam, and its writer (<c>KshIOOut.cpp</c>) writes kson points closer than
        /// 1/32 unchanged, so a K-Shoot export of densely sampled movement reads back as runs of tiny
        /// slams (ABYSS MOTION: 29 the .kson twin does not have — 27 steps on a 5-pulse grid, one 20-pulse
        /// pair, one 30-pulse pair into a 5-pulse run). Three facts separate them, all measured:
        /// <list type="number">
        /// <item>A slam ends on a held value. libkson's writer shortens a slam whenever the next point is
        /// near so that (but for a next point 31–34 pulses away) no change follows its end within 1/32, and none of the 3 749 real slams of the 24
        /// library charts is followed that closely by another change. So: if points continue to change
        /// in steps closer than 1/32 after the second point, it is movement.</item>
        /// <item>A sub-1/32 slam also starts from a held value: a step inside a chain of sub-1/32 points
        /// that changes more than once is a sample of that movement. (Only for sub-1/32 pairs: a full
        /// 1/32 slam right after dense movement is real — ABYSS 74160/74400/74640.)</item>
        /// <item>Slams are 1/32 long. libkson writes a shorter one only to make room for a point that
        /// follows within 1/16 of its start, and all 3 749 library slams are exactly 1/32. A lone
        /// sub-1/32 pair without such a follower is two points (ABYSS lane R 80320).</item>
        /// </list>
        /// None of the 23 hand-made library charts has a pair this rule rejects (their slam counts are
        /// unchanged); on ABYSS MOTION the ksh slams now equal the kson ones except one section that
        /// only the ksh has.
        /// </summary>
        private static bool IsSlam(List<RawPoint> pts, int i)
        {
            if (DenseChanges(pts, i + 1, +1) > 0) return false;
            long gap = pts[i + 1].Y - pts[i].Y;
            if (gap >= Timebase.SlamMaxPulses) return true;
            if (DenseChanges(pts, i, -1) > 0) return false;
            return i + 2 < pts.Count && pts[i + 2].Y - pts[i].Y <= 2 * Timebase.SlamMaxPulses;
        }

        /// <summary>Value changes walking from <paramref name="k"/> in direction <paramref name="dir"/> while points are closer than 1/32.</summary>
        private static int DenseChanges(List<RawPoint> pts, int k, int dir)
        {
            int changes = 0;
            for (int n = k + dir; n >= 0 && n < pts.Count; k = n, n += dir)
            {
                var (a, b) = dir > 0 ? (pts[k], pts[n]) : (pts[n], pts[k]);
                if (b.Y - a.Y >= Timebase.SlamMaxPulses) break;
                if (b.V != a.V) changes++;
            }
            return changes;
        }

        /// <summary>
        /// K-Shoot Editor writes a curved segment (laser_*_curve) to ksh together with baked intermediate
        /// points so KSM v1 still draws an approximation. They are not authored points: the kson twin of
        /// ABYSS MOTION has neither them nor their count. The samples neither follow the kson curve
        /// formula nor a fixed spacing (collinear ones are dropped), so detection is structural: after a
        /// curved point, the segment ends at the farthest point — at most the next curved or slam point —
        /// such that every point in between carries no curve/slam and lies strictly between the start
        /// and end values, strictly monotone. Those in-between points are dropped. Baked samples are never
        /// closer than 1/32 (they would read as slams; ABYSS: all 40 pulses or more apart), so a point that
        /// close to its predecessor ends the segment — it is an authored point, like the 5-pulse points
        /// after ABYSS lane R 74040 that the kson keeps.
        /// </summary>
        private static List<RawPoint> CollapseBakedCurves(List<RawPoint> pts)
        {
            if (!pts.Any(p => p.Curve is { IsLinear: false })) return pts;
            var result = new List<RawPoint>();
            int i = 0;
            while (i < pts.Count)
            {
                var p = pts[i];
                result.Add(p);
                if (p.Curve is not { IsLinear: false } || i + 2 >= pts.Count)
                {
                    i++;
                    continue;
                }
                int best = i + 1;
                for (int j = i + 2; j < pts.Count; j++)
                {
                    var inner = pts[j - 1];
                    if (inner.Curve != null || inner.Vf != null) break;
                    if (inner.Y - pts[j - 2].Y <= Timebase.SlamMaxPulses) break;
                    double a = p.ValueAfter, b = pts[j].V;
                    bool ok = a != b;
                    double prev = a;
                    for (int k = i + 1; k < j && ok; k++)
                    {
                        double v = pts[k].V;
                        ok = Math.Min(a, b) < v && v < Math.Max(a, b) && (b > a ? v > prev : v < prev);
                        prev = v;
                    }
                    if (ok) best = j;
                }
                i = best;
            }
            return result;
        }
    }
}
