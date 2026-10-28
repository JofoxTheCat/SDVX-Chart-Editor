using System.Text.Json.Nodes;
using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class KsonFormatTests
{
    private static IEnumerable<TestCaseData> KsonFiles() => RealCharts.Cases(".kson", "RealChartRoundTripIsLossless");

    [TestCaseSource(nameof(KsonFiles))]
    public void RealChartRoundTripIsLossless(string path)
    {
        RealCharts.RequireFile(path);
        var a = KsonReader.ReadFile(path);
        var b = KsonReader.Read(KsonWriter.Write(a));
        ModelSnapshot.AssertEqual(a, b, "kson round trip");
        Assert.That(a.Extra.Count, Is.EqualTo(0), "the K-Shoot Editor file should be fully understood: " + a.Extra.ToJsonString());
        TestContext.Out.WriteLine(ChartFacts.Describe(a));
    }

    /// <summary>
    /// The pair in the local library that exists in both formats. It is a K-Shoot Editor v2 export, so the
    /// ksh carries baked curve samples and was edited slightly apart from the kson (see the list below).
    /// Everything else must agree exactly.
    /// </summary>
    [Test]
    public void AbyssMotionKshAndKsonAgree()
    {
        string dir = Path.Combine(RealCharts.RequireRoot(), "ABYSS MOTION 192.333");
        if (!Directory.Exists(dir)) Assert.Ignore("ABYSS MOTION not in the local library");
        var ksh = KshReader.ReadFile(Path.Combine(dir, "ABYSS MOTION 192.333.ksh"));
        var kson = KsonReader.ReadFile(Path.Combine(dir, "ABYSS MOTION 192.333.kson"));
        TestContext.Out.WriteLine("ksh : " + ChartFacts.Describe(ksh));
        TestContext.Out.WriteLine("kson: " + ChartFacts.Describe(kson));

        // Notes: identical, including hold lengths (numbers also match ksh2vox: bt 130/240/216/151, fx 97/115).
        ModelSnapshot.AssertEqual(StripFx(kson.Notes.Bt), StripFx(ksh.Notes.Bt), "BT");
        ModelSnapshot.AssertEqual(StripFx(kson.Notes.Fx), StripFx(ksh.Notes.Fx), "FX");
        Assert.That(ksh.Notes.Bt.Select(l => l.Count), Is.EqualTo(new[] { 130, 240, 216, 151 }));
        Assert.That(ksh.Notes.Fx.Select(l => l.Count), Is.EqualTo(new[] { 97, 115 }));

        // FX effects and key sounds land on the same notes. One hold differs between the files: the ksh
        // says fx-l=2r;8 where the kson has 4r (there is no "2r" event in the kson at all).
        var effectDiffs = new List<string>();
        for (int lane = 0; lane < 2; lane++)
            for (int i = 0; i < kson.Notes.Fx[lane].Count; i++)
            {
                var a = kson.Notes.Fx[lane][i];
                var b = ksh.Notes.Fx[lane][i];
                Assert.That(b.KeySound, Is.EqualTo(a.KeySound), $"key sound lane {lane} at {a.Y}");
                if (ModelSnapshot.FirstDifference(ModelSnapshot.Of(a.Effects), ModelSnapshot.Of(b.Effects)) != null)
                    effectDiffs.Add($"{lane}:{a.Y}:{string.Join(",", a.Effects.Select(e => e.Name))}->{string.Join(",", b.Effects.Select(e => e.Name))}");
            }
        Assert.That(effectDiffs, Is.EqualTo(new[] { "0:5040:4r->2r" }));
        Assert.That(kson.Notes.Fx.Sum(l => l.Sum(n => n.Effects.Count)), Is.EqualTo(32));
        Assert.That(kson.Notes.Fx.Sum(l => l.Count(n => n.KeySound != null)), Is.EqualTo(16));
        Assert.That(ksh.Audio.FxEffects.OrphanLongEvents, Is.Empty);
        Assert.That(ksh.Audio.KeySound.OrphanFxChipEvents, Is.Empty);
        ModelSnapshot.AssertEqual(kson.Audio.FxEffects.Defs, ksh.Audio.FxEffects.Defs, "fx defs");
        ModelSnapshot.AssertEqual(kson.Audio.LaserEffects.Defs, ksh.Audio.LaserEffects.Defs, "laser defs");
        ModelSnapshot.AssertEqual(kson.Camera.Spin, ksh.Camera.Spin, "spins");
        ModelSnapshot.AssertEqual(kson.Beat.ScrollSpeed, ksh.Beat.ScrollSpeed, "scroll speed");
        ModelSnapshot.AssertEqual(kson.Camera.Body, ksh.Camera.Body, "camera graphs");

        // Lasers: section by section. Known differences between the two files:
        var known = new HashSet<(int, long)>
        {
            (0, 36480), // a one-slam section only the ksh has
            (0, 63120), // a baked sample overshoots the end value, so it is kept as a point
            (1, 25320), // a baked sample equals the end value (not strictly between)
            (1, 63120), // as (0, 63120)
        };
        // (1, 73680) — kson points every 5 pulses — and (1, 79680) — two points 20 pulses apart — used to
        // read back as 29 tiny slams; the reader's dense-movement rule makes them match exactly now.
        var valueDiffs = new List<string>();
        for (int lane = 0; lane < 2; lane++)
        {
            var kshSections = ksh.Notes.Lasers[lane].ToDictionary(s => s.Y);
            var ksonSections = kson.Notes.Lasers[lane].ToDictionary(s => s.Y);
            foreach (var y in kshSections.Keys.Union(ksonSections.Keys))
            {
                if (known.Contains((lane, y))) continue;
                Assert.That(kshSections.ContainsKey(y) && ksonSections.ContainsKey(y), $"lane {lane}: section {y} missing on one side");
                var a = ksonSections[y];
                var b = kshSections[y];
                Assert.That(b.Width, Is.EqualTo(a.Width), $"lane {lane} {y} width");
                Assert.That(b.Points.Count, Is.EqualTo(a.Points.Count), $"lane {lane} {y} point count");
                for (int i = 0; i < a.Points.Count; i++)
                {
                    var pa = a.Points[i];
                    var pb = b.Points[i];
                    Assert.That(pb.Ry, Is.EqualTo(pa.Ry), $"lane {lane} {y} point {i} ry");
                    if (Math.Abs(pb.V - pa.V) > 0.0101 || (pa.IsSlam && Math.Abs(pb.Vf!.Value - pa.Vf!.Value) > 0.0101))
                        valueDiffs.Add($"{lane}:{y + pa.Ry}");
                    Assert.That(pb.IsSlam, Is.EqualTo(pa.IsSlam), $"lane {lane} {y} point {i} slam");
                    // K-Shoot writes ksh curve values with 3 decimals
                    Assert.That(pb.Curve.HasValue, Is.EqualTo(pa.Curve.HasValue), $"lane {lane} {y} point {i} curve");
                    if (pa.Curve is { } ca && pb.Curve is { } cb)
                        Assert.That(Math.Max(Math.Abs(ca.A - cb.A), Math.Abs(ca.B - cb.B)), Is.LessThanOrEqualTo(0.0005 + 1e-9), $"lane {lane} {y} point {i} curve");
                }
            }
        }
        TestContext.Out.WriteLine($"laser values differing by more than ksh quantisation: {valueDiffs.Count} ({string.Join(" ", valueDiffs)})");
        // 9 of ~430 matched points carry a different value in the two files (mostly curve start points
        // whose ksh value is a curve sample); pinned so any reader change shows up.
        Assert.That(valueDiffs, Is.EqualTo(new[] { "0:21360", "0:40680", "0:41640", "0:42240", "0:94440", "1:32280", "1:42240", "1:84720", "1:94440" }));
        // Totals, for the report: kson L0 62/199/80; ksh differs only by the listed sections.
        Assert.That(kson.Notes.Lasers[0].Sum(s => s.Points.Count), Is.EqualTo(199));
        Assert.That(kson.Notes.Lasers[0].Sum(s => s.SlamCount), Is.EqualTo(80));

        // Slams, position by position: the ksh has exactly the kson's (80 / 100) plus the one-slam section
        // only it has. Before the dense-movement rule the ksh read 29 more on lane R (129).
        var kshSlams = SlamPulses(ksh);
        var ksonSlams = SlamPulses(kson);
        Assert.That(ksonSlams.Select(l => l.Count), Is.EqualTo(new[] { 80, 100 }));
        Assert.That(kshSlams[0].Except(ksonSlams[0]), Is.EqualTo(new[] { 36480L }));
        Assert.That(ksonSlams[0].Except(kshSlams[0]), Is.Empty);
        Assert.That(kshSlams[1], Is.EqualTo(ksonSlams[1]));
    }

    private static List<long>[] SlamPulses(Chart c)
        => c.Notes.Lasers.Select(lane => lane.SelectMany(s => s.Points.Where(p => p.IsSlam).Select(p => s.Y + p.Ry)).ToList()).ToArray();

    /// <summary>
    /// CLAUDE.md §8a: ΛZΛLEΛ MXM, present in both formats, has 157 ksh points vs 127 kson points on lane 0
    /// (30 slams) and otherwise identical notes. Runs only where that chart is in the library.
    /// </summary>
    [Test]
    public void AzaleaKshAndKsonAgree()
    {
        string root = RealCharts.RequireRoot();
        var kson = Directory.EnumerateFiles(root, "*.kson", SearchOption.AllDirectories)
                            .FirstOrDefault(f => f.Contains("ZΛLE", StringComparison.Ordinal) || f.Contains("azalea", StringComparison.OrdinalIgnoreCase));
        string? ksh = kson == null ? null : Path.ChangeExtension(kson, ".ksh");
        if (kson == null || !File.Exists(ksh)) Assert.Ignore("ΛZΛLEΛ (.ksh + .kson) is not in the local library");
        var a = KshReader.ReadFile(ksh!);
        var b = KsonReader.ReadFile(kson);
        ModelSnapshot.AssertEqual(StripFx(b.Notes.Bt), StripFx(a.Notes.Bt), "BT");
        ModelSnapshot.AssertEqual(StripFx(b.Notes.Fx), StripFx(a.Notes.Fx), "FX");
        Assert.That(b.Notes.Lasers[0].Sum(s => s.Points.Count), Is.EqualTo(127));
        Assert.That(b.Notes.Lasers[0].Sum(s => s.SlamCount), Is.EqualTo(30));
        // ksh lines on lane 0 = model points + one extra line per slam
        Assert.That(a.Notes.Lasers[0].Sum(s => s.Points.Count + s.SlamCount), Is.EqualTo(157));
        Assert.That(a.Notes.Lasers[0].Sum(s => s.SlamCount), Is.EqualTo(30));
        for (int lane = 0; lane < 2; lane++)
            Assert.That(a.Notes.Lasers[lane].Count, Is.EqualTo(b.Notes.Lasers[lane].Count));
    }

    private static List<(long, long)>[] StripFx(List<Note>[] lanes) => lanes.Select(l => l.Select(n => (n.Y, n.Length)).ToList()).ToArray();

    [Test]
    public void SyntheticChartWithEveryFeatureRoundTrips()
    {
        var c = FullChart();
        string json = KsonWriter.Write(c, indented: true);
        var b = KsonReader.Read(json);
        ModelSnapshot.AssertEqual(c, b, "kson round trip");
        Assert.That(KsonWriter.Write(b, indented: true), Is.EqualTo(json));
    }

    [Test]
    public void UnknownKeysArePreserved()
    {
        string json = """
        {
          "format_version": 1,
          "meta": { "title": "t", "artist": "a", "chart_author": "c", "difficulty": 2, "level": 12, "disp_bpm": "150", "x_custom": [1, 2] },
          "beat": { "bpm": [[0, 150.0]], "future_thing": { "k": true } },
          "note": { "bt": [[0], [], [], []] },
          "camera": { "tilt": { "scale": [[0, 1.0]] } },
          "new_top_level": "kept",
          "impl": { "other-tool": { "x": 1 }, "sdvx-editor": { "end_pulse": 3840, "difficulty": { "slot": 4 } } }
        }
        """;
        var c = KsonReader.Read(json);
        Assert.That(c.Meta.DifficultySlot, Is.EqualTo(4));
        Assert.That(c.EndPulse, Is.EqualTo(3840));
        var back = KsonWriter.ToJson(c);
        Assert.That(back["meta"]!["x_custom"]!.ToJsonString(), Is.EqualTo("[1,2]"));
        Assert.That(back["beat"]!["future_thing"]!["k"]!.GetValue<bool>(), Is.True);
        Assert.That(back["camera"]!["tilt"]!["scale"], Is.Not.Null, "old-draft tilt object is kept verbatim");
        Assert.That(back["new_top_level"]!.GetValue<string>(), Is.EqualTo("kept"));
        Assert.That(back["impl"]!["other-tool"]!["x"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(back["meta"]!["difficulty"]!.GetValue<int>(), Is.EqualTo(3), "kson stores MXM as 3");
        ModelSnapshot.AssertEqual(c, KsonReader.Read(back.ToJsonString()), "round trip with extras");
    }

    [Test]
    public void OlderDraftShapesAreRead()
    {
        string json = """
        {
          "meta": { "title": "t", "artist": "a", "chart_author": "c", "difficulty": { "idx": 1, "name": "ADV" }, "level": 7, "disp_bpm": "" },
          "beat": { "bpm": [{ "y": 0, "v": 128 }], "time_sig": [[0, 3, 4], { "idx": 2, "v": { "n": 7, "d": 8 } }] },
          "note": {
            "bt": [[{ "y": 0, "l": 240 }], [], [], []],
            "laser": [[{ "y": 0, "v": [{ "ry": 0, "v": 0.0, "vf": 1.0 }, { "ry": 240, "v": 0.5, "a": 0.2, "b": 0.8 }], "w": 2 }], []]
          },
          "audio": { "audio_effect": { "fx": { "def": { "myfx": { "type": "gate", "v": { "rate": "50%" } } } } } },
          "camera": { "cam": { "body": { "zoom_size": [[0, 10]], "split": [[0, 5]] } } }
        }
        """;
        var c = KsonReader.Read(json);
        Assert.That(c.Meta.DifficultySlot, Is.EqualTo(1));
        Assert.That(c.Beat.Bpm.Single().Bpm, Is.EqualTo(128));
        Assert.That(c.Beat.TimeSignatures.Select(t => (t.MeasureIndex, t.Numerator, t.Denominator)), Is.EqualTo(new[] { (0, 3, 4), (2, 7, 8) }));
        Assert.That(c.Notes.Bt[0].Single().Length, Is.EqualTo(240));
        var s = c.Notes.Lasers[0].Single();
        Assert.That(s.Width, Is.EqualTo(2));
        Assert.That(s.Points[0].IsSlam, Is.True);
        Assert.That(s.Points[1].Curve, Is.EqualTo(new Curve(0.2, 0.8)));
        Assert.That(c.Audio.FxEffects.Defs.Single().Name, Is.EqualTo("myfx"));
        Assert.That(c.Camera.Body.ZoomSide.Single().V, Is.EqualTo(10));
        Assert.That(c.Camera.Body.CenterSplit.Single().V, Is.EqualTo(5));
        Assert.That(c.Extra.Count, Is.EqualTo(0));
    }

    /// <summary>kson → ksh → model: everything survives except the ksh column of the export loss matrix.</summary>
    [Test]
    public void KsonToKshDropsOnlyTheMatrixLosses()
    {
        var src = FullChart();
        var back = KshReader.Read(KshWriter.Write(src));
        ModelSnapshot.AssertEqual(ExpectedAfterKsh(src), back, "kson → ksh");
    }

    [Test]
    public void AbyssKsonToKshDropsOnlyTheMatrixLosses()
    {
        string path = Path.Combine(RealCharts.RequireRoot(), "ABYSS MOTION 192.333", "ABYSS MOTION 192.333.kson");
        RealCharts.RequireFile(path);
        var src = KsonReader.ReadFile(path);
        var back = KshReader.Read(KshWriter.Write(src), new KshReadOptions { CollapseBakedCurves = false });
        // Including the sub-1/32 point runs at lane R 73680/79680, which used to be excluded here because
        // they read back as slams (see KshReader.IsSlam).
        ModelSnapshot.AssertEqual(ExpectedAfterKsh(src), back, "ABYSS kson → ksh");
    }

    /// <summary>Applies exactly the documented ksh losses to a model.</summary>
    private static Chart ExpectedAfterKsh(Chart src)
    {
        var e = KsonReader.Read(KsonWriter.Write(src)); // deep copy
        e.Extra = new JsonObject();
        e.FormatVersion = 1;
        e.Editor.AppName = "";
        e.Editor.AppVersion = "";
        // bookmarks are no longer a ksh loss: they travel as //#bookmark comment lines (CollabTests)
        e.Bg.Filename = "";
        e.Meta.DifficultySlot = Math.Min(e.Meta.DifficultySlot, 3);
        e.Meta.DifficultyName = null;
        if (e.Meta.DispBpm.Length == 0)
        {
            var bpms = e.Beat.Bpm.Select(b => b.Bpm).ToList();
            e.Meta.DispBpm = bpms.Min() == bpms.Max()
                ? bpms[0].ToString(System.Globalization.CultureInfo.InvariantCulture)
                : $"{bpms.Min().ToString(System.Globalization.CultureInfo.InvariantCulture)}-{bpms.Max().ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
        if (e.Compat.KshVersion.Length == 0) e.Compat.KshVersion = "171";
        if (e.Meta.StdBpm == 0) e.Meta.StdBpm = null;
        if (e.Beat.TimeSignatures.Count == 0) e.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        // laser values quantised to 51 steps; [v, v] is not a slam
        foreach (var lane in e.Notes.Lasers)
            foreach (var s in lane)
                foreach (var p in s.Points)
                {
                    p.V = Math.Round(p.V * 50, MidpointRounding.AwayFromZero) / 50;
                    if (p.Vf is { } vf) p.Vf = Math.Round(vf * 50, MidpointRounding.AwayFromZero) / 50;
                    if (p.Vf == p.V) p.Vf = null;
                }
        // the ksh reader states the chart end (the last bar line)
        var timing = new Timing.TimingMap(e.Beat);
        long last = Math.Max(src.LastEventPulse(), 0);
        e.EndPulse ??= timing.MeasureStart(timing.MeasureAt(last) + 1);
        return e;
    }

    /// <summary>A chart that uses every model feature at least once.</summary>
    public static Chart FullChart()
    {
        var c = new Chart();
        c.Meta = new ChartMeta
        {
            Title = "Full ∇ Test", TitleTranslit = "full test", TitleImgFilename = "title.png",
            Artist = "Artist", ArtistTranslit = "artist", ArtistImgFilename = "artist.png",
            ChartAuthor = "Charter", DifficultySlot = 4, DifficultyName = "MXM", Level = 18.5, DispBpm = "",
            StdBpm = 150, JacketFilename = "jk.png", JacketAuthor = "Illust", IconFilename = "icon.png", Information = "info",
        };
        c.Beat.Bpm.Add(new BpmChange(0, 150));
        c.Beat.Bpm.Add(new BpmChange(3840, 200.5));
        c.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        c.Beat.TimeSignatures.Add(new TimeSignature(4, 6, 8));
        c.Beat.ScrollSpeed.Add(new GraphPoint(0, 1));
        c.Beat.ScrollSpeed.Add(new GraphPoint(960, 1, 2, new Curve(0.25, 0.75)));
        c.Beat.ScrollSpeed.Add(new GraphPoint(1920, 0.5));
        c.Beat.Stops.Add(new Stop(2880, 240));
        c.GaugeTotal = 0;
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[1].Add(new Note(240, 480));
        c.Notes.Bt[2].Add(new Note(1200));
        c.Notes.Bt[3].Add(new Note(4200, 120)); // inside the 6/8 part
        c.Notes.Fx[0].Add(new Note(480, 480) { Effects = { new FxEffectEvent("retrigger", 0, new() { ["wave_length"] = "1/16" }), new FxEffectEvent("myfx", 240) } });
        c.Notes.Fx[1].Add(new Note(960) { KeySound = new KeySoundRef("clap", 0.6) });
        c.Notes.Fx[1].Add(new Note(1440) { KeySound = new KeySoundRef("hit.wav") });
        c.Notes.Lasers[0].Add(new LaserSection(0, new[]
        {
            new LaserPoint(0, 0), new LaserPoint(240, 1, null, new Curve(0.3, 0.7)), new LaserPoint(480, 0.2, 0.8), new LaserPoint(960, 0.5),
        }));
        c.Notes.Lasers[1].Add(new LaserSection(1920, new[] { new LaserPoint(0, 0.25, 0.75) }, 2));
        c.Notes.Lasers[1].Add(new LaserSection(2400, new[] { new LaserPoint(0, 1), new LaserPoint(120, 0) }));
        c.Audio.Bgm = new BgmInfo { Filename = "song.ogg", Volume = 0.75, OffsetMs = -40, PreviewOffsetMs = 30000, PreviewDurationMs = 12000, LegacyFpFilenames = { "song_f.ogg" } };
        c.Audio.KeySound.LaserVolume.Add(new PulseValue<double>(0, 0.5));
        c.Audio.KeySound.LaserVolume.Add(new PulseValue<double>(1920, 0.8));
        c.Audio.KeySound.LaserSlamEvents["slam_down"] = new List<long> { 480 };
        c.Audio.KeySound.LaserVolumeAuto = false;
        c.Audio.FxEffects.Defs.Add(new AudioEffectDef("myfx", "gate", new() { ["rate"] = "60%", ["wave_length"] = "1/8" }));
        c.Audio.FxEffects.ParamChanges["myfx"] = new() { ["rate"] = new() { new PulseValue<string>(720, "80%") } };
        c.Audio.LaserEffects.Defs.Add(new AudioEffectDef("lpf", "low_pass_filter", new() { ["freq_max"] = "8000Hz", ["q"] = "1.5" }));
        c.Audio.LaserEffects.PulseEvents["peaking_filter"] = new List<long> { 0 };
        c.Audio.LaserEffects.PulseEvents["lpf"] = new List<long> { 1920 };
        c.Audio.LaserEffects.ParamChanges["lpf"] = new() { ["q"] = new() { new PulseValue<string>(2160, "2.0") } };
        c.Audio.LaserEffects.PeakingFilterDelay = 60;
        c.Audio.LaserEffects.LegacyFilterGain.Add(new PulseValue<double>(0, 0.5));
        c.Camera.Tilt.Add(new TiltPoint(0, TiltValue.Of(TiltMode.Normal)));
        c.Camera.Tilt.Add(new TiltPoint(960, TiltValue.Of(0.5), TiltValue.Of(-0.5)));
        c.Camera.Tilt.Add(new TiltPoint(1440, TiltValue.Of(0.25), null, new Curve(0.1, 0.9)));
        c.Camera.Tilt.Add(new TiltPoint(1920, TiltValue.Of(1), TiltValue.Of(TiltMode.KeepBiggest)));
        c.Camera.Body.ZoomTop.Add(new GraphPoint(0, 0));
        c.Camera.Body.ZoomTop.Add(new GraphPoint(480, 100, -100));
        c.Camera.Body.ZoomBottom.Add(new GraphPoint(0, 0, null, new Curve(0.8, 0.1)));
        c.Camera.Body.ZoomBottom.Add(new GraphPoint(960, 50));
        c.Camera.Body.ZoomSide.Add(new GraphPoint(240, 20));
        c.Camera.Body.CenterSplit.Add(new GraphPoint(240, 100));
        c.Camera.Body.RotationDeg.Add(new GraphPoint(720, 45));
        c.Camera.Spin.Add(new CamPattern(480, -1, 960));
        c.Camera.HalfSpin.Add(new CamPattern(1920, 1, 240));
        c.Camera.Swing.Add(new CamPattern(2400, -1, 240) { SwingScale = 150, SwingRepeat = 3, SwingDecayOrder = 2 });
        c.Bg = new BgInfo
        {
            Filename = "bg.png", LegacyBg = { "desert", "night" },
            Layer = new BgLayer { Filename = "arrow", DurationMs = 500, RotateTilt = true, RotateSpin = false },
            Movie = new BgMovie { Filename = "movie.mp4", OffsetMs = 120 },
        };
        c.Editor = new EditorInfo { AppName = "SDVX Chart Editor", AppVersion = "0.1.0" };
        c.Editor.Comments.Add(new PulseValue<string>(960, "chorus"));
        c.Editor.Bookmarks.Add(new PulseValue<string>(1920, "drop"));
        c.Compat.KshVersion = "171";
        c.Compat.KshUnknownMeta["custom_header"] = "1";
        c.Compat.KshUnknownOption["custom_option"] = new() { new PulseValue<string>(720, "x") };
        c.Compat.KshUnknownLine.Add(new PulseValue<string>(1200, "#unknown line"));
        return c;
    }
}
