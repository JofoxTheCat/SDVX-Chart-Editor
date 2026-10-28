using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Tests;

/// <summary>Classic (K-Shoot / kson editor) laser placement while the classic-laser key is held.</summary>
[TestFixture]
public class ClassicLaserTests
{
    private static LaserPath first(double v) => new(new[] { new LaserAnchor(0, v, type: LaserCurveType.Linear, auto: true) });

    /// <summary>Clicks in order; <c>classic</c> per click (true = key held).</summary>
    private static LaserPath place(LaserPath p, params (long Ry, double V, bool Classic)[] clicks)
    {
        foreach (var (ry, v, classic) in clicks)
            p = (classic ? ClassicLaser.Add(p, ry, v) : LaserPathEditing.AddAnchor(p, ry, v)) ?? throw new AssertionException($"click {ry} refused");
        return p;
    }

    private static Chart chartWith(LaserSection s)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Lasers[0].Add(s);
        return c;
    }

    [TestCase(0.33, false, 0.3)]
    [TestCase(0.36, false, 0.4)]
    [TestCase(0.33, true, 0.35)]
    [TestCase(1.2, false, 1.0)]
    [TestCase(-0.1, true, 0.0)]
    public void ValuesSnapToTenths(double v, bool wide, double expected) => Assert.That(ClassicLaser.Snap(v, wide), Is.EqualTo(expected).Within(1e-12));

    [Test]
    public void ClassicClicksArePlainStraightPoints()
    {
        var p = place(first(0.3), (240, 0.7, true), (480, 0.1, true), (720, 0.5, true));
        Assert.That(p.Segments().Select(s => s.Effective), Is.All.EqualTo(LaserCurveType.Linear), "no perfect curve / Bezier from the count");
        Assert.That(p.Segments(), Has.Count.EqualTo(3), "one straight segment per pair of points");
        Assert.That(p.IsTrivial, Is.True, "an all-classic laser stores no path");

        // export identical to the same points placed as a plain section
        var section = LaserPaths.ToSection(p, 960, 1);
        var plain = new LaserSection(960, new[] { new LaserPoint(0, 0.3), new LaserPoint(240, 0.7), new LaserPoint(480, 0.1), new LaserPoint(720, 0.5) });
        Assert.That(section.Path, Is.Null);
        Assert.That(KsonWriter.Write(chartWith(section)), Is.EqualTo(KsonWriter.Write(chartWith(plain))));
        Assert.That(KshWriter.Write(chartWith(section)), Is.EqualTo(KshWriter.Write(chartWith(plain))));
    }

    [Test]
    public void SlamAndFinishLikeTheKsonEditor()
    {
        var p = place(first(0.3), (240, 0.7, true));
        p = ClassicLaser.Add(p, 240, 0.2)!;
        Assert.That(p.Anchors[1] is { IsSlam: true, V: 0.7, Vf: 0.2 }, "same beat, other value = slam");
        Assert.That(ClassicLaser.IsFinishClick(p, 240, 0.9), Is.True, "after a slam, the same beat again finishes");
        Assert.That(ClassicLaser.IsFinishClick(p, 480, 0.2), Is.False);

        var q = place(first(0.3), (240, 0.7, true));
        Assert.That(ClassicLaser.IsFinishClick(q, 240, 0.7), Is.True, "the last point again finishes");
        Assert.That(ClassicLaser.IsFinishClick(q, 240, 0.6), Is.False, "another value there is a slam, not the finish");
        Assert.That(ClassicLaser.Add(q, 0, 0.5), Is.Null, "another anchor's pulse is refused");
    }

    [Test]
    public void ModesMixInOneChain()
    {
        // two anchor clicks, one classic point, then two anchor clicks again: the anchor typing restarts at the classic point
        var p = place(first(0.2), (240, 0.8, false), (480, 0.3, true), (720, 0.6, false), (960, 0.2, false));
        var segs = p.Segments();
        Assert.That(segs.Select(s => (s.Start, s.End, s.Effective)), Is.EqualTo(new[]
        {
            (0, 1, LaserCurveType.Linear), (1, 2, LaserCurveType.Linear), (2, 4, LaserCurveType.PerfectCurve),
        }));
        Assert.That(p.Anchors[2] is { Type: LaserCurveType.PerfectCurve, Auto: true }, "the last classic point stays an auto start");

        // classic after an anchor curve: the curve keeps its count, the classic point is joined straight
        var q = place(first(0.2), (240, 0.8, false), (480, 0.3, false), (720, 0.6, true));
        Assert.That(q.Segments().Select(s => s.Effective), Is.EqualTo(new[] { LaserCurveType.PerfectCurve, LaserCurveType.Linear }));
    }

    [Test]
    public void ClassicPointBeforeTheStart()
    {
        var p = place(first(0.5), (240, 0.6, false), (480, 0.2, false));
        Assert.That(p.Anchors[0].Type, Is.EqualTo(LaserCurveType.PerfectCurve));
        var shifted = new LaserPath(p.Anchors.Select(a => new LaserAnchor(a.Ry + 240, a.V, a.Vf, a.Type, a.Auto)));
        var q = ClassicLaser.Add(shifted, 0, 0.9)!;
        Assert.That(q.Segments().First().Effective, Is.EqualTo(LaserCurveType.Linear), "the new first point joins straight");
        Assert.That(q.Anchors[1].Type, Is.EqualTo(LaserCurveType.PerfectCurve), "the old first point keeps its curve");
    }
}
