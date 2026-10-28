using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using SDVXEditor.Core.Gameplay;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Play;

/// <summary>What the HUD shows each frame.</summary>
public sealed record PlaytestHudState(
    ScoreProcessor Score,
    double Bpm,
    double HiSpeed,
    double ModSpeed,
    HiSpeedBasis Basis,
    bool HiSpeedForced,
    double Rate,
    double AudioOffset,
    double? EstimatedLevel,
    double Progress,
    bool Autoplay,
    bool Paused,
    bool Unsaved,
    string? Banner);

/// <summary>Score, chain, judgement counts, early/late, gauge, BPM / hi-speed and the level estimate.</summary>
public partial class PlaytestHud : CompositeDrawable
{
    private readonly SpriteText score, exScore, chain, chainLabel, popup, popupTiming, title, badges, banner, gaugeText;
    private readonly SpriteText[] infoLines = new SpriteText[4];

    /// <summary>
    /// The status block bottom-left: values on dark ground, keys below (audit U17: one long line ran over the track's
    /// left edge). Smoke test: it must stay left of the track.
    /// </summary>
    public Drawable StatusBlock { get; }

    /// <summary>The chain counter (smoke test: above the track's far end, not on it).</summary>
    public Drawable ChainDisplay => chain;

    /// <summary>The status lines as shown (smoke test).</summary>
    public IEnumerable<string> StatusLines => infoLines.Select(l => l.Text.ToString());
    private readonly Box gaugeFill, progressFill, clearLine;
    private readonly SpriteText[] countLines = new SpriteText[7];
    private readonly Container gaugeBar;

    public string LastPopup { get; private set; } = "";

    public PlaytestHud(string titleText)
    {
        RelativeSizeAxes = Axes.Both;

        static SpriteText text(float size, bool bold = false) => new SpriteText
        {
            Font = bold ? EditorFont.Bold(size) : EditorFont.Regular(size),
            Colour = Colour4.White,
            Shadow = true,
        };

        InternalChildren = new Drawable[]
        {
            // progress
            new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 4,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.White.Opacity(0.1f) },
                    progressFill = new Box { RelativeSizeAxes = Axes.Both, Width = 0, Colour = EditorColours.Lanes.LaserLeft },
                },
            },
            title = text(24, true).With(t =>
            {
                t.Position = new Vector2(32, 22);
                t.Text = titleText;
            }),
            badges = text(18, true).With(t =>
            {
                t.Position = new Vector2(32, 54);
                t.Colour = Colour4.FromHex("ffcf4a");
            }),
            StatusBlock = new Container
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(20, -18),
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.55f) },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                        Padding = new MarginPadding { Horizontal = 10, Vertical = 6 },
                        ChildrenEnumerable = Enumerable.Range(0, infoLines.Length).Select(i => infoLines[i] = text(17).With(t =>
                        {
                            t.Colour = i >= 2 ? Colour4.White.Opacity(0.65f) : Colour4.White.Opacity(0.9f);
                        })),
                    },
                },
            },
            new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Position = new Vector2(40, -40),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                ChildrenEnumerable = Enumerable.Range(0, 7).Select(i => countLines[i] = text(22, i < 4)),
            },
            score = text(64, true).With(t =>
            {
                t.Anchor = Anchor.TopRight;
                t.Origin = Anchor.TopRight;
                t.Position = new Vector2(-40, 18);
                t.Font = EditorFont.Bold(64).With(fixedWidth: true);
            }),
            exScore = text(22).With(t =>
            {
                t.Anchor = Anchor.TopRight;
                t.Origin = Anchor.TopRight;
                t.Position = new Vector2(-44, 92);
                t.Colour = Colour4.White.Opacity(0.85f);
            }),
            // above the track's far end: at y 150 the digits sat on the lasers coming in (audit U17)
            chain = text(64, true).With(t =>
            {
                t.Anchor = Anchor.TopCentre;
                t.Origin = Anchor.TopCentre;
                t.Position = new Vector2(0, 26);
                t.Font = EditorFont.Bold(64).With(fixedWidth: true);
            }),
            chainLabel = text(18, true).With(t =>
            {
                t.Anchor = Anchor.TopCentre;
                t.Origin = Anchor.TopCentre;
                t.Position = new Vector2(0, 94);
                t.Text = "CHAIN";
                t.Colour = Colour4.White.Opacity(0.7f);
            }),
            popup = text(46, true).With(t =>
            {
                t.Anchor = Anchor.Centre;
                t.Origin = Anchor.Centre;
                t.Position = new Vector2(0, 150);
                t.Alpha = 0;
            }),
            popupTiming = text(24, true).With(t =>
            {
                t.Anchor = Anchor.Centre;
                t.Origin = Anchor.Centre;
                t.Position = new Vector2(0, 192);
                t.Alpha = 0;
            }),
            banner = text(40, true).With(t =>
            {
                t.Anchor = Anchor.Centre;
                t.Origin = Anchor.Centre;
                t.Position = new Vector2(0, -120);
            }),
            // gauge (effective rate), right edge
            gaugeBar = new Container
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                Position = new Vector2(-60, 40),
                Size = new Vector2(22, 460),
                Masking = true,
                CornerRadius = 3,
                BorderThickness = 2,
                BorderColour = Colour4.White.Opacity(0.5f),
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.5f) },
                    gaugeFill = new Box
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        RelativeSizeAxes = Axes.Both,
                        Height = 0,
                    },
                    clearLine = new Box
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.Centre,
                        RelativePositionAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Width = 1,
                        Height = 2,
                        Y = -(float)ScoreProcessor.ClearGauge,
                        X = 11,
                        Colour = Colour4.White,
                    },
                },
            },
            gaugeText = text(18, true).With(t =>
            {
                t.Anchor = Anchor.CentreRight;
                t.Origin = Anchor.TopCentre;
                t.Position = new Vector2(-71, 280);
            }),
        };
    }

    /// <summary>Shows a judgement (CRITICAL / NEAR / ERROR and EARLY/LATE).</summary>
    public void ShowJudgement(JudgementEvent e)
    {
        (string label, Colour4 colour) = e.Result switch
        {
            HitResult.SCritical => ("S-CRITICAL", Colour4.FromHex("fff59d")),
            HitResult.Critical => ("CRITICAL", Colour4.FromHex("ffe36b")),
            HitResult.Near => ("NEAR", Colour4.FromHex("53c8ff")),
            _ => ("ERROR", Colour4.FromHex("ff4d4d")),
        };

        LastPopup = label;
        popup.Text = label;
        popup.Colour = colour;
        popup.FinishTransforms();
        popup.ScaleTo(1.15f).ScaleTo(1, 120, Easing.OutQuint);
        popup.FadeIn().Delay(300).FadeOut(200);

        string timing = e.IsEarly ? "EARLY" : e.IsLate ? "LATE" : "";
        popupTiming.Text = timing;
        popupTiming.Colour = e.IsEarly ? Colour4.FromHex("ff8ad8") : Colour4.FromHex("7dffb0");
        popupTiming.FinishTransforms();
        if (timing.Length > 0) popupTiming.FadeIn().Delay(300).FadeOut(200);
        else popupTiming.Alpha = 0;
    }

    public void Refresh(PlaytestHudState s)
    {
        var sc = s.Score;
        var inv = CultureInfo.InvariantCulture;

        score.Text = sc.Score.ToString("00,000,000", inv).Replace(',', ' ');
        exScore.Text = string.Create(inv, $"EX {sc.ExScore} / {sc.MaxExScore}   score pace {ScoreProcessor.GradeOf(sc.PaceScore)}");
        chain.Text = sc.Chain.ToString(inv);
        chain.Alpha = chainLabel.Alpha = sc.Chain > 0 ? 1 : 0.25f;

        string[] lines =
        {
            $"S-CRITICAL  {sc.SCriticals}",
            $"CRITICAL  {sc.Criticals - sc.SCriticals}",
            $"NEAR  {sc.Nears}",
            $"ERROR  {sc.Errors}",
            $"EARLY {sc.Early}   LATE {sc.Late}",
            $"MAX CHAIN  {sc.MaxChain}",
            string.Create(inv, $"mean {sc.MeanOffsetMs:+0.0;-0.0;0.0} ms"),
        };
        for (int i = 0; i < lines.Length; i++) countLines[i].Text = lines[i];
        countLines[0].Colour = Colour4.FromHex("fff59d");
        countLines[1].Colour = Colour4.FromHex("ffe36b");
        countLines[2].Colour = Colour4.FromHex("53c8ff");
        countLines[3].Colour = Colour4.FromHex("ff6b6b");

        // hi-speed = mod speed ÷ basis BPM; a forced hi-speed (smoke stress run) says so and shows its real mod speed.
        // Plain words for the basis (audit P8: "mode BPM" read as jargon).
        string basis = s.Basis.Source == HiSpeedBasisSource.StdBpm ? "standard" : "most used";
        string speed = s.HiSpeedForced
            ? string.Create(inv, $"hi-speed {s.HiSpeed:0.0##} (forced) = {s.HiSpeed * s.Basis.Bpm:0.#} ÷ BPM {s.Basis.Bpm:0.##}")
            : string.Create(inv, $"hi-speed {s.HiSpeed:0.00} = {s.ModSpeed:0} ÷ BPM {s.Basis.Bpm:0.##} ({basis})");
        string level = s.EstimatedLevel is { } l ? Core.LevelEstimation.EstimateText.Level(l) : "…";
        infoLines[0].Text = string.Create(inv, $"BPM {s.Bpm:0.##} · rate {s.Rate * 100:0}% · offset {s.AudioOffset:+0;-0;0} ms · est. {level}");
        infoLines[1].Text = speed;
        infoLines[2].Text = "F3/F4 hi-speed · Tab autoplay · Enter pause";
        infoLines[3].Text = "` retry · Esc results";

        var parts = new List<string>();
        if (s.Autoplay) parts.Add("AUTOPLAY");
        if (s.Paused) parts.Add("PAUSED");
        if (s.Unsaved) parts.Add("UNSAVED CHANGES (testing the chart in memory)");
        badges.Text = string.Join("   ", parts);

        banner.Text = s.Banner ?? "";

        gaugeFill.Height = (float)sc.Gauge;
        // hard gauge (EXCESSIVE): orange, blue below 30 %; its line marks 30 % (where damage starts to shrink)
        bool hard = sc.GaugeType == GaugeType.Hard;
        clearLine.Y = -(float)(hard ? 0.3 : ScoreProcessor.ClearGauge);
        gaugeFill.Colour = hard
            ? sc.Gauge >= 0.3 ? ColourInfo.GradientVertical(Colour4.FromHex("ffb347"), Colour4.FromHex("ff7a1a")) : ColourInfo.GradientVertical(Colour4.FromHex("6fd3ff"), Colour4.FromHex("2b7bff"))
            : sc.Cleared
                ? ColourInfo.GradientVertical(Colour4.FromHex("ff6ad5"), Colour4.FromHex("ff3d7f"))
                : ColourInfo.GradientVertical(Colour4.FromHex("6fd3ff"), Colour4.FromHex("2b7bff"));
        string kind = sc.GaugeType switch { GaugeType.Hard => "HARD ", GaugeType.Easy => "EASY ", _ => "" };
        gaugeText.Text = string.Create(inv, $"{kind}{sc.Gauge * 100:0.0}%");
        progressFill.Width = (float)Math.Clamp(s.Progress, 0, 1);
    }
}
