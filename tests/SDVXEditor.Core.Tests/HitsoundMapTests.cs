using NUnit.Framework;
using SDVXEditor.Core.Audio;

namespace SDVXEditor.Core.Tests;

public class HitsoundMapTests
{
    [Test]
    public void DefaultsFollowTheOsuManiaPick()
    {
        var m = HitsoundMap.Parse(null);
        Assert.That(m[HitsoundSlot.BtChip], Is.EqualTo("soft-hitnormal"));
        Assert.That(m[HitsoundSlot.BtHold], Is.EqualTo("soft-hitnormal"));
        Assert.That(m[HitsoundSlot.FxChip], Is.EqualTo("drum-hitnormal"));
        Assert.That(m[HitsoundSlot.FxHold], Is.EqualTo("drum-hitnormal"));
        Assert.That(m[HitsoundSlot.Slam], Is.EqualTo("normal-hitnormal"));
        Assert.That(m[HitsoundSlot.LaserStart], Is.EqualTo(HitsoundMap.None));
        Assert.That(HitsoundMap.Parse(HitsoundMap.DefaultText).ToString(), Is.EqualTo(HitsoundMap.DefaultText));
    }

    [Test]
    public void ParsesOverridesAndIgnoresJunk()
    {
        var m = HitsoundMap.Parse(" fx = soft-hitclap ; LASER=normal-slidertick, slam=SYNTH, bt=..\\..\\evil, nope=x, bthold");
        Assert.That(m[HitsoundSlot.FxChip], Is.EqualTo("soft-hitclap"));
        Assert.That(m[HitsoundSlot.LaserStart], Is.EqualTo("normal-slidertick"));
        Assert.That(m[HitsoundSlot.Slam], Is.EqualTo(HitsoundMap.Synth));
        Assert.That(m[HitsoundSlot.BtChip], Is.EqualTo("soft-hitnormal"), "a path is not a sample name");
        Assert.That(m[HitsoundSlot.BtHold], Is.EqualTo("soft-hitnormal"));
    }

    [Test]
    public void SkinLineIsTheOnlyThingRead()
    {
        using var t = new TempDir("osu");
        File.WriteAllLines(Path.Combine(t.Path, "osu!.tester.cfg"), new[]
        {
            "# osu! configuration for tester",
            "Password = hunter2",
            "SkinSamples = 1",
            "Skin = My Skin (v2)",
            "Skin = second",
        });
        Directory.CreateDirectory(Path.Combine(t.Path, "Skins", "My Skin (v2)"));
        File.WriteAllBytes(Path.Combine(t.Path, "Skins", "My Skin (v2)", "drum-hitnormal.ogg"), new byte[] { 1 });

        Assert.That(OsuSkinLocator.ReadSkinLine(Path.Combine(t.Path, "osu!.tester.cfg")), Is.EqualTo("My Skin (v2)"));
        string? folder = OsuSkinLocator.FindSkinFolder(t.Path, "tester");
        Assert.That(folder, Is.EqualTo(Path.Combine(t.Path, "Skins", "My Skin (v2)")));
        Assert.That(OsuSkinLocator.ResolveSample(folder, "drum-hitnormal"), Does.EndWith("drum-hitnormal.ogg"));
        Assert.That(OsuSkinLocator.ResolveSample(folder, "soft-hitnormal"), Is.Null);
    }

    [Test]
    public void SkinNameCannotLeaveTheSkinsFolder()
    {
        using var t = new TempDir("osu");
        File.WriteAllLines(Path.Combine(t.Path, "osu!.cfg"), new[] { "Skin = ..\\..\\Windows" });
        Assert.That(OsuSkinLocator.FindSkinFolder(t.Path, "nobody"), Is.Null);
        Assert.That(OsuSkinLocator.FindSkinFolder(Path.Combine(t.Path, "missing"), "nobody"), Is.Null);
    }
}
