using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// Slams made and edited as two points (start end, landing end) in the anchor-path editor. The chart model stays one
/// point / anchor with <c>V</c> and <c>Vf</c>.
/// </summary>
[TestFixture]
public class SlamEditingTests
{

    /// <summary>The laser tool's first click (laserNewUp) and the following clicks (placementClick → AddAnchor).</summary>
    private static LaserPlacement clicks(params (long Ry, double V)[] at)
    {
        var p = new LaserPlacement(0, false, 960, new LaserPath(new[] { new LaserAnchor(0, at[0].V, type: LaserCurveType.Linear, auto: true) }));
        foreach (var (ry, v) in at.Skip(1))
            p = p.With(p.Y, LaserPathEditing.AddAnchor(p.Path, ry, v)!);
        return p;
    }

    private static Chart chartWith(params LaserSection[] lane0)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        foreach (var s in lane0) c.Notes.Lasers[0].Add(s);
        return c;
    }

    /// <summary>0 → slam 0.2→0.8 at 240 → 0.8 at 480, plain points.</summary>
    private static LaserSection plainSlam() =>
        new(960, new[] { new LaserPoint(0, 0), new LaserPoint(240, 0.2, 0.8), new LaserPoint(480, 0.8) });

    // ---- placing -----------------------------------------------------------

    [Test]
    public void TwoClicksOnOneBeatMakeAPlaceableSlam()
    {
        // the regression: the draft was one slam anchor, previewed as a single point and dropped on right click
        var pl = clicks((0, 0.2), (0, 0.8));
        Assert.That(pl.Path.Anchors.Single().IsSlam, "the second click on the same beat turns the anchor into a slam");
        Assert.That(pl.Preview.SlamCount, Is.EqualTo(1), "the preview shows the slam");
        Assert.That(pl.Result, Is.Not.Null, "finishing adds it");

        // exactly what dragging a slam gives: a lone slam, ONE point, no tail (official charts: 118,940 such sections)
        var drawn = LaserEditing.NewSection(960, 0.2, 960, 0.8, 1)!;
        Assert.That(LaserPaths.SamePoints(pl.Result!.Points, drawn.Points, 0), Is.True);
        Assert.That(pl.Result.Points.Select(p => (p.Ry, p.V, p.Vf)), Is.EqualTo(new[] { (0L, 0.2, (double?)0.8) }));
        Assert.That(pl.Result.Y, Is.EqualTo(960));
        Assert.That(pl.Result.Path, Is.Null, "a straight slam is plain points, no stored path");
    }

    [Test]
    public void OnePlainAnchorIsNotPlaceable()
    {
        var pl = clicks((0, 0.2));
        Assert.That(pl.Result, Is.Null);
        Assert.That(pl.Preview.Points, Has.Count.EqualTo(1));
    }

    [Test]
    public void ASlamIsAHardBreakForAutoTyping()
    {
        // clicks A, B, C, C' (slam), D, E: two 3-anchor segments, each a perfect curve (no Bezier joint at a slam)
        var pl = clicks((0, 0.1), (240, 0.5), (480, 0.2), (480, 0.9), (720, 0.5), (960, 0.8));
        var segs = pl.Path.Segments();
        Assert.That(segs.Select(s => (s.Start, s.End)), Is.EqualTo(new[] { (0, 2), (2, 4) }));
        Assert.That(segs.Select(s => s.Effective), Is.EqualTo(new[] { LaserCurveType.PerfectCurve, LaserCurveType.PerfectCurve }));
        Assert.That(pl.Path.Anchors[2].Auto, Is.True, "the slam anchor stays auto-typed");

        // a lone slam followed by two clicks: one perfect curve starting at the slam's end value
        var lone = clicks((0, 0.1), (0, 0.9), (240, 0.4), (480, 0.6));
        Assert.That(lone.Path.Segments().Single().Effective, Is.EqualTo(LaserCurveType.PerfectCurve));
        Assert.That(LaserPathBaker.ValueAt(lone.Path, 0), Is.EqualTo(0.9).Within(1e-9), "the curve starts at the landing end");
        Assert.That(lone.Result!.Points[0].IsSlam, Is.True);
    }

    [Test]
    public void SlamClickRule()
    {
        var p = clicks((0, 0.2), (240, 0.5)).Path;
        Assert.That(LaserPathEditing.IsSlamClick(p, 240, 0.52), Is.True, "last beat, other value");
        Assert.That(LaserPathEditing.IsSlamClick(p, 240, 0.5), Is.False, "same value = the anchor itself");
        Assert.That(LaserPathEditing.IsSlamClick(p, 0, 0.9), Is.False, "not the last anchor");
        Assert.That(LaserPathEditing.IsSlamClick(LaserPathEditing.AddAnchor(p, 240, 0.9)!, 240, 0.1), Is.False, "already a slam");
    }

    // ---- dragging ------------------------------------------------------------

    [Test]
    public void DraggingTheSlamEndInTimeMovesTheSlam()
    {
        var p = LaserPaths.AnchorsOf(plainSlam());
        var moved = LaserPathEditing.MoveAnchor(p, 1, 300, 0.9, slamEnd: true)!;
        Assert.That(moved.Anchors[1].Ry, Is.EqualTo(300), "the landing end moves the slam in time");
        Assert.That(moved.Anchors[1].V, Is.EqualTo(0.2), "the start value stays");
        Assert.That(moved.Anchors[1].Vf, Is.EqualTo(0.9));

        var start = LaserPathEditing.MoveAnchor(p, 1, 200, 0.1)!;
        Assert.That((start.Anchors[1].Ry, start.Anchors[1].V, start.Anchors[1].Vf), Is.EqualTo((200L, 0.1, (double?)0.8)), "the start end moves V and the slam");

        var clamped = LaserPathEditing.MoveAnchor(p, 1, 5000, 0.9, slamEnd: true)!;
        Assert.That(clamped.Anchors[1].Ry, Is.EqualTo(479), "both ends stay strictly between the neighbours");
        Assert.That(LaserPathEditing.MoveAnchor(p, 1, 240, 0.2, slamEnd: true)!.Anchors[1].IsSlam, Is.False, "landing on the start value removes the jump");
    }

    // ---- deleting one end ------------------------------------------------------

    [Test]
    public void DeleteSlamEndKeepsTheOtherValue()
    {
        var p = LaserPaths.AnchorsOf(plainSlam());
        var noStart = LaserPathEditing.DeleteSlamEnd(p, 1, end: false)!;
        Assert.That((noStart.Anchors[1].V, noStart.Anchors[1].Vf), Is.EqualTo((0.8, (double?)null)));
        var noEnd = LaserPathEditing.DeleteSlamEnd(p, 1, end: true)!;
        Assert.That((noEnd.Anchors[1].V, noEnd.Anchors[1].Vf), Is.EqualTo((0.2, (double?)null)));
        Assert.That(LaserPathEditing.DeleteSlamEnd(p, 0, false), Is.Null, "no slam there");
        Assert.That(noStart.Count, Is.EqualTo(3), "the anchor stays");
    }

    [Test]
    public void SelectedSlamEndDeletesOnlyThatEnd()
    {
        var s = plainSlam();
        var c = chartWith(s);
        var before = KsonWriter.Write(c);
        var sel = new Selection();
        sel.Add(new LaserSlamEndRef(0, s, s.Points[1], true));
        var stack = new CommandStack(c);
        stack.Execute(SelectionOps.Delete(sel)!);

        var now = c.Notes.Lasers[0].Single();
        Assert.That(now.Points.Select(pt => (pt.Ry, pt.V, pt.Vf)), Is.EqualTo(new[] { (0L, 0.0, (double?)null), (240L, 0.2, null), (480L, 0.8, null) }));
        Assert.That(stack.Undo(), Is.True);
        Assert.That(c.Notes.Lasers[0].Single(), Is.SameAs(s), "one undo step puts the exact section back");
        Assert.That(KsonWriter.Write(c), Is.EqualTo(before));
    }

    [Test]
    public void BothEndsSelectedDeleteTheAnchor()
    {
        var s = plainSlam();
        var c = chartWith(s);
        var sel = new Selection();
        sel.Add(new LaserSlamEndRef(0, s, s.Points[1], false));
        sel.Add(new LaserSlamEndRef(0, s, s.Points[1], true));
        new CommandStack(c).Execute(SelectionOps.Delete(sel)!);
        Assert.That(c.Notes.Lasers[0].Single().Points.Select(pt => (pt.Ry, pt.V)), Is.EqualTo(new[] { (0L, 0.0), (480L, 0.8) }));

        // a two-point section loses its last anchor pair: the section goes
        var two = new LaserSection(0, new[] { new LaserPoint(0, 0.1, 0.9), new LaserPoint(60, 0.9) });
        var c2 = chartWith(two);
        var sel2 = new Selection();
        sel2.Add(new LaserSlamEndRef(0, two, two.Points[0], false));
        sel2.Add(new LaserSlamEndRef(0, two, two.Points[0], true));
        new CommandStack(c2).Execute(SelectionOps.Delete(sel2)!);
        Assert.That(c2.Notes.Lasers[0], Is.Empty);
    }

    [Test]
    public void DeletingASlamEndOfACurvedPathKeepsItsCurves()
    {
        var path = new LaserPath(new[]
        {
            new LaserAnchor(0, 0.1, type: LaserCurveType.PerfectCurve), new LaserAnchor(240, 0.6), new LaserAnchor(480, 0.3, 0.9, LaserCurveType.Linear),
            new LaserAnchor(720, 0.5),
        });
        var s = LaserPaths.ToSection(path, 1920, 1);
        var slamPoint = s.Points.Single(pt => pt.IsSlam);
        var c = chartWith(s);
        var sel = new Selection();
        sel.Add(new LaserSlamEndRef(0, s, slamPoint, false));
        new CommandStack(c).Execute(SelectionOps.Delete(sel)!);

        var now = c.Notes.Lasers[0].Single();
        var kept = LaserPaths.ValidPath(now)!;
        Assert.That(kept, Is.Not.Null, "the path stays valid (edited through the anchors, not the baked points)");
        Assert.That(kept.Anchors[0].Type, Is.EqualTo(LaserCurveType.PerfectCurve));
        Assert.That((kept.Anchors[2].V, kept.Anchors[2].IsSlam), Is.EqualTo((0.9, false)), "the slam became a plain anchor at its end value");
    }

    // ---- selection -------------------------------------------------------------

    [Test]
    public void SelectionHoldsSlamEndsOneByOne()
    {
        var s = plainSlam();
        var point = new LaserPointRef(0, s, s.Points[1]);
        var start = new LaserSlamEndRef(0, s, s.Points[1], false);
        var sel = new Selection();

        sel.Toggle(start);
        Assert.That(sel.IsEndSelected(point, false) && !sel.IsEndSelected(point, true) && !sel.Contains(point));
        Assert.That(sel.Sections.Single().Section, Is.SameAs(s), "an end touches its section (move / copy act on it)");
        Assert.That(sel.StartPulse, Is.EqualTo(1200));
        Assert.That(sel.Count, Is.EqualTo(1));

        sel.Toggle(start);
        Assert.That(sel.IsEmpty);

        // a whole selected point: ctrl+click on one end leaves the other
        sel.Add(point);
        sel.Add(start);
        Assert.That(sel.SlamEnds, Is.Empty, "an end of a whole selected point adds nothing");
        sel.Toggle(start);
        Assert.That(!sel.Contains(point) && sel.Contains(start with { End = true }) && !sel.Contains(start));
        sel.Clear();
        Assert.That(sel.IsEmpty);
    }

    [Test]
    public void ClipboardAndMoveTakeTheWholeSectionOfASlamEnd()
    {
        var s = plainSlam();
        var c = chartWith(s);
        var sel = new Selection();
        sel.Add(new LaserSlamEndRef(0, s, s.Points[1], true));

        var clip = ClipboardData.FromSelection(sel);
        Assert.That(clip.Lasers.Single().Section.Points, Has.Count.EqualTo(3));

        var move = SelectionOps.Move(c, sel, 240)!;
        new CommandStack(c).Execute(move);
        Assert.That(c.Notes.Lasers[0].Single().Y, Is.EqualTo(1200));
    }

    [Test]
    public void TimestampWritesTheSlamOfASelectedEndOnce()
    {
        var s = plainSlam();
        var c = chartWith(s);
        var timing = TimingMap.FromChart(c);
        var one = new Selection();
        one.Add(new LaserSlamEndRef(0, s, s.Points[1], true));
        var both = new Selection();
        both.Add(new LaserSlamEndRef(0, s, s.Points[1], true));
        both.Add(new LaserSlamEndRef(0, s, s.Points[1], false));

        string text = ChartTimestamp.Of(timing, one).ToString();
        Assert.That(text, Is.EqualTo(ChartTimestamp.Of(timing, both).ToString()));
        Assert.That(text, Does.Contain("0.2>0.8"), text);

        // read back, it selects the slam point
        Assert.That(ChartTimestamp.TryParse(text, out var parsed), Is.True);
        Assert.That(parsed.Resolve(c, timing).LaserPoints.Single().Point, Is.SameAs(s.Points[1]));
    }

    // ---- formats: nothing changes ----------------------------------------------

    [Test]
    public void PlainSectionsRebakeThroughAnchorsUnchanged()
    {
        // the anchor route (delete of a slam end) re-bakes a plain section: everything but the edited point must stay
        var s = new LaserSection(480, new[]
        {
            new LaserPoint(0, 0.25, null, new Curve(0.3, 0.7)), new LaserPoint(120, 0.5, 0.0), new LaserPoint(240, 0.125, 1.0),
            new LaserPoint(600, 1.0),
        }, 2);
        var back = LaserPaths.ToSection(LaserPaths.AnchorsOf(s), s.Y, s.Width);
        Assert.That(LaserPaths.SamePoints(back.Points, s.Points, 0), Is.True);
        Assert.That((back.Y, back.Width, back.Path), Is.EqualTo((s.Y, s.Width, (LaserPath?)null)));

        var c1 = chartWith(s);
        var c2 = chartWith(back);
        Assert.That(KsonWriter.Write(c2), Is.EqualTo(KsonWriter.Write(c1)));
        Assert.That(KshWriter.Write(c2), Is.EqualTo(KshWriter.Write(c1)));
    }

    [Test]
    public void APlacedSlamWritesLikeADrawnOne()
    {
        var placed = clicks((0, 0.2), (0, 0.8)).Result!;
        var drawn = LaserEditing.NewSection(960, 0.2, 960, 0.8, 1)!;
        Assert.That(KsonWriter.Write(chartWith(placed)), Is.EqualTo(KsonWriter.Write(chartWith(drawn))));
        Assert.That(KshWriter.Write(chartWith(placed)), Is.EqualTo(KshWriter.Write(chartWith(drawn))));
    }
}
