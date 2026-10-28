using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Validation;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// Two laser sections of one side never share a pulse and keep at least <see cref="OverlapRule.MinLaserGap"/> apart
/// (the 2026-10-27 bug: a free drag stopped a lone slam 5 pulses before the next laser — two lasers "at once").
/// </summary>
public class LaserGapTests
{
    private static Chart chart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        return c;
    }

    private static LaserSection diagonal(long y) => new(y, new[] { new LaserPoint(0, 0.9), new LaserPoint(480, 0.1) });
    private static LaserSection slam(long y) => new(y, new[] { new LaserPoint(0, 0.6, 0) });

    [Test]
    public void RuleDefinition()
    {
        Assert.That(OverlapRule.SectionsTooClose(0, 240, 240, 480), Is.True, "shared pulse");
        Assert.That(OverlapRule.SectionsTooClose(0, 240, 245, 480), Is.True, "one VOX tick apart (the screenshot)");
        Assert.That(OverlapRule.SectionsTooClose(0, 240, 249, 480), Is.True);
        Assert.That(OverlapRule.SectionsTooClose(0, 240, 250, 480), Is.False, "1/96 note apart (ksh: end, '-', start at 192 lines)");
        Assert.That(OverlapRule.SectionsTooClose(250, 480, 0, 240), Is.False, "order does not matter");
        Assert.That(OverlapRule.SectionsTooClose(960, 960, 960 - OverlapRule.MinLaserGap + 1, 2000), Is.True, "lone slam inside the gap");
    }

    [Test]
    public void LoneSlamCannotSitRightBeforeALaser()
    {
        var c = chart();
        c.Notes.Lasers[1].Add(diagonal(960));
        foreach (long y in new long[] { 960, 955, 951 })
            Assert.That(OverlapRule.CanPlace(c, new[] { (1, slam(y)) }, new HashSet<LaserSection>()), Is.False, $"slam at {y}");
        Assert.That(OverlapRule.CanPlace(c, new[] { (1, slam(950)) }, new HashSet<LaserSection>()), Is.True);
        Assert.That(OverlapRule.CanPlace(c, new[] { (1, slam(1440 + 10)) }, new HashSet<LaserSection>()), Is.True, "after the end");
        Assert.That(OverlapRule.CanPlace(c, new[] { (1, slam(1440 + 5)) }, new HashSet<LaserSection>()), Is.False);
        Assert.That(OverlapRule.CanPlace(c, new[] { (0, slam(960)) }, new HashSet<LaserSection>()), Is.True, "the other side is free");
    }

    [Test]
    public void MovingASlamStopsAtTheGap()
    {
        var c = chart();
        var d = diagonal(960);
        var s = slam(480);
        c.Notes.Lasers[1].Add(s);
        c.Notes.Lasers[1].Add(d);
        var sel = new Selection();
        sel.Add(new LaserPointRef(1, s, s.Points[0]));
        Assert.That(SelectionOps.Move(c, sel, 475), Is.Null, "5 pulses before the laser");
        Assert.That(SelectionOps.Move(c, sel, 480), Is.Null, "on its start");
        Assert.That(SelectionOps.Move(c, sel, 470), Is.Not.Null, "1/96 before it");
    }

    [Test]
    public void PasteNextToALaserIsRefused()
    {
        var c = chart();
        c.Notes.Lasers[1].Add(diagonal(960));
        var src = chart();
        var s = slam(0);
        src.Notes.Lasers[1].Add(s);
        var sel = new Selection();
        sel.Add(new LaserPointRef(1, s, s.Points[0]));
        var clip = ClipboardData.FromSelection(sel);
        Assert.That(SelectionOps.Paste(c, clip, 955), Is.Null);
        Assert.That(SelectionOps.Paste(c, clip, 950), Is.Not.Null);
    }

    [Test]
    public void ExistingCloseSectionsStayEditable()
    {
        // a file (official charts do this 56 times) with a section 5 pulses after another: edits that keep the gap are fine
        var c = chart();
        var a = slam(955);
        var d = diagonal(960);
        c.Notes.Lasers[1].Add(a);
        c.Notes.Lasers[1].Add(d);
        var reshaped = new LaserSection(960, new[] { new LaserPoint(0, 0.5), new LaserPoint(480, 0.1) });
        Assert.That(OverlapRule.CanPlace(c, new[] { (1, reshaped) }, new HashSet<LaserSection> { d }), Is.True, "same bounds");
        var away = new LaserSection(1000, new[] { new LaserPoint(0, 0.5), new LaserPoint(440, 0.1) });
        Assert.That(OverlapRule.CanPlace(c, new[] { (1, away) }, new HashSet<LaserSection> { d }), Is.True, "farther away");
        var closer = new LaserSection(955, new[] { new LaserPoint(0, 0.5), new LaserPoint(485, 0.1) });
        Assert.That(OverlapRule.CanPlace(c, new[] { (1, closer) }, new HashSet<LaserSection> { d }), Is.False, "never onto it");
    }

    [Test]
    public void ChartCheckReportsTouchingAndCloseSections()
    {
        var c = chart();
        c.Notes.Lasers[1].Add(slam(955));
        c.Notes.Lasers[1].Add(diagonal(960));
        c.Notes.Lasers[0].Add(diagonal(0));
        c.Notes.Lasers[0].Add(slam(480));
        var issues = ChartChecker.Run(c).Issues;
        Assert.That(issues.Where(i => i.RuleId == "laser.gap").Select(i => i.Pulse), Is.EqualTo(new long[] { 960 }));
        Assert.That(issues.Where(i => i.RuleId == "laser.overlap").Select(i => i.Pulse), Is.EqualTo(new long[] { 480 }), "a slam on the end pulse");
        Assert.That(issues.First(i => i.RuleId == "laser.gap").LaserPoints, Has.Count.EqualTo(2), "jump selects both");
    }

    [Test]
    public void ChartCheckSeesALongSectionPastAShortOne()
    {
        var c = chart();
        c.Notes.Lasers[0].Add(new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(1920, 1) }));
        c.Notes.Lasers[0].Add(slam(480));
        c.Notes.Lasers[0].Add(slam(960));
        Assert.That(ChartChecker.Run(c).Issues.Count(i => i.RuleId == "laser.overlap"), Is.EqualTo(2));
    }

    [Test]
    public void DeleteRangeJoinsSectionsItPushesTogether()
    {
        // A ends inside the deleted range, B starts inside it: both land on its start — they join into one laser with a slam
        var c = chart();
        var a = new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(600, 0.5) });
        var b = new LaserSection(720, new[] { new LaserPoint(0, 1), new LaserPoint(480, 0.2) });
        c.Notes.Lasers[0].Add(a);
        c.Notes.Lasers[0].Add(b);
        var stack = new CommandStack(c);
        stack.Execute(TimeShift.DeleteRange(c, 480, 960)!.Command);

        Assert.That(c.Notes.Lasers[0], Has.Count.EqualTo(1));
        var joined = c.Notes.Lasers[0][0];
        Assert.That(joined.Points.Select(p => (p.Ry, p.V, p.Vf)), Is.EqualTo(new (long, double, double?)[] { (0, 0, null), (480, 0.5, 1), (720, 0.2, null) }));
        Assert.That(ChartChecker.Run(c).Issues.Where(i => i.RuleId.StartsWith("laser.")), Is.Empty);

        stack.Undo();
        Assert.That(c.Notes.Lasers[0], Is.EqualTo(new[] { a, b }));
        Assert.That((a.Y, a.EndY, b.Y, b.EndY), Is.EqualTo((0L, 600L, 720L, 1200L)));
        stack.Redo();
        Assert.That(c.Notes.Lasers[0], Has.Count.EqualTo(1));
    }

    [Test]
    public void ResnapJoinsSectionsWithinTheGap()
    {
        var c = chart();
        c.Notes.Lasers[1].Add(new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(475, 0.5) }));
        c.Notes.Lasers[1].Add(new LaserSection(490, new[] { new LaserPoint(0, 0.5), new LaserPoint(470, 1) }));
        var stack = new CommandStack(c);
        stack.Execute(TimingCommands.Resnap(c, 4));
        Assert.That(c.Notes.Lasers[1], Has.Count.EqualTo(1), string.Join(" | ", c.Notes.Lasers[1].Select(s => $"{s.Y}-{s.EndY}")));
        stack.Undo();
        Assert.That(c.Notes.Lasers[1], Has.Count.EqualTo(2));
    }

    [Test]
    public void DifferentWidthsAreNotJoined()
    {
        var lane = new List<(long, List<LaserPoint>, int)>
        {
            (0, new List<LaserPoint> { new(0, 0), new(480, 1) }, 1),
            (480, new List<LaserPoint> { new(0, 0.5), new(240, 0) }, 2),
        };
        Assert.That(RemapPulsesCommand.JoinClose(lane), Is.Null);
    }

    private static IEnumerable<TestCaseData> libraryCharts() => RealCharts.Cases(".ksh", "LibraryChartsKeepTheGap").Concat(RealCharts.Cases(".kson", "LibraryChartsKeepTheGap"));

    /// <summary>The real library (K-Shoot-written ksh / kson): no section shares a pulse; gaps under 1/96: none (25 gaps of exactly 1/96).</summary>
    [TestCaseSource(nameof(libraryCharts))]
    public void LibraryChartsKeepTheGap(string path)
    {
        RealCharts.RequireFile(path);
        var c = path.EndsWith(".kson", StringComparison.OrdinalIgnoreCase) ? KsonReader.ReadFile(path) : KshReader.ReadFile(path);
        var issues = ChartChecker.Run(c).Issues;
        Assert.That(issues.Where(i => i.RuleId == "laser.overlap").Select(i => i.Message), Is.Empty);
        Assert.That(issues.Where(i => i.RuleId == "laser.gap").Select(i => i.Message), Is.Empty, "K-Shoot never writes sections closer than 1/96");
    }
}
