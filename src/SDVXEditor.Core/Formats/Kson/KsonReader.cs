using System.Text.Json;
using System.Text.Json.Nodes;
using SDVXEditor.Core.Charts;
using static SDVXEditor.Core.Formats.Kson.KsonJson;

namespace SDVXEditor.Core.Formats.Kson;

/// <summary>
/// Tolerant kson reader. Accepts the 1.0 compact array layout written by K-Shoot Editor as well as
/// the object forms and key names of older drafts (<c>{"y":…,"l":…}</c>, <c>time_sig [idx, n, d]</c>,
/// <c>def</c> as a dictionary, <c>zoom_size</c>/<c>split</c>/<c>rotation_z</c>). Every key it does not
/// understand — or a known key in a shape it cannot parse — is kept verbatim in <see cref="Chart.Extra"/>.
/// </summary>
public static class KsonReader
{
    /// <summary>Key under kson <c>impl</c> that holds this editor's own data.</summary>
    public const string ImplKey = "sdvx-editor";

    private static readonly JsonDocumentOptions docOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static Chart ReadFile(string path) => Read(File.ReadAllText(path)); // strips a UTF-8 BOM

    public static Chart Read(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: docOptions) as JsonObject
                   ?? throw new FormatException("kson root is not an object");
        return new Reader(root).Run();
    }

    private sealed class Reader
    {
        private readonly JsonObject root;
        private readonly Chart chart = new();

        public Reader(JsonObject root) => this.root = root;

        public Chart Run()
        {
            chart.FormatVersion = (int)Int(root["format_version"], 1);
            Guard(new[] { "meta" }, () => ReadMeta(Obj(root, "meta")));
            Guard(new[] { "beat" }, () => ReadBeat(Obj(root, "beat")));
            if (Obj(root, "gauge") is { } gauge)
            {
                chart.GaugeTotal = (int)Int(gauge["total"]);
                KeepUnknown(gauge, new[] { "gauge" }, "total");
            }
            Guard(new[] { "note" }, () => ReadNotes(Obj(root, "note")));
            Guard(new[] { "audio" }, () => ReadAudio(Obj(root, "audio")));
            Guard(new[] { "camera" }, () => ReadCamera(Obj(root, "camera")));
            Guard(new[] { "bg" }, () => ReadBg(Obj(root, "bg")));
            Guard(new[] { "editor" }, () => ReadEditor(Obj(root, "editor")));
            Guard(new[] { "compat" }, () => ReadCompat(Obj(root, "compat")));
            AttachFxEvents();
            ReadImpl(Obj(root, "impl"));
            KeepUnknown(root, Array.Empty<string>(),
                "format_version", "meta", "beat", "gauge", "note", "audio", "camera", "bg", "editor", "compat", "impl");
            chart.Notes.SortAll();
            return chart;
        }

        // ------------------------------------------------------------ plumbing

        private static JsonObject? Obj(JsonObject? parent, string key) => parent?[key] as JsonObject;

        private void KeepUnknown(JsonObject obj, string[] path, params string[] known)
        {
            foreach (var (key, value) in obj)
            {
                if (known.Contains(key)) continue;
                KsonJson.Path(chart.Extra, path)[key] = value?.DeepClone();
            }
        }

        /// <summary>Stores <paramref name="node"/> verbatim in Extra at path/key.</summary>
        private void KeepRaw(string[] path, string key, JsonNode? node)
            => KsonJson.Path(chart.Extra, path)[key] = node?.DeepClone();

        /// <summary>Runs a whole-section parse; a section that throws is kept raw instead of half-read.</summary>
        private void Guard(string[] path, Action read)
        {
            try { read(); }
            catch (Exception e) when (e is InvalidOperationException or FormatException or InvalidCastException or IndexOutOfRangeException or ArgumentException)
            {
                string key = path[^1];
                KeepRaw(path[..^1], key, root[key]);
            }
        }

        /// <summary>Parses one known key; if its shape is not understood it goes to Extra and the default stays.</summary>
        private void Field(JsonObject obj, string[] path, string key, Action<JsonNode> read)
        {
            if (obj[key] is not { } node) return;
            try { read(node); }
            catch (Exception e) when (e is InvalidOperationException or FormatException or InvalidCastException or IndexOutOfRangeException or ArgumentException or NullReferenceException)
            {
                KeepRaw(path, key, node);
            }
        }

        private static JsonArray Arr(JsonNode n) => n as JsonArray ?? throw new FormatException("expected array");

        // ------------------------------------------------------------ shapes

        private static Curve? ReadCurve(JsonNode? n)
        {
            if (n is JsonArray c && c.Count >= 2) return new Curve(Num(c[0]), Num(c[1]));
            return null;
        }

        /// <summary>Value slot: number, [v, vf], or {v, vf}.</summary>
        private static (double V, double? Vf) ReadValue(JsonNode? n)
        {
            switch (n)
            {
                case JsonArray a when a.Count >= 2: return (Num(a[0]), Num(a[1]));
                case JsonArray a when a.Count == 1: return (Num(a[0]), null);
                case JsonObject o: return (Num(o["v"]), o["vf"] is { } vf ? Num(vf) : null);
                default:
                    if (!IsNumber(n)) throw new FormatException("expected number");
                    return (Num(n), null);
            }
        }

        public static GraphPoint ReadGraphPoint(JsonNode n)
        {
            if (n is JsonObject o)
            {
                var (v, vf) = o["v"] is JsonObject inner ? ReadValue(inner) : (Num(o["v"]), o["vf"] is { } f ? Num(f) : (double?)null);
                Curve? c = o["a"] != null || o["b"] != null ? new Curve(Num(o["a"]), Num(o["b"])) : ReadCurve(o["curve"]);
                return new GraphPoint(Int(o["y"]), v, vf, c);
            }
            var a = Arr(n);
            var (val, valF) = ReadValue(At(a, 1));
            return new GraphPoint(Int(a[0]), val, valF, ReadCurve(At(a, 2)));
        }

        private static List<GraphPoint> ReadGraph(JsonNode n) => Arr(n).Select(p => ReadGraphPoint(p!)).OrderBy(p => p.Y).ToList();

        private static List<PulseValue<T>> ReadByPulse<T>(JsonNode n, Func<JsonNode?, T> value)
        {
            var list = new List<PulseValue<T>>();
            foreach (var e in Arr(n))
            {
                if (e is JsonObject o) list.Add(new PulseValue<T>(Int(o["y"]), value(o["v"])));
                else if (e is JsonArray a) list.Add(new PulseValue<T>(Int(a[0]), value(At(a, 1))));
                else throw new FormatException("ByPulse entry");
            }
            return list;
        }

        /// <summary><c>[y, label]</c> / <c>[y, label, "#rrggbb"]</c> / <c>{"y", "v", "color"}</c>; sorted by pulse.</summary>
        /// <summary>
        /// <c>song_sources</c>: an array of URL strings, kept verbatim (validation is the UI's job). Anything else in it
        /// (a number, an object) makes the whole key unknown, so it round-trips raw instead of losing entries.
        /// </summary>
        private static IReadOnlyList<string> ReadSongSources(JsonNode n)
            => Arr(n).Select(e => e is JsonValue v && v.TryGetValue<string>(out var s) ? s : throw new FormatException("song source")).ToArray();

        private static BookmarkList ReadBookmarks(JsonNode n)
        {
            var list = new BookmarkList();
            foreach (var e in Arr(n))
            {
                if (e is JsonArray a)
                    list.Set(Int(a[0]), new Bookmark(Bookmark.CleanLabel(At(a, 1) is { } l ? Str(l) : ""), Bookmark.NormaliseColour(At(a, 2) is { } c ? Str(c) : null)));
                else if (e is JsonObject o)
                    list.Set(Int(o["y"]), new Bookmark(Bookmark.CleanLabel(o["v"] is { } l ? Str(l) : ""), Bookmark.NormaliseColour(o["color"] is { } c ? Str(c) : null)));
                else throw new FormatException("bookmark entry");
            }
            return list;
        }

        private static List<long> ReadPulses(JsonNode n) => Arr(n).Select(e => e is JsonArray a ? Int(a[0]) : Int(e)).ToList();

        // ------------------------------------------------------------ meta

        private void ReadMeta(JsonObject? meta)
        {
            if (meta == null) return;
            var m = chart.Meta;
            string[] path = { "meta" };
            m.Title = Str(meta["title"]);
            m.TitleTranslit = Str(meta["title_translit"]);
            m.TitleImgFilename = Str(meta["title_img_filename"]);
            m.Artist = Str(meta["artist"]);
            m.ArtistTranslit = Str(meta["artist_translit"]);
            m.ArtistImgFilename = Str(meta["artist_img_filename"]);
            m.ChartAuthor = Str(meta["chart_author"]);
            Field(meta, path, "difficulty", n =>
            {
                // uint 0..3, a name, or an older-draft object {idx, name}
                if (n is JsonObject o)
                {
                    m.DifficultySlot = (int)Int(o["idx"], 3);
                    if (o["name"] is { } nm) m.DifficultyName = Str(nm);
                }
                else if (IsString(n))
                {
                    string s = Str(n);
                    m.DifficultySlot = Difficulty.FromName(s) ?? 3;
                    if (Difficulty.FromName(s) == null) m.DifficultyName = s;
                }
                else m.DifficultySlot = (int)Math.Clamp(Int(n, 3), 0, 4);
            });
            m.Level = Num(meta["level"], 1);
            m.DispBpm = Str(meta["disp_bpm"]);
            if (meta["std_bpm"] is { } std) m.StdBpm = Num(std);
            m.JacketFilename = Str(meta["jacket_filename"]);
            m.JacketAuthor = Str(meta["jacket_author"]);
            m.IconFilename = Str(meta["icon_filename"]);
            m.Information = Str(meta["information"]);
            KeepUnknown(meta, path, "title", "title_translit", "title_img_filename", "artist", "artist_translit",
                "artist_img_filename", "chart_author", "difficulty", "level", "disp_bpm", "std_bpm",
                "jacket_filename", "jacket_author", "icon_filename", "information");
        }

        // ------------------------------------------------------------ beat

        private void ReadBeat(JsonObject? beat)
        {
            if (beat == null) return;
            string[] path = { "beat" };
            var b = chart.Beat;
            Field(beat, path, "bpm", n => b.Bpm = ReadByPulse(n, v => Num(v)).Select(p => new BpmChange(p.Y, p.Value)).OrderBy(x => x.Y).ToList());
            Field(beat, path, "time_sig", n =>
            {
                foreach (var e in Arr(n))
                {
                    if (e is JsonObject o)
                    {
                        var v = o["v"] as JsonObject ?? o;
                        b.TimeSignatures.Add(new TimeSignature((int)Int(o["idx"]), (int)Int(v["n"], 4), (int)Int(v["d"], 4)));
                    }
                    else
                    {
                        var a = Arr(e!);
                        if (a[1] is JsonArray sig) // 1.0: [idx, [n, d]]
                            b.TimeSignatures.Add(new TimeSignature((int)Int(a[0]), (int)Int(sig[0], 4), (int)Int(sig[1], 4)));
                        else // older drafts / ksh2vox: [idx, n, d]
                            b.TimeSignatures.Add(new TimeSignature((int)Int(a[0]), (int)Int(a[1], 4), (int)Int(At(a, 2), 4)));
                    }
                }
                b.TimeSignatures.Sort((x, y) => x.MeasureIndex.CompareTo(y.MeasureIndex));
            });
            Field(beat, path, "scroll_speed", n => b.ScrollSpeed = ReadGraph(n));
            Field(beat, path, "stop", n => b.Stops = ReadByPulse(n, v => Int(v)).Select(p => new Stop(p.Y, p.Value)).ToList());
            KeepUnknown(beat, path, "bpm", "time_sig", "scroll_speed", "stop");
        }

        // ------------------------------------------------------------ notes

        private void ReadNotes(JsonObject? note)
        {
            if (note == null) return;
            string[] path = { "note" };
            Field(note, path, "bt", n => ReadNoteLanes(Arr(n), chart.Notes.Bt));
            Field(note, path, "fx", n => ReadNoteLanes(Arr(n), chart.Notes.Fx));
            Field(note, path, "laser", n =>
            {
                var lanes = Arr(n);
                for (int lane = 0; lane < 2 && lane < lanes.Count; lane++)
                    foreach (var s in Arr(lanes[lane]!))
                        chart.Notes.Lasers[lane].Add(ReadLaserSection(s!));
            });
            KeepUnknown(note, path, "bt", "fx", "laser");
        }

        private static void ReadNoteLanes(JsonArray lanes, List<Note>[] target)
        {
            for (int lane = 0; lane < target.Length && lane < lanes.Count; lane++)
            {
                foreach (var e in Arr(lanes[lane]!))
                {
                    target[lane].Add(e switch
                    {
                        JsonObject o => new Note(Int(o["y"]), Int(o["l"])),
                        JsonArray a => new Note(Int(a[0]), Int(At(a, 1))),
                        _ => new Note(Int(e)),
                    });
                }
            }
        }

        private static LaserSection ReadLaserSection(JsonNode n)
        {
            JsonNode? y, pts, w;
            if (n is JsonObject o) { y = o["y"]; pts = o["v"]; w = o["w"] ?? o["wide"]; }
            else { var a = Arr(n); y = a[0]; pts = At(a, 1); w = At(a, 2); }
            var sec = new LaserSection(Int(y), width: w == null ? 1 : (int)Int(w, 1));
            foreach (var p in pts as JsonArray ?? new JsonArray())
            {
                if (p is JsonObject po)
                {
                    var (v, vf) = (Num(po["v"]), po["vf"] is { } f ? Num(f) : (double?)null);
                    Curve? c = po["a"] != null || po["b"] != null ? new Curve(Num(po["a"]), Num(po["b"])) : null;
                    sec.Points.Add(new LaserPoint(Int(po["ry"]), v, vf, c));
                }
                else
                {
                    var pa = Arr(p!);
                    var (v, vf) = ReadValue(At(pa, 1));
                    sec.Points.Add(new LaserPoint(Int(pa[0]), v, vf, ReadCurve(At(pa, 2))));
                }
            }
            sec.Points.Sort((a, b) => a.Ry.CompareTo(b.Ry));
            return sec;
        }

        // ------------------------------------------------------------ audio

        private void ReadAudio(JsonObject? audio)
        {
            if (audio == null) return;
            string[] path = { "audio" };
            if (Obj(audio, "bgm") is { } bgm) ReadBgm(bgm);
            if (Obj(audio, "key_sound") is { } ks) ReadKeySound(ks);
            if (Obj(audio, "audio_effect") is { } ae) ReadAudioEffect(ae);
            KeepUnknown(audio, path, "bgm", "key_sound", "audio_effect");
        }

        private void ReadBgm(JsonObject bgm)
        {
            var b = chart.Audio.Bgm;
            string[] path = { "audio", "bgm" };
            b.Filename = Str(bgm["filename"]);
            b.Volume = Num(bgm["vol"], 1.0);
            b.OffsetMs = Int(bgm["offset"]);
            if (Obj(bgm, "preview") is { } pv)
            {
                b.PreviewOffsetMs = Int(pv["offset"]);
                b.PreviewDurationMs = Int(pv["duration"], 15000);
                KeepUnknown(pv, new[] { "audio", "bgm", "preview" }, "offset", "duration");
            }
            if (Obj(bgm, "legacy") is { } lg)
            {
                Field(lg, new[] { "audio", "bgm", "legacy" }, "fp_filenames", n => b.LegacyFpFilenames = Arr(n).Select(x => Str(x)).ToList());
                KeepUnknown(lg, new[] { "audio", "bgm", "legacy" }, "fp_filenames");
            }
            KeepUnknown(bgm, path, "filename", "vol", "offset", "preview", "legacy");
        }

        private readonly List<(string Name, int Lane, long Y, double? Vol)> chipEvents = new();
        private readonly List<(string Name, int Lane, long Y, Dictionary<string, string> Params)> longEvents = new();

        private void ReadKeySound(JsonObject ks)
        {
            var k = chart.Audio.KeySound;
            if (Obj(ks, "fx") is { } fx)
            {
                string[] p = { "audio", "key_sound", "fx" };
                Field(fx, p, "chip_event", n =>
                {
                    foreach (var (name, lanesNode) in (JsonObject)n)
                    {
                        var lanes = Arr(lanesNode!);
                        for (int lane = 0; lane < 2 && lane < lanes.Count; lane++)
                            foreach (var e in Arr(lanes[lane]!))
                            {
                                if (e is JsonArray a)
                                    chipEvents.Add((name, lane, Int(a[0]), (At(a, 1) as JsonObject)?["vol"] is { } vol ? Num(vol) : null));
                                else
                                    chipEvents.Add((name, lane, Int(e), null));
                            }
                    }
                });
                KeepUnknown(fx, p, "chip_event");
            }
            if (Obj(ks, "laser") is { } laser)
            {
                string[] p = { "audio", "key_sound", "laser" };
                Field(laser, p, "vol", n => k.LaserVolume = ReadByPulse(n, v => Num(v)));
                Field(laser, p, "slam_event", n =>
                {
                    foreach (var (name, pulses) in (JsonObject)n) k.LaserSlamEvents[name] = ReadPulses(pulses!);
                });
                if (Obj(laser, "legacy") is { } lg)
                {
                    if (lg["vol_auto"] is { } va) k.LaserVolumeAuto = Bool(va);
                    KeepUnknown(lg, new[] { "audio", "key_sound", "laser", "legacy" }, "vol_auto");
                }
                KeepUnknown(laser, p, "vol", "slam_event", "legacy");
            }
            KeepUnknown(ks, new[] { "audio", "key_sound" }, "fx", "laser");
        }

        private static List<AudioEffectDef> ReadDefs(JsonNode n)
        {
            var list = new List<AudioEffectDef>();
            IEnumerable<(string, JsonNode?)> entries = n switch
            {
                JsonArray a => a.Select(e => (Str(((JsonArray)e!)[0]), At((JsonArray)e!, 1))),
                JsonObject o => o.Select(kv => (kv.Key, kv.Value)), // older drafts: dictionary
                _ => throw new FormatException("def"),
            };
            foreach (var (name, defNode) in entries)
            {
                var def = new AudioEffectDef { Name = name };
                if (defNode is JsonObject d)
                {
                    def.Type = Str(d["type"]);
                    if (d["v"] is JsonObject v)
                        foreach (var (pk, pv) in v) def.Params[pk] = Str(pv);
                }
                list.Add(def);
            }
            return list;
        }

        private static Dictionary<string, Dictionary<string, List<PulseValue<string>>>> ReadParamChanges(JsonNode n)
        {
            var result = new Dictionary<string, Dictionary<string, List<PulseValue<string>>>>();
            foreach (var (effect, paramsNode) in (JsonObject)n)
            {
                var dict = new Dictionary<string, List<PulseValue<string>>>();
                foreach (var (param, list) in (JsonObject)paramsNode!)
                    dict[param] = ReadByPulse(list!, v => Str(v));
                result[effect] = dict;
            }
            return result;
        }

        private void ReadAudioEffect(JsonObject ae)
        {
            if (Obj(ae, "fx") is { } fx)
            {
                string[] p = { "audio", "audio_effect", "fx" };
                var info = chart.Audio.FxEffects;
                Field(fx, p, "def", n => info.Defs = ReadDefs(n));
                Field(fx, p, "param_change", n => info.ParamChanges = ReadParamChanges(n));
                Field(fx, p, "long_event", n =>
                {
                    foreach (var (name, lanesNode) in (JsonObject)n)
                    {
                        var lanes = Arr(lanesNode!);
                        for (int lane = 0; lane < 2 && lane < lanes.Count; lane++)
                            foreach (var e in Arr(lanes[lane]!))
                            {
                                var prm = new Dictionary<string, string>();
                                long y;
                                if (e is JsonArray a)
                                {
                                    y = Int(a[0]);
                                    if (At(a, 1) is JsonObject po)
                                        foreach (var (pk, pv) in po) prm[pk] = Str(pv);
                                }
                                else y = Int(e);
                                longEvents.Add((name, lane, y, prm));
                            }
                    }
                });
                KeepUnknown(fx, p, "def", "param_change", "long_event");
            }
            if (Obj(ae, "laser") is { } laser)
            {
                string[] p = { "audio", "audio_effect", "laser" };
                var info = chart.Audio.LaserEffects;
                Field(laser, p, "def", n => info.Defs = ReadDefs(n));
                Field(laser, p, "param_change", n => info.ParamChanges = ReadParamChanges(n));
                Field(laser, p, "pulse_event", n =>
                {
                    foreach (var (name, pulses) in (JsonObject)n) info.PulseEvents[name] = ReadPulses(pulses!);
                });
                if (laser["peaking_filter_delay"] is { } d) info.PeakingFilterDelay = (int)Int(d);
                if (Obj(laser, "legacy") is { } lg)
                {
                    Field(lg, new[] { "audio", "audio_effect", "laser", "legacy" }, "filter_gain", n => info.LegacyFilterGain = ReadByPulse(n, v => Num(v)));
                    KeepUnknown(lg, new[] { "audio", "audio_effect", "laser", "legacy" }, "filter_gain");
                }
                KeepUnknown(laser, p, "def", "param_change", "pulse_event", "peaking_filter_delay", "legacy");
            }
            KeepUnknown(ae, new[] { "audio", "audio_effect" }, "fx", "laser");
        }

        /// <summary>Binds key sounds and long events to their FX notes (after notes are read).</summary>
        private void AttachFxEvents()
        {
            foreach (var (name, lane, y, vol) in chipEvents)
            {
                var chip = chart.Notes.Fx[lane].FirstOrDefault(n => n.Y == y && n.Length == 0 && n.KeySound == null);
                if (chip != null) chip.KeySound = new KeySoundRef(name, vol);
                else chart.Audio.KeySound.OrphanFxChipEvents.Add(new OrphanKeySound(name, lane, y, vol));
            }
            foreach (var (name, lane, y, prm) in longEvents)
            {
                var hold = chart.Notes.Fx[lane].FirstOrDefault(n => n.Length > 0 && n.Y <= y && y < n.EndY);
                if (hold != null) hold.Effects.Add(new FxEffectEvent(name, y - hold.Y, prm));
                else chart.Audio.FxEffects.OrphanLongEvents.Add(new OrphanFxEvent(name, lane, y, prm));
            }
            foreach (var lane in chart.Notes.Fx)
                foreach (var n in lane)
                    n.Effects = n.Effects.OrderBy(e => e.RelY).ToList();
        }

        // ------------------------------------------------------------ camera

        private static TiltValue ReadTiltScalar(JsonNode? n)
        {
            if (IsString(n))
            {
                string s = Str(n);
                if (TiltValue.Names.TryGetValue(s, out var mode)) return TiltValue.Of(mode);
                if (s == "big") return TiltValue.Of(TiltMode.Bigger);
                if (s == "keep") return TiltValue.Of(TiltMode.KeepBigger);
                throw new FormatException("tilt name " + s);
            }
            if (!IsNumber(n)) throw new FormatException("tilt value");
            return TiltValue.Of(Num(n));
        }

        public static TiltPoint ReadTiltPoint(JsonArray e)
        {
            long y = Int(e[0]);
            var v = At(e, 1);
            if (v is not JsonArray a) return new TiltPoint(y, ReadTiltScalar(v));
            if (a[0] is JsonArray vv) // [[v, vf], [a, b]]
                return new TiltPoint(y, ReadTiltScalar(vv[0]), vv.Count > 1 ? ReadTiltScalar(vv[1]) : null, ReadCurve(At(a, 1)));
            if (At(a, 1) is JsonArray curve) // [v, [a, b]]
                return new TiltPoint(y, ReadTiltScalar(a[0]), null, ReadCurve(curve));
            return new TiltPoint(y, ReadTiltScalar(a[0]), a.Count > 1 ? ReadTiltScalar(a[1]) : null);
        }

        private static List<CamPattern> ReadPatterns(JsonNode n, bool swing)
        {
            var list = new List<CamPattern>();
            foreach (var e in Arr(n))
            {
                var a = Arr(e!);
                var p = new CamPattern(Int(a[0]), (int)Int(At(a, 1), 1), Int(At(a, 2)));
                if (swing && At(a, 3) is JsonObject o)
                {
                    if (o["scale"] is { } sc) p.SwingScale = Num(sc);
                    if (o["repeat"] is { } rp) p.SwingRepeat = (int)Int(rp);
                    if (o["decay_order"] is { } dc) p.SwingDecayOrder = (int)Int(dc);
                }
                list.Add(p);
            }
            return list;
        }

        private void ReadCamera(JsonObject? cam)
        {
            if (cam == null) return;
            var c = chart.Camera;
            Field(cam, new[] { "camera" }, "tilt", n => c.Tilt = Arr(n).Select(e => ReadTiltPoint(Arr(e!))).ToList());
            if (Obj(cam, "cam") is { } cc)
            {
                if (Obj(cc, "body") is { } body)
                {
                    string[] p = { "camera", "cam", "body" };
                    Field(body, p, "zoom_top", n => c.Body.ZoomTop = ReadGraph(n));
                    Field(body, p, "zoom_bottom", n => c.Body.ZoomBottom = ReadGraph(n));
                    Field(body, p, "zoom_side", n => c.Body.ZoomSide = ReadGraph(n));
                    Field(body, p, "center_split", n => c.Body.CenterSplit = ReadGraph(n));
                    Field(body, p, "rotation_deg", n => c.Body.RotationDeg = ReadGraph(n));
                    // older draft names (kson-rs)
                    if (c.Body.ZoomSide.Count == 0) Field(body, p, "zoom_size", n => c.Body.ZoomSide = ReadGraph(n));
                    if (c.Body.CenterSplit.Count == 0) Field(body, p, "split", n => c.Body.CenterSplit = ReadGraph(n));
                    if (c.Body.RotationDeg.Count == 0 && body["rotation_z"] is JsonArray)
                        Field(body, p, "rotation_z", n => c.Body.RotationDeg = ReadGraph(n));
                    var known = new List<string> { "zoom_top", "zoom_bottom", "zoom_side", "center_split", "rotation_deg", "zoom_size", "split" };
                    if (body["rotation_z"] is JsonArray) known.Add("rotation_z");
                    KeepUnknown(body, p, known.ToArray());
                }
                if (Obj(cc, "pattern") is { } pat)
                {
                    if (Obj(pat, "laser") is { } pl)
                    {
                        if (Obj(pl, "slam_event") is { } se)
                        {
                            string[] p = { "camera", "cam", "pattern", "laser", "slam_event" };
                            Field(se, p, "spin", n => c.Spin = ReadPatterns(n, false));
                            Field(se, p, "half_spin", n => c.HalfSpin = ReadPatterns(n, false));
                            Field(se, p, "swing", n => c.Swing = ReadPatterns(n, true));
                            KeepUnknown(se, p, "spin", "half_spin", "swing");
                        }
                        KeepUnknown(pl, new[] { "camera", "cam", "pattern", "laser" }, "slam_event");
                    }
                    KeepUnknown(pat, new[] { "camera", "cam", "pattern" }, "laser");
                }
                KeepUnknown(cc, new[] { "camera", "cam" }, "body", "pattern");
            }
            KeepUnknown(cam, new[] { "camera" }, "tilt", "cam");
        }

        // ------------------------------------------------------------ bg / editor / compat / impl

        private void ReadBg(JsonObject? bg)
        {
            if (bg == null) return;
            var b = chart.Bg;
            b.Filename = Str(bg["filename"]);
            if (Obj(bg, "legacy") is { } lg)
            {
                string[] p = { "bg", "legacy" };
                Field(lg, p, "bg", n =>
                {
                    IEnumerable<JsonNode?> items = n is JsonArray a ? a : new[] { n };
                    b.LegacyBg = items.Select(i => i is JsonObject o ? Str(o["filename"]) : Str(i)).ToList();
                });
                if (Obj(lg, "layer") is { } layer)
                {
                    b.Layer = new BgLayer { Filename = Str(layer["filename"]), DurationMs = Int(layer["duration"]) };
                    if (Obj(layer, "rotation") is { } rot)
                    {
                        b.Layer.RotateTilt = Bool(rot["tilt"], true);
                        b.Layer.RotateSpin = Bool(rot["spin"], true);
                        KeepUnknown(rot, new[] { "bg", "legacy", "layer", "rotation" }, "tilt", "spin");
                    }
                    KeepUnknown(layer, new[] { "bg", "legacy", "layer" }, "filename", "duration", "rotation");
                }
                if (Obj(lg, "movie") is { } movie)
                {
                    b.Movie = new BgMovie { Filename = Str(movie["filename"]), OffsetMs = Int(movie["offset"]) };
                    KeepUnknown(movie, new[] { "bg", "legacy", "movie" }, "filename", "offset");
                }
                KeepUnknown(lg, p, "bg", "layer", "movie");
            }
            KeepUnknown(bg, new[] { "bg" }, "filename", "legacy");
        }

        private void ReadEditor(JsonObject? ed)
        {
            if (ed == null) return;
            chart.Editor.AppName = Str(ed["app_name"]);
            chart.Editor.AppVersion = Str(ed["app_version"]);
            Field(ed, new[] { "editor" }, "comment", n => chart.Editor.Comments = ReadByPulse(n, v => Str(v)));
            KeepUnknown(ed, new[] { "editor" }, "app_name", "app_version", "comment");
        }

        private void ReadCompat(JsonObject? compat)
        {
            if (compat == null) return;
            var c = chart.Compat;
            c.KshVersion = Str(compat["ksh_version"]);
            if (Obj(compat, "ksh_unknown") is { } ku)
            {
                string[] p = { "compat", "ksh_unknown" };
                Field(ku, p, "meta", n => { foreach (var (k, v) in (JsonObject)n) c.KshUnknownMeta[k] = Str(v); });
                Field(ku, p, "option", n => { foreach (var (k, v) in (JsonObject)n) c.KshUnknownOption[k] = ReadByPulse(v!, x => Str(x)); });
                Field(ku, p, "line", n => c.KshUnknownLine = ReadByPulse(n, x => Str(x)));
                KeepUnknown(ku, p, "meta", "option", "line");
            }
            KeepUnknown(compat, new[] { "compat" }, "ksh_version", "ksh_unknown");
        }

        private void ReadVoxExtras(JsonObject vox)
        {
            string[] p = { "impl", ImplKey, "vox" };
            var v = chart.VoxExtras;
            if (vox["bg_no"] is { } bg) v.BgNo = (int)Int(bg);
            Field(vox, p, "post_effect", n => v.PostEffects = Arr(n).Select(e =>
            {
                var a = Arr(e!);
                // [y, name, param, from, to, length, stage?, index?, length_ms?] (docs/VOX_EFFECTS.md)
                return new PostEffectRow(Int(a[0]), Str(At(a, 1)), Str(At(a, 2)), Num(At(a, 3)), Num(At(a, 4)), Int(At(a, 5)),
                    (int)Int(At(a, 6)), (int)Int(At(a, 7)), IsNumber(At(a, 8)) ? Num(At(a, 8)) : null);
            }).ToList());
            Field(vox, p, "commands", n => v.Commands = Arr(n).Select(e =>
            {
                var a = Arr(e!);
                string tok(int i) => At(a, i) is { } t && IsNumber(t) ? VoxToken.Format(Num(t), VoxValueKind.Float) : Str(At(a, i), "0");
                return new VoxCommandRow(Int(a[0]), Str(At(a, 1)), Int(At(a, 2)), Int(At(a, 3)), tok(4), tok(5), tok(6), tok(7));
            }).ToList());
            if (Obj(vox, "bpm_option") is { } bo)
            {
                if (bo["RepresentativeBpm"] is { } rb) v.RepresentativeBpm = Num(rb);
                if (bo["ConstantScroll"] is { } cs) v.ConstantScroll = Bool(cs);
                KeepUnknown(bo, new[] { "impl", ImplKey, "vox", "bpm_option" }, "RepresentativeBpm", "ConstantScroll");
            }
            Field(vox, p, "scripts", n => v.Scripts = Arr(n).Select(e =>
            {
                var a = Arr(e!);
                return new VoxScript((int)Int(a[0]), Arr(At(a, 1)!).Select(l => Str(l)), Str(At(a, 2)));
            }).ToList());
            Field(vox, p, "scripted", n => v.Scripted = Arr(n).Select(e =>
            {
                var a = Arr(e!);
                return new ScriptedNote(Int(a[0]), (int)Int(At(a, 1)), Arr(At(a, 2)!).Select(i => (int)Int(i)));
            }).ToList());
            Field(vox, p, "sp_controller", n => v.SpController = Arr(n).Select(e =>
            {
                var a = Arr(e!);
                return new SpControllerRow(Int(a[0]), Str(At(a, 1)), (int)Int(At(a, 2)), Int(At(a, 3)),
                    Num(At(a, 4)), Num(At(a, 5)), Num(At(a, 6)), Num(At(a, 7)));
            }).ToList());
            KeepUnknown(vox, p, "bg_no", "post_effect", "sp_controller", "commands", "bpm_option", "scripts", "scripted");
        }

        /// <summary>
        /// <c>laser_paths</c> (see <c>KsonWriter.LaserPathsNode</c>): attached to the section of that lane starting at y,
        /// only when the path still bakes to the section's points — a chart whose lasers were edited by another tool
        /// keeps its points and loses the anchors (counted in <see cref="Chart.StaleLaserPaths"/>).
        /// </summary>
        private void ReadLaserPaths(JsonNode n)
        {
            var lanes = Arr(n);
            for (int lane = 0; lane < Math.Min(2, lanes.Count); lane++)
            {
                foreach (var e in lanes[lane] as JsonArray ?? new JsonArray())
                {
                    var entry = Arr(e!);
                    long y = Int(entry[0]);
                    var path = ReadAnchors(At(entry, 1)!);

                    var section = chart.Notes.Lasers[lane].FirstOrDefault(s => s.Y == y);
                    if (section == null || !LaserPaths.TryAttach(section, path)) chart.StaleLaserPaths++;
                }
            }
        }

        /// <summary>An anchor list (see <c>KsonWriter.AnchorsNode</c>).</summary>
        private static LaserPath ReadAnchors(JsonNode n)
        {
            var path = new LaserPath();
            foreach (var an in Arr(n))
            {
                var a = Arr(an!);
                var (v, vf) = ReadValue(At(a, 1));
                LaserCurveType? type = At(a, 2) is { } t && IsString(t)
                    ? Str(t) switch { "perfect" => LaserCurveType.PerfectCurve, "bezier" => LaserCurveType.Bezier, _ => LaserCurveType.Linear }
                    : null;
                path.Anchors.Add(new LaserAnchor(Int(a[0]), v, vf, type, Bool(At(a, 3)), ReadCurve(At(a, 4))));
            }

            return path;
        }

        /// <summary>
        /// <c>camera_paths</c> (see <c>KsonWriter.CameraPathsNode</c>): attached to the run of that lane starting at y only
        /// when the path still bakes to the run's points; otherwise dropped and counted in <see cref="Chart.StaleCameraPaths"/>.
        /// Unknown lane names are kept verbatim.
        /// </summary>
        private void ReadCameraPaths(JsonNode n)
        {
            if (n is not JsonObject o) throw new FormatException("expected object");
            foreach (var (name, lanes) in o)
            {
                if (CameraPaths.Parse(name) is not { } lane)
                {
                    KsonJson.Path(chart.Extra, new[] { "impl", ImplKey, "camera_paths" })[name] = lanes?.DeepClone();
                    continue;
                }

                foreach (var e in lanes as JsonArray ?? new JsonArray())
                {
                    var entry = Arr(e!);
                    if (!CameraPaths.TryAttach(chart.Camera, lane, Int(entry[0]), ReadAnchors(At(entry, 1)!))) chart.StaleCameraPaths++;
                }
            }
        }

        private void ReadImpl(JsonObject? impl)
        {
            if (impl == null) return;
            if (Obj(impl, ImplKey) is { } ours)
            {
                string[] p = { "impl", ImplKey };
                if (ours["end_pulse"] is { } ep) chart.EndPulse = Int(ep);
                if (Obj(ours, "difficulty") is { } d)
                {
                    chart.Meta.DifficultySlot = (int)Math.Clamp(Int(d["slot"], chart.Meta.DifficultySlot), 0, 4);
                    if (d["name"] is { } nm) chart.Meta.DifficultyName = Str(nm);
                }
                Field(ours, p, "bookmarks", n => chart.Editor.Bookmarks = ReadBookmarks(n));
                Field(ours, p, "song_sources", n => chart.Editor.SongSources = ReadSongSources(n));
                if (Obj(ours, "vox") is { } vox) ReadVoxExtras(vox);
                Field(ours, p, "laser_paths", ReadLaserPaths);
                Field(ours, p, "camera_paths", ReadCameraPaths);
                KeepUnknown(ours, p, "end_pulse", "difficulty", "bookmarks", "song_sources", "vox", "laser_paths", "camera_paths");
            }
            KeepUnknown(impl, new[] { "impl" }, ImplKey);
        }
    }
}
