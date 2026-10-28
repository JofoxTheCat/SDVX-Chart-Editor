using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Vox;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// VOX export through the Python host. The integration tests need Python 3.10+ and ksh2vox (the submodule,
/// or the local converter checkout) and skip themselves otherwise.
/// Set SDVX_VOX_OUT to keep the output of <see cref="ExportRealSongs"/> in a folder of your choice.
/// </summary>
[NonParallelizable]
public class VoxExportTests
{
    public const string LocalConverter = @"D:\SDVX-DEV\SDVX-Chart-Converter";

    /// <summary>Prefers the local converter checkout (0.16.0) over the submodule (GitHub v0.12.0) — see the report.</summary>
    private static string? converterDir()
        => Environment.GetEnvironmentVariable("SDVX_CONVERTER") is { Length: > 0 } env ? env
           : File.Exists(Path.Combine(LocalConverter, "ksh2vox.py")) ? LocalConverter
           : VoxExporter.FindConverterDir(null);

    private static VoxExporter exporterOrIgnore(List<string>? log = null)
    {
        string? conv = converterDir();
        if (conv == null) Assert.Ignore("ksh2vox not found");
        if (!PythonLocator.Find(null).Found) Assert.Ignore("no Python 3.10+");
        return new VoxExporter(new VoxExporterSettings { ConverterDir = conv, Log = log == null ? null : l => { lock (log) log.Add(l); } });
    }

    // ------------------------------------------------------------------ unit

    [Test]
    public void VersionComparison()
    {
        Assert.That(VoxExporter.IsOutdated("0.12.0"), Is.True);
        Assert.That(VoxExporter.IsOutdated("0.15.9"), Is.True);
        Assert.That(VoxExporter.IsOutdated("0.16.0"), Is.False);
        Assert.That(VoxExporter.IsOutdated("0.16.1-dev"), Is.False);
        Assert.That(VoxExporter.IsOutdated("1.0"), Is.False);
        Assert.That(VoxExporter.IsOutdated(null), Is.True);
    }

    [Test]
    public void ExplicitPythonSettingNeverFallsBack()
    {
        var r = PythonLocator.Find(@"Z:\nope\python.exe");
        Assert.That(r.Found, Is.False);
        Assert.That(r.Tried.Single(), Does.Contain("setting"));
    }

    [Test]
    public void HostScriptIsCopiedToTheOutput()
        => Assert.That(VoxExporter.FindHostScript(null), Is.Not.Null.And.Contains(Path.Combine("python", "vox_host.py")));

    [Test]
    public void StageWritesSlotNamedKsonAndCopiesAssets()
    {
        using var t = new TempDir("vox");
        string song = TempDir.MakeSong(t.Path, "Src");
        var folder = SongFolder.Load(song);
        var chart = folder.Charts[0].Chart;
        chart.Meta.DifficultySlot = Difficulty.Maximum;
        chart.Meta.Level = 17.5;
        chart.Beat.TimeSignatures.Add(new TimeSignature(2, 6, 8));
        var src = new VoxExportSource
        {
            SongName = "My: Song?",
            Charts = { new VoxExportChart { Chart = chart, AudioPath = folder.Charts[0].AudioPath, JacketPath = folder.Charts[0].JacketPath } },
        };
        string staged = VoxExporter.Stage(src, out var warnings, t.Sub("stage"));
        Assert.That(Path.GetFileName(staged), Is.EqualTo("My_ Song_"));
        Assert.That(File.Exists(Path.Combine(staged, "mxm.kson")), "MXM only survives through the file name");
        Assert.That(File.Exists(Path.Combine(staged, "song.ogg")));
        Assert.That(File.Exists(Path.Combine(staged, "jacket.png")));
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(staged, "mxm.kson")))!.AsObject();
        Assert.That(doc["meta"]!["level"]!.GetValue<double>(), Is.EqualTo(17.5), "ksh2vox 0.16.0 keeps tenths: difnum 175");
        Assert.That(warnings, Has.None.Contains("17.5"));
        Assert.That(doc["meta"]!["jacket_filename"]!.GetValue<string>(), Is.EqualTo("jacket.png"));
        Assert.That(doc["audio"]!["bgm"]!["filename"]!.GetValue<string>(), Is.EqualTo("song.ogg"));
        Assert.That(doc["audio"]!["bgm"]!["preview"]!["duration"]!.GetValue<long>(), Is.EqualTo(15000));
        var sig = doc["beat"]!["time_sig"]!.AsArray();
        Assert.That(sig.Last()!["n"]!.GetValue<int>(), Is.EqualTo(6), "time_sig in the object form ksh2vox 0.16.0 can read");
        Assert.That(sig.Last()!["idx"]!.GetValue<int>(), Is.EqualTo(2));
        // The editor's chart object is not modified by staging.
        Assert.That(chart.Meta.Level, Is.EqualTo(17.5));
    }

    [Test]
    public void StageRefusesTwoChartsInOneSlot()
    {
        var c1 = new Chart();
        var c2 = new Chart();
        var src = new VoxExportSource { SongName = "x", Charts = { new() { Chart = c1 }, new() { Chart = c2 } } };
        Assert.That(Assert.Throws<VoxExportException>(() => VoxExporter.Stage(src, out _))!.Kind, Is.EqualTo("duplicate_slot"));
    }

    // ------------------------------------------------------------------ host process

    [Test]
    public async Task CrashIsDetectedWithStderr()
    {
        var py = PythonLocator.Find(null);
        if (!py.Found) Assert.Ignore("no Python");
        using var t = new TempDir("vox");
        string script = Path.Combine(t.Path, "crash.py");
        File.WriteAllText(script, "import sys, json\nprint(json.dumps({'event':'ready'}), flush=True)\nsys.stdin.readline()\nprint('boom: something broke', file=sys.stderr, flush=True)\nsys.exit(3)\n");
        await using var host = await PythonHost.StartAsync(py.Command!, script, Array.Empty<string>(), TimeSpan.FromSeconds(20));
        var e = Assert.ThrowsAsync<PythonHostException>(() => host.CallAsync("ping", timeout: TimeSpan.FromSeconds(20)))!;
        Assert.That(e.Kind, Is.EqualTo("crashed"));
        Assert.That(e.Message, Does.Contain("exit code 3"));
        Assert.That(e.StderrTail, Has.Some.Contains("boom"));
        Assert.That(host.IsRunning, Is.False);
    }

    [Test]
    public async Task TimeoutKillsTheHost()
    {
        var py = PythonLocator.Find(null);
        if (!py.Found) Assert.Ignore("no Python");
        using var t = new TempDir("vox");
        string script = Path.Combine(t.Path, "hang.py");
        File.WriteAllText(script, "import sys, json, time\nprint(json.dumps({'event':'ready'}), flush=True)\nsys.stdin.readline()\ntime.sleep(60)\n");
        await using var host = await PythonHost.StartAsync(py.Command!, script, Array.Empty<string>(), TimeSpan.FromSeconds(20));
        var e = Assert.ThrowsAsync<PythonHostException>(() => host.CallAsync("ping", timeout: TimeSpan.FromSeconds(1)))!;
        Assert.That(e.Kind, Is.EqualTo("timeout"));
        Assert.That(host.IsRunning, Is.False);
    }

    [Test]
    public async Task StrayStdoutDoesNotBreakTheProtocol()
    {
        var py = PythonLocator.Find(null);
        if (!py.Found) Assert.Ignore("no Python");
        using var t = new TempDir("vox");
        string script = Path.Combine(t.Path, "noisy.py");
        File.WriteAllText(script, "import sys, json\nprint('hello, not json', flush=True)\nprint(json.dumps({'event':'ready'}), flush=True)\nfor line in sys.stdin:\n    r = json.loads(line)\n    print('noise', flush=True)\n    print(json.dumps({'id': r['id'], 'result': {'ok': True}}), flush=True)\n");
        await using var host = await PythonHost.StartAsync(py.Command!, script, Array.Empty<string>(), TimeSpan.FromSeconds(20));
        var r = await host.CallAsync("anything", timeout: TimeSpan.FromSeconds(20));
        Assert.That(r!["ok"]!.GetValue<bool>(), Is.True);
        Assert.That(host.StderrTail, Has.Some.Contains("not protocol"));
    }

    [Test]
    public async Task HostReportsVersionAndSelfCheck()
    {
        await using var exp = exporterOrIgnore();
        var info = await exp.GetInfoAsync();
        TestContext.Out.WriteLine(info.Raw.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Assert.That(info.ConverterLoaded, Is.True, info.ConverterError);
        Assert.That(info.ConverterVersion, Is.Not.Null);
        Assert.That(info.TemplateFound, Is.True);
        foreach (var w in info.Warnings) TestContext.Out.WriteLine("warning: " + w);
    }

    [Test]
    public async Task HostErrorsAreTyped()
    {
        await using var exp = exporterOrIgnore();
        using var t = new TempDir("vox");
        string song = TempDir.MakeSong(t.Path, "S");
        var e = Assert.ThrowsAsync<PythonHostException>(() => exp.ExportFolderAsync(song, new VoxExportOptions { OutputDir = t.Sub("out", "music") }))!;
        Assert.That(e.Kind, Is.EqualTo("id_required"));
        // The host survives a failed call.
        Assert.That((await exp.GetInfoAsync()).ConverterLoaded, Is.True);
    }

    [Test]
    public async Task ExportSyntheticChartWithImportMap()
    {
        await using var exp = exporterOrIgnore();
        using var t = new TempDir("vox");
        string song = TempDir.MakeSong(t.Path, "Synthetic");
        var folder = SongFolder.Load(song);
        folder.Charts[0].Chart.Beat.TimeSignatures.Add(new TimeSignature(1, 6, 8)); // exercises the time_sig adaptation
        var src = VoxExportSource.FromSongFolder(folder);
        string music = t.Sub("mod", "music");
        var events = new List<string>();
        var opts = new VoxExportOptions { OutputDir = music, StartId = 2900 };
        var r = await exp.ExportAsync(src, opts, new SyncProgress(p => events.Add(p.Event)));
        Assert.That(r.SongId, Is.EqualTo(2900));
        Assert.That(r.IsNew, Is.True);
        Assert.That(r.VoxFiles.Select(Path.GetFileName), Is.EqualTo(new[] { "2900_synthetic_3e.vox" }));
        Assert.That(File.Exists(Path.Combine(t.Path, "mod", "others", "music_db.merged.xml")));
        Assert.That(r.MapPath, Is.EqualTo(Path.Combine(t.Path, "mod", "import_map.json")));
        Assert.That(events, Does.Contain("chart_done").And.Contain("song_done"));
        VoxChecks.AssertValid(r.VoxFiles[0]);

        // Same song name again → same id from the import map.
        var r2 = await exp.ExportAsync(src, opts);
        Assert.That(r2.SongId, Is.EqualTo(2900));
        Assert.That(r2.IsNew, Is.False);
    }

    /// <summary>
    /// The id floor over several base dbs (ksh2vox 0.20.1+): the plugin json lags behind the game, so the game's
    /// music_db.xml decides; custom (nautica-tagged) json entries never count; StartId stays a lower bound; a
    /// remembered id that a game update overtook is reassigned and its old folder removed.
    /// </summary>
    [Test]
    public async Task ExportIdFloorOverSeveralBaseDbs()
    {
        await using var exp = exporterOrIgnore();
        var info = await exp.GetInfoAsync();
        if (!VoxExporter.SupportsMultipleBaseDbs(info.ConverterVersion)) Assert.Ignore($"ksh2vox {info.ConverterVersion} takes one base db");

        using var t = new TempDir("vox-floor");
        string json = Path.Combine(t.Path, "music_db.json");
        File.WriteAllText(json, """{"mdb":{"music":[{"id":2384},{"id":3000,"info":{"nautica":true}}]}}""");
        string xml = Path.Combine(t.Path, "music_db.xml");
        void game(int maxId) => File.WriteAllText(xml,
            "<?xml version=\"1.0\" encoding=\"Shift_JIS\"?>\n<mdb>"
            + string.Concat(new[] { 1, maxId }.Select(i => $"<music id=\"{i}\"><info><title_name>real {i}</title_name></info></music>"))
            + "</mdb>\n");

        string song = TempDir.MakeSong(t.Path, "Floor");
        var src = VoxExportSource.FromSongFolder(SongFolder.Load(song));
        string music = t.Sub("mod", "music");

        // json alone would say 2385; the game's xml knows 2950; the nautica entry at 3000 does not count
        game(2950);
        var opts = new VoxExportOptions { OutputDir = music, StartId = 2900, BaseDbPaths = new[] { json, xml } };
        var r = await exp.ExportAsync(src, opts);
        Assert.That(r.SongId, Is.EqualTo(2951));

        // StartId above every real song: StartId wins
        game(2403);
        string music2 = t.Sub("mod2", "music");
        var r2 = await exp.ExportAsync(src, new VoxExportOptions { OutputDir = music2, StartId = 2900, BaseDbPaths = new[] { json, xml } });
        Assert.That(r2.SongId, Is.EqualTo(2900));

        // a game update overtakes the remembered 2951: reassigned, the old folder is gone
        game(2960);
        var r3 = await exp.ExportAsync(src, new VoxExportOptions { OutputDir = music, StartId = 2900, BaseDbPaths = new[] { json, xml }, Force = false });
        Assert.That(r3.SongId, Is.EqualTo(2961));
        Assert.That(Directory.GetDirectories(music).Select(Path.GetFileName), Is.EqualTo(new[] { "2961_floor" }));
    }

    /// <summary>
    /// Every game effect through the real exporter (kson staging → ksh2vox 0.20.0): the rows the game reads, with the
    /// official field counts (#POSTEFFECT 9, SPCONTROLER 8 — any other count is dropped by the game's parser).
    /// </summary>
    [Test]
    public async Task ExportGameEffects()
    {
        await using var exp = exporterOrIgnore();
        var info = await exp.GetInfoAsync();
        if (!Editing.ExportSupport.MapsEffectsToVox(info.ConverterVersion)) Assert.Ignore($"ksh2vox {info.ConverterVersion} < 0.20.0");
        using var t = new TempDir("vox");
        string song = TempDir.MakeSong(t.Path, "Effects");
        var folder = SongFolder.Load(song);
        var chart = folder.Charts[0].Chart;
        var fx = VoxEffectsTests.EffectsChart();
        chart.Notes.Bt[0].Add(new Note(Timebase.PulsesPerWholeNote));
        chart.VoxExtras = fx.VoxExtras.Clone();
        var r = await exp.ExportAsync(VoxExportSource.FromSongFolder(folder), new VoxExportOptions { OutputDir = t.Sub("mod", "music"), StartId = 2950 });
        foreach (var w in r.Warnings) TestContext.Out.WriteLine("warning: " + w);
        string vox = File.ReadAllText(r.VoxFiles[0], Encoding.Latin1).Replace("\r\n", "\n");
        VoxChecks.AssertValid(r.VoxFiles[0]);
        TestContext.Out.WriteLine(vox[vox.IndexOf("#SPCONTROLER", StringComparison.Ordinal)..]);

        string body(string name)
        {
            int i = vox.IndexOf("\n#" + name + "\n", StringComparison.Ordinal);
            Assert.That(i, Is.GreaterThanOrEqualTo(0), name);
            int s = i + name.Length + 3;
            return vox[s..vox.IndexOf("\n#END", s - 1, StringComparison.Ordinal)];
        }

        var pe = body("POSTEFFECT").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(pe, Does.Contain("002,01,00\t2\t0\tColorConversion\t1\t0\tMode\t2.0f\t2.0f"));
        Assert.That(pe, Does.Contain("002,01,00\t2\t96\tColorConversion\t1\t0\tStrength\t0.0f\t100.0f"));
        Assert.That(pe, Does.Contain("002,01,00\t2\t0\tColorConversion\t0\t1\tStrength\t0.0f\t155.52f"));
        Assert.That(pe, Does.Contain("003,01,00\t2\t500ms\tRandomShake\t0\t0\tnone\t20.0f\t0.0f"));
        Assert.That(pe.All(l => l.Split('\t').Length == 9), "9 fields per #POSTEFFECT row");

        var sp = body("SPCONTROLER").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(sp.All(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length == 8), "8 fields per SPCONTROLER row");
        Assert.That(sp, Does.Contain("002,01,00\tLaneY\t2\t48\t0.00\t1024.00\t0.00\t0.00"));
        Assert.That(sp, Does.Contain("002,01,00\tBIL_RotZ\t2\t192\td0.00\td-45.00\td0.00\td0.00"));
        Assert.That(sp, Does.Contain("001,01,00\tBAROFF\t0\t0\tON\t0\t0\t0"));
        Assert.That(sp, Does.Contain("002,02,00\tSpecialN\t0\t0\tLANECOL\txFFFF0000\ti0\ti0"));
        Assert.That(sp, Does.Contain("002,01,00\tSpecialN\t0\t96\tSIDEBG\ti1\ti1\t0"));
        Assert.That(body("BPM OPTION"), Is.EqualTo("RepresentativeBpm\t174.0000\nConstantScroll\t1"));
        Assert.That(body("SCRIPTED_TRACK3"), Is.EqualTo("002,01,00\t7"));
        Assert.That(vox, Does.Contain("@SCRIPTSTART 7\n$offsetX = $offsetX + 0.178 when ( $targetStep - $currentStep ) > 48\n@SCRIPTEND"));
        string db = File.ReadAllText(Path.Combine(t.Path, "mod", "others", "music_db.merged.xml"), Encoding.Latin1);
        Assert.That(db, Does.Contain("<bg_no __type=\"u16\">105</bg_no>"));
    }

    [Test]
    public async Task SubmoduleConverterIsReportedAndStillUsable()
    {
        string? sub = VoxExporter.FindConverterDir(null);
        if (sub == null) Assert.Ignore("submodule not checked out");
        if (!PythonLocator.Find(null).Found) Assert.Ignore("no Python 3.10+");
        await using var exp = new VoxExporter(); // default lookup = the submodule
        var info = await exp.GetInfoAsync();
        TestContext.Out.WriteLine($"submodule: {info.ConverterDir} = ksh2vox {info.ConverterVersion}");
        foreach (var w in info.Warnings) TestContext.Out.WriteLine("warning: " + w);
        Assert.That(info.ConverterLoaded, Is.True, info.ConverterError);
        if (!VoxExporter.IsOutdated(info.ConverterVersion)) Assert.Pass("submodule is current");

        Assert.That(info.Warnings, Has.Some.Contains("older than"));
        using var t = new TempDir("vox");
        string song = TempDir.MakeSong(t.Path, "Old");
        var src = VoxExportSource.FromSongFolder(SongFolder.Load(song));
        string music = t.Sub("mod", "music");
        var e = Assert.ThrowsAsync<PythonHostException>(() => exp.ExportAsync(src, new VoxExportOptions { OutputDir = music, StartId = 2900 }))!;
        Assert.That(e.Kind, Is.EqualTo("converter_too_old"));
        var r = await exp.ExportAsync(src, new VoxExportOptions { OutputDir = music, SongId = 2901 });
        Assert.That(r.VoxFiles, Has.Count.EqualTo(1));
        Assert.That(r.Warnings, Has.Some.Contains("older than"));
        VoxChecks.AssertValid(r.VoxFiles[0]);
    }

    /// <summary>
    /// End-to-end: every Nautica song is loaded with SongFolder, staged as .kson and converted by ksh2vox
    /// through the host. Each .vox must have CRLF only and closed laser sections.
    /// </summary>
    [TestCaseSource(typeof(VoxExportTests), nameof(SongCases))]
    public async Task ExportRealSongs(string folderPath)
    {
        if (folderPath.Length == 0) Assert.Ignore("local chart library not found");
        var log = new List<string>();
        await using var exp = exporterOrIgnore(log);
        string outRoot = Environment.GetEnvironmentVariable("SDVX_VOX_OUT") is { Length: > 0 } o ? o : Path.Combine(Path.GetTempPath(), "SDVXEditorTests", "vox-out");
        if (outRoot.Contains("data_mods", StringComparison.OrdinalIgnoreCase)) Assert.Fail("refusing to write into a game data_mods folder");
        string music = Path.Combine(outRoot, "music");

        var folder = SongFolder.Load(folderPath);
        var r = await exp.ExportAsync(VoxExportSource.FromSongFolder(folder), new VoxExportOptions { OutputDir = music, StartId = 2410 });
        TestContext.Out.WriteLine($"{folder.Name} -> id {r.SongId}, {r.SongDir} ({r.Elapsed.TotalSeconds:0.00} s, ksh2vox {r.ConverterVersion})");
        foreach (var f in r.Files) TestContext.Out.WriteLine($"  {Path.GetFileName(f)}  {new FileInfo(f).Length} bytes");
        foreach (var w in r.Warnings) TestContext.Out.WriteLine("  warning: " + w);
        Assert.That(r.VoxFiles.Count, Is.EqualTo(folder.Charts.Count), "one .vox per chart");
        Assert.That(r.Files.Any(f => f.EndsWith(".2dx")), "audio written");
        Assert.That(r.Files.Any(f => Path.GetFileName(f).StartsWith("jk_")), "jacket written");
        foreach (var vox in r.VoxFiles)
            TestContext.Out.WriteLine("  " + VoxChecks.AssertValid(vox));
    }

    public static IEnumerable<TestCaseData> SongCases()
    {
        if (!Directory.Exists(RealCharts.Root))
        {
            yield return new TestCaseData("").SetName("ExportRealSongs(no local charts)");
            yield break;
        }
        foreach (var d in Directory.GetDirectories(RealCharts.Root).OrderBy(d => d, StringComparer.Ordinal))
            if (Directory.EnumerateFiles(d).Any(SongFolder.IsChartFile))
                yield return new TestCaseData(d).SetName($"ExportRealSongs({Path.GetFileName(d)})");
    }

    private sealed class SyncProgress(Action<VoxExportProgress> a) : IProgress<VoxExportProgress>
    {
        public void Report(VoxExportProgress value) => a(value);
    }
}

/// <summary>Structural checks of a .vox (converter CLAUDE.md §6).</summary>
public static class VoxChecks
{
    public static string AssertValid(string voxPath)
    {
        byte[] data = File.ReadAllBytes(voxPath);
        int crcrlf = 0, lf = 0, crlf = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != (byte)'\n') continue;
            lf++;
            if (i >= 1 && data[i - 1] == '\r') crlf++;
            if (i >= 2 && data[i - 1] == '\r' && data[i - 2] == '\r') crcrlf++;
        }
        Assert.That(crcrlf, Is.EqualTo(0), $"{voxPath}: \\r\\r\\n found");
        Assert.That(crlf, Is.EqualTo(lf), $"{voxPath}: bare LF found");

        string text = Encoding.Latin1.GetString(data);
        var report = new StringBuilder(Path.GetFileName(voxPath) + ":");
        foreach (string track in new[] { "#TRACK1", "#TRACK8" })
        {
            var lines = section(text, track);
            int starts = 0, ends = 0;
            var runs = new Dictionary<string, int>();
            foreach (var l in lines)
            {
                var f = l.Split('\t');
                Assert.That(f.Length, Is.EqualTo(10), $"{track} laser line must have 10 fields: {l}");
                if (f[2] == "1") starts++;
                if (f[2] == "2") ends++;
                runs[f[0]] = runs.GetValueOrDefault(f[0]) + 1;
            }
            Assert.That(starts, Is.EqualTo(ends), $"{voxPath} {track}: {starts} section starts vs {ends} ends");
            Assert.That(runs.Values.DefaultIfEmpty(0).Max(), Is.LessThanOrEqualTo(2), $"{track}: more than two points on one position");
            report.Append($" {track} {starts}/{ends} sections, {runs.Values.Count(v => v == 2)} slams;");
        }
        report.Append($" {crlf} CRLF lines, 0 \\r\\r\\n");
        return report.ToString();
    }

    private static List<string> section(string text, string header)
    {
        var lines = text.Split("\r\n");
        int i = Array.IndexOf(lines, header);
        Assert.That(i, Is.GreaterThanOrEqualTo(0), $"{header} missing");
        var result = new List<string>();
        for (int j = i + 1; j < lines.Length && lines[j] != "#END"; j++)
            if (lines[j].Length > 0 && !lines[j].StartsWith("//")) result.Add(lines[j]);
        return result;
    }
}
