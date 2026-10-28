using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Tests;

/// <summary>Anchor paths on camera curves (<see cref="CameraPaths"/>, <see cref="CameraPathEditing"/>).</summary>
[TestFixture]
public class CameraPathTests
{
    private static Chart chart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        return c;
    }

    /// <summary>Clicks as the camera lane tool does them: the first one fresh, the rest extending the placement chain.</summary>
    private static void place(Chart c, CamPathLane lane, params (long Y, double V)[] clicks)
    {
        CameraChain? chain = null;
        foreach (var (y, v) in clicks)
        {
            var r = CameraPathCommands.Add(c, lane, y, v, null, chain) ?? throw new AssertionException($"click at {y} refused");
            new CommandStack(c).Execute(r.Command);
            chain = r.Chain;
        }
    }

    private static CamRun run(Chart c, CamPathLane lane, int i = 0) => CameraPaths.Runs(c.Camera, lane)[i];

    private static string types(LaserPath p) => string.Join(" ", p.Anchors.Select(a => (a.Type?.ToString() ?? "-") + (a.Auto ? "*" : "")));

    // ---- typing ----------------------------------------------------------------

    [Test]
    public void ChainTypesByAnchorCountLikeLasers()
    {
        var c = chart();
        place(c, CamPathLane.ZoomBottom, (960, 0), (1440, 100));
        Assert.That(run(c, CamPathLane.ZoomBottom).Curved, Is.False, "2 clicks = straight (not stored)");
        Assert.That(c.Camera.Body.ZoomBottom.Select(p => (p.Y, p.V)), Is.EqualTo(new[] { (960L, 0.0), (1440L, 100.0) }));

        c = chart();
        place(c, CamPathLane.ZoomBottom, (960, 0), (1440, 100), (1920, -50));
        var r = run(c, CamPathLane.ZoomBottom);
        Assert.That(r.Curved && r.Stored!.Path.Anchors[0].Type == LaserCurveType.PerfectCurve, $"3 clicks = perfect curve ({types(r.Anchors())})");

        c = chart();
        place(c, CamPathLane.ZoomBottom, (960, 0), (1440, 100), (1920, -50), (2400, 20));
        r = run(c, CamPathLane.ZoomBottom);
        Assert.That(r.Anchors().Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier), $"4 clicks = Bezier ({types(r.Anchors())})");
        Assert.That(r.Anchors().Anchors.Skip(1).Take(2).All(a => a.Type == null), "inner chain anchors inherit");
    }

    [Test]
    public void AChainInTheMiddleEndsAtItsLastClick()
    {
        var c = chart();
        c.Camera.Body.ZoomTop.Add(new GraphPoint(0, 0));
        c.Camera.Body.ZoomTop.Add(new GraphPoint(9600, 50));
        place(c, CamPathLane.ZoomTop, (960, 10), (1440, 80), (1920, -40));
        var a = run(c, CamPathLane.ZoomTop).Anchors();
        Assert.That(a.Anchors.Select(x => x.Ry), Is.EqualTo(new long[] { 0, 960, 1440, 1920, 9600 }));
        var segs = a.Segments();
        Assert.That(segs.Select(s => (s.Start, s.End, s.Effective)), Is.EqualTo(new[]
        {
            (0, 1, LaserCurveType.Linear), (1, 3, LaserCurveType.PerfectCurve), (3, 4, LaserCurveType.Linear),
        }), types(a));
    }

    [Test]
    public void TwoCurvesInARowMeetAtABezierAnchor()
    {
        var c = chart();
        place(c, CamPathLane.RotationDeg, (0, 0), (480, 30), (960, 0), (1440, -30), (1920, 10));
        // clicking the middle chain anchor (inherit) makes it an auto segment start: 3 + 3 anchors
        var a = LaserPathEditing.CycleType(run(c, CamPathLane.RotationDeg).Anchors(), 2);
        Assert.That(a.Segments().Select(s => s.Effective), Is.EqualTo(new[] { LaserCurveType.PerfectCurve, LaserCurveType.Bezier }), types(a));
    }

    // ---- evaluation, monotone, bake ---------------------------------------------

    [Test]
    public void EachTypeEvaluates()
    {
        var range = CameraPaths.Range(CamPathLane.ZoomBottom);
        LaserPath path(LaserCurveType t, params (long, double)[] pts)
        {
            var p = new LaserPath(pts.Select((x, i) => new LaserAnchor(x.Item1, x.Item2, type: i == 0 ? t : null))) { Range = range };
            p.Normalize();
            return p;
        }

        var straight = path(LaserCurveType.Linear, (0, 0), (480, 100), (960, -100));
        Assert.That(LaserPathBaker.ValueAt(straight, 240), Is.EqualTo(50).Within(1e-9), "straight = polyline");
        Assert.That(LaserPathBaker.ValueAt(straight, 720), Is.EqualTo(0).Within(1e-9));

        var arc = path(LaserCurveType.PerfectCurve, (0, 0), (480, 150), (960, 0));
        Assert.That(LaserPathBaker.ValueAt(arc, 480)!.Value, Is.EqualTo(150).Within(0.5), "a perfect curve passes through its middle anchor");
        Assert.That(LaserPathBaker.ValueAt(arc, 240)!.Value, Is.GreaterThan(75 + 10), "and bulges above the chord");

        var bez = path(LaserCurveType.Bezier, (0, 0), (320, 200), (640, -200), (960, 0));
        Assert.That(LaserPathBaker.ValueAt(bez, 0), Is.EqualTo(0).Within(1e-9));
        Assert.That(LaserPathBaker.ValueAt(bez, 960), Is.EqualTo(0).Within(1e-9));
        Assert.That(LaserPathBaker.ValueAt(bez, 320)!.Value, Is.LessThan(200), "inner Bezier anchors are control points, not on the curve");

        // values beyond the view range are not clamped (kson allows any camera value)
        var big = path(LaserCurveType.PerfectCurve, (0, 250), (480, 299), (960, 250));
        Assert.That(LaserPathBaker.Evaluate(big).SelectMany(p => p.V).Max(), Is.GreaterThan(299));
    }

    [Test]
    public void CurvesAreClampedToBeMonotoneInTime()
    {
        // a nearly closed arc: the circle runs backwards in time between the anchors
        var p = new LaserPath(new[]
        {
            new LaserAnchor(0, -150, type: LaserCurveType.PerfectCurve),
            new LaserAnchor(40, 200),
            new LaserAnchor(80, -140),
        }) { Range = CameraPaths.Range(CamPathLane.ZoomTop) };
        p.Normalize();
        foreach (var piece in LaserPathBaker.Evaluate(p))
            for (int k = 1; k < piece.T.Length; k++)
                Assert.That(piece.T[k], Is.GreaterThan(piece.T[k - 1]), "time strictly increases");

        var baked = LaserPathBaker.Bake(p, 0, CameraPaths.KsonOptions(CamPathLane.ZoomTop));
        for (int k = 1; k < baked.Count; k++) Assert.That(baked[k].Ry, Is.GreaterThan(baked[k - 1].Ry));
        Assert.That(baked[0].Ry == 0 && baked[^1].Ry == 80);
    }

    [Test]
    public void AJumpIsAHardBreak()
    {
        var c = chart();
        place(c, CamPathLane.ZoomSide, (0, 0), (480, 100), (960, 0));
        var r = run(c, CamPathLane.ZoomSide);
        var jumped = CameraPathEditing.SetJump(r.Anchors(), 1, -100)!;
        Assert.That(jumped.Anchors[1].Type, Is.Not.Null, "a jump anchor starts a segment");
        new CommandStack(c).Execute(CameraPaths.Replace(c.Camera, r, jumped, "jump")!);
        var g = c.Camera.Body.ZoomSide;
        var j = g.Single(p => p.Y == 480);
        Assert.That(j.V == 100 && j.Vf == -100, "the bake keeps the jump on its pulse");
        Assert.That(Graph.ValueAt(g, 480, 0), Is.EqualTo(-100));
        Assert.That(Graph.ValueAt(g, 479.9, 0), Is.EqualTo(100).Within(0.5));
    }

    [Test]
    public void QuadraticBezierIsOneExactKsonCurve()
    {
        var c = chart();
        place(c, CamPathLane.ZoomBottom, (0, 0), (300, 200), (960, 100));
        var r = run(c, CamPathLane.ZoomBottom);
        var path = CameraPathEditing.Add(r.Anchors(), 2000, 0)!.Value.Path; // keep a straight tail behind it
        path = LaserPathEditing.SetType(path, 0, LaserCurveType.Bezier)!;
        new CommandStack(c).Execute(CameraPaths.Replace(c.Camera, r, path, "bezier")!);
        var g = c.Camera.Body.ZoomBottom;
        Assert.That(g.Select(p => p.Y), Is.EqualTo(new long[] { 0, 960, 2000 }), "the middle anchor is the kson control point");
        Assert.That(g[0].Curve, Is.EqualTo(new Curve(300.0 / 960, 2.0)));
        var stored = run(c, CamPathLane.ZoomBottom).Stored!.Path;
        // exact: the remaining difference is only the polyline the evaluator draws the Bezier with (64 samples)
        Assert.That(LaserPathBaker.MaxError(stored, 0, run(c, CamPathLane.ZoomBottom).Points, 0), Is.LessThan(0.05));
        for (int k = 1; k < 10; k++)
        {
            double u = k / 10.0, t = 2 * (1 - u) * u * 300 + u * u * 960, v = 2 * (1 - u) * u * 200 + u * u * 100;
            Assert.That(Graph.ValueAt(g, t, 0), Is.EqualTo(v).Within(1e-9), $"the kson curve is the quadratic Bezier (u {u})");
        }
    }

    [TestCase(CamPathLane.ZoomTop, LaserCurveType.PerfectCurve, 3)]
    [TestCase(CamPathLane.ZoomBottom, LaserCurveType.Bezier, 4)]
    [TestCase(CamPathLane.ZoomSide, LaserCurveType.Bezier, 6)]
    [TestCase(CamPathLane.CenterSplit, LaserCurveType.PerfectCurve, 3)]
    [TestCase(CamPathLane.RotationDeg, LaserCurveType.Bezier, 5)]
    [TestCase(CamPathLane.Tilt, LaserCurveType.PerfectCurve, 3)]
    [TestCase(CamPathLane.Tilt, LaserCurveType.Bezier, 4)]
    public void BakesStayWithinTheLaneBound(CamPathLane lane, LaserCurveType type, int anchors)
    {
        var range = CameraPaths.Range(lane);
        var rnd = new Random(7 + anchors + (int)lane);
        var p = new LaserPath(Enumerable.Range(0, anchors).Select(i =>
            new LaserAnchor(i * 480L, range.Min + range.Span * (0.2 + 0.6 * rnd.NextDouble()), type: i == 0 ? type : null))) { Range = range };
        p.Normalize();
        long y = 3 * 960 + 35;
        double tol = CameraPaths.Tolerance(lane);
        var kson = LaserPathBaker.Bake(p, y, CameraPaths.KsonOptions(lane));
        var ksh = LaserPathBaker.Bake(p, y, CameraPaths.KshOptions(lane));
        double ek = CameraPaths.MaxError(p, y, kson), eh = CameraPaths.MaxError(p, y, ksh);
        TestContext.Out.WriteLine($"{lane} {type} x{anchors}: kson {kson.Count} points, error {ek:0.0000}; ksh {ksh.Count} points, error {eh:0.0000} (bound {tol})");
        Assert.That(ek, Is.LessThanOrEqualTo(tol + 1e-9), $"kson bake error {ek} (bound {tol}, {kson.Count} points)");
        Assert.That(eh, Is.LessThanOrEqualTo(tol + 1e-9), $"ksh bake error {eh} (bound {tol}, {ksh.Count} points)");
        Assert.That(kson.Where(pt => pt.Ry > 0 && pt.Ry < p.Anchors[^1].Ry).All(pt => (y + pt.Ry) % 5 == 0), "kson samples on the 5-pulse grid");
        Assert.That(ksh.All(pt => pt.Curve == null), "no curves in the ksh bake");
        Assert.That(kson.Count, Is.LessThan(anchors * 120), "the bake is thinned, not every tick");
    }

    [Test]
    public void MenuPointsRefuseToSplitACurve()
    {
        var c = chart();
        place(c, CamPathLane.ZoomTop, (0, 0), (480, 100), (960, 0));
        Assert.That(CameraPathCommands.InsideCurve(c, CamPathLane.ZoomTop, 240), Is.True);
        Assert.That(CameraPathCommands.InsideCurve(c, CamPathLane.ZoomTop, 960), Is.False, "on the end anchor");
        Assert.That(CameraPathCommands.InsideCurve(c, CamPathLane.ZoomTop, 2000), Is.False, "after the curve");
        Assert.That(CameraPathCommands.InsideCurve(c, CamPathLane.ZoomBottom, 240), Is.False, "another lane");
    }

    // ---- old charts --------------------------------------------------------------

    [Test]
    public void OldChartsAreStraightAnchorsAndRebakeToThemselves()
    {
        var c = chart();
        var g = c.Camera.Body.ZoomTop;
        g.Add(new GraphPoint(0, 0, null, new Curve(0.2, 0.8)));
        g.Add(new GraphPoint(960, 100, -50));
        g.Add(new GraphPoint(1920, 30));
        var before = KsonWriter.Write(c);
        var r = run(c, CamPathLane.ZoomTop);
        Assert.That(r.Curved, Is.False);
        var a = r.Anchors();
        Assert.That(a.Anchors.All(x => x.Type == LaserCurveType.Linear) && a.Anchors[0].Ease == new Curve(0.2, 0.8) && a.Anchors[1].Vf == -50);
        var cmd = CameraPaths.Replace(c.Camera, r, a, "noop")!;
        new CommandStack(c).Execute(cmd);
        Assert.That(KsonWriter.Write(c), Is.EqualTo(before), "an unchanged old graph rebakes to exactly its points, nothing stored");
        Assert.That(c.Camera.Paths, Is.Empty);
    }

    [Test]
    public void TheEaseHandleShapesOnlyStraightPairs()
    {
        var c = chart();
        place(c, CamPathLane.ZoomBottom, (0, 0), (960, 100));
        var a = run(c, CamPathLane.ZoomBottom).Anchors();
        var eased = CameraPathEditing.SetEase(a, 0, new Curve(0.1, 0.9))!;
        Assert.That(eased.Anchors[0].Ease, Is.EqualTo(new Curve(0.1, 0.9)));
        new CommandStack(c).Execute(CameraPaths.Replace(c.Camera, run(c, CamPathLane.ZoomBottom), eased, "ease")!);
        Assert.That(c.Camera.Body.ZoomBottom[0].Curve, Is.EqualTo(new Curve(0.1, 0.9)), "kept as the kson curve of the point");

        var c2 = chart();
        place(c2, CamPathLane.ZoomBottom, (0, 0), (480, 100), (960, 0));
        Assert.That(CameraPathEditing.SetEase(run(c2, CamPathLane.ZoomBottom).Anchors(), 0, new Curve(0.1, 0.9)), Is.Null, "a perfect curve is shaped by its anchors");
    }

    // ---- tilt runs ----------------------------------------------------------------

    [Test]
    public void TiltRunsSplitAtAutoModesAndKeepThem()
    {
        var c = chart();
        var t = c.Camera.Tilt;
        t.Add(new TiltPoint(0, TiltValue.Of(TiltMode.Bigger)));
        t.Add(new TiltPoint(480, TiltValue.Of(TiltMode.Normal), TiltValue.Of(0.5)));   // auto → manual
        t.Add(new TiltPoint(960, TiltValue.Of(1.0)));
        t.Add(new TiltPoint(1440, TiltValue.Of(-0.5), TiltValue.Of(TiltMode.Zero))); // manual → auto
        t.Add(new TiltPoint(1920, TiltValue.Of(0.2)));
        t.Add(new TiltPoint(2400, TiltValue.Of(0.4)));
        var runs = CameraPaths.Runs(c.Camera, CamPathLane.Tilt);
        Assert.That(runs.Select(r => (r.Y, r.Count)), Is.EqualTo(new[] { (480L, 3), (1920L, 2) }));
        Assert.That(runs[0].HeadAuto == TiltMode.Normal && runs[0].TailAuto == TiltMode.Zero);
        Assert.That(runs[0].Points.Select(p => p.V), Is.EqualTo(new[] { 0.5, 1.0, -0.5 }));

        // a curve through the first run keeps both mode switches and the auto point before it
        var a = LaserPathEditing.SetType(runs[0].Anchors(), 1, null)!;
        var curved = LaserPathEditing.SetType(a, 0, LaserCurveType.PerfectCurve)!;
        new CommandStack(c).Execute(CameraPaths.Replace(c.Camera, runs[0], curved, "curve")!);
        Assert.That(t[0].Value.Auto, Is.EqualTo(TiltMode.Bigger));
        Assert.That(t[1] is { Y: 480, Value.Auto: TiltMode.Normal, Final: { IsAuto: false } f } && f.Manual == 0.5);
        var last = t.Single(p => p.Y == 1440);
        Assert.That(last.Value.Manual == -0.5 && last.Final?.Auto == TiltMode.Zero);
        Assert.That(t.Count(p => p.Y > 480 && p.Y < 1440), Is.GreaterThan(1), "the curve is sampled between the anchors");
        var r0 = CameraPaths.Runs(c.Camera, CamPathLane.Tilt)[0];
        Assert.That(r0.Curved && r0.HeadAuto == TiltMode.Normal && r0.TailAuto == TiltMode.Zero);
        var ev = new CameraEvaluator(c);
        Assert.That(ev.EvaluateTilt(960).Input, Is.EqualTo(1.0).Within(0.005), "the evaluator follows the curve through the middle anchor");

        // a run cannot be dragged over its neighbours
        var moved = LaserPathEditing.MoveAnchor(r0.Anchors(), 2, 1920 - 480 + 100, 0)!;
        Assert.That(CameraPaths.Replace(c.Camera, r0, moved, "move"), Is.Null);
    }

    [Test]
    public void NewTiltPointsJoinTheRunTheyInterpolateWith()
    {
        var c = chart();
        var t = c.Camera.Tilt;
        t.Add(new TiltPoint(0, TiltValue.Of(0.0)));
        t.Add(new TiltPoint(960, TiltValue.Of(TiltMode.Normal)));
        t.Add(new TiltPoint(1920, TiltValue.Of(0.5)));
        Assert.That(CameraPaths.TargetFor(c.Camera, CamPathLane.Tilt, 480)!.Y, Is.EqualTo(0), "after a manual value: that run");
        Assert.That(CameraPaths.TargetFor(c.Camera, CamPathLane.Tilt, 1440)!.Y, Is.EqualTo(1920), "before a manual value after an auto mode: the next run");
        Assert.That(CameraPaths.TargetFor(c.Camera, CamPathLane.Tilt, 2400)!.Y, Is.EqualTo(1920));
        Assert.That(CameraPaths.TargetFor(c.Camera, CamPathLane.Tilt, 960), Is.Null, "taken");
        t.Insert(2, new TiltPoint(1200, TiltValue.Of(TiltMode.Bigger)));
        var fresh = CameraPaths.TargetFor(c.Camera, CamPathLane.Tilt, 1100)!;
        Assert.That(fresh.IsEmpty && fresh.Start == 2, "between two auto points: a new run");
    }

    // ---- formats ------------------------------------------------------------------

    [Test]
    public void KsonRoundTripKeepsAnchorsForGraphsAndTilt()
    {
        var c = chart();
        place(c, CamPathLane.ZoomBottom, (960, 0), (1440, 120), (1920, -60), (2400, 30));
        place(c, CamPathLane.Tilt, (0, 0), (480, 1), (960, -0.5));
        var cycled = LaserPathEditing.CycleType(run(c, CamPathLane.ZoomBottom).Anchors(), 2);
        new CommandStack(c).Execute(CameraPaths.Replace(c.Camera, run(c, CamPathLane.ZoomBottom), cycled, "cycle")!);

        string text = KsonWriter.Write(c);
        Assert.That(text, Does.Contain("camera_paths"));
        var back = KsonReader.Read(text);
        Assert.That(back.StaleCameraPaths, Is.Zero);
        foreach (var lane in new[] { CamPathLane.ZoomBottom, CamPathLane.Tilt })
        {
            var want = run(c, lane).Stored!.Path;
            var got = run(back, lane).Stored?.Path;
            Assert.That(got, Is.Not.Null, $"{lane} anchors survive");
            Assert.That(got!.Anchors.Select(a => (a.Ry, a.V, a.Vf, a.Type, a.Auto)), Is.EqualTo(want.Anchors.Select(a => (a.Ry, a.V, a.Vf, a.Type, a.Auto))));
        }

        Assert.That(KsonWriter.Write(back), Is.EqualTo(text), "write → read → write is stable");
    }

    [Test]
    public void APathWhosePointsWereEditedElsewhereIsDropped()
    {
        var c = chart();
        place(c, CamPathLane.ZoomTop, (0, 0), (480, 100), (960, 0));
        var g = c.Camera.Body.ZoomTop;
        new CommandStack(c).Execute(new EditGraphPointCommand(CamGraph.ZoomTop, g[1], g[1].Y, g[1].V + 7, null, null));
        Assert.That(run(c, CamPathLane.ZoomTop).Curved, Is.False, "a direct point edit makes the stored path stale");
        Assert.That(run(c, CamPathLane.ZoomTop).Anchors().Count, Is.EqualTo(g.Count), "fallback: one straight anchor per point");

        var c2 = chart();
        place(c2, CamPathLane.ZoomTop, (0, 0), (480, 100), (960, 0));
        var node = System.Text.Json.Nodes.JsonNode.Parse(KsonWriter.Write(c2))!;
        node["camera"]!["cam"]!["body"]!["zoom_top"]![1]![1] = 42.0;
        var back = KsonReader.Read(node.ToJsonString());
        Assert.That(back.StaleCameraPaths, Is.EqualTo(1));
        Assert.That(run(back, CamPathLane.ZoomTop).Curved, Is.False);
    }

    [Test]
    public void UnknownCameraPathLanesAreKept()
    {
        var c = chart();
        place(c, CamPathLane.ZoomTop, (0, 0), (480, 100), (960, 0));
        var node = System.Text.Json.Nodes.JsonNode.Parse(KsonWriter.Write(c))!;
        node["impl"]![KsonReader.ImplKey]!["camera_paths"]!["future_lane"] = new System.Text.Json.Nodes.JsonArray(1, 2);
        var text = KsonWriter.Write(KsonReader.Read(node.ToJsonString()));
        Assert.That(text, Does.Contain("future_lane"));
        Assert.That(run(KsonReader.Read(text), CamPathLane.ZoomTop).Curved);
    }

    [Test]
    public void KshExportWritesTheBakeWithoutCurves()
    {
        var c = chart();
        for (int m = 0; m < 6; m++) c.Notes.Bt[0].Add(new Note(m * 960));
        place(c, CamPathLane.ZoomBottom, (960, 0), (1440, 150), (1920, -50));
        place(c, CamPathLane.Tilt, (960, 0), (1440, 1), (1920, 0));
        // an old-style eased straight pair elsewhere keeps its ksh curve line
        c.Camera.Body.ZoomSide.Add(new GraphPoint(960, 0, null, new Curve(0.3, 0.7)));
        c.Camera.Body.ZoomSide.Add(new GraphPoint(1920, 100));

        string ksh = KshWriter.Write(c);
        Assert.That(ksh, Does.Not.Contain("zoom_bottom_curve"));
        Assert.That(ksh, Does.Not.Contain("tilt_curve"));
        Assert.That(ksh, Does.Contain("zoom_side_curve"));

        var back = KshReader.Read(ksh);
        foreach (var lane in new[] { CamPathLane.ZoomBottom, CamPathLane.Tilt })
        {
            var r = run(c, lane);
            var got = run(back, lane);
            double err = CameraPaths.MaxError(r.Stored!.Path, r.Y, got.Points.Select(p => new LaserPoint(p.Ry + got.Y - r.Y, p.V, p.Vf, p.Curve)).ToList());
            Assert.That(err, Is.LessThanOrEqualTo(CameraPaths.Tolerance(lane) + 1e-6), $"{lane}: the ksh file reads back as the curve (error {err})");
        }
    }

    // ---- commands ------------------------------------------------------------------

    [Test]
    public void ReplaceUndoesExactly()
    {
        var c = chart();
        place(c, CamPathLane.ZoomBottom, (0, 0), (480, 100), (960, 0));
        var stack = new CommandStack(c);
        var pointsBefore = c.Camera.Body.ZoomBottom.ToList();
        var pathsBefore = c.Camera.Paths.ToList();
        string text = KsonWriter.Write(c);

        var r = run(c, CamPathLane.ZoomBottom);
        stack.Execute(CameraPaths.Replace(c.Camera, r, LaserPathEditing.MoveAnchor(r.Anchors(), 1, 600, 250), "move")!);
        stack.Execute(CameraPaths.Replace(c.Camera, run(c, CamPathLane.ZoomBottom), LaserPathEditing.SetType(run(c, CamPathLane.ZoomBottom).Anchors(), 0, LaserCurveType.Bezier), "type")!);
        Assert.That(KsonWriter.Write(c), Is.Not.EqualTo(text));
        stack.Undo();
        stack.Undo();
        Assert.That(c.Camera.Body.ZoomBottom, Is.EqualTo(pointsBefore), "the same point objects come back");
        Assert.That(c.Camera.Paths, Is.EqualTo(pathsBefore));
        Assert.That(KsonWriter.Write(c), Is.EqualTo(text));
        stack.Redo();
        stack.Redo();
        Assert.That(run(c, CamPathLane.ZoomBottom).Stored!.Path.Anchors[0].Type, Is.EqualTo(LaserCurveType.Bezier));
    }

    [Test]
    public void DeletingDownToNothingRemovesTheRun()
    {
        var c = chart();
        place(c, CamPathLane.CenterSplit, (0, 0), (480, 100), (960, 0));
        var stack = new CommandStack(c);
        for (int i = 0; i < 3; i++)
        {
            var r = run(c, CamPathLane.CenterSplit);
            stack.Execute(CameraPaths.Replace(c.Camera, r, CameraPathEditing.Delete(r.Anchors(), 0), "delete")!);
        }

        Assert.That(c.Camera.Body.CenterSplit, Is.Empty);
        Assert.That(c.Camera.Paths, Is.Empty);
    }
}
