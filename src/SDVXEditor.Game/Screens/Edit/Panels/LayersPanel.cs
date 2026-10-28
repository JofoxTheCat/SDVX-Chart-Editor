using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;
using SDVXEditor.Game.Screens.Edit.Components;

namespace SDVXEditor.Game.Screens.Edit.Panels;

/// <summary>What the layers panel can hide (K-Shoot Editor's Layers: show / hide, "show only this").</summary>
public enum EditorLayer
{
    Bt,
    Fx,
    LaserLeft,
    LaserRight,

    /// <summary>BPM / scroll speed / stop markers beside the playfield.</summary>
    TimingMarkers,

    /// <summary>Bookmark lines in the timeline and the bottom summary.</summary>
    Bookmarks,

    /// <summary>Beat grid lines of the playfield.</summary>
    Grid,
}

/// <summary>
/// Visibility per <see cref="EditorLayer"/>, shared by the views. A hidden note / laser layer is neither drawn nor
/// selectable in Compose (it cannot be edited by accident); chart actions on an existing selection still apply.
/// </summary>
public sealed class LayerVisibility
{
    private readonly Dictionary<EditorLayer, BindableBool> layers = Enum.GetValues<EditorLayer>().ToDictionary(l => l, _ => new BindableBool(true));

    /// <summary>Raised after any layer changed.</summary>
    public event Action? Changed;

    public LayerVisibility()
    {
        foreach (var b in layers.Values) b.BindValueChanged(_ => Changed?.Invoke());
    }

    public BindableBool this[EditorLayer layer] => layers[layer];

    public bool IsVisible(EditorLayer layer) => layers[layer].Value;

    public bool AllVisible => layers.Values.All(b => b.Value);

    public static string Label(EditorLayer layer) => layer switch
    {
        EditorLayer.Bt => "BT notes",
        EditorLayer.Fx => "FX notes",
        EditorLayer.LaserLeft => "Laser L",
        EditorLayer.LaserRight => "Laser R",
        EditorLayer.TimingMarkers => "BPM / scroll / stop markers",
        EditorLayer.Bookmarks => "Bookmarks",
        EditorLayer.Grid => "Beat grid",
        _ => layer.ToString(),
    };

    /// <summary>Shows only <paramref name="layer"/> among the object layers (BT, FX, lasers); the others stay as they are.</summary>
    public void ShowOnly(EditorLayer layer)
    {
        foreach (var (l, b) in layers)
            b.Value = l == layer || !IsObjectLayer(l) && b.Value;
        layers[layer].Value = true;
    }

    public void ShowAll()
    {
        foreach (var b in layers.Values) b.Value = true;
    }

    public static bool IsObjectLayer(EditorLayer l) => l is EditorLayer.Bt or EditorLayer.Fx or EditorLayer.LaserLeft or EditorLayer.LaserRight;
}

/// <summary>Layers panel (Ctrl+Shift+U): a toggle per layer, "only" beside each object layer, Show all.</summary>
public partial class LayersPanel : SidePanel
{
    public LayerVisibility Layers { get; }

    /// <summary>The "only" buttons per layer (smoke test).</summary>
    public readonly Dictionary<EditorLayer, Drawable> OnlyButtons = new Dictionary<EditorLayer, Drawable>();

    public readonly Dictionary<EditorLayer, Drawable> Toggles = new Dictionary<EditorLayer, Drawable>();

    public Drawable ShowAllButton { get; private set; } = null!;

    public LayersPanel(LayerVisibility layers)
        : base("Layers")
    {
        Layers = layers;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        foreach (var layer in Enum.GetValues<EditorLayer>())
        {
            var toggle = new PinkCheckbox(LayerVisibility.Label(layer), 16);
            toggle.Current.BindTo(Layers[layer]);
            Toggles[layer] = toggle;

            var row = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 24,
                Child = toggle.With(t =>
                {
                    t.Anchor = Anchor.CentreLeft;
                    t.Origin = Anchor.CentreLeft;
                }),
            };

            if (LayerVisibility.IsObjectLayer(layer))
            {
                var only = PanelControls.Button("only", () => Layers.ShowOnly(layer), 54).With(b =>
                {
                    b.Anchor = Anchor.CentreRight;
                    b.Origin = Anchor.CentreRight;
                    b.Height = 22;
                });
                OnlyButtons[layer] = only;
                row.Add(only);
            }

            Content.Add(row);
        }

        Content.Add(ShowAllButton = PanelControls.Button("Show all", Layers.ShowAll, 100));
        Content.Add(PanelControls.Note("Hidden notes and lasers are not drawn and cannot be clicked or box-selected in Compose."));
    }
}
