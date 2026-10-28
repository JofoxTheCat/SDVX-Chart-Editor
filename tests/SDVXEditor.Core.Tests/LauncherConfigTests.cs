using System.Text;
using NUnit.Framework;
using SDVXEditor.Core.Library;

namespace SDVXEditor.Core.Tests;

public class LauncherConfigTests
{
    private static string write(TempDir t, string name, byte[] bytes)
    {
        string p = Path.Combine(t.Path, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static byte[] utf8(string s, bool bom = false)
        => (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(Encoding.UTF8.GetBytes(s)).ToArray();

    [Test]
    public void ReadsChartsPath()
    {
        using var t = new TempDir("cfg");
        string p = write(t, LauncherConfig.FileName, utf8("{\"nautica_charts_path\": \"D:\\\\Charts\\\\Nautica\", \"other\": 1}"));
        var c = LauncherConfig.Load(p);
        Assert.That(c.NauticaChartsPath, Is.EqualTo(Path.GetFullPath(@"D:\Charts\Nautica")));
        Assert.That(c.Root["other"]!.GetValue<int>(), Is.EqualTo(1));
    }

    [Test]
    public void FindsBaseMusicDbs()
    {
        using var t = new TempDir("cfg-bases");
        string game = t.Sub("game");
        File.WriteAllText(Path.Combine(game, "spice64.exe"), "");
        string xml = Path.Combine(t.Sub("game", "data", "others"), "music_db.xml");
        File.WriteAllText(xml, "<mdb/>");
        string asphyxia = t.Sub("asphyxia");
        File.WriteAllText(Path.Combine(asphyxia, "asphyxia-core.exe"), "");
        t.Sub("asphyxia", "plugins", "other");                       // no index.ts: not the plugin
        string plugin = t.Sub("asphyxia", "plugins", "sdvx@asphyxia");
        File.WriteAllText(Path.Combine(plugin, "index.ts"), "");
        t.Sub("asphyxia", "plugins", "sdvx@asphyxia", "handlers");
        string json = Path.Combine(t.Sub("asphyxia", "plugins", "sdvx@asphyxia", "webui", "asset", "json"), "music_db.json");
        File.WriteAllText(json, "{}");

        // relative paths, like the launcher writes them: relative to the config's folder
        string p = write(t, LauncherConfig.FileName, utf8("{\"game_exe_path\": \"game/spice64.exe\", \"asphyxia_exe\": \"asphyxia/asphyxia-core.exe\"}"));
        var c = LauncherConfig.Load(p);
        Assert.That(c.GameDir, Is.EqualTo(Path.GetFullPath(game)));
        Assert.That(c.PluginDir, Is.EqualTo(Path.GetFullPath(plugin)));
        Assert.That(c.BaseMusicDbs, Is.EqualTo(new[] { Path.GetFullPath(json), Path.GetFullPath(xml) }));

        // nothing configured and no spice64.exe next to the config: no bases
        string bare = write(t, "bare.json", utf8("{}"));
        Assert.That(LauncherConfig.Load(bare).BaseMusicDbs, Is.Empty);
    }

    [Test]
    public void AcceptsUtf8Bom()
    {
        using var t = new TempDir("cfg");
        string p = write(t, LauncherConfig.FileName, utf8("{\"nautica_charts_path\": \"C:\\\\Jürgen\\\\曲\"}", bom: true));
        Assert.That(LauncherConfig.Load(p).NauticaChartsPath, Is.EqualTo(Path.GetFullPath(@"C:\Jürgen\曲")));
    }

    [Test]
    public void RelativePathIsRelativeToTheConfig()
    {
        using var t = new TempDir("cfg");
        string p = write(t, LauncherConfig.FileName, utf8("{\"nautica_charts_path\": \"test_charts\"}"));
        Assert.That(LauncherConfig.Load(p).NauticaChartsPath, Is.EqualTo(Path.Combine(t.Path, "test_charts")));
    }

    [Test]
    public void MissingOrEmptyKeyIsNull()
    {
        using var t = new TempDir("cfg");
        Assert.That(LauncherConfig.Load(write(t, "a.json", utf8("{}"))).NauticaChartsPath, Is.Null);
        Assert.That(LauncherConfig.Load(write(t, "b.json", utf8("{\"nautica_charts_path\": \"  \"}"))).NauticaChartsPath, Is.Null);
        Assert.That(LauncherConfig.Load(write(t, "c.json", utf8("{\"nautica_charts_path\": 5}"))).NauticaChartsPath, Is.Null);
    }

    [TestCase("{\"nautica_charts_path\": \"x\",}", TestName = "trailing comma")]
    [TestCase("{\"nautica_charts_path\": \"x\"", TestName = "truncated")]
    [TestCase("{\"a\": NaN}", TestName = "NaN (Python json accepts it, the bridge refuses it)")]
    [TestCase("", TestName = "empty file")]
    [TestCase("// comment\n{}", TestName = "comment")]
    public void BrokenJsonIsRefused(string text)
    {
        using var t = new TempDir("cfg");
        string p = write(t, LauncherConfig.FileName, utf8(text));
        var e = Assert.Throws<LauncherConfigException>(() => LauncherConfig.Load(p))!;
        Assert.That(e.Kind, Is.EqualTo("config_unreadable"));
    }

    [Test]
    public void InvalidUtf8IsRefused()
    {
        using var t = new TempDir("cfg");
        string p = write(t, LauncherConfig.FileName, new byte[] { (byte)'{', (byte)'"', 0xE4, (byte)'"', (byte)':', (byte)'1', (byte)'}' });
        Assert.That(Assert.Throws<LauncherConfigException>(() => LauncherConfig.Load(p))!.Kind, Is.EqualTo("config_unreadable"));
    }

    [Test]
    public void NonObjectIsRefused()
    {
        using var t = new TempDir("cfg");
        string p = write(t, LauncherConfig.FileName, utf8("[1,2]"));
        Assert.That(Assert.Throws<LauncherConfigException>(() => LauncherConfig.Load(p))!.Kind, Is.EqualTo("config_not_object"));
    }

    [Test]
    public void LocateStopsAtTheFirstExistingCandidateEvenIfBroken()
    {
        using var t = new TempDir("cfg");
        string missing = Path.Combine(t.Sub("a"), LauncherConfig.FileName);
        string broken = Path.Combine(t.Sub("b"), LauncherConfig.FileName);
        string good = Path.Combine(t.Sub("c"), LauncherConfig.FileName);
        File.WriteAllText(broken, "{nope");
        File.WriteAllText(good, "{\"nautica_charts_path\": \"x\"}");

        var r = LauncherConfig.Locate(null, new[] { missing, broken, good });
        Assert.That(r.Found, Is.False, "must not guess past a broken config");
        Assert.That(r.Error!.Kind, Is.EqualTo("config_unreadable"));
        Assert.That(r.Error.Path, Is.EqualTo(broken));
        Assert.That(r.Searched, Is.EqualTo(new[] { missing, broken }));

        var ok = LauncherConfig.Locate(null, new[] { missing, good, broken });
        Assert.That(ok.Config!.Path, Is.EqualTo(good));
    }

    [Test]
    public void ExplicitPathNeverFallsBack()
    {
        using var t = new TempDir("cfg");
        string good = Path.Combine(t.Sub("c"), LauncherConfig.FileName);
        File.WriteAllText(good, "{}");
        var r = LauncherConfig.Locate(Path.Combine(t.Path, "nope.json"), new[] { good });
        Assert.That(r.Found, Is.False);
        Assert.That(r.Error!.Kind, Is.EqualTo("config_not_found"));

        // A folder means "the config in that folder".
        Assert.That(LauncherConfig.Locate(Path.GetDirectoryName(good)).Config!.Path, Is.EqualTo(good));
    }

    [Test]
    public void NothingFoundIsNotAnError()
    {
        using var t = new TempDir("cfg");
        var r = LauncherConfig.Locate(null, new[] { Path.Combine(t.Path, LauncherConfig.FileName) });
        Assert.That(r.Found, Is.False);
        Assert.That(r.Error, Is.Null);
    }

    [Test]
    public void DefaultCandidatesListCwdAppDirAndDevExample()
    {
        using var t = new TempDir("cfg");
        string app = t.Sub("launcher", "tools", "editor");
        var c = LauncherConfig.DefaultCandidates(app, t.Path);
        Assert.That(c[0], Is.EqualTo(Path.Combine(t.Path, LauncherConfig.FileName)));
        Assert.That(c, Does.Contain(Path.Combine(t.Path, "launcher", LauncherConfig.FileName)));
        if (OperatingSystem.IsWindows()) Assert.That(c, Does.Contain(LauncherConfig.DevLocations[0]));
    }

    [Test]
    public void DevExampleConfigIfPresent()
    {
        string p = LauncherConfig.DevLocations[0];
        if (!File.Exists(p)) Assert.Ignore("dev launcher config not on this machine");
        var c = LauncherConfig.Load(p);
        Assert.That(c.NauticaChartsPath, Is.Not.Null.And.Not.Empty);
        TestContext.Out.WriteLine($"{p} -> {c.NauticaChartsPath}");
    }
}
