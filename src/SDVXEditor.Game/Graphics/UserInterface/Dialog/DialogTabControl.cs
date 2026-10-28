using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;

namespace SDVXEditor.Game.Graphics.UserInterface.Dialog;

/// <summary>
/// Windows tab control: a row of small tab headers over a bordered white page.
/// The selected header is white, a little taller, and merges into the page.
/// </summary>
public partial class DialogTabControl : CompositeDrawable
{
    public const float HEADER_HEIGHT = 22;

    public readonly BindableInt SelectedIndex = new BindableInt();

    private readonly FillFlowContainer<Header> headers;
    private readonly Container pages;
    private readonly List<Drawable> pageList = new List<Drawable>();

    public IReadOnlyList<string> PageTitles => headers.Children.Select(h => h.Title).ToList();

    /// <param name="pagePadding">Inner padding of the page area (0 for a list filling the page).</param>
    public DialogTabControl(float pagePadding = 6)
    {
        InternalChildren = new Drawable[]
        {
            new Container
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding { Top = HEADER_HEIGHT },
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    BorderThickness = 1,
                    BorderColour = EditorColours.Dialog.TabBorder,
                    Masking = true,
                    CornerRadius = 1,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window },
                        pages = new Container { RelativeSizeAxes = Axes.Both, Padding = new MarginPadding(pagePadding) },
                    },
                },
            },
            headers = new FillFlowContainer<Header>
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                X = 2,
            },
        };

        SelectedIndex.BindValueChanged(e =>
        {
            for (int i = 0; i < pageList.Count; i++)
                pageList[i].Alpha = i == e.NewValue ? 1 : 0;

            foreach (var h in headers)
                h.Selected = h.Index == e.NewValue;
        });
    }

    /// <summary>Adds a page; its content fills the page area.</summary>
    public void AddPage(string title, Drawable content)
    {
        int index = pageList.Count;
        content.Alpha = index == SelectedIndex.Value ? 1 : 0;
        pageList.Add(content);
        pages.Add(content);
        headers.Add(new Header(title, index, () => SelectedIndex.Value = index) { Selected = index == SelectedIndex.Value });
    }

    private partial class Header : ClickableContainer
    {
        public string Title { get; }
        public int Index { get; }

        private readonly Box background;
        private readonly Box hover;
        private readonly SpriteText text;

        private bool selected;

        public bool Selected
        {
            get => selected;
            set
            {
                selected = value;
                background.Colour = value ? EditorColours.Dialog.Window : EditorColours.Dialog.Body;
                Y = value ? 0 : 2;
                Height = value ? HEADER_HEIGHT + 1 : HEADER_HEIGHT - 2;
                text.Y = value ? 0 : -1;
            }
        }

        public Header(string title, int index, Action action)
        {
            Title = title;
            Index = index;
            Action = action;
            AutoSizeAxes = Axes.X;
            Height = HEADER_HEIGHT;
            Margin = new MarginPadding { Right = -1 };

            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Body },
                hover = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.ButtonHover, Alpha = 0 },
                text = new SpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = title,
                    Font = EditorFont.Dialog(),
                    Colour = EditorColours.Dialog.Text,
                    Margin = new MarginPadding { Horizontal = 6 },
                },
                // left, top and right edge; no bottom edge so the selected tab merges into the page
                new Box { RelativeSizeAxes = Axes.Y, Width = 1, Colour = EditorColours.Dialog.TabBorder },
                new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = EditorColours.Dialog.TabBorder },
                new Box { RelativeSizeAxes = Axes.Y, Width = 1, Anchor = Anchor.TopRight, Origin = Anchor.TopRight, Colour = EditorColours.Dialog.TabBorder },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (!selected) hover.FadeIn(60);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e) => hover.FadeOut(60);
    }
}
