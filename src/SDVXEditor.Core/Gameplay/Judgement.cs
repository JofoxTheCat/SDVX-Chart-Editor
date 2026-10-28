namespace SDVXEditor.Core.Gameplay;

/// <summary>Judged unit of a chart. Every unit is worth one chain (combo) step.</summary>
public enum ScoreObjectKind
{
    /// <summary>BT or FX chip: timed press, CRITICAL / NEAR / ERROR.</summary>
    Chip,

    /// <summary>One tick of a BT/FX hold: button held or not (CRITICAL / ERROR).</summary>
    HoldTick,

    /// <summary>One tick of a laser: cursor on the laser or not (CRITICAL / ERROR).</summary>
    LaserTick,

    /// <summary>A slam (right-angle laser): knob turned in its direction in time (CRITICAL / ERROR).</summary>
    Slam,
}

public enum HitResult
{
    /// <summary>Inside the S-CRITICAL window (chips only); scores like CRITICAL, adds EX score.</summary>
    SCritical,
    Critical,
    Near,
    Error,
}

/// <summary>
/// Lanes of the playfield as used by the judge: 0-3 BT-A..D, 4/5 FX-L/R, 6/7 laser L/R.
/// </summary>
public static class PlayLanes
{
    public const int BtA = 0;
    public const int FxL = 4;
    public const int FxR = 5;
    public const int LaserL = 6;
    public const int LaserR = 7;

    /// <summary>Number of buttons (BT + FX).</summary>
    public const int Buttons = 6;

    public static bool IsFx(int lane) => lane is FxL or FxR;
    public static bool IsLaser(int lane) => lane >= LaserL;
}

/// <summary>
/// Timing windows in milliseconds of <b>real</b> time (half-widths: a hit is CRITICAL when
/// |offset| ≤ <see cref="CriticalMs"/>).
/// <para>
/// Defaults: CRITICAL ±46 ms is unnamed-sdvx-clone's <c>HitWindow::NORMAL.perfect</c> (Main/src/HitStat.cpp,
/// <c>HitWindow(46, 150)</c>), NEAR ±92 ms is twice that, and ±138 ms (three times) is where a press still
/// consumes a chip as ERROR; later than that the chip is missed. USC's own near window is wider (150 ms) — its
/// "good" — so these defaults are stricter than USC and closer to the arcade feel; all are configurable.
/// The slam window (84 ms; early input accepted up to half of it) is USC's <c>HitWindow::slam</c>.
/// The S-CRITICAL window is not documented anywhere public; 23 ms (USC's HARD perfect) is an assumption.
/// </para>
/// </summary>
public sealed record JudgementWindows
{
    public double SCriticalMs { get; init; } = 23;
    public double CriticalMs { get; init; } = 46;
    public double NearMs { get; init; } = 92;
    public double ErrorMs { get; init; } = 138;
    public double SlamMs { get; init; } = 84;

    /// <summary>How far (in laser value units, 0..1) the cursor may be off the laser and still count.</summary>
    public double LaserLeniency { get; init; } = 1.0 / 12;

    /// <summary>
    /// After an input in the laser's direction, the cursor keeps following a moving laser for this long
    /// without further input (knob encoders report in bursts). Real-time ms.
    /// </summary>
    public double LaserAssistMs { get; init; } = 150;

    public static readonly JudgementWindows Default = new();

    /// <summary>
    /// Windows in song time for a playback rate: song time runs <paramref name="rate"/> × real time, so a window of
    /// w real ms is w × rate song ms — the player gets the same real-time windows at any rate.
    /// </summary>
    public JudgementWindows ForRate(double rate)
    {
        if (rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
        return this with
        {
            SCriticalMs = SCriticalMs * rate,
            CriticalMs = CriticalMs * rate,
            NearMs = NearMs * rate,
            ErrorMs = ErrorMs * rate,
            SlamMs = SlamMs * rate,
            LaserAssistMs = LaserAssistMs * rate,
        };
    }

    /// <summary>Result of a chip press at <paramref name="offsetMs"/> (press − note), or null when outside every window.</summary>
    public HitResult? JudgeChip(double offsetMs)
    {
        double d = Math.Abs(offsetMs);
        if (d <= SCriticalMs) return HitResult.SCritical;
        if (d <= CriticalMs) return HitResult.Critical;
        if (d <= NearMs) return HitResult.Near;
        if (d <= ErrorMs) return HitResult.Error;
        return null;
    }

    /// <summary>
    /// Reads the settings form <c>critical,near,error[,slam]</c> (real-time ms). Missing or invalid numbers keep
    /// their defaults; the result is <see cref="Sanitised"/>.
    /// </summary>
    public static JudgementWindows Parse(string? text)
    {
        var w = Default;
        var parts = (text ?? "").Split(',', ';');
        double? at(int i) => i < parts.Length && double.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0 ? v : null;
        w = w with
        {
            CriticalMs = at(0) ?? w.CriticalMs,
            NearMs = at(1) ?? w.NearMs,
            ErrorMs = at(2) ?? w.ErrorMs,
            SlamMs = at(3) ?? w.SlamMs,
        };
        // the S-CRITICAL window follows a changed CRITICAL window (half of it, as the default 23/46)
        if (at(0) is { } c && c != Default.CriticalMs) w = w with { SCriticalMs = c / 2 };
        return w.Sanitised();
    }

    /// <summary>Keeps the windows ordered (S-CRITICAL ≤ CRITICAL ≤ NEAR ≤ ERROR) and positive.</summary>
    public JudgementWindows Sanitised()
    {
        double c = Math.Clamp(CriticalMs, 1, 500);
        double n = Math.Clamp(NearMs, c, 1000);
        double e = Math.Clamp(ErrorMs, n, 1500);
        return this with
        {
            SCriticalMs = Math.Clamp(SCriticalMs, 0, c),
            CriticalMs = c,
            NearMs = n,
            ErrorMs = e,
            SlamMs = Math.Clamp(SlamMs, 1, 1000),
            LaserLeniency = Math.Clamp(LaserLeniency, 0.005, 0.5),
            LaserAssistMs = Math.Clamp(LaserAssistMs, 0, 1000),
        };
    }
}

/// <summary>One judgement as reported by <see cref="GameplayJudge"/>.</summary>
/// <param name="OffsetMs">Press time − object time in song ms (negative = early); NaN when there was no input (miss / tick).</param>
public readonly record struct JudgementEvent(ScoreObject Object, HitResult Result, double OffsetMs, double TimeMs)
{
    public bool IsEarly => !double.IsNaN(OffsetMs) && OffsetMs < 0 && Result is HitResult.Near or HitResult.Error;
    public bool IsLate => !double.IsNaN(OffsetMs) && OffsetMs > 0 && Result is HitResult.Near or HitResult.Error;
    public bool IsMiss => Result == HitResult.Error;
}
