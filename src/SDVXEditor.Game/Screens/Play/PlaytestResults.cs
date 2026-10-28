using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Play;

/// <summary>Result summary shown when the chart ends or the test is stopped.</summary>
public partial class PlaytestResults : CompositeDrawable
{
    public PlaytestResults(ScoreProcessor s, string title, bool autoplayUsed, bool reachedEnd, double stoppedAtMs, double meanOffsetMs)
    {
        RelativeSizeAxes = Axes.Both;
        var inv = CultureInfo.InvariantCulture;

        static SpriteText text(string t, float size, bool bold = false, Colour4? colour = null) => new SpriteText
        {
            Text = t,
            Font = bold ? EditorFont.Bold(size) : EditorFont.Regular(size),
            Colour = colour ?? Colour4.White,
            Shadow = true,
        };

        string clear = s.Failed ? "TRACK CRASH (hard gauge)" : s.ClearMark;
        var clearColour = clear switch
        {
            "PERFECT" => Colour4.FromHex("ffe36b"),
            "ULTIMATE CHAIN" => Colour4.FromHex("ff8ad8"),
            "COMPLETE" => Colour4.FromHex("7dffb0"),
            _ => Colour4.FromHex("ff6b6b"),
        };

        string stopped = UiFormat.Time(Math.Max(0, stoppedAtMs)); // the editor's mm:ss:mmm (audit U2)

        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.72f) },
            new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 760,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 10,
                BorderThickness = 2,
                BorderColour = Colour4.White.Opacity(0.25f),
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.FromHex("141225").Opacity(0.96f) },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(36),
                        Spacing = new Vector2(0, 8),
                        Children = new Drawable[]
                        {
                            text(Header(reachedEnd, s.Failed), 22, true, Colour4.White.Opacity(0.7f)),
                            text(title, 26, true),
                            text(s.Score.ToString("00,000,000", inv).Replace(',', ' '), 72, true),
                            text(reachedEnd || s.Failed ? $"{s.Grade}   ·   {clear}" : $"score pace {ScoreProcessor.GradeOf(s.PaceScore)} ({UiFormat.Grouped(s.PaceScore)})   ·   {SoFar(s)}", 34, true,
                                reachedEnd || s.Failed ? clearColour : Colour4.White),
                            text(string.Create(inv, $"EX SCORE {s.ExScore} / {s.MaxExScore}      MAX CHAIN {s.MaxChain} / {s.TotalUnits}"), 22),
                            text(gaugeLine(s, stopped, inv), 22, false, s.Failed ? Colour4.FromHex("ff6b6b") : null),
                            new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = Colour4.White.Opacity(0.2f) },
                            text(string.Create(inv, $"S-CRITICAL {s.SCriticals}    CRITICAL {s.Criticals - s.SCriticals}    NEAR {s.Nears}    ERROR {s.Errors}"), 24, true),
                            text(string.Create(inv, $"EARLY {s.Early} (near {s.NearsEarly} / error {s.ErrorsEarly})    LATE {s.Late} (near {s.NearsLate} / error {s.ErrorsLate})"), 21),
                            text(string.Create(inv, $"mean chip offset {meanOffsetMs:+0.0;-0.0;0.0} ms (song time)    judged {s.Judged} of {s.TotalUnits} judgements"), 21),
                            text(autoplayUsed ? "autoplay was used" : "", 20, false, Colour4.FromHex("ffcf4a")),
                            new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = Colour4.White.Opacity(0.2f) },
                            text($"Enter / Esc: back to the editor at {stopped}     ` : retry", 21, false, Colour4.White.Opacity(0.8f)),
                        },
                    },
                },
            },
        };
    }

    /// <summary>"RESULT", "RESULT (track crash)" or "RESULT (stopped)" (audit U18: a crash read "stopped").</summary>
    public static string Header(bool reachedEnd, bool failed) => reachedEnd ? "RESULT" : failed ? "RESULT (track crash)" : "RESULT (stopped)";

    /// <summary>
    /// A stopped run has no clear mark yet: say what the judgements so far show instead of "PERFECT so far" next to a
    /// gauge that "would fail" (the gauge only fills over the whole chart).
    /// </summary>
    public static string SoFar(ScoreProcessor s) => s.Errors > 0 ? string.Create(CultureInfo.InvariantCulture, $"{s.Errors} ERROR so far")
        : s.Nears > 0 ? "no ERROR so far"
        : "no NEAR or ERROR so far";

    private static string gaugeLine(ScoreProcessor s, string stopped, CultureInfo inv) => s.GaugeType switch
    {
        GaugeType.Hard when s.Failed => string.Create(inv, $"hard gauge emptied at {stopped}, after {s.FailedAtJudged} of {s.TotalUnits} judgements"),
        GaugeType.Hard => string.Create(inv, $"hard gauge {s.Gauge * 100:0.0}% (survived)"),
        GaugeType.Easy => string.Create(inv, $"easy gauge {s.Gauge * 100:0.0}% ({gaugeVerdict(s)})"),
        _ => string.Create(inv, $"effective rate {s.Gauge * 100:0.0}% ({gaugeVerdict(s)})"),
    };

    private static string gaugeVerdict(ScoreProcessor s) => s.Cleared ? "clear" : s.Judged < s.TotalUnits ? "so far - a clear needs 70% at the end" : "below 70%, failed";
}
