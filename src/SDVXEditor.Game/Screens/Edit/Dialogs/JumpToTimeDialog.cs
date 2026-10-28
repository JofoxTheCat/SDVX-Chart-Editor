using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>
/// osu!-style "jump to time": opened by a click on the bottom bar's time. The current time stands pre-selected in
/// the field (Ctrl+C copies it); typing a time (<c>mm:ss:mmm</c>, <c>ss.mmm</c>, plain ms) or pasting a modding
/// timestamp (<c>01:23:456 (…) - </c>) and Enter jumps there, a timestamp also selects its objects. Esc closes.
/// A text that is no time keeps the dialog open with the reason under the field.
/// </summary>
public partial class JumpToTimeDialog : DialogWindow
{
    private readonly string initialText;
    private readonly Func<string, string?> jump;

    /// <summary>The time field (smoke test).</summary>
    public TimeTextBox TimeBox { get; private set; } = null!;

    /// <summary>Why the last text was refused, or empty.</summary>
    public SpriteText ErrorText { get; private set; } = null!;

    /// <param name="initialText">The current time, shown selected.</param>
    /// <param name="jump">Jumps to a time or timestamp; returns null on success, else the reason it could not.</param>
    public JumpToTimeDialog(string initialText, Func<string, string?> jump)
        : base("Jump to Time", new Vector2(460, 124))
    {
        this.initialText = initialText;
        this.jump = jump;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        Body.Children = new Drawable[]
        {
            DialogText.Label("Time (mm:ss:mmm, ss.mmm or ms) or a modding timestamp - Enter jumps, Ctrl+C copies")
                      .With(t => t.Position = new Vector2(10, 8)),
            TimeBox = new TimeTextBox(Cancel)
            {
                Position = new Vector2(10, 28),
                Width = 440,
                Text = initialText,
            },
            ErrorText = DialogText.Label("").With(t =>
            {
                t.Position = new Vector2(10, 58);
                t.Colour = EditorColours.Dialog.NoteRed;
            }),
            new DialogButton("Jump", Submit)
            {
                IsDefault = true,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-96, -10),
                Size = new Vector2(80, 26),
            },
            new DialogButton("Cancel", Cancel)
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-10, -10),
                Size = new Vector2(80, 26),
            },
        };

        TimeBox.OnCommit += (_, _) => Submit();
    }

    private bool focusedOnce;

    protected override void Update()
    {
        base.Update();

        // focus and select once the field is alive (in LoadComplete it is not yet, and the focus manager refuses it)
        if (focusedOnce || !TimeBox.IsAlive) return;

        focusedOnce = true;
        GetContainingFocusManager()?.ChangeFocus(TimeBox);
        TimeBox.SelectAll();
    }

    /// <summary>Jumps to the field's text; closes on success, else shows why and keeps the text.</summary>
    public void Submit()
    {
        string? error = jump(TimeBox.Text);
        if (error == null)
        {
            Accept();
            return;
        }

        ErrorText.Text = error;
        GetContainingFocusManager()?.ChangeFocus(TimeBox);
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Key.Enter or Key.KeypadEnter)
        {
            Submit();
            return true;
        }

        return base.OnKeyDown(e);
    }

    /// <summary>The field: Esc closes the dialog at once (a plain text box would only drop its focus first).</summary>
    public partial class TimeTextBox : DialogTextBox
    {
        private readonly Action escape;

        public TimeTextBox(Action escape)
        {
            this.escape = escape;
            LengthLimit = 4096; // a timestamp of a large selection is long
            CommitOnFocusLost = false; // Cancel / ✕ must not jump on the way out
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == Key.Escape)
            {
                escape();
                return true;
            }

            return base.OnKeyDown(e);
        }
    }
}
