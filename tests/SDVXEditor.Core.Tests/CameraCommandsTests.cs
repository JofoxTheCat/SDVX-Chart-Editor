using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class CameraCommandsTests
{
    private static Chart NewChart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[0].Add(new Note(3840));
        // laser L with a slam 0 -> 1 at 960 (right)
        c.Notes.Lasers[0].Add(new LaserSection(480, new[] { new LaserPoint(0, 0), new LaserPoint(480, 0, 1), new LaserPoint(960, 1) }));
        return c;
    }

    [Test]
    public void GraphPointAddEditRemoveUndoRedo()
    {
        var chart = NewChart();
        var before = ModelSnapshot.Of(chart);
        var stack = new CommandStack(chart);

        var a = new GraphPoint(0, 0);
        var b = new GraphPoint(960, 100);
        stack.Execute(new AddGraphPointCommand(CamGraph.ZoomBottom, b));
        stack.Execute(new AddGraphPointCommand(CamGraph.ZoomBottom, a));
        Assert.That(chart.Camera.Body.ZoomBottom.Select(p => p.Y), Is.EqualTo(new long[] { 0, 960 }), "kept sorted");
        Assert.Throws<InvalidOperationException>(() => stack.Execute(new AddGraphPointCommand(CamGraph.ZoomBottom, new GraphPoint(960, 5))));

        // move a past b, give it a curve and a slam
        stack.Execute(new EditGraphPointCommand(CamGraph.ZoomBottom, a, 1920, 50, -20, new Curve(0.2, 0.8)));
        Assert.That(chart.Camera.Body.ZoomBottom.Select(p => p.Y), Is.EqualTo(new long[] { 960, 1920 }));
        Assert.That(a.Vf, Is.EqualTo(-20));
        Assert.That(a.Curve, Is.EqualTo(new Curve(0.2, 0.8)));

        stack.Execute(new RemoveGraphPointCommand(CamGraph.ZoomBottom, b));
        Assert.That(chart.Camera.Body.ZoomBottom, Has.Count.EqualTo(1));

        var edited = ModelSnapshot.Of(chart);
        while (stack.Undo()) { }
        Assert.That(ModelSnapshot.FirstDifference(before, ModelSnapshot.Of(chart)), Is.Null);
        while (stack.Redo()) { }
        Assert.That(ModelSnapshot.FirstDifference(edited, ModelSnapshot.Of(chart)), Is.Null);
    }

    [Test]
    public void EvaluateGraphsCurvesAndSlams()
    {
        var chart = NewChart();
        var eval = new CameraEvaluator(chart);
        var d = eval.Evaluate(100);
        Assert.That(d.ZoomBottom, Is.Zero);
        Assert.That(d.TiltMode, Is.EqualTo(TiltMode.Normal));
        Assert.That(d.TiltScale, Is.EqualTo(1));

        chart.Camera.Body.ZoomBottom.AddRange(new[] { new GraphPoint(0, 0), new GraphPoint(960, 100, 300), new GraphPoint(1920, 0) });
        Assert.That(eval.Evaluate(480).ZoomBottom, Is.EqualTo(50).Within(1e-9));
        Assert.That(eval.Evaluate(960).ZoomBottom, Is.EqualTo(300), "value after the instant jump");
        Assert.That(eval.Evaluate(959.999).ZoomBottom, Is.EqualTo(100).Within(0.01), "value before the jump");
        Assert.That(eval.Evaluate(1440).ZoomBottom, Is.EqualTo(150).Within(1e-9));
        Assert.That(eval.Evaluate(5000).ZoomBottom, Is.EqualTo(0));

        chart.Camera.Body.ZoomBottom[0].Curve = new Curve(1, 0); // ease-in: slow start
        Assert.That(eval.Evaluate(240).ZoomBottom, Is.LessThan(25));

        chart.Camera.Body.RotationDeg.Add(new GraphPoint(0, 15));
        Assert.That(eval.Evaluate(3000).RollDeg, Is.EqualTo(15).Within(1e-9), "no lasers there: roll = rotation only");
    }

    [Test]
    public void EvaluateTiltManualAutoAndZero()
    {
        var chart = NewChart();
        var eval = new CameraEvaluator(chart);

        // auto: left laser at 1 (after the slam at 960) → input 1
        Assert.That(eval.Evaluate(1200).TiltInput, Is.EqualTo(1).Within(1e-9));
        Assert.That(eval.Evaluate(600).TiltInput, Is.EqualTo(0).Within(1e-9));
        Assert.That(eval.Evaluate(1200).TiltDeg, Is.EqualTo(CameraEvaluator.TiltDegreesPerUnit).Within(1e-9));

        var stack = new CommandStack(chart);
        stack.Execute(new AddTiltPointCommand(new TiltPoint(0, TiltValue.Of(TiltMode.Bigger))));
        Assert.That(eval.Evaluate(1200).TiltScale, Is.EqualTo(1.75));
        stack.Execute(new AddTiltPointCommand(new TiltPoint(1100, TiltValue.Of(TiltMode.Zero))));
        Assert.That(eval.Evaluate(1200).TiltDeg, Is.Zero);

        // manual 0 → 1 over 960 pulses, then auto again
        var m = new TiltPoint(2000, TiltValue.Of(0.0));
        stack.Execute(new AddTiltPointCommand(m));
        stack.Execute(new AddTiltPointCommand(new TiltPoint(2960, TiltValue.Of(1.0))));
        stack.Execute(new AddTiltPointCommand(new TiltPoint(4000, TiltValue.Of(TiltMode.Normal))));
        var s = eval.Evaluate(2480);
        Assert.That(s.TiltManual, Is.True);
        Assert.That(s.TiltInput, Is.EqualTo(0.5).Within(1e-9));
        Assert.That(eval.Evaluate(3500).TiltInput, Is.EqualTo(1), "last manual value holds until the auto point");
        Assert.That(eval.Evaluate(4100).TiltManual, Is.False);

        stack.Execute(new EditTiltPointCommand(m, 2000, TiltValue.Of(0.0), TiltValue.Of(-1.0), new Curve(0, 1)));
        Assert.That(eval.Evaluate(2000).TiltInput, Is.EqualTo(-1), "instant change at the point");
        stack.Undo();
        Assert.That(m.Final, Is.Null);
    }

    [Test]
    public void PatternsSnapToSlamsAndRoll()
    {
        var chart = NewChart();
        var slam = CameraCommands.SlamNear(chart, 970, 30);
        Assert.That(slam, Is.EqualTo(((long)960, 1)));
        Assert.That(CameraCommands.SlamNear(chart, 2000, 30), Is.Null);

        var stack = new CommandStack(chart);
        var spin = new CamPattern(960, 1, 480);
        stack.Execute(new AddPatternCommand(CamPatternKind.Spin, spin));
        Assert.That(CameraCommands.HasMatchingSlam(chart, spin), Is.True);
        var eval = new CameraEvaluator(chart);
        Assert.That(eval.Evaluate(960 + 240).PatternRollDeg, Is.EqualTo(270).Within(1e-9), "eased spin is 3/4 through at half time");
        Assert.That(eval.Evaluate(2000).PatternRollDeg, Is.Zero);

        stack.Execute(new EditPatternCommand(CamPatternKind.Spin, spin, new CamPattern(960, -1, 960)));
        Assert.That(CameraCommands.HasMatchingSlam(chart, spin), Is.False, "direction no longer matches the slam");
        stack.Undo();
        Assert.That(spin.Direction, Is.EqualTo(1));
        Assert.That(spin.Length, Is.EqualTo(480));

        var swing = new CamPattern(960, 1, 960) { SwingScale = 500, SwingRepeat = 2, SwingDecayOrder = 0 };
        stack.Execute(new AddPatternCommand(CamPatternKind.Swing, swing));
        Assert.That(CameraEvaluator.PatternRollAt(CamPatternKind.Swing, swing, 0.125), Is.EqualTo(2 * CameraEvaluator.SwingDegrees).Within(1e-9));
        stack.Execute(new RemovePatternCommand(CamPatternKind.Spin, spin));
        Assert.That(chart.Camera.Spin, Is.Empty);
        stack.Undo();
        Assert.That(chart.Camera.Spin, Has.Count.EqualTo(1));
    }

    [Test]
    public void ProjectionIsSymmetricPinnedAndRolls()
    {
        var s = CameraState.Default;
        var judge = CameraProjection.Project(s, 0, 0);
        Assert.That(judge.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(judge.Y, Is.EqualTo(CameraProjection.JudgeScreenY).Within(1e-9));

        var l = CameraProjection.Project(s, -1, 0.5);
        var r = CameraProjection.Project(s, 1, 0.5);
        Assert.That(l.X, Is.EqualTo(-r.X).Within(1e-9));
        Assert.That(l.Y, Is.EqualTo(r.Y).Within(1e-9));
        Assert.That(CameraProjection.Project(s, 0, 1).Y, Is.LessThan(judge.Y), "far end is higher on screen");
        Assert.That(Math.Abs(CameraProjection.Project(s, 1, 1).X), Is.LessThan(Math.Abs(CameraProjection.Project(s, 1, 0).X)), "perspective narrows the far end");

        // closer camera → wider lane at the judgement line, still pinned there
        var near = s with { ZoomBottom = 200 };
        Assert.That(CameraProjection.Project(near, 1, 0).X, Is.GreaterThan(CameraProjection.Project(s, 1, 0).X));
        Assert.That(CameraProjection.Project(near, 0, 0).Y, Is.EqualTo(CameraProjection.JudgeScreenY).Within(1e-9));

        // 90° roll turns the lane's right edge downwards around the judgement centre
        var rolled = s with { RotationDeg = 90 };
        var re = CameraProjection.Project(rolled, 1, 0);
        var flat = CameraProjection.Project(s, 1, 0);
        Assert.That(re.X, Is.EqualTo(0).Within(1e-9));
        Assert.That(re.Y - CameraProjection.JudgeScreenY, Is.EqualTo(flat.X).Within(1e-9));

        // centre split moves both halves outwards
        var split = s with { CenterSplit = 100 };
        Assert.That(CameraProjection.SplitX(split, 0.1), Is.EqualTo(0.1 + CameraProjection.BtLaneWidth / 2).Within(1e-9));
        Assert.That(CameraProjection.SplitX(split, -0.1), Is.EqualTo(-0.1 - CameraProjection.BtLaneWidth / 2).Within(1e-9));
    }

    /// <summary>A chart with every camera / background / VOX-extra setting the camera tab edits.</summary>
    public static Chart EditedCameraChart()
    {
        var chart = NewChart();
        var stack = new CommandStack(chart);
        stack.Execute(new AddGraphPointCommand(CamGraph.ZoomTop, new GraphPoint(0, 0, null, new Curve(0.25, 0.75))));
        stack.Execute(new AddGraphPointCommand(CamGraph.ZoomTop, new GraphPoint(960, 120)));
        stack.Execute(new AddGraphPointCommand(CamGraph.ZoomBottom, new GraphPoint(480, -40, 80)));
        stack.Execute(new AddGraphPointCommand(CamGraph.ZoomSide, new GraphPoint(1920, 30)));
        stack.Execute(new AddGraphPointCommand(CamGraph.CenterSplit, new GraphPoint(1920, 100)));
        stack.Execute(new AddGraphPointCommand(CamGraph.RotationDeg, new GraphPoint(2880, 45.5)));
        stack.Execute(new AddTiltPointCommand(new TiltPoint(0, TiltValue.Of(TiltMode.Biggest))));
        stack.Execute(new AddTiltPointCommand(new TiltPoint(1920, TiltValue.Of(0.5), TiltValue.Of(-0.5), new Curve(0.5, 1))));
        stack.Execute(new AddTiltPointCommand(new TiltPoint(2880, TiltValue.Of(0.25))));
        stack.Execute(new AddTiltPointCommand(new TiltPoint(3840, TiltValue.Of(TiltMode.KeepBigger))));
        stack.Execute(new AddPatternCommand(CamPatternKind.Spin, new CamPattern(960, 1, 480)));
        stack.Execute(new AddPatternCommand(CamPatternKind.HalfSpin, new CamPattern(1440, -1, 240)));
        stack.Execute(new AddPatternCommand(CamPatternKind.Swing, new CamPattern(2400, 1, 960) { SwingScale = 300, SwingRepeat = 4, SwingDecayOrder = 1 }));
        var bg = chart.Bg.Clone();
        bg.LegacyBg = new List<string> { "cyber", "bg1.jpg" };
        bg.Layer = new BgLayer { Filename = "layer.gif", DurationMs = 1200, RotateTilt = true, RotateSpin = false };
        bg.Movie = new BgMovie { Filename = "movie.mp4", OffsetMs = -250 };
        stack.Execute(CameraCommands.SetBg(bg));
        var vox = chart.VoxExtras.Clone();
        vox.BgNo = 63;
        vox.PostEffects.Add(new PostEffectRow(960, "RadicalBlur", "Strength", 0, 0.75, 480));
        vox.SpController.AddRange(VoxExtrasInfo.TemplateSpController);
        stack.Execute(CameraCommands.SetVoxExtras(vox));
        return chart;
    }

    [Test]
    public void KsonRoundTripOfCameraEditsIsLossless()
    {
        var chart = EditedCameraChart();
        var again = KsonReader.Read(KsonWriter.Write(chart));
        ModelSnapshot.AssertEqual(chart, again, "kson round trip of camera/bg/vox extras");
        Assert.That(again.Extra.Count, Is.Zero, "nothing fell into Extra");
    }

    [Test]
    public void SetBgAndVoxExtrasUndo()
    {
        var chart = NewChart();
        var before = ModelSnapshot.Of(chart);
        var stack = new CommandStack(chart);
        var bg = chart.Bg.Clone();
        bg.LegacyBg.Add("night");
        stack.Execute(CameraCommands.SetBg(bg));
        var vox = chart.VoxExtras.Clone();
        vox.BgNo = 7;
        stack.Execute(CameraCommands.SetVoxExtras(vox));
        Assert.That(chart.Bg.LegacyBg, Is.EqualTo(new[] { "night" }));
        Assert.That(chart.VoxExtras.BgNo, Is.EqualTo(7));
        stack.Undo();
        stack.Undo();
        Assert.That(ModelSnapshot.FirstDifference(before, ModelSnapshot.Of(chart)), Is.Null);
    }

    /// <summary>The badges claim ksh keeps graphs, tilt and bg/layer/movie: prove it with the real writer and reader.</summary>
    [Test]
    public void KshKeepsWhatTheBadgesClaim()
    {
        var chart = EditedCameraChart();
        var back = KshReader.Read(KshWriter.Write(chart));

        foreach (var g in CameraGraphs.All)
            ModelSnapshot.AssertEqual(CameraGraphs.Get(chart.Camera, g), CameraGraphs.Get(back.Camera, g), "ksh " + CameraGraphs.Name(g));
        ModelSnapshot.AssertEqual(chart.Camera.Tilt, back.Camera.Tilt, "ksh tilt");
        ModelSnapshot.AssertEqual(chart.Bg.LegacyBg, back.Bg.LegacyBg, "ksh bg");
        ModelSnapshot.AssertEqual(chart.Bg.Layer!, back.Bg.Layer!, "ksh layer");
        ModelSnapshot.AssertEqual(chart.Bg.Movie!, back.Bg.Movie!, "ksh movie");
        Assert.That(back.Camera.Spin.Concat(back.Camera.HalfSpin).Concat(back.Camera.Swing).Count(), Is.EqualTo(3), "patterns on distinct lines survive");

        // bg_no is a ksh header key and post effects are //#vox_pe comment lines; the raw base SPCONTROLER rows stay kson-only
        Assert.That(ExportSupport.Get("bg_no").Ksh, Is.EqualTo(Support.Yes));
        Assert.That(ExportSupport.Get("post_effect").Ksh, Is.EqualTo(Support.Yes));
        Assert.That(ExportSupport.Get("sp_controller").Ksh, Is.EqualTo(Support.No));
        Assert.That(back.VoxExtras.BgNo, Is.EqualTo(chart.VoxExtras.BgNo));
        ModelSnapshot.AssertEqual(chart.VoxExtras.PostEffects, back.VoxExtras.PostEffects, "ksh post effects");
        Assert.That(back.VoxExtras.SpController, Is.Empty, "ksh has no field for the raw SPCONTROLER base rows");
    }
}
