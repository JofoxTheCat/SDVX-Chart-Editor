using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Compose;

/// <summary>
/// Centre of the compose tab: the density/overview column and the playfield. The editor screen
/// puts a <see cref="DensityColumn"/> and a <see cref="ComposePlayfield"/> into the two containers.
/// </summary>
public partial class ComposeTabContent : CompositeDrawable
{
    /// <summary>Playfield including its marker margins (<see cref="PlayfieldLayout.TOTAL_WIDTH"/>).</summary>
    public const float PLAYFIELD_WIDTH = PlayfieldLayout.TOTAL_WIDTH;

    /// <summary>
    /// A playfield's container: the playfield plus <see cref="PlayfieldLayout.OVERHANG"/> free space on both sides,
    /// where wide lasers are drawn (and edited) at true scale past the lanes, as in game. The container masks, so
    /// nothing spills into the neighbouring column.
    /// </summary>
    public const float FIELD_WIDTH = PLAYFIELD_WIDTH + 2 * PlayfieldLayout.OVERHANG;

    public const float DENSITY_WIDTH = 134;

    /// <summary>Density column width while a reference is open (two playfields with overhang need the room).</summary>
    public const float DENSITY_WIDTH_WITH_REFERENCE = 100;

    public const float DENSITY_GAP = 4;

    /// <summary>Gap between the main playfield's and the reference playfield's overhang space.</summary>
    public const float REFERENCE_GAP = 8;

    /// <summary>Vertical minimap of the whole chart (reference 02, pink/yellow bars).</summary>
    public Container DensityContainer { get; private set; } = null!;

    /// <summary>The scrolling playfield goes here (inside the overhang padding).</summary>
    public Container PlayfieldContainer { get; private set; } = null!;

    /// <summary>
    /// "Open difficulty for reference" (osu!mania): a second, read-only playfield. With it open, the main chart sits
    /// in the left half and the reference in the right half — density | main | reference, each playfield with its
    /// wide-laser overhang: 100 + 4 + 2 x 760 + 8 = 1632 of 1920 design px, clear of the left toolbox (120) and the
    /// right toggle bar (124). Without a reference the main playfield is centred.
    /// </summary>
    public Container ReferenceContainer { get; private set; } = null!;

    public bool ReferenceOpen { get; private set; }

    public void SetReferenceOpen(bool open)
    {
        ReferenceOpen = open;
        if (!open)
        {
            DensityContainer.Width = DENSITY_WIDTH;
            PlayfieldContainer.X = 0;
            DensityContainer.X = -FIELD_WIDTH / 2 - DENSITY_GAP;
            ReferenceContainer.Alpha = 0;
            return;
        }

        float block = DENSITY_WIDTH_WITH_REFERENCE + DENSITY_GAP + 2 * FIELD_WIDTH + REFERENCE_GAP;
        float left = -block / 2; // relative to the screen centre
        DensityContainer.Width = DENSITY_WIDTH_WITH_REFERENCE;
        DensityContainer.X = left + DENSITY_WIDTH_WITH_REFERENCE;
        PlayfieldContainer.X = left + DENSITY_WIDTH_WITH_REFERENCE + DENSITY_GAP + FIELD_WIDTH / 2;
        ReferenceContainer.X = PlayfieldContainer.X + FIELD_WIDTH + REFERENCE_GAP;
        ReferenceContainer.Alpha = 1;
    }

    public ComposeTabContent()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            DensityContainer = new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopRight,
                X = -FIELD_WIDTH / 2 - DENSITY_GAP,
                Y = 6,
                Width = DENSITY_WIDTH,
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
            },
            PlayfieldContainer = new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 6,
                Width = FIELD_WIDTH,
                Padding = new MarginPadding { Horizontal = PlayfieldLayout.OVERHANG },
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
                Masking = true,
            },
            ReferenceContainer = new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 6,
                Width = FIELD_WIDTH,
                Padding = new MarginPadding { Horizontal = PlayfieldLayout.OVERHANG },
                RelativeSizeAxes = Axes.Y,
                Height = 0.99f,
                Masking = true,
                Alpha = 0,
            },
        };
    }
}

/// <summary>Placeholder centre for tabs that are not built yet (camera, effects).</summary>
public partial class PlaceholderTabContent : CompositeDrawable
{
    public PlaceholderTabContent(string title, string description)
    {
        RelativeSizeAxes = Axes.Both;
        InternalChild = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(900, 240),
            Masking = true,
            CornerRadius = 12,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Osu.PanelDark },
                new osu.Framework.Graphics.Sprites.SpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 50,
                    Text = title,
                    Font = EditorFont.Regular(40),
                    Colour = Colour4.White,
                },
                new TextFlowContainer(t =>
                {
                    t.Font = EditorFont.Regular(22);
                    t.Colour = EditorColours.TextMuted;
                })
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 120,
                    Width = 760,
                    AutoSizeAxes = Axes.Y,
                    TextAnchor = Anchor.TopCentre,
                    Text = description,
                },
            },
        };
    }
}
