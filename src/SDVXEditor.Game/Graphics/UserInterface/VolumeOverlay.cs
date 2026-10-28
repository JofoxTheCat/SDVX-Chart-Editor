using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Configuration;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// osu!-style volume control, bottom-right: a big master ring with music and effects rings
/// beside it. Appears while adjusting (Alt+wheel over the side areas), the hovered ring is
/// the one that changes (master otherwise), fades out after ~1 s. Values live in
/// <see cref="EditorConfigManager"/>.
/// </summary>
public partial class VolumeOverlay : CompositeDrawable
{
    private const double hide_delay = 1000;

    [Resolved]
    private EditorConfigManager config { get; set; } = null!;

    private Meter master = null!;
    private Meter music = null!;
    private Meter effects = null!;
    private Drawable content = null!;

    public VolumeOverlay()
    {
        Anchor = Anchor.BottomRight;
        Origin = Anchor.BottomRight;
        AutoSizeAxes = Axes.Both;
        Alpha = 0;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = content = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(16, 0),
            Margin = new MarginPadding(24),
            Children = new Drawable[]
            {
                effects = new Meter("Effects", config.GetBindable<double>(EditorSetting.VolumeEffects), 110) { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft },
                music = new Meter("Music", config.GetBindable<double>(EditorSetting.VolumeMusic), 110) { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft },
                master = new Meter("Master", config.GetBindable<double>(EditorSetting.VolumeMaster), 170) { Anchor = Anchor.BottomLeft, Origin = Anchor.BottomLeft },
            },
        };
    }

    /// <summary>Changes the hovered (or master) volume by <paramref name="direction"/> steps of 5 %.</summary>
    public void Adjust(int direction)
    {
        var target = music.IsHovered ? music : effects.IsHovered ? effects : master;
        target.Value.Value = Math.Clamp(Math.Round(target.Value.Value + direction * 0.05, 2), 0, 1);

        this.FadeIn(100);
        // keep it up while the cursor rests on it; otherwise fade after the delay
        Scheduler.CancelDelayedTasks();
        Scheduler.AddDelayed(hideIfIdle, hide_delay);
    }

    private void hideIfIdle()
    {
        if (IsHovered)
        {
            Scheduler.AddDelayed(hideIfIdle, hide_delay);
            return;
        }

        this.FadeOut(300);
    }

    protected override bool OnScroll(ScrollEvent e)
    {
        if (Alpha <= 0)
            return false;

        Adjust(Math.Sign(e.ScrollDelta.Y));
        return true;
    }

    private partial class Meter : CompositeDrawable
    {
        public readonly Bindable<double> Value;

        private readonly string label;
        private CircularProgress progress = null!;
        private SpriteText valueText = null!;
        private Circle background = null!;

        public Meter(string label, Bindable<double> value, float size)
        {
            this.label = label;
            // the caller passes config.GetBindable(): already a bound copy, linked to the stored setting only by
            // weak references. Keep that copy itself; a further GetBoundCopy() would leave the intermediate copy
            // unreferenced, and once a GC collects it the meter no longer reads or writes the setting.
            Value = value;
            Size = new Vector2(size);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                background = new Circle { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.75f) },
                new CircularProgress
                {
                    RelativeSizeAxes = Axes.Both,
                    InnerRadius = 0.12f,
                    Progress = 1,
                    Colour = Colour4.White.Opacity(0.12f),
                },
                progress = new CircularProgress
                {
                    RelativeSizeAxes = Axes.Both,
                    InnerRadius = 0.12f,
                    RoundedCaps = true,
                    Colour = EditorColours.Osu.Pink,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        valueText = new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = EditorFont.Regular(Width * 0.24f),
                            Colour = Colour4.White,
                        },
                        new SpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Text = label,
                            Font = EditorFont.Bold(Math.Max(18, Width * 0.15f)), // was ~13 px: unreadable (audit U16)
                            Colour = Colour4.White.Opacity(0.85f),
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Value.BindValueChanged(e =>
            {
                progress.TransformTo(nameof(CircularProgress.Progress), e.NewValue, 120, Easing.OutQuint);
                valueText.Text = $"{e.NewValue * 100:0}";
            }, true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(Colour4.FromHex("2a2a2a"), 100);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => background.FadeColour(Colour4.Black.Opacity(0.75f), 100);
    }
}

/// <summary>Short message above the bottom bar that fades out (e.g. "Undo: not wired yet").</summary>
public partial class EditorToast : CompositeDrawable
{
    private SpriteText? text;

    /// <summary>A message shown before the toast was loaded (it appears once it is).</summary>
    private string? pending;

    public EditorToast()
    {
        Anchor = Anchor.BottomCentre;
        Origin = Anchor.BottomCentre;
        AutoSizeAxes = Axes.Both;
        Alpha = 0;
        Masking = true;
        CornerRadius = 8;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.8f) },
            text = new SpriteText
            {
                Font = EditorFont.Regular(24),
                Colour = Colour4.White,
                Margin = new MarginPadding { Horizontal = 18, Vertical = 8 },
                Text = pending ?? string.Empty,
            },
        };
    }

    public string LastMessage => text?.Text.ToString() ?? pending ?? string.Empty;

    /// <summary>How many messages were shown (smoke test: a repeated message still counts as feedback).</summary>
    public int ShowCount { get; private set; }

    public void Show(string message)
    {
        ShowCount++;

        // the owner may report something while it is still loading (its toast not built yet): keep the message
        if (text == null)
        {
            pending = message;
            return;
        }

        text.Text = message;
        this.FadeIn(100).Then().Delay(1500).FadeOut(400);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        if (pending != null) Show(pending);
    }
}
