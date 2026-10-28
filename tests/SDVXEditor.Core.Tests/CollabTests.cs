using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tests;

/// <summary>Labelled bookmarks in kson / ksh, the bookmark command, and the .svz package.</summary>
public class CollabTests
{
    private static Chart chartWithBookmarks()
    {
        var c = new Chart();
        c.Meta.Title = "Collab";
        c.Meta.DifficultySlot = 2;
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[1].Add(new Note(4 * 960));
        c.Editor.Bookmarks.Set(960, new Bookmark("Part A - mapper1", "#FF66AA"));
        c.Editor.Bookmarks.Set(1920, new Bookmark("Part B – mapper2 | intro=drop"));
        c.Editor.Bookmarks.Set(2880, new Bookmark("", "#4fdb4f"));
        c.Editor.Bookmarks.Set(0, new Bookmark("start"));
        return c;
    }

    private static void assertBookmarks(Chart c)
    {
        var b = c.Editor.Bookmarks;
        Assert.That(b.Select(x => x.Y), Is.EqualTo(new long[] { 0, 960, 1920, 2880 }), "sorted by pulse");
        Assert.That(b[0].Value, Is.EqualTo(new Bookmark("start")));
        Assert.That(b[1].Value, Is.EqualTo(new Bookmark("Part A - mapper1", "#ff66aa")), "colour normalised to lower-case #rrggbb");
        Assert.That(b[2].Value, Is.EqualTo(new Bookmark("Part B – mapper2 | intro=drop")), "'|', '=' and non-ASCII survive");
        Assert.That(b[3].Value, Is.EqualTo(new Bookmark("", "#4fdb4f")));
    }

    [Test]
    public void BookmarksRoundTripThroughKson()
    {
        string json = KsonWriter.Write(chartWithBookmarks());
        Assert.That(json, Does.Contain("[960,\"Part A - mapper1\",\"#ff66aa\"]"));
        Assert.That(json, Does.Contain("[0,\"start\"]"), "no colour = the old [y, label] form");
        assertBookmarks(KsonReader.Read(json));
    }

    [Test]
    public void OldLabelOnlyKsonBookmarksStillRead()
    {
        string json = KsonWriter.Write(new Chart { Beat = { Bpm = { new BpmChange(0, 120) } } })
            .Replace("\"beat\"", "\"impl\":{\"sdvx-editor\":{\"bookmarks\":[[480,\"\"],[960,\"drop\"],{\"y\":1440,\"v\":\"obj\",\"color\":\"0f0\"}]}},\"beat\"");
        var b = KsonReader.Read(json).Editor.Bookmarks;
        Assert.That(b.Select(x => (x.Y, x.Value.Label, x.Value.Colour)), Is.EqualTo(new[] { (480L, "", (string?)null), (960L, "drop", null), (1440L, "obj", "#00ff00") }));
    }

    [Test]
    public void BookmarksRoundTripThroughKsh()
    {
        string ksh = KshWriter.Write(chartWithBookmarks());
        Assert.That(ksh, Does.Contain("//#bookmark|#ff66aa|Part A - mapper1\r\n"));
        Assert.That(ksh, Does.Contain("//#bookmark||start\r\n"));
        var back = KshReader.Read(ksh);
        assertBookmarks(back);
        Assert.That(back.Editor.Comments, Is.Empty, "bookmark lines are not read as plain comments");
        Assert.That(KshWriter.Write(back), Is.EqualTo(ksh), "ksh → model → ksh is stable");
    }

    [Test]
    public void KshBookmarkLineEdgeCases()
    {
        Assert.That(Bookmark.FromKshLine("//#bookmark|label only"), Is.EqualTo(new Bookmark("label only")));
        Assert.That(Bookmark.FromKshLine("//#bookmark|notacolour|x"), Is.EqualTo(new Bookmark("x")), "an invalid colour is dropped, the label kept");
        Assert.That(Bookmark.FromKshLine("//a plain comment"), Is.Null);
        Assert.That(new Bookmark("two\nlines\tand tab").ToKshLine(), Is.EqualTo("//#bookmark||two lines and tab"), "a label can never break the ksh line");
    }

    [Test]
    public void PlainKshCommentsAreUntouched()
    {
        var c = chartWithBookmarks();
        c.Editor.Comments.Add(new PulseValue<string>(960, "just a note"));
        var back = KshReader.Read(KshWriter.Write(c));
        Assert.That(back.Editor.Comments.Select(x => (x.Y, x.Value)), Is.EqualTo(new[] { (960L, "just a note") }));
    }

    [Test]
    public void BookmarkCommandIsUndoableAndAddressedByPulse()
    {
        var c = chartWithBookmarks();
        var stack = new CommandStack(c);
        var rename = SetBookmarkCommand.Create(c, 960, new Bookmark("Part A - done", "#ff66aa"))!;
        stack.Execute(rename);
        Assert.That(stack.IsDirty, "a bookmark edit marks the chart dirty");
        Assert.That(c.Editor.Bookmarks.At(960)!.Value.Label, Is.EqualTo("Part A - done"));

        // Ctrl+B adds outside the stack in the meantime: undo must still restore exactly the renamed one
        c.Editor.Bookmarks.Set(480, new Bookmark());
        stack.Undo();
        Assert.That(c.Editor.Bookmarks.At(960)!.Value, Is.EqualTo(new Bookmark("Part A - mapper1", "#ff66aa")));
        Assert.That(c.Editor.Bookmarks.At(480), Is.Not.Null);

        stack.Execute(SetBookmarkCommand.Create(c, 1920, null)!);
        Assert.That(c.Editor.Bookmarks.At(1920), Is.Null);
        stack.Undo();
        Assert.That(c.Editor.Bookmarks.At(1920)!.Value.Label, Does.StartWith("Part B"));

        Assert.That(SetBookmarkCommand.Create(c, 0, new Bookmark("start")), Is.Null, "no change = no command");
    }

    [Test]
    public void LabelOnlyAddStillCompiles()
    {
        var list = new BookmarkList();
        list.Add(new PulseValue<string>(10, "drop"));
        Assert.That(list[0].Value, Is.EqualTo(new Bookmark("drop")));
    }

    // ------------------------------------------------------------------ .svz

    private static (SongLibrary Lib, SongImporter Imp, string Editor) library(TempDir t)
    {
        string editor = t.Sub("Songs");
        var lib = new SongLibrary(new SongLibraryOptions { EditorRoot = editor, CachePath = "" });
        return (lib, new SongImporter(lib), editor);
    }

    /// <summary>A song with a .kson EXH (bookmarks), a .ksh ADV, audio, jacket, a key sound in a sub-folder, and a fork marker.</summary>
    private static string makeSong(TempDir t)
    {
        string dir = TempDir.MakeSong(t.Sub("src"), "Collab Song");
        var exh = KsonReader.ReadFile(Path.Combine(dir, "exh.kson"));
        exh.Editor.Bookmarks.Set(480, new Bookmark("Part B - mapper2", "#ff66aa"));
        KsonWriter.WriteFile(exh, Path.Combine(dir, "exh.kson"));

        var adv = KsonReader.ReadFile(Path.Combine(dir, "exh.kson"));
        adv.Meta.DifficultySlot = 1;
        adv.Meta.Level = 8;
        File.WriteAllText(Path.Combine(dir, "adv.ksh"), KshWriter.Write(adv), new UTF8Encoding(true));

        Directory.CreateDirectory(Path.Combine(dir, "sounds"));
        File.WriteAllBytes(Path.Combine(dir, "sounds", "clap.wav"), new byte[] { 9, 9 });
        File.WriteAllText(Path.Combine(dir, SongLibrary.ForkMarkerName), "{\"source\":\"C:/private/path\"}");
        return dir;
    }

    [Test]
    public void SvzExportContainsOneFolderAllChartsAsKsonAndAManifest()
    {
        using var t = new TempDir("svz");
        string song = makeSong(t);
        var r = SvzPackage.Export(song, Path.Combine(t.Path, "out", "Collab Song.svz"),
            new SvzExportOptions { CollabNotes = "B is yours", Now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero) });

        using var zip = ZipFile.OpenRead(r.Path);
        var names = zip.Entries.Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.That(names, Is.EqualTo(new[]
        {
            "Collab Song/adv.ksh", "Collab Song/adv.kson", "Collab Song/exh.kson", "Collab Song/jacket.png",
            "Collab Song/sdvxeditor.json", "Collab Song/song.ogg", "Collab Song/sounds/clap.wav",
        }), "one top folder; the ksh chart also as kson; fork marker (a local path) left out");

        var m = r.Manifest;
        Assert.That(m.Folder, Is.EqualTo("Collab Song"));
        Assert.That(m.Charts.Select(c => (c.File, c.Slot)), Is.EqualTo(new[] { ("adv.kson", 1), ("exh.kson", 2) }));
        Assert.That(m.Charts[1].Bookmarks.Single().Label, Is.EqualTo("Part B - mapper2"));
        Assert.That(m.Charts[1].Bookmarks.Single().TimeMs, Is.EqualTo(800), "480 pulses = 2 beats at 150 BPM");

        using var reader = new StreamReader(zip.GetEntry("Collab Song/sdvxeditor.json")!.Open());
        var parsed = SvzManifest.Parse(reader.ReadToEnd());
        Assert.That(parsed.CollabNotes, Is.EqualTo("B is yours"));
        Assert.That(parsed.ExportedAt, Is.EqualTo(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero)));
        Assert.That(parsed.Charts.Count, Is.EqualTo(2));
    }

    [Test]
    public void SvzRoundTripKeepsChartsBookmarksAndFiles()
    {
        using var t = new TempDir("svz");
        string song = makeSong(t);
        string svz = SvzPackage.Export(song, Path.Combine(t.Path, "Collab Song.svz")).Path;

        var (_, imp, editor) = library(t);
        var r = imp.Import(svz);
        Assert.That(r.Kind, Is.EqualTo(SongImportKind.Svz));
        Assert.That(r.Song.FolderName, Is.EqualTo("Collab Song"));
        Assert.That(r.Manifest, Is.Not.Null);
        Assert.That(File.Exists(Path.Combine(r.Song.FolderPath, SvzManifest.FileName)), Is.False, "the manifest is not left in the song folder");
        Assert.That(File.Exists(Path.Combine(r.Song.FolderPath, "sounds", "clap.wav")));

        var folder = SongFolder.Load(r.Song.FolderPath);
        Assert.That(folder.Charts.Select(c => (c.Slot, c.Format)), Is.EquivalentTo(new[] { (1, ChartFormat.Kson), (2, ChartFormat.Kson) }));
        var exh = folder[2]!.Chart;
        Assert.That(exh.Editor.Bookmarks.Single().Value, Is.EqualTo(new Bookmark("Part B - mapper2", "#ff66aa")));
        ModelSnapshot.AssertEqual(KsonReader.ReadFile(Path.Combine(song, "exh.kson")), exh, "exh survives the package byte for byte");

        // importing again: same folder name → keep both (default) or replace
        Assert.That(imp.WouldReplaceExisting(svz));
        Assert.That(imp.PlannedFolderName(svz), Is.EqualTo("Collab Song"));
        var copy = imp.Import(svz);
        Assert.That(copy.Song.FolderName, Is.EqualTo("Collab Song 2"), "keep both");
        var replaced = imp.Import(svz, new SongImportOptions { Existing = ExistingSongPolicy.Replace });
        Assert.That(replaced.Song.FolderName, Is.EqualTo("Collab Song"));
        Assert.That(Directory.GetDirectories(editor).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Collab Song", "Collab Song 2" }));
    }

    [Test]
    public void AnyZipOfASongFolderImportsAsSvzWithoutManifest()
    {
        using var t = new TempDir("svz");
        string song = TempDir.MakeSong(t.Sub("src"), "Plain");
        string svz = Path.Combine(t.Path, "plain.svz");
        ZipFile.CreateFromDirectory(song, svz, CompressionLevel.Optimal, includeBaseDirectory: true);
        var (_, imp, _) = library(t);
        var r = imp.Import(svz);
        Assert.That(r.Song.FolderName, Is.EqualTo("Plain"));
        Assert.That(r.Manifest, Is.Null);
    }

    [Test]
    public void BrokenManifestIsAWarningNotAnError()
    {
        using var t = new TempDir("svz");
        string song = TempDir.MakeSong(t.Sub("src"), "Broken");
        File.WriteAllText(Path.Combine(song, SvzManifest.FileName), "{ not json");
        string svz = Path.Combine(t.Path, "b.svz");
        ZipFile.CreateFromDirectory(song, svz, CompressionLevel.Optimal, includeBaseDirectory: true);
        var (_, imp, _) = library(t);
        var r = imp.Import(svz);
        Assert.That(r.Manifest, Is.Null);
        Assert.That(r.Warnings, Has.Some.Contains("manifest ignored"));
        Assert.That(File.Exists(Path.Combine(r.Song.FolderPath, SvzManifest.FileName)), Is.False);
    }

    [TestCase("../evil/exh.kson")]
    [TestCase("S/../../evil.kson")]
    [TestCase("/abs/exh.kson")]
    [TestCase("C:/x/exh.kson")]
    public void SvzZipSlipIsRefused(string entry)
    {
        using var t = new TempDir("svz");
        var (_, imp, editor) = library(t);
        string svz = Path.Combine(t.Path, "evil.svz");
        using (var fs = File.Create(svz))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using (var s = z.CreateEntry("S/exh.kson").Open()) s.Write(Encoding.UTF8.GetBytes(TempDir.MinimalKson("S")));
            using (var s = z.CreateEntry(entry).Open()) s.Write(Encoding.UTF8.GetBytes("x"));
        }

        var e = Assert.Throws<SongImportException>(() => imp.Import(svz))!;
        Assert.That(e.Kind, Is.EqualTo("unsafe_path"));
        Assert.That(Directory.GetDirectories(editor).Where(d => !Path.GetFileName(d).StartsWith('.')), Is.Empty, "nothing imported");
        Assert.That(Directory.Exists(Path.Combine(t.Path, "evil")), Is.False);
    }

    [Test]
    public void SvzSizeCapsComeFromTheImporter()
    {
        using var t = new TempDir("svz");
        string song = makeSong(t);
        Assert.Throws<InvalidOperationException>(() => SvzPackage.Export(song, Path.Combine(t.Path, "x.svz"), new SvzExportOptions { Limits = new ZipLimits { MaxEntries = 3 } }));
        Assert.That(File.Exists(Path.Combine(t.Path, "x.svz")), Is.False);
        Assert.That(File.Exists(Path.Combine(t.Path, "x.svz.partial")), Is.False);
    }

    [Test]
    public void ManifestParsingIsTolerant()
    {
        var m = SvzManifest.Parse("{\"format_version\":9,\"title\":\"T\",\"charts\":[{\"file\":\"a.kson\",\"slot\":\"x\",\"level\":\"17.5\",\"bookmarks\":[{\"y\":1,\"label\":\"L\",\"color\":\"#abc\"}]}],\"future\":{\"a\":1}}");
        Assert.That(m.IsNewerFormat);
        Assert.That(m.Title, Is.EqualTo("T"));
        Assert.That(m.Charts[0].Slot, Is.EqualTo(-1), "wrong type falls back");
        Assert.That(m.Charts[0].Level, Is.EqualTo(17.5));
        Assert.That(m.Charts[0].Bookmarks[0].Colour, Is.EqualTo("#aabbcc"));
        Assert.Throws<FormatException>(() => SvzManifest.Parse("[1,2]"));
        Assert.Throws<FormatException>(() => SvzManifest.Parse("{\"format\":\"osu-osz\"}"));
        Assert.That(SvzManifest.Parse("{}").Charts, Is.Empty);
    }
}
