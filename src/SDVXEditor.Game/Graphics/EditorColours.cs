using osu.Framework.Graphics;

namespace SDVXEditor.Game.Graphics;

/// <summary>
/// Editor theme. The editor chrome follows osu!stable (neutral greys, pink
/// highlights); song select and playtest may use the NABLA set.
/// </summary>
public static class EditorColours
{
    // osu!-style editor chrome
    public static readonly Colour4 Background = Colour4.FromHex("1a1a1a");
    public static readonly Colour4 BackgroundAlt = Colour4.FromHex("222222");
    public static readonly Colour4 Panel = Colour4.FromHex("2a2a2a");
    public static readonly Colour4 PanelLight = Colour4.FromHex("333333");
    public static readonly Colour4 PanelHover = Colour4.FromHex("3d3d3d");
    public static readonly Colour4 Border = Colour4.FromHex("444444");
    public static readonly Colour4 Text = Colour4.FromHex("eeeeee");
    public static readonly Colour4 TextMuted = Colour4.FromHex("999999");

    /// <summary>osu! pink, used for active tabs, selection and other editor highlights.</summary>
    public static readonly Colour4 Highlight = Colour4.FromHex("ff66aa");
    public static readonly Colour4 HighlightDark = Colour4.FromHex("cc5288");

    /// <summary>
    /// NABLA accent set, the launcher's default profile (hue 120). Source:
    /// reference/notes/nabla-design.md section 1, converted from oklch to sRGB
    /// and clipped to gamut, so the darkest tones are approximate.
    /// </summary>
    public static class Nabla
    {
        public static readonly Colour4 Primary = Colour4.FromHex("d6f65a");     // oklch(92% 0.181 120)
        public static readonly Colour4 PrimaryInk = Colour4.FromHex("131900");  // oklch(20% 0.05 120)
        public static readonly Colour4 Accent = Colour4.FromHex("b2ec81");      // oklch(88% 0.15 132)
        public static readonly Colour4 BgDeep = Colour4.FromHex("060900");      // oklch(13% 0.03 120)
        public static readonly Colour4 BgPanel = Colour4.FromHex("0f1402");     // oklch(18% 0.038 120)
        public static readonly Colour4 BgElevated = Colour4.FromHex("1a2006");  // oklch(23% 0.044 120)
        public static readonly Colour4 Border = Colour4.FromHex("292e16");      // oklch(29% 0.042 120)
        public static readonly Colour4 Fg = Colour4.FromHex("edf0e7");          // oklch(95% 0.012 120)
        public static readonly Colour4 FgMuted = Colour4.FromHex("969b88");     // oklch(68% 0.028 120)
        public static readonly Colour4 Danger = Colour4.FromHex("ed4a49");      // oklch(64% 0.2 25)

        // Primaries of the other launcher profiles
        public static readonly Colour4 ProfileDarkGreen = Colour4.FromHex("19d27f"); // oklch(76% 0.18 156)
        public static readonly Colour4 ProfileBlue = Colour4.FromHex("0ec9ff");      // oklch(78% 0.149 227)
        public static readonly Colour4 ProfileYellow = Colour4.FromHex("f4d400");    // oklch(87% 0.18 98)
        public static readonly Colour4 ProfileRed = Colour4.FromHex("f10b4c");       // oklch(61% 0.241 17)
    }

    /// <summary>
    /// osu!stable in-game widget colours, measured from the reference
    /// screenshots (02/03/06). Approximate: the screenshots are compressed.
    /// </summary>
    public static class Osu
    {
        /// <summary>Active tab of the big lower-case tab strip.</summary>
        public static readonly Colour4 TabActive = Colour4.FromHex("1e88e5");
        public static readonly Colour4 TabStripBackground = Colour4.Black.Opacity(0.75f);

        public static readonly Colour4 StepperTop = Colour4.FromHex("4a9cf5");
        public static readonly Colour4 StepperBottom = Colour4.FromHex("1c6ad6");
        public static readonly Colour4 StepperBorder = Colour4.FromHex("8cc4ff");

        public static readonly Colour4 Pink = Colour4.FromHex("ff66aa");
        public static readonly Colour4 PinkDot = Colour4.FromHex("f06a8a");
        public static readonly Colour4 SliderTrack = Colour4.FromHex("e88aa0");

        /// <summary>Translucent black of the timeline / bottom bar.</summary>
        public static readonly Colour4 Overlay = Colour4.Black.Opacity(0.6f);
        public static readonly Colour4 PanelDark = Colour4.FromHex("0c0c0c").Opacity(0.88f);

        public static readonly Colour4 TileBackground = Colour4.FromHex("2b2b2b").Opacity(0.85f);
        public static readonly Colour4 TileActive = Colour4.FromHex("3c3c3c").Opacity(0.95f);

        public static readonly Colour4 ResetOrange = Colour4.FromHex("f39c12");
        public static readonly Colour4 TapBlue = Colour4.FromHex("8fd0ee");
        public static readonly Colour4 TimingSetupTop = Colour4.FromHex("ffd400");
        public static readonly Colour4 TimingSetupBottom = Colour4.FromHex("f5a200");

        public static readonly Colour4 KiaiOrange = Colour4.FromHex("d9862a");
        public static readonly Colour4 JudgementGreen = Colour4.FromHex("9cff2e");

        // Numbered action buttons (06)
        public static readonly Colour4 MenuGreen = Colour4.FromHex("4b6b1a");
        public static readonly Colour4 MenuRed = Colour4.FromHex("761c08");
        public static readonly Colour4 MenuBlue = Colour4.FromHex("234a8f");
        public static readonly Colour4 MenuGrey = Colour4.FromHex("3a3a3a");

        /// <summary>Non-destructive primary action (Edit) — red stays for Delete only (audit U20).</summary>
        public static readonly Colour4 MenuYellow = Colour4.FromHex("8a6a10");

        // Song select
        public static readonly Colour4 SongSelectLabel = Colour4.FromHex("8fb8e8");
        public static readonly Colour4 FilterTabRed = Colour4.FromHex("5c0a12");
        public static readonly Colour4 SearchGreen = Colour4.FromHex("a6ff3b");
        public static readonly Colour4 Link = Colour4.FromHex("7cc3ff");
        public static readonly Colour4 Rail = Colour4.FromHex("2f62e0");
    }

    /// <summary>Object colours per lane type, as in game: BT white, FX orange, lasers blue / pink.</summary>
    public static class Lanes
    {
        public static readonly Colour4 Bt = Colour4.White;
        public static readonly Colour4 Fx = Colour4.FromHex("ff9a2e");
        public static readonly Colour4 LaserLeft = Colour4.FromHex("0ec9ff");
        public static readonly Colour4 LaserRight = Colour4.FromHex("ff4fa3");
    }

    /// <summary>
    /// Beat snap divisor tick colours (osu-editor-spec.md 1.9). The colour depends on the
    /// reduced fraction of the beat a tick lands on, not on the slider value.
    /// </summary>
    public static class Divisor
    {
        public static readonly Colour4 White = Colour4.White;
        public static readonly Colour4 Red = Colour4.FromHex("ff2a2a");
        public static readonly Colour4 Blue = Colour4.FromHex("3d8bff");
        public static readonly Colour4 Purple = Colour4.FromHex("b561ff");
        public static readonly Colour4 Yellow = Colour4.FromHex("ffd400");
        public static readonly Colour4 Grey = Colour4.FromHex("8c8c8c");
        public static readonly Colour4 DarkGrey = Colour4.FromHex("555555");
        public static readonly Colour4 Finest = Colour4.FromHex("3d3d3d");

        /// <summary>
        /// Snap divisors offered by the slider (= <see cref="SDVXEditor.Core.Timing.TimingMap.SnapDivisors"/>). VOX has 48
        /// ticks per beat, so only divisors of 48 land on whole VOX ticks; 1/64, 1/96 and 1/192 (K-Shoot Editor's finest
        /// quantize) fall between them and between kson pulses too (240 per beat: rounded to the nearest pulse) — the chart
        /// check reports objects off the VOX grid.
        /// </summary>
        public static readonly int[] Steps = SDVXEditor.Core.Timing.TimingMap.SnapDivisors.ToArray();

        public static Colour4 ForDivisor(int divisor) => divisor switch
        {
            1 => White,
            2 => Red,
            3 or 6 => Purple,
            4 => Blue,
            5 or 7 or 8 or 9 => Yellow,
            12 => Grey,
            16 or 24 or 32 or 48 => DarkGrey, // like osu!
            _ => Finest, // 1/64, 1/96, 1/192: dimmer still, they only show when zoomed far in
        };

        /// <summary>Colour of tick <paramref name="index"/> within a beat split into <paramref name="divisor"/> parts.</summary>
        public static Colour4 ForTick(int index, int divisor)
        {
            if (divisor <= 0) return White;

            int i = ((index % divisor) + divisor) % divisor;
            if (i == 0) return White;

            return ForDivisor(divisor / gcd(i, divisor));
        }

        private static int gcd(int a, int b) => b == 0 ? a : gcd(b, a % b);
    }

    /// <summary>
    /// Difficulty slot colours. NOV/ADV/EXH/MXM come from the launcher (nabla-design.md 6,
    /// oklch converted to sRGB). INF/GRV/HVN/VVD/XCD are not documented anywhere: own,
    /// distinguishable placeholder tones until checked against the installed game.
    /// </summary>
    public static class Difficulty
    {
        public static readonly Colour4 Novice = Colour4.FromHex("60aaf3");    // oklch(72% 0.13 250)
        public static readonly Colour4 Advanced = Colour4.FromHex("e7b643");  // oklch(80% 0.14 85)
        public static readonly Colour4 Exhaust = Colour4.FromHex("f75d59");   // oklch(68% 0.19 25)
        public static readonly Colour4 Maximum = Colour4.FromHex("d3c6f0");   // oklch(85% 0.06 300)
        public static readonly Colour4 Infinite = Colour4.FromHex("dd56b8");  // assumption
        public static readonly Colour4 Gravity = Colour4.FromHex("f68c36");   // assumption
        public static readonly Colour4 Heavenly = Colour4.FromHex("4cdbe3");  // assumption
        public static readonly Colour4 Vivid = Colour4.FromHex("d36dea");     // assumption
        public static readonly Colour4 Exceed = Colour4.FromHex("61d7b2");    // assumption
    }

    /// <summary>
    /// Light "native Windows dialog" theme of the F6 timing window and Song Setup
    /// (reference screenshots 04/05), after the classic Windows 10 control palette.
    /// </summary>
    public static class Dialog
    {
        public static readonly Colour4 Body = Colour4.FromHex("f0f0f0");
        public static readonly Colour4 TitleBar = Colour4.FromHex("f0f0f0");
        public static readonly Colour4 TitleText = Colour4.FromHex("6d6d6d");
        public static readonly Colour4 WindowBorder = Colour4.FromHex("8a8a8a");
        public static readonly Colour4 Text = Colour4.FromHex("000000");
        public static readonly Colour4 TextDisabled = Colour4.FromHex("a0a0a0");
        public static readonly Colour4 Window = Colour4.White;

        public static readonly Colour4 ControlBorder = Colour4.FromHex("7a7a7a");
        public static readonly Colour4 ControlBorderHover = Colour4.FromHex("171717");
        public static readonly Colour4 FocusBorder = Colour4.FromHex("0078d7");
        public static readonly Colour4 GroupBorder = Colour4.FromHex("d5d5d5");
        public static readonly Colour4 TabBorder = Colour4.FromHex("d9d9d9");
        public static readonly Colour4 ListBorder = Colour4.FromHex("828790");
        public static readonly Colour4 HeaderSeparator = Colour4.FromHex("e5e5e5");

        public static readonly Colour4 Button = Colour4.FromHex("e1e1e1");
        public static readonly Colour4 ButtonBorder = Colour4.FromHex("adadad");
        public static readonly Colour4 ButtonHover = Colour4.FromHex("e5f1fb");
        public static readonly Colour4 ButtonPressed = Colour4.FromHex("cce4f7");
        public static readonly Colour4 ButtonPressedBorder = Colour4.FromHex("005499");

        public static readonly Colour4 Selection = Colour4.FromHex("cce8ff");
        public static readonly Colour4 SelectionBorder = Colour4.FromHex("99d1ff");
        public static readonly Colour4 HoverRow = Colour4.FromHex("e5f3ff");
        public static readonly Colour4 TextSelection = Colour4.FromHex("0078d7");

        public static readonly Colour4 CloseHover = Colour4.FromHex("e81123");
        public static readonly Colour4 Glyph = Colour4.FromHex("333333");
        public static readonly Colour4 RadioDot = Colour4.FromHex("005fb8");
        public static readonly Colour4 NoteRed = Colour4.FromHex("ff0000");
        public static readonly Colour4 Link = Colour4.FromHex("0066cc");

        public static readonly Colour4 DotRed = Colour4.FromHex("b8261d");
        public static readonly Colour4 DotGreen = Colour4.FromHex("3a9a2a");
        public static readonly Colour4 DotBlue = Colour4.FromHex("2f6ec0");
        public static readonly Colour4 DotGrey = Colour4.FromHex("808080");
        public static readonly Colour4 AddGreen = Colour4.FromHex("43a33a");
        public static readonly Colour4 RemoveRed = Colour4.FromHex("d32f2f");
    }
}
