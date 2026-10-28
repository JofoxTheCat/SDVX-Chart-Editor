using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osuTK;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Play;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>Static layout of song select (reference 01): header, left panel, top-right controls, footer.</summary>
public partial class SongSelectScreen
{
    // ---- layout pieces -----------------------------------------------------

    private static SpriteText text(string s, FontUsage font) => new SpriteText
    {
        Text = s,
        Font = font,
        Colour = Colour4.White,
        Shadow = true,
    };

    /// <summary>Widest value of a details row: the panel minus the widest label ("Illustrator:") and a gap.</summary>
    public const float DETAIL_VALUE_WIDTH = left_width - 18 - 150;

    private static Drawable row(string label, string value, Colour4? valueColour = null, bool gapAfter = false) => new DetailRow(label, value, valueColour)
    {
        Margin = new MarginPadding { Bottom = gapAfter ? 18 : 0 },
    };

    /// <summary>
    /// "Label:   value" with the value right-aligned and cut with "…" before it reaches the label (audit B2: a long
    /// charter ran over "Charter:"); the full value is the tooltip.
    /// </summary>
    public partial class DetailRow : Container
    {
        public SpriteText Label { get; }
        public TruncatingText Value { get; }

        public DetailRow(string label, string value, Colour4? valueColour)
        {
            RelativeSizeAxes = Axes.X;
            Height = 30;
            Children = new Drawable[]
            {
                Label = new SpriteText { Text = label, Font = EditorFont.Bold(23), Colour = Colour4.White, Shadow = true },
                Value = new TruncatingText
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Text = value,
                    Font = EditorFont.Regular(21),
                    Colour = valueColour ?? Colour4.White,
                    Shadow = true,
                    MaxWidth = DETAIL_VALUE_WIDTH,
                    Truncate = true,
                },
            };
        }
    }

    /// <summary>A header line, cut with "…" before it reaches the Group / Sort controls on the black band (audit B2).</summary>
    private static SpriteText headerText(string s, FontUsage font) => new SpriteText
    {
        Text = s,
        Font = font,
        Colour = Colour4.White,
        Shadow = true,
        MaxWidth = 1080,
        Truncate = true,
    };

    private Drawable createLeftPanel() => new Container
    {
        Position = new Vector2(0, left_header_height + 50),
        Width = left_width,
        AutoSizeAxes = Axes.Y,
        Padding = new MarginPadding { Horizontal = 9 },
        Children = new Drawable[]
        {
            new Container
            {
                Position = new Vector2(21, 10),
                Size = new Vector2(470, 55),
                Masking = true,
                CornerRadius = 1, // radius 0 draws no border
                BorderThickness = 3,
                BorderColour = Colour4.White,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.35f) },
                    statusIcon = new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        X = 34,
                        Size = new Vector2(30),
                        Icon = FontAwesome.Solid.Heart,
                        Colour = Colour4.White,
                    },
                    statusText = new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        X = 24,
                        Font = EditorFont.Regular(32),
                        Colour = EditorColours.Nabla.Primary,
                    },
                },
            },
            copyBadge = new Container
            {
                Position = new Vector2(21, 72),
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 4,
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Nabla.ProfileBlue.Opacity(0.85f) },
                    new SpriteText
                    {
                        Text = "Launcher song — edits are saved as a copy",
                        Font = EditorFont.Bold(18),
                        Colour = Colour4.FromHex("002434"),
                        Margin = new MarginPadding { Horizontal = 8, Vertical = 2 },
                    },
                },
            },
            new FillFlowContainer
            {
                Y = 110,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 16),
                Children = new Drawable[]
                {
                    details = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                    },
                    supportButton = new SupportArtistButton(SupportButtonStyle.Card) { SongTitle = () => supportTitle, Margin = new MarginPadding { Top = 14 } },
                },
            },
        },
    };

    /// <summary>
    /// Middle column between the details and the wheel: the chart's skill radar and, under it, the song's
    /// difficulties as ∇ level triangles.
    /// </summary>
    private Drawable createChartPanel() => chartPanel = new FillFlowContainer
    {
        Position = new Vector2(left_width + (wheel_left - left_width) / 2, left_header_height + 18),
        Origin = Anchor.TopCentre,
        AutoSizeAxes = Axes.Both,
        Direction = FillDirection.Vertical,
        Spacing = new Vector2(0, 18),
        Children = new Drawable[]
        {
            radar = new SkillRadarDisplay { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre },
            levelBadges = new LevelBadgeRow { Anchor = Anchor.TopCentre, Origin = Anchor.TopCentre },
        },
    };

    private FillFlowContainer chartPanel = null!;

    /// <summary>Gap the radar column keeps to the details column and to the wheel.</summary>
    public const float CHART_PANEL_GAP = 12;

    /// <summary>The radar + level column (smoke test: it must not reach into the details column).</summary>
    public Drawable ChartPanel => chartPanel;

    /// <summary>
    /// The radar column gets the room between the details and the wheel and no more: its bounds (all six labels) were
    /// wider than that and covered the right-aligned detail values ("Tutoria", "Edito", the dates).
    /// </summary>
    private void fitChartPanel()
    {
        float room = wheel_left - left_width - 2 * CHART_PANEL_GAP;
        float natural = chartPanel.DrawWidth;
        float scale = natural > room && natural > 0 ? room / natural : 1;
        if (Math.Abs(chartPanel.Scale.X - scale) > 0.001f) chartPanel.Scale = new Vector2(scale);
    }

    private Drawable createTopChrome()
    {
        var groupDropdown = new OsuDropdown<SongGrouping>(Enum.GetValues<SongGrouping>())
        {
            Bordered = true,
            Width = 268,
            Height = 34,
            FontSize = 25,
            Formatter = groupName,
        };
        groupDropdown.Current.BindTo(Grouping);

        var sortDropdown = new OsuDropdown<SongSort>(Enum.GetValues<SongSort>())
        {
            Bordered = true,
            Width = 268,
            Height = 34,
            FontSize = 25,
            Formatter = sortName,
        };
        sortDropdown.Current.BindTo(Sort);

        var search = new SearchBox { Width = 440 };
        search.Current.BindTo(Search);

        return new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                // header band and the rounded left block; the rail is their border
                new Container
                {
                    Position = new Vector2(-100, -100),
                    Size = new Vector2(left_width + 100 + 90, left_header_height + 100),
                    Masking = true,
                    CornerRadius = 90,
                    BorderThickness = rail,
                    BorderColour = EditorColours.Osu.Rail,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = header_height,
                    Padding = new MarginPadding { Left = left_width + 90 - rail },
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
                        new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = rail,
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Colour = EditorColours.Osu.Rail,
                        },
                    },
                },
                header = new FillFlowContainer
                {
                    Position = new Vector2(8, 2),
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                },
                // group / sort
                new FillFlowContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Position = new Vector2(-20, 36),
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Children = new Drawable[]
                    {
                        topLabel("Group"),
                        groupDropdown,
                        new Container { Width = 16 },
                        topLabel("Sort"),
                        sortDropdown,
                    },
                },
                new FilterTabs(this)
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Position = new Vector2(-24, 78),
                },
                new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Position = new Vector2(-20, 116),
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.9f) }, // panels scroll underneath
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(8, 0),
                            Padding = new MarginPadding { Horizontal = 8, Vertical = 2 },
                            Children = new Drawable[]
                            {
                        new SpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "Search:",
                            Font = EditorFont.Bold(26),
                            Colour = EditorColours.Osu.SearchGreen,
                            Shadow = true,
                        },
                        search,
                    },
                },
                    },
                },
            },
        };
    }

    private static Drawable topLabel(string s) => new SpriteText
    {
        Anchor = Anchor.CentreLeft,
        Origin = Anchor.CentreLeft,
        Text = s,
        Font = EditorFont.Regular(44),
        Colour = EditorColours.Osu.SongSelectLabel,
    };

    private Drawable createFooter() => new Container
    {
        Anchor = Anchor.BottomLeft,
        Origin = Anchor.BottomLeft,
        RelativeSizeAxes = Axes.X,
        Height = footer_height,
        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
            new Box { RelativeSizeAxes = Axes.X, Height = rail, Colour = EditorColours.Osu.Rail },
            exitButton = new FooterButton("EXIT", null, 64, () => game.Exit())
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(30, -10),
            },
            new FillFlowContainer
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(330, -22),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(36, 0),
                Children = new Drawable[]
                {
                    new FooterButton("Random", FontAwesome.Solid.Random, 30, () => wheel.SelectSetRelative(Random.Shared.Next(-6, 7))),
                    new FooterButton("Options", FontAwesome.Solid.Cog, 30, OpenEditorOptions), // the options overlay; chart options: right-click
                    Tutorial.TutorialAnchorExtensions.WithTutorialAnchor(new FooterButton("Tutorial", FontAwesome.Solid.GraduationCap, 30, () => (game as EditorGame)?.Tutorial.ShowChapterList()), "songselect.tutorial"),
                    thanksButton = new FooterButton("Thanks", FontAwesome.Solid.Heart, 30, () => OpenThanks()),
                },
            },
            new FlatButton("PLAY", EditorColours.Nabla.BgElevated, null, 30)
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Position = new Vector2(-300, 6),
                Size = new Vector2(150, 70),
                CornerRadius = 35,
                BorderTint = EditorColours.Nabla.Primary,
                Action = () => this.Push(new PlaytestScreen()),
            },
            new FlatButton("EDIT", EditorColours.Nabla.Primary, EditorColours.Nabla.Accent, 40)
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Position = new Vector2(-40, 6),
                Size = new Vector2(240, 96),
                CornerRadius = 48,
                TextColour = EditorColours.Nabla.PrimaryInk,
                Action = EditSelected,
            },
        },
    };

    /// <summary>Dark-red tab row under group/sort: quick filters (Collections · Recently edited · By charter · My charts · No grouping).</summary>
    private partial class FilterTabs : CompositeDrawable
    {
        private readonly SongSelectScreen screen;

        public FilterTabs(SongSelectScreen screen)
        {
            this.screen = screen;
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var flow = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
            };

            foreach (var (label, tab) in new (string, SongSelectTab)[]
                     {
                         ("Collections", SongSelectTab.Collections),
                         ("Recently edited", SongSelectTab.RecentlyEdited),
                         ("By charter", SongSelectTab.ByCharter),
                         ("My charts", SongSelectTab.MyCharts),
                         ("No grouping", SongSelectTab.NoGrouping),
                     })
            {
                flow.Add(new Tab(label, tab, screen));
            }

            InternalChild = flow;
        }

        private partial class Tab : ClickableContainer
        {
            private readonly SongSelectTab tab;
            private readonly SongSelectScreen screen;
            private readonly IBindable<SongSelectTab> active;
            private Box background = null!;

            public Tab(string label, SongSelectTab tab, SongSelectScreen screen)
            {
                this.tab = tab;
                this.screen = screen;
                active = screen.ActiveTab.GetBoundCopy();
                Size = new Vector2(172, 28);
                Action = () => this.screen.SelectTab(this.tab);

                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = osu.Framework.Graphics.Colour.ColourInfo.GradientVertical(
                            EditorColours.Osu.FilterTabRed.Lighten(0.3f), EditorColours.Osu.FilterTabRed),
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = label,
                        Font = EditorFont.Bold(19),
                        Colour = Colour4.White,
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                active.BindValueChanged(e => background.FadeTo(tab == e.NewValue ? 0.45f : 1, 100), true);
            }
        }
    }

    /// <summary>The search line: plain text on the dark band, no visible box until typed into.</summary>
    private partial class SearchBox : OsuTextBox
    {
        public SearchBox()
        {
            Height = 34;
            FontSize = 26;
            PlaceholderText = "type to search!";
            BackgroundUnfocused = Colour4.Transparent;
            BackgroundFocused = Colour4.White.Opacity(0.08f);
            BorderThickness = 0;
        }
    }

    private FooterButton exitButton = null!;
    private FooterButton thanksButton = null!;

    /// <summary>The footer's Thanks button (smoke test clicks it).</summary>
    public Drawable ThanksButton => thanksButton;

    /// <summary>Footer → Thanks: the credits list (the same dialog as the editor's menu bar → Thanks).</summary>
    public Edit.Dialogs.CreditsDialog OpenThanks()
    {
        if (dialogs.Current is Edit.Dialogs.CreditsDialog open) return open;
        var dialog = new Edit.Dialogs.CreditsDialog();
        dialogs.Push(dialog);
        return dialog;
    }

    /// <summary>The footer's left button label ("EXIT", smoke test).</summary>
    public string ExitLabel => exitButton.Text;

    /// <summary>Alt+wheel → volume where <c>isVolumeArea</c> says so; plain wheel and everything else pass through.</summary>
    private partial class AltWheelVolumeLayer : Drawable
    {
        private readonly Func<Vector2, bool> isVolumeArea;
        private readonly Action<int> adjust;

        public AltWheelVolumeLayer(Func<Vector2, bool> isVolumeArea, Action<int> adjust)
        {
            this.isVolumeArea = isVolumeArea;
            this.adjust = adjust;
            RelativeSizeAxes = Axes.Both;
        }

        protected override bool OnScroll(ScrollEvent e)
        {
            if (!e.AltPressed || e.ControlPressed || e.ShiftPressed || !isVolumeArea(e.ScreenSpaceMousePosition) || e.ScrollDelta.Y == 0)
                return false;

            adjust(Math.Sign(e.ScrollDelta.Y));
            return true;
        }
    }

    private partial class FooterButton : ClickableContainer
    {
        private readonly FillFlowContainer content;

        public string Text { get; }

        public FooterButton(string text, IconUsage? icon, float size, Action action)
        {
            Text = text;
            Action = action;
            AutoSizeAxes = Axes.Both;
            Child = content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(size * 0.2f, 0),
                Alpha = 0.85f,
            };

            if (icon is IconUsage i)
            {
                content.Add(new SpriteIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(size * 0.6f),
                    Icon = i,
                    Colour = Colour4.White,
                });
            }

            content.Add(new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Text = text,
                Font = EditorFont.Bold(size),
                Colour = Colour4.White,
            });
        }

        protected override bool OnHover(HoverEvent e)
        {
            content.FadeTo(1, 80).FadeColour(EditorColours.Nabla.Primary, 80);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            content.FadeTo(0.85f, 80).FadeColour(Colour4.White, 80);
            base.OnHoverLost(e);
        }
    }
}
