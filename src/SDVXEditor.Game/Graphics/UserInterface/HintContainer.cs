using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Localisation;

namespace SDVXEditor.Game.Graphics.UserInterface;

/// <summary>A plain container with a tooltip: explains a control that has no label of its own (UI audit U8 / P1).</summary>
public partial class HintContainer : Container, IHasTooltip
{
    public LocalisableString TooltipText { get; set; }
}

/// <summary>A sprite text with a settable tooltip (a value whose raw form is only for the curious).</summary>
public partial class TooltipSpriteText : osu.Framework.Graphics.Sprites.SpriteText, IHasTooltip
{
    public LocalisableString TooltipText { get; set; }
}
