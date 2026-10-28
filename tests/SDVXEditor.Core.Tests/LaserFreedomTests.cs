using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Tests;

/// <summary>Bezier control anchors moving freely, and widening / narrowing a placed laser.</summary>
[TestFixture]
public class LaserFreedomTests
{
    private static readonly (double, double) limits = (-0.5, 1.5);

    /// <summary>A 4-anchor Bezier 0..720: anchors 1 and 2 are controls.</summary>
    private static LaserPath bezier()
    {
        var p = new LaserPath(new[]
        {
            new LaserAnchor(0, 0.3, type: LaserCurveType.Linear, auto: true), new LaserAnchor(240, 0.7), new LaserAnchor(480, 0.4), new LaserAnchor(720, 0.6),
        });
        p.Normalize();
        return p;
    }

    private static Chart chartWith(LaserSection s)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Lasers[0].Add(s);
        return c;
    }

    [Test]
    public void ControlsAreRecognised()
    {
        var p = bezier();
        Assert.That(p.Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier));
        Assert.That(Enumerable.Range(0, 4).Select(p.IsControl), Is.EqualTo(new[] { false, true, true, false }));
        var three = new LaserPath(p.Anchors.Take(3).Select(a => a.Clone()));
        three.Normalize();
        Assert.That(three.IsControl(1), Is.False, "the middle of a perfect curve lies on the curve");
    }

    [Test]
    public void ControlMovesOutOfOrderAndOutOfRange()
    {
        var moved = LaserPathEditing.MoveAnchor(bezier(), 1, 600, 1.3, controlLimits: limits)!;
        Assert.That((moved.Anchors[1].Ry, moved.Anchors[1].V), Is.EqualTo((600L, 1.3)), "after the next anchor, past the lane");
        Assert.That(moved.IsControl(1) && moved.Anchors[2].Ry == 480, "the order of the anchors is kept, the curve keeps its controls");

        var clamped = LaserPathEditing.MoveAnchor(bezier(), 1, 5000, 9, controlLimits: limits)!;
        Assert.That((clamped.Anchors[1].Ry, clamped.Anchors[1].V), Is.EqualTo((719L, 1.5)), "inside the segment, inside the limits");

        var camera = LaserPathEditing.MoveAnchor(bezier(), 1, 600, 1.3)!;
        Assert.That((camera.Anchors[1].Ry, camera.Anchors[1].V), Is.EqualTo((479L, 1.0)), "without limits (camera lanes) the old clamping stays");

        // the end anchor stays after every control of its segment
        var end = LaserPathEditing.MoveAnchor(moved, 3, 500, 0.6, controlLimits: limits)!;
        Assert.That(end.Anchors[3].Ry, Is.EqualTo(601));
    }

    [Test]
    public void FreeControlsBakeToAValidLaser()
    {
        var moved = LaserPathEditing.MoveAnchor(bezier(), 1, 600, 1.4, controlLimits: limits)!;
        var s = LaserPaths.ToSection(moved, 960, 1);
        Assert.That(s.Points.All(pt => pt.V is >= 0 and <= 1), "values clamped to the lane");
        Assert.That(s.Points.Zip(s.Points.Skip(1)).All(x => x.First.Ry < x.Second.Ry), "time strictly increasing");
        Assert.That(LaserPaths.ValidPath(s), Is.Not.Null);

        // kson round trip keeps the free control (order and value)
        var back = KsonReader.Read(KsonWriter.Write(chartWith(s))).Notes.Lasers[0].Single();
        var path = LaserPaths.ValidPath(back)!;
        Assert.That((path.Anchors[1].Ry, path.Anchors[1].V, path.Anchors[2].Ry), Is.EqualTo((600L, 1.4, 480L)));
        Assert.That(LaserPaths.SamePoints(back.Points, s.Points, 1e-9), Is.True);
    }

    [Test]
    public void ThreeAnchorBezierWithAControlBeyondTheLaneIsNoKsonCurve()
    {
        var p = new LaserPath(new[] { new LaserAnchor(0, 0.2, type: LaserCurveType.Bezier), new LaserAnchor(240, 1.4), new LaserAnchor(480, 0.8) });
        p.Normalize();
        Assert.That(p.Anchors[1].V, Is.EqualTo(1.4), "a control keeps its value");
        var s = LaserPaths.ToSection(p, 0, 1);
        Assert.That(s.Points.All(pt => pt.Curve == null || pt.Curve.Value.IsLinear), "sampled, not one kson curve that would leave 0..1");
        Assert.That(s.Points.All(pt => pt.V is >= 0 and <= 1));
    }

    [Test]
    public void ANoLongerControlAnchorIsSortedAndClampedAgain()
    {
        var moved = LaserPathEditing.MoveAnchor(bezier(), 1, 600, 1.3, controlLimits: limits)!;
        var straight = LaserPathEditing.SetType(moved, 0, LaserCurveType.Linear)!;
        Assert.That(straight.Anchors.Select(a => a.Ry), Is.Ordered.Ascending);
        Assert.That(straight.Anchors.All(a => a.V is >= 0 and <= 1));
    }

    [TestCase(0.2, 0.35)]
    [TestCase(0.0, 0.25)]
    [TestCase(1.0, 0.75)]
    public void WideningKeepsThePlaceOnScreen(double v, double wide)
    {
        var s = new LaserSection(0, new[] { new LaserPoint(0, v), new LaserPoint(240, 0.5) });
        var (w, clamped) = LaserEditing.WithWidth(s, 2);
        Assert.That((w.Width, w.Points[0].V, clamped), Is.EqualTo((2, wide, 0)));
        var (n, back) = LaserEditing.WithWidth(w, 1);
        Assert.That((n.Width, n.Points[0].V, back), Is.EqualTo((1, v, 0)), "narrowing it again is exact");
    }

    [Test]
    public void KeepValuesOnlyChangesTheWidth()
    {
        var s = LaserPaths.ToSection(bezier(), 960, 1);
        var (w, clamped) = LaserEditing.WithWidth(s, 2, keepPosition: false);
        Assert.That((w.Width, clamped, LaserPaths.SamePoints(w.Points, s.Points, 0)), Is.EqualTo((2, 0, true)));
        Assert.That(LaserPaths.ValidPath(w)?.Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier), "the path stays");
    }

    [Test]
    public void NarrowingClampsWhatLiesBeyondTheLanes()
    {
        var s = new LaserSection(0, new[] { new LaserPoint(0, 0.05, 0.9), new LaserPoint(240, 0.1, 0.0), new LaserPoint(480, 0.5) }, 2);
        var (n, clamped) = LaserEditing.WithWidth(s, 1);
        Assert.That(n.Points.Select(p => (p.V, p.Vf)), Is.EqualTo(new[] { (0.0, (double?)1.0), (0.0, null), (0.5, null) }));
        Assert.That(clamped, Is.EqualTo(4), "0.05, 0.9, 0.1 and 0.0 lay beyond the normal range; the second slam's ends meet and it loses its jump");
    }

    [Test]
    public void WideningACurvedPathKeepsItsTypes()
    {
        var s = LaserPaths.ToSection(bezier(), 960, 1);
        var (w, _) = LaserEditing.WithWidth(s, 2);
        var path = LaserPaths.ValidPath(w)!;
        Assert.That(path.Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier));
        Assert.That(path.Anchors.Select(a => a.V), Is.EqualTo(new[] { 0.4, 0.6, 0.45, 0.55 }).Within(1e-12));
    }
}
