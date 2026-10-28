using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Textures;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// The song wheel (reference 01), an osu!-style carousel: one panel per chart; the charts of a song form a
/// set that collapses into one song panel unless it holds the selection; optional group headers on top.
/// The selected panel sits at the vertical centre of the wheel, shifted left; the others step right on an
/// arc the farther they are. Panels are created only while they are near the viewport, so the wheel stays
/// cheap for large libraries.
/// <para>
/// Mouse wheel scrolls freely, Up/Down move by chart, Left/Right by song, PageUp/PageDown by a screen of
/// songs, Home/End to the ends. Click selects, double-click or Enter confirms, right-click asks for the
/// options overlay. Clicking a group header folds it.
/// </para>
/// </summary>
public partial class SongWheel : CompositeDrawable
{
    public const float PANEL_HEIGHT = 125;
    public const float PANEL_GAP = 5;
    public const float GROUP_HEIGHT = 56;

    /// <summary>Distance between two panel centres; the arc steps once per this distance.</summary>
    public const float PANEL_SPACING = PANEL_HEIGHT + PANEL_GAP;

    /// <summary>
    /// The charters of a collapsed song: the first one plus "+N" (audit U15: "a // b / c" mixed separators and was cut
    /// mid-name).
    /// </summary>
    public static string CharterSummary(IEnumerable<string> charters)
    {
        var list = charters.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct().ToList();
        return list.Count switch
        {
            0 => "unknown charter",
            1 => list[0],
            _ => $"{list[0]} +{list.Count - 1}",
        };
    }

    public readonly Bindable<ISongListItem?> Selected = new Bindable<ISongListItem?>();

    /// <summary>Double-click / Enter on a chart.</summary>
    public event Action<ISongListItem>? Confirmed;

    /// <summary>Right-click on a panel (the chart, or the song's first chart for a collapsed song).</summary>
    public event Action<ISongListItem>? ContextRequested;

    private IReadOnlyList<WheelGroup> groups = Array.Empty<WheelGroup>();
    private readonly HashSet<string> collapsedGroups = new HashSet<string>();
    private readonly Dictionary<string, string> lastChartOfSet = new Dictionary<string, string>();

    private List<Row> rows = new List<Row>();
    private readonly Dictionary<string, WheelPanel> panels = new Dictionary<string, WheelPanel>();
    private readonly Dictionary<string, float> setOrigins = new Dictionary<string, float>();

    private Container<WheelPanel> panelContainer = null!;
    private SpriteText emptyText = null!;
    private Container stickyHeader = null!;
    private SpriteText stickyTitle = null!;
    private SpriteText stickyCount = null!;
    private bool hasHeaders;

    /// <summary>Top of the wheel's visible area (the screen clips the panels below its search line).</summary>
    public float TopInset { get; set; }

    /// <summary>
    /// The group the topmost visible card belongs to, pinned at the top while its own header is scrolled away (audit
    /// U24: "By source" looked like it did nothing because the only header sat above the visible list). Null while hidden.
    /// </summary>
    public string? StickyGroupTitle => stickyHeader.Alpha > 0 ? stickyTitle.Text.ToString() : null;

    private float scrollPosition;
    private float scrollTarget;
    private float contentHeight;
    private bool snapNextLayout = true;

    /// <summary>Charts in display order (collapsed groups included), for navigation and tests.</summary>
    public IReadOnlyList<ISongListItem> Items { get; private set; } = Array.Empty<ISongListItem>();

    public IReadOnlyList<WheelGroup> Groups => groups;

    /// <summary>Shown when there is nothing to list.</summary>
    public string EmptyMessage
    {
        get => emptyText.Text.ToString();
        set => emptyText.Text = value;
    }

    /// <summary>Panels currently alive (visible or near), for tests.</summary>
    public int LivePanelCount => panels.Count;

    /// <summary>Screen-space vertical centre of the selected panel relative to the wheel's centre (0 = centred).</summary>
    public float SelectedOffsetFromCentre
    {
        get
        {
            var row = rows.FirstOrDefault(r => r.Chart != null && r.Chart == Selected.Value);
            return row == null ? float.NaN : row.Y + row.Height / 2 - scrollPosition;
        }
    }

    public SongWheel()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            panelContainer = new Container<WheelPanel> { RelativeSizeAxes = Axes.Both },
            stickyHeader = new Container
            {
                X = 8,
                Size = new Vector2(WheelPanel.WIDTH, 40),
                Masking = true,
                CornerRadius = 6,
                BorderThickness = 2,
                BorderColour = EditorColours.Osu.Rail.Opacity(0.8f),
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("0a1830").Opacity(0.95f) },
                    stickyTitle = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 52,
                        Font = EditorFont.Bold(24),
                        Colour = Colour4.White,
                        Shadow = true,
                    },
                    stickyCount = new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 640,
                        Font = EditorFont.Regular(19),
                        Colour = Colour4.White.Opacity(0.7f),
                    },
                },
            },
            emptyText = new SpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                X = -60,
                Font = EditorFont.Regular(30),
                Colour = Colour4.White.Opacity(0.8f),
                Shadow = true,
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Selected.BindValueChanged(e =>
        {
            if (e.NewValue is { } item)
            {
                lastChartOfSet[item.SongKey] = item.ChartKey;
                foreach (var g in groups.Where(g => g.Sets.Any(s => s.Charts.Contains(item))))
                    collapsedGroups.Remove(g.Key);
            }

            rebuildRows();
            centreOnSelection();
        }, true);
    }

    // ---- content -----------------------------------------------------------

    /// <summary>
    /// Replaces the content. The selection survives by <see cref="ISongListItem.ChartKey"/> (items are rebuilt on
    /// every library refresh); if the chart is gone, the item at the same position is selected.
    /// </summary>
    public void SetGroups(IReadOnlyList<WheelGroup> newGroups)
    {
        var previous = Selected.Value;
        int previousIndex = previous == null ? 0 : Math.Max(0, indexOf(previous));

        groups = newGroups;
        Items = groups.SelectMany(g => g.Sets).SelectMany(s => s.Charts).ToList();
        emptyText.Alpha = Items.Count == 0 ? 1 : 0;

        var keep = previous == null ? null : Items.FirstOrDefault(i => i.ChartKey == previous.ChartKey);
        var next = keep ?? (Items.Count > 0 ? Items[Math.Min(previousIndex, Items.Count - 1)] : null);

        if (next != previous)
        {
            Selected.Value = next; // rebuilds through the binding
        }
        else
        {
            rebuildRows();
            centreOnSelection();
        }
    }

    /// <summary>Selects the chart with this key (e.g. a freshly imported song). False if it is not listed.</summary>
    public bool SelectByKey(string chartOrSongKey)
    {
        var item = Items.FirstOrDefault(i => i.ChartKey == chartOrSongKey)
                   ?? Items.Where(i => string.Equals(i.SongKey, chartOrSongKey, StringComparison.OrdinalIgnoreCase)).MaxBy(i => i.DeclaredLevel);
        if (item == null) return false;

        Selected.Value = item;
        return true;
    }

    private int indexOf(ISongListItem item)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Items[i] == item || Items[i].ChartKey == item.ChartKey) return i;

        return -1;
    }

    // ---- navigation --------------------------------------------------------

    /// <summary>Moves the selection by charts.</summary>
    public void SelectRelative(int direction)
    {
        if (Items.Count == 0) return;

        int index = Selected.Value == null ? 0 : Math.Clamp(indexOf(Selected.Value) + direction, 0, Items.Count - 1);
        Selected.Value = Items[index];
    }

    /// <summary>Moves the selection by songs (sets), landing on the chart last used in that song.</summary>
    public void SelectSetRelative(int direction)
    {
        var sets = groups.SelectMany(g => g.Sets).ToList();
        if (sets.Count == 0) return;

        int current = Selected.Value == null ? -1 : sets.FindIndex(s => s.Charts.Contains(Selected.Value));
        int index = Math.Clamp(current + direction, 0, sets.Count - 1);
        selectSet(sets[index]);
    }

    private void selectSet(WheelSet set)
    {
        var chart = lastChartOfSet.TryGetValue(set.Key, out string? key) ? set.Charts.FirstOrDefault(c => c.ChartKey == key) : null;
        Selected.Value = chart ?? set.Charts.MaxBy(c => c.Slot);
    }

    private void toggleGroup(WheelGroup group)
    {
        if (!collapsedGroups.Remove(group.Key))
        {
            // folding the group that holds the selection would hide it: fold the others instead of moving it
            if (Selected.Value != null && group.Sets.Any(s => s.Charts.Contains(Selected.Value)))
                return;

            collapsedGroups.Add(group.Key);
        }

        rebuildRows();
        centreOnSelection();
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (e.ControlPressed || e.AltPressed) return base.OnKeyDown(e);

        int page = Math.Max(1, (int)(DrawHeight / PANEL_SPACING) - 1);

        switch (e.Key)
        {
            case Key.Up: SelectRelative(-1); return true;
            case Key.Down: SelectRelative(1); return true;
            case Key.Left: SelectSetRelative(-1); return true;
            case Key.Right: SelectSetRelative(1); return true;
            case Key.PageUp: SelectSetRelative(-page); return true;
            case Key.PageDown: SelectSetRelative(page); return true;
            case Key.Home: SelectSetRelative(-int.MaxValue / 2); return true;
            case Key.End: SelectSetRelative(int.MaxValue / 2); return true;

            case Key.Enter:
            case Key.KeypadEnter:
                if (Selected.Value != null && !e.Repeat)
                    Confirmed?.Invoke(Selected.Value);
                return true;
        }

        return base.OnKeyDown(e);
    }

    protected override bool OnScroll(ScrollEvent e)
    {
        scrollTarget = Math.Clamp(scrollTarget - SDVXEditor.Game.Input.WheelInput.Delta(e) * PANEL_SPACING * 1.2f, 0, Math.Max(0, contentHeight));
        return true;
    }

    // ---- layout ------------------------------------------------------------

    private void rebuildRows()
    {
        // where each set currently is on screen, so its charts unfold from there (and fold back into it)
        setOrigins.Clear();
        foreach (var p in panels.Values)
        {
            string? set = p.Row.Set?.Key ?? p.Row.Chart?.SongKey;
            if (set != null && (!setOrigins.TryGetValue(set, out float y) || p.CurrentY < y))
                setOrigins[set] = p.CurrentY;
        }

        var list = new List<Row>();
        float y0 = 0;
        bool headers = groups.Count > 1 || groups.Any(g => g.Title.Length > 0);
        hasHeaders = headers;
        var selected = Selected.Value;

        foreach (var group in groups)
        {
            bool holdsSelection = selected != null && group.Sets.Any(s => s.Charts.Contains(selected));
            bool collapsed = collapsedGroups.Contains(group.Key) && !holdsSelection;

            if (headers)
            {
                list.Add(new Row("g:" + group.Key, y0, GROUP_HEIGHT) { Group = group, Collapsed = collapsed, Owner = group });
                y0 += GROUP_HEIGHT + PANEL_GAP;
            }

            if (collapsed) continue;

            foreach (var set in group.Sets)
            {
                bool expanded = set.Charts.Count == 1 || (selected != null && set.Charts.Contains(selected));

                if (expanded)
                {
                    foreach (var chart in set.Charts)
                    {
                        list.Add(new Row($"c:{group.Key}:{chart.ChartKey}", y0, PANEL_HEIGHT) { Set = set, Chart = chart, Owner = group });
                        y0 += PANEL_SPACING;
                    }
                }
                else
                {
                    list.Add(new Row($"s:{group.Key}:{set.Key}", y0, PANEL_HEIGHT) { Set = set, Owner = group });
                    y0 += PANEL_SPACING;
                }
            }
        }

        rows = list;
        contentHeight = y0;

        // drop panels whose row is gone or changed kind (a set panel vs. its chart panels)
        var keys = rows.ToDictionary(r => r.Key);
        foreach (var (key, panel) in panels.ToList())
        {
            if (keys.TryGetValue(key, out var row) && panel.Matches(row))
            {
                panel.Row = row;
                panel.RowSelected = row.Chart != null && row.Chart == selected;
                continue;
            }

            panels.Remove(key);
            panelContainer.Remove(panel, true);
        }
    }

    private void centreOnSelection()
    {
        var row = rows.FirstOrDefault(r => r.Chart != null && r.Chart == Selected.Value);
        if (row != null)
            scrollTarget = row.Y + row.Height / 2;
        else
            scrollTarget = Math.Clamp(scrollTarget, 0, contentHeight);

        if (snapNextLayout && rows.Count > 0)
        {
            scrollPosition = scrollTarget;
            snapNextLayout = false;
        }
    }

    /// <summary>Jumps to the selection without animation (after entering the screen).</summary>
    public void SnapToSelection()
    {
        centreOnSelection();
        scrollPosition = scrollTarget;
    }

    protected override void Update()
    {
        base.Update();

        // frame-rate independent exponential damping
        float k = 1 - (float)Math.Exp(-Clock.ElapsedFrameTime / 70);
        scrollPosition += (scrollTarget - scrollPosition) * k;

        float centre = DrawHeight / 2;
        float reach = DrawHeight / 2 + PANEL_HEIGHT * 2;

        var visible = new HashSet<string>();

        foreach (var row in rows)
        {
            float rel = row.Y + row.Height / 2 - scrollPosition;
            if (Math.Abs(rel) > reach) continue;

            visible.Add(row.Key);

            if (!panels.TryGetValue(row.Key, out var panel))
            {
                panel = createPanel(row);
                string? set = row.Set?.Key;
                panel.CurrentY = set != null && setOrigins.TryGetValue(set, out float origin) ? origin : row.Y;
                panels[row.Key] = panel;
                panelContainer.Add(panel);
            }

            panel.CurrentY += (row.Y - panel.CurrentY) * k;
            panel.SelectionShift += ((panel.RowSelected ? 1 : 0) - panel.SelectionShift) * k;

            float y = centre + panel.CurrentY - scrollPosition;
            float dist = Math.Abs(y + row.Height / 2 - centre) / PANEL_SPACING;
            float x = (row.Group != null ? 8 : 28) + 20 * Math.Min(dist, 8) - 32 * panel.SelectionShift;

            panel.Position = new Vector2(x, y);
        }

        foreach (var (key, panel) in panels.ToList())
        {
            if (visible.Contains(key)) continue;

            panels.Remove(key);
            panelContainer.Remove(panel, true);
        }

        updateStickyHeader(centre);
    }

    private void updateStickyHeader(float centre)
    {
        // the topmost row that reaches into the visible area
        Row? top = null;
        foreach (var row in rows)
        {
            float y = centre + row.Y - scrollPosition;
            if (y + row.Height > TopInset + 4) { top = row; break; }
        }

        bool show = hasHeaders && top is { Group: null, Owner: { } };
        stickyHeader.Alpha = show ? 1 : 0;
        if (!show) return;

        var g = top!.Owner!;
        stickyHeader.Y = TopInset;
        if (stickyTitle.Text.ToString() != g.Title)
        {
            stickyTitle.Text = g.Title;
            stickyCount.Text = $"{g.Sets.Count} song{(g.Sets.Count == 1 ? "" : "s")} · {g.ChartCount} chart{(g.ChartCount == 1 ? "" : "s")}";
        }
    }

    private WheelPanel createPanel(Row row)
    {
        WheelPanel panel = row.Group != null ? new GroupPanel(row, this)
            : row.Chart != null ? new ChartPanel(row, this)
            : new SetPanel(row, this);

        panel.RowSelected = row.Chart != null && row.Chart == Selected.Value;
        return panel;
    }

    private void panelClicked(Row row)
    {
        if (row.Group != null) toggleGroup(row.Group);
        else if (row.Chart != null) Selected.Value = row.Chart;
        else if (row.Set != null) selectSet(row.Set);
    }

    private void panelDoubleClicked(Row row)
    {
        if (row.Chart != null && Selected.Value == row.Chart)
            Confirmed?.Invoke(row.Chart);
    }

    private void panelContext(Row row)
    {
        if (row.Group != null) return;

        if (row.Chart != null) Selected.Value = row.Chart;
        else if (row.Set != null) selectSet(row.Set);

        if (Selected.Value != null)
            ContextRequested?.Invoke(Selected.Value);
    }

    // ---- rows and panels ---------------------------------------------------

    private sealed class Row
    {
        public string Key { get; }
        public float Y { get; }
        public float Height { get; }
        public WheelGroup? Group { get; init; }
        public WheelSet? Set { get; init; }
        public ISongListItem? Chart { get; init; }
        public bool Collapsed { get; init; }

        /// <summary>The group this row belongs to (header rows: their own group).</summary>
        public WheelGroup? Owner { get; init; }

        public Row(string key, float y, float height)
        {
            Key = key;
            Y = y;
            Height = height;
        }
    }

    private abstract partial class WheelPanel : CompositeDrawable
    {
        public const float WIDTH = 1100;

        public Row Row;
        public float CurrentY;
        public float SelectionShift;

        protected readonly SongWheel Wheel;

        private bool rowSelected;

        public bool RowSelected
        {
            get => rowSelected;
            set
            {
                if (rowSelected == value && IsLoaded) return;

                rowSelected = value;
                if (IsLoaded) UpdateState();
            }
        }

        protected WheelPanel(Row row, SongWheel wheel)
        {
            Row = row;
            Wheel = wheel;
            Size = new Vector2(WIDTH, row.Height);
        }

        public virtual bool Matches(Row row) => true;

        protected abstract void UpdateState();

        protected override void LoadComplete()
        {
            base.LoadComplete();
            UpdateState();
        }

        protected override bool OnClick(ClickEvent e)
        {
            Wheel.panelClicked(Row);
            return true;
        }

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            Wheel.panelDoubleClicked(Row);
            return true;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            if (e.Button == MouseButton.Right)
            {
                Wheel.panelContext(Row);
                return true;
            }

            return e.Button == MouseButton.Left;
        }
    }

    /// <summary>Group header: a slim dark bar with the group name and chart count.</summary>
    private partial class GroupPanel : WheelPanel
    {
        private SpriteIcon chevron = null!;

        public GroupPanel(Row row, SongWheel wheel)
            : base(row, wheel)
        {
        }

        public override bool Matches(Row row) => row.Group == Row.Group && row.Collapsed == Row.Collapsed;

        [BackgroundDependencyLoader]
        private void load()
        {
            var group = Row.Group!;

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                BorderThickness = 2,
                BorderColour = EditorColours.Osu.Rail.Opacity(0.8f),
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = ColourInfo.GradientHorizontal(Colour4.FromHex("0a1830").Opacity(0.92f), Colour4.FromHex("0a1830").Opacity(0.5f)) },
                    chevron = new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        X = 28,
                        Size = new Vector2(18),
                        Icon = FontAwesome.Solid.ChevronDown,
                        Colour = EditorColours.Nabla.Primary,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 52,
                        Text = group.Title,
                        Font = EditorFont.Bold(28),
                        Colour = Colour4.White,
                        Shadow = true,
                    },
                    new SpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 640,
                        Text = $"{group.Sets.Count} song{(group.Sets.Count == 1 ? "" : "s")} · {group.ChartCount} chart{(group.ChartCount == 1 ? "" : "s")}",
                        Font = EditorFont.Regular(21),
                        Colour = Colour4.White.Opacity(0.7f),
                    },
                },
            };
        }

        protected override void UpdateState()
        {
            chevron.Rotation = Row.Collapsed ? -90 : 0;
        }
    }

    /// <summary>Shared look of song and chart panels: translucent blue card, jacket left, text right.</summary>
    private abstract partial class CardPanel : WheelPanel
    {
        protected const float JACKET_WIDTH = 160;

        private Box fill = null!;
        private Container frame = null!;
        protected Container Text = null!;

        protected CardPanel(Row row, SongWheel wheel)
            : base(row, wheel)
        {
        }

        protected abstract ISongListItem Representative { get; }

        protected abstract IEnumerable<Drawable> CreateText();

        [BackgroundDependencyLoader]
        private void load()
        {
            var item = Representative;

            InternalChild = frame = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 1, // radius 0 draws no border
                BorderThickness = 2.5f,
                BorderColour = Colour4.FromHex("a8c4ff").Opacity(0.5f),
                Children = new Drawable[]
                {
                    fill = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("0c2244").Opacity(0.62f) },
                    new Container
                    {
                        Size = new Vector2(JACKET_WIDTH, PANEL_HEIGHT),
                        Masking = true,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientVertical(
                                    Colour4.FromHSV(item.Hue, 0.5f, 0.85f),
                                    Colour4.FromHSV((item.Hue + 0.08f) % 1, 0.75f, 0.35f)),
                            },
                            new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(44),
                                Icon = FontAwesome.Solid.Music,
                                Colour = Colour4.White.Opacity(0.55f),
                            },
                            new AsyncImage(item.JacketPath, ImageVariant.Thumbnail) { RelativeSizeAxes = Axes.Both },
                        },
                    },
                    Text = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0.5f,
                        Children = CreateText().ToArray(),
                    },
                },
            };
        }

        protected override void UpdateState()
        {
            fill.FadeColour(RowSelected ? Colour4.FromHex("17305e").Opacity(0.97f) : Colour4.FromHex("0c2244").Opacity(0.62f), 150);
            frame.BorderColour = RowSelected ? EditorColours.Nabla.ProfileBlue : Colour4.FromHex("a8c4ff").Opacity(0.5f);
            Text.FadeTo(RowSelected ? 1 : IsHovered ? 0.8f : 0.5f, 150);
        }

        protected override bool OnHover(HoverEvent e)
        {
            UpdateState();
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => UpdateState();

        protected static Drawable Line(string text, float y, FontUsage font, Colour4 colour) => new SpriteText
        {
            Position = new Vector2(JACKET_WIDTH + 20, y),
            Text = text,
            Font = font,
            Colour = colour,
            Shadow = true,
            Truncate = true,
            MaxWidth = 640,
        };

        /// <summary>Small pill marking launcher songs ("their" library, edited as a copy).</summary>
        protected static Drawable SourceBadge(ISongListItem item) => new Container
        {
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 4,
            Alpha = item.Source == SongSource.Launcher ? 1 : 0,
            Margin = new MarginPadding { Left = 14, Top = 3 },
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Nabla.ProfileBlue.Opacity(0.85f) },
                new SpriteText
                {
                    Text = "LAUNCHER",
                    Font = EditorFont.Bold(14),
                    Colour = Colour4.FromHex("002434"),
                    Margin = new MarginPadding { Horizontal = 6, Vertical = 1 },
                },
            },
        };
    }

    private partial class ChartPanel : CardPanel
    {
        public ChartPanel(Row row, SongWheel wheel)
            : base(row, wheel)
        {
        }

        protected override ISongListItem Representative => Row.Chart!;

        public override bool Matches(Row row) => row.Chart != null && row.Chart.ChartKey == Row.Chart!.ChartKey && row.Chart == Row.Chart;

        protected override IEnumerable<Drawable> CreateText()
        {
            var item = Row.Chart!;
            var slotColour = item.Slot.Colour();

            var dashes = new FillFlowContainer
            {
                Position = new Vector2(JACKET_WIDTH + 22, 96),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10, 0),
            };

            for (int i = 1; i <= 20; i++)
            {
                dashes.Add(new Box
                {
                    Size = new Vector2(22, 7),
                    Colour = i <= item.Level ? slotColour : Colour4.White.Opacity(0.18f),
                });
            }

            yield return Line(item.Title, 4, EditorFont.Regular(34), Colour4.White);
            yield return Line($"{item.Artist} // {item.Charter}", 42, EditorFont.Regular(23), Colour4.White);
            yield return new FillFlowContainer
            {
                Position = new Vector2(JACKET_WIDTH + 20, 66),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Children = new[]
                {
                    new SpriteText
                    {
                        Text = $"{item.Slot.ShortName()} {SDVXEditor.Core.Charts.Difficulty.FormatLevel(item.DeclaredLevel)}",
                        Font = EditorFont.Bold(23),
                        Colour = slotColour,
                        Shadow = true,
                    },
                    SourceBadge(item),
                },
            };
            yield return dashes;
        }
    }

    /// <summary>A collapsed song: title, artist, and one chip per chart.</summary>
    private partial class SetPanel : CardPanel
    {
        public SetPanel(Row row, SongWheel wheel)
            : base(row, wheel)
        {
        }

        protected override ISongListItem Representative => Row.Set!.Charts.MaxBy(c => c.Slot)!;

        public override bool Matches(Row row) => row.Set != null && row.Chart == null && row.Set.Charts.SequenceEqual(Row.Set!.Charts);

        protected override IEnumerable<Drawable> CreateText()
        {
            var set = Row.Set!;
            var first = set.First;

            var chips = new FillFlowContainer
            {
                Position = new Vector2(JACKET_WIDTH + 20, 76),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
            };

            foreach (var chart in set.Charts)
            {
                chips.Add(new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    BorderThickness = 2,
                    BorderColour = chart.Slot.Colour(),
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.45f) },
                        new SpriteText
                        {
                            Text = $"{chart.Slot.ShortName()} {SDVXEditor.Core.Charts.Difficulty.FormatLevel(chart.DeclaredLevel)}",
                            Font = EditorFont.Bold(20),
                            Colour = chart.Slot.Colour(),
                            Margin = new MarginPadding { Horizontal = 8, Vertical = 3 },
                        },
                    },
                });
            }

            chips.Add(SourceBadge(first));

            yield return Line(first.Title, 4, EditorFont.Regular(34), Colour4.White);
            yield return Line($"{first.Artist} // {CharterSummary(set.Charts.Select(c => c.Charter))}", 42, EditorFont.Regular(23), Colour4.White);
            yield return chips;
        }
    }
}
