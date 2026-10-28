using NUnit.Framework;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tests;

/// <summary>The camera tab's "game background N, first used by song …" line (music_db.xml of the user's game).</summary>
public class MusicDbBackgroundsTests
{
    private const string xml = """
        <?xml version="1.0" encoding="Shift_JIS"?>
        <mdb>
          <music id="12">
            <info><title_name>Later &amp; Song</title_name><bg_no __type="u16">63</bg_no></info>
          </music>
          <music id="3">
            <info><title_name>First Song</title_name><bg_no __type="u16">63</bg_no></info>
          </music>
          <music id="7">
            <info><title_name>Other</title_name><bg_no __type="u16">2</bg_no></info>
          </music>
        </mdb>
        """;

    [Test]
    public void FirstSongAndCountPerBackground()
    {
        var db = MusicDbBackgrounds.Parse(xml);
        Assert.That(db.Find(63), Is.EqualTo(new MusicDbBackgrounds.Use(3, "First Song", 2)));
        Assert.That(db.Find(2)?.Title, Is.EqualTo("Other"));
        Assert.That(db.Find(5), Is.Null);
    }

    [Test]
    public void Descriptions()
    {
        var db = MusicDbBackgrounds.Parse(xml);
        Assert.That(MusicDbBackgrounds.Describe(63, db), Is.EqualTo("game background 63, first used by song 3 \"First Song\" (2 songs use it)"));
        Assert.That(MusicDbBackgrounds.Describe(2, db), Is.EqualTo("game background 2, first used by song 7 \"Other\""));
        Assert.That(MusicDbBackgrounds.Describe(9, db), Does.Contain("no official song uses it"));
        Assert.That(MusicDbBackgrounds.Describe(null, db), Does.Contain("writes 0"));
        Assert.That(MusicDbBackgrounds.Describe(63, null), Does.StartWith("game background 63 (no music_db.xml"));
    }

    [Test]
    public void ReadsTheInstalledGamesMusicDb()
    {
        const string path = @"D:\SDVX-RIP\SOUND VOLTEX NABLA\data\others\music_db.xml";
        if (!File.Exists(path)) Assert.Ignore("no game install on this machine");
        var db = MusicDbBackgrounds.Load(path);
        Assert.That(db, Is.Not.Null);
        Assert.That(db!.ByBgNo.Count, Is.GreaterThan(10));
        Assert.That(db.ByBgNo.Values.Sum(u => u.Count), Is.GreaterThan(2000), "one entry per song");
        TestContext.Out.WriteLine(MusicDbBackgrounds.Describe(63, db));
        Assert.That(MusicDbBackgrounds.Load(path), Is.SameAs(db), "cached per file time");
    }
}
