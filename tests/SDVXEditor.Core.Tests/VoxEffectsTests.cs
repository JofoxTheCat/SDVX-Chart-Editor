using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.Timing;
using SDVXEditor.Core.Validation;

namespace SDVXEditor.Core.Tests;

/// <summary>VOX game effects (docs/VOX_EFFECTS.md): model, kson / ksh storage, evaluator, colour maths, VOX rows.</summary>
public class VoxEffectsTests
{
    private const long Beat = Timebase.PulsesPerQuarter;
    private const long Measure = Timebase.PulsesPerWholeNote;

    /// <summary>A chart with every kind of game effect (lengths on the 1/192 grid so ksh keeps them exactly).</summary>
    public static Chart EffectsChart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Bt[0].Add(new Note(Measure));
        c.Notes.Fx[1].Add(new Note(2 * Measure, Beat));
        var v = c.VoxExtras;
        v.BgNo = 105;
        v.PostEffects.Add(new PostEffectRow(Measure, PostEffects.ColorConversion, "Mode", 2, 2, 0, stage: 1));
        v.PostEffects.Add(new PostEffectRow(Measure, PostEffects.ColorConversion, "Strength", 0, 100, 2 * Beat, stage: 1));
        v.PostEffects.Add(new PostEffectRow(Measure, PostEffects.ColorConversion, "Strength", 0, 155.52, 0, stage: 0, index: 1));
        v.PostEffects.Add(new PostEffectRow(2 * Measure, PostEffects.RandomShake, "none", 20, 0, 0, lengthMs: 500));
        v.PostEffects.Add(new PostEffectRow(2 * Measure, PostEffects.SetFrameLabel, "in6", 0, 0, 0));
        v.Commands.Add(new VoxCommandRow(Measure, "LaneY", 2, Beat, "0.00", "1024.00", "0.00", "0.00"));       // a toggle (hide)
        v.Commands.Add(new VoxCommandRow(2 * Measure, "LaneY", 2, 2 * Beat, "1024.00", "-1024.00", "0.00", "0.00")); // not a toggle
        v.Commands.Add(new VoxCommandRow(3 * Measure, "LaneY", 2, Beat, "-1024.00", "0.00", "0.00", "0.00"));
        v.Commands.Add(new VoxCommandRow(Measure, "BIL_RotZ", 2, Measure, "d0.00", "d-45.00", "d0.00", "d0.00"));
        v.Commands.Add(new VoxCommandRow(Measure, "Morphing2", 2, Beat, "0.0", "0.8", "0", "0"));
        v.Commands.Add(new VoxCommandRow(0, "BAROFF", 0, 0, "ON", "0", "0", "0"));
        v.Commands.Add(new VoxCommandRow(2 * Measure, "BAROFF", 0, 0, "OFF", "0", "0", "0"));
        v.Commands.Add(new VoxCommandRow(Measure, "BAR", 0, 0));
        v.Commands.Add(new VoxCommandRow(Measure + Beat, "SpecialN", 0, 0, "LANECOL", "xFFFF0000", "i0", "i0"));
        v.Commands.Add(new VoxCommandRow(Measure, "SpecialN", 0, 0, "LANECLEARCOL", "x80202020", "i0", "i0"));
        v.Commands.Add(new VoxCommandRow(Measure, "SpecialN", 0, 2 * Beat, "SIDEBG", "i1", "i1", "0"));
        v.RepresentativeBpm = 174;
        v.ConstantScroll = true;
        v.Scripts.Add(new VoxScript(7, new[] { "$offsetX = $offsetX + 0.178 when ( $targetStep - $currentStep ) > 48" }));
        v.Scripted.Add(new ScriptedNote(Measure, 3, new[] { 7 }));
        v.Sort();
        return c;
    }

    [Test]
    public void KsonRoundTripKeepsEverything()
    {
        var c = EffectsChart();
        c.VoxExtras.Scripts[0].Name = "dodge right";
        var back = KsonReader.Read(KsonWriter.Write(c));
        ModelSnapshot.AssertEqual(c.VoxExtras, back.VoxExtras, "kson vox extras");
        Assert.That(back.Extra.ToJsonString(), Does.Not.Contain("commands"), "nothing left in Extra");
    }

    [Test]
    public void KsonFromEditor017StillReads()
    {
        // 0.17-era rows: 6 elements, the official typo
        const string kson = """{"format_version":1,"meta":{"title":"t"},"beat":{"bpm":[[0,120]]},"impl":{"sdvx-editor":{"vox":{"post_effect":[[960,"ChromaticAbberation","Strength",0,1,240]]}}}}""";
        var c = KsonReader.Read(kson);
        var r = c.VoxExtras.PostEffects.Single();
        Assert.That((r.Name, r.Stage, r.Index, r.LengthMs), Is.EqualTo((PostEffects.ChromaticAberration, 0, 0, (double?)null)));
        Assert.That(KsonWriter.Write(c), Does.Contain("[960,\"ChromaticAberration\",\"Strength\",0,1,240]").Or.Contain("\"ChromaticAberration\""));
    }

    [Test]
    public void KshRoundTripKeepsEverythingButScriptNames()
    {
        var c = EffectsChart();
        string ksh = KshWriter.Write(c);
        TestContext.Out.WriteLine(ksh);
        Assert.That(ksh, Does.Contain("bg_no=105"));
        Assert.That(ksh, Does.Contain("lane_toggle=48"), "the plain hide is a native lane_toggle (one beat = 48/192)");
        Assert.That(ksh, Does.Contain("//#vox_sp|LaneY|2|96|1024.00|-1024.00|0.00|0.00"));
        Assert.That(ksh, Does.Contain("//#vox_pe|0|0|500ms|RandomShake|none|20|0"));
        int header = ksh.IndexOf("\n--", StringComparison.Ordinal);
        Assert.That(ksh.IndexOf("//#vox_", StringComparison.Ordinal), Is.GreaterThan(header), "never in the header");

        var back = KshReader.Read(ksh);
        ModelSnapshot.AssertEqual(c.VoxExtras, back.VoxExtras, "ksh vox extras");
        Assert.That(back.Compat.KshUnknownOption.ContainsKey("lane_toggle"), Is.False);
    }

    [Test]
    public void KshLaneToggleAlternates()
    {
        const string ksh = "title=t\nt=120\n--\nlane_toggle=96\n0000|00|--\n--\nlane_toggle=192\n0000|00|--\n--\nlane_toggle=48\n0000|00|--\n--\n";
        var rows = KshReader.Read(ksh).VoxExtras.Commands;
        Assert.That(rows.Select(r => (r.Y, r.Name, r.Type, r.Length, VoxToken.Number(r.P1), VoxToken.Number(r.P2))), Is.EqualTo(new[]
        {
            (0L, "LaneY", 2L, Measure / 2, (double?)0, (double?)1024),
            (Measure, "LaneY", 2L, Measure, (double?)1024, (double?)0),
            (2 * Measure, "LaneY", 2L, Measure / 4, (double?)0, (double?)1024),
        }));
        Assert.That(KshWriter.Write(KshReader.Read(ksh)), Does.Contain("lane_toggle=96").And.Contain("lane_toggle=192").And.Contain("lane_toggle=48"));
    }

    [Test]
    public void EvaluatorFollowsTheGameRules()
    {
        var c = EffectsChart();
        var timing = new TimingMap(c.Beat);
        var e = new VoxEffectEvaluator(c, p => timing.PulseToMs(p));
        double m1 = timing.PulseToMs(Measure), beatMs = timing.PulseToMs(Beat);

        Assert.That(e.Post(1, PostEffects.ColorConversion, "Strength", m1 - 1), Is.EqualTo(0), "default before the first row");
        Assert.That(e.Post(1, PostEffects.ColorConversion, "Strength", m1 + beatMs), Is.EqualTo(50).Within(1e-6), "half way through 2 beats");
        Assert.That(e.Post(1, PostEffects.ColorConversion, "Strength", m1 + 5 * beatMs), Is.EqualTo(100), "holds its end value");
        Assert.That(e.Post(1, PostEffects.ColorConversion, "Mode", m1), Is.EqualTo(2));
        Assert.That(e.Post(0, PostEffects.ColorConversion, "Strength", m1, index: 1), Is.EqualTo(155.52), "instance index 1 is its own value");
        Assert.That(e.Post(0, PostEffects.ColorConversion, "Strength", m1), Is.EqualTo(0), "instance 0 untouched");
        double m2 = timing.PulseToMs(2 * Measure);
        Assert.That(e.Post(0, PostEffects.RandomShake, "none", m2 + 250), Is.EqualTo(10).Within(1e-6), "500 ms length");

        Assert.That(e.Command("LaneY", m1 + beatMs / 2), Is.EqualTo(512).Within(1e-6));
        Assert.That(e.Command("LaneY", m2 + beatMs), Is.EqualTo(0).Within(1e-6), "1024 → -1024 over two beats");
        Assert.That(e.Command("BIL_RotZ", m1 + timing.PulseToMs(Measure) / 2 + 0), Is.EqualTo(-22.5).Within(0.5));
        Assert.That(e.LaneColour(m1), Is.Null);
        Assert.That(e.LaneColour(m1 + beatMs), Is.EqualTo(0xFFFF0000u));
        Assert.That(e.LaneClearColour(m1), Is.EqualTo(0x80202020u));
    }

    [Test]
    public void TweenFromCurrentValueWhenTypeIsNotTwo()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.VoxExtras.Commands.Add(new VoxCommandRow(0, "LaneY", 2, Beat, "0", "1024"));
        c.VoxExtras.Commands.Add(new VoxCommandRow(2 * Beat, "LaneY", 1, 2 * Beat, "5000", "0")); // p1 ignored: from current (1024)
        var t = new TimingMap(c.Beat);
        var e = new VoxEffectEvaluator(c, p => t.PulseToMs(p));
        Assert.That(e.Command("LaneY", t.PulseToMs(3 * Beat)), Is.EqualTo(512).Within(1e-6));
    }

    [Test]
    public void BarOffIsPerMeasure()
    {
        var c = EffectsChart();
        var t = new TimingMap(c.Beat);
        var e = new VoxEffectEvaluator(c, p => t.PulseToMs(p));
        long start(long p) => t.MeasureStart(t.MeasureAt(p));
        Assert.That(e.BarHidden(0, start), Is.True);
        Assert.That(e.BarHidden(Measure, start), Is.False, "BAR forces the line");
        Assert.That(e.BarHidden(2 * Measure, start), Is.False, "OFF at measure 3 ends the range");
    }

    [TestCase(0, 100, 1f, 0f, 0f, 0.298912f, 0.298912f, 0.298912f)]
    [TestCase(2, 100, 1f, 0.25f, 0f, 0f, 0.75f, 1f)]
    [TestCase(2, 50, 1f, 1f, 1f, 0.5f, 0.5f, 0.5f)]
    [TestCase(3, 100, 1f, 0f, 0f, 1f, 0f, 0f)] // negative cyan, complement → red: hue kept, brightness inverted
    [TestCase(3, 100, 0.2f, 0.2f, 0.2f, 0.8f, 0.8f, 0.8f)]
    [TestCase(4, 120, 1f, 0f, 0f, 0f, 1f, 0f)]
    [TestCase(1, 100, 1f, 1f, 1f, 1f, 1f, 0.937f)]
    [TestCase(2, 0, 1f, 0f, 0f, 1f, 0f, 0f)] // strength 0 skips the pass
    public void ColorConversionMatchesTheShader(int mode, double strength, float r, float g, float b, float er, float eg, float eb)
    {
        var (or, og, ob) = ColorConversionMath.Apply(mode, strength, r, g, b);
        Assert.That(new[] { or, og, ob }, Is.EqualTo(new[] { er, eg, eb }).Within(0.002f));
    }

    [Test]
    public void TokensAndCatalogue()
    {
        Assert.That(VoxToken.Number("d-45"), Is.EqualTo(-45));
        Assert.That(VoxToken.Number("f0.4"), Is.EqualTo(0.4));
        Assert.That(VoxToken.Number("i5"), Is.EqualTo(5));
        Assert.That(VoxToken.Number("xFF"), Is.EqualTo(255));
        Assert.That(VoxToken.Number("1024.00"), Is.EqualTo(1024));
        Assert.That(VoxToken.Number("LANECOL"), Is.Null);
        Assert.That(VoxToken.Argb("x80202020"), Is.EqualTo(0x80202020u));
        Assert.That(VoxToken.Argb("i-1"), Is.EqualTo(0xFFFFFFFFu), "official LANECOL also writes int bits");
        Assert.That(VoxToken.Format(-45, VoxValueKind.Degrees), Is.EqualTo("d-45.00"));
        Assert.That(VoxToken.Format(-0.00001, VoxValueKind.Float), Is.EqualTo("0.00"));
        Assert.That(VoxToken.IsValid("0 1"), Is.False);
        Assert.That(VoxToken.IsValid("se_08_big008"), Is.True);

        Assert.That(PostEffects.Names, Is.EqualTo(new[]
        {
            "ChromaticAberration", "ColorConversion", "RadicalBlur", "SimpleNoise", "CrtMonitorEffect", "LuminousEffect", "SetFrameLabel", "RandomShake",
        }), "game id order");
        Assert.That(PostEffects.Normalise("ChromaticAbberation"), Is.EqualTo("ChromaticAberration"));
        foreach (var dead in new[] { "LaneX", "GEO_RotZ", "Tilt_Mag", "HudY", "AIRL_ScaX", "Morphing0", "SpecialN:LANEDISP", "SpecialN:ADJRVZ" })
        {
            Assert.That(VoxCommands.Find(dead), Is.Null, dead + " is never read by the game and must not be offered");
            Assert.That(VoxCommands.Dead, Does.Contain(dead));
        }
        foreach (var cmd in VoxCommands.All)
        {
            var row = cmd.Create(0, Beat);
            Assert.That(new[] { row.P1, row.P2, row.P3, row.P4 }.All(VoxToken.IsValid), cmd.Key);
            Assert.That(VoxCommands.Of(row), Is.SameAs(cmd));
        }
        Assert.That(GameBackgrounds.DefinesPostEffects(105), Is.True);
        Assert.That(GameBackgrounds.DefinesPostEffects(0), Is.False);
        Assert.That(GameBackgrounds.Find(105)!.Label, Does.Contain("2085"));
    }

    [Test]
    public void PresetsAreValidRows()
    {
        foreach (var p in PostEffectPreset.All)
        {
            var rows = p.At(Measure, Measure, 1);
            Assert.That(rows, Is.Not.Empty, p.Name);
            foreach (var r in rows)
            {
                var info = PostEffects.Find(r.Name);
                Assert.That(info, Is.Not.Null, p.Name);
                Assert.That(info!.ParamIsLabel || info.Param(r.Param) != null, Is.True, $"{p.Name}: {r.Name}.{r.Param}");
                Assert.That(r.Stage, Is.EqualTo(1));
                Assert.That(r.Y, Is.GreaterThanOrEqualTo(Measure));
            }
        }
    }

    [Test]
    public void TimingRemapMovesRowsAndScriptedNotes()
    {
        var c = EffectsChart();
        var stack = new CommandStack(c);
        long before = c.VoxExtras.Commands.First(r => r.Name == "BIL_RotZ").Y;
        stack.Execute(new RemapPulsesCommand("shift", p => p + Measure, RetimeTargets.All));
        Assert.That(c.VoxExtras.Commands.First(r => r.Name == "BIL_RotZ").Y, Is.EqualTo(before + Measure));
        Assert.That(c.VoxExtras.Scripted[0].Y, Is.EqualTo(2 * Measure));
        stack.Undo();
        Assert.That(c.VoxExtras.Scripted[0].Y, Is.EqualTo(Measure));
    }

    [Test]
    public void EditingHelpers()
    {
        var v = new VoxExtrasInfo();
        int i = VoxEffectEditing.AddPostEffects(v, PostEffectPreset.All[0].At(960, 480, 0));
        Assert.That(i, Is.EqualTo(0));
        int j = VoxEffectEditing.AddCommand(v, VoxEffectEditing.Tween("BIL_RotZ", 480, 240, 0, -45));
        Assert.That(v.Commands[j].P2, Is.EqualTo("d-45.00"));
        int k = VoxEffectEditing.Move(v, new VoxRowRef(VoxRowKind.Command, j), 1920);
        Assert.That(v.Commands[k].Y, Is.EqualTo(1920));
        VoxEffectEditing.SetLength(v, new VoxRowRef(VoxRowKind.PostEffect, 1), 960);
        Assert.That(v.PostEffects[1].Length, Is.EqualTo(960));
        VoxEffectEditing.Delete(v, new VoxRowRef(VoxRowKind.PostEffect, 0));
        Assert.That(v.PostEffects, Has.Count.EqualTo(1));
    }

    [TestCase("$targetStep = $currentStep + 48000 when ( $targetStep - $currentStep ) > 96")]
    [TestCase("$offsetX = $offsetX + 5.0f * ( $targetStep - $currentStep - 48 ) / 48 when ( $targetStep - $currentStep ) > 48")]
    [TestCase("$targetStep = $targetStep - ( 48 * 2.5 ) when $currentStep < ( 48 * 4 * 25 + 48 * 1.5 )")]
    [TestCase("$targetStep = ( $targetStep - $currentStep ) * 0.5f + $currentStep")]
    [TestCase("$targetStep =  ( $targetStep - 54466 ) + 23808 + 48 * 8 - 6 when $currentStep <= 23808")]
    public void OfficialScriptsAreValid(string line) => Assert.That(VoxScriptSyntax.Check(line), Is.Null);

    [TestCase("$currentStep = 1")]
    [TestCase("$offsetX = 1 +")]
    [TestCase("$offsetX = 1 when 2")]
    [TestCase("#END")]
    [TestCase("$offsetY = (1")]
    [TestCase("$offsetX = foo")]
    public void BadScriptsAreRefused(string line) => Assert.That(VoxScriptSyntax.Check(line), Is.Not.Null);

    [Test]
    public void ScriptEvaluation()
    {
        var half = new VoxScript(5, new[] { "$targetStep = ( $targetStep - $currentStep ) * 0.5f + $currentStep" });
        Assert.That(VoxScriptEval.Run(new[] { half }, 200, 100).TargetStep, Is.EqualTo(150));
        var slide = new VoxScript(1, new[] { "$offsetX = $offsetX + 5.0f * ( $targetStep - $currentStep - 48 ) / 48 when ( $targetStep - $currentStep ) > 48" });
        Assert.That(VoxScriptEval.Run(new[] { slide }, 196, 100).OffsetX, Is.EqualTo(5).Within(1e-9));
        Assert.That(VoxScriptEval.Run(new[] { slide }, 120, 100).OffsetX, Is.EqualTo(0), "when false: no change");
        Assert.That(VoxScriptEval.Run(new[] { new VoxScript(2, new[] { "$offsetY = -(2 - 3) * 4 / 2" }) }, 0, 0).OffsetY, Is.EqualTo(2));
    }

    [Test]
    public void ChartCheckFindsEffectProblems()
    {
        var c = EffectsChart();
        c.VoxExtras.BgNo = 0;
        c.VoxExtras.Commands.Add(new VoxCommandRow(0, "LaneX", 2, 0));
        c.VoxExtras.Scripted.Add(new ScriptedNote(Beat, 4, new[] { 99 }));
        c.VoxExtras.Scripts.Add(new VoxScript(3, new[] { "$offsetX = )" }));
        var ids = new EffectChecks().Run(new ChartCheckContext(c)).Select(i => i.RuleId).ToList();
        Assert.That(ids, Does.Contain("effect.vox.post-effect-background"));
        Assert.That(ids, Does.Contain("effect.vox.dead-command"));
        Assert.That(ids, Does.Contain("effect.vox.script-no-note"));
        Assert.That(ids, Does.Contain("effect.vox.script-undefined"));
        Assert.That(ids, Does.Contain("effect.vox.script-syntax"));
        Assert.That(ids, Does.Contain("effect.vox.experimental"));

        var ok = EffectsChart();
        Assert.That(new EffectChecks().Run(new ChartCheckContext(ok)).Where(i => i.Severity != IssueSeverity.Info), Is.Empty, "bg 105 defines post effects; every row valid");
        Assert.That(ChartChecker.Rules.Any(r => r is EffectChecks), "found by the checker");
    }
}
