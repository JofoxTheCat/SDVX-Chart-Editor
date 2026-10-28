using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Graphics.Containers;

namespace SDVXEditor.Game.Graphics.UserInterface.Dialog;

/// <summary>
/// Hosts modal <see cref="DialogWindow"/>s above everything else. Lives outside the
/// UI scaling container on purpose: native dialogs keep their pixel size whatever
/// the window size is (reference 04/05 are 1:1 screenshots).
/// </summary>
[Cached]
public partial class DialogOverlay : CompositeDrawable
{
    private readonly PopupHostContainer host;
    private readonly List<DialogWindow> open = new List<DialogWindow>();

    public DialogOverlay()
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = host = new PopupHostContainer();
    }

    public bool HasOpenDialog => open.Count > 0;

    public IReadOnlyList<DialogWindow> OpenDialogs => open;

    public DialogWindow? Current => open.Count > 0 ? open[^1] : null;

    /// <summary>Shows <paramref name="window"/> centred, modal (everything behind it is blocked).</summary>
    public void Push(DialogWindow window)
    {
        var blocker = new InputBlocker(() => window.Flash());

        window.Anchor = Anchor.Centre;
        window.Origin = Anchor.Centre;
        window.Closed += () =>
        {
            open.Remove(window);
            host.Remove(blocker, true);
            host.Remove(window, true);
        };

        open.Add(window);
        host.Add(blocker);
        host.Add(window);
    }

    /// <summary>Cancels every open dialog (used when the owning screen exits).</summary>
    public void CloseAll()
    {
        foreach (var w in open.ToArray())
            w.Cancel();
    }

    /// <summary>How much a modal window darkens what is behind it (audit U9: text of the panel behind peeked out
    /// around the window, and a second dialog stacked on the first with both fully bright).</summary>
    public const float BACKDROP_DIM = 0.4f;

    /// <summary>The backdrop of the topmost dialog (smoke test checks that it is drawn).</summary>
    public Drawable? CurrentBackdrop => host.Children.OfType<InputBlocker>().LastOrDefault();

    private partial class InputBlocker : Box
    {
        private readonly Action clicked;

        public InputBlocker(Action clicked)
        {
            this.clicked = clicked;
            RelativeSizeAxes = Axes.Both;
            Colour = Colour4.Black;
            Alpha = 0;
            AlwaysPresent = true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            this.FadeTo(BACKDROP_DIM, 120);
        }

        public override bool HandlePositionalInput => true;

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            clicked();
            return true;
        }

        protected override bool OnClick(ClickEvent e) => true;
        protected override bool OnScroll(ScrollEvent e) => true;
        protected override bool OnHover(HoverEvent e) => true;
        protected override bool OnMouseMove(MouseMoveEvent e) => true;
    }
}

/// <summary>
/// Draggable modal window in the light Windows look: title bar with ✕, #f0f0f0 body,
/// 1 px border. Subclasses fill <see cref="Body"/>. Esc = Cancel, Enter = OK.
/// </summary>
public abstract partial class DialogWindow : CompositeDrawable
{
    public const float TITLE_HEIGHT = 30;

    public string Title { get; }

    /// <summary>Raised once the window has closed (either way).</summary>
    public event Action? Closed;

    /// <summary>The client area below the title bar.</summary>
    protected Container Body { get; private set; } = null!;

    private Box titleBackground = null!;
    private bool isClosed;

    protected DialogWindow(string title, Vector2 clientSize)
    {
        Title = title;
        Size = clientSize + new Vector2(2, TITLE_HEIGHT + 2);
        Masking = true;
        // osu-framework draws no border at corner radius 0 (measured): keep it just above
        CornerRadius = 1;
        BorderThickness = 1;
        BorderColour = EditorColours.Dialog.WindowBorder;
        EdgeEffect = new EdgeEffectParameters
        {
            Type = EdgeEffectType.Shadow,
            Colour = Colour4.Black.Opacity(0.35f),
            Radius = 14,
            Offset = new Vector2(0, 4),
        };
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Body },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(1),
                Children = new Drawable[]
                {
                    new TitleBar(this)
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = TITLE_HEIGHT,
                        Children = new Drawable[]
                        {
                            titleBackground = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.TitleBar },
                            new SpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                X = 9,
                                Text = Title,
                                Font = EditorFont.Dialog(12.5f),
                                Colour = EditorColours.Dialog.TitleText,
                            },
                            new CloseButton(Cancel)
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                            },
                        },
                    },
                    Body = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = TITLE_HEIGHT },
                    },
                },
            },
        };
    }

    /// <summary>Called by the OK button / Enter. Default: close.</summary>
    protected virtual void OnAccept()
    {
    }

    /// <summary>Called by Cancel / ✕ / Esc. Default: close.</summary>
    protected virtual void OnCancel()
    {
    }

    public void Accept()
    {
        if (isClosed) return;

        OnAccept();
        close();
    }

    public void Cancel()
    {
        if (isClosed) return;

        OnCancel();
        close();
    }

    private void close()
    {
        isClosed = true;
        Closed?.Invoke();
    }

    /// <summary>Title flash when the user clicks outside a modal window, like Windows does.</summary>
    public void Flash() => titleBackground.FlashColour(Colour4.White, 300);

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                return true;

            case Key.Enter:
            case Key.KeypadEnter:
                Accept();
                return true;
        }

        // modal: nothing behind the dialog sees keys while it is open
        return true;
    }

    // block clicks from falling through to the screen behind
    protected override bool OnMouseDown(MouseDownEvent e) => true;
    protected override bool OnClick(ClickEvent e) => true;
    protected override bool OnScroll(ScrollEvent e) => true;
    protected override bool OnHover(HoverEvent e) => true;

    private partial class TitleBar : Container
    {
        private readonly DialogWindow window;

        public TitleBar(DialogWindow window)
        {
            this.window = window;
        }

        protected override bool OnDragStart(DragStartEvent e) => e.Button == MouseButton.Left;

        protected override void OnDrag(DragEvent e)
        {
            var parent = window.Parent;
            if (parent == null) return;

            var now = parent.ToLocalSpace(e.ScreenSpaceMousePosition);
            var last = parent.ToLocalSpace(e.ScreenSpaceLastMousePosition);
            window.Position += now - last;
        }
    }

    private partial class CloseButton : ClickableContainer
    {
        private Box background = null!;
        private SpriteIcon cross = null!;

        public CloseButton(Action close)
        {
            Action = close;
            Size = new Vector2(46, TITLE_HEIGHT);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.CloseHover, Alpha = 0 },
                cross = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(10),
                    Icon = FontAwesome.Solid.Times,
                    Colour = EditorColours.Dialog.Glyph,
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeIn(60);
            cross.FadeColour(Colour4.White, 60);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeOut(60);
            cross.FadeColour(EditorColours.Dialog.Glyph, 60);
        }
    }
}
