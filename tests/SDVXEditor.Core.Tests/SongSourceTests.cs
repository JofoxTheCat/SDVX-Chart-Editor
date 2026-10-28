using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Credits;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tests;

/// <summary>Song sources: the music-site allow-list, kson/ksh storage, the song-level read and the Thanks file.</summary>
[TestFixture]
public class SongSourceTests
{
    // ---- allow-list and normalisation

    [TestCase("https://soundcloud.com/fax0508", "SoundCloud", "https://soundcloud.com/fax0508")]
    [TestCase("https://on.soundcloud.com/AbC12", "SoundCloud", "https://on.soundcloud.com/AbC12")]
    [TestCase("https://m.soundcloud.com/fax0508/track?si=123abc&utm_source=clipboard&utm_medium=text", "SoundCloud", "https://m.soundcloud.com/fax0508/track")]
    [TestCase("https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC?si=a1b2c3", "Spotify", "https://open.spotify.com/track/4uLU6hMCjMI75M1A2tKUQC")]
    [TestCase("https://spotify.link/xyz", "Spotify", "https://spotify.link/xyz")]
    [TestCase("https://www.deezer.com/track/123", "Deezer", "https://www.deezer.com/track/123")]
    [TestCase("https://deezer.page.link/abc", "Deezer", "https://deezer.page.link/abc")]
    [TestCase("https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42&si=share", "YouTube", "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=42")]
    [TestCase("https://youtu.be/dQw4w9WgXcQ?si=x", "YouTube", "https://youtu.be/dQw4w9WgXcQ")]
    [TestCase("https://m.youtube.com/watch?v=abc&feature=share", "YouTube", "https://m.youtube.com/watch?v=abc")]
    [TestCase("https://music.youtube.com/watch?v=abc&list=PL1", "YouTube Music", "https://music.youtube.com/watch?v=abc&list=PL1")]
    [TestCase("https://music.apple.com/jp/album/x/123?i=456", "Apple Music", "https://music.apple.com/jp/album/x/123?i=456")]
    [TestCase("https://someartist.bandcamp.com/track/song", "Bandcamp", "https://someartist.bandcamp.com/track/song")]
    [TestCase("https://bandcamp.com/discover", "Bandcamp", "https://bandcamp.com/discover")]
    [TestCase("https://tidal.com/browse/track/1", "Tidal", "https://tidal.com/browse/track/1")]
    [TestCase("https://listen.tidal.com/track/1", "Tidal", "https://listen.tidal.com/track/1")]
    [TestCase("https://music.amazon.co.jp/albums/B0", "Amazon Music", "https://music.amazon.co.jp/albums/B0")]
    [TestCase("https://www.beatport.com/track/x/1", "Beatport", "https://www.beatport.com/track/x/1")]
    [TestCase("https://www.nicovideo.jp/watch/sm9", "Niconico", "https://www.nicovideo.jp/watch/sm9")]
    [TestCase("https://nico.ms/sm9", "Niconico", "https://nico.ms/sm9")]
    [TestCase("https://audiomack.com/artist/song/x", "Audiomack", "https://audiomack.com/artist/song/x")]
    public void AcceptsMusicSites(string input, string service, string normalised)
    {
        var r = MusicLinks.Validate(input);
        Assert.That(r.Ok, Is.True, r.Error);
        Assert.That(r.Service!.Name, Is.EqualTo(service));
        Assert.That(r.Url, Is.EqualTo(normalised));
    }

    [TestCase("  soundcloud.com/fax0508  ", "https://soundcloud.com/fax0508")]
    [TestCase("http://soundcloud.com/fax0508", "https://soundcloud.com/fax0508")]
    [TestCase("HTTPS://SoundCloud.COM/Fax0508", "https://soundcloud.com/Fax0508")]
    [TestCase("https://soundcloud.com.:443/fax0508#t=1:00", "https://soundcloud.com/fax0508#t=1:00")]
    public void NormalisesToHttps(string input, string expected)
    {
        var r = MusicLinks.Validate(input);
        Assert.That(r.Ok, Is.True, r.Error);
        Assert.That(r.Url, Is.EqualTo(expected));
    }

    [Test]
    public void TrackingParametersCanBeKept()
    {
        var r = MusicLinks.Validate("https://open.spotify.com/track/1?si=abc", stripTracking: false);
        Assert.That(r.Url, Is.EqualTo("https://open.spotify.com/track/1?si=abc"));
    }

    [TestCase("", "Enter a link")]
    [TestCase("https://example.com/song", "not a music site")]
    [TestCase("https://evilyoutube.com/watch?v=x", "not a music site")]
    [TestCase("https://youtube.com.example.org/watch", "not a music site")]
    [TestCase("https://apple.com/music", "not a music site")]
    [TestCase("https://www.amazon.com/dp/B0", "not a music site")]
    [TestCase("ftp://soundcloud.com/x", "Only https")]
    [TestCase("javascript:alert(1)", "Only https")]
    [TestCase("mailto:me@soundcloud.com", "Only https")]
    [TestCase("file:///C:/song.mp3", "Only https")]
    [TestCase("https://user:pw@soundcloud.com/x", "user name")]
    [TestCase("https://soundcloud.com:8443/x", "port")]
    [TestCase("https://127.0.0.1/x", "IP address")]
    [TestCase("https://soundcloud.com/a b", "spaces")]
    [TestCase("not a link", "spaces")]
    public void RefusesEverythingElse(string input, string messagePart)
    {
        var r = MusicLinks.Validate(input);
        Assert.That(r.Ok, Is.False);
        Assert.That(r.Error, Does.Contain(messagePart));
    }

    [Test]
    public void EveryServiceHasHostsAndNarrowHostsComeFirst()
    {
        Assert.That(MusicLinks.Services.All(s => s.Hosts.Count > 0 && s.Hosts.All(h => h == h.ToLowerInvariant())));
        Assert.That(MusicLinks.ServiceForHost("music.youtube.com")!.Name, Is.EqualTo("YouTube Music"));
        Assert.That(MusicLinks.ServiceForHost("www.youtube.com")!.Name, Is.EqualTo("YouTube"));
        Assert.That(MusicLinks.ServiceForHost("example.com"), Is.Null);
    }

    [Test]
    public void OpenableDropsInvalidAndDuplicates()
    {
        var list = MusicLinks.Openable(new[] { "https://soundcloud.com/fax0508", "https://evil.example/x", "http://soundcloud.com/fax0508", "", "https://youtu.be/x" });
        Assert.That(list.Select(l => l.Url), Is.EqualTo(new[] { "https://soundcloud.com/fax0508", "https://youtu.be/x" }));
        Assert.That(list.Select(l => l.Service.Name), Is.EqualTo(new[] { "SoundCloud", "YouTube" }));
    }

    [Test]
    public void DisplayHelpers()
    {
        Assert.That(MusicLinks.DisplayHost("https://www.youtube.com/watch?v=1"), Is.EqualTo("youtube.com"));
        Assert.That(MusicLinks.ShortForm("https://soundcloud.com/fax0508/"), Is.EqualTo("soundcloud.com/fax0508"));
        Assert.That(MusicLinks.ShortForm("https://soundcloud.com/" + new string('a', 100), 20), Has.Length.EqualTo(20));
    }

    // ---- storage

    private static Chart chart(params string[] sources)
    {
        var c = KsonReader.Read(TempDir.MinimalKson());
        c.Editor.SongSources = sources;
        return c;
    }

    [Test]
    public void KsonStoresSourcesInTheEditorImpl()
    {
        string text = KsonWriter.Write(chart("https://soundcloud.com/fax0508", "https://youtu.be/x"));
        Assert.That(text, Does.Contain("\"song_sources\""));
        var back = KsonReader.Read(text);
        Assert.That(back.Editor.SongSources, Is.EqualTo(new[] { "https://soundcloud.com/fax0508", "https://youtu.be/x" }));
        Assert.That(back.Extra.Count, Is.EqualTo(0));
        Assert.That(KsonWriter.Write(back), Is.EqualTo(text), "second generation byte-identical");
    }

    [Test]
    public void KsonWithoutSourcesIsUnchanged()
    {
        string text = KsonWriter.Write(chart());
        Assert.That(text, Does.Not.Contain("song_sources"));
        var back = KsonReader.Read(text);
        Assert.That(back.Editor.SongSources, Is.Empty);
        Assert.That(KsonWriter.Write(back), Is.EqualTo(text));
    }

    [Test]
    public void KsonSourcesWithForeignValuesRoundTripRaw()
    {
        // a hand-written impl with a number in the list: kept raw, nothing lost
        string doc = TempDir.MinimalKson()[..^1] + ",\"impl\":{\"sdvx-editor\":{\"song_sources\":[\"https://soundcloud.com/a\",5]}}}";
        var c = KsonReader.Read(doc);
        Assert.That(c.Editor.SongSources, Is.Empty);
        Assert.That(KsonWriter.Write(c), Does.Contain("\"song_sources\"").And.Contain("5"));
    }

    [Test]
    public void KshStoresSourcesAsCommentLines()
    {
        var c = chart("https://soundcloud.com/fax0508", "https://www.youtube.com/watch?v=abc&t=1");
        string ksh = KshWriter.Write(c);
        Assert.That(ksh, Does.Contain("//#source|https://soundcloud.com/fax0508\r\n"));
        Assert.That(ksh, Does.Contain("//#source|https://www.youtube.com/watch?v=abc&t=1\r\n"));
        // in the body (after the first bar line), where ksh2vox and KSM skip // lines even when they hold '='
        Assert.That(ksh.IndexOf("//#source|", StringComparison.Ordinal), Is.GreaterThan(ksh.IndexOf("\r\n--\r\n", StringComparison.Ordinal)));

        var back = KshReader.Read(ksh);
        Assert.That(back.Editor.SongSources, Is.EqualTo(c.Editor.SongSources));
        Assert.That(back.Editor.Comments.Any(x => x.Value.Contains("#source")), Is.False, "not read as a plain comment");
        Assert.That(KshWriter.Write(back), Is.EqualTo(ksh), "second generation byte-identical");
    }

    [Test]
    public void KshReadsSourceLinesInTheHeaderToo()
    {
        string ksh = "title=t\r\nartist=a\r\n//#source|https://soundcloud.com/x\r\nt=120\r\n--\r\n0000|00|--\r\n--\r\n";
        var c = KshReader.Read(ksh);
        Assert.That(c.Editor.SongSources, Is.EqualTo(new[] { "https://soundcloud.com/x" }));
        Assert.That(c.Editor.Comments, Is.Empty);
        Assert.That(c.Compat.KshUnknownMeta, Is.Empty);
    }

    [Test]
    public void KshDropsSourcesThatCannotBeOneLine()
    {
        string ksh = KshWriter.Write(chart("https://soundcloud.com/a\r\nt=999", "https://soundcloud.com/b"));
        Assert.That(ksh, Does.Not.Contain("t=999"));
        Assert.That(KshReader.Read(ksh).Editor.SongSources, Is.EqualTo(new[] { "https://soundcloud.com/b" }));
    }

    [Test]
    public void SongLevelReadTakesTheFirstNonEmptyChart()
    {
        using var tmp = new TempDir("src");
        string dir = TempDir.MakeSong(tmp.Path, "Song");
        File.WriteAllText(Path.Combine(dir, "adv.kson"), KsonWriter.Write(chart()));
        var mxm = chart("https://soundcloud.com/fax0508");
        mxm.Meta.DifficultySlot = 4;
        File.WriteAllText(Path.Combine(dir, "mxm.kson"), KsonWriter.Write(mxm));

        var song = SongFolder.Load(dir);
        Assert.That(song.Charts, Has.Count.EqualTo(3));
        Assert.That(song.SongSources, Is.EqualTo(new[] { "https://soundcloud.com/fax0508" }));

        var lib = LibrarySong.FromFolder(song, "fp", SongSource.Editor, false);
        Assert.That(lib.SongSources, Is.EqualTo(new[] { "https://soundcloud.com/fax0508" }));
    }

    [Test]
    public void OldSongsReadAsEmpty()
    {
        using var tmp = new TempDir("src");
        var song = SongFolder.Load(TempDir.MakeSong(tmp.Path, "Old"));
        Assert.That(song.SongSources, Is.Empty);
    }

    [Test]
    public void SiblingsGetTheListAndUnchangedFilesStayByteIdentical()
    {
        using var tmp = new TempDir("src");
        string dir = TempDir.MakeSong(tmp.Path, "Song"); // exh.kson
        File.WriteAllText(Path.Combine(dir, "adv.kson"), KsonWriter.Write(chart()));
        File.WriteAllText(Path.Combine(dir, "nov.ksh"), KshWriter.Write(chart()));
        string ksh = File.ReadAllText(Path.Combine(dir, "nov.ksh"));
        var song = SongFolder.Load(dir);
        string active = Path.Combine(dir, "exh.kson");
        var sources = new[] { "https://soundcloud.com/fax0508" };

        var written = SongSourceSync.WriteToSiblings(song, active, sources);
        Assert.That(written.Select(Path.GetFileName), Is.EqualTo(new[] { "adv.kson" }), "only the other kson; never the ksh, never the active file");
        Assert.That(KsonReader.ReadFile(Path.Combine(dir, "adv.kson")).Editor.SongSources, Is.EqualTo(sources));
        Assert.That(File.ReadAllText(Path.Combine(dir, "nov.ksh")), Is.EqualTo(ksh));
        Assert.That(song.Charts.Where(c => c.FilePath != active && c.Format == ChartFormat.Kson).All(c => c.Chart.Editor.SongSources.SequenceEqual(sources)));

        byte[] before = File.ReadAllBytes(Path.Combine(dir, "adv.kson"));
        Assert.That(SongSourceSync.WriteToSiblings(SongFolder.Load(dir), active, sources), Is.Empty, "same list: nothing rewritten");
        Assert.That(File.ReadAllBytes(Path.Combine(dir, "adv.kson")), Is.EqualTo(before));
    }

    [Test]
    public void NewDifficultyKeepsTheSources()
    {
        var c = Editing.NewDifficulty.Create(chart("https://soundcloud.com/x"), 4, copyNotes: false);
        Assert.That(c.Editor.SongSources, Is.EqualTo(new[] { "https://soundcloud.com/x" }));
    }

    // ---- Thanks

    private static string thisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Test]
    public void CreditsParseIsForgiving()
    {
        var f = CreditsFile.Parse("""
            { "title": "Thanks", "sections": [
                { "title": "A", "entries": [ { "name": "Fax", "role": "Tutorial song", "link": "https://soundcloud.com/fax0508" },
                                             { "name": "", "role": "skipped" },
                                             { "name": "NoLink", "link": "http://example.com" } ] },
                { "title": "", "entries": [ { "name": "x" } ] },
                { "title": "Empty", "entries": [] }, ] }
            """);
        Assert.That(f.Sections.Select(s => s.Title), Is.EqualTo(new[] { "A" }));
        Assert.That(f.Sections[0].Entries, Has.Count.EqualTo(2));
        Assert.That(f.Sections[0].Entries[0].Link, Is.EqualTo("https://soundcloud.com/fax0508"));
        Assert.That(f.Sections[0].Entries[1].Link, Is.Null, "http links are dropped");
    }

    [Test]
    public void ShippedCreditsFileParses()
    {
        string? dir = Path.GetDirectoryName(thisFile());
        while (dir != null && !File.Exists(Path.Combine(dir, "SDVXEditor.sln"))) dir = Path.GetDirectoryName(dir);
        if (dir == null) Assert.Ignore("repository root not found");
        var f = CreditsFile.Parse(File.ReadAllText(Path.Combine(dir!, "src", "SDVXEditor.Resources", "Credits", "credits.json")));
        var all = f.Sections.SelectMany(s => s.Entries).ToList();
        Assert.That(all.Any(e => e.Name == "Fax" && e.Role == "Tutorial song" && e.Link == "https://soundcloud.com/fax0508"));
        Assert.That(all.Any(e => e.Role == "Tutorial chart"));
        Assert.That(f.Sections.Any(s => s.Title == "Playtesters"));
        Assert.That(all.Any(e => e.Role.Contains("Noto") && e.Name.Contains("Open Font License")));
    }
}
