using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SDVXEditor.Core.Charts;
using static SDVXEditor.Core.Formats.Kson.KsonJson;

namespace SDVXEditor.Core.Formats.Kson;

/// <summary>
/// Writes the kson 1.0 compact layout (<c>[y, l]</c> holds, <c>[y, [v, vf], [a, b]]</c> graph points,
/// <c>time_sig [[idx, [n, d]]]</c>, <c>def [[name, {type, v}]]</c>). Defaults are omitted. Data kson
/// cannot express natively (MXM slot, explicit end, bookmarks) goes to <c>impl["sdvx-editor"]</c>;
/// <see cref="Chart.Extra"/> is merged back last.
/// </summary>
public static class KsonWriter
{
    public static void WriteFile(Chart chart, string path, bool indented = false)
        => File.WriteAllText(path, Write(chart, indented), new UTF8Encoding(false));

    public static string Write(Chart chart, bool indented = false)
        => ToJson(chart).ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = indented,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public static JsonObject ToJson(Chart c)
    {
        var root = new JsonObject
        {
            ["format_version"] = c.FormatVersion,
            ["meta"] = Meta(c),
            ["beat"] = Beat(c.Beat),
        };
        if (c.GaugeTotal != 0) root["gauge"] = new JsonObject { ["total"] = c.GaugeTotal };
        root["note"] = Notes(c.Notes);
        AddIfAny(root, "audio", Audio(c));
        AddIfAny(root, "camera", Camera(c.Camera));
        AddIfAny(root, "bg", Bg(c.Bg));
        AddIfAny(root, "editor", Editor(c.Editor));
        AddIfAny(root, "compat", Compat(c.Compat));
        AddIfAny(root, "impl", Impl(c));
        MergeMissing(root, c.Extra);
        return root;
    }

    private static void AddIfAny(JsonObject parent, string key, JsonObject? obj)
    {
        if (obj is { Count: > 0 }) parent[key] = obj;
    }

    private static void AddIfAny(JsonObject parent, string key, JsonArray? arr)
    {
        if (arr is { Count: > 0 }) parent[key] = arr;
    }

    private static void AddIfSet(JsonObject parent, string key, string value)
    {
        if (!string.IsNullOrEmpty(value)) parent[key] = value;
    }

    // ------------------------------------------------------------ shapes

    private static JsonArray CurveNode(Curve c) => new(JsonValue.Create(c.A), JsonValue.Create(c.B));

    private static JsonNode ValueNode(double v, double? vf)
        => vf is { } f ? new JsonArray(JsonValue.Create(v), JsonValue.Create(f)) : JsonValue.Create(v);

    public static JsonArray GraphPointNode(GraphPoint p)
    {
        var a = new JsonArray(JsonValue.Create(p.Y), ValueNode(p.V, p.Vf));
        if (p.Curve is { } c) a.Add(CurveNode(c));
        return a;
    }

    private static JsonArray Graph(IEnumerable<GraphPoint> points) => new(points.Select(p => (JsonNode)GraphPointNode(p)).ToArray());

    private static JsonArray ByPulse<T>(IEnumerable<PulseValue<T>> list, Func<T, JsonNode> value)
        => new(list.Select(p => (JsonNode)new JsonArray(JsonValue.Create(p.Y), value(p.Value))).ToArray());

    private static JsonArray Pulses(IEnumerable<long> ys) => new(ys.Select(y => (JsonNode)JsonValue.Create(y)).ToArray());

    // ------------------------------------------------------------ sections

    private static JsonObject Meta(Chart c)
    {
        var m = c.Meta;
        var o = new JsonObject { ["title"] = m.Title };
        AddIfSet(o, "title_translit", m.TitleTranslit);
        AddIfSet(o, "title_img_filename", m.TitleImgFilename);
        o["artist"] = m.Artist;
        AddIfSet(o, "artist_translit", m.ArtistTranslit);
        AddIfSet(o, "artist_img_filename", m.ArtistImgFilename);
        o["chart_author"] = m.ChartAuthor;
        o["difficulty"] = Math.Clamp(m.DifficultySlot, 0, 3);
        o["level"] = NumNode(m.Level);
        o["disp_bpm"] = m.DispBpm;
        if (m.StdBpm is { } std) o["std_bpm"] = std;
        AddIfSet(o, "jacket_filename", m.JacketFilename);
        AddIfSet(o, "jacket_author", m.JacketAuthor);
        AddIfSet(o, "icon_filename", m.IconFilename);
        AddIfSet(o, "information", m.Information);
        return o;
    }

    private static JsonObject Beat(BeatInfo b)
    {
        var o = new JsonObject
        {
            ["bpm"] = new JsonArray(b.Bpm.Select(x => (JsonNode)new JsonArray(JsonValue.Create(x.Y), JsonValue.Create(x.Bpm))).ToArray()),
        };
        AddIfAny(o, "time_sig", new JsonArray(b.TimeSignatures.Select(t => (JsonNode)new JsonArray(
            JsonValue.Create(t.MeasureIndex), new JsonArray(JsonValue.Create(t.Numerator), JsonValue.Create(t.Denominator)))).ToArray()));
        AddIfAny(o, "scroll_speed", Graph(b.ScrollSpeed));
        AddIfAny(o, "stop", new JsonArray(b.Stops.Select(s => (JsonNode)new JsonArray(JsonValue.Create(s.Y), JsonValue.Create(s.Length))).ToArray()));
        return o;
    }

    private static JsonArray NoteLanes(List<Note>[] lanes)
        => new(lanes.Select(l => (JsonNode)new JsonArray(l.Select(n => n.Length > 0
            ? (JsonNode)new JsonArray(JsonValue.Create(n.Y), JsonValue.Create(n.Length))
            : JsonValue.Create(n.Y)).ToArray())).ToArray());

    private static JsonObject Notes(NoteData n)
    {
        var o = new JsonObject
        {
            ["bt"] = NoteLanes(n.Bt),
            ["fx"] = NoteLanes(n.Fx),
            ["laser"] = new JsonArray(n.Lasers.Select(lane => (JsonNode)new JsonArray(lane.Select(s =>
            {
                var pts = new JsonArray(s.Points.Select(p =>
                {
                    var a = new JsonArray(JsonValue.Create(p.Ry), ValueNode(p.V, p.Vf));
                    if (p.Curve is { } cv) a.Add(CurveNode(cv));
                    return (JsonNode)a;
                }).ToArray());
                var sec = new JsonArray(JsonValue.Create(s.Y), pts);
                if (s.Width != 1) sec.Add(s.Width);
                return (JsonNode)sec;
            }).ToArray())).ToArray()),
        };
        return o;
    }

    private static JsonObject Audio(Chart c)
    {
        var a = c.Audio;
        var o = new JsonObject();

        var bgm = new JsonObject { ["filename"] = a.Bgm.Filename };
        if (a.Bgm.Volume != 1.0) bgm["vol"] = a.Bgm.Volume;
        if (a.Bgm.OffsetMs != 0) bgm["offset"] = a.Bgm.OffsetMs;
        var preview = new JsonObject();
        if (a.Bgm.PreviewOffsetMs != 0) preview["offset"] = a.Bgm.PreviewOffsetMs;
        if (a.Bgm.PreviewDurationMs != 15000) preview["duration"] = a.Bgm.PreviewDurationMs;
        AddIfAny(bgm, "preview", preview);
        if (a.Bgm.LegacyFpFilenames.Count > 0)
            bgm["legacy"] = new JsonObject { ["fp_filenames"] = new JsonArray(a.Bgm.LegacyFpFilenames.Select(f => (JsonNode)JsonValue.Create(f)).ToArray()) };
        if (bgm.Count > 1 || a.Bgm.Filename.Length > 0) o["bgm"] = bgm;

        var ks = new JsonObject();
        var chipEvents = new Dictionary<string, List<(long Y, double? Vol)>[]>();
        void AddChip(string name, int lane, long y, double? vol)
        {
            if (!chipEvents.TryGetValue(name, out var lanes)) chipEvents[name] = lanes = new[] { new List<(long, double?)>(), new List<(long, double?)>() };
            lanes[lane].Add((y, vol));
        }
        for (int lane = 0; lane < 2; lane++)
            foreach (var n in c.Notes.Fx[lane])
                if (n.KeySound is { } k) AddChip(k.Name, lane, n.Y, k.Volume);
        foreach (var orphan in a.KeySound.OrphanFxChipEvents) AddChip(orphan.Name, orphan.Lane, orphan.Y, orphan.Volume);
        if (chipEvents.Count > 0)
        {
            var ce = new JsonObject();
            foreach (var (name, lanes) in chipEvents)
                ce[name] = new JsonArray(lanes.Select(l => (JsonNode)new JsonArray(l.OrderBy(e => e.Y).Select(e => e.Vol is { } v
                    ? (JsonNode)new JsonArray(JsonValue.Create(e.Y), new JsonObject { ["vol"] = v })
                    : JsonValue.Create(e.Y)).ToArray())).ToArray());
            ks["fx"] = new JsonObject { ["chip_event"] = ce };
        }
        var ksl = new JsonObject();
        AddIfAny(ksl, "vol", ByPulse(a.KeySound.LaserVolume, v => JsonValue.Create(v)));
        if (a.KeySound.LaserSlamEvents.Count > 0)
        {
            var se = new JsonObject();
            foreach (var (name, ys) in a.KeySound.LaserSlamEvents) se[name] = Pulses(ys);
            ksl["slam_event"] = se;
        }
        if (a.KeySound.LaserVolumeAuto is { } va) ksl["legacy"] = new JsonObject { ["vol_auto"] = va };
        AddIfAny(ks, "laser", ksl);
        AddIfAny(o, "key_sound", ks);

        var ae = new JsonObject();
        var fx = new JsonObject();
        AddIfAny(fx, "def", Defs(a.FxEffects.Defs));
        AddIfAny(fx, "param_change", ParamChanges(a.FxEffects.ParamChanges));
        var longEvents = new Dictionary<string, List<(long Y, Dictionary<string, string> P)>[]>();
        void AddLong(string name, int lane, long y, Dictionary<string, string> p)
        {
            if (!longEvents.TryGetValue(name, out var lanes)) longEvents[name] = lanes = new[] { new List<(long, Dictionary<string, string>)>(), new List<(long, Dictionary<string, string>)>() };
            lanes[lane].Add((y, p));
        }
        for (int lane = 0; lane < 2; lane++)
            foreach (var n in c.Notes.Fx[lane])
                foreach (var e in n.Effects) AddLong(e.Name, lane, n.Y + e.RelY, e.Params);
        foreach (var orphan in a.FxEffects.OrphanLongEvents) AddLong(orphan.Name, orphan.Lane, orphan.Y, orphan.Params);
        if (longEvents.Count > 0)
        {
            var le = new JsonObject();
            foreach (var (name, lanes) in longEvents)
                le[name] = new JsonArray(lanes.Select(l => (JsonNode)new JsonArray(l.OrderBy(e => e.Y).Select(e => e.P.Count > 0
                    ? (JsonNode)new JsonArray(JsonValue.Create(e.Y), StringMap(e.P))
                    : JsonValue.Create(e.Y)).ToArray())).ToArray());
            fx["long_event"] = le;
        }
        AddIfAny(ae, "fx", fx);

        var laser = new JsonObject();
        var le2 = a.LaserEffects;
        AddIfAny(laser, "def", Defs(le2.Defs));
        AddIfAny(laser, "param_change", ParamChanges(le2.ParamChanges));
        if (le2.PulseEvents.Count > 0)
        {
            var pe = new JsonObject();
            foreach (var (name, ys) in le2.PulseEvents) pe[name] = Pulses(ys);
            laser["pulse_event"] = pe;
        }
        if (le2.PeakingFilterDelay is { } d) laser["peaking_filter_delay"] = d;
        if (le2.LegacyFilterGain.Count > 0)
            laser["legacy"] = new JsonObject { ["filter_gain"] = ByPulse(le2.LegacyFilterGain, v => JsonValue.Create(v)) };
        AddIfAny(ae, "laser", laser);
        AddIfAny(o, "audio_effect", ae);
        return o;
    }

    private static JsonObject StringMap(Dictionary<string, string> map)
    {
        var o = new JsonObject();
        foreach (var (k, v) in map) o[k] = v;
        return o;
    }

    private static JsonArray Defs(List<AudioEffectDef> defs)
        => new(defs.Select(d =>
        {
            var def = new JsonObject { ["type"] = d.Type };
            if (d.Params.Count > 0) def["v"] = StringMap(d.Params);
            return (JsonNode)new JsonArray(JsonValue.Create(d.Name), def);
        }).ToArray());

    private static JsonObject ParamChanges(Dictionary<string, Dictionary<string, List<PulseValue<string>>>> changes)
    {
        var o = new JsonObject();
        foreach (var (effect, prms) in changes)
        {
            var e = new JsonObject();
            foreach (var (param, list) in prms) e[param] = ByPulse(list, v => JsonValue.Create(v));
            o[effect] = e;
        }
        return o;
    }

    private static JsonNode TiltScalar(TiltValue v) => v.Auto is { } m ? JsonValue.Create(TiltValue.NameOf(m)) : JsonValue.Create(v.Manual);

    public static JsonArray TiltPointNode(TiltPoint t)
    {
        JsonNode value;
        if (t.Final is { } f && t.Curve is { } c1)
            value = new JsonArray(new JsonArray(TiltScalar(t.Value), TiltScalar(f)), CurveNode(c1));
        else if (t.Final is { } f2)
            value = new JsonArray(TiltScalar(t.Value), TiltScalar(f2));
        else if (t.Curve is { } c2)
            value = new JsonArray(TiltScalar(t.Value), CurveNode(c2));
        else
            value = TiltScalar(t.Value);
        return new JsonArray(JsonValue.Create(t.Y), value);
    }

    private static JsonArray Patterns(List<CamPattern> list, bool swing)
        => new(list.Select(p =>
        {
            var a = new JsonArray(JsonValue.Create(p.Y), JsonValue.Create(p.Direction), JsonValue.Create(p.Length));
            if (swing && (p.SwingScale != null || p.SwingRepeat != null || p.SwingDecayOrder != null))
            {
                var prm = new JsonObject();
                if (p.SwingScale is { } s) prm["scale"] = NumNode(s);
                if (p.SwingRepeat is { } r) prm["repeat"] = r;
                if (p.SwingDecayOrder is { } d) prm["decay_order"] = d;
                a.Add(prm);
            }
            return (JsonNode)a;
        }).ToArray());

    private static JsonObject Camera(CameraInfo c)
    {
        var o = new JsonObject();
        AddIfAny(o, "tilt", new JsonArray(c.Tilt.Select(t => (JsonNode)TiltPointNode(t)).ToArray()));
        var body = new JsonObject();
        AddIfAny(body, "zoom_top", Graph(c.Body.ZoomTop));
        AddIfAny(body, "zoom_bottom", Graph(c.Body.ZoomBottom));
        AddIfAny(body, "zoom_side", Graph(c.Body.ZoomSide));
        AddIfAny(body, "center_split", Graph(c.Body.CenterSplit));
        AddIfAny(body, "rotation_deg", Graph(c.Body.RotationDeg));
        var se = new JsonObject();
        AddIfAny(se, "spin", Patterns(c.Spin, false));
        AddIfAny(se, "half_spin", Patterns(c.HalfSpin, false));
        AddIfAny(se, "swing", Patterns(c.Swing, true));
        var cam = new JsonObject();
        AddIfAny(cam, "body", body);
        if (se.Count > 0) cam["pattern"] = new JsonObject { ["laser"] = new JsonObject { ["slam_event"] = se } };
        AddIfAny(o, "cam", cam);
        return o;
    }

    private static JsonObject Bg(BgInfo b)
    {
        var o = new JsonObject();
        AddIfSet(o, "filename", b.Filename);
        var lg = new JsonObject();
        AddIfAny(lg, "bg", new JsonArray(b.LegacyBg.Select(f => (JsonNode)new JsonObject { ["filename"] = f }).ToArray()));
        if (b.Layer is { } layer)
        {
            var l = new JsonObject { ["filename"] = layer.Filename };
            if (layer.DurationMs != 0) l["duration"] = layer.DurationMs;
            if (!layer.RotateTilt || !layer.RotateSpin)
                l["rotation"] = new JsonObject { ["tilt"] = layer.RotateTilt, ["spin"] = layer.RotateSpin };
            lg["layer"] = l;
        }
        if (b.Movie is { } m)
        {
            var mv = new JsonObject { ["filename"] = m.Filename };
            if (m.OffsetMs != 0) mv["offset"] = m.OffsetMs;
            lg["movie"] = mv;
        }
        AddIfAny(o, "legacy", lg);
        return o;
    }

    private static JsonObject Editor(EditorInfo e)
    {
        var o = new JsonObject();
        AddIfSet(o, "app_name", e.AppName);
        AddIfSet(o, "app_version", e.AppVersion);
        AddIfAny(o, "comment", ByPulse(e.Comments, v => JsonValue.Create(v)));
        return o;
    }

    private static JsonObject Compat(CompatInfo c)
    {
        var o = new JsonObject();
        AddIfSet(o, "ksh_version", c.KshVersion);
        var ku = new JsonObject();
        if (c.KshUnknownMeta.Count > 0) ku["meta"] = StringMap(c.KshUnknownMeta);
        if (c.KshUnknownOption.Count > 0)
        {
            var opt = new JsonObject();
            foreach (var (k, list) in c.KshUnknownOption) opt[k] = ByPulse(list, v => JsonValue.Create(v));
            ku["option"] = opt;
        }
        AddIfAny(ku, "line", ByPulse(c.KshUnknownLine, v => JsonValue.Create(v)));
        AddIfAny(o, "ksh_unknown", ku);
        return o;
    }

    /// <summary><c>impl["sdvx-editor"].vox</c>: bg_no, post_effect rows <c>[y, name, param, from, to, length]</c>, sp_controller rows <c>[y, name, type, length, p1..p4]</c>.</summary>
    private static JsonObject VoxExtras(VoxExtrasInfo v)
    {
        var o = new JsonObject();
        if (v.BgNo is { } bg) o["bg_no"] = bg;
        AddIfAny(o, "post_effect", new JsonArray(v.PostEffects.Select(p =>
        {
            // [y, name, param, from, to, length] + [stage, index] + [length_ms] only when needed (0.17 readers take [:6])
            var row = new JsonArray(JsonValue.Create(p.Y), JsonValue.Create(p.Name), JsonValue.Create(p.Param), NumNode(p.From), NumNode(p.To),
                JsonValue.Create(p.Length));
            if (p.Stage != 0 || p.Index != 0 || p.LengthMs != null)
            {
                row.Add(p.Stage);
                row.Add(p.Index);
            }
            if (p.LengthMs is { } ms) row.Add(NumNode(ms));
            return (JsonNode)row;
        }).ToArray()));
        AddIfAny(o, "sp_controller", new JsonArray(v.SpController.Select(r => (JsonNode)new JsonArray(
            JsonValue.Create(r.Y), JsonValue.Create(r.Name), JsonValue.Create(r.Type), JsonValue.Create(r.Length),
            NumNode(r.P1), NumNode(r.P2), NumNode(r.P3), NumNode(r.P4))).ToArray()));
        AddIfAny(o, "commands", new JsonArray(v.Commands.Select(r => (JsonNode)new JsonArray(
            JsonValue.Create(r.Y), JsonValue.Create(r.Name), JsonValue.Create(r.Type), JsonValue.Create(r.Length),
            JsonValue.Create(r.P1), JsonValue.Create(r.P2), JsonValue.Create(r.P3), JsonValue.Create(r.P4))).ToArray()));
        var bo = new JsonObject();
        if (v.RepresentativeBpm is { } rb) bo["RepresentativeBpm"] = NumNode(rb);
        if (v.ConstantScroll is { } cs) bo["ConstantScroll"] = cs;
        AddIfAny(o, "bpm_option", bo);
        AddIfAny(o, "scripts", new JsonArray(v.Scripts.Select(s =>
        {
            var row = new JsonArray(JsonValue.Create(s.Id), new JsonArray(s.Lines.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()));
            if (s.Name.Length > 0) row.Add(s.Name);
            return (JsonNode)row;
        }).ToArray()));
        AddIfAny(o, "scripted", new JsonArray(v.Scripted.Select(s => (JsonNode)new JsonArray(
            JsonValue.Create(s.Y), JsonValue.Create(s.Track), new JsonArray(s.Ids.Select(i => (JsonNode)JsonValue.Create(i)).ToArray()))).ToArray()));
        return o;
    }

    /// <summary>
    /// <c>[y, label]</c>, or <c>[y, label, "#rrggbb"]</c> with a colour. Readers that only know <c>[y, v]</c>
    /// (editor 0.1.0) still get the label.
    /// </summary>
    private static JsonArray Bookmarks(BookmarkList list) => new(list.Select(b =>
    {
        var e = new JsonArray(JsonValue.Create(b.Y), JsonValue.Create(Bookmark.CleanLabel(b.Value.Label)));
        if (Bookmark.NormaliseColour(b.Value.Colour) is { } colour) e.Add(JsonValue.Create(colour));
        return (JsonNode)e;
    }).ToArray());

    /// <summary>
    /// <c>impl["sdvx-editor"].laser_paths</c>: per laser lane <c>[[y, [anchor, …]], …]</c> for every section with a curved
    /// path; anchor = <c>[ry, v | [v, vf], type | null]</c> + optional <c>auto</c> (0/1) and ease <c>[a, b]</c>; type is
    /// <c>"linear"</c>, <c>"perfect"</c> or <c>"bezier"</c>, null = inherit. The section's points stay the bake, so other
    /// readers see the same shape.
    /// </summary>
    private static JsonArray? LaserPathsNode(NoteData notes)
    {
        bool any = false;
        var lanes = new JsonArray();
        foreach (var lane in notes.Lasers)
        {
            var entries = new JsonArray();
            foreach (var s in lane)
            {
                if (LaserPaths.ValidPath(s) is not { IsTrivial: false } path) continue;
                any = true;
                entries.Add(new JsonArray(JsonValue.Create(s.Y), AnchorsNode(path)));
            }
            lanes.Add(entries);
        }

        return any ? lanes : null;
    }

    /// <summary>Anchor list <c>[[ry, v | [v, vf], type | null, auto?, [a, b]?], …]</c> (shared by laser and camera paths).</summary>
    private static JsonArray AnchorsNode(LaserPath path) => new(path.Anchors.Select(a =>
    {
        var n = new JsonArray(JsonValue.Create(a.Ry), ValueNode(a.V, a.IsSlam ? a.Vf : null),
            a.Type is { } t ? JsonValue.Create(TypeName(t)) : null);
        if (a.Auto || a.Ease != null) n.Add(a.Auto ? 1 : 0);
        if (a.Ease is { } e) n.Add(CurveNode(e));
        return (JsonNode)n;
    }).ToArray());

    /// <summary>
    /// <c>impl["sdvx-editor"].camera_paths</c>: per camera lane name (<c>zoom_top</c> … <c>rotation_deg</c>, <c>tilt</c>)
    /// <c>[[y, [anchor, …]], …]</c> for every curved run (anchor pulses relative to y, the run's first point); the graph /
    /// tilt points stay the bake, so other readers see the same shape.
    /// </summary>
    private static JsonObject? CameraPathsNode(CameraInfo cam)
    {
        var o = new JsonObject();
        foreach (var group in CameraPaths.ValidPaths(cam).GroupBy(p => p.Lane).OrderBy(g => g.Key))
            o[CameraPaths.Name(group.Key)] = new JsonArray(group.OrderBy(p => p.Y)
                .Select(p => (JsonNode)new JsonArray(JsonValue.Create(p.Y), AnchorsNode(p.Path))).ToArray());
        return o.Count > 0 ? o : null;
    }

    public static string TypeName(LaserCurveType t) => t switch
    {
        LaserCurveType.PerfectCurve => "perfect",
        LaserCurveType.Bezier => "bezier",
        _ => "linear",
    };

    private static JsonObject Impl(Chart c)
    {
        var ours = new JsonObject();
        if (c.EndPulse is { } end) ours["end_pulse"] = end;
        if (c.Meta.DifficultySlot > 3 || c.Meta.DifficultyName != null)
        {
            var d = new JsonObject { ["slot"] = c.Meta.DifficultySlot };
            if (c.Meta.DifficultyName != null) d["name"] = c.Meta.DifficultyName;
            ours["difficulty"] = d;
        }
        AddIfAny(ours, "bookmarks", Bookmarks(c.Editor.Bookmarks));
        AddIfAny(ours, "song_sources", new JsonArray(c.Editor.SongSources.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()));
        AddIfAny(ours, "vox", VoxExtras(c.VoxExtras));
        if (LaserPathsNode(c.Notes) is { } paths) ours["laser_paths"] = paths;
        if (CameraPathsNode(c.Camera) is { } cameraPaths) ours["camera_paths"] = cameraPaths;
        var impl = new JsonObject();
        AddIfAny(impl, KsonReader.ImplKey, ours);
        return impl;
    }
}
