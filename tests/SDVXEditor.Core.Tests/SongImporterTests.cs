using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tests;

public class SongImporterTests
{
    private static (SongLibrary Lib, SongImporter Imp, string Editor, string Launcher) setup(TempDir t)
    {
        string editor = t.Sub("Songs");
        string launcher = t.Sub("launcher");
        var lib = new SongLibrary(new SongLibraryOptions { EditorRoot = editor, LauncherChartsPath = launcher, CachePath = "" });
        return (lib, new SongImporter(lib), editor, launcher);
    }

    /// <summary>Builds a zip from (entry name, content) pairs with raw entry names (so unsafe names can be written).</summary>
    private static string zip(TempDir t, string name, params (string Entry, byte[] Data)[] entries)
    {
        string p = Path.Combine(t.Sub("zips"), name);
        using (var fs = File.Create(p))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var (entry, data) in entries)
            {
                var e = z.CreateEntry(entry, CompressionLevel.Optimal);
                using var s = e.Open();
                s.Write(data);
            }
        }
        return p;
    }

    private static byte[] b(string s) => Encoding.UTF8.GetBytes(s);
    private static readonly byte[] kson = b(TempDir.MinimalKson("Zipped"));

    private static void assertNothingLeft(string editor)
        => Assert.That(Directory.GetDirectories(editor).Where(d => Path.GetFileName(d).StartsWith(".import-")), Is.Empty, "staging folder left behind");

    // ------------------------------------------------------------------ zip layouts

    [Test]
    public void ZipWithOneTopFolder()
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string z = zip(t, "download-123.zip", ("My Song/exh.kson", kson), ("My Song/song.ogg", new byte[] { 1 }), ("My Song/sub/bg.png", new byte[] { 2 }));
        var r = imp.Import(z);
        Assert.That(r.Kind, Is.EqualTo(SongImportKind.Zip));
        Assert.That(r.Song.FolderName, Is.EqualTo("My Song"), "folder name from the archive, not the zip name");
        Assert.That(r.Song.Source, Is.EqualTo(SongSource.Editor));
        Assert.That(r.Song.Title, Is.EqualTo("Zipped"));
        Assert.That(File.Exists(Path.Combine(editor, "My Song", "sub", "bg.png")));
        assertNothingLeft(editor);
    }

    [Test]
    public void FlatZipUsesTheZipName()
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string z = zip(t, "Flat Song.zip", ("exh.kson", kson), ("song.ogg", new byte[] { 1 }));
        var r = imp.Import(z);
        Assert.That(r.Song.FolderName, Is.EqualTo("Flat Song"));
        assertNothingLeft(editor);
    }

    [Test]
    public void MacOsJunkIsIgnored()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        string z = zip(t, "x.zip", ("S/exh.kson", kson), ("__MACOSX/S/._exh.kson", new byte[] { 0 }), ("S/.DS_Store", new byte[] { 0 }));
        var r = imp.Import(z);
        Assert.That(r.Song.FolderName, Is.EqualTo("S"));
        Assert.That(File.Exists(Path.Combine(r.Song.FolderPath, ".DS_Store")), Is.False);
    }

    [Test]
    public void TwoTopFoldersAreRefused()
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string z = zip(t, "x.zip", ("A/exh.kson", kson), ("B/exh.kson", kson));
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(z))!.Kind, Is.EqualTo("archive_layout"));
        Assert.That(Directory.GetDirectories(editor), Is.Empty);
    }

    [Test]
    public void ZipWithoutChartsIsRefused()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        string z = zip(t, "x.zip", ("A/song.ogg", new byte[] { 1 }));
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(z))!.Kind, Is.EqualTo("no_charts"));
    }

    [Test]
    public void CorruptZipIsRefused()
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string p = Path.Combine(t.Path, "broken.zip");
        File.WriteAllBytes(p, b("PK\u0003\u0004 this is not really a zip"));
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(p))!.Kind, Is.EqualTo("archive_invalid"));
        assertNothingLeft(editor);
    }

    // ------------------------------------------------------------------ zip safety

    [TestCase("../evil.kson")]
    [TestCase("S/../../evil.kson")]
    [TestCase("/abs/evil.kson")]
    [TestCase("C:/evil.kson")]
    [TestCase("..\\evil.kson")]
    [TestCase("S/file.kson:stream")]
    public void ZipSlipIsRefused(string entry)
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string z = zip(t, "evil.zip", ("S/exh.kson", kson), (entry, b("pwned")));
        var e = Assert.Throws<SongImportException>(() => imp.Import(z))!;
        Assert.That(e.Kind, Is.EqualTo("unsafe_path"));
        Assert.That(File.Exists(Path.Combine(t.Path, "evil.kson")), Is.False);
        Assert.That(File.Exists(Path.Combine(editor, "evil.kson")), Is.False);
        Assert.That(Directory.GetDirectories(editor).Where(d => !Path.GetFileName(d).StartsWith('.')), Is.Empty);
        assertNothingLeft(editor);
    }

    [Test]
    public void TooManyEntriesAreRefused()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        var entries = new List<(string, byte[])> { ("S/exh.kson", kson) };
        for (int i = 0; i < 20; i++) entries.Add(($"S/f{i}.txt", new byte[] { 1 }));
        string z = zip(t, "many.zip", entries.ToArray());
        var opts = new SongImportOptions { Zip = new ZipLimits { MaxEntries = 10 } };
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(z, opts))!.Kind, Is.EqualTo("archive_too_large"));
    }

    [Test]
    public void CompressionBombIsRefusedByRatio()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        // 4 MiB of zeros compresses ~1000:1 — beyond the bridge's 200x for entries above 1 MiB.
        string z = zip(t, "bomb.zip", ("S/exh.kson", kson), ("S/zeros.wav", new byte[4 * 1024 * 1024]));
        var e = Assert.Throws<SongImportException>(() => imp.Import(z))!;
        Assert.That(e.Kind, Is.EqualTo("archive_too_large"));
        Assert.That(e.Message, Does.Contain("fold"));
    }

    [Test]
    public void SmallCompressibleFilesAreFine()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        // 512 KiB of zeros: highly compressible but below the 1 MiB ratio floor.
        string z = zip(t, "ok.zip", ("S/exh.kson", kson), ("S/pad.txt", new byte[512 * 1024]));
        Assert.That(imp.Import(z).Song.FolderName, Is.EqualTo("S"));
    }

    [Test]
    public void UnpackedTotalIsCapped()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        var rnd = new Random(1);
        byte[] noise = new byte[300 * 1024];
        rnd.NextBytes(noise);
        string z = zip(t, "big.zip", ("S/exh.kson", kson), ("S/a.ogg", noise), ("S/b.ogg", noise));
        var opts = new SongImportOptions { Zip = new ZipLimits { MaxUnpackedBytes = 500 * 1024 } };
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(z, opts))!.Kind, Is.EqualTo("archive_too_large"));
    }

    [Test]
    public void ArchiveFileSizeIsCapped()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        string z = zip(t, "x.zip", ("S/exh.kson", kson));
        var opts = new SongImportOptions { Zip = new ZipLimits { MaxArchiveBytes = 10 } };
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(z, opts))!.Kind, Is.EqualTo("archive_too_large"));
    }

    // ------------------------------------------------------------------ name collisions

    [Test]
    public void ExistingNamePolicies()
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string z = zip(t, "x.zip", ("S/exh.kson", kson));
        Assert.That(imp.Import(z).Song.FolderName, Is.EqualTo("S"));

        var renamed = imp.Import(z);
        Assert.That(renamed.Song.FolderName, Is.EqualTo("S 2"));
        Assert.That(renamed.Warnings, Has.Some.Contains("already exists"));

        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(z, new SongImportOptions { Existing = ExistingSongPolicy.Fail }))!.Kind,
                    Is.EqualTo("song_exists"));

        File.WriteAllText(Path.Combine(editor, "S", "old.txt"), "x");
        var replaced = imp.Import(z, new SongImportOptions { Existing = ExistingSongPolicy.Replace });
        Assert.That(replaced.Song.FolderName, Is.EqualTo("S"));
        Assert.That(File.Exists(Path.Combine(editor, "S", "old.txt")), Is.False);
    }

    // ------------------------------------------------------------------ folder / chart / audio

    [Test]
    public void FolderAndChartFileImports()
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string src = TempDir.MakeSong(t.Sub("elsewhere"), "Dropped");
        var before = TempDir.Snapshot(src);

        var r = imp.Import(src);
        Assert.That(r.Kind, Is.EqualTo(SongImportKind.Folder));
        Assert.That(r.AlreadyInLibrary, Is.False);
        Assert.That(r.Song.FolderPath, Is.EqualTo(Path.Combine(editor, "Dropped")));
        Assert.That(TempDir.Snapshot(src), Is.EqualTo(before), "the source is copied, not moved");

        var r2 = imp.Import(Path.Combine(src, "exh.kson"));
        Assert.That(r2.Kind, Is.EqualTo(SongImportKind.ChartFile));
        Assert.That(r2.Song.FolderName, Is.EqualTo("Dropped 2"));
    }

    [Test]
    public void DroppingALibrarySongOpensItInPlace()
    {
        using var t = new TempDir("imp");
        var (lib, imp, editor, launcher) = setup(t);
        string s = TempDir.MakeSong(launcher, "Launcher Song");
        var r = imp.Import(Path.Combine(s, "exh.kson"));
        Assert.That(r.AlreadyInLibrary, Is.True);
        Assert.That(r.Song.FolderPath, Is.EqualTo(s));
        Assert.That(r.Song.IsReadOnly, Is.True);
        Assert.That(Directory.GetDirectories(editor), Is.Empty, "no copy");
    }

    [Test]
    public void FolderWithoutChartsAndUnknownFilesAreRefused()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(t.Sub("empty")))!.Kind, Is.EqualTo("no_charts"));
        string txt = Path.Combine(t.Path, "a.txt");
        File.WriteAllText(txt, "x");
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(txt))!.Kind, Is.EqualTo("unsupported"));
        Assert.That(Assert.Throws<SongImportException>(() => imp.Import(Path.Combine(t.Path, "missing.zip")))!.Kind, Is.EqualTo("not_found"));
    }

    [Test]
    public void AudioFileCreatesANewSong()
    {
        using var t = new TempDir("imp");
        var (_, imp, editor, _) = setup(t);
        string audio = Path.Combine(t.Path, "Some Artist - Great Tune.mp3");
        File.WriteAllBytes(audio, new byte[] { 0xFF, 0xFB, 0x90, 0x00 });
        var r = imp.Import(audio);
        Assert.That(r.Kind, Is.EqualTo(SongImportKind.Audio));
        Assert.That(r.Song.Title, Is.EqualTo("Great Tune"));
        Assert.That(r.Song.Artist, Is.EqualTo("Some Artist"));
        Assert.That(r.Song.FolderName, Is.EqualTo("Some Artist - Great Tune"));
        Assert.That(r.Song.AudioPath, Is.EqualTo(Path.Combine(editor, "Some Artist - Great Tune", "Some Artist - Great Tune.mp3")));
        Assert.That(r.Song.BpmMax, Is.EqualTo(120));
        var chart = SongFolder.Load(r.Song.FolderPath).Charts.Single();
        Assert.That(chart.Slot, Is.EqualTo(Difficulty.Exhaust));
        Assert.That(chart.Chart.Beat.Bpm.Single().Bpm, Is.EqualTo(120));
        Assert.That(chart.Chart.Audio.Bgm.Filename, Is.EqualTo("Some Artist - Great Tune.mp3"));
    }

    [Test]
    public void AudioTitleFromId3Tag()
    {
        using var t = new TempDir("imp");
        var (_, imp, _, _) = setup(t);
        string audio = Path.Combine(t.Path, "track01.mp3");
        File.WriteAllBytes(audio, id3v23(("TIT2", "タイトル"), ("TPE1", "Artiste")).Concat(new byte[] { 0xFF, 0xFB, 0x90, 0x00 }).ToArray());
        var r = imp.Import(audio);
        Assert.That(r.Song.Title, Is.EqualTo("タイトル"));
        Assert.That(r.Song.Artist, Is.EqualTo("Artiste"));
    }

    [Test]
    public void AudioTitleFromVorbisComment()
    {
        using var t = new TempDir("imp");
        string p = Path.Combine(t.Path, "x.ogg");
        File.WriteAllBytes(p, oggVorbis("TITLE=Ogg Title", "ARTIST=Ogg Artist"));
        Assert.That(AudioTags.Read(p), Is.EqualTo(((string?)"Ogg Title", (string?)"Ogg Artist")));
    }

    [Test]
    public void AudioTagsOfRealFiles()
    {
        var files = SongFolder.AudioExtensions.SelectMany(e => RealCharts.Files(e)).Where(f => f.EndsWith(".ogg") || f.EndsWith(".mp3")).ToList();
        if (files.Count == 0) Assert.Ignore("local chart library not found");
        foreach (var f in files)
        {
            var (title, artist) = AudioTags.Read(f); // must never throw
            TestContext.Out.WriteLine($"{Path.GetFileName(f)}: title={title ?? "-"} artist={artist ?? "-"}");
        }
    }

    // ------------------------------------------------------------------ tag fixtures

    private static byte[] id3v23(params (string Id, string Text)[] frames)
    {
        var body = new List<byte>();
        foreach (var (id, text) in frames)
        {
            byte[] data = new byte[] { 1, 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(text)).ToArray(); // UTF-16 with BOM
            body.AddRange(Encoding.ASCII.GetBytes(id));
            body.AddRange(new[] { (byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length });
            body.AddRange(new byte[] { 0, 0 });
            body.AddRange(data);
        }
        int n = body.Count;
        var head = new List<byte> { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, (byte)((n >> 21) & 0x7F), (byte)((n >> 14) & 0x7F), (byte)((n >> 7) & 0x7F), (byte)(n & 0x7F) };
        return head.Concat(body).ToArray();
    }

    private static byte[] oggVorbis(params string[] comments)
    {
        byte[] ident = new byte[] { 1 }.Concat(Encoding.ASCII.GetBytes("vorbis")).Concat(new byte[23]).ToArray();
        var c = new List<byte> { 3 };
        c.AddRange(Encoding.ASCII.GetBytes("vorbis"));
        byte[] vendor = Encoding.UTF8.GetBytes("test");
        c.AddRange(BitConverter.GetBytes(vendor.Length));
        c.AddRange(vendor);
        c.AddRange(BitConverter.GetBytes(comments.Length));
        foreach (var s in comments)
        {
            byte[] d = Encoding.UTF8.GetBytes(s);
            c.AddRange(BitConverter.GetBytes(d.Length));
            c.AddRange(d);
        }
        c.Add(1); // framing bit
        return page(ident).Concat(page(c.ToArray())).ToArray();

        static byte[] page(byte[] packet)
        {
            var lacing = new List<byte>();
            int left = packet.Length;
            while (left >= 255)
            {
                lacing.Add(255);
                left -= 255;
            }
            lacing.Add((byte)left);
            var h = new List<byte>();
            h.AddRange(Encoding.ASCII.GetBytes("OggS"));
            h.AddRange(new byte[22]); // version, flags, granule, serial, seq, crc (not checked)
            h.Add((byte)lacing.Count);
            h.AddRange(lacing);
            h.AddRange(packet);
            return h.ToArray();
        }
    }
}
