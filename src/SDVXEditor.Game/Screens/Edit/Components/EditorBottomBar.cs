using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osuTK;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Screens.Edit.Input;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// Bottom bar (reference 02): time and percentage (click: jump-to-time dialog), the summary timeline (markers, a
/// position marker and seek-by-click; the part after the audio's end is hatched red when the chart goes on there),
/// the bookmark buttons (add, remove, previous, next) and Collab, Play / Pause / Stop, Test, and the playback rate
/// selector (25–100 %) with a white glowing underline under the active rate.
/// </summary>
public partial class EditorBottomBar : CompositeDrawable
{
    public const float HEIGHT = 45;

    /// <summary>The rates offered. osu!stable's editor stops at 100 % as well.</summary>
    public static readonly double[] Rates = { 0.25, 0.5, 0.75, 1 };

    private const float rate_button_width = 48;
    private const float rates_right = 8;
    private const float transport_right = rates_right + 4 * rate_button_width + 82;
    private const float bookmarks_right = transport_right + 222;
    private const float summary_right = bookmarks_right + 186;

    private readonly EditorClock playback;
    private readonly Action test;

    /// <summary>Runs an editor action (bookmark buttons); set by the screen.</summary>
    public Action<EditorAction>? Trigger { get; set; }

    /// <summary>Opens the collab / bookmark list window; set by the screen.</summary>
    public Action? OpenCollab { get; set; }

    /// <summary>Opens the jump-to-time dialog (click on the time); set by the screen.</summary>
    public Action? OpenTimeDialog { get; set; }

    /// <summary>Summary timeline content (kiai/effect spans, point ticks). Empty: filled later.</summary>
    public Container SummaryContent { get; private set; } = null!;

    private SpriteText timeText = null!;
    private SpriteText percentText = null!;
    private Box marker = null!;
    private Container summary = null!;
    private Container beyondAudio = null!;
    private FillFlowContainer<RateButton> rateFlow = null!;

    /// <summary>The clickable time display (smoke test).</summary>
    public Drawable TimeDisplay { get; private set; } = null!;

    public BarButton AddBookmarkButton { get; private set; } = null!;
    public BarButton RemoveBookmarkButton { get; private set; } = null!;
    public BarButton PreviousBookmarkButton { get; private set; } = null!;
    public BarButton NextBookmarkButton { get; private set; } = null!;
    public BarButton CollabButton { get; private set; } = null!;

    /// <summary>The rate buttons, slowest first (smoke test).</summary>
    public IReadOnlyList<RateButton> RateButtons => rateFlow.Children;

    /// <summary>True while the summary marks a stretch of chart after the audio's end.</summary>
    public bool BeyondAudioShown => beyondAudio.Alpha > 0;

    public EditorBottomBar(EditorClock playback, Action test)
    {
        this.playback = playback;
        this.test = test;
        RelativeSizeAxes = Axes.X;
        Height = HEIGHT;
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        static string key(EditorAction a) => EditorKeyBindings.ShortcutOf(a) is { Length: > 0 } k ? $" ({k})" : "";

        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.82f) },
            TimeDisplay = new TimeButton(() => OpenTimeDialog?.Invoke(),
                timeText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 8,
                    Font = EditorFont.Regular(22),
                    Colour = Colour4.White,
                },
                percentText = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 140,
                    Font = EditorFont.Regular(20),
                    Colour = Colour4.White,
                })
            {
                X = 18,
            },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Left = 240, Right = summary_right, Vertical = 6 },
                Child = summary = new SeekArea(playback)
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        beyondAudio = new BeyondAudioStretch
                        {
                            RelativePositionAxes = Axes.X,
                            RelativeSizeAxes = Axes.Both,
                            Alpha = 0,
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Colour = Colour4.White.Opacity(0.5f),
                        },
                        SummaryContent = new Container { RelativeSizeAxes = Axes.Both },
                        marker = new Box
                        {
                            RelativePositionAxes = Axes.X,
                            RelativeSizeAxes = Axes.Y,
                            Width = 2,
                            Origin = Anchor.TopCentre,
                            Colour = Colour4.White,
                        },
                    },
                },
            },
            new FillFlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -bookmarks_right,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
                Children = new Drawable[]
                {
                    AddBookmarkButton = new BarButton(FontAwesome.Solid.Bookmark, FontAwesome.Solid.Plus, () => Trigger?.Invoke(EditorAction.AddBookmark),
                        () => "Add bookmark at the cursor" + key(EditorAction.AddBookmark)),
                    RemoveBookmarkButton = new BarButton(FontAwesome.Solid.Bookmark, FontAwesome.Solid.Minus, () => Trigger?.Invoke(EditorAction.RemoveBookmark),
                        () => "Remove the bookmark nearest to the cursor (within 2 s)" + key(EditorAction.RemoveBookmark)),
                    PreviousBookmarkButton = new BarButton(FontAwesome.Solid.Bookmark, FontAwesome.Solid.ChevronLeft, () => Trigger?.Invoke(EditorAction.PreviousBookmark),
                        () => "Previous bookmark" + key(EditorAction.PreviousBookmark), badgeLeft: true),
                    NextBookmarkButton = new BarButton(FontAwesome.Solid.Bookmark, FontAwesome.Solid.ChevronRight, () => Trigger?.Invoke(EditorAction.NextBookmark),
                        () => "Next bookmark" + key(EditorAction.NextBookmark)),
                    new Box { Width = 1, Height = 22, Anchor = Anchor.CentreLeft, Origin = Anchor.CentreLeft, Colour = Colour4.White.Opacity(0.4f) },
                    CollabButton = new BarButton(FontAwesome.Solid.UserFriends, null, () => OpenCollab?.Invoke(),
                        () => "Collab bookmarks: labels and colours", width: 36),
                },
            },
            new FillFlowContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -transport_right,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(16, 0),
                Children = new Drawable[]
                {
                    new IconButton(FontAwesome.Solid.Play, () => playback.IsPlaying.Value = true),
                    new IconButton(FontAwesome.Solid.Pause, () => playback.IsPlaying.Value = false),
                    new IconButton(FontAwesome.Solid.Stop, playback.Stop),
                    new IconButton(FontAwesome.Solid.Video, test, "Test"),
                },
            },
            new SpriteText
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                X = -(rates_right + 4 * rate_button_width + 8),
                Y = -3,
                Text = "Playback",
                Font = EditorFont.Regular(17),
                Colour = Colour4.White,
            },
            rateFlow = new FillFlowContainer<RateButton>
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                X = -rates_right,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
            },
        };

        foreach (double r in Rates)
            rateFlow.Add(new RateButton(r, () => playback.Rate.Value = r));
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        playback.Rate.BindValueChanged(e =>
        {
            foreach (var b in rateFlow)
                b.Active = Math.Abs(b.Rate - e.NewValue) < 0.001;
        }, true);
    }

    protected override void Update()
    {
        base.Update();

        double t = playback.CurrentTime.Value;
        timeText.Text = ChartTimestamp.FormatTime(t);

        double length = playback.LengthMs;
        double fraction = length > 0 ? t / length : 0;
        percentText.Text = (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        marker.X = (float)fraction;

        if (playback.ChartBeyondAudio && length > 0)
        {
            float audioEnd = (float)(playback.AudioLengthMs / length);
            beyondAudio.X = audioEnd;
            beyondAudio.Width = 1 - audioEnd;
            beyondAudio.Alpha = 1;
        }
        else
            beyondAudio.Alpha = 0;
    }

    /// <summary>
    /// The chart's stretch after the audio's end: hatched red (its words are the timeline's tooltip there); the label only shows where it
    /// fits (audit U3: a 7 px dark-red "no audio" ran under the bookmark buttons).
    /// </summary>
    private partial class BeyondAudioStretch : Container
    {
        private readonly SpriteText label;

        public BeyondAudioStretch()
        {
            Masking = true;
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("ff3b3b").Opacity(0.28f) },
                new Box { RelativeSizeAxes = Axes.Y, Width = 2, Colour = Colour4.FromHex("ff3b3b") },
                label = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 6,
                    Text = "no audio",
                    Font = EditorFont.Bold(15),
                    Colour = Colour4.FromHex("ffd6d6"),
                    Shadow = true,
                },
            };
        }

        protected override void Update()
        {
            base.Update();
            label.Alpha = DrawWidth >= label.DrawWidth + 12 ? 1 : 0;
        }
    }

    private partial class SeekArea : Container
    {
        private readonly EditorClock playback;

        public SeekArea(EditorClock playback)
        {
            this.playback = playback;
        }

        private void seek(Vector2 screenSpace)
        {
            if (DrawWidth <= 0) return;

            float x = Math.Clamp(ToLocalSpace(screenSpace).X / DrawWidth, 0, 1);
            playback.CurrentTime.Value = x * playback.LengthMs;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            seek(e.ScreenSpaceMousePosition);
            return true;
        }

        protected override bool OnDragStart(DragStartEvent e) => true;

        protected override void OnDrag(DragEvent e) => seek(e.ScreenSpaceMousePosition);
    }

    /// <summary>Time + percentage: click opens the jump-to-time dialog (osu!stable).</summary>
    private partial class TimeButton : ClickableContainer, IHasTooltip
    {
        private readonly Box hover;

        public TimeButton(Action action, params Drawable[] content)
        {
            Action = action;
            Size = new Vector2(212, HEIGHT - 10);
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            Masking = true;
            CornerRadius = 4;
            Add(hover = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White, Alpha = 0 });
            AddRange(content);
        }

        public LocalisableString TooltipText => "Jump to a time or a modding timestamp (click)";

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeTo(0.12f, 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hover.FadeOut(80);
            base.OnHoverLost(e);
        }
    }

    /// <summary>A small icon button of the bar with an optional badge icon (bookmark +, −, ‹, ›) and a tooltip.</summary>
    public partial class BarButton : ClickableContainer, IHasTooltip
    {
        private readonly Func<string> tooltip;
        private readonly Container content;

        public BarButton(IconUsage icon, IconUsage? badge, Action action, Func<string> tooltip, bool badgeLeft = false, float width = 28)
        {
            this.tooltip = tooltip;
            Action = action;
            Size = new Vector2(width, 30);
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;

            var children = new List<Drawable>
            {
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = icon,
                    Size = new Vector2(badge == null ? 24 : 17),
                    X = badge == null ? 0 : badgeLeft ? 3 : -3,
                },
            };

            if (badge is IconUsage b)
            {
                children.Add(new SpriteIcon
                {
                    Anchor = badgeLeft ? Anchor.CentreLeft : Anchor.CentreRight,
                    Origin = badgeLeft ? Anchor.CentreLeft : Anchor.CentreRight,
                    Icon = b,
                    Size = new Vector2(10),
                    Y = 5,
                });
            }

            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Colour = Colour4.White,
                Children = children,
            };
        }

        public LocalisableString TooltipText => tooltip();

        protected override bool OnHover(HoverEvent e)
        {
            content.FadeColour(EditorColours.Osu.Pink.Lighten(0.4f), 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            content.FadeColour(Colour4.White, 80);
            base.OnHoverLost(e);
        }
    }

    private partial class IconButton : ClickableContainer
    {
        private readonly FillFlowContainer content;

        public IconButton(IconUsage icon, Action action, string? text = null)
        {
            Action = action;
            AutoSizeAxes = Axes.Both;
            Child = content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(4, 0),
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = icon,
                        Size = new Vector2(text == null ? 26 : 30, 26),
                        Colour = Colour4.White,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = text ?? string.Empty,
                        Font = EditorFont.Regular(26),
                        Colour = Colour4.White,
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            content.FadeColour(EditorColours.Osu.Pink.Lighten(0.4f), 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            content.FadeColour(Colour4.White, 80);
            base.OnHoverLost(e);
        }
    }

    /// <summary>One rate; the active one is white with a glowing white underline (the old arrow covered the number).</summary>
    public partial class RateButton : ClickableContainer
    {
        public double Rate { get; }

        private readonly SpriteText text;
        private readonly Container underline;

        /// <summary>True while this is the current rate (smoke test).</summary>
        public bool IsActive { get; private set; }

        public bool Active
        {
            set
            {
                IsActive = value;
                text.FadeColour(value ? Colour4.White : Colour4.FromHex("9fb8ff"), 100);
                text.FadeTo(value ? 1 : 0.45f, 100);
                underline.FadeTo(value ? 1 : 0, 100);
            }
        }

        public RateButton(double rate, Action action)
        {
            Rate = rate;
            Action = action;
            Size = new Vector2(rate_button_width, HEIGHT);
            Children = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Width = 1,
                    Height = 18,
                    Y = -4,
                    Colour = Colour4.White.Opacity(0.6f),
                },
                text = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Y = -2,
                    Text = $"{rate * 100:0}%",
                    Font = EditorFont.Regular(20),
                    Colour = Colour4.FromHex("9fb8ff"),
                },
                underline = new Container
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Y = -7,
                    Size = new Vector2(34, 2.5f),
                    Masking = true,
                    CornerRadius = 1.25f,
                    Alpha = 0,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = Colour4.White.Opacity(0.75f),
                        Radius = 6,
                    },
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White },
                },
            };
        }
    }
}
