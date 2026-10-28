using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Tutorial;

namespace SDVXEditor.Core.Tests;

/// <summary>The main tutorial (Fax's song): its definition, the solution chart, the exercise targets and the chapter charts.</summary>
public class MainTutorialTests
{
    private static string? repoFile(params string[] parts)
    {
        // from the test binaries (inside the repo) or, built elsewhere (--artifacts-path), from this source file
        foreach (string start in new[] { TestContext.CurrentContext.TestDirectory, Path.GetDirectoryName(sourceFile()) ?? "" })
        {
            for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "SDVXEditor.sln"))) return Path.Combine(new[] { d.FullName }.Concat(parts).ToArray());
            }
        }

        return null;
    }

    private static string sourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string? shippedFile(string name) => repoFile("src", "SDVXEditor.Resources", "Tutorial", "Main", name);

    private static MainTutorialSong shipped()
    {
        string? path = shippedFile("tutorial.json");
        if (path == null || !File.Exists(path)) Assert.Ignore("Tutorial/Main/tutorial.json not found");
        return MainTutorialSong.Parse(File.ReadAllText(path!));
    }

    private static Chart solution(MainTutorialSong def)
    {
        string? path = shippedFile("solution.kson");
        return MainTutorialCharts.Solution(def, path != null && File.Exists(path) ? File.ReadAllText(path) : null);
    }

    [Test]
    public void ShippedDefinitionMatchesTheSong()
    {
        var def = shipped();
        // 235 BPM, first downbeat 681.6 ms by onset analysis (docs/TUTORIAL.md), the drop at bar 16
        Assert.That(def.Bpm, Is.EqualTo(235));
        Assert.That(def.OffsetMs, Is.EqualTo(682));
        Assert.That(def.Drop, Is.EqualTo(15));
        Assert.That(def.Title, Is.EqualTo("[REDACTED] (Cut Ver.)"));
        Assert.That(def.Artist, Is.EqualTo("Fax"));
        Assert.That(def.SongSources, Is.EqualTo(new[] { "https://soundcloud.com/fax0508" }));
        Assert.That(def.License, Does.Contain("permission"));
        Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(shippedFile("tutorial.json"))!, def.Audio)), "the audio named in tutorial.json ships next to it");
        // the charted measures end before the audio does (53.76 s)
        Assert.That(def.LengthMs, Is.LessThan(53_760));
        Assert.That(def.MsOfMeasure(def.Drop), Is.EqualTo(682 + 15 * 4 * 60000.0 / 235).Within(1e-6));
    }

    [Test]
    public void MainPlanFitsItsBudget()
    {
        Assert.That(MainTutorialPlan.Chapters, Has.Count.EqualTo(8), "chapters 0-7");
        Assert.That(MainTutorialPlan.TotalMinutes, Is.InRange(MainTutorialPlan.TargetMinutes - 0.5, MainTutorialPlan.TargetMinutes + 0.5));
        Assert.That(MainTutorialPlan.TotalMinutes, Is.LessThanOrEqualTo(MainTutorialPlan.MaxMinutes), "the ceiling");
        Assert.That(MainTutorialPlan.TotalSteps, Is.EqualTo(95));
        Assert.That(MainTutorialPlan.Chapters.All(c => c.Steps >= 5 && c.Minutes is > 0 and <= 3.5), "no chapter is a stub or longer than 3.5 minutes");
        Assert.That(MainTutorialPlan.Chapters.All(c => c.Minutes * 60 >= c.Steps * 6), "every chapter's minutes cover its steps");
    }

    [Test]
    public void SolutionFitsEveryExercise()
    {
        var def = shipped();
        var chart = solution(def);
        Assert.That(MainTutorialCharts.Validate(def, chart), Is.Empty);

        var t = MainTutorialCharts.Targets(def, chart);
        Assert.That(t.Chips, Has.Count.EqualTo(4));
        Assert.That(t.Mirrored.Select(x => x.Lane), Is.EqualTo(t.Chips.Select(x => 3 - x.Lane)));
        Assert.That(t.Slam!.Points[0].IsSlam, Is.True);
        Assert.That(t.CurveLaser!.Curve, Is.True);
        Assert.That(t.WideLaser!.Wide, Is.True);
        Assert.That(t.ResizeStart!.Length, Is.LessThan(t.ResizeHold!.Length));
        Assert.That(chart.Editor.SongSources, Is.EqualTo(def.SongSources));
        Assert.That(chart.Editor.Bookmarks.Select(b => b.Value.Label), Is.EqualTo(def.Sections.Select(s => s.Name)));
    }

    [Test]
    public void PlaceholderIsReadableAtThisTempo()
    {
        var def = shipped();
        var chart = MainTutorialCharts.Placeholder(def);
        // quarter notes and a few eighths: nothing denser than 1/2 of a beat, no overlaps
        Assert.That(TutorialChecks.OffGrid(chart, 0, long.MaxValue, 2), Is.Empty);
        Assert.That(TutorialChecks.CountOverlaps(chart), Is.Zero);
        Assert.That(chart.Notes.Bt.Concat(chart.Notes.Fx).SelectMany(l => l).Max(n => n.EndY), Is.LessThanOrEqualTo(MainTutorialSong.At(def.Measures)));
        Assert.That(chart.Beat.ScrollSpeed.Single().Vf, Is.EqualTo(def.DropSpeed), "the speed jump at the drop (chapter 0's F6 exercise)");
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    [TestCase(7)]
    public void ChapterChartsRoundTripThroughKson(int chapter)
    {
        var def = shipped();
        var chart = MainTutorialCharts.ForChapter(def, solution(def), chapter);
        var back = KsonReader.Read(KsonWriter.Write(chart));
        Assert.That(back.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 235.0) }));
        Assert.That(back.Audio.Bgm.OffsetMs, Is.EqualTo(682));
        Assert.That(back.Editor.SongSources, Is.EqualTo(def.SongSources), "kson impl[\"sdvx-editor\"].song_sources");
        Assert.That(TutorialChecks.CountObjects(back, 0, long.MaxValue), Is.EqualTo(TutorialChecks.CountObjects(chart, 0, long.MaxValue)));
        Assert.That(TutorialChecks.CountOverlaps(back), Is.Zero);
    }

    [Test]
    public void ChapterStatesCutOutWhatTheChapterRebuilds()
    {
        var def = shipped();
        var sol = solution(def);
        var t = MainTutorialCharts.Targets(def, sol);
        var ex = def.Exercises;

        var c1 = MainTutorialCharts.ForChapter(def, sol, 1);
        Assert.That(TutorialChecks.CountPlaced(c1, t.Chips.Concat(t.Holds).Concat(t.FxChips).Concat(t.FxHold)), Is.Zero);
        Assert.That(TutorialChecks.IsPlaced(c1, t.StrayChip) && TutorialChecks.IsPlaced(c1, t.ResizeStart!));
        Assert.That(TutorialChecks.IsPlaced(c1, t.ResizeHold!) || TutorialChecks.IsPlaced(c1, t.ModifierHold!), Is.False);
        Assert.That(TutorialChecks.CountObjects(c1, ex.ChipPractice.FromPulse, ex.ChipPractice.ToPulse), Is.Zero, "the free bar is empty");
        Assert.That(TutorialChecks.IsPlaced(c1, t.Slam!), Is.False, "lasers come in chapter 2");
        Assert.That(TutorialChecks.CountObjects(c1, MainTutorialSong.At(def.Drop + 4), long.MaxValue), Is.GreaterThan(50), "the rest of the song is mapped already");

        var c2 = MainTutorialCharts.ForChapter(def, sol, 2);
        Assert.That(t.Chips.Concat(t.Mirrored).Concat(t.Holds).Append(t.ResizeHold!).All(x => TutorialChecks.IsPlaced(c2, x)));
        Assert.That(TutorialChecks.IsPlaced(c2, t.StrayChip), Is.False);
        Assert.That(TutorialChecks.IsPlaced(c2, t.StraightLaser!) || TutorialChecks.IsPlaced(c2, t.CurveLaser!), Is.False);
        Assert.That(TutorialChecks.CountObjects(c2, ex.LaserPractice.FromPulse, ex.LaserPractice.ToPulse), Is.Zero);

        var c3 = MainTutorialCharts.ForChapter(def, sol, 3);
        Assert.That(new[] { t.Slam!, t.StraightLaser!, t.CurveLaser!, t.WideLaser! }.All(x => TutorialChecks.IsPlaced(c3, x)));
        Assert.That(TutorialChecks.CountObjects(c3, ex.FinalPractice.FromPulse, ex.FinalPractice.ToPulse), Is.Zero, "chapter 6's free section stays empty until then");

        var c7 = MainTutorialCharts.ForChapter(def, sol, 7);
        Assert.That(TutorialChecks.CountObjects(c7, ex.FinalPractice.FromPulse, ex.FinalPractice.ToPulse), Is.GreaterThan(0));
    }

    [Test]
    public void DefinitionValidation()
    {
        Assert.Throws<FormatException>(() => MainTutorialSong.Parse("{\"measures\": 10}"));
        Assert.Throws<FormatException>(() => MainTutorialSong.Parse("{\"exercises\": {\"chips\": {\"from\": 0, \"to\": 2}}}"));
        Assert.Throws<FormatException>(() => MainTutorialSong.Parse("{\"exercises\": {\"finalPractice\": {\"from\": 46, \"to\": 50}}}"));
        Assert.DoesNotThrow(() => MainTutorialSong.Parse("{ // comment\n \"bpm\": 200, }"));
    }

    [Test]
    public void SolutionKsonMustKeepTheTempo()
    {
        var def = shipped();
        var other = MainTutorialCharts.Placeholder(def);
        other.Beat.Bpm.Add(new BpmChange(MainTutorialSong.At(20), 180));
        Assert.Throws<FormatException>(() => MainTutorialCharts.Solution(def, KsonWriter.Write(other)));
        Assert.That(MainTutorialCharts.Validate(def, MainTutorialCharts.Solution(def, KsonWriter.Write(MainTutorialCharts.Placeholder(def)))), Is.Empty,
            "the placeholder written out as solution.kson reads back and fits");
    }
}
