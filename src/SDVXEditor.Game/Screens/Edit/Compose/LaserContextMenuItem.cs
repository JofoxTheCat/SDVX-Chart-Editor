using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Containers;

namespace SDVXEditor.Game.Screens.Edit.Compose;

/// <summary>
/// One row of the laser context menu (right click on an anchor or a laser path in select mode): a check mark for the
/// current type, a colour chip matching the anchor colours, the label. Title rows are dim and do nothing.
/// </summary>
public partial class LaserContextMenuItem : PopupMenuItem
{
    public const float HEIGHT = 28;

    public LaserMenuItem Item { get; }

    public string Label => Item.Label;

    private Box highlight = null!;

    public LaserContextMenuItem(LaserMenuItem item)
    {
        Item = item;
        RelativeSizeAxes = Axes.X;
        Height = HEIGHT;
        Enabled.Value = item.Enabled && !item.IsTitle;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        LaserCurveType? type = Item.Label switch
        {
            "Straight" => LaserCurveType.Linear,
            "Perfect curve" => LaserCurveType.PerfectCurve,
            "Bezier" => LaserCurveType.Bezier,
            _ => null,
        };

        Children = new Drawable[]
        {
            highlight = new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.Pink, Alpha = 0 },
            new SpriteIcon
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 8,
                Size = new Vector2(11),
                Icon = FontAwesome.Solid.Check,
                Alpha = Item.Checked ? 1 : 0,
            },
            new Box
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = 26,
                Size = new Vector2(10),
                Colour = type is { } t ? ComposePlayfield.AnchorColour(t) : Item.Label == "Inherit" ? ComposePlayfield.AnchorColour(null) : Colour4.Transparent,
            },
            new SpriteText
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                X = Item.IsTitle ? 8 : 44,
                Text = Item.Label,
                Font = Item.IsTitle ? EditorFont.Bold(15) : EditorFont.Regular(17),
                Colour = Item.IsTitle ? EditorColours.TextMuted : Item.Enabled ? Colour4.White : Colour4.White.Opacity(0.35f),
            },
        };
    }

    protected override void UpdateHighlight(bool highlighted) => highlight.FadeTo(highlighted && Enabled.Value ? 0.55f : 0, 80);

    /// <summary>
    /// The anchor / curve-type popup (laser paths in compose, camera anchors in the camera tab): one row per item; a pick
    /// calls <paramref name="close"/> first, then the item's action.
    /// </summary>
    public static FillFlowContainer CreateMenu(IReadOnlyList<LaserMenuItem> items, Action close)
    {
        var list = new PopupMenuList();
        foreach (var item in items)
        {
            var row = new LaserContextMenuItem(item);
            row.Action = () =>
            {
                close();
                item.Run?.Invoke();
            };
            list.Add(row);
        }

        return new FillFlowContainer
        {
            Width = 190,
            AutoSizeAxes = Axes.Y,
            Masking = true,
            CornerRadius = 5,
            BorderThickness = 1,
            BorderColour = Colour4.White.Opacity(0.25f),
            Children = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("161616").Opacity(0.96f) },
                        list,
                    },
                },
            },
        };
    }
}
