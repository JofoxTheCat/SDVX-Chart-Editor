using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Input;

namespace SDVXEditor.Game.Graphics.Containers;

/// <summary>
/// Top-most layer for drop-down lists and similar popups. A popup living inside a
/// flow would be covered by later siblings, so dropdowns hand their list to the
/// nearest <see cref="PopupLayer"/> instead. A press anywhere outside the popup
/// closes it (and is swallowed, like a native combo box); a press inside it is left
/// to the popup's own items.
/// </summary>
public partial class PopupLayer : Container
{
    private Drawable? current;
    private Drawable? anchor;
    private Action? onClosed;

    public bool IsOpen => current != null;

    /// <summary>The open popup (null when closed).</summary>
    public Drawable? Current => current;

    public PopupLayer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    /// <summary>Shows <paramref name="popup"/> directly under <paramref name="anchor"/>.</summary>
    /// <param name="matchWidth">Give the popup the anchor's width.</param>
    /// <remarks>
    /// A <see cref="PopupMenuList"/> inside the popup gets keyboard focus (Up/Down/Enter/Esc). The popup closes by itself
    /// when the anchor disappears (its dialog closed, its screen left).
    /// </remarks>
    public void Open(Drawable anchor, Drawable popup, bool matchWidth = true, Action? closed = null)
    {
        Close();

        var quad = ToLocalSpace(anchor.ScreenSpaceDrawQuad);
        popup.Anchor = Anchor.TopLeft;
        popup.Origin = Anchor.TopLeft;
        popup.Position = quad.BottomLeft;

        if (matchWidth)
            popup.Width = quad.Width;

        Add(popup);
        current = popup;
        this.anchor = anchor;
        onClosed = closed;

        // keep the list on screen, then hand it the keyboard (focus needs it alive: next frame)
        Schedule(() =>
        {
            if (current != popup) return;

            if (popup.Y + popup.DrawHeight > DrawHeight)
                popup.Y = Math.Max(0, quad.TopLeft.Y - popup.DrawHeight);
            if (popup.X + popup.DrawWidth > DrawWidth)
                popup.X = Math.Max(0, DrawWidth - popup.DrawWidth);

            if (findMenu(popup) is PopupMenuList menu)
                GetContainingFocusManager()?.ChangeFocus(menu);
        });
    }

    private static PopupMenuList? findMenu(Drawable d)
    {
        if (d is PopupMenuList menu) return menu;

        if (d is Container<Drawable> container)
        {
            foreach (var child in container.Children)
            {
                if (findMenu(child) is PopupMenuList found)
                    return found;
            }
        }

        return null;
    }

    public void Close()
    {
        if (current == null)
            return;

        var popup = current;
        var closed = onClosed;
        current = null;
        anchor = null;
        onClosed = null;

        Remove(popup, true);
        closed?.Invoke();
    }

    protected override void Update()
    {
        base.Update();

        // the control that opened the list went away (dialog closed, screen exited, page hidden)
        if (anchor != null && !isShown(anchor))
            Close();
    }

    /// <summary>Present all the way up and still in the same tree as this layer.</summary>
    private bool isShown(Drawable d)
    {
        Drawable top = d;
        for (Drawable? p = d; p != null; p = p.Parent)
        {
            if (!p.IsPresent) return false;

            top = p;
        }

        Drawable ownTop = this;
        while (ownTop.Parent != null) ownTop = ownTop.Parent;
        return top == ownTop;
    }

    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => current != null;

    protected override bool OnMouseDown(MouseDownEvent e)
    {
        if (current == null)
            return false;

        // Inside the popup: swallow the press (nothing behind may react) but keep the popup, so the item under the
        // cursor - which sits before this layer in the input queue - still receives the click on release.
        // Closing here, as this layer used to, removed the item before its click and no pick ever landed.
        if (current.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
            return true;

        Close();
        return true;
    }

    protected override bool OnScroll(ScrollEvent e) => current != null;
}

/// <summary>
/// One entry of a <see cref="PopupMenuList"/>: clickable, highlighted by hover and by the keyboard.
/// Subclasses draw the highlight in their own look.
/// </summary>
public abstract partial class PopupMenuItem : ClickableContainer
{
    private bool highlighted;

    public bool Highlighted
    {
        get => highlighted;
        set
        {
            if (highlighted == value && IsLoaded) return;

            highlighted = value;
            if (IsLoaded) UpdateHighlight(value);
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        UpdateHighlight(highlighted);
    }

    /// <summary>Draws the highlighted / normal state.</summary>
    protected abstract void UpdateHighlight(bool highlighted);

    protected override bool OnHover(HoverEvent e)
    {
        (Parent as PopupMenuList)?.Highlight(this);
        return true;
    }
}

/// <summary>
/// Vertical list of <see cref="PopupMenuItem"/>s in a popup. Takes keyboard focus when its <see cref="PopupLayer"/>
/// opens it: Up/Down (Home/End, PageUp/PageDown) move the highlight, Enter/Space pick, Esc closes. The highlight
/// follows the mouse too, like a Windows list.
/// </summary>
public partial class PopupMenuList : FillFlowContainer<PopupMenuItem>
{
    private int highlightedIndex = -1;

    [Resolved(CanBeNull = true)]
    private PopupLayer? layer { get; set; }

    public PopupMenuList()
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
    }

    public override bool AcceptsFocus => true;

    public int HighlightedIndex => highlightedIndex;

    public void Highlight(PopupMenuItem item) => Highlight(IndexOf(item));

    public void Highlight(int index)
    {
        highlightedIndex = Children.Count == 0 ? -1 : Math.Clamp(index, 0, Children.Count - 1);
        for (int i = 0; i < Children.Count; i++)
            Children[i].Highlighted = i == highlightedIndex;
    }

    private int IndexOf(PopupMenuItem item)
    {
        for (int i = 0; i < Children.Count; i++)
        {
            if (Children[i] == item) return i;
        }

        return -1;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        if (highlightedIndex < 0)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i].Highlighted) highlightedIndex = i;
            }
        }
    }

    protected override bool OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Highlight(highlightedIndex < 0 ? 0 : highlightedIndex - 1);
                return true;

            case Key.Down:
                Highlight(highlightedIndex + 1);
                return true;

            case Key.Home:
            case Key.PageUp:
                Highlight(0);
                return true;

            case Key.End:
            case Key.PageDown:
                Highlight(Children.Count - 1);
                return true;

            case Key.Enter:
            case Key.KeypadEnter:
            case Key.Space:
                if (highlightedIndex >= 0 && highlightedIndex < Children.Count)
                    Children[highlightedIndex].TriggerClick();
                else
                    layer?.Close();
                return true;

            case Key.Escape:
                layer?.Close();
                return true;

            case Key.Tab:
                layer?.Close();
                return true;
        }

        // everything else stays here while the list is open (a dialog must not take Enter as OK, a screen keys as shortcuts)
        return !e.ControlPressed && !e.AltPressed && !e.SuperPressed;
    }
}

/// <summary>Container that owns a <see cref="PopupLayer"/> above its content and caches it for descendants.</summary>
public partial class PopupHostContainer : Container
{
    private readonly Container content;
    private readonly PopupLayer popupLayer;

    protected override Container<Drawable> Content => content;

    /// <summary>The layer popups of this host's content open in.</summary>
    public PopupLayer PopupLayer => popupLayer;

    public PopupHostContainer()
    {
        RelativeSizeAxes = Axes.Both;
        AddRangeInternal(new Drawable[]
        {
            content = new Container { RelativeSizeAxes = Axes.Both },
            popupLayer = new PopupLayer(),
        });
    }

    protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
    {
        var deps = new DependencyContainer(base.CreateChildDependencies(parent));
        deps.Cache(popupLayer);
        return deps;
    }
}
