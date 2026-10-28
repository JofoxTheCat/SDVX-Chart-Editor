using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.SongSelect;

public enum ToastKind
{
    Info,
    Progress,
    Success,
    Error,
}

/// <summary>Small notifications stacked above the footer (import progress, export result, errors).</summary>
public partial class ToastStack : FillFlowContainer<ToastStack.Toast>
{
    /// <summary>Text of the newest toast, kept after it faded (smoke test).</summary>
    public string LastMessage { get; private set; } = string.Empty;

    public ToastStack()
    {
        AutoSizeAxes = Axes.Both;
        Direction = FillDirection.Vertical;
        Spacing = new Vector2(0, 8);
        LayoutDuration = 200;
        LayoutEasing = Easing.OutQuint;
    }

    /// <summary>Shows a toast. Progress toasts stay until <see cref="Toast.Finish"/>; the others fade after a while.</summary>
    public Toast Show(string text, ToastKind kind = ToastKind.Info)
    {
        var toast = new Toast(this);
        Add(toast);
        toast.Set(text, kind);
        return toast;
    }

    public partial class Toast : CompositeDrawable
    {
        private readonly ToastStack stack;
        private readonly Box accent;
        private readonly SpriteIcon icon;
        private readonly TextFlowContainer text;

        public Toast(ToastStack stack)
        {
            this.stack = stack;
            Anchor = Anchor.BottomRight;
            Origin = Anchor.BottomRight;
            Width = 620;
            AutoSizeAxes = Axes.Y;
            Masking = true;
            CornerRadius = 8;
            Alpha = 0;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Nabla.BgPanel.Opacity(0.94f) },
                accent = new Box { RelativeSizeAxes = Axes.Y, Width = 6 },
                icon = new SpriteIcon { Position = new Vector2(22, 16), Size = new Vector2(22) },
                text = new TextFlowContainer(t =>
                {
                    t.Font = EditorFont.Regular(22);
                    t.Colour = EditorColours.Nabla.Fg;
                })
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Left = 58, Right = 16, Vertical = 14 },
                },
            };
        }

        public void Set(string message, ToastKind kind)
        {
            stack.LastMessage = message;
            text.Text = message;

            var colour = kind switch
            {
                ToastKind.Error => EditorColours.Nabla.Danger,
                ToastKind.Success => EditorColours.Nabla.Primary,
                ToastKind.Progress => EditorColours.Nabla.ProfileBlue,
                _ => EditorColours.Nabla.FgMuted,
            };

            accent.Colour = colour;
            icon.Colour = colour;
            icon.Icon = kind switch
            {
                ToastKind.Error => FontAwesome.Solid.ExclamationTriangle,
                ToastKind.Success => FontAwesome.Solid.Check,
                ToastKind.Progress => FontAwesome.Solid.Spinner,
                _ => FontAwesome.Solid.InfoCircle,
            };

            ClearTransforms();
            icon.ClearTransforms();
            icon.Rotation = 0;
            LifetimeEnd = double.MaxValue;
            this.FadeIn(150);

            if (kind == ToastKind.Progress)
            {
                icon.Spin(1200, RotationDirection.Clockwise);
                return;
            }

            this.Delay(kind == ToastKind.Error ? 9000 : 5000).FadeOut(400).Expire();
        }

        /// <summary>Replaces a progress toast with its outcome.</summary>
        public void Finish(string message, ToastKind kind) => Set(message, kind);

        /// <summary>A click on the toast (e.g. "open the export folder"); null = not clickable.</summary>
        public Action? Clicked { get; set; }

        protected override bool OnClick(osu.Framework.Input.Events.ClickEvent e)
        {
            if (Clicked == null) return false;
            Clicked();
            return true;
        }

        protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e) => Clicked != null;
    }
}
