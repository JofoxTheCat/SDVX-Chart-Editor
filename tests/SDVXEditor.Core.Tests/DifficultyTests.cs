using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.LevelEstimation;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class DifficultyTests
{
    /// <summary>
    /// Synthetic stream chart: <paramref name="measures"/> 4/4 measures at <paramref name="bpm"/>, one
    /// BT note every 960/<paramref name="perMeasure"/> pulses cycling A-B-C-D, an FX chip on every
    /// beat if requested, and a left laser sweeping 0↔1 each measure with a slam every second measure.
    /// </summary>
    private static Chart Stream(int perMeasure, double bpm = 160, int measures = 48, bool fx = false, bool lasers = true, int slot = 2)
    {
        var c = new Chart();
        c.Meta.DifficultySlot = slot;
        c.Beat.Bpm.Add(new BpmChange(0, bpm));
        c.Beat.TimeSignatures.Add(new TimeSignature(0, 4, 4));
        long step = 960 / perMeasure;
        int k = 0;
        for (long y = 960; y < 960L * (measures + 1); y += step, k++)
            c.Notes.Bt[k % 4].Add(new Note(y));
        if (fx)
            for (long y = 960; y < 960L * (measures + 1); y += 240)
                c.Notes.Fx[(int)(y / 240 % 2)].Add(new Note(y));
        if (lasers)
            for (int m = 1; m <= measures; m += 2)
            {
                var s = new LaserSection(960L * m);
                s.Points.Add(new LaserPoint(0, 0));
                s.Points.Add(new LaserPoint(480, 1));
                s.Points.Add(new LaserPoint(960, 1, 0)); // slam back
                s.Points.Add(new LaserPoint(1440, 0.5));
                c.Notes.Lasers[0].Add(s);
            }
        c.Notes.SortAll();
        return c;
    }

    [Test]
    public void FeatureExtractionIsDeterministic()
    {
        var a = DifficultyFeatures.Extract(Stream(16, fx: true));
        var b = DifficultyFeatures.Extract(Stream(16, fx: true));
        Assert.That(a.Features, Is.EqualTo(b.Features));
        Assert.That(a.Sections, Is.EqualTo(b.Sections));
        Assert.That(a.Features.Select(kv => kv.Key), Is.EqualTo(DifficultyFeatures.FeatureNames));
    }

    [Test]
    public void BasicFeatureValues()
    {
        // 4 chips, one per beat at 120 BPM → events at 0.0, 0.5, 1.0, 1.5 s.
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        for (int i = 0; i < 4; i++) c.Notes.Bt[i].Add(new Note(i * 240L));
        var f = DifficultyFeatures.Extract(c);
        Assert.That(f["n_notes"], Is.EqualTo(4));
        Assert.That(f["n_events"], Is.EqualTo(4));
        Assert.That(f["dur"], Is.EqualTo(1.5).Within(1e-9));
        Assert.That(f["dens"], Is.EqualTo(4 / 1.5).Within(1e-9));
        Assert.That(f["pk1"], Is.EqualTo(3)); // (t-1, t] holds 3 events at t = 1.0 and 1.5
        Assert.That(f["slams"], Is.Zero);
        Assert.That(f["laser_frac"], Is.Zero);
    }

    [Test]
    public void ChordsMergeAndSlamsCount()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(0));
        c.Notes.Bt[3].Add(new Note(0));
        var s = new LaserSection(0);
        s.Points.Add(new LaserPoint(0, 0, 1)); // slam at the section start
        s.Points.Add(new LaserPoint(480, 0));
        c.Notes.Lasers[1].Add(s);
        c.Notes.Bt[1].Add(new Note(960));
        var f = DifficultyFeatures.Extract(c);
        Assert.That(f["n_events"], Is.EqualTo(2), "BT A + BT D + laser start/slam at 0 are one chord");
        Assert.That(f["slams"], Is.EqualTo(1));
        Assert.That(f["travel"], Is.EqualTo(1.5 + 1.0).Within(1e-9), "slam travel ×1.5, then 1 → 0");
        Assert.That(f["ch2"], Is.EqualTo(0.5));
    }

    [Test]
    public void StrainSectionsCoverTheChart()
    {
        var f = DifficultyFeatures.Extract(Stream(8));
        Assert.That(f.Sections, Is.Not.Empty);
        Assert.That(f.Sections.Zip(f.Sections.Skip(1)).All(p => p.First.StartMs < p.Second.StartMs));
        Assert.That(f.Sections.All(s => s.Total >= 0 && s.Left >= 0 && s.Right >= 0 && s.Knobs >= 0));
        Assert.That(f.Sections.Max(s => s.Total), Is.GreaterThan(0));
        Assert.That(f.Sections.Any(s => s.Knobs > 0), "the laser shows up in the knob line");
        Assert.That(f.Sections[^1].StartMs, Is.LessThanOrEqualTo(f.DurationSeconds * 1000 + 2000));
    }

    [Test]
    public void EmbeddedCoefficientsLoad()
    {
        var c = DifficultyCoefficients.LoadEmbedded(out string? error);
        Assert.That(c, Is.Not.Null, error);
        Assert.That(c!.SlotAware, Is.Not.Null);
        Assert.That(c.SlotFree, Is.Not.Null);
        Assert.That(c.TrainingCharts, Is.GreaterThan(1000));
        Assert.That(DifficultyEstimator.Default.IsFallback, Is.False, DifficultyEstimator.Default.FallbackReason);
        // every term refers to a feature the extractor produces (or a slot indicator)
        var known = DifficultyFeatures.FeatureNames.Concat(new[] { "slot_1", "slot_2", "slot_3", "slot_4" }).ToHashSet();
        foreach (var m in new[] { c.SlotAware!, c.SlotFree! })
        {
            Assert.That(m.Terms.Select(t => t.Name).Where(n => !known.Contains(n)), Is.Empty);
            Assert.That(m.IsotonicX.Zip(m.IsotonicX.Skip(1)).All(p => p.First < p.Second), "isotonic x ascending");
            Assert.That(m.IsotonicY.Zip(m.IsotonicY.Skip(1)).All(p => p.First <= p.Second), "isotonic y monotone");
        }
        // JSON round trip
        var again = DifficultyCoefficients.Parse(c.ToJson());
        Assert.That(again.SlotAware!.Terms.Count, Is.EqualTo(c.SlotAware!.Terms.Count));
    }

    [Test]
    public void FallbackModelStillEstimates()
    {
        var est = new DifficultyEstimator(DifficultyCoefficients.Fallback, isFallback: true, fallbackReason: "test");
        var r = est.Estimate(Stream(16));
        Assert.That(r.EstimatedLevel, Is.Not.Null);
        Assert.That(r.UsedFallback && r.LowConfidence);
        Assert.That(r.ModelName, Is.EqualTo("fallback"));
        Assert.That(r.Band, Is.GreaterThanOrEqualTo(2.0));
    }

    [Test]
    public void BrokenCoefficientFileIsRejected()
    {
        Assert.Throws<FormatException>(() => DifficultyCoefficients.Parse("{\"schema\": 99, \"slot_free\": {}}"));
        Assert.Throws<FormatException>(() => DifficultyCoefficients.Parse("{\"schema\": 1}"));
    }

    [Test]
    public void EmptyChartHasNoEstimate()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        var r = DifficultyEstimator.Default.Estimate(c);
        Assert.That(r.EstimatedLevel, Is.Null);
        Assert.That(r.LowConfidence);
        Assert.That(r.ToString(), Is.EqualTo("Est. –"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DenserChartEstimatesHigher(bool useSlot)
    {
        var o = new DifficultyEstimateOptions { UseDifficultySlot = useSlot };
        var est = DifficultyEstimator.Default;
        double l4 = est.Estimate(Stream(4), o).EstimatedLevel!.Value;
        double l8 = est.Estimate(Stream(8), o).EstimatedLevel!.Value;
        double l16 = est.Estimate(Stream(16), o).EstimatedLevel!.Value;
        double l16fx = est.Estimate(Stream(16, fx: true), o).EstimatedLevel!.Value;
        TestContext.Out.WriteLine($"slot={useSlot}: 4ths {l4}  8ths {l8}  16ths {l16}  16ths+fx {l16fx}");
        Assert.That(l8, Is.GreaterThan(l4));
        Assert.That(l16, Is.GreaterThan(l8));
        Assert.That(l16fx, Is.GreaterThanOrEqualTo(l16));
    }

    [Test]
    public void SlowerTempoEstimatesLower()
    {
        var est = DifficultyEstimator.Default;
        double fast = est.Estimate(Stream(16, bpm: 180)).EstimatedLevel!.Value;
        double slow = est.Estimate(Stream(16, bpm: 90)).EstimatedLevel!.Value;
        TestContext.Out.WriteLine($"180 BPM {fast}  90 BPM {slow}");
        Assert.That(slow, Is.LessThan(fast));
    }

    [Test]
    public void ResultCarriesBandGraphAndContributors()
    {
        var r = DifficultyEstimator.Default.Estimate(Stream(16, fx: true));
        TestContext.Out.WriteLine(r + "  " + string.Join(", ", r.TopContributors.Select(c => $"{c.Feature} {c.Contribution:+0.00;-0.00}")));
        Assert.That(r.EstimatedLevel, Is.InRange(1.0, 21.0));
        Assert.That(r.EstimatedLevel!.Value, Is.EqualTo(Math.Round(r.EstimatedLevel.Value, 1)));
        Assert.That(r.Band, Is.GreaterThan(0));
        Assert.That(r.Low, Is.LessThan(r.High));
        Assert.That(r.PerSectionStrain, Is.Not.Empty);
        Assert.That(r.TopContributors, Is.Not.Empty);
        Assert.That(r.ModelName, Is.EqualTo("slot_aware"));
    }

    /// <summary>Fan charts with author-declared levels; loose bound (author scales differ, MAE ≈ 1).</summary>
    [Test]
    public void NauticaChartsEstimateWithinLooseBound()
    {
        string root = RealCharts.RequireRoot();
        var errors = new List<double>();
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            foreach (var c in SongFolder.Load(dir).Charts)
            {
                if (c.Chart.Meta.Level <= 0) continue;
                var r = DifficultyEstimator.Default.Estimate(c.Chart);
                double err = r.EstimatedLevel!.Value - c.Chart.Meta.Level;
                TestContext.Out.WriteLine($"{Path.GetFileName(dir)}/{Path.GetFileName(c.FilePath)}: declared {c.Chart.Meta.Level} → {r}");
                Assert.That(Math.Abs(err), Is.LessThanOrEqualTo(4.0), c.FilePath);
                errors.Add(Math.Abs(err));
            }
        }
        if (errors.Count == 0) Assert.Ignore("no labelled charts");
        TestContext.Out.WriteLine($"n={errors.Count} MAE {errors.Average():0.00}");
        Assert.That(errors.Average(), Is.LessThan(1.5));
    }

    /// <summary>
    /// The .ksh and .kson of the same chart give (nearly) the same estimate. ABYSS MOTION's ksh used to
    /// read 29 densely sampled laser points as slams (18.5 vs 18.9); the ksh reader no longer does
    /// (KshReader.IsSlam), and both now estimate 18.9.
    /// </summary>
    [Test]
    public void KshAndKsonTwinsAgree()
    {
        string root = RealCharts.RequireRoot();
        string ksh = Path.Combine(root, "ABYSS MOTION 192.333", "ABYSS MOTION 192.333.ksh");
        string kson = Path.ChangeExtension(ksh, ".kson");
        if (!File.Exists(ksh) || !File.Exists(kson)) Assert.Ignore("twin charts not found");
        var ca = Formats.Ksh.KshReader.ReadFile(ksh);
        var cb = Formats.Kson.KsonReader.ReadFile(kson);
        var a = DifficultyEstimator.Default.Estimate(ca);
        var b = DifficultyEstimator.Default.Estimate(cb);
        TestContext.Out.WriteLine($"ksh {a}  kson {b}");
        foreach (var (name, va) in a.Features)
            if (Math.Abs(va - b.Features[name]) > 1e-6 * Math.Max(1, Math.Abs(va)))
                TestContext.Out.WriteLine($"  {name}: ksh {va:0.####} kson {b.Features[name]:0.####}");
        Assert.That(a.EstimatedLevel!.Value, Is.EqualTo(b.EstimatedLevel!.Value).Within(0.1));
    }
}
