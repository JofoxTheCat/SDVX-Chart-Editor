using System.Globalization;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Testing;
using osuTK;
using osuTK.Input;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Edit.Compose;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// The chapters of both tutorials, as data. Each has 0 Song Setup &amp; Timing, 1 Chips/Holds/FX, 2 Lasers,
/// 3 Navigating, 4 Camera &amp; Background, 5 Effects &amp; Sounds, 6 Test &amp; Difficulty, 7 Save/Export/Share. Early steps
/// show ghosts to copy exactly, later ones give a count or a pattern, the last ones only check rules on a section the
/// user maps alone. Main tutorial: <c>Main/TutorialChapters.Main*.cs</c>; classic (temporary):
/// <c>TutorialChapters.Setup/Compose/More.cs</c>. The helpers below are shared.
/// </summary>
public static partial class TutorialChapters
{
    /// <summary>The main tutorial (Fax's song).</summary>
    public static IReadOnlyList<TutorialChapter> Main { get; } = new[]
    {
        mainSongSetupAndTiming(),
        mainChipsHoldsFx(),
        mainLasers(),
        mainNavigating(),
        mainCamera(),
        mainEffects(),
        mainTestAndDifficulty(),
        mainSaveExportShare(),
    };

    /// <summary>The classic tutorial (placeholder song). TEMPORARY: delete with TutorialChapters.Setup/Compose/More.cs.</summary>
    public static IReadOnlyList<TutorialChapter> Classic { get; } = new[]
    {
        songSetupAndTiming(),
        chipsHoldsFx(),
        lasers(),
        navigating(),
        camera(),
        effects(),
        testAndDifficulty(),
        saveExportShare(),
    };

    // ---- shared helpers ------------------------------------------------------------------

    private const long beat = TutorialSongDefinition.Beat;
    private static long at(int measure, double b = 0) => TutorialSongDefinition.At(measure, b);

    private static string inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>"0:13.100" style time for the texts.</summary>
    private static string clock(double ms) => inv($"{(int)(ms / 60000):00}:{(int)(ms / 1000) % 60:00}:{(int)Math.Round(ms) % 1000:000}");

    /// <summary>
    /// Scrolls (and zooms out when needed) so pulses [from, to) are on screen, unless they already are. Waits for the
    /// editor to be loaded when the chapter has just opened it.
    /// </summary>
    internal static void Show(TutorialContext c, long from, long to) => c.Manager.WhenEditorReady(() =>
    {
        var e = c.Editor;
        if (e == null) return;

        var pf = e.Playfield;
        float y0 = pf.YOfPulse(from), y1 = pf.YOfPulse(to);
        float judge = pf.DrawHeight - PlayfieldLayout.JUDGEMENT_FROM_BOTTOM;
        if (y0 <= judge + 40 && y0 >= 30 && y1 >= 30 && !e.Playback.IsPlaying.Value) return;

        e.Playback.IsPlaying.Value = false;
        double msFrom = c.MsOf(from), msTo = c.MsOf(to);
        double lead = (c.MsOf(from + beat) - msFrom) / 2;
        float need = (float)((msTo - msFrom + lead) * PlayfieldLayout.PX_PER_MS);
        float avail = judge - 40;
        if (need * e.Zoom.Value > avail)
            e.Zoom.Value = Math.Clamp((float)Math.Floor(avail / need * 20) / 20, e.Zoom.MinValue, e.Zoom.MaxValue);
        e.Playback.CurrentTime.Value = Math.Max(0, msFrom - lead);
    });

    /// <summary>Puts the editor cursor on a pulse (bookmark/jump exercises start from a known place).</summary>
    internal static void SeekTo(TutorialContext c, long pulse) => c.Manager.WhenEditorReady(() =>
    {
        if (c.Editor is not { } e) return;
        e.Playback.IsPlaying.Value = false;
        e.Playback.CurrentTime.Value = Math.Max(0, c.MsOf(pulse));
    });

    private static Func<Vector2> pf(TutorialContext c, long pulse, float x) => () => c.PlayfieldPoint(pulse, x);
    private static float bt(int lane) => TutorialContext.LaneX(NoteKind.Bt, lane);
    private static float fx(int lane) => TutorialContext.LaneX(NoteKind.Fx, lane);
    private static float laser(double v, bool wide = false) => TutorialContext.LaserX(v, wide);

    private static bool allPlaced(TutorialContext c, IEnumerable<TutorialTarget> targets) => c.Chart is { } chart && targets.All(t => TutorialCharts.IsPlaced(chart, t));

    private static string placed(TutorialContext c, IReadOnlyList<TutorialTarget> targets, string what) =>
        c.Chart is { } chart ? $"{TutorialCharts.CountPlaced(chart, targets)} / {targets.Count} {what}" : "";

    /// <summary>The standard "save" step that ends a chapter's mapping part.</summary>
    private static TutorialStep saveStep(string text = "Press {key:Save} to save. The editor writes a .kson file into the song folder; the title bar's * disappears.") =>
        new TutorialStep("Save your work", text)
        {
            Enter = c => c.Bag["saveBefore"] = c.Session?.Commands.UndoDescription,
            Done = c => c.Session is { IsDirty: false } s && s.ChartPath is string p && File.Exists(p),
            Demo = (d, _) => d.Press(EditorAction.Save),
        };

    /// <summary>The menu item at a path ("File", "Export Song", "ksh...") if it is on screen.</summary>
    internal static Drawable? MenuItem(TutorialContext c, params string[] path)
    {
        var editor = c.Editor;
        if (editor == null) return null;

        IReadOnlyList<MenuItem> items = editor.MenuBar.Items;
        MenuItem? item = null;
        foreach (string name in path)
        {
            item = items.FirstOrDefault(i => i.Text.Value.ToString() == name || i.Text.Value.ToString().StartsWith(name, StringComparison.Ordinal));
            if (item == null) return null;
            items = item.Items;
        }

        return editor.MenuBar.ChildrenOfType<Menu.DrawableMenuItem>().FirstOrDefault(d => d.Item == item && TutorialAnchors.IsShown(d));
    }

    /// <summary>Demo: clicks through a menu path, waiting for each submenu to open.</summary>
    private static void clickMenu(ITutorialDemo d, TutorialContext c, params string[] path)
    {
        for (int i = 0; i < path.Length; i++)
        {
            var prefix = path.Take(i + 1).ToArray();
            d.WaitFor(() => MenuItem(c, prefix) != null, "menu item " + string.Join(" > ", prefix));
            d.Click(() => MenuItem(c, prefix));
        }
    }

    /// <summary>The field (text box, number box, combo box) in the same row as a label inside <paramref name="root"/>.</summary>
    internal static T? FieldNear<T>(Drawable? root, string label) where T : Drawable
    {
        var sprite = root?.ChildrenOfType<osu.Framework.Graphics.Sprites.SpriteText>().FirstOrDefault(t => t.Text.ToString() == label && TutorialAnchors.IsShown(t));
        for (Drawable? p = sprite?.Parent; p != null && p != root; p = p.Parent)
        {
            if (p is CompositeDrawable cd && cd.ChildrenOfType<T>().FirstOrDefault(TutorialAnchors.IsShown) is { } field) return field;
        }

        return null;
    }

    private static T? ancestor<T>(Drawable? d) where T : class
    {
        for (Drawable? p = d; p != null; p = p.Parent)
        {
            if (p is T t) return t;
        }

        return null;
    }
}
