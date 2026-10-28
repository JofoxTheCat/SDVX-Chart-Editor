using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>
/// A label cut with "…" when it does not fit, whose full text is the tooltip then (UI audit U6: truncated option
/// labels had no way to read the rest). Set <c>Truncate</c> plus a width (<c>MaxWidth</c> or a relative width).
/// </summary>
public partial class TruncatingText : SpriteText, IHasTooltip
{
    public TruncatingText()
    {
        Truncate = true;
    }

    /// <summary>
    /// The full text while it is probably cut. The natural width is estimated (half the font size per Latin letter,
    /// a full one per CJK glyph) — good enough to decide whether a tooltip is useful.
    /// </summary>
    public LocalisableString TooltipText => LooksTruncated ? Text : default;

    public bool LooksTruncated
    {
        get
        {
            string s = Text.ToString();
            float natural = 0;
            foreach (char c in s) natural += c >= 0x2E80 ? Font.Size : Font.Size * 0.5f;
            return natural > DrawWidth + 2;
        }
    }
}
