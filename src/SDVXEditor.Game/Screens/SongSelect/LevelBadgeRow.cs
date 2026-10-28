using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// All difficulties of the selected song as ∇ level triangles, in game order NOV · ADV · EXH · INF-class · MXM.
/// The INF position shows the song's own variant (GRV/HVN/VVD/XCD/NBL); a slot the song does not have is an empty
/// outline. The selected chart is drawn larger with a glow; clicking another triangle selects that chart.
/// </summary>
public partial class LevelBadgeRow : CompositeDrawable
{
    public const float BadgeWidth = 80;
    public const float SelectedWidth = 98;

    private readonly FillFlowContainer<LevelBadge> flow;

    /// <summary>A badge was clicked: select its chart.</summary>
    public event Action<ISongListItem>? ChartClicked;

    public IReadOnlyList<LevelBadge> Badges => flow.Children;

    public LevelBadgeRow()
    {
        AutoSizeAxes = Axes.Both;
        InternalChild = flow = new FillFlowContainer<LevelBadge>
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
        };
    }

    /// <summary>Shows the charts of the selected item's song; <paramref name="songCharts"/> are all list items of that song.</summary>
    public void Show(ISongListItem? selected, IEnumerable<ISongListItem> songCharts)
    {
        flow.Clear();
        if (selected == null) return;

        var charts = songCharts.ToList();
        for (int core = 0; core <= 4; core++)
        {
            // several charts can share a slot (two INF-class files); the selected one wins, else the first
            var inSlot = charts.Where(c => c.Slot.ToCore() == core).ToList();
            var item = inSlot.FirstOrDefault(c => c.ChartKey == selected.ChartKey) ?? inSlot.FirstOrDefault();
            var slot = item?.Slot ?? core switch
            {
                0 => DifficultySlot.Novice,
                1 => DifficultySlot.Advanced,
                2 => DifficultySlot.Exhaust,
                3 => DifficultySlot.Infinite,
                _ => DifficultySlot.Maximum,
            };
            bool isSelected = item != null && item.ChartKey == selected.ChartKey;

            var badge = new LevelBadge(slot, item, isSelected, isSelected ? SelectedWidth : BadgeWidth)
            {
                // bottom-aligned: the tips line up whatever the size
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
            };
            badge.Clicked += c => ChartClicked?.Invoke(c);
            flow.Add(badge);
        }
    }
}
