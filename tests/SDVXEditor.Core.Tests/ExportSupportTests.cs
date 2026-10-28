using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;

namespace SDVXEditor.Core.Tests;

/// <summary>The VOX column of the export matrix follows the ksh2vox version (0.17.0 maps camera, tilt, effects, filters, VOX extras).</summary>
[TestFixture]
public class ExportSupportTests
{
    [TestCase(null)]
    [TestCase("0.12.0")]
    [TestCase("0.16.0")]
    [TestCase("0.16.9")]
    public void BelowMappingVersionEveryVoxCellIsDropped(string? version)
    {
        Assert.That(ExportSupport.MapsToVox(version), Is.False);
        Assert.That(ExportSupport.For(version), Is.SameAs(ExportSupport.Legacy));
        Assert.That(ExportSupport.For(version).All(f => f.Vox == Support.No), Is.True, "ksh2vox < 0.17.0 drops every camera/background/effect setting");
        Assert.That(ExportSupport.Badge("zoom_top", version), Is.EqualTo("kson ✓  ksh ✓  vox ✗"));
    }

    [TestCase("0.17.0")]
    [TestCase("0.17.1")]
    [TestCase("0.18")]
    [TestCase("0.18.9")]
    [TestCase("0.17.0rc1")]
    public void FromMappingVersionTheNewValuesApply(string version)
    {
        Assert.That(ExportSupport.MapsToVox(version), Is.True);
        Assert.That(ExportSupport.MapsSpinsToVox(version), Is.False);
        Assert.That(ExportSupport.For(version), Is.SameAs(ExportSupport.Mapped));
    }

    [TestCase("0.19.0")]
    [TestCase("0.19")]
    [TestCase("0.19.0rc1")]
    [TestCase("0.19.9")]
    public void FromSpinVersionSpinsAndTiltBlocksApply(string version)
    {
        Assert.That(ExportSupport.MapsToVox(version), Is.True);
        Assert.That(ExportSupport.MapsSpinsToVox(version), Is.True);
        Assert.That(ExportSupport.MapsEffectsToVox(version), Is.False);
        Assert.That(ExportSupport.For(version), Is.SameAs(ExportSupport.MappedSpins));
    }

    [TestCase("0.20.0")]
    [TestCase("0.20.1")]
    [TestCase("1.0.0")]
    public void FromEffectsVersionGameEffectsApply(string version)
    {
        Assert.That(ExportSupport.MapsEffectsToVox(version), Is.True);
        Assert.That(ExportSupport.For(version), Is.SameAs(ExportSupport.MappedEffects));
    }

    /// <summary>ksh2vox 0.20.0 writes the game effects; every other row keeps its 0.19 value.</summary>
    [Test]
    public void EffectsVersionChangesOnlyTheGameEffects()
    {
        var changed = new[] { "post_effect", "lane_y", "lane_colour", "bil_rotz", "morphing2", "bar_lines", "bpm_option", "special_n", "note_scripts" };
        Assert.That(ExportSupport.MappedEffects.Count, Is.EqualTo(ExportSupport.MappedSpins.Count));
        for (int i = 0; i < ExportSupport.MappedSpins.Count; i++)
        {
            var a = ExportSupport.MappedSpins[i];
            var b = ExportSupport.MappedEffects[i];
            Assert.That((b.Id, b.Label, b.Kson, b.Ksh, b.Note), Is.EqualTo((a.Id, a.Label, a.Kson, a.Ksh, a.Note)), a.Id);
            if (!changed.Contains(a.Id)) Assert.That((b.Vox, b.VoxNote), Is.EqualTo((a.Vox, a.VoxNote)), a.Id);
            else Assert.That(b.VoxNote, Is.Not.EqualTo(a.VoxNote), a.Id);
        }

        const string v = "0.20.0";
        foreach (string id in changed.Skip(1)) Assert.That(ExportSupport.Get(id, "0.19.0").Vox, Is.EqualTo(Support.No), id);
        Assert.That(ExportSupport.Get("lane_y", v).Vox, Is.EqualTo(Support.Yes));
        Assert.That(ExportSupport.Get("note_scripts", v).Vox, Is.EqualTo(Support.Yes));
        Assert.That(ExportSupport.Get("morphing2", v).Vox, Is.EqualTo(Support.Partial));
        Assert.That(ExportSupport.Get("post_effect", v).VoxNote, Does.Contain("bg_no 0"));
        Assert.That(ExportSupport.Badge("lane_y", v), Is.EqualTo("kson ✓  ksh ✓  vox ✓"));
        Assert.That(ExportSupport.Badge("note_scripts", v), Is.EqualTo("kson ✓  ksh ~  vox ✓"));
    }

    /// <summary>ksh2vox 0.19.0 writes spins (laser field 4) and the Tilt block marker; every other row keeps its 0.17 value.</summary>
    [Test]
    public void SpinVersionChangesOnlySpinSwingAndManualTilt()
    {
        var changed = new[] { "spin", "swing", "tilt_manual" };
        Assert.That(ExportSupport.MappedSpins.Count, Is.EqualTo(ExportSupport.Mapped.Count));
        for (int i = 0; i < ExportSupport.Mapped.Count; i++)
        {
            var a = ExportSupport.Mapped[i];
            var b = ExportSupport.MappedSpins[i];
            Assert.That((b.Id, b.Label, b.Kson, b.Ksh, b.Note), Is.EqualTo((a.Id, a.Label, a.Kson, a.Ksh, a.Note)), a.Id);
            if (!changed.Contains(a.Id))
                Assert.That((b.Vox, b.VoxNote), Is.EqualTo((a.Vox, a.VoxNote)), a.Id);
            else
                Assert.That(b.VoxNote, Is.Not.EqualTo(a.VoxNote), a.Id);
        }

        const string v = "0.19.0";
        Assert.That(ExportSupport.Get("spin", v).Vox, Is.EqualTo(Support.Partial));
        Assert.That(ExportSupport.Get("swing", v).Vox, Is.EqualTo(Support.Partial));
        Assert.That(ExportSupport.Get("tilt_manual", v).Vox, Is.EqualTo(Support.Partial));
        Assert.That(ExportSupport.Get("zoom_side", v).Vox, Is.EqualTo(Support.No));
        Assert.That(ExportSupport.Get("center_split", v).Vox, Is.EqualTo(Support.No));
        Assert.That(ExportSupport.Get("rotation_deg", v).Vox, Is.EqualTo(Support.No));
        Assert.That(ExportSupport.Badge("spin", v), Is.EqualTo("kson ✓  ksh ~  vox ~"));
        Assert.That(ExportSupport.Badge("spin", "0.18.0"), Is.EqualTo("kson ✓  ksh ~  vox ✗"));

        Assert.That(ExportSupport.Get("spin", v).VoxNote, Does.Contain("half spin → 5"));
        Assert.That(ExportSupport.Get("spin", v).VoxNote, Does.Contain("without a slam is dropped"));
        Assert.That(ExportSupport.Get("swing", v).VoxNote, Does.Contain("type 5"));
        Assert.That(ExportSupport.Get("tilt_manual", v).VoxNote, Does.Contain("±1 clamped"));
        Assert.That(ExportSupport.Get("tilt_manual", v).VoxNote, Does.Contain("field 7"));
    }

    [Test]
    public void MappedValuesMatchTheConverterChangelog()
    {
        const string v = "0.17.0";
        var expected = new Dictionary<string, Support>
        {
            ["zoom_top"] = Support.Partial,
            ["zoom_bottom"] = Support.Partial,
            ["zoom_side"] = Support.No,
            ["center_split"] = Support.No,
            ["rotation_deg"] = Support.No,
            ["tilt_auto"] = Support.Partial,
            ["tilt_manual"] = Support.Partial,
            ["spin"] = Support.No,
            ["swing"] = Support.No,
            ["bg_image"] = Support.No,
            ["bg_layer"] = Support.No,
            ["bg_movie"] = Support.No,
            ["bg_no"] = Support.Yes,
            ["post_effect"] = Support.Yes,
            ["sp_controller"] = Support.Yes,
            ["scroll_speed"] = Support.Partial,
            ["stop"] = Support.Yes,
            ["fx_def"] = Support.Partial,
            ["fx_assign"] = Support.Partial,
            ["fx_param_change"] = Support.No,
            ["laser_filter"] = Support.Partial,
            ["filter_gain"] = Support.No,
            ["filter_delay"] = Support.No,
            ["slam_volume"] = Support.No,
            ["chip_sound"] = Support.No,
            ["lane_y"] = Support.No,
            ["lane_colour"] = Support.No,
            ["bil_rotz"] = Support.No,
            ["morphing2"] = Support.No,
            ["bar_lines"] = Support.No,
            ["bpm_option"] = Support.No,
            ["special_n"] = Support.No,
            ["note_scripts"] = Support.No,
        };

        Assert.That(ExportSupport.Mapped.Select(f => f.Id), Is.EquivalentTo(expected.Keys), "every row has a 0.17.0 value");
        foreach (var (id, s) in expected)
            Assert.That(ExportSupport.Get(id, v).Vox, Is.EqualTo(s), id);

        Assert.That(ExportSupport.Badge("zoom_top", v), Is.EqualTo("kson ✓  ksh ✓  vox ~"));
        Assert.That(ExportSupport.Badge("bg_no", v), Is.EqualTo("kson ✓  ksh ✓  vox ✓"));
    }

    [Test]
    public void OnlyTheVoxColumnDependsOnTheVersion()
    {
        for (int i = 0; i < ExportSupport.Legacy.Count; i++)
        {
            var a = ExportSupport.Legacy[i];
            var b = ExportSupport.Mapped[i];
            Assert.That((b.Id, b.Label, b.Kson, b.Ksh, b.Note), Is.EqualTo((a.Id, a.Label, a.Kson, a.Ksh, a.Note)), a.Id);
        }
    }

    [Test]
    public void EveryCellHasATooltipAndPartialOnesSayWhatIsLost()
    {
        foreach (var f in ExportSupport.Legacy.Concat(ExportSupport.Mapped).Concat(ExportSupport.MappedSpins).Concat(ExportSupport.MappedEffects))
            Assert.That(f.VoxNote, Is.Not.Empty, f.Id);

        Assert.That(ExportSupport.Get("zoom_top", "0.17.0").VoxNote, Does.Contain("±100 clamped"));
        Assert.That(ExportSupport.Get("zoom_bottom", "0.17.0").VoxNote, Does.Contain("±100 clamped"));
        Assert.That(ExportSupport.Get("tilt_manual", "0.17.0").VoxNote, Does.Contain("±1 clamped"));
        Assert.That(ExportSupport.Get("tilt_auto", "0.17.0").VoxNote, Does.Contain("zero falls back to normal"));
        Assert.That(ExportSupport.Get("laser_filter", "0.17.0").VoxNote, Does.Contain("inside a section"));
        Assert.That(ExportSupport.Get("fx_assign", "0.17.0").VoxNote, Does.Contain("2..13"));
        Assert.That(ExportSupport.Get("scroll_speed", "0.17.0").VoxNote, Does.Contain("negative speeds become 0"));
    }

    /// <summary>The ksh claims of the two timing rows: scroll speed and stops survive the real ksh writer and reader.</summary>
    [Test]
    public void KshKeepsScrollSpeedAndStops()
    {
        var chart = new Chart();
        chart.Beat.Bpm.Add(new BpmChange(0, 120));
        chart.Notes.Bt[0].Add(new Note(0));
        chart.Notes.Bt[0].Add(new Note(7680));
        chart.Beat.ScrollSpeed.Add(new GraphPoint(960, 1.5));
        chart.Beat.ScrollSpeed.Add(new GraphPoint(1920, -0.5));
        chart.Beat.Stops.Add(new Stop(3840, 480));

        var back = KshReader.Read(KshWriter.Write(chart));
        ModelSnapshot.AssertEqual(chart.Beat.ScrollSpeed, back.Beat.ScrollSpeed, "ksh scroll_speed");
        ModelSnapshot.AssertEqual(chart.Beat.Stops, back.Beat.Stops, "ksh stop");
        Assert.That(ExportSupport.Get("scroll_speed").Ksh, Is.EqualTo(Support.Yes));
        Assert.That(ExportSupport.Get("stop").Ksh, Is.EqualTo(Support.Yes));
    }

    [Test]
    public void UnknownIdThrows() => Assert.Throws<KeyNotFoundException>(() => ExportSupport.Get("nope", "0.17.0"));
}
