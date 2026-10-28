using NUnit.Framework;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Formats.Ksh;
using SDVXEditor.Core.Formats.Kson;

namespace SDVXEditor.Core.Tests;

[TestFixture]
public class EffectCommandsTests
{
    private static Chart NewChart()
    {
        var c = new Chart();
        c.Beat.Bpm.Add(new BpmChange(0, 120));
        c.Notes.Fx[0].Add(new Note(0, 480));
        c.Notes.Fx[0].Add(new Note(960));
        c.Notes.Fx[1].Add(new Note(960, 960));
        c.Notes.Fx[1].Add(new Note(2400));
        c.Notes.Lasers[0].Add(new LaserSection(480, new[] { new LaserPoint(0, 0), new LaserPoint(480, 1) }));
        c.Notes.Lasers[1].Add(new LaserSection(1920, new[] { new LaserPoint(0, 1), new LaserPoint(240, 1, 0) }));
        return c;
    }

    private static List<Note> Holds(Chart c) => c.Notes.Fx.SelectMany(l => l).Where(n => n.IsHold).ToList();
    private static List<Note> Chips(Chart c) => c.Notes.Fx.SelectMany(l => l).Where(n => !n.IsHold).ToList();

    [Test]
    public void CatalogDefaults()
    {
        var def = EffectCatalog.NewDef("myretrig", "retrigger");
        Assert.That(def.Params["update_period"], Is.EqualTo("1/2"));
        Assert.That(def.Params["mix"], Is.EqualTo("0%>100%"));
        Assert.That(EffectCatalog.PresetFx, Does.Contain("tapestop").And.Not.Contain("switch_audio").And.Not.Contain("peaking_filter"));
        Assert.That(EffectCatalog.PresetLaser, Does.Contain("peaking_filter"));
        Assert.That(EffectCatalog.Types.Select(t => t.Label), Is.SupersetOf(new[] { "Retrigger", "Gate", "Flanger", "PitchShift", "BitCrusher", "Phaser", "Wobble", "TapeStop", "Echo", "SideChain", "SwitchAudio", "HighPassFilter", "LowPassFilter", "PeakingFilter" }));
    }

    [Test]
    public void DefinitionsRenamePropagatesAndUndoes()
    {
        var chart = NewChart();
        var before = ModelSnapshot.Of(chart);
        var stack = new CommandStack(chart);

        var def = EffectCatalog.NewDef("rt", "retrigger");
        stack.Execute(new AddEffectDefCommand(false, def));
        Assert.Throws<InvalidOperationException>(() => stack.Execute(new AddEffectDefCommand(false, new AudioEffectDef("rt", "gate"))));
        stack.Execute(EffectCommands.AssignToHolds(Holds(chart), "rt"));
        stack.Execute(new SetParamChangeCommand(false, "rt", "update_period", 240, "1/8"));
        Assert.That(EffectCommands.Uses(chart, "rt", false), Is.EqualTo(2));

        var p = new Dictionary<string, string>(def.Params) { ["rate"] = "80%" };
        stack.Execute(new EditEffectDefCommand(false, def, "rt8", "retrigger", p));
        Assert.That(Holds(chart).All(h => h.Effects.Single().Name == "rt8"), "holds follow the rename");
        Assert.That(chart.Audio.FxEffects.ParamChanges.Keys, Is.EqualTo(new[] { "rt8" }));
        Assert.That(def.Params["rate"], Is.EqualTo("80%"));

        // laser def rename moves its filter switches
        var lpf = EffectCatalog.NewDef("mylpf", "low_pass_filter");
        stack.Execute(new AddEffectDefCommand(true, lpf));
        stack.Execute(new SetLaserFilterCommand("mylpf", 480));
        stack.Execute(new EditEffectDefCommand(true, lpf, "lpf2", "low_pass_filter", lpf.Params));
        Assert.That(chart.Audio.LaserEffects.PulseEvents.Keys, Is.EqualTo(new[] { "lpf2" }));

        stack.Execute(new RemoveEffectDefCommand(false, def));
        Assert.That(chart.Audio.FxEffects.Defs, Is.Empty);

        var edited = ModelSnapshot.Of(chart);
        while (stack.Undo()) { }
        Assert.That(ModelSnapshot.FirstDifference(before, ModelSnapshot.Of(chart)), Is.Null);
        while (stack.Redo()) { }
        Assert.That(ModelSnapshot.FirstDifference(edited, ModelSnapshot.Of(chart)), Is.Null);
    }

    [Test]
    public void HoldAssignmentChipSoundsAndClear()
    {
        var chart = NewChart();
        var stack = new CommandStack(chart);
        var holds = Holds(chart);
        var cmd = EffectCommands.AssignToHolds(holds.Concat(Chips(chart)), "gate", new Dictionary<string, string> { ["wave_length"] = "1/16" });
        Assert.That(cmd.Count, Is.EqualTo(2), "chips are ignored");
        stack.Execute(cmd);
        Assert.That(holds[0].Effects.Single().Params["wave_length"], Is.EqualTo("1/16"));
        Assert.That(holds[0].Effects[0], Is.Not.SameAs(holds[1].Effects[0]), "each hold gets its own event");

        // an effect switch beyond the hold end is dropped
        stack.Execute(new SetHoldEffectsCommand(holds, new[] { new FxEffectEvent("gate"), new FxEffectEvent("flanger", 600) }));
        Assert.That(holds[0].Effects.Select(e => e.Name), Is.EqualTo(new[] { "gate" }), "hold 0 is 480 long");
        Assert.That(holds[1].Effects.Select(e => e.Name), Is.EqualTo(new[] { "gate", "flanger" }));

        var chips = Chips(chart);
        stack.Execute(new SetChipKeySoundCommand(chips.Concat(holds), new KeySoundRef("hit.wav", 0.6)));
        Assert.That(chips.All(c => c.KeySound == new KeySoundRef("hit.wav", 0.6)));
        Assert.That(holds.All(h => h.KeySound == null), "holds never get a chip sound");
        stack.Execute(new SetChipKeySoundCommand(chips.Take(1), null));
        Assert.That(chips[0].KeySound, Is.Null);
        stack.Undo();
        Assert.That(chips[0].KeySound, Is.Not.Null);

        stack.Execute(EffectCommands.AssignToHolds(holds, null));
        Assert.That(holds.All(h => h.Effects.Count == 0));
        stack.Undo();
        Assert.That(holds[1].Effects, Has.Count.EqualTo(2));
    }

    [Test]
    public void ParamChangesFiltersGainAndSlamVolume()
    {
        var chart = NewChart();
        var stack = new CommandStack(chart);
        stack.Execute(new SetParamChangeCommand(true, "peaking_filter", "gain", 960, "70%"));
        stack.Execute(new SetParamChangeCommand(true, "peaking_filter", "gain", 480, "40%"));
        stack.Execute(new SetParamChangeCommand(true, "peaking_filter", "gain", 960, "90%"));
        var list = chart.Audio.LaserEffects.ParamChanges["peaking_filter"]["gain"];
        Assert.That(list.Select(p => (p.Y, p.Value)), Is.EqualTo(new[] { (480L, "40%"), (960L, "90%") }));
        stack.Execute(new SetParamChangeCommand(true, "peaking_filter", "gain", 480, null));
        stack.Execute(new SetParamChangeCommand(true, "peaking_filter", "gain", 960, null));
        Assert.That(chart.Audio.LaserEffects.ParamChanges, Is.Empty, "empty entries are removed");
        stack.Undo();
        Assert.That(chart.Audio.LaserEffects.ParamChanges["peaking_filter"]["gain"], Has.Count.EqualTo(1));

        stack.Execute(new SetLaserFilterCommand("low_pass_filter", 480));
        stack.Execute(new SetLaserFilterCommand("bitcrusher", 1920));
        Assert.That(EffectCommands.FilterAt(chart, 1000), Is.EqualTo("low_pass_filter"));
        Assert.That(EffectCommands.FilterAt(chart, 2000), Is.EqualTo("bitcrusher"));
        Assert.That(EffectCommands.FilterAt(chart, 100), Is.Null);
        stack.Execute(new SetLaserFilterCommand("high_pass_filter", 480));
        Assert.That(chart.Audio.LaserEffects.PulseEvents.ContainsKey("low_pass_filter"), Is.False, "replaced at the same pulse");
        stack.Execute(new SetLaserFilterCommand(null, 1920));
        Assert.That(EffectCommands.FilterAt(chart, 2000), Is.EqualTo("high_pass_filter"));
        stack.Undo();
        Assert.That(EffectCommands.FilterAt(chart, 2000), Is.EqualTo("bitcrusher"));

        stack.Execute(EffectCommands.SetFilterGain(0, 0.7));
        stack.Execute(EffectCommands.SetSlamVolume(1920, 0.4));
        stack.Execute(EffectCommands.SetPeakingFilterDelay(80));
        Assert.That(EffectCommands.ValueAt(chart.Audio.LaserEffects.LegacyFilterGain, 500, 0.5), Is.EqualTo(0.7));
        Assert.That(EffectCommands.ValueAt(chart.Audio.KeySound.LaserVolume, 100, 1), Is.EqualTo(1));
        Assert.That(EffectCommands.ValueAt(chart.Audio.KeySound.LaserVolume, 2000, 1), Is.EqualTo(0.4));
        stack.Undo();
        Assert.That(chart.Audio.LaserEffects.PeakingFilterDelay, Is.Null);
        stack.Execute(EffectCommands.SetSlamVolume(1920, null));
        Assert.That(chart.Audio.KeySound.LaserVolume, Is.Empty);
    }

    [Test]
    public void ImportKeySoundCopiesReusesAndNumbers()
    {
        string root = Path.Combine(Path.GetTempPath(), "sdvx-keysound-" + Guid.NewGuid().ToString("N"));
        string song = Path.Combine(root, "song"), src = Path.Combine(root, "src");
        Directory.CreateDirectory(song);
        Directory.CreateDirectory(src);
        try
        {
            string a = Path.Combine(src, "hit.wav");
            File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
            Assert.That(KeySoundFiles.Import(song, a), Is.EqualTo("hit.wav"));
            Assert.That(KeySoundFiles.Import(song, a), Is.EqualTo("hit.wav"), "same content reused");
            File.WriteAllBytes(a, new byte[] { 9 });
            Assert.That(KeySoundFiles.Import(song, a), Is.EqualTo("hit_2.wav"), "different content numbered");
            Assert.That(KeySoundFiles.Import(song, Path.Combine(song, "hit.wav")), Is.EqualTo("hit.wav"), "a file already in the folder");
            File.WriteAllBytes(Path.Combine(song, "song.ogg"), new byte[] { 0 });
            Assert.That(KeySoundFiles.List(song, "song.ogg"), Is.EqualTo(new[] { "hit.wav", "hit_2.wav" }));
            File.WriteAllBytes(Path.Combine(src, "x.mp3"), new byte[] { 0 });
            Assert.Throws<ArgumentException>(() => KeySoundFiles.Import(song, Path.Combine(src, "x.mp3")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>A chart with every effect setting the effects tab edits.</summary>
    public static Chart EditedEffectsChart()
    {
        var chart = NewChart();
        var stack = new CommandStack(chart);
        stack.Execute(new AddEffectDefCommand(false, EffectCatalog.NewDef("rt", "retrigger")));
        stack.Execute(new AddEffectDefCommand(true, EffectCatalog.NewDef("mylpf", "low_pass_filter")));
        stack.Execute(EffectCommands.AssignToHolds(Holds(chart).Take(1), "rt"));
        stack.Execute(EffectCommands.AssignToHolds(Holds(chart).Skip(1), "gate", new Dictionary<string, string> { ["wave_length"] = "1/16" }));
        stack.Execute(new SetParamChangeCommand(false, "rt", "update_period", 240, "1/8"));
        stack.Execute(new SetChipKeySoundCommand(Chips(chart).Take(1), new KeySoundRef("hit.wav", 0.5)));
        stack.Execute(new SetChipKeySoundCommand(Chips(chart).Skip(1), new KeySoundRef("clap")));
        stack.Execute(new SetLaserFilterCommand("mylpf", 480));
        stack.Execute(new SetLaserFilterCommand("bitcrusher", 1920));
        stack.Execute(EffectCommands.SetFilterGain(480, 0.7));
        stack.Execute(EffectCommands.SetSlamVolume(1920, 0.4));
        stack.Execute(EffectCommands.SetPeakingFilterDelay(60));
        return chart;
    }

    [Test]
    public void KsonRoundTripOfEffectEditsIsLossless()
    {
        var chart = EditedEffectsChart();
        var again = KsonReader.Read(KsonWriter.Write(chart));
        ModelSnapshot.AssertEqual(chart, again, "kson round trip of effects");
        Assert.That(again.Extra.Count, Is.Zero);
        Assert.That(again.Audio.FxEffects.OrphanLongEvents, Is.Empty);
        Assert.That(again.Audio.KeySound.OrphanFxChipEvents, Is.Empty);
    }

    [Test]
    public void KshKeepsChipSoundsDefsAndFilters()
    {
        var chart = EditedEffectsChart();
        var back = KshReader.Read(KshWriter.Write(chart));
        ModelSnapshot.AssertEqual(Chips(chart).Select(c => c.KeySound!).ToList(), Chips(back).Select(c => c.KeySound!).ToList(), "ksh chip sounds");
        Assert.That(back.Audio.FxEffects.Defs.Select(d => d.Name), Is.EqualTo(new[] { "rt" }));
        Assert.That(EffectCommands.FilterAt(back, 1000), Is.EqualTo("mylpf"));
        Assert.That(Holds(back)[0].Effects.Select(e => e.Name), Is.EqualTo(new[] { "rt" }));
        Assert.That(back.Audio.LaserEffects.PeakingFilterDelay, Is.EqualTo(60));
        Assert.That(EffectCommands.ValueAt(back.Audio.KeySound.LaserVolume, 2000, 1), Is.EqualTo(0.4).Within(1e-9));
    }
}
