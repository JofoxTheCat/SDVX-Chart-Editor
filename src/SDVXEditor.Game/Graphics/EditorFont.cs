using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.IO.Stores;

namespace SDVXEditor.Game.Graphics;

/// <summary>
/// Single place that names the UI font. Roboto ships inside osu-framework
/// (Regular and Bold, plus Roboto Condensed) and is registered by the base Game;
/// see docs/FONTS.md for why it is not Aller.
/// Roboto has no Greek and no CJK, so Noto Sans and Noto Sans JP are registered behind it as fallbacks
/// (<see cref="AddFallbacks"/>); every SpriteText picks them up without naming them.
/// </summary>
public static class EditorFont
{
    public const string Family = "Roboto";
    public const string CondensedFamily = "RobotoCondensed";

    public static FontUsage Regular(float size = 14) => new FontUsage(Family, size, "Regular");

    public static FontUsage Bold(float size = 14) => new FontUsage(Family, size, "Bold");

    public static FontUsage Condensed(float size = 14, bool bold = false) => new FontUsage(CondensedFamily, size, bold ? "Bold" : "Regular");

    /// <summary>Text of the light Windows-look dialogs (9 pt Segoe UI is ~12 px; Roboto runs a little wider).</summary>
    public static FontUsage Dialog(float size = 12.5f, bool bold = false) => bold ? Bold(size) : Regular(size);

    /// <summary>
    /// Resolution of the Noto Sans JP glyph store relative to Roboto's 100 px cell. Must match
    /// <c>default_cjk_resolution</c> in tools/FontGen (it is also the <c>size</c> in the .bin's info block / 100).
    /// </summary>
    public const float CjkResolution = 0.6f;

    private static readonly string[] fallback_weights = { "Regular", "Bold" };

    /// <summary>
    /// Registers the generated fallback glyph stores (src/SDVXEditor.Resources/Fonts, see docs/FONTS.md).
    /// The framework's lookup is: requested font ("Roboto-Bold"), then any store whose name ends with the weight
    /// ("…-Bold"), then any store at all — so a Bold title falls back to NotoSans-Bold / NotoSansJP-Bold, and
    /// characters Roboto has are never taken from Noto. Noto Sans (Latin-ext, Greek, Cyrillic) is checked before
    /// Noto Sans JP, so Greek stays proportional.
    /// </summary>
    public static void AddFallbacks(osu.Framework.Game game, IRenderer renderer)
    {
        foreach (string weight in fallback_weights)
            game.AddFont(game.Resources, $"Fonts/NotoSans/NotoSans-{weight}");

        // Rendered smaller than Roboto to keep ~8,300 glyphs per weight reasonable; the nested store's scale
        // brings it back to the same em. Added to Fonts before its glyph stores so it shares the atlas and cache.
        var cjk = new FontStore(renderer, null, 100 * CjkResolution);
        game.Fonts.AddStore(cjk);

        foreach (string weight in fallback_weights)
            game.AddFont(game.Resources, $"Fonts/NotoSansJP/NotoSansJP-{weight}", cjk);
    }
}
