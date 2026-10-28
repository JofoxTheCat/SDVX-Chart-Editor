using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Testing;
using SDVXEditor.Core.Tutorial;

namespace SDVXEditor.Game.Tutorial;

/// <summary>Helpers of the main tutorial's chapters: spotlight regions, bar labels, task waits for the demos.</summary>
public static partial class TutorialChapters
{
    /// <summary>The playfield around some ghost targets (a quarter beat of air above and below).</summary>
    private static RectangleF? region(TutorialContext c, IEnumerable<TutorialTarget> targets, bool lasers = false, bool wide = false)
    {
        var list = targets.ToList();
        if (list.Count == 0) return null;
        return c.PlayfieldRegion(list.Min(t => t.Y) - beat / 4, list.Max(t => t.EndY) + beat / 4, lasers || list.Any(t => t.IsLaser), wide || list.Any(t => t.Wide));
    }

    private static RectangleF? region(TutorialContext c, TutorialWindow w, bool lasers = true) => c.PlayfieldRegion(w.FromPulse, w.ToPulse, lasers);

    /// <summary>"bar 3" / "bars 12–13" / "bar 16, beat 2" for a pulse range (1-based, as the cards say it).</summary>
    private static string bars(long from, long to)
    {
        long m = MainTutorialSong.Measure;
        int b0 = (int)(from / m) + 1, b1 = (int)((to - 1) / m) + 1;
        if (from % m == 0 && to % m == 0) return b0 == b1 ? $"bar {b0}" : $"bars {b0}–{b1}";
        int beat0 = (int)(from % m / beat) + 1;
        return b0 == b1 ? $"bar {b0}, beat {beat0}" : $"bars {b0}–{b1}";
    }

    /// <summary>Demo: waits until the step's task <paramref name="index"/> (0-based) is the current one (the earlier ones ticked).</summary>
    private static void task(ITutorialDemo d, TutorialContext c, int index) =>
        d.WaitFor(() => c.Manager.TaskIndex >= index || c.Manager.StepSolved, $"task {index + 1} to be current");

    private static RectangleF? rectOf(Drawable? d) => d != null && TutorialAnchors.IsShown(d) ? d.ScreenSpaceDrawQuad.AABBFloat : null;

    /// <summary>The first drop-down (any <c>Dropdown</c>) in the same row as the button labelled <paramref name="button"/>.</summary>
    private static Drawable? dropdownBeside(Drawable? root, string button)
    {
        var b = TutorialContext.FindText(root, button);
        int depth = 0;
        for (Drawable? p = b?.Parent; p != null && p != root && depth++ < 6; p = p.Parent)
        {
            if (p is CompositeDrawable cd && cd.ChildrenOfType<SDVXEditor.Game.Graphics.UserInterface.OsuDropdown<string>>().FirstOrDefault(TutorialAnchors.IsShown) is { } dd) return dd;
        }

        return null;
    }

    /// <summary>
    /// A section of a panel: from the header containing <paramref name="fromHeader"/> down to the one containing
    /// <paramref name="toHeader"/> (exclusive; null = only that header's own row block, 60 px), as wide as the panel.
    /// </summary>
    private static RectangleF? section(Drawable? panel, string fromHeader, string? toHeader, float fallbackHeight = 64)
    {
        if (panel == null || !TutorialAnchors.IsShown(panel)) return null;
        var a = TutorialContext.FindText(panel, fromHeader, contains: true);
        if (a == null) return null;
        var p = panel.ScreenSpaceDrawQuad.AABBFloat;
        var ra = a.ScreenSpaceDrawQuad.AABBFloat;
        float bottom = toHeader != null && TutorialContext.FindText(panel, toHeader, contains: true) is { } b ? b.ScreenSpaceDrawQuad.AABBFloat.Top - 4 : ra.Bottom + fallbackHeight;
        var r = new RectangleF(p.Left + 4, ra.Top - 4, p.Width - 8, Math.Max(10, bottom - ra.Top + 4));
        return RectangleF.Intersect(r, p); // never past the panel's own bottom
    }
}
