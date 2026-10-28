using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.LevelEstimation;
using SDVXEditor.Core.Patterns;
using SDVXEditor.Core.Radar;

namespace SDVXEditor.Core.Tests;

/// <summary>Pattern analysis (Core/Patterns), the fitted radar model and the pattern-aware level model.</summary>
[TestFixture]
public class PatternTests
{
    private static Chart chart(double bpm = 120)
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, bpm));
        c.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        return c;
    }

    /// <summary>
    /// A busy synthetic chart: a 16th stream over BT A–D, FX chips on beats, a left laser zig-zag with slams, a right
    /// laser held still, BT holds.
    /// </summary>
    private static Chart busy(int measures = 32, double bpm = 160, int perMeasure = 16)
    {
        var c = chart(bpm);
        long step = 960 / perMeasure;
        int k = 0;
        for (long y = 960; y < 960L * (measures + 1); y += step, k++)
            c.Notes.Bt[k % 4].Add(new Note(y));
        for (long y = 960; y < 960L * (measures + 1); y += 240)
            c.Notes.Fx[(int)(y / 240 % 2)].Add(new Note(y));
        for (int m = 1; m <= measures; m += 2)
        {
            var s = new LaserSection(960L * m);
            s.Points.Add(new LaserPoint(0, 0));
            s.Points.Add(new LaserPoint(480, 1));
            s.Points.Add(new LaserPoint(960, 1, 0));
            s.Points.Add(new LaserPoint(1440, 0.5));
            c.Notes.Lasers[0].Add(s);
            c.Notes.Lasers[1].Add(new LaserSection(960L * m + 480, new[] { new LaserPoint(0, 1), new LaserPoint(480, 1) }));
        }
        c.EndPulse = 960L * (measures + 2);
        c.Notes.SortAll();
        return c;
    }

    // ---------------------------------------------------------------- features

    [Test]
    public void AnalysisIsDeterministicAndOrdered()
    {
        var a = PatternAnalysis.Analyze(busy());
        var b = PatternAnalysis.Analyze(busy());
        Assert.That(a.Features, Is.EqualTo(b.Features));
        Assert.That(a.Features.Select(kv => kv.Key), Is.EqualTo(PatternAnalysis.FeatureNames));
        Assert.That(PatternAnalysis.FeatureNames, Is.Unique);
        Assert.That(a.Features.All(kv => double.IsFinite(kv.Value)));
    }

    [Test]
    public void VolumeAndPeakValues()
    {
        // 120 BPM: a quarter is 0.5 s. 8 chips on BT A, one per quarter (0 .. 3.5 s), chart end at 4 s.
        var c = chart();
        for (int i = 0; i < 8; i++) c.Notes.Bt[0].Add(new Note(i * 240L));
        c.Notes.Bt[1].Add(new Note(0, 480)); // a one-second hold
        c.EndPulse = 1920;
        var f = PatternAnalysis.Analyze(c);
        Assert.That(f["dur"], Is.EqualTo(4.0).Within(1e-9));
        Assert.That(f["chips_ps"], Is.EqualTo(8 / 4.0).Within(1e-9));
        Assert.That(f["holds_ps"], Is.EqualTo(1 / 4.0).Within(1e-9));
        Assert.That(f["hold_time_frac"], Is.EqualTo(1.0 / 4.0).Within(1e-9));
        Assert.That(f["chord_ps"], Is.EqualTo(8 / 4.0).Within(1e-9), "the hold start shares the first chip's pulse");
        Assert.That(f["chord2_frac"], Is.EqualTo(1 / 8.0).Within(1e-9));
        // (t − 1.25, t]: chords at 0.5, 1.0, 1.5 → 3 → /1.25
        Assert.That(f["peak_chords_1.25"], Is.EqualTo(3 / 1.25).Within(1e-9));
        Assert.That(f["jack_ps"], Is.Zero, "0.5 s apart is no jack");
    }

    [Test]
    public void JacksTrillsAndGrid()
    {
        var c = chart(150); // a 16th = 0.1 s
        for (int i = 0; i < 8; i++) c.Notes.Bt[0].Add(new Note(i * 60L));          // jack on BT A
        for (int i = 0; i < 8; i++) c.Notes.Bt[2 + i % 2].Add(new Note(1920 + i * 60L)); // trill C-D
        c.Notes.Bt[1].Add(new Note(3840 + 40));                                     // a 1/24 note
        c.EndPulse = 4800;
        c.Notes.SortAll();
        var f = PatternAnalysis.Analyze(c);
        double dur = f["dur"];
        Assert.That(f["jack_outer_ps"] * dur, Is.EqualTo(7).Within(1e-9));
        Assert.That(f["trill_ps"] * dur, Is.EqualTo(6).Within(1e-9), "C D C D …: every note from the third on");
        Assert.That(f["triplet_frac"], Is.EqualTo(1 / 17.0).Within(1e-9));
        Assert.That(f["stream_frac"], Is.GreaterThan(0.9));
    }

    [Test]
    public void KnobAndButtonRelations()
    {
        // left laser held for a measure (0–2 s, left hand busy); a note on the right side = one-hand,
        // a note on the left side = hand-trip; the laser moves in the first half only
        var c = chart();
        c.Notes.Lasers[0].Add(new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(480, 0.8), new LaserPoint(960, 0.8) }));
        c.Notes.Bt[3].Add(new Note(240));  // right hand, laser moving
        c.Notes.Bt[0].Add(new Note(720));  // left hand, laser still, laser on the far (right) half
        c.Notes.Bt[2].Add(new Note(1920)); // no laser
        c.EndPulse = 1920;
        var f = PatternAnalysis.Analyze(c);
        double dur = f["dur"];
        Assert.That(f["onehand_ps"] * dur, Is.EqualTo(1).Within(1e-9));
        Assert.That(f["onehand_moving_ps"] * dur, Is.EqualTo(1).Within(1e-9));
        Assert.That(f["handtrip_ps"] * dur, Is.EqualTo(1).Within(1e-9));
        Assert.That(f["handtrip_moving_ps"], Is.Zero);
        Assert.That(f["handtrip_far_ps"] * dur, Is.EqualTo(1).Within(1e-9));
        Assert.That(f["laser_frac"], Is.EqualTo(2.0 / 4.0).Within(1e-9));
        Assert.That(f["laser_moving_frac"], Is.EqualTo(1.0 / 4.0).Within(1e-9));
        Assert.That(f["laser_travel_ps"] * dur, Is.EqualTo(0.8).Within(1e-9));
    }

    [Test]
    public void DirectionChangesNeedTheHysteresis()
    {
        Assert.That(PatternAnalysis.DirectionChanges(new[] { 0.0, 1.0, 0.0, 1.0 }), Is.EqualTo(2));
        Assert.That(PatternAnalysis.DirectionChanges(new[] { 0.5, 0.52, 0.5, 0.52, 0.5 }), Is.Zero, "jitter below 0.05 is no reversal");
        Assert.That(PatternAnalysis.DirectionChanges(new[] { 0.0, 0.3, 0.29, 0.6, 0.0 }), Is.EqualTo(1));
    }

    [Test]
    public void HandModelSwapsSides()
    {
        var c = chart();
        c.Notes.Lasers[0].Add(new LaserSection(0, new[] { new LaserPoint(0, 0), new LaserPoint(960, 0) }));
        c.Notes.Fx[0].Add(new Note(240)); // FX L under the left knob: hand-trip by default
        c.EndPulse = 960;
        var def = PatternAnalysis.Analyze(c);
        var swapped = PatternAnalysis.Analyze(c, new HandModel("fx-swapped", new[] { 0, 0, 1, 1, 1, 0 }));
        Assert.That(def["handtrip_ps"], Is.GreaterThan(0));
        Assert.That(def["onehand_ps"], Is.Zero);
        Assert.That(swapped["onehand_ps"], Is.GreaterThan(0));
        Assert.That(swapped["handtrip_ps"], Is.Zero);
    }

    [Test]
    public void TempoAndCameraFeatures()
    {
        var c = chart(150);
        c.Beat.Bpm.Add(new BpmChange(1920, 300));
        c.Beat.Bpm.Add(new BpmChange(3840, 150));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[0].Add(new Note(5760));
        c.Camera.Body.ZoomTop.Add(new GraphPoint(0, 0));
        c.Camera.Body.ZoomTop.Add(new GraphPoint(960, 100));
        c.EndPulse = 5760;
        var f = PatternAnalysis.Analyze(c);
        Assert.That(f["bpm_changes"], Is.EqualTo(2));
        Assert.That(f["bpm_logratio"], Is.EqualTo(Math.Log(2)).Within(1e-9));
        Assert.That(f["cam_zoom_ps"] * f["dur"], Is.EqualTo(1.0).Within(1e-9), "zoom 0 → 100 is one unit of travel");
    }

    [Test]
    public void EmptyChartIsEmpty()
    {
        var f = PatternAnalysis.Analyze(chart());
        Assert.That(f.IsEmpty);
        Assert.That(f.Features.Select(kv => kv.Key), Is.EqualTo(PatternAnalysis.FeatureNames));
    }

    // ---------------------------------------------------------------- VOX camera rows

    [Test]
    public void VoxReaderReadsCameraTweensAndSpins()
    {
        string text = string.Join("\r\n", new[]
        {
            "#FORMAT VERSION", "13", "#END",
            "#BEAT INFO", "001,01,00\t4\t4", "#END",
            "#BPM INFO", "001,01,00\t120.00\t4", "#END",
            "#TRACK1",
            "001,01,00\t0.000000\t1\t0\t0\t1\t0\t0\t0\t0",
            "002,01,00\t0.000000\t0\t5\t0\t1\t0\t0\t0\t0",
            "002,01,00\t1.000000\t2\t0\t0\t1\t0\t0\t0\t0",
            "#END",
            "#SPCONTROLER",
            "001,01,00\tRealize\t3\t0\t36.12\t60.12\t110.12\t0.00",
            "001,01,00\tCAM_RotX\t2\t192\t0.00\t0.50\t0.00\t0.00",
            "002,01,00\tCAM_Radi\t2\t96\t0.00\t-0.25\t0.00\t0.00",
            "003,01,00\tTilt\t2\t48\t0.00\t1.00\t0\t0",
            "#END",
        }) + "\r\n";
        var c = VoxReader.Read(text);
        Assert.That(c.Camera.Body.ZoomTop.Select(p => (p.Y, p.V)), Is.EqualTo(new[] { (0L, 0.0), (960L, 50.0) }));
        Assert.That(c.Camera.Body.ZoomBottom.Select(p => (p.Y, p.V)), Is.EqualTo(new[] { (960L, 0.0), (1440L, 25.0) }));
        Assert.That(c.Camera.Tilt.Select(t => (t.Y, t.Value.Manual)), Is.EqualTo(new[] { (1920L, 0.0), (2160L, 1.0) }));
        Assert.That(c.Camera.Spin.Single().Y, Is.EqualTo(960));
    }

    // ---------------------------------------------------------------- model format

    [Test]
    public void AdditiveModelEvaluatesLikeTheSpec()
    {
        // intercept 1 + 2·x + log1p(y) + tree(x ≤ 0.5 ? 10 : (y ≤ 3 ? 20 : 30)), clamped to [0, 50]
        var m = new AdditiveModel
        {
            Features = new() { "x", "y" },
            Intercept = 1,
            Linear = new() { new LinearTerm { Feature = "x", Weight = 2 }, new LinearTerm { Feature = "y", Transform = "log1p", Weight = 1 } },
            Trees = new()
            {
                new RegressionTree
                {
                    Feature = new[] { 0, -1, 1, -1, -1 },
                    Threshold = new[] { 0.5, 0, 3, 0, 0 },
                    Left = new[] { 1, 0, 3, 0, 0 },
                    Right = new[] { 2, 0, 4, 0, 0 },
                    Value = new[] { 0, 10.0, 0, 20, 30 },
                },
            },
            Min = 0,
            Max = 50,
        };
        m.Validate();
        Assert.That(m.Predict(new Dictionary<string, double> { ["x"] = 0.5, ["y"] = 0 }), Is.EqualTo(1 + 1 + 0 + 10).Within(1e-12));
        Assert.That(m.Predict(new Dictionary<string, double> { ["x"] = 1, ["y"] = 3 }), Is.EqualTo(1 + 2 + Math.Log(4) + 20).Within(1e-12));
        Assert.That(m.Predict(new Dictionary<string, double> { ["x"] = 9, ["y"] = 9 }), Is.EqualTo(50), "clamped");
        var missing = new List<string>();
        m.Raw(new Dictionary<string, double> { ["x"] = 0 }, missing);
        Assert.That(missing, Does.Contain("y"));

        var bad = new RegressionTree { Feature = new[] { 0 }, Threshold = new[] { 0.0 }, Left = new[] { 0 }, Right = new[] { 0 }, Value = new[] { 0.0 } };
        Assert.Throws<FormatException>(() => bad.Validate(1), "children must point forward");
    }

    [Test]
    public void ModelJsonKeepsTreesOnOneLine()
    {
        var file = RadarModelFile.LoadEmbedded(out string? error);
        Assume.That(file, Is.Not.Null, error);
        string json = file!.ToJson();
        var again = RadarModelFile.Parse(json);
        Assert.That(again.Axes.Keys, Is.EquivalentTo(file.Axes.Keys));
        Assert.That(again.Axes["peak"].Trees.Count, Is.EqualTo(file.Axes["peak"].Trees.Count));
        Assert.That(json.Split('\n').Count(l => l.TrimStart().StartsWith("{\"f\":", StringComparison.Ordinal)),
                    Is.EqualTo(file.Axes.Values.Sum(a => a.Trees.Count)), "one tree per line");
        var x = busy();
        foreach (var (key, model) in file.Axes)
            Assert.That(again.Axes[key].Predict(PatternAnalysis.Analyze(x).ToDictionary()), Is.EqualTo(model.Predict(PatternAnalysis.Analyze(x).ToDictionary())), key);
    }

    // ---------------------------------------------------------------- radar model

    [Test]
    public void EmbeddedRadarModelLoads()
    {
        var file = RadarModelFile.LoadEmbedded(out string? error);
        Assert.That(file, Is.Not.Null, error);
        Assert.That(SkillRadarModel.Default.IsClassic, Is.False, SkillRadarModel.Default.FallbackReason);
        Assert.That(file!.TrainingCharts, Is.GreaterThan(1000));
        var known = PatternAnalysis.FeatureNames.ToHashSet();
        foreach (var axis in SkillRadar.Axes)
        {
            var m = file.Axes[SkillRadar.Key(axis)];
            Assert.That(m.Features.Where(n => !known.Contains(n)), Is.Empty, $"{axis}: tree features exist");
            Assert.That(m.Linear.Select(t => t.Feature).Where(n => !known.Contains(n)), Is.Empty, $"{axis}: linear features exist");
            Assert.That(m.Min, Is.EqualTo(0));
            Assert.That(m.Max, Is.EqualTo(200));
            Assert.That(file.Metrics.ContainsKey(SkillRadar.Key(axis)), $"{axis}: metrics recorded");
        }
    }

    [Test]
    public void RadarModelIsDeterministicAndInRange()
    {
        var a = SkillRadarModel.Default.Estimate(busy()).Radar;
        var b = SkillRadarModel.Default.Estimate(busy()).Radar;
        Assert.That(a, Is.EqualTo(b));
        foreach (var axis in SkillRadar.Axes) Assert.That(a[axis], Is.InRange(0, 200), axis.ToString());
        Assert.That(SkillRadarModel.Default.Estimate(chart()).Radar, Is.EqualTo(default(SkillRadar)), "an empty chart is all zero");
    }

    [Test]
    public void RadarModelMonotonicSanity()
    {
        var model = SkillRadarModel.Default;
        Assume.That(model.IsClassic, Is.False);
        var sparse = model.Estimate(busy(perMeasure: 4)).Radar;
        var medium = model.Estimate(busy(perMeasure: 8)).Radar;
        var dense = model.Estimate(busy(perMeasure: 16)).Radar;
        Assert.That(sparse.Notes, Is.LessThan(medium.Notes));
        Assert.That(medium.Notes, Is.LessThan(dense.Notes));
        Assert.That(sparse.Peak, Is.LessThan(dense.Peak));

        // the same buttons without any laser: no TSUMAMI movement, no knob-and-button axes
        var plain = busy();
        plain.Notes.Lasers[0].Clear();
        plain.Notes.Lasers[1].Clear();
        var noKnob = model.Estimate(plain).Radar;
        var withKnob = model.Estimate(busy()).Radar;
        Assert.That(noKnob.Tsumami, Is.LessThan(withKnob.Tsumami));
        Assert.That(noKnob.OneHand, Is.LessThanOrEqualTo(withKnob.OneHand));
        Assert.That(noKnob.HandTrip, Is.LessThanOrEqualTo(withKnob.HandTrip));
        Assert.That(noKnob.OneHand, Is.LessThan(10), "no knob, (almost) no ONE-HAND");
    }

    [Test]
    public void MissingRadarModelFallsBackToClassic()
    {
        var m = new SkillRadarModel(null, "test");
        var c = busy();
        var r = m.Estimate(c);
        Assert.That(r.IsClassic);
        Assert.That(r.Radar, Is.EqualTo(RadarCalculator.Compute(c, RadarSource.Kson)));
        Assert.That(() => RadarModelFile.Parse("{\"schema\": 1, \"axes\": {}}"), Throws.InstanceOf<FormatException>());
    }

    // ---------------------------------------------------------------- level model with pattern trees

    [Test]
    public void LevelModelTreesUseKnownFeatures()
    {
        var c = DifficultyCoefficients.LoadEmbedded(out string? error);
        Assert.That(c, Is.Not.Null, error);
        var known = DifficultyFeatures.FeatureNames
                                      .Concat(PatternAnalysis.FeatureNames.Select(n => DifficultyEstimator.PatternPrefix + n))
                                      .Concat(new[] { "slot_1", "slot_2", "slot_3", "slot_4" }).ToHashSet();
        foreach (var m in new[] { c!.SlotAware!, c.SlotFree! })
            Assert.That((m.TreeFeatures ?? new List<string>()).Where(n => !known.Contains(n)), Is.Empty);
        Assert.That(DifficultyFeatures.FeatureNames.Intersect(PatternAnalysis.FeatureNames.Select(n => DifficultyEstimator.PatternPrefix + n)), Is.Empty);
    }

    [Test]
    public void PatternAwareEstimateHasNoMissingFeatures()
    {
        var est = DifficultyEstimator.Default;
        var c = busy();
        c.Meta.DifficultySlot = 2;
        var r1 = est.Estimate(c);
        var r2 = est.Estimate(c);
        Assert.That(r1.EstimatedLevel, Is.Not.Null);
        Assert.That(r1.EstimatedLevel, Is.EqualTo(r2.EstimatedLevel));
        Assert.That(r1.LowConfidenceReasons.Where(s => s.StartsWith("features missing", StringComparison.Ordinal)), Is.Empty);
        foreach (bool slot in new[] { false, true })
            Assert.That(est.Estimate(c, new DifficultyEstimateOptions { UseDifficultySlot = slot }).EstimatedLevel, Is.InRange(1.0, 21.0));
    }

    [Test]
    public void DenserChartsEstimateHigher()
    {
        var est = DifficultyEstimator.Default;
        double level(int perMeasure) => est.Estimate(busy(perMeasure: perMeasure)).EstimatedLevel!.Value;
        Assert.That(level(4), Is.LessThan(level(8)));
        Assert.That(level(8), Is.LessThan(level(16)));
    }
}
