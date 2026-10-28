using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>One button of a <see cref="ChoiceDialog"/>.</summary>
public sealed record DialogChoice(string Label, Action? Action, bool IsDefault = false);

/// <summary>
/// A small modal question with a row of buttons (the editor's "Save changes?", "New difficulty: copy or empty?",
/// "Which slot?", "Open which difficulty?"). A button closes the window and then runs its action; Esc / ✕ = the
/// cancel action, Enter = the default button.
/// </summary>
public partial class ChoiceDialog : DialogWindow
{
    private const float button_height = 28;

    private readonly string[] lines;
    private readonly IReadOnlyList<DialogChoice> choices;
    private readonly Action? cancelled;
    private readonly bool vertical;
    private readonly Dictionary<string, DialogButton> buttons = new Dictionary<string, DialogButton>();
    private Action? chosen;

    /// <summary>Label of the button that closed the dialog (null: cancelled with Esc / ✕).</summary>
    public string? Choice { get; private set; }

    /// <param name="vertical">Buttons stacked full width (long labels, lists of difficulties) instead of a right-aligned row.</param>
    public ChoiceDialog(string title, IEnumerable<string> lines, IReadOnlyList<DialogChoice> choices, Action? cancelled = null, bool vertical = false, float width = 470)
        : base(title, size(lines.Count(), choices.Count, vertical, width))
    {
        this.lines = lines.ToArray();
        this.choices = choices;
        this.cancelled = cancelled;
        this.vertical = vertical;
    }

    private static Vector2 size(int lineCount, int buttonCount, bool vertical, float width) =>
        new Vector2(width, 24 + lineCount * 18 + (vertical ? buttonCount * (button_height + 6) + 10 : button_height + 26));

    /// <summary>The button with <paramref name="label"/> (smoke test clicks it with the mouse).</summary>
    public DialogButton Button(string label) => buttons.TryGetValue(label, out var b) ? b : throw new InvalidOperationException($"dialog \"{Title}\" has no button \"{label}\" (has {string.Join(", ", buttons.Keys)})");

    public IReadOnlyCollection<string> Labels => buttons.Keys;

    [BackgroundDependencyLoader]
    private void load()
    {
        var text = new FillFlowContainer
        {
            Position = new Vector2(12, 12),
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
        };
        for (int i = 0; i < lines.Length; i++)
            text.Add(DialogText.Label(lines[i], bold: i == 0));

        var content = new List<Drawable> { text };

        for (int i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var button = new DialogButton(choice.Label, () => choose(choice))
            {
                IsDefault = choice.IsDefault,
                Size = vertical ? new Vector2(1, button_height) : new Vector2(Math.Max(100, choice.Label.Length * 7.5f + 24), button_height),
            };

            if (vertical)
            {
                button.RelativeSizeAxes = Axes.X;
                button.Width = 1;
                button.Y = 20 + lines.Length * 18 + i * (button_height + 6);
            }

            buttons[choice.Label] = button;
            content.Add(button);
        }

        if (!vertical)
        {
            // right-aligned row, first choice leftmost
            float x = -12;
            foreach (var choice in choices.Reverse())
            {
                var b = buttons[choice.Label];
                b.Anchor = Anchor.BottomRight;
                b.Origin = Anchor.BottomRight;
                b.Position = new Vector2(x, -10);
                x -= b.Width + 10;
            }
        }

        Body.Children = vertical
            ? new Drawable[] { new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding { Horizontal = 12 }, Children = content } }
            : content.ToArray();
    }

    private void choose(DialogChoice choice)
    {
        Choice = choice.Label;
        chosen = choice.Action;
        Accept();
    }

    /// <summary>Enter: the default button (or the first one).</summary>
    protected override void OnAccept()
    {
        if (Choice == null && (choices.FirstOrDefault(c => c.IsDefault) ?? choices.FirstOrDefault()) is { } fallback)
        {
            Choice = fallback.Label;
            chosen = fallback.Action;
        }

        // after the window is gone (Closed runs once the overlay removed it): the action may open the next dialog or exit the screen
        if (chosen is { } run) Closed += run;
    }

    protected override void OnCancel()
    {
        Choice = null;
        if (cancelled is { } run) Closed += run;
    }
}
