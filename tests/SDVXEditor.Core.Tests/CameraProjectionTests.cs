using NUnit.Framework;
using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Tests;

/// <summary>
/// The lane projection against unnamed-sdvx-clone's geometry (Track.cpp: 1 wide, 12 long; Camera.cpp: judgement line
/// 0.9 below / 0.9 ahead, 1.5° anchor tilt, 60° fov, line 5 % above the bottom; PitchScaleFunc × 15°; zoom 1.65^…).
/// Expected numbers come from an independent re-implementation of USC's transforms (scratch script, 2026-10-26),
/// in viewport heights; the only intended difference is our image shift that puts the judgement line at
/// <see cref="CameraProjection.JudgeScreenY"/> instead of 0.441 below the centre.
/// </summary>
[TestFixture]
public class CameraProjectionTests
{
    private static double Width(CameraState s, double along)
        => CameraProjection.Project(s, 1, along).X - CameraProjection.Project(s, -1, along).X;

    /// <summary>Screen distance of a 1/16 ahead of the judgement line, in lane widths there, at a hi-speed (8 / hs beats on the lane).</summary>
    private static double Sixteenth(CameraState s, double hiSpeed)
    {
        double a = 0.25 / (8 / hiSpeed);
        return (CameraProjection.Project(s, 0, 0).Y - CameraProjection.Project(s, 0, a).Y) / Width(s, 0);
    }

    [Test]
    public void DefaultLaneMatchesUsc()
    {
        var s = CameraState.Default;
        Assert.That(CameraProjection.LaneLength / 2, Is.EqualTo(12), "12 lane widths long");
        Assert.That(Width(s, 0), Is.EqualTo(0.774).Within(0.001), "lane width at the judgement line");
        Assert.That(Width(s, 1) / Width(s, 0), Is.EqualTo(0.090).Within(0.002), "far end ≈ 9 % of the near width");
        Assert.That(Sixteenth(s, 600 / 321.0), Is.EqualTo(0.354).Within(0.002), "1/16 at mod speed 600, 321 BPM");
        Assert.That(Sixteenth(s, 600 / 288.0), Is.EqualTo(0.378).Within(0.002), "1/16 at mod speed 600, 288 BPM");
        // the lane spans 0.67 of the viewport height, like USC's (0.441 − (−0.228))
        Assert.That(CameraProjection.Project(s, 0, 0).Y - CameraProjection.Project(s, 0, 1).Y, Is.EqualTo(0.669).Within(0.002));
    }

    [Test]
    public void ZoomMatchesUsc()
    {
        // AureoLe MXM at 2:24.4: zoom_top 34.4, zoom_bottom −93.4
        var aureole = CameraState.Default with { ZoomTop = 34.4, ZoomBottom = -93.4 };
        Assert.That(aureole.PitchDeg, Is.EqualTo(4.38).Within(0.01));
        Assert.That(Width(aureole, 0), Is.EqualTo(0.525).Within(0.001));
        Assert.That(Width(aureole, 1) / Width(aureole, 0), Is.EqualTo(0.130).Within(0.002));
        Assert.That(Sixteenth(aureole, 600 / 321.0), Is.EqualTo(0.434).Within(0.002));

        var near = CameraState.Default with { ZoomBottom = 100 };
        Assert.That(near.DistanceFactor, Is.EqualTo(1 / 1.65).Within(1e-9));
        Assert.That(Width(near, 0), Is.EqualTo(1.277).Within(0.002));
        Assert.That(Sixteenth(near, 600 / 321.0), Is.EqualTo(0.285).Within(0.002));

        var down = CameraState.Default with { ZoomTop = -100 };
        Assert.That(Sixteenth(down, 600 / 321.0), Is.EqualTo(0.274).Within(0.002));
        Assert.That(Width(down, 1) / Width(down, 0), Is.EqualTo(0.086).Within(0.002));

        foreach (var z in new[] { aureole, near, down, CameraState.Default with { ZoomBottom = -300 } })
            Assert.That(CameraProjection.Project(z, 0, 0).Y, Is.EqualTo(CameraProjection.JudgeScreenY).Within(1e-9), "judgement line stays put");
    }

    [Test]
    public void ZoomUnitsFollowUsc()
    {
        // PitchScaleFunc × 15°: linear 0..5.59 → 0..4.75, wraps at 24 (= 360°)
        Assert.That((CameraState.Default with { ZoomTop = 35 }).PitchDeg, Is.EqualTo(0.35 / 5.59 * 4.75 * 15).Within(1e-9));
        Assert.That((CameraState.Default with { ZoomTop = 559 }).PitchDeg, Is.EqualTo(4.75 * 15).Within(1e-9));
        Assert.That((CameraState.Default with { ZoomTop = 1200 }).PitchDeg, Is.EqualTo(180).Within(1e-9));
        Assert.That((CameraState.Default with { ZoomTop = 2400 }).PitchDeg, Is.EqualTo(360).Within(1e-9));
        Assert.That((CameraState.Default with { ZoomTop = -400 }).PitchDeg, Is.EqualTo(-3.05 * 15).Within(1e-9));
        Assert.That((CameraState.Default with { ZoomTop = -1200 }).PitchDeg, Is.EqualTo(-180).Within(1e-9));

        // zoom_bottom ≤ 0: the track moves 1.65^−z − 1 track widths away from the camera (default distance 1.256)
        Assert.That((CameraState.Default with { ZoomBottom = -100 }).DistanceFactor, Is.EqualTo(1 + 0.65 / 1.2560).Within(0.001));
        Assert.That((CameraState.Default with { ZoomBottom = 300 }).DistanceFactor, Is.EqualTo(Math.Pow(1.65, -Math.Pow(3, 1.35))).Within(1e-9));
        Assert.That(CameraState.Default.DistanceFactor, Is.EqualTo(1));

        // zoom_side 100: (1 × 500 / 696) / 2 track widths = 0.718 of our half-width units
        Assert.That((CameraState.Default with { ZoomSide = 100 }).ShiftX, Is.EqualTo(500.0 / 696).Within(1e-9));
        Assert.That(CameraProjection.Project(CameraState.Default with { ZoomSide = 100 }, 0, 0).X, Is.GreaterThan(0));
    }
}
