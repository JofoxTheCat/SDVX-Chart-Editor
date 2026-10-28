using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.Radar;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// The C# skill radar against ksh2vox's radar.py: synthetic cases with hand-computed numbers, and — when Python and
/// the local converter checkout are present — every chart of the local library run through both, ±1 per axis.
/// </summary>
public class RadarTests
{
    private static Chart chart(double bpm = 120)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, bpm));
        c.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        return c;
    }

    [Test]
    public void EmptyChartIsAllZero()
    {
        var c = chart();
        c.EndPulse = 960 * 4;
        Assert.That(RadarCalculator.Compute(c, RadarSource.Kson), Is.EqualTo(new SkillRadar(0, 0, 0, 0, 0, 0)));
    }

    [Test]
    public void NotesAndPeakFollowTheRadarTsFormulas()
    {
        // 120 BPM: one quarter = 0.5 s. 8 chips on BT A, one per quarter, over 4 measures (8 s).
        var c = chart();
        for (int i = 0; i < 8; i++) c.Notes.Bt[0].Add(new Note(i * 240L));
        c.EndPulse = 960 * 4;

        var raw = RadarCalculator.RawStats(RadarInput.FromChart(c, RadarSource.Kson));
        Assert.That(raw.Duration, Is.EqualTo(8.0).Within(1e-9));
        Assert.That(raw.NotesTotal, Is.EqualTo(8));
        Assert.That(raw.PeakNoteDensity, Is.EqualTo(3), "notes at t, t-0.5, t-1.0 are inside the 1 s window");

        var r = RadarCalculator.Compute(c, RadarSource.Kson);
        Assert.That(r.Notes, Is.EqualTo((int)Math.Round((16.663 * 1.0 + 38.798) / 1.5)));
        Assert.That(r.Peak, Is.EqualTo((int)Math.Round((2.1644 * 3 + 42.113) / 1.5)));
        Assert.That(r.Tsumami, Is.Zero);
        Assert.That(r.Tricky, Is.Zero, "0.5 s apart is no jack");
    }

    [Test]
    public void KnobNotesCountAsOneHandAndHandTrip()
    {
        var c = chart();
        // left laser held at the far right (value 1.0) for a whole measure, notes on BT A (left side) under it
        c.Notes.Lasers[0].Add(new LaserSection(0, new[] { new LaserPoint(0, 1.0), new LaserPoint(960, 1.0) }));
        c.Notes.Bt[0].Add(new Note(240));
        c.Notes.Bt[3].Add(new Note(480));
        c.Notes.Bt[3].Add(new Note(1440)); // outside the section
        c.EndPulse = 1920;

        var raw = RadarCalculator.RawStats(RadarInput.FromChart(c, RadarSource.Kson));
        Assert.That(raw.OneHandNotes, Is.EqualTo(2));
        Assert.That(raw.WrongSideNotes, Is.EqualTo(1), "knob on the right half over the right-side note");
    }

    [Test]
    public void SlamsAndChainsAreCountedLikeRadarPy()
    {
        var c = chart();
        // slam 0 → 1, hold, then a slant chain of two moving segments, then flat
        c.Notes.Lasers[1].Add(new LaserSection(0, new[]
        {
            new LaserPoint(0, 0.0, 1.0),
            new LaserPoint(480, 1.0),
            new LaserPoint(720, 0.5),
            new LaserPoint(960, 0.0),
            new LaserPoint(1200, 0.0),
        }));
        c.EndPulse = 1920;

        foreach (var source in new[] { RadarSource.Kson, RadarSource.Ksh })
        {
            var raw = RadarCalculator.RawStats(RadarInput.FromChart(c, source));
            Assert.That(raw.Slams, Is.EqualTo(1), source.ToString());
            Assert.That(raw.MovingLasers, Is.EqualTo(1), $"{source}: one chain of two segments");
        }

        // ksh: radar.py sees the second slam line 1/32 later
        var ksh = RadarInput.FromChart(c, RadarSource.Ksh);
        Assert.That(ksh.Laser[1][0][1], Is.EqualTo((6L, 50.0)));
    }

    [Test]
    public void BpmChangesMakeItTricky()
    {
        var c = chart(150);
        c.Beat.Bpm.Add(new BpmChange(960, 300));
        c.Notes.Bt[0].Add(new Note(0));
        c.EndPulse = 1920;
        var raw = RadarCalculator.RawStats(RadarInput.FromChart(c, RadarSource.Kson));
        Assert.That(raw.BpmInverseDifferences, Is.EqualTo(1.0 / 150 - 1.0 / 300).Within(1e-12));
        Assert.That(raw.Duration, Is.EqualTo(1.6 + 0.8).Within(1e-9));
        Assert.That(RadarCalculator.Compute(c, RadarSource.Kson).Tricky, Is.GreaterThan(10));
    }

    [Test]
    public void ValuesAreClampedTo1000()
    {
        var c = chart(1000);
        for (int i = 0; i < 4000; i++) c.Notes.Bt[i % 4].Add(new Note(i * 5L));
        c.EndPulse = 4000 * 5;
        Assert.That(RadarCalculator.Compute(c, RadarSource.Kson).Notes, Is.EqualTo(1000));
    }

    [Test]
    public void HeaderBpmParsesLikeKsh2vox()
    {
        Assert.That(RadarInput.HeaderBpm("120-240"), Is.EqualTo((120.0, true)));
        Assert.That(RadarInput.HeaderBpm("180"), Is.EqualTo((180.0, false)));
        Assert.That(RadarInput.HeaderBpm("?"), Is.Null);
        Assert.That(RadarInput.HeaderBpm(""), Is.Null);
    }

    [Test]
    public void RangedHeaderBpmIsNoTempoChange()
    {
        // ksh2vox 0.17.1: t=35-321 with the first inline t=321 is one tempo, not a 35 → 321 change at tick 0
        var c = chart(321);
        c.Meta.DispBpm = "35-321";
        c.Notes.Bt[0].Add(new Note(0));
        c.EndPulse = 1920;
        var input = RadarInput.FromChart(c, RadarSource.Ksh);
        Assert.That(input.Bpm, Is.EqualTo(new List<(long, double)> { (0, 321.0) }));
        Assert.That(RadarCalculator.RawStats(input).BpmInverseDifferences, Is.Zero);

        // a plain header is the tempo at 0, an inline t= on the same tick wins (emit_vox's per-tick dedupe)
        c.Meta.DispBpm = "200";
        input = RadarInput.FromChart(c, RadarSource.Ksh);
        Assert.That(RadarCalculator.RawStats(input).BpmInverseDifferences, Is.Zero);
        Assert.That(RadarCalculator.RawStats(input).Duration, Is.EqualTo(8 * 60.0 / 321).Within(1e-9));

        // ranged header, first inline tempo later: it applies from tick 0
        c.Beat.Bpm.Clear();
        c.Beat.Bpm.Add(new BpmChange(480, 321));
        c.Meta.DispBpm = "35-321";
        Assert.That(RadarInput.FromChart(c, RadarSource.Ksh).Bpm, Is.EqualTo(new List<(long, double)> { (0, 321.0) }));
    }

    // ---------------------------------------------------------------- parity with radar.py

    private const string referenceScript = """
        import sys, os, io, json, contextlib
        sys.path.insert(0, sys.argv[1])
        import ksh2vox, radar
        out = {}
        for p in sys.argv[2:]:
            with contextlib.redirect_stdout(io.StringIO()):
                parsed = ksh2vox.load_chart(p)
            out[p] = radar.compute(parsed)
        sys.stdout.write(json.dumps(out))
        """;

    /// <summary>radar.py's values for <paramref name="files"/>, or ignores the test without Python / the converter.</summary>
    private static Dictionary<string, SkillRadar> pythonReference(IReadOnlyList<string> files)
    {
        string conv = VoxExportTests.LocalConverter;
        if (!File.Exists(Path.Combine(conv, "radar.py"))) Assert.Ignore("local converter checkout with radar.py not found");
        var py = PythonLocator.Find(null);
        if (!py.Found) Assert.Ignore("no Python 3.10+");

        using var t = new TempDir();
        string script = Path.Combine(t.Path, "radar_reference.py");
        File.WriteAllText(script, referenceScript.Replace("\r\n", "\n"));

        var psi = new ProcessStartInfo(py.Command!.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        foreach (string a in py.Command.PrefixArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(conv);
        foreach (string f in files) psi.ArgumentList.Add(f);

        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEndAsync();
        string stdout = proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(120_000)) { proc.Kill(); Assert.Fail("radar.py reference timed out"); }
        Assert.That(proc.ExitCode, Is.Zero, stderr.Result);

        var json = JsonNode.Parse(stdout)!.AsObject();
        return json.ToDictionary(kv => Path.GetFullPath(kv.Key), kv =>
        {
            var o = kv.Value!.AsObject();
            return new SkillRadar((int)o["notes"]!, (int)o["peak"]!, (int)o["tsumami"]!, (int)o["tricky"]!, (int)o["hand_trip"]!, (int)o["one_hand"]!);
        });
    }

    [Test]
    public void LibraryChartsMatchRadarPy()
    {
        RealCharts.RequireRoot();
        var files = RealCharts.Files(".ksh").Concat(RealCharts.Files(".kson")).Select(Path.GetFullPath).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.That(files, Is.Not.Empty);
        var reference = pythonReference(files);

        int exact = 0, maxDiff = 0;
        var failures = new List<string>();
        foreach (string f in files)
        {
            var expected = reference[f];
            var actual = RadarCalculator.ComputeFile(f);
            int diff = SkillRadar.Axes.Max(a => Math.Abs(expected[a] - actual[a]));
            maxDiff = Math.Max(maxDiff, diff);
            if (expected == actual) exact++;
            string name = Path.GetRelativePath(RealCharts.Root, f);
            TestContext.Out.WriteLine($"{name}: py [{expected}]{(diff == 0 ? "" : $"  c# [{actual}]  max diff {diff}")}");
            if (diff > 1) failures.Add($"{name}: py [{expected}] vs c# [{actual}]");
        }

        TestContext.Out.WriteLine($"{exact}/{files.Count} charts identical, max axis difference {maxDiff}");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    // ---------------------------------------------------------------- official charts (informational)

    private const string gameData = @"D:\SDVX-RIP\SOUND VOLTEX NABLA\data";

    /// <summary>
    /// Our radar on the official VOX charts against the values Konami stores in music_db.xml. Informational only:
    /// radar.py is an approximation (kshoot-tools' formulas), so this reports the error, it asserts nothing about it.
    /// </summary>
    [Test, Explicit("reads the local game install; informational")]
    public void OfficialChartsAgainstMusicDb()
    {
        string db = Path.Combine(gameData, "others", "music_db.xml");
        if (!File.Exists(db)) Assert.Ignore("game install not found");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var doc = System.Xml.Linq.XDocument.Parse(Encoding.GetEncoding(932).GetString(File.ReadAllBytes(db)));

        string[] slotNames = { "novice", "advanced", "exhaust", "infinite", "maximum" };
        var reference = new Dictionary<(int, int), SkillRadar>();
        foreach (var music in doc.Root!.Elements("music"))
        {
            int id = (int)music.Attribute("id")!;
            var diffs = music.Element("difficulty");
            for (int s = 0; s < slotNames.Length; s++)
            {
                var r = diffs?.Element(slotNames[s])?.Element("radar");
                if (r == null) continue;
                int v(string a) => int.TryParse((string?)r.Element(a) ?? (string?)r.Element(a.Replace('-', '_')), out int x) ? x : 0;
                reference[(id, s)] = new SkillRadar(v("notes"), v("peak"), v("tsumami"), v("tricky"), v("hand-trip"), v("one-hand"));
            }
        }

        var errors = SkillRadar.Axes.ToDictionary(a => a, _ => new List<(double Ours, double Theirs)>());
        int charts = 0;
        foreach (string vox in Directory.EnumerateFiles(Path.Combine(gameData, "music"), "*.vox", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(vox);
            if (name.Length < 4 || !int.TryParse(name[..4], out int id) || VoxReader.SlotFromFileName(vox) is not int slot) continue;
            if (name.EndsWith("_6u.vox", StringComparison.Ordinal)) continue; // ultimate shares the MXM slot
            if (!reference.TryGetValue((id, slot), out var theirs) || theirs == default) continue;
            SkillRadar ours;
            try { ours = RadarCalculator.ComputeFile(vox); }
            catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException) { continue; }
            charts++;
            foreach (var a in SkillRadar.Axes) errors[a].Add((ours[a], theirs[a]));
        }

        TestContext.Out.WriteLine($"{charts} official charts with a music_db radar");
        foreach (var (axis, list) in errors)
        {
            double mae = list.Average(p => Math.Abs(p.Ours - p.Theirs));
            double meanOurs = list.Average(p => p.Ours), meanTheirs = list.Average(p => p.Theirs);
            double cov = list.Sum(p => (p.Ours - meanOurs) * (p.Theirs - meanTheirs));
            double sdO = Math.Sqrt(list.Sum(p => Math.Pow(p.Ours - meanOurs, 2))), sdT = Math.Sqrt(list.Sum(p => Math.Pow(p.Theirs - meanTheirs, 2)));
            double r = sdO > 0 && sdT > 0 ? cov / (sdO * sdT) : double.NaN;
            TestContext.Out.WriteLine($"{SkillRadar.Label(axis),-10} MAE {mae,6:0.0}  mean ours {meanOurs,6:0.0} / music_db {meanTheirs,6:0.0}  Pearson r {r:0.00}");
        }
    }

    // ---------------------------------------------------------------- parity: model radar vs ksh2vox radar_model.py

    private const string modelReferenceScript = """
        import sys, io, json, contextlib
        sys.path.insert(0, sys.argv[1])
        with contextlib.redirect_stdout(io.StringIO()):
            import ksh2vox, radar_model
        if radar_model.load_model() is None:
            sys.stdout.write(json.dumps({"error": radar_model.model_error()}))
            sys.exit(0)
        out = {}
        for p in sys.argv[2:]:
            with contextlib.redirect_stdout(io.StringIO()):
                parsed = ksh2vox.load_chart(p)
            out[p] = radar_model.compute(parsed)
        sys.stdout.write(json.dumps(out))
        """;

    /// <summary>
    /// The fitted radar (<see cref="SkillRadarModel"/>) against its Python port in the converter (ksh2vox 0.18.0
    /// radar_model.py, fed from ksh2vox's own parser): every library chart must give the identical six values. The
    /// port reproduces the feature extraction statement by statement (including List.Sort's tie order, which decides
    /// floating-point sums), so there is no tolerance.
    /// </summary>
    [Test]
    public void LibraryChartsMatchRadarModelPy()
    {
        RealCharts.RequireRoot();
        string conv = VoxExportTests.LocalConverter;
        if (!File.Exists(Path.Combine(conv, "radar_model.py"))) Assert.Ignore("local converter checkout with radar_model.py not found");
        var model = SkillRadarModel.Default;
        if (model.IsClassic) Assert.Ignore("no embedded radar model: " + model.FallbackReason);
        var py = PythonLocator.Find(null);
        if (!py.Found) Assert.Ignore("no Python 3.10+");

        var files = RealCharts.Files(".ksh").Concat(RealCharts.Files(".kson")).Select(Path.GetFullPath).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.That(files, Is.Not.Empty);

        using var t = new TempDir();
        string script = Path.Combine(t.Path, "radar_model_reference.py");
        File.WriteAllText(script, modelReferenceScript.Replace("\r\n", "\n"));
        var psi = new ProcessStartInfo(py.Command!.FileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONUTF8"] = "1";
        foreach (string a in py.Command.PrefixArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(script);
        psi.ArgumentList.Add(conv);
        foreach (string f in files) psi.ArgumentList.Add(f);
        using var proc = Process.Start(psi)!;
        var stderr = proc.StandardError.ReadToEndAsync();
        string stdout = proc.StandardOutput.ReadToEnd();
        if (!proc.WaitForExit(120_000)) { proc.Kill(); Assert.Fail("radar_model.py reference timed out"); }
        Assert.That(proc.ExitCode, Is.Zero, stderr.Result);

        var json = JsonNode.Parse(stdout)!.AsObject();
        if (json["error"] is { } err) Assert.Ignore("converter has no radar model: " + err);
        var reference = json.ToDictionary(kv => Path.GetFullPath(kv.Key), kv =>
        {
            var o = kv.Value!.AsObject();
            return new SkillRadar((int)o["notes"]!, (int)o["peak"]!, (int)o["tsumami"]!, (int)o["tricky"]!, (int)o["hand_trip"]!, (int)o["one_hand"]!);
        });

        var failures = new List<string>();
        foreach (string f in files)
        {
            var expected = reference[f];
            var actual = model.EstimateFile(f).Radar;
            string name = Path.GetRelativePath(RealCharts.Root, f);
            TestContext.Out.WriteLine($"{name}: py [{expected}]{(expected == actual ? "" : $"  c# [{actual}]")}");
            if (expected != actual) failures.Add($"{name}: py [{expected}] vs c# [{actual}]");
        }
        TestContext.Out.WriteLine($"{files.Count - failures.Count}/{files.Count} charts identical");
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
