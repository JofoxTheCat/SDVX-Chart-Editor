namespace SDVXEditor.Core.LevelEstimation;

/// <summary>
/// Compact slot-free fit used only when no coefficient file is embedded (CV MAE ≈ 0.96, no
/// isotonic map). Pasted from the calibrator's "fallback constants" output; aggregate numbers only.
/// </summary>
internal static class FallbackConstants
{
    public const double Intercept = 11.857961040475917;
    public const double CvMae = 0.96;

    public static readonly (string Name, string Transform, double Mean, double Sd, double Weight)[] Terms =
    {
        ("s_0.05_0.3_w", "log1p", 4.328176852542372, 0.42186415576254793, 5.363404490433636),
        ("s_0.125_0.3_t10", "id", 8.208559500493559, 3.2642514110901555, -1.072313923238196),
        ("travel_ps", "id", 0.9677845598062188, 0.5716144595183454, 0.6781805507065367),
        ("slam_ps", "id", 0.619625648064062, 0.5548045324450749, 0.07056071390291607),
    };
}
