using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Tutorial;

namespace SDVXEditor.Core.Tests;

/// <summary>The tutorial song definition, the chapter charts and the checks the tutorial steps use.</summary>
public class TutorialTests
{
    private static readonly TutorialSongDefinition def = new();
    private static long at(int m, double b = 0) => TutorialSongDefinition.At(m, b);

    private static string? repoFile(params string[] parts)
    {
        for (var d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(Path.Combine(d.FullName, "SDVXEditor.sln"))) return Path.Combine(new[] { d.FullName }.Concat(parts).ToArray());
        }

        return null;
    }

    [Test]
    public void ShippedDefinitionParsesAndMatchesTheDefaults()
    {
        string? path = repoFile("src", "SDVXEditor.Resources", "Tutorial", "tutorial.json");
        if (path == null || !File.Exists(path)) Assert.Ignore("tutorial.json not found");

        var parsed = TutorialSongDefinition.Parse(File.ReadAllText(path!));
        Assert.That(parsed.Bpm, Is.EqualTo(150));
        Assert.That(parsed.SpeedUp.Measure, Is.EqualTo(8));
        Assert.That(parsed.SpeedUp.Bpm, Is.EqualTo(175));
        Assert.That(parsed.OffsetMs, Is.EqualTo(300));
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(path!)!, parsed.Audio)), "the audio named in tutorial.json ships next to it");
    }

    [Test]
    public void TimeMathFollowsTheTempoChange()
    {
        Assert.That(def.MsAt(0), Is.EqualTo(300).Within(1e-9));
        Assert.That(def.MsAt(at(8)), Is.EqualTo(300 + 8 * 1600).Within(1e-9));
        Assert.That(def.MsAt(at(9)), Is.EqualTo(13100 + 4 * 60000.0 / 175).Within(1e-6));
        Assert.That(def.LengthMs, Is.LessThan(30_000), "the tutorial map is at most 30 s");
    }

    [Test]
    public void PlanFitsTheTenMinuteBudget()
    {
        // Johannes: the whole tutorial ≈ 10 minutes (it was 13.5 with 88 steps before the trim)
        Assert.That(TutorialPlan.Chapters, Has.Count.EqualTo(8), "chapters 0-7");
        Assert.That(TutorialPlan.TotalMinutes, Is.InRange(TutorialPlan.TargetMinutes - 0.5, TutorialPlan.TargetMinutes + 0.5));
        Assert.That(TutorialPlan.TotalSteps, Is.EqualTo(69));
        Assert.That(TutorialPlan.Chapters.All(c => c.Steps >= 5 && c.Minutes is > 0 and <= 2), "no chapter is a stub or longer than two minutes");
        // a step takes 6 s at the very least (reading the card): the estimate cannot be below what the steps need
        Assert.That(TutorialPlan.Chapters.All(c => c.Minutes * 60 >= c.Steps * 6), "every chapter's minutes cover its steps");
    }

    [Test]
    public void DefinitionValidation()
    {
        Assert.Throws<FormatException>(() => TutorialSongDefinition.Parse("{\"measures\": 10}"));
        Assert.Throws<FormatException>(() => TutorialSongDefinition.Parse("{\"bpm\": 5}"));
        Assert.DoesNotThrow(() => TutorialSongDefinition.Parse("{ // comment\n \"bpm\": 140, }"));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(6)]
    [TestCase(7)]
    public void ChapterChartsRoundTripThroughKson(int chapter)
    {
        var chart = TutorialCharts.ForChapter(def, chapter);
        var back = KsonReader.Read(KsonWriter.Write(chart));
        Assert.That(back.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 150.0), (at(8), 175.0) }));
        Assert.That(back.Audio.Bgm.OffsetMs, Is.EqualTo(300));
        Assert.That(back.Editor.Bookmarks.Select(b => b.Value.Label), Is.EqualTo(new[] { "intro", "main", "breakdown", "speed-up" }));
        Assert.That(TutorialCharts.CountObjects(back, 0, long.MaxValue), Is.EqualTo(TutorialCharts.CountObjects(chart, 0, long.MaxValue)));
        Assert.That(TutorialCharts.CountOverlaps(back), Is.Zero);
    }

    [Test]
    public void ChapterStatesBuildOnEachOther()
    {
        var c1 = TutorialCharts.ForChapter(def, 1);
        Assert.That(TutorialCharts.CountObjects(c1, 0, long.MaxValue), Is.EqualTo(1), "chapter 1 starts with the stray chip only");
        Assert.That(TutorialCharts.CountPlaced(c1, TutorialCharts.Chips), Is.Zero);

        var c2 = TutorialCharts.ForChapter(def, 2);
        foreach (var t in TutorialCharts.Chips.Concat(TutorialCharts.Mirrored).Concat(TutorialCharts.Holds).Concat(TutorialCharts.FxChips).Concat(TutorialCharts.FxHold))
            Assert.That(TutorialCharts.IsPlaced(c2, t), t.ToString());
        Assert.That(TutorialCharts.IsPlaced(c2, TutorialCharts.StraightLaser), Is.False);
        Assert.That(c2.Notes.Bt[0].Any(n => n.Y == TutorialCharts.StrayChip.Y), Is.False, "the stray chip is gone");

        var c3 = TutorialCharts.ForChapter(def, 3);
        Assert.That(TutorialCharts.IsPlaced(c3, TutorialCharts.StraightLaser));
        Assert.That(TutorialCharts.IsPlaced(c3, TutorialCharts.SlamLaser));
        Assert.That(TutorialCharts.IsPlaced(c3, TutorialCharts.WideLaser));
        Assert.That(TutorialCharts.CountSlams(c3, at(7), at(8)), Is.EqualTo(1), "chapter 4's spin needs the slam of bar 8");
    }

    [Test]
    public void GhostMatchingIsExactInTimeAndLaneTolerantInValue()
    {
        var chart = TutorialCharts.Base(def);
        var chip = TutorialCharts.Chips[0];
        TutorialCharts.Place(chart, chip with { Lane = 0 });
        Assert.That(TutorialCharts.IsPlaced(chart, chip), Is.False, "wrong lane");
        TutorialCharts.Place(chart, chip with { Y = chip.Y + 30 });
        Assert.That(TutorialCharts.IsPlaced(chart, chip), Is.False, "off by a 1/8");
        TutorialCharts.Place(chart, chip);
        Assert.That(TutorialCharts.IsPlaced(chart, chip));

        var slam = TutorialCharts.SlamLaser;
        chart.Notes.Lasers[1].Add(new LaserSection(slam.Y, new[] { new LaserPoint(0, 0, 1), new LaserPoint(480, 1) }));
        Assert.That(TutorialCharts.IsPlaced(chart, slam), Is.False, "a slam the other way round");
        chart.Notes.Lasers[1].Clear();
        chart.Notes.Lasers[1].Add(new LaserSection(slam.Y, new[] { new LaserPoint(0, 0.98, 0.02), new LaserPoint(480, 0.02) }));
        Assert.That(TutorialCharts.IsPlaced(chart, slam), "values within the tolerance");

        chart.Notes.Lasers[0].Add(new LaserSection(TutorialCharts.CurveLaser.Y, TutorialCharts.CurveLaser.Points.Select(p => p.Clone())));
        Assert.That(TutorialCharts.IsPlaced(chart, TutorialCharts.CurveLaser), Is.False, "three straight points are not a curve");
    }

    [Test]
    public void FreeMappingRules()
    {
        var chart = TutorialCharts.Base(def);
        long from = at(10), to = at(12);
        for (int i = 0; i < 8; i++) TutorialCharts.Place(chart, TutorialTarget.Chip(NoteKind.Bt, i % 4, from + i * 60));
        Assert.That(TutorialCharts.CountObjects(chart, from, to), Is.EqualTo(8));
        Assert.That(TutorialCharts.OffGrid(chart, from, to, 4), Is.Empty, "1/4 = 60 pulses");
        Assert.That(TutorialCharts.OffGrid(chart, from, to, 2), Has.Count.EqualTo(4), "every second one is off the 1/2 grid");
        Assert.That(TutorialCharts.CountSlams(chart, from, to), Is.Zero);
        TutorialCharts.Place(chart, TutorialTarget.Laser(1, at(11, 2), false, new LaserPoint(0, 0, 1), new LaserPoint(240, 1)));
        Assert.That(TutorialCharts.CountSlams(chart, from, to), Is.EqualTo(1));
        Assert.That(TutorialCharts.CountLasers(chart, from, to), Is.EqualTo(1));
        Assert.That(TutorialCharts.CountBeatChips(chart, from, to), Is.EqualTo(2), "chips exactly on a beat");
    }
}
