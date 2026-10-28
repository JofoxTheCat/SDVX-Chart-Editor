using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class TimingCommandsTests
{
    private const long Beat = 240, Bar = 960;

    /// <summary>120 BPM, notes on beats, a hold, a laser, scroll points, a stop, bookmarks.</summary>
    private static Chart NewChart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Audio.Bgm.OffsetMs = 100;
        c.Audio.Bgm.PreviewOffsetMs = 2100; // pulse 960 at 120 BPM + 100 ms
        c.Audio.Bgm.PreviewDurationMs = 10000;
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[0].Add(new Note(480));
        c.Notes.Bt[1].Add(new Note(Bar, Beat));
        c.Notes.Fx[0].Add(new Note(2 * Bar, 2 * Beat) { Effects = { new FxEffectEvent("gate", Beat) } });
        c.Notes.Lasers[0].Add(new LaserSection(Bar, new[] { new LaserPoint(0, 0), new LaserPoint(Beat, 1), new LaserPoint(2 * Beat, 0.5) }));
        c.Beat.ScrollSpeed.Add(new GraphPoint(0, 1));
        c.Beat.ScrollSpeed.Add(new GraphPoint(Bar, 2));
        c.Beat.Stops.Add(new Stop(3 * Bar, Beat));
        c.Editor.Bookmarks.Add(new PulseValue<string>(2 * Bar, "drop"));
        return c;
    }

    private static void AssertUndoRedoExact(Chart chart, IEditCommand cmd)
    {
        var before = ModelSnapshot.Of(chart);
        var stack = new CommandStack(chart);
        stack.Execute(cmd);
        var after = ModelSnapshot.Of(chart);
        Assert.That(stack.Undo(), Is.True);
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(chart), "after undo");
        Assert.That(stack.Redo(), Is.True);
        ModelSnapshot.AssertEqual(after, ModelSnapshot.Of(chart), "after redo");
    }

    private static Chart KsonRoundTrip(Chart c) => KsonReader.Read(KsonWriter.Write(c));

    // ---------------------------------------------------------------- primitive points

    [Test]
    public void BpmPointsAddMoveChangeRemove()
    {
        var c = NewChart();
        var stack = new CommandStack(c);
        var before = ModelSnapshot.Of(c);

        var p = new BpmChange(2 * Bar, 180);
        stack.Execute(TimingCommands.AddBpm(p));
        Assert.That(c.Beat.Bpm.Select(b => b.Y), Is.EqualTo(new[] { 0L, 2 * Bar }));

        stack.Execute(TimingCommands.ChangeBpm(p, Bar, 150));
        Assert.That(c.Beat.Bpm[1], Is.SameAs(p), "identity kept");
        Assert.That((p.Y, p.Bpm), Is.EqualTo((Bar, 150.0)));

        // moving the first point behind the second re-sorts; undo restores the order
        var first = c.Beat.Bpm[0];
        stack.Execute(TimingCommands.ChangeBpm(first, 3 * Bar, 120));
        Assert.That(c.Beat.Bpm.Select(b => b.Y), Is.EqualTo(new[] { Bar, 3 * Bar }));
        stack.Undo();
        Assert.That(c.Beat.Bpm[0], Is.SameAs(first));

        stack.Execute(TimingCommands.RemoveBpm(p));
        Assert.That(c.Beat.Bpm, Has.Count.EqualTo(1));

        while (stack.Undo()) { }
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(c), "everything undone");
    }

    [Test]
    public void TimeSignaturesPerMeasureAnyDenominator()
    {
        var c = NewChart();
        var stack = new CommandStack(c);

        stack.Execute(TimingCommands.SetTimeSignature(c, 2, 7, 8)!);
        Assert.That(new TimingMap(c.Beat).SignatureOfMeasure(2).Denominator, Is.EqualTo(8));
        Assert.That(new TimingMap(c.Beat).MeasureStart(3), Is.EqualTo(2 * Bar + 840));

        // same measure again = change, not a second entry
        stack.Execute(TimingCommands.SetTimeSignature(c, 2, 5, 16)!);
        Assert.That(c.Beat.TimeSignatures, Has.Count.EqualTo(1));
        Assert.That(new TimingMap(c.Beat).MeasureLength(2), Is.EqualTo(300));

        Assert.That(TimingCommands.SetTimeSignature(c, 0, 4, 4), Is.Null, "4/4 is already in effect at measure 0");

        var back = KsonRoundTrip(c);
        Assert.That(back.Beat.TimeSignatures.Single(), Has.Property("Numerator").EqualTo(5).And.Property("Denominator").EqualTo(16));

        stack.Undo();
        stack.Undo();
        Assert.That(c.Beat.TimeSignatures, Is.Empty);
    }

    [Test]
    public void ScrollSpeedPointsWithSlamAndCurve()
    {
        var c = NewChart();
        var p = c.Beat.ScrollSpeed[1];
        AssertUndoRedoExact(c, TimingCommands.ChangeScrollSpeed(p, Bar + Beat, 2, 0.5, new Curve(0.2, 0.8)));
        Assert.That((p.Y, p.V, p.Vf, p.Curve), Is.EqualTo((Bar + Beat, 2.0, (double?)0.5, (Curve?)new Curve(0.2, 0.8))));

        var back = KsonRoundTrip(c).Beat.ScrollSpeed;
        Assert.That(back.Select(x => (x.Y, x.V, x.Vf, x.Curve)), Is.EqualTo(c.Beat.ScrollSpeed.Select(x => (x.Y, x.V, x.Vf, x.Curve))));

        AssertUndoRedoExact(c, TimingCommands.AddScrollSpeed(new GraphPoint(3 * Bar, 0.75)));
        AssertUndoRedoExact(c, TimingCommands.RemoveScrollSpeed(c.Beat.ScrollSpeed[0]));
    }

    [Test]
    public void StopsAndAudio()
    {
        var c = NewChart();
        AssertUndoRedoExact(c, TimingCommands.ChangeStop(c.Beat.Stops[0], 3 * Bar + Beat, 2 * Beat));
        AssertUndoRedoExact(c, TimingCommands.AddStop(new Stop(Bar, 120)));
        AssertUndoRedoExact(c, TimingCommands.SetOffset(-35));
        AssertUndoRedoExact(c, TimingCommands.SetPreview(12_345, 9_000));
        AssertUndoRedoExact(c, TimingCommands.SetBgmVolume(0.75));

        var back = KsonRoundTrip(c);
        Assert.That(back.Beat.Stops.Select(s => (s.Y, s.Length)), Is.EqualTo(c.Beat.Stops.Select(s => (s.Y, s.Length))));
        Assert.That(back.Audio.Bgm.OffsetMs, Is.EqualTo(-35));
        Assert.That((back.Audio.Bgm.PreviewOffsetMs, back.Audio.Bgm.PreviewDurationMs), Is.EqualTo((12_345L, 9_000L)));
        Assert.That(back.Audio.Bgm.Volume, Is.EqualTo(0.75).Within(1e-9));
    }

    // ---------------------------------------------------------------- "when applying timing changes"

    [Test]
    public void BpmChangeKeepsMusicalPositionByDefault()
    {
        var c = NewChart();
        var notesBefore = ModelSnapshot.Of(c.Notes);
        var draft = TimingDraft.From(c);
        draft.Beat.Bpm[0].Bpm = 240;

        var cmd = TimingCommands.Apply(c, draft, new TimingChangeOptions())!;
        AssertUndoRedoExact(c, cmd);
        ModelSnapshot.AssertEqual(notesBefore, ModelSnapshot.Of(c.Notes), "notes keep their pulses");
        Assert.That(c.Beat.Bpm.Single().Bpm, Is.EqualTo(240));

        // the preview follows the grid: pulse 960 is now at 100 + 1000 ms
        Assert.That(c.Audio.Bgm.PreviewOffsetMs, Is.EqualTo(1100));
        Assert.That(c.Editor.Bookmarks[0].Y, Is.EqualTo(2 * Bar), "bookmark keeps its musical position");
    }

    [Test]
    public void BpmChangeKeepTimeRequantisesNotes()
    {
        var c = NewChart();
        var oldMap = TimingMap.FromChart(c);
        var draft = TimingDraft.From(c);
        draft.Beat.Bpm[0].Bpm = 240; // twice as fast: same ms = twice the pulses

        var options = new TimingChangeOptions { MoveNotesWithGrid = false, SnapNotes = true, SnapDivisor = 4 };
        var cmd = TimingCommands.Apply(c, draft, options)!;
        var stack = new CommandStack(c);
        var before = ModelSnapshot.Of(c);
        stack.Execute(cmd);

        var newMap = TimingMap.FromChart(c);
        Assert.That(c.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new[] { 0L, 960 }));
        Assert.That(c.Notes.Bt[1].Single().Y, Is.EqualTo(2 * Bar));
        Assert.That(c.Notes.Bt[1].Single().Length, Is.EqualTo(2 * Beat), "hold end keeps its time too");
        Assert.That(c.Notes.Fx[0].Single().Effects.Single().RelY, Is.EqualTo(2 * Beat), "effect events stay at their time");
        var laser = c.Notes.Lasers[0].Single();
        Assert.That(laser.Y, Is.EqualTo(2 * Bar));
        Assert.That(laser.Points.Select(p => p.Ry), Is.EqualTo(new[] { 0L, 2 * Beat, 4 * Beat }));

        foreach (var n in new[] { 0L, 480, Bar })
            Assert.That(newMap.PulseToMs(c.Notes.Bt.SelectMany(l => l).ElementAt(Array.IndexOf(new[] { 0L, 480, Bar }, n)).Y),
                Is.EqualTo(oldMap.PulseToMs(n)).Within(0.01), $"note at old pulse {n} keeps its ms");

        // extras follow the grid (default): bookmark and scroll points keep pulses
        Assert.That(c.Editor.Bookmarks[0].Y, Is.EqualTo(2 * Bar));
        Assert.That(c.Beat.ScrollSpeed[1].Y, Is.EqualTo(Bar));

        stack.Undo();
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(c), "undo of a keep-time BPM change is exact");
        Assert.That(stack.CanUndo, Is.False, "one undo step");
    }

    [Test]
    public void KeepTimeWithoutSnapRoundsToPulse_WithSnapQuantises()
    {
        var c = NewChart();
        var draft = TimingDraft.From(c);
        draft.Beat.Bpm[0].Bpm = 123;

        var free = c;
        TimingCommands.Apply(free, draft, new TimingChangeOptions { MoveNotesWithGrid = false })!.Execute(free);
        long freeY = free.Notes.Bt[0][1].Y;
        Assert.That(freeY, Is.EqualTo((long)Math.Round(480 * 123 / 120.0)), "nearest pulse");

        var c2 = NewChart();
        var d2 = TimingDraft.From(c2);
        d2.Beat.Bpm[0].Bpm = 123;
        TimingCommands.Apply(c2, d2, new TimingChangeOptions { MoveNotesWithGrid = false, SnapNotes = true, SnapDivisor = 4 })!.Execute(c2);
        Assert.That(c2.Notes.Bt[0][1].Y % (Beat / 4), Is.EqualTo(0), "on the 1/4 grid");
        Assert.That(c2.Notes.Bt[0][0].Y, Is.EqualTo(0), "the note on the unchanged pulse 0 is untouched");
    }

    [Test]
    public void OffsetChangeKeepTimeAndExtrasKeepTime()
    {
        var c = NewChart();
        var draft = TimingDraft.From(c);
        draft.OffsetMs = 600; // 500 ms later = one beat at 120 BPM

        var options = new TimingChangeOptions { MoveNotesWithGrid = false, MoveExtrasWithGrid = false };
        var cmd = TimingCommands.Apply(c, draft, options)!;
        AssertUndoRedoExact(c, cmd);

        Assert.That(c.Audio.Bgm.OffsetMs, Is.EqualTo(600));
        Assert.That(c.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new[] { 0L, 480 - Beat }), "a note would move before pulse 0: clamped to 0");
        Assert.That(c.Beat.ScrollSpeed.Select(p => p.Y), Is.EqualTo(new[] { 0L, Bar - Beat }), "scroll points keep their time (clamped at 0)");
        Assert.That(c.Beat.Stops[0].Y, Is.EqualTo(3 * Bar - Beat));
        Assert.That(c.Editor.Bookmarks[0].Y, Is.EqualTo(2 * Bar - Beat), "bookmark keeps its time");
        Assert.That(c.Audio.Bgm.PreviewOffsetMs, Is.EqualTo(2100), "preview keeps its time");
    }

    [Test]
    public void OffsetChangeMovesEverythingWithTheGrid()
    {
        var c = NewChart();
        var draft = TimingDraft.From(c);
        draft.OffsetMs = 600;
        TimingCommands.Apply(c, draft, new TimingChangeOptions())!.Execute(c);

        Assert.That(c.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new[] { 0L, 480 }));
        Assert.That(c.Audio.Bgm.PreviewOffsetMs, Is.EqualTo(2600), "preview moved with the offset");
        Assert.That(c.Editor.Bookmarks[0].Y, Is.EqualTo(2 * Bar));
    }

    [Test]
    public void ScaleObjectsWhenABpmPointMoves()
    {
        var c = NewChart();
        c.Beat.Bpm.Add(new BpmChange(Bar, 120));
        c.Beat.Bpm.Add(new BpmChange(2 * Bar, 120));
        c.Notes.Bt[2].Add(new Note(Bar + 480));

        var draft = TimingDraft.From(c);
        draft.Beat.Bpm[1].Y = Bar + Beat; // middle point one beat later

        var cmd = TimingCommands.Apply(c, draft, new TimingChangeOptions { ScaleNotes = true })!;
        AssertUndoRedoExact(c, cmd);

        Assert.That(c.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new[] { 0L, 600 }), "0..960 stretched to 0..1200");
        Assert.That(c.Notes.Bt[2].Single().Y, Is.EqualTo(Bar + Beat + 360), "960..1920 squeezed to 1200..1920");
        Assert.That(c.Notes.Fx[0].Single().Y, Is.EqualTo(2 * Bar), "the unmoved anchor stays");
        Assert.That(c.Beat.ScrollSpeed[1].Y, Is.EqualTo(Bar + Beat), "extras scale too");
    }

    [Test]
    public void ScaleWithSnap()
    {
        var c = NewChart();
        c.Beat.Bpm.Add(new BpmChange(Bar, 120));
        c.Beat.Bpm.Add(new BpmChange(2 * Bar, 120));
        c.Notes.Bt[3].Add(new Note(Bar + 100));
        var draft = TimingDraft.From(c);
        draft.Beat.Bpm[1].Y = Bar + 70;
        TimingCommands.Apply(c, draft, new TimingChangeOptions { ScaleNotes = true, SnapNotes = true, SnapDivisor = 4 })!.Execute(c);
        Assert.That(c.Notes.Bt[3].Single().Y % 60, Is.EqualTo(0), "moved notes land on the 1/4 grid");
    }

    [Test]
    public void LaserPointsCollapsingBecomeASlam()
    {
        var s = new LaserSection(1000, new[] { new LaserPoint(0, 0), new LaserPoint(10, 1), new LaserPoint(240, 0.5) });
        var r = RemapPulsesCommand.RemapSection(s, y => y / 60 * 60)!.Value;
        Assert.That(r.Y, Is.EqualTo(960));
        Assert.That(r.Points.Select(p => (p.Ry, p.V, p.Vf)), Is.EqualTo(new[] { (0L, 0.0, (double?)1.0), (240L, 0.5, (double?)null) }));
        Assert.That(s.Points.Count, Is.EqualTo(3), "input untouched");
    }

    [Test]
    public void ApplyIsOneUndoStepAndNullWhenUnchanged()
    {
        var c = NewChart();
        Assert.That(TimingCommands.Apply(c, TimingDraft.From(c), new TimingChangeOptions()), Is.Null);

        var draft = TimingDraft.From(c);
        draft.Beat.Bpm[0].Bpm = 150;
        draft.Beat.ScrollSpeed.RemoveAt(1);
        draft.Beat.ScrollSpeed.Add(new GraphPoint(2 * Bar, 3));
        draft.Beat.TimeSignatures.Add(new TimeSignature(4, 3, 4));
        draft.Beat.Stops.Clear();
        draft.PreviewDurationMs = 8000;

        var stack = new CommandStack(c);
        var before = ModelSnapshot.Of(c);
        stack.Execute(TimingCommands.Apply(c, draft, new TimingChangeOptions { MoveNotesWithGrid = false })!);
        Assert.That(c.Beat.ScrollSpeed.Select(p => p.Y), Is.EqualTo(new[] { 0L, 2 * Bar }));
        Assert.That(c.Beat.TimeSignatures.Single().Numerator, Is.EqualTo(3));
        Assert.That(c.Beat.Stops, Is.Empty);
        Assert.That(c.Audio.Bgm.PreviewDurationMs, Is.EqualTo(8000));
        Assert.That(stack.History.Count(), Is.EqualTo(1));

        var back = KsonRoundTrip(c);
        Assert.That(KsonWriter.Write(back), Is.EqualTo(KsonWriter.Write(c)), "kson round trip of the retimed chart");

        stack.Undo();
        ModelSnapshot.AssertEqual(before, ModelSnapshot.Of(c), "undone");
    }

    [Test]
    public void ResnapAllNotes()
    {
        var c = NewChart();
        c.Notes.Bt[3].Add(new Note(Bar + 17));
        AssertUndoRedoExact(c, TimingCommands.Resnap(c, 4));
        Assert.That(c.Notes.Bt[3].Single().Y, Is.EqualTo(Bar));
    }

    // ---------------------------------------------------------------- base scroll speed

    [Test]
    public void BaseScrollSpeedScalesTheGraph()
    {
        var c = NewChart();
        Assert.That(TimingCommands.BaseScrollSpeed(c), Is.EqualTo(1));
        AssertUndoRedoExact(c, TimingCommands.SetBaseScrollSpeed(c, 1.5)!);
        Assert.That(c.Beat.ScrollSpeed.Select(p => p.V), Is.EqualTo(new[] { 1.5, 3.0 }));

        var empty = new Chart();
        empty.Beat.Bpm.Add(new BpmChange(0, 120));
        TimingCommands.SetBaseScrollSpeed(empty, 0.8)!.Execute(empty);
        Assert.That(empty.Beat.ScrollSpeed.Single().V, Is.EqualTo(0.8));
        Assert.That(TimingCommands.SetBaseScrollSpeed(empty, 0.8), Is.Null);
    }

    // ---------------------------------------------------------------- property transformer

    [Test]
    public void LinearRampWithSteps()
    {
        var graph = new List<GraphPoint> { new(0, 1), new(Bar, 9), new(4 * Bar, 1) };
        var r = ScrollSpeedTools.Ramp(graph, Bar, 2 * Bar, 1, 2, RampShape.Linear, steps: 4);
        Assert.That(r.Select(p => (p.Y, p.V)), Is.EqualTo(new[]
        {
            (0L, 1.0), (Bar, 1.0), (Bar + 240, 1.25), (Bar + 480, 1.5), (Bar + 720, 1.75), (2 * Bar, 2.0), (4 * Bar, 1.0),
        }));
        Assert.That(graph, Has.Count.EqualTo(3), "input untouched");
    }

    [Test]
    public void ExponentialSteppedRampOnSnapTicks()
    {
        var map = new TimingMap(new BeatInfo { Bpm = { new BpmChange(0, 120) } });
        var r = ScrollSpeedTools.Ramp(new List<GraphPoint>(), 0, Bar, 1, 4, RampShape.Exponential, stepped: true, grid: map, snapDivisor: 1);
        Assert.That(r.Select(p => p.Y), Is.EqualTo(new[] { 0L, 240, 480, 720, 960 }));
        Assert.That(r.Select(p => p.ValueAfter), Is.EqualTo(new[] { 1, Math.Sqrt(2), 2, Math.Pow(2, 1.5), 4 }).Within(1e-9));
        // stepped: each point jumps from the previous level, so the graph is flat between points
        Assert.That(r[2].V, Is.EqualTo(Math.Sqrt(2)).Within(1e-9));
        Assert.That(Graph.ValueAt(r, 360, 1), Is.EqualTo(Math.Sqrt(2)).Within(1e-9));

        // as a command on a chart, undoable and through kson
        var c = NewChart();
        var graph = ScrollSpeedTools.Ramp(c.Beat.ScrollSpeed, 2 * Bar, 3 * Bar, 0.5, 2, RampShape.Exponential, stepped: true,
            grid: TimingMap.FromChart(c), snapDivisor: 2);
        AssertUndoRedoExact(c, new SetScrollSpeedGraphCommand("ramp", graph));
        var back = KsonRoundTrip(c).Beat.ScrollSpeed;
        Assert.That(back.Select(p => (p.Y, p.V, p.Vf)), Is.EqualTo(c.Beat.ScrollSpeed.Select(p => (p.Y, p.V, p.Vf))));
    }

    [Test]
    public void ScaleShiftCopyPaste()
    {
        var graph = new List<GraphPoint> { new(0, 1), new(Bar, 2, 3), new(2 * Bar, 1), new(3 * Bar, 0.5) };

        var scaled = ScrollSpeedTools.Scale(graph, Bar, 2 * Bar, 2);
        Assert.That(scaled.Select(p => (p.V, p.Vf)), Is.EqualTo(new[] { (1.0, (double?)null), (4.0, (double?)6.0), (2.0, (double?)null), (0.5, (double?)null) }));
        Assert.That(scaled[0], Is.SameAs(graph[0]), "untouched points pass through");
        Assert.That(graph[1].V, Is.EqualTo(2), "input untouched");

        var shifted = ScrollSpeedTools.Shift(graph, Bar, 2 * Bar, Bar);
        Assert.That(shifted.Select(p => (p.Y, p.V)), Is.EqualTo(new[] { (0L, 1.0), (2 * Bar, 2.0), (3 * Bar, 1.0) }), "the moved point replaced the one at 3 bars");

        var clip = ScrollSpeedTools.Copy(graph, Bar, 2 * Bar);
        Assert.That(clip.Select(p => p.Y), Is.EqualTo(new[] { 0L, Bar }));
        var pasted = ScrollSpeedTools.Paste(graph, clip, 3 * Bar);
        Assert.That(pasted.Select(p => (p.Y, p.V, p.Vf)), Is.EqualTo(new[]
        {
            (0L, 1.0, (double?)null), (Bar, 2.0, (double?)3.0), (2 * Bar, 1.0, (double?)null), (3 * Bar, 2.0, (double?)3.0), (4 * Bar, 1.0, (double?)null),
        }));

        var c = NewChart();
        AssertUndoRedoExact(c, new SetScrollSpeedGraphCommand("paste", ScrollSpeedTools.Paste(c.Beat.ScrollSpeed, clip, 3 * Bar)));
        var back = KsonRoundTrip(c);
        Assert.That(back.Beat.ScrollSpeed.Select(p => (p.Y, p.V, p.Vf)), Is.EqualTo(c.Beat.ScrollSpeed.Select(p => (p.Y, p.V, p.Vf))));
    }

    [Test]
    public void RemapCoversExtrasAndUndoesExactly()
    {
        var c = NewChart();
        c.Camera.Body.ZoomTop.Add(new GraphPoint(Bar, 10));
        c.Camera.Tilt.Add(new TiltPoint(Bar, TiltValue.Of(0.5)));
        c.Camera.Spin.Add(new CamPattern(Bar, 1, Beat));
        c.Audio.KeySound.LaserSlamEvents["slam_up"] = new List<long> { Bar };
        c.Audio.FxEffects.ParamChanges["flanger"] = new() { ["period"] = new() { new PulseValue<string>(Bar, "1/4") } };
        c.Editor.Comments.Add(new PulseValue<string>(Bar, "hi"));
        c.EndPulse = 5 * Bar;

        var cmd = new RemapPulsesCommand("x2", y => y * 2, RetimeTargets.All);
        AssertUndoRedoExact(c, cmd);
        Assert.That(c.Camera.Body.ZoomTop.Single().Y, Is.EqualTo(2 * Bar));
        Assert.That(c.Camera.Tilt.Single().Y, Is.EqualTo(2 * Bar));
        Assert.That((c.Camera.Spin.Single().Y, c.Camera.Spin.Single().Length), Is.EqualTo((2 * Bar, 2 * Beat)));
        Assert.That(c.Audio.KeySound.LaserSlamEvents["slam_up"].Single(), Is.EqualTo(2 * Bar));
        Assert.That(c.Audio.FxEffects.ParamChanges["flanger"]["period"].Single().Y, Is.EqualTo(2 * Bar));
        Assert.That(c.Editor.Comments.Single().Y, Is.EqualTo(2 * Bar));
        Assert.That(c.EndPulse, Is.EqualTo(10 * Bar));
        Assert.That(c.Beat.Bpm.Single().Y, Is.EqualTo(0), "BPM points are never remapped");
    }

    /// <summary>
    /// Keep time + snap on real charts with many tempo changes: every note moves by at most half a snap step
    /// (plus one pulse of grid rounding) measured at the tempo where it lands; without snap, by at most half a pulse.
    /// </summary>
    [TestCaseSource(typeof(RealCharts), nameof(RealCharts.Cases), new object[] { ".ksh", "KeepTimeOnRealCharts" })]
    public void KeepTimeOnRealCharts(string path)
    {
        RealCharts.RequireFile(path);
        foreach (int divisor in new[] { 0, 16 })
        {
            var c = Formats.Ksh.KshReader.ReadFile(path);
            var oldMap = TimingMap.FromChart(c);
            var draft = TimingDraft.From(c);
            long at = oldMap.MeasureStart(4);
            var point = draft.Beat.Bpm.LastOrDefault(b => b.Y <= at) ?? draft.Beat.Bpm[0];
            point.Bpm = Math.Round(point.Bpm * 1.02, 3);

            var before = c.Notes.Bt.SelectMany(l => l).Select(n => (Note: n, Ms: oldMap.PulseToMs(n.Y))).ToList();
            var cmd = TimingCommands.Apply(c, draft, new TimingChangeOptions { MoveNotesWithGrid = false, SnapNotes = divisor > 0, SnapDivisor = divisor })!;
            cmd.Execute(c);
            var newMap = TimingMap.FromChart(c);

            double reach = divisor > 0 ? Timebase.PulsesPerQuarter / (2.0 * divisor) + 1 : 0.5;
            foreach (var (n, ms) in before)
            {
                if (n.Y == 0 && ms > newMap.PulseToMs(0)) continue; // clamped at pulse 0
                double drift = Math.Abs(newMap.PulseToMs(n.Y) - ms);
                double limit = Math.Max(newMap.PulseToMs(n.Y + reach) - newMap.PulseToMs(n.Y), newMap.PulseToMs(n.Y) - newMap.PulseToMs(n.Y - reach)) + 1e-6;
                Assert.That(drift, Is.LessThanOrEqualTo(limit), $"1/{divisor}: note at {n.Y} drifted {drift:0.00} ms");
            }

            cmd.Undo(c);
            Assert.That(KsonWriter.Write(c), Is.EqualTo(KsonWriter.Write(Formats.Ksh.KshReader.ReadFile(path))), "undo exact");
        }
    }
}
