using System.Globalization;
using NUnit.Framework;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Library;
using SDVXEditor.Core.LevelEstimation;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// The UI's one number / time / date format (UI audit U1/U2), the song counter (B4) and the plain-words level estimate
/// (B6/U19). Run under a German culture on purpose: the formats must not follow the Windows locale.
/// </summary>
[TestFixture]
public class UiTextTests
{
    private CultureInfo before = null!;

    [SetUp]
    public void SetUp()
    {
        before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
    }

    [TearDown]
    public void TearDown() => CultureInfo.CurrentCulture = before;

    [Test]
    public void NumbersUseADotWhateverTheCulture()
    {
        Assert.That(UiFormat.Number(327.42, 2), Is.EqualTo("327.42"));
        Assert.That(UiFormat.Trim(120), Is.EqualTo("120"));
        Assert.That(UiFormat.Trim(1.5), Is.EqualTo("1.5"));
        Assert.That(UiFormat.Percent(0.05), Is.EqualTo("5.0%"));
        Assert.That(UiFormat.Grouped(10_000_000), Is.EqualTo("10 000 000"));
    }

    [Test]
    public void TimesAreMinutesSecondsMilliseconds()
    {
        Assert.That(UiFormat.Time(346_508), Is.EqualTo("05:46:508"));
        Assert.That(UiFormat.Time(6_769), Is.EqualTo("00:06:769"));
        Assert.That(UiFormat.Time(-1_500), Is.EqualTo("-00:01:500"));
        Assert.That(UiFormat.Duration(385_000), Is.EqualTo("6:25"));
        Assert.That(UiFormat.Duration(0), Is.EqualTo("--:--"));
    }

    [Test]
    public void DatesAreIso()
        => Assert.That(UiFormat.DateTime(new DateTime(2026, 10, 27, 23, 28, 0)), Is.EqualTo("2026-10-27 23:28"));

    [Test]
    public void LongPathsAreCutInTheMiddle()
    {
        string path = @"C:\Users\player\AppData\Local\Temp\SDVXEditor\Exports";
        string cut = UiFormat.MiddleEllipsis(path, 30);
        Assert.That(cut.Length, Is.EqualTo(30));
        Assert.That(cut, Does.StartWith(@"C:\Users\"));
        Assert.That(cut, Does.EndWith(@"\Exports"));
        Assert.That(cut, Does.Contain("…"));
        Assert.That(UiFormat.MiddleEllipsis(@"C:\short", 30), Is.EqualTo(@"C:\short"));
    }

    [Test]
    public void SongCounterFollowsWhatIsListed()
    {
        Assert.That(LibraryCountText.Format(4, 7, 4, 7), Is.EqualTo("4 songs · 7 charts"));
        Assert.That(LibraryCountText.Format(1, 1, 1, 1), Is.EqualTo("1 song · 1 chart"));
        // a search that leaves one song of five
        Assert.That(LibraryCountText.Format(1, 2, 5, 9), Is.EqualTo("1 of 5 songs · 2 charts shown"));
    }

    [Test]
    public void EstimatesAboveTheGamesMaximumRead20Plus()
    {
        Assert.That(EstimateText.Level(17.34), Is.EqualTo("17.3"));
        Assert.That(EstimateText.Level(20.0), Is.EqualTo("20.0"));
        Assert.That(EstimateText.Level(20.4), Is.EqualTo("20+"));

        var uncertain = new DifficultyResult { EstimatedLevel = 20.4, Band = 0.8, LowConfidenceReasons = new[] { "very short chart (< 30 s)" } };
        Assert.That(EstimateText.Short(20.4, uncertain), Is.EqualTo("20+★ (±0.8), uncertain"));
        Assert.That(EstimateText.Short(null, null), Is.EqualTo("…"));
    }

    [Test]
    public void ModelInternalsBecomeWords()
    {
        // B6: what Song Setup used to print verbatim
        var r = new DifficultyResult
        {
            EstimatedLevel = 18,
            TopContributors = new[] { ("s_0.05_0.1_w", 1.0), ("s_0.3_0.3_t10", 0.8), ("s_0.3_0.1_w", 0.5) },
            LowConfidenceReasons = new[] { "features far outside the training range: n_events, dur" },
        };

        string drivers = EstimateText.Drivers(r);
        string reasons = string.Join("; ", EstimateText.Reasons(r));
        Assert.That(drivers, Is.EqualTo("short bursts of notes, sustained note density"));
        Assert.That(reasons, Is.EqualTo("unusual for an official chart: number of notes, song length"));

        foreach (string text in new[] { drivers, reasons })
        {
            Assert.That(text, Does.Not.Contain("_"), "no feature ids");
            Assert.That(text, Does.Not.Contain("training"), "no model jargon");
        }
    }

    [Test]
    public void EveryFeatureOfTheShippedModelHasWords()
    {
        // all term names of the embedded coefficients map to something other than the catch-all
        var c = DifficultyCoefficients.LoadEmbedded(out _);
        Assert.That(c, Is.Not.Null);
        var names = new[] { c!.SlotAware, c.SlotFree }.Where(m => m != null).SelectMany(m => m!.Terms.Select(t => t.Name)).Distinct()
                                                    .Where(n => !n.StartsWith("slot_", StringComparison.Ordinal)).ToList();
        Assert.That(names, Is.Not.Empty);
        var unnamed = names.Where(n => EstimateText.Feature(n) == "other chart features").ToList();
        Assert.That(unnamed, Is.Empty, "features without words: " + string.Join(", ", unnamed));
    }
}
