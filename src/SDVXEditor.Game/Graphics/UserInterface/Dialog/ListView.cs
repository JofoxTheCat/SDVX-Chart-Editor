using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;
using SDVXEditor.Game.Input;

namespace SDVXEditor.Game.Graphics.UserInterface.Dialog;

/// <summary>One column of a <see cref="ListView{T}"/>.</summary>
/// <param name="Header">Column header text.</param>
/// <param name="Width">Fixed width in pixels.</param>
/// <param name="Text">Cell text.</param>
/// <param name="Dot">Optional coloured dot drawn before the text (F6: red = BPM point, green = scroll speed).</param>
/// <param name="Icon">Optional glyph drawn instead of text (F6: ☆ marker; Roboto has no ☆).</param>
public sealed record ListViewColumn<T>(string Header, float Width, Func<T, string> Text, Func<T, Colour4?>? Dot = null, Func<T, IconUsage?>? Icon = null);

/// <summary>
/// Windows "details" list view: column headers, white rows, light-blue selection.
/// Click selects one row, Ctrl+click toggles, Shift+click selects a range. A click gives the list keyboard
/// focus: Up/Down (Home/End, PageUp/PageDown) move the selection, Shift extends it, Ctrl+A selects all.
/// </summary>
public partial class ListView<T> : CompositeDrawable
    where T : class
{
    public const float HEADER_HEIGHT = 22;
    public const float ROW_HEIGHT = 20;

    public readonly BindableList<T> Items = new BindableList<T>();
    public readonly BindableList<T> SelectedItems = new BindableList<T>();

    public event Action<T>? ItemDoubleClicked;

    private readonly IReadOnlyList<ListViewColumn<T>> columns;
    private FillFlowContainer<Row> rows = null!;
    private BasicScrollContainer scroll = null!;
    private int anchorIndex = -1;
    private int cursorIndex = -1;

    public ListView(IEnumerable<ListViewColumn<T>> columns)
    {
        this.columns = columns.ToList();
        Masking = true;
        CornerRadius = 1; // no border is drawn at radius 0
        BorderThickness = 1;
        BorderColour = EditorColours.Dialog.ListBorder;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var header = new Container { RelativeSizeAxes = Axes.X, Height = HEADER_HEIGHT };
        header.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window });

        float x = 0;
        foreach (var c in columns)
        {
            header.Add(new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = x + 6,
                Text = c.Header,
                Font = EditorFont.Dialog(),
                Colour = EditorColours.Dialog.Text,
            });
            x += c.Width;
            header.Add(new Box { X = x - 1, Width = 1, RelativeSizeAxes = Axes.Y, Colour = EditorColours.Dialog.HeaderSeparator });
        }

        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window },
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(1),
                Children = new Drawable[]
                {
                    header,
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Top = HEADER_HEIGHT },
                        Child = scroll = new BasicScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollbarOverlapsContent = false,
                            Child = rows = new FillFlowContainer<Row>
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                            },
                        },
                    },
                },
            },
        };

        Items.BindCollectionChanged((_, _) => rebuild(), true);
        SelectedItems.BindCollectionChanged((_, _) =>
        {
            foreach (var r in rows)
                r.Selected = SelectedItems.Contains(r.Item);
        }, true);
    }

    private void rebuild()
    {
        rows.Clear();

        for (int i = 0; i < Items.Count; i++)
            rows.Add(new Row(this, Items[i], i) { Selected = SelectedItems.Contains(Items[i]) });

        // drop selections of removed items
        foreach (var s in SelectedItems.ToArray())
        {
            if (!Items.Contains(s))
                SelectedItems.Remove(s);
        }
    }

    /// <summary>Selects exactly one item (or none).</summary>
    public void Select(T? item)
    {
        SelectedItems.Clear();
        if (item != null)
        {
            SelectedItems.Add(item);
            anchorIndex = cursorIndex = Items.IndexOf(item);
        }
    }

    public override bool AcceptsFocus => true;

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        if (Items.Count == 0 || e.AltPressed)
            return false;

        int pageRows = Math.Max(1, (int)(scroll.DrawHeight / ROW_HEIGHT) - 1);
        int from = cursorIndex >= 0 && cursorIndex < Items.Count ? cursorIndex : SelectedItems.Count > 0 ? Items.IndexOf(SelectedItems[^1]) : -1;
        int to;

        switch (e.Key)
        {
            // the key labelled A on the active layout (AZERTY: physical Q)
            case var _ when e.ControlPressed && KeyboardLayoutService.Logical(e.Key) == Key.A:
                foreach (var item in Items.Where(i => !SelectedItems.Contains(i)).ToList())
                    SelectedItems.Add(item);
                return true;

            case Key.Up: to = from - 1; break;
            case Key.Down: to = from + 1; break;
            case Key.PageUp: to = from - pageRows; break;
            case Key.PageDown: to = from + pageRows; break;
            case Key.Home: to = 0; break;
            case Key.End: to = Items.Count - 1; break;

            default:
                return false;
        }

        to = Math.Clamp(from < 0 ? 0 : to, 0, Items.Count - 1);

        if (e.ShiftPressed && anchorIndex >= 0)
        {
            int lo = Math.Min(anchorIndex, to), hi = Math.Max(anchorIndex, to);
            SelectedItems.Clear();
            SelectedItems.AddRange(Items.Skip(lo).Take(hi - lo + 1));
        }
        else
        {
            SelectedItems.Clear();
            SelectedItems.Add(Items[to]);
            anchorIndex = to;
        }

        cursorIndex = to;
        if (to < rows.Count)
            scroll.ScrollIntoView(rows.Children[to]);
        return true;
    }

    private void rowPressed(int index, bool ctrl, bool shift)
    {
        var item = Items[index];

        if (shift && anchorIndex >= 0)
        {
            int from = Math.Min(anchorIndex, index), to = Math.Max(anchorIndex, index);
            var range = Items.Skip(from).Take(to - from + 1).ToList();

            if (!ctrl)
                SelectedItems.Clear();

            foreach (var r in range.Where(r => !SelectedItems.Contains(r)))
                SelectedItems.Add(r);
            cursorIndex = index;
            return;
        }

        if (ctrl)
        {
            if (!SelectedItems.Remove(item))
                SelectedItems.Add(item);
        }
        else
        {
            SelectedItems.Clear();
            SelectedItems.Add(item);
        }

        anchorIndex = cursorIndex = index;
    }

    /// <summary>Re-reads the cell texts (call after editing an item in place).</summary>
    public void RefreshRows() => rebuild();

    private partial class Row : CompositeDrawable
    {
        public T Item { get; }

        private readonly ListView<T> list;
        private readonly int index;

        private Container selection = null!;
        private Box hover = null!;
        private bool selected;

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                if (selection != null)
                    selection.Alpha = value ? 1 : 0;
            }
        }

        public Row(ListView<T> list, T item, int index)
        {
            this.list = list;
            Item = item;
            this.index = index;
            RelativeSizeAxes = Axes.X;
            Height = ROW_HEIGHT;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            hover = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.HoverRow, Alpha = 0 };
            selection = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 1, // radius 0 draws no border
                    BorderThickness = 1,
                    BorderColour = EditorColours.Dialog.SelectionBorder,
                    Alpha = selected ? 1 : 0,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Selection },
                };

            var children = new List<Drawable> { hover, selection };

            float x = 0;
            foreach (var c in list.columns)
            {
                float textX = x + 6;
                var dot = c.Dot?.Invoke(Item);

                if (dot is Colour4 dotColour)
                {
                    children.Add(new Circle
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.Centre,
                        X = x + 10,
                        Size = new Vector2(6),
                        Colour = dotColour,
                    });
                    textX = x + 20;
                }

                if (c.Icon?.Invoke(Item) is IconUsage glyph)
                {
                    children.Add(new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = textX,
                        Size = new Vector2(10),
                        Icon = glyph,
                        Colour = EditorColours.Dialog.Text,
                    });
                }

                children.Add(new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = textX,
                    Text = c.Text(Item),
                    Font = EditorFont.Dialog(),
                    Colour = EditorColours.Dialog.Text,
                    Truncate = true,
                    MaxWidth = Math.Max(0, c.Width - (textX - x) - 2),
                });

                x += c.Width;
            }

            InternalChildren = children;
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            list.rowPressed(index, e.ControlPressed, e.ShiftPressed);
            return true;
        }

        // handled so the click gives the list keyboard focus (focus follows the drawable that took the click)
        protected override bool OnClick(ClickEvent e) => true;

        protected override bool OnDoubleClick(DoubleClickEvent e)
        {
            list.ItemDoubleClicked?.Invoke(Item);
            return true;
        }

        protected override bool OnHover(HoverEvent e)
        {
            hover.FadeIn(60);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => hover.FadeOut(60);
    }
}
