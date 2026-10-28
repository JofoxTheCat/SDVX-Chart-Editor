using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Timing;
using SDVXEditor.Core.Validation;

namespace SDVXEditor.Core.Tests;

/// <summary>Chart check, insert / delete time, slam sounds, hold effects, laser swap, inspector, gauges (2026-10-27 batch).</summary>
public class FeatureBatchTests
{
    private static Chart chart(double bpm = 120)
    {
        var c = new Chart();
        c.Meta.Title = "t";
        c.Audio.Bgm.Filename = "a.ogg";
        c.Beat.Bpm.Add(new BpmChange(0, bpm));
        return c;
    }

    private static LaserSection section(long y, params (long Ry, double V, double? Vf)[] pts) =>
        new LaserSection(y, pts.Select(p => new LaserPoint(p.Ry, p.V, p.Vf)));

    private static IEnumerable<string> ids(Chart c, double? audioMs = null) =>
        ChartChecker.Run(new ChartCheckContext(c) { AudioLengthMs = audioMs }).Issues.Select(i => i.RuleId);

    // ---- chart check -------------------------------------------------------------------------------------------

    [Test]
    public void RulesAreDiscoveredAndACrashBecomesAnIssue()
    {
        var rules = ChartChecker.Rules.Select(r => r.Id).ToList();
        Assert.That(rules, Is.SupersetOf(new[] { "timing", "notes", "laser", "slam", "effect-use", "meta", "audio", "format" }));
        Assert.That(rules, Is.Unique);

        var report = ChartChecker.Run(new ChartCheckContext(chart()), new IChartCheck[] { new Throwing() });
        Assert.That(report.Issues.Single().RuleId, Is.EqualTo("boom.crashed"));
        Assert.That(report.Issues.Single().Severity, Is.EqualTo(IssueSeverity.Error));
    }

    private sealed class Throwing : IChartCheck
    {
        public string Id => "boom";
        public string Category => "Test";
        public IEnumerable<ChartIssue> Run(ChartCheckContext context) => throw new InvalidOperationException("x");
    }

    [Test]
    public void CleanChartHasNoWarnings()
    {
        var c = chart();
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[1].Add(new Note(240, 240));
        c.Notes.Lasers[0].Add(section(0, (0, 0, null), (480, 1, null)));
        var report = ChartChecker.Run(c);
        Assert.That(report.Errors + report.Warnings, Is.Zero, string.Join("; ", report.Issues.Select(i => i.Message)));
    }

    [Test]
    public void NoteAndLaserStructure()
    {
        var c = chart();
        c.Notes.Bt[0].Add(new Note(0, 480));
        c.Notes.Bt[0].Add(new Note(240));
        c.Notes.Lasers[1].Add(section(0, (0, 0, null), (480, 1.2, null)));
        c.Notes.Lasers[1].Add(section(480, (0, 1, null), (240, 0, null)));
        c.Notes.Lasers[0].Add(section(960, (0, 0.5, null)));
        var found = ids(c).ToList();
        Assert.That(found, Does.Contain("notes.overlap"));
        Assert.That(found, Does.Contain("laser.value-range"));
        Assert.That(found, Does.Contain("laser.overlap"));
        Assert.That(found, Does.Contain("laser.single-point"));

        var overlap = ChartChecker.Run(c).Issues.First(i => i.RuleId == "notes.overlap");
        Assert.That(overlap.Notes, Has.Count.EqualTo(2), "jump selects both notes");
        Assert.That(overlap.Pulse, Is.EqualTo(240));
    }

    [Test]
    public void SlamPatterns()
    {
        var c = chart();
        // two slams 1/32 apart, a narrow slam, a 1/16 moving segment after a slam
        c.Notes.Lasers[0].Add(section(0, (0, 0, 1), (30, 1, 0), (480, 0, 0.05), (960, 0, 1), (1020, 0.5, null)));
        var issues = ChartChecker.Run(c).Issues;
        Assert.That(issues.Count(i => i.RuleId == "slam.successive"), Is.EqualTo(1));
        Assert.That(issues.Single(i => i.RuleId == "slam.narrow").Pulse, Is.EqualTo(480));
        Assert.That(issues.Single(i => i.RuleId == "slam.short-segment").Pulse, Is.EqualTo(960));

        // a wide laser's slam of 0.06 is 0.12 of a normal width: not narrow
        var w = chart();
        w.Notes.Lasers[0].Add(new LaserSection(0, new[] { new LaserPoint(0, 0.5, 0.56), new LaserPoint(480, 0.56) }, 2));
        Assert.That(ids(w), Does.Not.Contain("slam.narrow"));
    }

    [Test]
    public void EffectsKeySoundsAndPatterns()
    {
        var c = chart();
        var hold = new Note(0, 480);
        hold.Effects.Add(new FxEffectEvent("nonexistent"));
        c.Notes.Fx[0].Add(hold);
        var ok = new Note(960, 480);
        ok.Effects.Add(new FxEffectEvent("retrigger"));
        c.Notes.Fx[1].Add(ok);
        c.Audio.KeySound.LaserSlamEvents["slam_up"] = new List<long> { 100 };
        c.Camera.Spin.Add(new CamPattern(200, 1, 240));
        c.Audio.LaserEffects.PulseEvents["mystery"] = new List<long> { 0 };
        var found = ids(c).ToList();
        Assert.That(found.Count(i => i == "effect.undefined-fx"), Is.EqualTo(1), "a preset name is fine");
        Assert.That(found, Does.Contain("keysound.slam-not-on-slam"));
        Assert.That(found, Does.Contain("camera.pattern-without-slam"));
        Assert.That(found, Does.Contain("effect.undefined-laser"));

        c.Notes.Lasers[0].Add(section(100, (0, 0, 1), (240, 1, null)));
        Assert.That(ids(c), Does.Not.Contain("keysound.slam-not-on-slam"), "now there is a slam at 100");
    }

    [Test]
    public void MetadataTimingAndFormats()
    {
        var c = chart();
        c.Meta.TitleTranslit = "テスト";
        c.Meta.DispBpm = "120~240";
        c.Meta.StdBpm = 300;
        c.Meta.Level = 25;
        c.Beat.TimeSignatures.Add(new TimeSignature(1, 7, 7));
        c.Notes.Bt[2].Add(new Note(1)); // between VOX ticks
        c.Notes.Lasers[0].Add(section(0, (0, 0.333, null), (480, 1, null)));
        var found = ids(c).ToList();
        Assert.That(found, Is.SupersetOf(new[]
        {
            "meta.non-ascii-translit", "timing.disp-bpm", "timing.std-bpm", "meta.level-range", "timing.signature-denominator", "format.vox-grid", "format.ksh-laser-values",
        }));
    }

    [Test]
    public void ObjectsAfterTheAudioEnd()
    {
        var c = chart(); // 120 BPM: 1 beat = 500 ms
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[0].Add(new Note(240 * 4));
        Assert.That(ids(c, 3000), Does.Not.Contain("audio.past-end"));
        Assert.That(ids(c, 1000), Does.Contain("audio.past-end"));
        c.Audio.Bgm.OffsetMs = -1500; // chart 2000 ms plays at track 500 ms
        Assert.That(ids(c, 1000), Does.Not.Contain("audio.past-end"));
    }

    // ---- insert / delete time ------------------------------------------------------------------------------------

    private static Chart timeChart()
    {
        var c = chart();
        c.Beat.Bpm.Add(new BpmChange(1920, 180));
        c.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        c.Beat.TimeSignatures.Add(new TimeSignature(3, 3, 4));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[0].Add(new Note(960));
        c.Notes.Bt[1].Add(new Note(480, 960)); // spans 960
        c.Notes.Bt[2].Add(new Note(2400));
        c.Notes.Lasers[0].Add(section(720, (0, 0, null), (480, 1, null)));
        c.Beat.ScrollSpeed.Add(new GraphPoint(0, 1));
        c.Beat.ScrollSpeed.Add(new GraphPoint(1920, 2));
        c.Editor.Bookmarks.Set(1920, new Bookmark("b"));
        c.Editor.Bookmarks.Set(1000, new Bookmark("inside"));
        return c;
    }

    [Test]
    public void InsertBlankShiftsEverythingAfterTheCursor()
    {
        var c = timeChart();
        string before = KsonWriter.Write(c);
        var r = TimeShift.InsertBlank(c, 960, 960)!;
        Assert.That(r.SignaturesMoved, Is.True, "one 4/4 measure on a bar line");
        var stack = new CommandStack(c);
        stack.Execute(r.Command);

        Assert.That(c.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new long[] { 0, 1920 }));
        Assert.That(c.Notes.Bt[1][0].Length, Is.EqualTo(1920), "a hold across the cursor grows");
        Assert.That(c.Notes.Lasers[0][0].EndY, Is.EqualTo(720 + 480 + 960), "a laser across the cursor stretches");
        Assert.That(c.Beat.Bpm.Select(b => b.Y), Is.EqualTo(new long[] { 0, 2880 }));
        Assert.That(c.Beat.TimeSignatures.Select(s => s.MeasureIndex), Is.EqualTo(new[] { 0, 4 }));
        Assert.That(c.Beat.ScrollSpeed.Select(p => p.Y), Is.EqualTo(new long[] { 0, 2880 }));
        Assert.That(c.Editor.Bookmarks.Select(b => b.Y), Does.Contain(2880L));

        stack.Undo();
        Assert.That(KsonWriter.Write(c), Is.EqualTo(before), "undo restores the exact chart");
        stack.Redo();
        stack.Undo();
        Assert.That(KsonWriter.Write(c), Is.EqualTo(before));
    }

    [Test]
    public void InsertingPartOfAMeasureLeavesSignaturesAndKeepsTheTempoAtTheCursor()
    {
        var c = timeChart();
        c.Beat.Bpm.Add(new BpmChange(960, 150));
        var r = TimeShift.InsertBlank(c, 960, 60)!;
        Assert.That(r.SignaturesMoved, Is.False);
        r.Command.Execute(c);
        Assert.That(c.Beat.TimeSignatures.Select(s => s.MeasureIndex), Is.EqualTo(new[] { 0, 3 }));
        Assert.That(c.Beat.Bpm.OrderBy(b => b.Y).Select(b => b.Y), Is.EqualTo(new long[] { 0, 960, 1980 }), "the BPM point on the cursor stays");
    }

    [Test]
    public void DeleteRangeRemovesInsideAndPullsBack()
    {
        var c = timeChart();
        string before = KsonWriter.Write(c);
        var r = TimeShift.DeleteRange(c, 960, 1920)!;
        var stack = new CommandStack(c);
        stack.Execute(r.Command);

        Assert.That(c.Notes.Bt[0].Select(n => n.Y), Is.EqualTo(new long[] { 0 }), "the note at 960 was inside");
        Assert.That(c.Notes.Bt[1][0].Length, Is.EqualTo(480), "a hold into the range is cut at its start");
        Assert.That(c.Notes.Bt[2][0].Y, Is.EqualTo(1440));
        Assert.That(c.Notes.Lasers[0][0].Points.Count, Is.EqualTo(2));
        Assert.That(c.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 120.0), (960L, 180.0) }));
        Assert.That(c.Editor.Bookmarks.Select(b => b.Y), Is.EqualTo(new long[] { 960 }), "the inner bookmark goes, the one at the end lands on the start");
        Assert.That(r.SignaturesMoved, Is.True);
        Assert.That(c.Beat.TimeSignatures.Select(s => s.MeasureIndex), Is.EqualTo(new[] { 0, 2 }));

        stack.Undo();
        Assert.That(KsonWriter.Write(c), Is.EqualTo(before));
    }

    [Test]
    public void DeleteKeepsTheTempoOfTheRangeEnd()
    {
        var c = chart();
        c.Beat.Bpm.Add(new BpmChange(480, 200));
        c.Notes.Bt[0].Add(new Note(1440));
        var timing = TimingMap.FromChart(c);
        double after = timing.PulseToMs(1440) - timing.PulseToMs(960);
        TimeShift.DeleteRange(c, 240, 960)!.Command.Execute(c);
        Assert.That(c.Beat.Bpm.Select(b => (b.Y, b.Bpm)), Is.EqualTo(new[] { (0L, 120.0), (240L, 200.0) }));
        var t2 = TimingMap.FromChart(c);
        Assert.That(t2.PulseToMs(720) - t2.PulseToMs(240), Is.EqualTo(after).Within(1e-9), "the part after the range keeps its tempo");
    }

    [TestCaseSource(typeof(RealCharts), nameof(RealCharts.Cases), new object[] { ".kson", nameof(InsertThenDeleteIsIdentityOnRealCharts) })]
    public void InsertThenDeleteIsIdentityOnRealCharts(string path)
    {
        RealCharts.RequireFile(path);
        var c = KsonReader.ReadFile(path);
        string before = KsonWriter.Write(c);
        long at = new TimingMap(c.Beat).MeasureStart(8);
        TimeShift.InsertBlank(c, at, 480)!.Command.Execute(c);
        TimeShift.DeleteRange(c, at, at + 480)!.Command.Execute(c);
        Assert.That(KsonWriter.Write(c), Is.EqualTo(before));
        Assert.DoesNotThrow(() => ChartChecker.Run(c));
        Assert.That(ChartChecker.Run(c).Issues.Where(i => i.RuleId.EndsWith(".crashed")), Is.Empty);
    }

    [TestCaseSource(typeof(RealCharts), nameof(RealCharts.Cases), new object[] { ".ksh", nameof(ChartCheckRunsOnEveryLibraryKsh) })]
    public void ChartCheckRunsOnEveryLibraryKsh(string path)
    {
        RealCharts.RequireFile(path);
        var c = KshReader.ReadFile(path);
        var report = ChartChecker.Run(new ChartCheckContext(c) { SongFolder = Path.GetDirectoryName(path) });
        Assert.That(report.Issues.Where(i => i.RuleId.EndsWith(".crashed")), Is.Empty);
        Assert.That(report.Issues.Where(i => i.RuleId is "laser.point-order" or "laser.first-point" or "notes.negative"), Is.Empty, "the reader never produces these");
        TestContext.Out.WriteLine($"{Path.GetFileName(Path.GetDirectoryName(path))}/{Path.GetFileName(path)}: {report.Summary}");
    }

    // ---- slam sounds, hold effects, laser swap -------------------------------------------------------------------

    [Test]
    public void SlamSoundSetClearAndUndo()
    {
        var c = chart();
        c.Audio.KeySound.LaserSlamEvents["slam_up"] = new List<long> { 100, 200 };
        var stack = new CommandStack(c);
        stack.Execute(SlamSounds.Set(c, new long[] { 200, 300 }, "slam_mute")!);
        Assert.That(c.Audio.KeySound.LaserSlamEvents["slam_up"], Is.EqualTo(new long[] { 100 }));
        Assert.That(c.Audio.KeySound.LaserSlamEvents["slam_mute"], Is.EqualTo(new long[] { 200, 300 }));
        stack.Execute(SlamSounds.Set(c, new long[] { 100 }, null)!);
        Assert.That(c.Audio.KeySound.LaserSlamEvents.ContainsKey("slam_up"), Is.False, "an emptied list goes");
        Assert.That(SlamSounds.Set(c, new long[] { 300 }, "slam_mute"), Is.Null, "no change");
        stack.Undo();
        stack.Undo();
        Assert.That(c.Audio.KeySound.LaserSlamEvents.Keys, Is.EquivalentTo(new[] { "slam_up" }));
        Assert.That(c.Audio.KeySound.LaserSlamEvents["slam_up"], Is.EqualTo(new long[] { 100, 200 }));
        Assert.That(SlamSounds.FromLabel(SlamSounds.Label("slam_swing")), Is.EqualTo("slam_swing"));
        Assert.That(SlamSounds.FromLabel("Auto"), Is.Null);
    }

    [Test]
    public void HoldEffectCycleAndAssign()
    {
        var c = chart();
        c.Audio.FxEffects.Defs.Add(EffectCatalog.NewDef("mine", "gate"));
        var hold = new Note(0, 480);
        var chip = new Note(960);
        c.Notes.Fx[0].Add(hold);
        c.Notes.Fx[0].Add(chip);
        Assert.That(HoldEffects.Next(c, hold), Is.EqualTo("mine"), "definitions come first");
        var refs = new[] { new NoteRef(NoteKind.Fx, 0, hold), new NoteRef(NoteKind.Fx, 0, chip) };
        var stack = new CommandStack(c);
        stack.Execute(HoldEffects.Assign(refs, "mine")!);
        Assert.That(HoldEffects.Of(hold), Is.EqualTo("mine"));
        Assert.That(chip.Effects, Is.Empty, "chips are skipped");
        Assert.That(HoldEffects.Next(c, hold), Is.EqualTo(EffectCatalog.AvailableNames(c, false)[1]));
        Assert.That(HoldEffects.Next(c, hold, -1), Is.Null, "back to none");
        Assert.That(HoldEffects.Assign(refs, "mine"), Is.Null, "no change");
        stack.Undo();
        Assert.That(hold.Effects, Is.Empty);
    }

    [Test]
    public void SwapLaserColourKeepsValues()
    {
        var c = chart();
        var s = section(0, (0, 0.2, 0.8), (480, 0.3, null));
        c.Notes.Lasers[0].Add(s);
        var sel = new Selection();
        sel.Add(new LaserPointRef(0, s, s.Points[0]));
        var cmd = LaserSwap.Swap(c, sel)!;
        cmd.Execute(c);
        Assert.That(c.Notes.Lasers[0], Is.Empty);
        var moved = c.Notes.Lasers[1].Single();
        Assert.That(moved.Points.Select(p => (p.V, p.Vf)), Is.EqualTo(new[] { (0.2, (double?)0.8), (0.3, (double?)null) }));
        cmd.Undo(c);
        Assert.That(c.Notes.Lasers[0].Single(), Is.SameAs(s));

        c.Notes.Lasers[1].Add(section(100, (0, 0, null), (100, 1, null)));
        Assert.That(LaserSwap.Swap(c, sel), Is.Null, "the other lane is taken there");
    }

    // ---- inspector ---------------------------------------------------------------------------------------------

    [Test]
    public void InspectorValuesAndEdits()
    {
        var c = chart();
        var a = new Note(0, 240);
        var b = new Note(480, 240);
        var other = new Note(960);
        c.Notes.Bt[0].Add(a);
        c.Notes.Bt[1].Add(b);
        c.Notes.Bt[0].Add(other);
        var refs = new[] { new NoteRef(NoteKind.Bt, 0, a), new NoteRef(NoteKind.Bt, 1, b) };

        var s = NoteInspector.Of(refs);
        Assert.That(s.Start.Mixed, Is.True);
        Assert.That(s.Length.Value, Is.EqualTo(240));
        Assert.That(s.Lane.Mixed, Is.True);

        NoteInspector.SetStart(c, refs, 120)!.Execute(c);
        Assert.That((a.Y, b.Y), Is.EqualTo((120L, 600L)), "the group moves, spacing kept");
        Assert.That(NoteInspector.SetLength(c, refs, 960), Is.Null, "A would run into the chip at 960");
        NoteInspector.SetLength(c, refs, 0)!.Execute(c);
        Assert.That(a.IsHold || b.IsHold, Is.False);
        Assert.That(NoteInspector.SetLane(c, refs, 3), Is.Not.Null);
        Assert.That(NoteInspector.SetLane(c, new[] { refs[1] }, 0), Is.Not.Null, "lane A is free at 600");
    }

    [Test]
    public void InspectorText()
    {
        var t = new TimingMap(chart().Beat);
        Assert.That(Editing.InspectorText.ParsePosition("1440", t), Is.EqualTo(1440));
        Assert.That(Editing.InspectorText.ParsePosition("2:3", t), Is.EqualTo(960 + 480));
        Assert.That(Editing.InspectorText.ParsePosition("2:3+10", t), Is.EqualTo(960 + 490));
        Assert.That(Editing.InspectorText.ParsePosition("2:5", t), Is.Null, "4/4 has 4 beats");
        Assert.That(Editing.InspectorText.Position(t, 1450), Is.EqualTo("2:3+10"));
        Assert.That(Editing.InspectorText.ParseLength("1/4"), Is.EqualTo(60));
        Assert.That(Editing.InspectorText.ParseLength("1.5b"), Is.EqualTo(360));
        Assert.That(Editing.InspectorText.ParseLength(Editing.InspectorText.Length(360)), Is.EqualTo(360), "the shown text parses back");
        Assert.That(Editing.InspectorText.ParseLength("x"), Is.Null);
    }

    // ---- gauges ------------------------------------------------------------------------------------------------

    private static JudgementEvent error(bool chip) => new(new ScoreObject { Kind = chip ? ScoreObjectKind.Chip : ScoreObjectKind.HoldTick }, HitResult.Error, double.NaN, 0);

    [Test]
    public void HardGaugeFailsAndEasyHalvesDamage()
    {
        var hard = new ScoreProcessor(100, 0, 0, gauge: GaugeType.Hard);
        Assert.That(hard.Gauge, Is.EqualTo(1));
        hard.Apply(error(true));
        Assert.That(hard.Gauge, Is.EqualTo(0.91).Within(1e-12), "−9 % above 30 %");
        while (!hard.Failed) hard.Apply(error(true));
        Assert.That(hard.Gauge, Is.Zero);
        Assert.That(hard.Cleared, Is.False);
        Assert.That(hard.FailedAtJudged, Is.GreaterThan(11), "damage shrinks below 30 %");

        var easy = new ScoreProcessor(100, 0, 0, gauge: GaugeType.Easy);
        var normal = new ScoreProcessor(100, 0, 0);
        for (int i = 0; i < 10; i++)
        {
            var hit = new JudgementEvent(new ScoreObject { Kind = ScoreObjectKind.Chip }, HitResult.Critical, 0, 0);
            easy.Apply(hit);
            normal.Apply(hit);
        }

        double e0 = easy.Gauge, n0 = normal.Gauge;
        easy.Apply(error(true));
        normal.Apply(error(true));
        Assert.That(e0 - easy.Gauge, Is.EqualTo((n0 - normal.Gauge) / 2).Within(1e-12));
        Assert.That(new ScoreProcessor(100, 0, 0, gauge: GaugeType.Hard).ClearLine, Is.Zero);
    }
}
