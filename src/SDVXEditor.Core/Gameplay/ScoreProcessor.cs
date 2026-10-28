namespace SDVXEditor.Core.Gameplay;

/// <summary>
/// Score, EX score, chain and the effective-rate gauge from judgements.
/// <list type="bullet">
/// <item><b>Score</b> (RemyWiki "What is SOUND VOLTEX"; USC <c>Scoring.hpp</c> "2 = critical, 1 = near"): every chain
/// unit is worth 2 points at CRITICAL, a chip NEAR 1, ERROR 0; score = floor(10,000,000 × points / (2 × units)).</item>
/// <item><b>EX score</b> (RemyWiki, EXCEED GEAR S-CRITICAL option): chips S-CRITICAL 5 / CRITICAL 4 / NEAR 2,
/// hold and laser chains 2 per tick. Slams are chain units too and are given 2 here — the source table lists only
/// chips and chains, so the slam value is an assumption.</item>
/// <item><b>Gauge</b> (USC <c>GaugeNormal</c>, matching RemyWiki's +4T/n rule): T = chart total / 100 (2.10 when
/// automatic), n = ticks + 4 × chips; chip CRITICAL +4T/n, NEAR a third of that, ERROR −2 %; tick CRITICAL +T/n,
/// ERROR −0.5 %. Clear at 70 %. The test cannot be failed — the gauge only reports.</item>
/// <item><b>Hard gauge</b> (<see cref="GaugeType.Hard"/>, EXCESSIVE rate; USC <c>GaugeHard</c> in <c>Main/src/Gauge.cpp</c> +
/// <c>Main/include/Gauge.hpp</c>, read 2026-10-27): starts at 100 %, gains 12/21 of the normal ones, chip ERROR −9 %, tick
/// ERROR a quarter of that, damage × clamp(1 − 2 × (0.3 − gauge), 0.5, 1) (halved near 0, full from 30 % up); the run
/// fails when it reaches 0 (<see cref="Failed"/>, gauge stays 0); clear = still above 0.</item>
/// <item><b>Easy gauge</b> (<see cref="GaugeType.Easy"/>): the normal gauge with every ERROR's damage halved. Our own
/// definition — USC has no easy gauge and no source for the game's numbers was found; it is a practice aid.</item>
/// </list>
/// </summary>
public sealed class ScoreProcessor
{
    public const int MaxScore = 10_000_000;
    public const double ClearGauge = 0.70;

    public int TotalChips { get; }
    public int TotalTicks { get; }
    public int TotalSlams { get; }

    public int TotalUnits => TotalChips + TotalTicks + TotalSlams;
    public long MaxPoints => 2L * TotalUnits;
    public int MaxExScore => 5 * TotalChips + 2 * (TotalTicks + TotalSlams);

    public long Points { get; private set; }

    /// <summary>Points still possible for the units not judged yet are not counted (a running "add" score).</summary>
    public int Score => MaxPoints == 0 ? 0 : (int)(MaxScore * Points / MaxPoints);

    /// <summary>The score this run is on pace for: points per judged unit, scaled to the maximum.</summary>
    public int PaceScore => Judged == 0 ? MaxScore : (int)(MaxScore * Points / (2L * Judged));

    public int ExScore { get; private set; }
    public int Chain { get; private set; }
    public int MaxChain { get; private set; }
    public double Gauge { get; private set; }

    public int SCriticals { get; private set; }

    /// <summary>CRITICALs including S-CRITICALs and hit ticks/slams.</summary>
    public int Criticals { get; private set; }

    public int Nears { get; private set; }
    public int NearsEarly { get; private set; }
    public int NearsLate { get; private set; }
    public int Errors { get; private set; }
    public int ErrorsEarly { get; private set; }
    public int ErrorsLate { get; private set; }

    /// <summary>Units judged so far.</summary>
    public int Judged { get; private set; }

    /// <summary>Sum and count of chip offsets (ms, for the mean hit error).</summary>
    private double offsetSum;

    private int offsetCount;

    public double MeanOffsetMs => offsetCount == 0 ? 0 : offsetSum / offsetCount;

    public int Early => NearsEarly + ErrorsEarly;
    public int Late => NearsLate + ErrorsLate;

    private readonly double chipGain, tickGain;

    public GaugeType GaugeType { get; }

    /// <summary>Hard gauge only: the gauge reached 0 (the playtest stops there, like a TRACK CRASH).</summary>
    public bool Failed { get; private set; }

    /// <summary>Units judged when the hard gauge failed, or -1.</summary>
    public int FailedAtJudged { get; private set; } = -1;

    public const double HardGainRate = 12.0 / 21.0;
    public const double HardChipDrain = 0.09;

    public ScoreProcessor(int chips, int ticks, int slams, int gaugeTotal = 0, GaugeType gauge = GaugeType.Normal)
    {
        GaugeType = gauge;
        if (gauge == GaugeType.Hard) Gauge = 1;
        TotalChips = chips;
        TotalTicks = ticks;
        TotalSlams = slams;

        double total = gaugeTotal > 99 ? gaugeTotal / 100.0 : 2.10;
        int longUnits = ticks + slams;
        if (longUnits == 0 && chips > 0)
            chipGain = total / chips;
        else if (chips == 0 && longUnits > 0)
            tickGain = total / longUnits;
        else if (chips + longUnits > 0)
        {
            chipGain = 4 * total / (longUnits + 4.0 * chips);
            tickGain = chipGain / 4;
        }

        if (gauge == GaugeType.Hard)
        {
            chipGain *= HardGainRate;
            tickGain *= HardGainRate;
        }
    }

    public static ScoreProcessor For(PlayableChart chart, GaugeType gauge = GaugeType.Normal) =>
        new ScoreProcessor(chart.CountedChips, chart.CountedTicks, chart.CountedSlams, chart.Chart.GaugeTotal, gauge);

    /// <summary>Damage of an ERROR for the gauge type (chip or hold/laser tick).</summary>
    private double damage(bool chip) => GaugeType switch
    {
        GaugeType.Hard => (chip ? HardChipDrain : HardChipDrain / 4) * Math.Clamp(1 - (0.3 - Gauge) * 2, 0.5, 1),
        GaugeType.Easy => (chip ? 0.02 : 0.005) / 2,
        _ => chip ? 0.02 : 0.005,
    };

    private void gain(double amount)
    {
        if (Failed) return;
        Gauge = Math.Min(1, Gauge + amount);
    }

    public void Apply(JudgementEvent e)
    {
        Judged++;
        bool chip = e.Object.Kind == ScoreObjectKind.Chip;

        switch (e.Result)
        {
            case HitResult.SCritical:
            case HitResult.Critical:
                Points += 2;
                if (e.Result == HitResult.SCritical) SCriticals++;
                Criticals++;
                ExScore += chip ? (e.Result == HitResult.SCritical ? 5 : 4) : 2;
                gain(chip ? chipGain : tickGain);
                Chain++;
                break;

            case HitResult.Near:
                Points += 1;
                Nears++;
                if (e.OffsetMs < 0) NearsEarly++;
                else NearsLate++;
                ExScore += 2;
                gain(chipGain / 3);
                Chain++;
                break;

            default:
                Errors++;
                if (!double.IsNaN(e.OffsetMs))
                {
                    if (e.OffsetMs < 0) ErrorsEarly++;
                    else ErrorsLate++;
                }

                if (!Failed) Gauge = Math.Max(0, Gauge - damage(chip));
                if (GaugeType == GaugeType.Hard && Gauge <= 0 && !Failed)
                {
                    Failed = true;
                    FailedAtJudged = Judged;
                }

                Chain = 0;
                break;
        }

        if (chip && !double.IsNaN(e.OffsetMs) && e.Result != HitResult.Error)
        {
            offsetSum += e.OffsetMs;
            offsetCount++;
        }

        MaxChain = Math.Max(MaxChain, Chain);
    }

    public bool Cleared => GaugeType == GaugeType.Hard ? !Failed && Gauge > 0 : Gauge >= ClearGauge;

    /// <summary>The clear line of the gauge (70 %, or 0 for the hard gauge).</summary>
    public double ClearLine => GaugeType == GaugeType.Hard ? 0 : ClearGauge;

    /// <summary>PERFECT (all CRITICAL) / ULTIMATE CHAIN (no ERROR) / COMPLETE / CRASH, for the judged part.</summary>
    public string ClearMark =>
        Judged == 0 ? "-" :
        Errors == 0 && Nears == 0 ? "PERFECT" :
        Errors == 0 ? "ULTIMATE CHAIN" :
        Cleared ? "COMPLETE" : "CRASH";

    /// <summary>SOUND VOLTEX grade for a score (S ≥ 9.9M … D).</summary>
    public static string GradeOf(int score) => score switch
    {
        >= 9_900_000 => "S",
        >= 9_800_000 => "AAA+",
        >= 9_700_000 => "AAA",
        >= 9_500_000 => "AA+",
        >= 9_300_000 => "AA",
        >= 9_000_000 => "A+",
        >= 8_700_000 => "A",
        >= 7_500_000 => "B",
        >= 6_500_000 => "C",
        _ => "D",
    };

    public string Grade => GradeOf(Score);
}

/// <summary>Playtest gauge: normal effective rate, easy (halved damage) or hard (EXCESSIVE, can fail).</summary>
public enum GaugeType
{
    Normal,
    Easy,
    Hard,
}
