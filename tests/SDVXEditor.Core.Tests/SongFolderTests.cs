using NUnit.Framework;
using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class SongFolderTests
{
    [Test]
    public void KsonBeatsKshWithTheSameStem()
    {
        var picked = SongFolder.PickChartFiles(new[] { "azalea_MXM.ksh", "azalea_MXM.kson", "azalea_EXH.ksh", "jacket.png", "song.ogg" });
        Assert.That(picked, Is.EqualTo(new[] { "azalea_EXH.ksh", "azalea_MXM.kson" }));
    }

    [TestCase("nov.ksh", null, 0)]
    [TestCase("ADV.ksh", null, 1)]
    [TestCase("mysong_exh.ksh", null, 2)]
    [TestCase("grv.ksh", null, 3)]
    [TestCase("mxm.ksh", "infinite", 4)] // filename wins (he4ven case)
    [TestCase("i.ksh", null, 3)]
    [TestCase("m.kson", "3", 4)]
    [TestCase("chart.ksh", "challenge", 1)]
    [TestCase("chart.kson", "2", 2)]
    [TestCase("MXMver6.1.ksh", "infinite", 4)]
    public void DifficultyResolution(string file, string? declared, int expected)
        => Assert.That(Difficulty.Resolve(file, declared), Is.EqualTo(expected));

    [Test]
    public void UnknownDifficultyIsNull() => Assert.That(Difficulty.Resolve("chart.ksh", "weird"), Is.Null);

    [Test]
    public void VariantNameComesFromTheFilename()
    {
        Assert.That(Difficulty.FromFileName("song_vvd.ksh", out var name), Is.EqualTo(3));
        Assert.That(name, Is.EqualTo("VVD"));
    }

    [Test]
    public void NauticaLibraryLoads()
    {
        string root = RealCharts.RequireRoot();
        foreach (string dir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.Ordinal))
        {
            var song = SongFolder.Load(dir);
            Assert.That(song.Errors, Is.Empty, dir);
            Assert.That(song.Charts, Is.Not.Empty, dir);
            Assert.That(song.Charts.Select(c => c.Slot).Distinct().Count(), Is.EqualTo(song.Charts.Count), $"{dir}: one chart per slot");
            foreach (var c in song.Charts)
            {
                Assert.That(c.AudioPath, Is.Not.Null, c.FilePath);
                Assert.That(c.JacketPath, Is.Not.Null, c.FilePath);
            }
            TestContext.Out.WriteLine($"{song.Name}: " + string.Join(", ", song.Charts.Select(c =>
                $"{c.Chart.Meta.DifficultyDisplayName} {c.Chart.Meta.Level} ({Path.GetFileName(c.FilePath)}, jacket {Path.GetFileName(c.JacketPath)})")));
        }
    }

    [Test]
    public void JacketsComeFromTheChartsOwnDeclaration()
    {
        string root = RealCharts.RequireRoot();
        // CLAUDE.md §8b: the "first image" rule picked HoS.png / bg0.jpg / black.jpg for these songs.
        AssertJackets(root, "Canto IX Boss 10 Theme", "jacket.png");
        AssertJackets(root, "galvanizer_p3p", "jak.png");
        AssertJackets(root, "thedevilincarnate_p3p", "jak.png");
        var aureole = Path.Combine(root, "AureoLe_RUSHovEr_for_Angelus");
        if (Directory.Exists(aureole))
        {
            var song = SongFolder.Load(aureole);
            Assert.That(Path.GetFileName(song[Difficulty.Maximum]!.JacketPath), Is.EqualTo("MXM.jpg"), "per-difficulty jacket");
            Assert.That(Path.GetFileName(song[Difficulty.Exhaust]!.JacketPath), Is.EqualTo("AureoLe_RUSHovEr_for_Angelus.png"));
        }
    }

    private static void AssertJackets(string root, string folder, string expected)
    {
        string dir = Path.Combine(root, folder);
        if (!Directory.Exists(dir)) return;
        foreach (var c in SongFolder.Load(dir).Charts)
        {
            Assert.That(Path.GetFileName(c.JacketPath), Is.EqualTo(expected), c.FilePath);
            Assert.That(c.JacketIsFallback, Is.False);
        }
    }

    [Test]
    public void AbyssMotionUsesTheKson()
    {
        string dir = Path.Combine(RealCharts.RequireRoot(), "ABYSS MOTION 192.333");
        if (!Directory.Exists(dir)) Assert.Ignore("ABYSS MOTION not in the local library");
        var song = SongFolder.Load(dir);
        Assert.That(song.Charts, Has.Count.EqualTo(1));
        Assert.That(song.Charts[0].Format, Is.EqualTo(ChartFormat.Kson));
        Assert.That(song.Charts[0].Slot, Is.EqualTo(3));
        Assert.That(Path.GetFileName(song.Charts[0].AudioPath), Is.EqualTo("ABYSS MOTION 192.333.mp3"));
    }

    [Test]
    public void SlotsOfTheSampleLibrary()
    {
        string root = RealCharts.RequireRoot();
        void Expect(string folder, params int[] slots)
        {
            string dir = Path.Combine(root, folder);
            if (Directory.Exists(dir)) Assert.That(SongFolder.Load(dir).Charts.Select(c => c.Slot), Is.EqualTo(slots), folder);
        }
        Expect("Jumpy Star", 0, 1, 2, 4);
        Expect("galvanizer_p3p", 0, 1, 2, 4);
        Expect("Aegleseeker", 4);
        Expect("AureoLe_RUSHovEr_for_Angelus", 2, 4);
        Expect("じゃすと・べりー・くいっくりー！ - ジャベリン", 0, 1, 2, 3);
    }
}
