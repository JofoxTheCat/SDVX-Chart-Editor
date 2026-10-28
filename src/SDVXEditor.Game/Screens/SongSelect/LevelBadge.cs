using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osuTK;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// One difficulty as SOUND VOLTEX ∇ shows it: a downward triangle (the nabla) with a thick bevelled rim in the
/// difficulty colour, a dark face, the difficulty name on the top edge and the level inside. Drawn from primitives;
/// proportions (rim ≈ 17 % of the width, height ≈ 0.88 × width) and the three rim tones per difficulty were measured
/// on the game's level artwork — no game image or font is used.
/// </summary>
public partial class LevelBadge : CompositeDrawable
{
    /// <summary>Rim tones measured per difficulty: outer edge, main band, inner edge (game artwork, ∇ 2026071400).</summary>
    public readonly record struct Palette(Colour4 Outer, Colour4 Main, Colour4 Inner, Colour4 Top);

    public static Palette PaletteOf(DifficultySlot slot) => slot switch
    {
        DifficultySlot.Novice => new Palette(hex("4a3cc6"), hex("a79ef2"), hex("392ca4"), hex("6b5de4")),
        DifficultySlot.Advanced => new Palette(hex("c6983c"), hex("f2d69e"), hex("a47d2c"), hex("e4b75d")),
        DifficultySlot.Exhaust => new Palette(hex("c63c3c"), hex("f29e9e"), hex("a42d2c"), hex("e45d5d")),
        DifficultySlot.Infinite => new Palette(hex("b25093"), hex("e6aad3"), hex("923d76"), hex("d070b1")),
        DifficultySlot.Gravity => new Palette(hex("c6713c"), hex("f2be9e"), hex("a45b2c"), hex("e4915d")),
        DifficultySlot.Heavenly => new Palette(hex("3ca1c6"), hex("9edbf2"), hex("2c83a4"), hex("5dc0e4")),
        DifficultySlot.Vivid => new Palette(hex("dc2580"), hex("ff91c8"), hex("b71866"), hex("fa47a0")),
        DifficultySlot.Exceed => new Palette(hex("347ecd"), hex("99c6f6"), hex("2564aa"), hex("569eeb")),
        DifficultySlot.Nabla => new Palette(hex("aecd34"), hex("e3f699"), hex("8eaa25"), hex("cdeb56")),
        _ => new Palette(hex("818181"), hex("c8c8c8"), hex("686868"), hex("a0a0a0")), // MAXIMUM
    };

    public static string FullName(DifficultySlot slot) => slot switch
    {
        DifficultySlot.Novice => "NOVICE",
        DifficultySlot.Advanced => "ADVANCED",
        DifficultySlot.Exhaust => "EXHAUST",
        DifficultySlot.Infinite => "INFINITE",
        DifficultySlot.Gravity => "GRAVITY",
        DifficultySlot.Heavenly => "HEAVENLY",
        DifficultySlot.Vivid => "VIVID",
        DifficultySlot.Exceed => "EXCEED",
        DifficultySlot.Nabla => "NABLA",
        _ => "MAXIMUM",
    };

    private static Colour4 hex(string h) => Colour4.FromHex(h);

    private static readonly Colour4 face = Colour4.FromHex("121a15");

    public DifficultySlot Slot { get; }

    /// <summary>The chart behind the badge; null = the song has no chart in this slot (drawn empty).</summary>
    public ISongListItem? Item { get; }

    /// <summary>Level text as shown (<see cref="Core.Charts.Difficulty.FormatLevel"/>), "-" for an empty slot.</summary>
    public string LevelText { get; }

    public bool IsSelected { get; }

    public event Action<ISongListItem>? Clicked;

    private readonly float width;
    private QuadBatch shape = null!;

    public LevelBadge(DifficultySlot slot, ISongListItem? item, bool selected, float width)
    {
        Slot = slot;
        Item = item;
        IsSelected = selected;
        this.width = width;
        LevelText = item == null ? "-" : Core.Charts.Difficulty.FormatLevel(item.DeclaredLevel);
        Size = new Vector2(width, width * 0.88f);
    }

    // downward triangle: top-left, top-right, bottom tip
    private Vector2 a => new Vector2(0, 0);
    private Vector2 b => new Vector2(width, 0);
    private Vector2 c => new Vector2(width / 2, width * 0.88f);

    /// <summary>The triangle shrunk by <paramref name="inset"/> px toward its incentre (a parallel inner outline).</summary>
    private Vector2[] inset(float inset)
    {
        float la = (b - c).Length, lb = (a - c).Length, lc = (a - b).Length;
        var incentre = (a * la + b * lb + c * lc) / (la + lb + lc);
        float s = (la + lb + lc) / 2;
        float area = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y)) / 2;
        float r = area / s;
        float k = Math.Max(0, (r - inset) / r);
        return new[] { incentre + (a - incentre) * k, incentre + (b - incentre) * k, incentre + (c - incentre) * k };
    }

    private Vector2 centreOf()
    {
        float la = (b - c).Length, lb = (a - c).Length, lc = (a - b).Length;
        return (a * la + b * lb + c * lc) / (la + lb + lc);
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var p = PaletteOf(Slot);
        bool present = Item != null;
        float rim = width * 0.17f;

        shape = new QuadBatch();
        void tri(Vector2[] t, ColourInfo col) => shape.Add(new Quad(t[0], t[1], t[2], t[2]), col);

        if (present)
        {
            if (IsSelected)
            {
                // soft glow: translucent triangles grown outward, fading out
                tri(inset(-width * 0.10f), p.Main.Opacity(0.10f));
                tri(inset(-width * 0.06f), p.Main.Opacity(0.16f));
                tri(inset(-width * 0.03f), p.Main.Opacity(0.24f));
            }

            tri(inset(0), p.Outer);
            var band = inset(width * 0.02f);
            // main band, brighter toward the top edge like the artwork's bevel
            shape.Add(new Quad(band[0], band[1], band[2], band[2]), new ColourInfo { TopLeft = p.Main, TopRight = p.Main, BottomLeft = p.Main.Darken(0.08f), BottomRight = p.Main.Darken(0.08f) });
            tri(inset(rim - width * 0.02f), p.Inner);
            tri(inset(rim), face);
        }
        else
        {
            tri(inset(0), Colour4.White.Opacity(0.22f));
            tri(inset(width * 0.03f), Colour4.Black.Opacity(0.55f));
        }

        var mid = centreOf();
        InternalChildren = new Drawable[]
        {
            shape,
            // name on the top edge, light text with a dark outline (shadow) like the artwork
            new SpriteText
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.Centre,
                Y = rim * 0.5f,
                Text = FullName(Slot),
                Font = EditorFont.Bold(Math.Max(10, width * 0.15f)),
                Colour = present ? Colour4.White.Opacity(0.95f) : Colour4.White.Opacity(0.4f),
                Shadow = true,
                ShadowColour = Colour4.Black,
                ShadowOffset = new Vector2(0, 0.06f),
            },
            new SpriteText
            {
                // inside the dark face, below the name band (the face's widest part is at the top)
                Position = new Vector2(mid.X, mid.Y + width * 0.06f),
                Origin = Anchor.Centre,
                Text = LevelText,
                Font = EditorFont.Bold(LevelText.Length > 2 ? width * 0.25f : width * 0.31f),
                Colour = present ? Colour4.White : Colour4.White.Opacity(0.4f),
                Shadow = true,
            },
        };

        Alpha = present ? (IsSelected ? 1 : 0.8f) : 0.75f;
    }

    // clicks only inside the triangle
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
    {
        if (!base.ReceivePositionalInputAt(screenSpacePos)) return false;
        var p = ToLocalSpace(screenSpacePos);
        return new Triangle(a, b, c).Contains(p);
    }

    protected override bool OnHover(HoverEvent e)
    {
        if (Item != null && !IsSelected) this.FadeTo(1, 80);
        return base.OnHover(e);
    }

    protected override void OnHoverLost(HoverLostEvent e)
    {
        if (Item != null && !IsSelected) this.FadeTo(0.8f, 80);
        base.OnHoverLost(e);
    }

    protected override bool OnClick(ClickEvent e)
    {
        if (Item == null) return false;
        Clicked?.Invoke(Item);
        return true;
    }
}
