using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Input;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// Compose tools in toolbox order, keys 1-6. FX objects live under BT-A+B (FX-L) and
/// BT-C+D (FX-R); with an FX tool the click half picks the lane. Laser L/R is chosen by the
/// click side, Shift while drawing = wide laser.
/// </summary>
public enum EditorTool
{
    Select,
    BtChip,
    BtHold,
    FxChip,
    FxHold,
    Laser,
}

public static class EditorToolExtensions
{
    public static string Label(this EditorTool tool) => tool switch
    {
        EditorTool.BtChip => "BT Chip",
        EditorTool.BtHold => "BT Hold",
        EditorTool.FxChip => "FX Chip",
        EditorTool.FxHold => "FX Hold",
        _ => tool.ToString(),
    };

    /// <summary>The FX variant a BT tool turns into while Shift is held; other tools are unchanged.</summary>
    public static EditorTool ShiftVariant(this EditorTool tool) => tool.WithModifiers(true, false);

    /// <summary>The hold variant a chip tool turns into while the hold modifier is held; other tools are unchanged.</summary>
    public static EditorTool HoldVariant(this EditorTool tool) => tool.WithModifiers(false, true);

    /// <summary>
    /// The tool a click uses with Shift and/or the hold modifier held (Core <see cref="NoteToolVariant"/>): BT Chip +
    /// hold modifier = BT Hold, + Shift as well = FX Hold, FX Chip + hold modifier = FX Hold. A hold tool stays a hold
    /// tool; Select and Laser are unchanged.
    /// </summary>
    public static EditorTool WithModifiers(this EditorTool tool, bool shift, bool holdModifier)
    {
        if (tool is not (EditorTool.BtChip or EditorTool.BtHold or EditorTool.FxChip or EditorTool.FxHold)) return tool;

        bool fx = tool is EditorTool.FxChip or EditorTool.FxHold;
        bool hold = tool is EditorTool.BtHold or EditorTool.FxHold;
        var (rFx, rHold) = NoteToolVariant.Resolve(fx, hold, shift, holdModifier);
        return rFx ? rHold ? EditorTool.FxHold : EditorTool.FxChip : rHold ? EditorTool.BtHold : EditorTool.BtChip;
    }

    public static string KeyLabel(this HoldModifierKey key) => key == HoldModifierKey.Alt ? "Alt" : "Ctrl";
}

/// <summary>
/// Tool state of the editor: the chosen tool, whether Shift / the hold modifier are held, and the tool that a click
/// would actually use (<see cref="EffectiveTool"/>: BT Chip/Hold become FX Chip/Hold while Shift is down, BT/FX Chip
/// become BT/FX Hold while the hold modifier counts). The playfield reads <see cref="EffectiveTool"/>.
/// </summary>
public sealed class EditorToolState
{
    public readonly Bindable<EditorTool> Tool = new Bindable<EditorTool>(EditorTool.Select);
    public readonly BindableBool ShiftHeld = new BindableBool();

    /// <summary>Which key is the hold modifier (Options → Input, editor.ini <c>HoldModifier</c>).</summary>
    public readonly Bindable<HoldModifierKey> HoldModifier = new Bindable<HoldModifierKey>(HoldModifierKey.Ctrl);

    /// <summary>Which key snaps drags of existing laser points / anchors (Options → Input, editor.ini <c>DragSnapModifier</c>).</summary>
    public readonly Bindable<DragSnapKey> DragSnapModifier = new Bindable<DragSnapKey>(DragSnapKey.Shift);

    /// <summary>The hold modifier is down and counts (see Core <see cref="HoldModifierLatch"/>); fed by the editor screen.</summary>
    public IBindable<bool> HoldHeld => holdHeld;

    private readonly BindableBool holdHeld = new BindableBool();
    private readonly HoldModifierLatch latch = new HoldModifierLatch();

    /// <summary>
    /// The classic-laser key is down and counts (laser tool: K-Shoot style plain points, Core <c>ClassicLaser</c>). Same
    /// latch as the hold modifier: another key or an editor action while it is down uses it up until it is released.
    /// </summary>
    public IBindable<bool> ClassicLaserHeld => classicHeld;

    /// <summary>Label of the classic-laser key for the laser tile ("F"), set by the editor screen from the bindings.</summary>
    public readonly Bindable<string> ClassicKey = new Bindable<string>("F");

    private readonly BindableBool classicHeld = new BindableBool();
    private readonly HoldModifierLatch classicLatch = new HoldModifierLatch();

    /// <summary>Laser lane the laser tool draws on: 0 = left (blue), 1 = right (pink). Key 6 again or Q / E switch it.</summary>
    public readonly BindableInt LaserSide = new BindableInt { MinValue = 0, MaxValue = 1 };

    private readonly Bindable<EditorTool> effective = new Bindable<EditorTool>(EditorTool.Select);

    public IBindable<EditorTool> EffectiveTool => effective;

    public EditorToolState()
    {
        Tool.BindValueChanged(_ => update());
        ShiftHeld.BindValueChanged(_ => update());
        holdHeld.BindValueChanged(_ => update());
        HoldModifier.BindValueChanged(_ =>
        {
            latch.SetDown(false);
            holdHeld.Value = false;
        });
    }

    /// <summary>
    /// Feeds the state of Ctrl and Alt (key events and the per-frame poll of the editor screen, and every playfield press,
    /// so a press in the same frame as the key goes down already sees it). Shift stays event-driven in <see cref="ShiftHeld"/>.
    /// </summary>
    public void SetModifierKeys(bool ctrl, bool alt)
    {
        latch.SetDown(HoldModifier.Value == HoldModifierKey.Alt ? alt : ctrl);
        holdHeld.Value = latch.Active;
    }

    /// <summary>
    /// Another key, a wheel turn or an editor action happened: a held hold modifier was part of a combination, and so was
    /// a held classic-laser key unless <paramref name="classicKey"/> says the input was that key itself.
    /// </summary>
    public void OtherInput(bool classicKey = false)
    {
        latch.OtherInput();
        holdHeld.Value = latch.Active;
        if (classicKey) return;
        classicLatch.OtherInput();
        classicHeld.Value = classicLatch.Active;
    }

    /// <summary>Feeds whether the classic-laser key (its whole binding) is down (per-frame poll of the editor screen).</summary>
    public void SetClassicKey(bool down)
    {
        classicLatch.SetDown(down);
        classicHeld.Value = classicLatch.Active;
    }

    private void update() => effective.Value = Tool.Value.WithModifiers(ShiftHeld.Value, holdHeld.Value);
}

/// <summary>
/// Left toolbox of the compose tab (reference 02): two small dropdowns, then the big tool
/// tiles. The effective tool is bright, the rest dimmed; while Shift turns a BT tool into its
/// FX variant, the FX tile lights up instead and the BT tile's hint changes to "now FX". The hold
/// modifier (Ctrl or Alt) does the same for chip → hold ("Ctrl: Hold" / "now Hold").
/// </summary>
public partial class ComposeToolbox : CompositeDrawable
{
    public const float TILE_SIZE = 96;

    public readonly EditorToolState State;

    /// <summary>
    /// FX effect for new FX holds and, when changed with FX holds selected, for those (one undo step; the editor screen
    /// wires it). <see cref="NoEffect"/> or a kson effect name (preset or definition, <see cref="FxEffectChoices"/>).
    /// </summary>
    public readonly Bindable<string> FxEffect = new Bindable<string>(NoEffect);

    /// <summary>
    /// Slam sound for new slams and, when changed with slams selected, for those: "Auto" (no event, the default sound),
    /// "Down", "Up", "Swing", "Mute" (Core <c>SlamSounds</c>).
    /// </summary>
    public readonly Bindable<string> SlamSound = new Bindable<string>("Auto");

    public const string NoEffect = "None";

    /// <summary>The effect names offered (the chart's definitions + presets); set by the editor screen.</summary>
    public Func<IReadOnlyList<string>>? FxEffectChoices { get; set; }

    /// <summary>Display text of an effect name ("pitch_shift" → "PitchShift").</summary>
    public Func<string, string> FxEffectLabel { get; set; } = n => n;

    /// <summary>The two drop-downs (smoke test clicks them).</summary>
    public Drawable FxEffectDropdown { get; private set; } = null!;

    public Drawable SlamSoundDropdown { get; private set; } = null!;

    private readonly IBindable<EditorTool> effective = new Bindable<EditorTool>();

    /// <summary>The L | R switch under the laser tile.</summary>
    public LaserSideToggle SideToggle { get; private set; } = null!;

    /// <summary>The laser tool tile (shows the side as a big L / R badge).</summary>
    public ToolTile LaserTile { get; private set; } = null!;

    private readonly Dictionary<EditorTool, ToolTile> tileByTool = new Dictionary<EditorTool, ToolTile>();

    /// <summary>The tile of a tool (smoke test: hints and lit state).</summary>
    public ToolTile TileFor(EditorTool tool) => tileByTool[tool];

    public ComposeToolbox(EditorToolState state)
    {
        State = state;
        Width = TILE_SIZE + 24;
        AutoSizeAxes = Axes.Y;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var tiles = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 10),
            Margin = new MarginPadding { Top = 14 },
        };

        var defs = new (EditorTool Tool, IconUsage Icon, Colour4 Colour)[]
        {
            (EditorTool.Select, FontAwesome.Solid.MousePointer, Colour4.White),
            (EditorTool.BtChip, FontAwesome.Solid.Square, EditorColours.Lanes.Bt),
            (EditorTool.BtHold, FontAwesome.Solid.GripLinesVertical, EditorColours.Lanes.Bt),
            (EditorTool.FxChip, FontAwesome.Solid.Square, EditorColours.Lanes.Fx),
            (EditorTool.FxHold, FontAwesome.Solid.GripLinesVertical, EditorColours.Lanes.Fx),
            (EditorTool.Laser, FontAwesome.Solid.BezierCurve, EditorColours.Lanes.LaserLeft),
        };

        effective.BindTo(State.EffectiveTool);

        int key = 1;
        foreach (var (tool, icon, colour) in defs)
        {
            var tile = new ToolTile(icon, tool.Label(), (key++).ToString(), colour, TILE_SIZE)
            {
                Action = () => State.Tool.Value = tool,
            };

            tileByTool[tool] = tile;
            bool swaps = tool.ShiftVariant() != tool;
            bool holds = tool.HoldVariant() != tool;

            effective.BindValueChanged(e => tile.Active.Value = e.NewValue == tool, true);

            if (tool == EditorTool.Laser)
            {
                LaserTile = tile;
                State.LaserSide.BindValueChanged(e =>
                {
                    tile.IconColour.Value = e.NewValue == 0 ? EditorColours.Lanes.LaserLeft : EditorColours.Lanes.LaserRight;
                    tile.Badge.Value = e.NewValue == 0 ? "L" : "R";
                }, true);
            }

            // BT tiles advertise the Shift swap, chip tiles the hold modifier ("Ctrl: Hold"); while a swap is active on
            // the chosen tile its hint says so ("now FX" / "now Hold") and the tile of the effective tool lights up
            void updateHint()
            {
                // the laser tile shows its side as a badge; its second hint line advertises the classic-laser key
                if (tool == EditorTool.Laser)
                {
                    tile.Hint2.Value = State.ClassicLaserHeld.Value ? "classic lasers"
                        : State.ClassicKey.Value.Length > 0 ? State.ClassicKey.Value + ": classic" : string.Empty;
                    return;
                }

                bool chosen = State.Tool.Value == tool;
                tile.Hint.Value = !swaps ? string.Empty
                    : State.ShiftHeld.Value && chosen ? "now FX" : "Shift: FX";
                tile.Hint2.Value = !holds ? string.Empty
                    : State.HoldHeld.Value && chosen ? "now Hold" : State.HoldModifier.Value.KeyLabel() + ": Hold";
            }

            State.ShiftHeld.BindValueChanged(_ => updateHint());
            State.HoldHeld.BindValueChanged(_ => updateHint());
            State.HoldModifier.BindValueChanged(_ => updateHint());
            State.ClassicLaserHeld.BindValueChanged(_ => updateHint());
            State.ClassicKey.BindValueChanged(_ => updateHint());
            State.Tool.BindValueChanged(_ => updateHint(), true);
            Tutorial.TutorialAnchors.Register("compose.tool." + tool.ToString().ToLowerInvariant(), tile);
            tiles.Add(tile);

            if (tool == EditorTool.Laser)
            {
                tiles.Add(SideToggle = new LaserSideToggle(State) { Width = TILE_SIZE });
            }
        }

        var fx = new OsuDropdown<string>(new[] { NoEffect })
        {
            Width = TILE_SIZE + 20,
            ItemSource = () => new[] { NoEffect }.Concat(FxEffectChoices?.Invoke() ?? Array.Empty<string>()).ToList(),
            Formatter = n => n == NoEffect ? NoEffect : FxEffectLabel(n),
        };
        fx.Current.BindTo(FxEffect);
        FxEffectDropdown = fx;

        var slam = new OsuDropdown<string>(new[] { "Auto", "Down", "Up", "Swing", "Mute" })
        {
            Width = TILE_SIZE + 20,
        };
        slam.Current.BindTo(SlamSound);
        SlamSoundDropdown = slam;
        Tutorial.TutorialAnchors.Register("compose.fx-effect", fx);
        Tutorial.TutorialAnchors.Register("compose.slam-sound", slam);

        InternalChild = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                label("FX Effect"),
                fx,
                label("Slam Sound"),
                slam,
                tiles,
            },
        };
    }

    private static Drawable label(string text) => new SpriteText
    {
        Text = text,
        Font = EditorFont.Regular(19),
        Colour = Colour4.White,
        Shadow = true,
        Margin = new MarginPadding { Left = 6, Top = 4 },
    };
}

/// <summary>
/// Right toolbar toggles (reference 02 has New Combo / Whistle / … / Lock Notes; the
/// SDVX set per the mapping notes).
/// </summary>
public partial class ComposeToggleBar : CompositeDrawable
{
    public readonly BindableBool GridSnap = new BindableBool(true);
    public readonly BindableBool WideLaser = new BindableBool();
    public readonly BindableBool SlamAssist = new BindableBool(true);
    public readonly BindableBool LockNotes = new BindableBool();
    public readonly BindableBool MirrorPreview = new BindableBool();
    public readonly BindableBool Hitsounds = new BindableBool(true);

    /// <summary>A click on the Wide Laser tile (the editor screen also widens / narrows the selected lasers); null = just toggle.</summary>
    public Action? WideLaserClicked { get; set; }

    public ComposeToggleBar()
    {
        AutoSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChild = new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new Drawable[]
            {
                toggle(FontAwesome.Solid.Th, "Grid", "Snap", "T", GridSnap),
                wideTile(toggle(FontAwesome.Solid.ArrowsAltH, "Wide", "Laser", "W", WideLaser)),
                toggle(FontAwesome.Solid.Bolt, "Slam", "Assist", "S", SlamAssist),
                toggle(FontAwesome.Solid.Lock, "Lock", "Notes", "L", LockNotes),
                toggle(FontAwesome.Solid.ExchangeAlt, "Mirror", "Preview", "M", MirrorPreview),
                toggle(FontAwesome.Solid.VolumeUp, "Hit", "Sounds", "", Hitsounds),
            },
        };
    }

    private ToggleTile wideTile(ToggleTile tile)
    {
        tile.Action = () =>
        {
            if (WideLaserClicked is { } clicked) clicked();
            else WideLaser.Toggle();
        };
        return tile;
    }

    private static ToggleTile toggle(IconUsage icon, string top, string bottom, string key, BindableBool value)
    {
        var tile = new ToggleTile(icon, top, bottom, key);
        Tutorial.TutorialAnchors.Register("compose.toggle." + top.ToLowerInvariant(), tile);
        tile.Current.BindTo(value);
        return tile;
    }
}

/// <summary>
/// Two-segment switch "L | R" under the laser tile: the active side is filled in its laser colour (cyan / magenta),
/// a click picks the side and the laser tool. Q / E and pressing 6 again still switch it.
/// </summary>
public partial class LaserSideToggle : CompositeDrawable
{
    public const float HEIGHT = 26;

    private readonly EditorToolState state;

    public LaserSideToggle(EditorToolState state)
    {
        this.state = state;
        Height = HEIGHT;
        Masking = true;
        CornerRadius = 5;
        BorderThickness = 1.5f;
        BorderColour = Colour4.White.Opacity(0.35f);
    }

    /// <summary>The clickable half for a side (0 = L, 1 = R).</summary>
    public Drawable SegmentFor(int side) => segments[side];

    private readonly Segment[] segments = new Segment[2];

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            segments[0] = new Segment("L", EditorColours.Lanes.LaserLeft) { RelativeSizeAxes = Axes.Both, Width = 0.5f, Action = () => pick(0) },
            segments[1] = new Segment("R", EditorColours.Lanes.LaserRight)
            {
                RelativeSizeAxes = Axes.Both,
                Width = 0.5f,
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Action = () => pick(1),
            },
        };

        state.LaserSide.BindValueChanged(e =>
        {
            segments[0].Active = e.NewValue == 0;
            segments[1].Active = e.NewValue == 1;
        }, true);
    }

    private void pick(int side)
    {
        state.LaserSide.Value = side;
        state.Tool.Value = EditorTool.Laser;
    }

    private partial class Segment : ClickableContainer
    {
        private readonly Colour4 colour;
        private readonly Box background;
        private readonly SpriteText text;

        public Segment(string label, Colour4 colour)
        {
            this.colour = colour;
            Children = new Drawable[]
            {
                background = new Box { RelativeSizeAxes = Axes.Both },
                text = new SpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = label,
                    Font = EditorFont.Bold(19),
                },
            };
        }

        private bool active;

        public bool Active
        {
            get => active;
            set
            {
                active = value;
                background.FadeColour(value ? colour : EditorColours.Osu.TileBackground, 100);
                text.FadeColour(value ? Colour4.Black : colour, 100);
            }
        }
    }
}
