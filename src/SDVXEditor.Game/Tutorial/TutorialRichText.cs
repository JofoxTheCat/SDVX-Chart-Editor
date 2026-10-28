using System.Text.RegularExpressions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// The tutorial card's small markup, so a card reads as short coloured lines instead of a wall of text:
/// <list type="bullet">
/// <item><c>{key:Undo}</c> — the binding in effect for that editor action, drawn as a key (rebinding changes the text).</item>
/// <item><c>[k]Shift[/k]</c> — a key name, same style; <c>[ui]BT Chip[/ui]</c> — a button / tool / field name;
/// <c>[b]…[/b]</c> — bold white; <c>[c=ff5ad1]…[/c]</c> — a colour (the snap colours, the laser colours).</item>
/// <item>A blank line starts a paragraph, a single newline a new line; a line starting with "• " is a bullet with a
/// hanging indent.</item>
/// </list>
/// Anything else in square brackets is plain text.
/// </summary>
public static class TutorialRichText
{
    public static readonly Colour4 KeyColour = Colour4.FromHex("ffd84d");
    public static readonly Colour4 UiColour = Colour4.FromHex("8fd3ff");
    public static readonly Colour4 TextColour = Colour4.FromHex("e8e8f0");

    // the colours the editor uses (snap lines, lasers) — for [c=…] in the texts
    public const string White = "ffffff", Red = "ff5050", Blue = "4f8dff", Purple = "c47cff", LaserL = "4de1ff", LaserR = "ff5ad1", Orange = "ff9f43", Green = "7ee07e";

    private static readonly Regex key_token = new Regex(@"\{key:(\w+)\}", RegexOptions.Compiled);
    private static readonly Regex tag = new Regex(@"\[(k|ui|b|c=[0-9a-fA-F]{6})\](.*?)\[/(?:k|ui|b|c)\]", RegexOptions.Compiled);

    /// <summary>Replaces <c>{key:Action}</c> with <c>[k]binding[/k]</c>.</summary>
    public static string ExpandKeys(string text) => key_token.Replace(text, m =>
        Enum.TryParse<EditorAction>(m.Groups[1].Value, out var action) ? "[k]" + TutorialContext.KeyOf(action) + "[/k]" : m.Value);

    /// <summary>The text without markup (logs, tests).</summary>
    public static string Plain(string text) => tag.Replace(ExpandKeys(text), m => m.Groups[2].Value);

    /// <summary>Fills <paramref name="flow"/> (vertical) with the paragraphs and lines of <paramref name="text"/>.</summary>
    public static void Render(FillFlowContainer flow, string text, float size, Colour4? colour = null)
    {
        var baseColour = colour ?? TextColour;
        var paragraphs = ExpandKeys(text).Replace("\r", "").Split("\n\n");
        for (int p = 0; p < paragraphs.Length; p++)
        {
            var lines = paragraphs[p].Split('\n');
            for (int l = 0; l < lines.Length; l++)
            {
                string line = lines[l];
                bool bullet = line.StartsWith("• ", StringComparison.Ordinal);
                if (bullet) line = line[2..];

                var tf = new TextFlowContainer(t =>
                {
                    t.Font = EditorFont.Regular(size);
                    t.Colour = baseColour;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    LineSpacing = 0.12f,
                };
                AddLine(tf, line, size, baseColour);

                var margin = new MarginPadding { Top = l == 0 && p > 0 ? size * 0.55f : 0 };
                if (bullet)
                {
                    flow.Add(new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Margin = margin,
                        Padding = new MarginPadding { Left = size * 1.1f },
                        Children = new Drawable[]
                        {
                            new SpriteText { Text = "•", Font = EditorFont.Bold(size), Colour = KeyColour, X = -size * 0.9f },
                            tf,
                        },
                    });
                }
                else
                {
                    tf.Margin = margin;
                    flow.Add(tf);
                }
            }
        }
    }

    /// <summary>Adds one line (no newlines) with its spans to a text flow.</summary>
    public static void AddLine(TextFlowContainer tf, string line, float size, Colour4 baseColour)
    {
        int at = 0;
        foreach (Match m in tag.Matches(line))
        {
            if (m.Index > at) tf.AddText(line[at..m.Index], t => plain(t, size, baseColour));
            string kind = m.Groups[1].Value;
            string inner = m.Groups[2].Value;
            tf.AddText(inner, t =>
            {
                switch (kind)
                {
                    case "k":
                        t.Font = EditorFont.Bold(size);
                        t.Colour = KeyColour;
                        break;

                    case "ui":
                        t.Font = EditorFont.Bold(size);
                        t.Colour = UiColour;
                        break;

                    case "b":
                        t.Font = EditorFont.Bold(size);
                        t.Colour = Colour4.White;
                        break;

                    default:
                        t.Font = EditorFont.Bold(size);
                        t.Colour = Colour4.FromHex(kind[2..]);
                        break;
                }
            });
            at = m.Index + m.Length;
        }

        if (at < line.Length) tf.AddText(line[at..], t => plain(t, size, baseColour));
    }

    private static void plain(SpriteText t, float size, Colour4 colour)
    {
        t.Font = EditorFont.Regular(size);
        t.Colour = colour;
    }

    /// <summary>A one-line coloured span set (progress line, off-track box): <paramref name="tf"/> is cleared first.</summary>
    public static void SetLine(TextFlowContainer tf, string text, float size, Colour4 colour)
    {
        tf.Clear();
        var paragraphs = ExpandKeys(text).Split("\n\n");
        for (int i = 0; i < paragraphs.Length; i++)
        {
            if (i > 0) tf.NewParagraph();
            AddLine(tf, paragraphs[i].Replace('\n', ' '), size, colour);
        }
    }
}
