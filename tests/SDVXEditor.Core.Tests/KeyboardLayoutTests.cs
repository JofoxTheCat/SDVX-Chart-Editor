using NUnit.Framework;
using SDVXEditor.Core.Input;

namespace SDVXEditor.Core.Tests;

/// <summary>Logical letter shortcuts on top of physical (scancode) key events: KeyboardLayout.</summary>
public class KeyboardLayoutTests
{
    [Test]
    public void QwertyIsTheIdentity()
    {
        var us = KeyboardLayout.Qwerty;
        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            Assert.That(us.PhysicalKeyFor(c.ToString()), Is.EqualTo(c.ToString()));
            Assert.That(us.LogicalKeyFor(c.ToString()), Is.EqualTo(c.ToString()));
        }

        Assert.That(us.ToPhysicalCombo("Ctrl+Z"), Is.EqualTo("Ctrl+Z"));
        Assert.That(us.LabelOf("Slash"), Is.EqualTo("/"));
    }

    [Test]
    public void QwertzSwapsYAndZ()
    {
        var de = KeyboardLayout.Qwertz;

        // Ctrl+Z (undo) listens on the key labelled Z = physical Y, Ctrl+Y (redo) on physical Z
        Assert.That(de.ToPhysicalCombo("Ctrl+Z"), Is.EqualTo("Ctrl+Y"));
        Assert.That(de.ToPhysicalCombo("Ctrl+Y"), Is.EqualTo("Ctrl+Z"));
        Assert.That(de.ToPhysicalCombo("Ctrl+Shift+S"), Is.EqualTo("Ctrl+Shift+S"));

        // capture: pressing physical Y types z
        Assert.That(de.ToLogicalCombo("Ctrl+Y"), Is.EqualTo("Ctrl+Z"));
        Assert.That(de.LogicalKeyFor("Z"), Is.EqualTo("Y"));

        // non-letters stay physical, labelled with what the layout prints on them
        Assert.That(de.PhysicalKeyFor("Slash"), Is.EqualTo("Slash"));
        Assert.That(de.LabelOf("Slash"), Is.EqualTo("-"));
        Assert.That(de.LabelOf("Semicolon"), Is.EqualTo("Ö"));
        Assert.That(de.LogicalKeyFor("Semicolon"), Is.EqualTo("Semicolon"));
        Assert.That(de.PhysicalKeyFor("F5"), Is.EqualTo("F5"));
        Assert.That(de.PhysicalKeyFor("Number1"), Is.EqualTo("Number1"));
    }

    [Test]
    public void AzertyMovesLettersOntoOtherPositions()
    {
        var fr = KeyboardLayout.Azerty;
        Assert.That(fr.PhysicalKeyFor("A"), Is.EqualTo("Q"));
        Assert.That(fr.PhysicalKeyFor("Q"), Is.EqualTo("A"));
        Assert.That(fr.PhysicalKeyFor("W"), Is.EqualTo("Z"));
        Assert.That(fr.PhysicalKeyFor("M"), Is.EqualTo("Semicolon"));
        Assert.That(fr.LogicalKeyFor("Semicolon"), Is.EqualTo("M"));

        // physical M types ',' while M lives on the semicolon position: not nameable as a binding
        Assert.That(fr.LogicalKeyFor("M"), Is.Null);
        Assert.That(fr.ToLogicalCombo("Ctrl+M"), Is.Null);
    }

    [Test]
    public void EveryLetterRoundTrips()
    {
        foreach (var layout in new[] { KeyboardLayout.Qwerty, KeyboardLayout.Qwertz, KeyboardLayout.Azerty })
        {
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
                Assert.That(layout.LogicalKeyFor(layout.PhysicalKeyFor(c.ToString())), Is.EqualTo(c.ToString()), $"{layout}: {c}");
        }
    }

    [Test]
    public void LayoutWithoutLatinLettersFallsBackToPositions()
    {
        // a Cyrillic layout types no a-z anywhere: letters stay on their US positions, like osu!
        var ru = KeyboardLayout.FromCharacters("ru", new[] { ("Q", 'й'), ("W", 'ц'), ("Z", 'я'), ("Slash", '.') });
        Assert.That(ru.PhysicalKeyFor("Z"), Is.EqualTo("Z"));
        Assert.That(ru.LogicalKeyFor("Z"), Is.EqualTo("Z"));
        Assert.That(ru.LabelOf("Q"), Is.EqualTo("Й"));
    }
}
