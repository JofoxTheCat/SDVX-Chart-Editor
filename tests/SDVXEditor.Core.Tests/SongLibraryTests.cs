using System.Diagnostics;
using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tests;

public class SongLibraryTests
{
    private static SongLibrary make(TempDir t, out string editorRoot, out string launcherRoot, bool cache = true)
    {
        editorRoot = t.Sub("editor", "Songs");
        launcherRoot = t.Sub("launcher_charts");
        return new SongLibrary(new SongLibraryOptions
        {
            EditorRoot = editorRoot,
            LauncherChartsPath = launcherRoot,
            CachePath = cache ? Path.Combine(t.Path, "cache.json") : "",
        });
    }

    [Test]
    public void ScanReadsMetadataFromBothRoots()
    {
        using var t = new TempDir("lib");
        var lib = make(t, out string editor, out string launcher);
        TempDir.MakeSong(editor, "Mine");
        TempDir.MakeSong(launcher, "Theirs");
        Directory.CreateDirectory(Path.Combine(launcher, "not a song"));
        Directory.CreateDirectory(Path.Combine(launcher, ".import-123"));
        File.WriteAllText(Path.Combine(launcher, ".import-123", "x.kson"), TempDir.MinimalKson());

        var r = lib.Scan();
        Assert.That(r.Songs, Is.EqualTo(2));
        var mine = lib.Songs.Single(s => s.FolderName == "Mine");
        var theirs = lib.Songs.Single(s => s.FolderName == "Theirs");
        Assert.That(mine.Source, Is.EqualTo(SongSource.Editor));
        Assert.That(mine.IsReadOnly, Is.False);
        Assert.That(theirs.Source, Is.EqualTo(SongSource.Launcher));
        Assert.That(theirs.IsReadOnly, Is.True);

        Assert.That(mine.Title, Is.EqualTo("Mine"));
        Assert.That(mine.Artist, Is.EqualTo("Tester"));
        Assert.That(mine.Charter, Is.EqualTo("Me"));
        Assert.That(mine.BpmDisplay, Is.EqualTo("150"));
        Assert.That(mine.BpmMax, Is.EqualTo(150));
        Assert.That(mine.JacketPath, Does.EndWith("jacket.png"));
        Assert.That(mine.AudioPath, Does.EndWith("song.ogg"));
        Assert.That(mine.LengthMs, Is.EqualTo(1600).Within(1), "last event at pulse 960 = 4 beats of 400 ms at 150 BPM");
        var exh = mine[Difficulty.Exhaust]!;
        Assert.That(exh.Level, Is.EqualTo(12));
        Assert.That(exh.BtCount, Is.EqualTo(3));
        Assert.That(exh.HoldCount, Is.EqualTo(1));
        Assert.That(exh.NoteCount, Is.EqualTo(3));
        Assert.That(exh.LaserSections, Is.EqualTo(1));
    }

    [Test]
    public void SecondScanComesFromTheCacheAndChangesAreDetected()
    {
        using var t = new TempDir("lib");
        var lib = make(t, out string editor, out _);
        string a = TempDir.MakeSong(editor, "A");
        TempDir.MakeSong(editor, "B");
        Assert.That(lib.Scan().Loaded, Is.EqualTo(2));

        // A new library instance (= next app start) reads everything from the cache file.
        var lib2 = make(t, out _, out _);
        var r2 = lib2.Scan();
        Assert.That(r2.FromCache, Is.EqualTo(2));
        Assert.That(r2.Loaded, Is.EqualTo(0));
        Assert.That(lib2.Songs.Select(s => s.Title), Is.EquivalentTo(new[] { "A", "B" }));
        Assert.That(lib2.DetectChanges().Any, Is.False);

        // Touch one chart, add one song, remove one.
        string chart = Path.Combine(a, "exh.kson");
        File.WriteAllText(chart, TempDir.MinimalKson("A renamed"));
        File.SetLastWriteTimeUtc(chart, DateTime.UtcNow.AddMinutes(1));
        TempDir.MakeSong(editor, "C");
        Directory.Delete(Path.Combine(editor, "B"), true);

        var ch = lib2.DetectChanges();
        Assert.That(ch.Changed.Select(Path.GetFileName), Is.EqualTo(new[] { "A" }));
        Assert.That(ch.Added.Select(Path.GetFileName), Is.EqualTo(new[] { "C" }));
        Assert.That(ch.Removed.Select(Path.GetFileName), Is.EqualTo(new[] { "B" }));

        var r3 = lib2.Scan();
        Assert.That(r3.Loaded, Is.EqualTo(2), "A (changed) and C (new) are parsed");
        Assert.That(r3.FromCache, Is.EqualTo(0));
        Assert.That(lib2.Songs.Single(s => s.FolderName == "A").Title, Is.EqualTo("A renamed"));
    }

    [Test]
    public void BrokenCacheIsOnlyASlowerScan()
    {
        using var t = new TempDir("lib");
        var lib = make(t, out string editor, out _);
        TempDir.MakeSong(editor, "A");
        File.WriteAllText(Path.Combine(t.Path, "cache.json"), "{ garbage");
        Assert.That(lib.Scan().Songs, Is.EqualTo(1));
    }

    [Test]
    public void EditingALauncherSongForksItAndNeverTouchesTheOriginal()
    {
        using var t = new TempDir("lib");
        var lib = make(t, out string editor, out string launcher);
        string original = TempDir.MakeSong(launcher, "Nautica Song");
        File.WriteAllText(Path.Combine(original, "readme.txt"), "keep me");
        lib.Scan();
        var before = TempDir.Snapshot(launcher);
        var song = lib.Songs.Single();
        Assert.That(song.IsReadOnly, Is.True);

        var chart = SongFolder.Load(original).Charts[0].Chart;
        chart.Meta.Title = "Edited";
        var saved = lib.SaveChart(song, chart);

        Assert.That(TempDir.Snapshot(launcher), Is.EqualTo(before), "launcher folder must stay byte-identical");
        Assert.That(saved.Source, Is.EqualTo(SongSource.Editor));
        Assert.That(saved.IsReadOnly, Is.False);
        Assert.That(saved.FolderName, Is.Not.EqualTo("Nautica Song"), "new name: the launcher's import map is keyed on the folder name");
        Assert.That(saved.FolderName, Is.EqualTo("Nautica Song (edit)"));
        Assert.That(Path.GetDirectoryName(saved.FolderPath), Is.EqualTo(editor));
        Assert.That(saved.ForkedFrom, Is.EqualTo(original));
        Assert.That(saved.Title, Is.EqualTo("Edited"));
        Assert.That(File.Exists(Path.Combine(saved.FolderPath, "readme.txt")), "the whole folder is copied");
        Assert.That(File.Exists(Path.Combine(saved.FolderPath, "song.ogg")));
        Assert.That(SongFolder.Load(original).Title, Is.EqualTo("Nautica Song"));

        // A second edit of the launcher song reuses the fork instead of making "(edit) 2".
        var again = lib.EnsureWritable(lib.Songs.Single(s => s.Source == SongSource.Launcher));
        Assert.That(again.FolderPath, Is.EqualTo(saved.FolderPath));
        Assert.That(Directory.GetDirectories(editor).Count(d => !Path.GetFileName(d).StartsWith('.')), Is.EqualTo(1));

        // Editor songs are written in place.
        chart.Meta.Title = "Edited twice";
        var inPlace = lib.SaveChart(saved, chart);
        Assert.That(inPlace.FolderPath, Is.EqualTo(saved.FolderPath));
        Assert.That(inPlace.Title, Is.EqualTo("Edited twice"));
        Assert.That(TempDir.Snapshot(launcher), Is.EqualTo(before));

        // After a rescan both are listed: the untouched launcher original and the fork.
        lib.Scan();
        Assert.That(lib.Songs.Select(s => s.FolderName), Is.EquivalentTo(new[] { "Nautica Song", "Nautica Song (edit)" }));
    }

    [Test]
    public void ForkNameAvoidsCollisions()
    {
        using var t = new TempDir("lib");
        var lib = make(t, out string editor, out string launcher);
        TempDir.MakeSong(launcher, "S");
        TempDir.MakeSong(editor, "S (edit)"); // unrelated editor song with that name
        lib.Scan();
        var fork = lib.EnsureWritable(lib.Songs.Single(s => s.Source == SongSource.Launcher));
        Assert.That(fork.FolderName, Is.EqualTo("S (edit) 2"));
    }

    [Test]
    public void FingerprintMatchesKsh2vox()
    {
        // folder_fingerprint exists since ksh2vox 0.14.0 — the submodule (v0.12.0) does not have it.
        string? converter = File.Exists(Path.Combine(VoxExportTests.LocalConverter, "ksh2vox.py")) ? VoxExportTests.LocalConverter : VoxExporter.FindConverterDir(null);
        var py = PythonLocator.Find(null);
        if (converter == null || py.Command == null) Assert.Ignore("needs Python and the converter");
        using var t = new TempDir("fp");
        string song = TempDir.MakeSong(t.Path, "Ünïcode 曲");
        File.WriteAllText(Path.Combine(song, "notes.txt"), "ignored by the fingerprint");

        var psi = new ProcessStartInfo(py.Command!.FileName) { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var a in py.Command.PrefixArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("import sys; sys.path.insert(0, sys.argv[1]); import ksh2vox; print(ksh2vox.folder_fingerprint(sys.argv[2]))");
        psi.ArgumentList.Add(converter);
        psi.ArgumentList.Add(song);
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        using var p = Process.Start(psi)!;
        string expected = p.StandardOutput.ReadToEnd().Trim().Split('\n').Last().Trim();
        p.WaitForExit();
        Assert.That(SongLibrary.Fingerprint(song), Is.EqualTo(expected));
    }

    [Test]
    public void ScanTheNauticaChartsFolder()
    {
        string root = RealCharts.RequireRoot();
        using var t = new TempDir("lib");
        var lib = new SongLibrary(new SongLibraryOptions
        {
            EditorRoot = t.Sub("Songs"),
            LauncherChartsPath = root,
            CachePath = Path.Combine(t.Path, "cache.json"),
        });
        var sw = Stopwatch.StartNew();
        var r = lib.Scan();
        var first = sw.Elapsed;
        Assert.That(r.Songs, Is.EqualTo(Directory.GetDirectories(root).Count(d => Directory.EnumerateFiles(d).Any(SongFolder.IsChartFile))));
        foreach (var s in lib.Songs)
        {
            TestContext.Out.WriteLine($"{s.FolderName}: \"{s.Title}\" / {s.Artist} / {s.Charter} / {s.BpmDisplay} BPM / {s.LengthMs / 1000:0.0}s / jacket {Path.GetFileName(s.JacketPath)} / "
                                      + string.Join(", ", s.Charts.Select(c => $"{c.DifficultyName} {c.Level} ({c.NoteCount} notes, {c.Slams} slams)")));
            Assert.That(s.Source, Is.EqualTo(SongSource.Launcher));
            Assert.That(s.IsReadOnly, Is.True);
            Assert.That(s.Charts, Is.Not.Empty);
            Assert.That(s.Errors, Is.Empty);
            Assert.That(s.Title, Is.Not.Empty);
        }
        sw.Restart();
        var again = new SongLibrary(new SongLibraryOptions { EditorRoot = t.Sub("Songs"), LauncherChartsPath = root, CachePath = Path.Combine(t.Path, "cache.json") }).Scan();
        TestContext.Out.WriteLine($"first scan {first.TotalMilliseconds:0} ms, cached scan {sw.Elapsed.TotalMilliseconds:0} ms");
        Assert.That(again.FromCache, Is.EqualTo(r.Songs));
    }

    [Test]
    public void BackgroundImageAndModifiedTimeAreRecorded()
    {
        using var t = new TempDir("libbg");
        var lib = make(t, out string editor, out _);
        string folder = TempDir.MakeSong(editor, "Bg");
        string chart = Directory.EnumerateFiles(folder, "*.kson").Single();
        // ksh-style legacy bg list: a preset first (no file), then a real image with different case
        var kson = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(chart))!.AsObject();
        kson["bg"] = new System.Text.Json.Nodes.JsonObject
        {
            ["legacy"] = new System.Text.Json.Nodes.JsonObject { ["bg"] = new System.Text.Json.Nodes.JsonArray { new System.Text.Json.Nodes.JsonObject { ["filename"] = "desert" }, new System.Text.Json.Nodes.JsonObject { ["filename"] = "BG0.PNG" } } },
        };
        File.WriteAllText(chart, kson.ToJsonString());
        File.WriteAllBytes(Path.Combine(folder, "bg0.png"), TempDir.TinyPng);
        var when = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(chart, when);

        lib.Scan();
        var song = lib.Songs.Single();
        Assert.That(song.BackgroundPath, Does.EndWith("bg0.png").IgnoreCase);
        Assert.That(song.ModifiedUtc, Is.EqualTo(when));

        // and a song with only a preset has none
        Assert.That(LibrarySong.ResolveBackground(folder, new BgInfo { LegacyBg = { "ocean" } }), Is.Null);
    }
}
