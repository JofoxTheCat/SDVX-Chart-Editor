using NUnit.Framework;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Core.Input;

namespace SDVXEditor.Core.Tests;

public class KeyBindingTests
{
    private enum TestAction
    {
        Save,
        OpenDifficulty,
        PlayPause,
        TabTiming,
        PlaybackRateUp,
    }

    private static BindingTable<TestAction> table() => new BindingTable<TestAction>(new[]
    {
        (TestAction.Save, "Ctrl+S"),
        (TestAction.OpenDifficulty, "Ctrl+O"),
        (TestAction.PlayPause, "Space"),
        (TestAction.PlayPause, "C"),
        (TestAction.TabTiming, "F3"),
        (TestAction.PlaybackRateUp, "Alt+Up"),
    });

    [Test]
    public void CombosAreCanonical()
    {
        Assert.That(KeyComboText.Normalize("shift + control+P"), Is.EqualTo("Ctrl+Shift+P"));
        Assert.That(KeyComboText.Normalize("Alt+Ctrl+MouseWheelUp"), Is.EqualTo("Ctrl+Alt+MouseWheelUp"));
        Assert.That(KeyComboText.Normalize("Ctrl+Shift"), Is.Null, "modifiers alone are not a binding");
        Assert.That(KeyComboText.Normalize("Ctrl+..\\x"), Is.Null);
        Assert.That(KeyComboText.Normalize(""), Is.Null);
        Assert.That(KeyComboText.Same("ctrl+s", "Control+S"), Is.True);
        Assert.That(KeyComboText.Same("Ctrl+S", "S"), Is.False);
    }

    [Test]
    public void ActionIdsAreKebabCase()
    {
        Assert.That(BindingTable<TestAction>.IdOf(TestAction.PlaybackRateUp), Is.EqualTo("playback-rate-up"));
        Assert.That(BindingTable<TestAction>.ActionOf("Open-Difficulty"), Is.EqualTo(TestAction.OpenDifficulty));
        Assert.That(BindingTable<TestAction>.ActionOf("nope"), Is.Null);
    }

    [Test]
    public void OverridesRoundTripThroughTheSettingsText()
    {
        var t = table();
        Assert.That(t.ToOverrideText(), Is.Empty, "all defaults = nothing stored");

        t.Set(TestAction.TabTiming, new[] { "F8" });
        t.Set(TestAction.OpenDifficulty, Array.Empty<string>());
        t.Set(TestAction.PlayPause, new[] { "space", "shift+ctrl+K" });
        string text = t.ToOverrideText();
        Assert.That(text, Is.EqualTo("open-difficulty=none, play-pause=space|Ctrl+Shift+K, tab-timing=F8"));

        var back = table();
        back.LoadOverrides(text);
        Assert.That(back[TestAction.TabTiming], Is.EqualTo(new[] { "F8" }));
        Assert.That(back[TestAction.OpenDifficulty], Is.Empty, "\"none\" = deliberately unbound");
        Assert.That(back[TestAction.PlayPause], Is.EqualTo(new[] { "space", "Ctrl+Shift+K" }));
        Assert.That(back[TestAction.Save], Is.EqualTo(new[] { "Ctrl+S" }));
        Assert.That(back.ToOverrideText(), Is.EqualTo(text));
    }

    [Test]
    public void JunkInTheSettingsTextIsIgnored()
    {
        var t = table();
        t.LoadOverrides("bogus=F1, save=, tab-timing=Ctrl, play-pause=Ctrl+Shift|X ; =F2, open-difficulty");
        Assert.That(t[TestAction.Save], Is.EqualTo(new[] { "Ctrl+S" }), "an empty value keeps the default");
        Assert.That(t[TestAction.TabTiming], Is.EqualTo(new[] { "F3" }), "a modifier-only value is invalid");
        Assert.That(t[TestAction.PlayPause], Is.EqualTo(new[] { "X" }), "invalid combos are dropped, valid ones kept");
        Assert.That(t.IsDefault(TestAction.OpenDifficulty), Is.True);
    }

    [Test]
    public void SettingTheDefaultsAgainIsNoOverride()
    {
        var t = table();
        t.Set(TestAction.PlayPause, new[] { "SPACE", "c" });
        Assert.That(t.IsDefault(TestAction.PlayPause), Is.True);
        Assert.That(t.AllDefault, Is.True);
    }

    [Test]
    public void ConflictsAreFoundPerScope()
    {
        var t = table();
        Assert.That(t.AllConflicts(), Is.Empty);
        Assert.That(t.FindConflicts("control+o"), Is.EqualTo(new[] { TestAction.OpenDifficulty }));
        Assert.That(t.FindConflicts("Ctrl+O", TestAction.OpenDifficulty), Is.Empty, "the action itself is no conflict");

        var conflicts = t.Rebind(TestAction.TabTiming, 0, "Ctrl+O");
        Assert.That(conflicts, Is.EqualTo(new[] { TestAction.OpenDifficulty }));
        Assert.That(t[TestAction.TabTiming], Is.EqualTo(new[] { "Ctrl+O" }), "Keep binds anyway");
        Assert.That(t[TestAction.OpenDifficulty], Is.EqualTo(new[] { "Ctrl+O" }));
        Assert.That(t.AllConflicts().Single().Actions, Is.EquivalentTo(new[] { TestAction.TabTiming, TestAction.OpenDifficulty }));

        // the play bindings are a separate scope: the same key there is no conflict here
        var play = new PlayBindings();
        Assert.That(play.Table.FindConflicts("Ctrl+O"), Is.Empty);
    }

    [Test]
    public void SwapGivesTheOtherActionTheOldCombination()
    {
        var t = table();
        t.Rebind(TestAction.TabTiming, 0, "Ctrl+O", ConflictResolution.Swap);
        Assert.That(t[TestAction.TabTiming], Is.EqualTo(new[] { "Ctrl+O" }));
        Assert.That(t[TestAction.OpenDifficulty], Is.EqualTo(new[] { "F3" }));
        Assert.That(t.AllConflicts(), Is.Empty);

        // swapping into an empty slot has nothing to hand over: the other action just loses it
        var u = table();
        u.Rebind(TestAction.Save, 1, "Space", ConflictResolution.Swap);
        Assert.That(u[TestAction.Save], Is.EqualTo(new[] { "Ctrl+S", "Space" }));
        Assert.That(u[TestAction.PlayPause], Is.EqualTo(new[] { "C" }));
    }

    [Test]
    public void ClearOtherRemovesTheCombinationThere()
    {
        var t = table();
        t.Rebind(TestAction.Save, 0, "C", ConflictResolution.ClearOther);
        Assert.That(t[TestAction.Save], Is.EqualTo(new[] { "C" }));
        Assert.That(t[TestAction.PlayPause], Is.EqualTo(new[] { "Space" }));
        Assert.That(t.ToOverrideText(), Is.EqualTo("save=C, play-pause=Space"));
    }

    [Test]
    public void RebindClearsMovesAndResets()
    {
        var t = table();
        t.Rebind(TestAction.PlayPause, 1, null);
        Assert.That(t[TestAction.PlayPause], Is.EqualTo(new[] { "Space" }), "null clears the slot");
        t.Rebind(TestAction.PlayPause, 0, null);
        Assert.That(t[TestAction.PlayPause], Is.Empty);
        Assert.That(t.ToOverrideText(), Is.EqualTo("play-pause=none"));

        t.Reset(TestAction.PlayPause);
        Assert.That(t[TestAction.PlayPause], Is.EqualTo(new[] { "Space", "C" }));

        t.Rebind(TestAction.PlayPause, 1, "space");
        Assert.That(t[TestAction.PlayPause], Is.EqualTo(new[] { "space" }), "the same combo in another slot moves, it is not duplicated");

        int changes = 0;
        t.Changed += () => changes++;
        t.Set(TestAction.Save, new[] { "F12" });
        t.ResetAll();
        Assert.That(t.AllDefault, Is.True);
        Assert.That(changes, Is.EqualTo(2));
    }

    [Test]
    public void PlayBindingsKeepTheirSettingsLineAndCanBeUnbound()
    {
        var b = PlayBindings.Parse("bt-a=S|Joy1, back=none");
        Assert.That(b[PlayAction.BtA], Is.EqualTo(new[] { "S", "Joy1" }));
        Assert.That(b[PlayAction.Back], Is.Empty);
        Assert.That(b.ToString(), Does.Contain("bt-a=S|Joy1").And.Contain("back=none").And.Contain("bt-b=F"));
        Assert.That(PlayBindings.Parse(b.ToString()).ToString(), Is.EqualTo(b.ToString()));

        b.Table.Rebind(PlayAction.BtB, 0, "Joy1", ConflictResolution.Swap);
        Assert.That(b[PlayAction.BtB], Is.EqualTo(new[] { "Joy1" }));
        Assert.That(b[PlayAction.BtA], Is.EqualTo(new[] { "S", "F" }), "BT-A takes over BT-B's old key");

        Assert.That(PlayBindings.ReservedMeaning("tab"), Is.EqualTo("autoplay"));
        Assert.That(PlayBindings.ReservedMeaning("D"), Is.Null);
    }
}
