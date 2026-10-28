using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class LaserPathTests
{
    /// <summary>Placement as the laser tool does it: first anchor, then clicks in time order.</summary>
    private static LaserPath placed(params (long Ry, double V)[] clicks)
    {
        var p = new LaserPath(new[] { new LaserAnchor(clicks[0].Ry, clicks[0].V, type: LaserCurveType.Linear, auto: true) });
        foreach (var (ry, v) in clicks.Skip(1)) p = LaserPathEditing.AddAnchor(p, ry, v)!;
        return p;
    }

    private static Chart chartWith(int lane, LaserSection s)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Lasers[lane].Add(s);
        return c;
    }

    // ---- default typing ----------------------------------------------------

    [Test]
    public void PlacementTypesByAnchorCount()
    {
        Assert.That(placed((0, 0), (480, 1)).Anchors[0].Type, Is.EqualTo(LaserCurveType.Linear), "2 anchors = straight");
        Assert.That(placed((0, 0), (240, 1), (480, 0.5)).Anchors[0].Type, Is.EqualTo(LaserCurveType.PerfectCurve), "3 = perfect curve");
        var four = placed((0, 0), (240, 1), (480, 0.5), (720, 0.2));
        Assert.That(four.Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier), "4 = Bezier");
        Assert.That(four.Anchors.Skip(1).All(a => a.Type == null), "the rest are inherit anchors");
        Assert.That(four.Anchors[0].Auto, "still auto");
    }

    [Test]
    public void ClicksOutOfOrderAreSortedAndTheStartKeepsTheType()
    {
        var p = placed((480, 0.5), (960, 1), (0, 0));
        Assert.That(p.Anchors.Select(a => a.Ry), Is.EqualTo(new long[] { 0, 480, 960 }));
        Assert.That(p.Anchors[0].Type, Is.EqualTo(LaserCurveType.PerfectCurve));
        Assert.That(p.Anchors[1].Type, Is.Null);
    }

    [Test]
    public void SamePulseAsTheLastAnchorMakesASlam()
    {
        var p = placed((0, 0.2), (480, 0.2), (480, 0.8), (960, 0.8));
        Assert.That(p.Count, Is.EqualTo(3));
        Assert.That(p.Anchors[1].IsSlam && p.Anchors[1].Vf == 0.8);
        Assert.That(p.Anchors[1].Type, Is.Not.Null, "a slam starts a segment");
        Assert.That(LaserPathEditing.AddAnchor(p, 0, 1), Is.Null, "another existing pulse is refused");
    }

    [Test]
    public void TwoCurvesInARowMeetAtABezierAnchor()
    {
        var p = placed((0, 0), (240, 1), (480, 0.5), (720, 0), (960, 0.5));
        // click anchor 2 once: it becomes an auto segment start between a 3-anchor curve and a 3-anchor curve
        p = LaserPathEditing.CycleType(p, 2);
        var segs = p.Segments();
        Assert.That(segs.Count, Is.EqualTo(2));
        Assert.That(segs[0].Type, Is.EqualTo(LaserCurveType.PerfectCurve));
        Assert.That(p.Anchors[2].Auto && p.Anchors[2].Type == LaserCurveType.Bezier, "joint of two curves = Bezier");

        // after a straight segment the same anchor would be a perfect curve
        var q = placed((0, 0), (480, 0.5), (720, 0), (960, 0.5));
        q = LaserPathEditing.CycleType(q, 1);
        Assert.That(q.Segments()[0].Effective, Is.EqualTo(LaserCurveType.Linear));
        Assert.That(q.Anchors[1].Type, Is.EqualTo(LaserCurveType.PerfectCurve));
    }

    [Test]
    public void CycleOrder()
    {
        var p = placed((0, 0), (240, 1), (480, 0.5), (720, 0.2));
        var seen = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            p = LaserPathEditing.CycleType(p, 1);
            seen.Add($"{p.Anchors[1].Type?.ToString() ?? "inherit"}{(p.Anchors[1].Auto ? "*" : "")}");
        }
        Assert.That(seen, Is.EqualTo(new[] { "PerfectCurve*", "Linear", "PerfectCurve", "Bezier", "inherit" }));

        var first = p;
        for (int i = 0; i < 4; i++) first = LaserPathEditing.CycleType(first, 0);
        Assert.That(first.Anchors[0].Type, Is.Not.Null, "the first anchor never becomes inherit");
    }

    [Test]
    public void ExplicitTypeAndInsertKeepPathTypesValid()
    {
        var p = LaserPathEditing.SetType(placed((0, 0), (240, 1), (480, 0.5)), 0, LaserCurveType.PerfectCurve)!;
        Assert.That(p.Anchors[0].Auto, Is.False);
        var (q, i) = LaserPathEditing.InsertAnchor(p, 360, 0.7)!.Value;
        Assert.That(i, Is.EqualTo(2));
        Assert.That(q.Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier), "a perfect curve with 4 anchors becomes Bezier");
        Assert.That(LaserPathEditing.SetType(q, 0, null), Is.Null, "the first anchor cannot inherit");
        Assert.That(LaserPathEditing.InsertAnchor(q, 240, 0.1), Is.Null, "occupied pulse");
        Assert.That(LaserPathEditing.InsertAnchor(q, 600, 0.1), Is.Null, "outside");
    }

    [Test]
    public void DeleteAnchorPassesTheTypeOn()
    {
        var p = LaserPathEditing.SetType(placed((0, 0), (240, 1), (480, 0.5), (720, 0)), 0, LaserCurveType.Bezier)!;
        var d = LaserPathEditing.DeleteAnchor(p, 0)!;
        Assert.That(d.Anchors[0].Ry, Is.EqualTo(240));
        Assert.That(d.Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier));
        Assert.That(LaserPathEditing.DeleteAnchor(LaserPathEditing.DeleteAnchor(d, 1)!, 0), Is.Null, "two anchors minimum");
    }

    // ---- evaluation ----------------------------------------------------------

    [Test]
    public void PerfectCurvePassesThroughItsAnchors()
    {
        var p = placed((0, 0.1), (480, 0.9), (960, 0.3));
        var pieces = LaserPathBaker.Evaluate(p);
        Assert.That(pieces.Single().Segment.Effective, Is.EqualTo(LaserCurveType.PerfectCurve));
        Assert.That(LaserPathBaker.ValueAt(p, 0), Is.EqualTo(0.1).Within(1e-9));
        Assert.That(LaserPathBaker.ValueAt(p, 480)!.Value, Is.EqualTo(0.9).Within(2e-3));
        Assert.That(LaserPathBaker.ValueAt(p, 960), Is.EqualTo(0.3).Within(1e-9));
        // it is a circle in the curve plane: every sample has the same distance from the centre
        double s = LaserPathBaker.TIME_SCALE;
        (double x, double y)[] q = { (0, 0.1), (480 / s, 0.9), (960 / s, 0.3) };
        double d = 2 * (q[0].x * (q[1].y - q[2].y) + q[1].x * (q[2].y - q[0].y) + q[2].x * (q[0].y - q[1].y));
        double sq(int i) => q[i].x * q[i].x + q[i].y * q[i].y;
        double ux = (sq(0) * (q[1].y - q[2].y) + sq(1) * (q[2].y - q[0].y) + sq(2) * (q[0].y - q[1].y)) / d;
        double uy = (sq(0) * (q[2].x - q[1].x) + sq(1) * (q[0].x - q[2].x) + sq(2) * (q[1].x - q[0].x)) / d;
        double r = Math.Sqrt(ux * ux + (0.1 - uy) * (0.1 - uy));
        var piece = pieces[0];
        for (int k = 0; k < piece.T.Length; k++)
            Assert.That(Math.Sqrt(Math.Pow(piece.T[k] / s - ux, 2) + Math.Pow(piece.V[k] - uy, 2)), Is.EqualTo(r).Within(1e-6));
    }

    [Test]
    public void CurvesAreClampedToBeMonotoneInTime()
    {
        // an arc from (0, 0) up to (10, 1) and on to (480, 1) bulges backwards in time before pulse 0
        var p = LaserPathEditing.SetType(placed((0, 0), (10, 1), (480, 1)), 0, LaserCurveType.PerfectCurve)!;
        var piece = LaserPathBaker.Evaluate(p).Single();
        for (int k = 1; k < piece.T.Length; k++) Assert.That(piece.T[k], Is.GreaterThan(piece.T[k - 1]));
        Assert.That(piece.T[0], Is.EqualTo(0));
        Assert.That(piece.T[^1], Is.EqualTo(480));
        Assert.That(piece.V.All(v => v >= 0 && v <= 1), "values clamped to 0..1");
        // the baked section is a function of time too
        var s = LaserPaths.ToSection(p, 960, 1);
        for (int k = 1; k < s.Points.Count; k++) Assert.That(s.Points[k].Ry, Is.GreaterThan(s.Points[k - 1].Ry));
    }

    [Test]
    public void SlamIsAHardBreak()
    {
        var q = placed((0, 0.2), (480, 0.2), (480, 0.8), (720, 1), (960, 0.6));
        Assert.That(q.Count, Is.EqualTo(4), "the slam is one anchor");
        Assert.That(LaserPathBaker.ValueAt(q, 479.9)!.Value, Is.EqualTo(0.2).Within(1e-3));
        Assert.That(LaserPathBaker.ValueAt(q, 480), Is.EqualTo(0.8).Within(1e-9), "value after the slam");
        var s = LaserPaths.ToSection(q, 0, 1);
        Assert.That(s.Points.Single(pt => pt.Ry == 480).Vf, Is.EqualTo(0.8));
        Assert.That(s.SlamCount, Is.EqualTo(1));
    }

    // ---- baking --------------------------------------------------------------

    [Test]
    public void QuadraticBezierBakesToOneKsonCurve()
    {
        var p = LaserPathEditing.SetType(placed((0, 0), (120, 0.9), (480, 1)), 0, LaserCurveType.Bezier)!;
        var s = LaserPaths.ToSection(p, 960, 1);
        Assert.That(s.Points.Count, Is.EqualTo(2));
        Assert.That(s.Points[0].Curve, Is.EqualTo(new Curve(0.25, 0.9)));
        Assert.That(LaserPathBaker.MaxError(p, 960, s.Points, 960), Is.LessThan(1e-3), "the kson curve is that Bezier");
    }

    [TestCase(LaserCurveType.PerfectCurve, 3)]
    [TestCase(LaserCurveType.Bezier, 4)]
    [TestCase(LaserCurveType.Bezier, 6)]
    public void BakeStaysWithinTheErrorBudget(LaserCurveType type, int anchors)
    {
        var rnd = new Random(anchors);
        var clicks = Enumerable.Range(0, anchors).Select(i => ((long)(i * 480 + rnd.Next(-60, 60) * (i > 0 && i < anchors - 1 ? 1 : 0)), Math.Round(rnd.NextDouble(), 2))).ToArray();
        var p = LaserPathEditing.SetType(placed(clicks), 0, type)!;
        long y = 1925; // not on the grid
        var s = LaserPaths.ToSection(p, y, 1);
        Assert.That(LaserPathBaker.MaxError(p, y, s.Points, y), Is.LessThanOrEqualTo(LaserBakeOptions.TOLERANCE + 1e-6), "kson bake");
        Assert.That(s.Points.Count, Is.LessThan((p.Anchors[^1].Ry / 15) + 2), "far fewer points than grid steps");
        foreach (var pt in s.Points.Where(pt => p.Anchors.All(a => a.Ry != pt.Ry)))
            Assert.That((y + pt.Ry) % 5, Is.EqualTo(0), "samples on the absolute VOX tick grid");

        var ksh = LaserPathBaker.Bake(p, y, LaserBakeOptions.Ksh);
        Assert.That(ksh.All(pt => pt.Curve == null), "no curves for ksh");
        for (int k = 1; k < ksh.Count; k++) Assert.That(ksh[k].Ry - ksh[k - 1].Ry, Is.GreaterThan(Timebase.SlamMaxPulses), "never closer than 1/32 (would read as a slam)");
        Assert.That(LaserPathBaker.MaxError(p, y, ksh, y), Is.LessThanOrEqualTo(LaserBakeOptions.TOLERANCE + 1e-6), "ksh bake");
    }

    [Test]
    public void OldChartsAreStraightAnchorsAndBakeToThemselves()
    {
        var pts = new[] { new LaserPoint(0, 0, null, new Curve(0.2, 0.8)), new LaserPoint(240, 1, 0.5), new LaserPoint(480, 0.5) };
        var s = new LaserSection(960, pts);
        var anchors = LaserPaths.AnchorsOf(s);
        Assert.That(anchors.Anchors.All(a => a.Type == LaserCurveType.Linear));
        Assert.That(anchors.Anchors[0].Ease, Is.EqualTo(new Curve(0.2, 0.8)));
        var again = LaserPaths.ToSection(anchors, s.Y, 1);
        Assert.That(again.Path, Is.Null, "nothing to store");
        Assert.That(LaserPaths.SamePoints(again.Points, s.Points, 0), "identical points, curve kept");
    }

    // ---- storage -------------------------------------------------------------

    [Test]
    public void KsonRoundTripKeepsAnchorsAndTypes()
    {
        var p = LaserPathEditing.CycleType(placed((0, 0), (240, 1), (480, 0.5), (720, 0), (960, 0.5)), 2);
        var s = LaserPaths.ToSection(p, 1920, 2);
        var c = chartWith(1, s);
        string json = KsonWriter.Write(c);
        Assert.That(json, Does.Contain("laser_paths"));

        var back = KsonReader.Read(json);
        var r = back.Notes.Lasers[1].Single();
        var path = LaserPaths.ValidPath(r);
        Assert.That(path, Is.Not.Null);
        Assert.That(back.StaleLaserPaths, Is.EqualTo(0));
        Assert.That(path!.Anchors.Select(a => (a.Ry, a.V, a.Type, a.Auto)), Is.EqualTo(p.Anchors.Select(a => (a.Ry, a.V, a.Type, a.Auto))));
        Assert.That(LaserPaths.SamePoints(r.Points, s.Points, 0), "points unchanged");
        Assert.That(KsonWriter.Write(back), Is.EqualTo(json), "stable");
    }

    [Test]
    public void APathWhosePointsWereEditedElsewhereIsDropped()
    {
        var s = LaserPaths.ToSection(placed((0, 0), (240, 1), (480, 0.5)), 0, 1);
        var c = chartWith(0, s);
        var node = KsonWriter.ToJson(c);
        // another tool moved the end point of the section
        var pts = node["note"]!["laser"]![0]![0]![1]!.AsArray();
        pts[^1]![1] = 0.25;
        var back = KsonReader.Read(node.ToJsonString());
        Assert.That(back.StaleLaserPaths, Is.EqualTo(1));
        var r = back.Notes.Lasers[0].Single();
        Assert.That(LaserPaths.ValidPath(r), Is.Null);
        Assert.That(LaserPaths.AnchorsOf(r).Count, Is.EqualTo(r.Points.Count), "falls back to straight anchors on the points");
    }

    [Test]
    public void KshExportKeepsTheBakedShape()
    {
        var p = LaserPathEditing.SetType(placed((0, 0.1), (480, 0.9), (960, 0.2), (1440, 0.8)), 0, LaserCurveType.Bezier)!;
        var s = LaserPaths.ToSection(p, 3840, 1);
        var c = chartWith(0, s);
        var back = KshReader.Read(KshWriter.Write(c)).Notes.Lasers[0].Single();
        Assert.That(back.Y, Is.EqualTo(3840));
        Assert.That(back.SlamCount, Is.EqualTo(0), "no sample read back as a slam");
        double err = LaserPathBaker.MaxError(p, 3840, back.Points, back.Y);
        Assert.That(err, Is.LessThanOrEqualTo(LaserBakeOptions.TOLERANCE + 0.5 / LaserBakeOptionsSteps + 1e-6), "budget + ksh quantisation");
    }

    private const double LaserBakeOptionsSteps = 50;

    [TestCase(60)]
    [TestCase(120)]
    [TestCase(240)]
    public void KshExportOfAShortCurve(long u)
    {
        // the smoke test's final laser: perfect curve over 4 units, then a straight segment
        var p = LaserPathEditing.CycleType(placed((0, 0.2), (2 * u, 0.8), (4 * u, 0.3), (6 * u, 0.7)), 2);
        p = LaserPathEditing.SetType(p, 2, LaserCurveType.Linear)!;
        p = LaserPathEditing.MoveAnchor(p, 3, 7 * u, 0.5)!;
        var s = LaserPaths.ToSection(p, 9600, 1);
        var c = chartWith(0, s);
        var back = KshReader.Read(KshWriter.Write(c)).Notes.Lasers[0].Single();
        var ksh = LaserPathBaker.Bake(LaserPaths.ValidPath(s)!, s.Y, LaserBakeOptions.Ksh);
        double bakeErr = LaserPathBaker.MaxError(p, s.Y, ksh, s.Y);
        double err = LaserPathBaker.MaxError(p, s.Y, back.Points, back.Y);
        TestContext.Out.WriteLine($"u {u}: ksh bake {bakeErr:0.0000}, read back {err:0.0000}; bake {string.Join(" ", ksh.Select(x => $"{x.Ry}:{x.V:0.000}"))}; back {string.Join(" ", back.Points.Select(x => $"{x.Ry}:{x.V:0.000}{(x.IsSlam ? ">" + x.Vf : "")}"))}");
        Assert.That(err, Is.LessThanOrEqualTo(Math.Max(bakeErr, LaserBakeOptions.TOLERANCE) + 0.5 / LaserBakeOptionsSteps + 1e-6));
        // a steep, short arc bulges back in time: the monotone cut makes the path rise almost vertically at its start,
        // which no grid can follow — the read-back equals the bake, the bake error is grid-limited (reported above)
    }

    [Test]
    public void ToSectionRebasesWhenTheFirstAnchorMoves()
    {
        var p = placed((0, 0), (240, 1), (480, 0.5));
        var s = LaserPaths.ToSection(p, 960, 1);
        var moved = LaserPathCommands.Move(chartWith(0, s), 0, s, 0, 1200, 0.3)!;
        var n = moved.Result.Single().Section;
        Assert.That(n.Y, Is.EqualTo(1199), "clamped to one pulse before the next anchor");
        Assert.That(n.Points[0].Ry, Is.EqualTo(0));
        Assert.That(LaserPaths.ValidPath(n)!.Anchors[0].Ry, Is.EqualTo(0));
    }

    // ---- commands --------------------------------------------------------------

    [Test]
    public void CommandsUndoExactly()
    {
        var s = LaserPaths.ToSection(placed((0, 0), (240, 1), (480, 0.5), (720, 0.2)), 960, 1);
        var c = chartWith(0, s);
        var stack = new CommandStack(c);
        string before = KsonWriter.Write(c);

        stack.Execute(LaserPathCommands.SetType(c, 0, s, 2, LaserCurveType.Linear)!);
        var s2 = c.Notes.Lasers[0].Single();
        Assert.That(LaserPaths.ValidPath(s2)!.Segments().Count, Is.EqualTo(2));
        stack.Execute(LaserPathCommands.Insert(c, 0, s2, 960 + 600, 0.9)!);
        var s3 = c.Notes.Lasers[0].Single();
        stack.Execute(LaserPathCommands.Move(c, 0, s3, 1, 960 + 200, 0.4)!);
        var s4 = c.Notes.Lasers[0].Single();
        stack.Execute(LaserPathCommands.Delete(c, 0, s4, 3)!);
        stack.Execute(LaserPathCommands.SetSegmentType(c, 0, c.Notes.Lasers[0].Single(), 1, LaserCurveType.PerfectCurve)!);
        Assert.That(KsonWriter.Write(c), Is.Not.EqualTo(before));

        while (stack.Undo()) { }
        Assert.That(KsonWriter.Write(c), Is.EqualTo(before));
        Assert.That(c.Notes.Lasers[0].Single(), Is.SameAs(s));
    }

    [Test]
    public void AnAllStraightPathIsNotStored()
    {
        // the smoke test's sequence: 4 anchors, click anchor 3 while placing, finish, menu "Straight" on anchor 2
        var p = LaserPathEditing.CycleType(placed((0, 0.2), (480, 0.8), (960, 0.3), (1440, 0.7)), 2);
        var s = LaserPaths.ToSection(p, 4800, 1);
        Assert.That(LaserPaths.ValidPath(s), Is.Not.Null, "after placement");
        var c = chartWith(0, s);
        var cmd = LaserPathCommands.SetType(c, 0, s, 1, LaserCurveType.Linear)!;
        cmd.Execute(c);
        var n = c.Notes.Lasers[0].Single();
        // all straight now: nothing to store, the points are the anchors
        Assert.That(n.Path, Is.Null);
        var anchors = LaserPaths.AnchorsOf(n);
        Assert.That(anchors.Count, Is.EqualTo(4));
        Assert.That(anchors.Anchors.All(a => a.Type == LaserCurveType.Linear));
    }

    [Test]
    public void MirrorKeepsTheCurve()
    {
        var s = LaserPaths.ToSection(placed((0, 0.1), (240, 0.9), (480, 0.3)), 960, 1);
        var c = chartWith(0, s);
        var sel = new Selection();
        foreach (var pt in s.Points) sel.Add(new LaserPointRef(0, s, pt));
        var cmd = SelectionOps.Mirror(c, sel)!;
        cmd.Execute(c);
        var m = c.Notes.Lasers[1].Single();
        var path = LaserPaths.ValidPath(m)!;
        Assert.That(path.Anchors[0].Type, Is.EqualTo(LaserCurveType.PerfectCurve));
        Assert.That(path.Anchors.Select(a => a.V), Is.EqualTo(new[] { 0.9, 0.1, 0.7 }).Within(1e-9));
    }

    [Test]
    public void DirectPointEditsMakeThePathStale()
    {
        var s = LaserPaths.ToSection(placed((0, 0.1), (240, 0.9), (480, 0.3)), 960, 1);
        Assert.That(LaserPaths.HasCurvedPath(s));
        var edited = LaserEditing.MovePoint(s, 1, 960 + 50, 0.5)!;
        Assert.That(LaserPaths.ValidPath(edited), Is.Null);
        Assert.That(LaserPaths.HasCurvedPath(s.Clone()), "a clone stays valid");
    }
}
