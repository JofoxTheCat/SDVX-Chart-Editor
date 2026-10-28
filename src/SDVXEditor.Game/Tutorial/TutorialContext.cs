using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Framework.Testing;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit;
using SDVXEditor.Game.Screens.Edit.Compose;
using SDVXEditor.Game.Screens.Edit.Input;
using SDVXEditor.Game.Screens.Play;
using SDVXEditor.Game.Screens.SongSelect;

namespace SDVXEditor.Game.Tutorial;

/// <summary>What a step can see: the game's screens and dialogs, the tutorial song and a per-chapter scratch bag.</summary>
public sealed class TutorialContext
{
    public TutorialContext(EditorGame game, TutorialManager manager, TutorialCourse course, ITutorialSong track, int chapter, string songFolder)
    {
        Game = game;
        Manager = manager;
        Course = course;
        Track = track;
        Chapter = chapter;
        SongFolder = songFolder;
    }

    public EditorGame Game { get; }
    public TutorialManager Manager { get; }
    public TutorialCourse Course { get; }

    /// <summary>The running tutorial's song.</summary>
    public ITutorialSong Track { get; }

    /// <summary>The classic tutorial's song definition (its chapters only).</summary>
    public TutorialSongDefinition Song => (TutorialSongDefinition)Track;

    /// <summary>The main tutorial's song definition (its chapters only).</summary>
    public MainTutorialSong Main => (MainTutorialSong)Track;

    public int Chapter { get; }

    private MainTutorialTargets? mainTargets;

    /// <summary>The main tutorial's ghost targets, read from its solution chart (computed once per chapter run).</summary>
    public MainTutorialTargets Targets => mainTargets ??= MainTutorialCharts.Targets(Main, MainTutorialCharts.Solution(Main, Course.ReadText("solution.kson")));

    /// <summary>The tutorial's one song folder ("Tutorial", "Tutorial (classic)").</summary>
    public string SongFolder { get; }

    /// <summary>Per-run scratch values of the steps (snapshots for "changed" conditions).</summary>
    public Dictionary<string, object?> Bag { get; } = new Dictionary<string, object?>();

    public IScreen? Screen => Game.TutorialScreens.CurrentScreen;
    public DialogOverlay Dialogs => Game.TutorialDialogs;

    /// <summary>The current editor, if any editor is on screen.</summary>
    public EditorScreen? AnyEditor => Screen is EditorScreen { IsLoaded: true } e ? e : null;

    /// <summary>The current editor when it has the tutorial song open.</summary>
    public EditorScreen? Editor => Screen is EditorScreen { IsLoaded: true } e && IsTutorialSession(e.Session) ? e : null;

    /// <summary>The editor under a playtest started from the tutorial song.</summary>
    public PlaytestScreen? Playtest => Screen is PlaytestScreen { IsLoaded: true } p ? p : null;

    public SongSelectScreen? SongSelect => Screen as SongSelectScreen;

    public EditorSession? Session => Editor?.Session;
    public Chart? Chart => Session?.Chart;

    public T? Dialog<T>() where T : DialogWindow => Dialogs.Current as T;

    public bool IsTutorialSession(EditorSession s) => s.Song?.FolderPath is string f && IsTutorialSessionFolder(f);

    public bool IsTutorialSessionFolder(string folder) =>
        string.Equals(Path.GetFullPath(folder).TrimEnd('\\', '/'), Path.GetFullPath(SongFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>The open drop-down / context menu layer, if any (combo box lists).</summary>
    public Drawable? OpenPopup() => Game.ChildrenOfType<SDVXEditor.Game.Graphics.Containers.PopupLayer>().FirstOrDefault(p => p.IsOpen);

    // ---- playfield geometry (ghosts, demos) ----------------------------------------------

    /// <summary>Screen-space point of a pulse and a playfield x (local design px) in the editor's compose playfield.</summary>
    public osuTK.Vector2 PlayfieldPoint(long pulse, float x)
    {
        var pf = Editor?.Playfield ?? throw new InvalidOperationException("tutorial: no editor playfield");
        return pf.ToScreenSpace(new osuTK.Vector2(x, pf.YOfPulse(pulse)));
    }

    /// <summary>Centre x of a lane.</summary>
    public static float LaneX(NoteKind kind, int lane) => PlayfieldLayout.LaneLeft(kind, lane) + PlayfieldLayout.LaneWidth(kind) / 2;

    public static float LaserX(double value, bool wide = false) => PlayfieldLayout.LaserX(value, wide);

    public double MsOf(long pulse) => Session?.PulseToMs(pulse) ?? Track.MsAt(pulse);

    /// <summary>Current editor time as a pulse (0 without an editor).</summary>
    public long CursorPulse => Session is { } s && Editor is { } e ? (long)Math.Round(s.MsToPulse(e.Playback.CurrentTime.Value)) : 0;

    // ---- spotlight areas (screen space) ---------------------------------------------------

    /// <summary>
    /// The compose playfield between two pulses (the BT/FX lanes, or with <paramref name="lasers"/> the whole width with the
    /// wide-laser overhang), clipped to what is on screen; null when the editor or that stretch is not visible.
    /// </summary>
    public osu.Framework.Graphics.Primitives.RectangleF? PlayfieldRegion(long from, long to, bool lasers = false, bool wide = false)
    {
        if (Editor is not { CurrentTab: EditorTab.Compose } e || Dialogs.HasOpenDialog) return null;
        var pf = e.Playfield;
        float left = PlayfieldLayout.LANES_LEFT, right = PlayfieldLayout.LANES_RIGHT;
        if (lasers || wide)
        {
            left = Math.Min(left, PlayfieldLayout.LaserX(0, wide)) - PlayfieldLayout.LASER_HALF_WIDTH - 8;
            right = Math.Max(right, PlayfieldLayout.LaserX(1, wide)) + PlayfieldLayout.LASER_HALF_WIDTH + 8;
        }

        var a = pf.ToScreenSpace(new osuTK.Vector2(left, pf.YOfPulse(to) - 10));
        var b = pf.ToScreenSpace(new osuTK.Vector2(right, pf.YOfPulse(from) + 10));
        var r = new osu.Framework.Graphics.Primitives.RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y);
        var vis = pf.ScreenSpaceDrawQuad.AABBFloat;
        var cut = osu.Framework.Graphics.Primitives.RectangleF.Intersect(r, new osu.Framework.Graphics.Primitives.RectangleF(r.X, vis.Y, r.Width, vis.Height)); // only the visible time range
        return cut.Width > 4 && cut.Height > 4 ? cut : null;
    }

    /// <summary>One column of the camera tab's lanes (optionally only between two pulses), clipped to the lanes.</summary>
    public osu.Framework.Graphics.Primitives.RectangleF? CameraColumn(Screens.Edit.Camera.CamLane lane, long? from = null, long? to = null)
    {
        if (Editor is not { CurrentTab: EditorTab.Camera } e || Dialogs.HasOpenDialog) return null;
        var lanes = e.CameraContent.Lanes;
        if (!TutorialAnchors.IsShown(lanes)) return null;
        float x = Screens.Edit.Camera.CameraLanes.LaneLeft(lane);
        float y0 = to is long t ? lanes.YOfPulse(t) - 16 : 0, y1 = from is long f ? lanes.YOfPulse(f) + 16 : lanes.DrawHeight;
        var a = lanes.ToScreenSpace(new osuTK.Vector2(x, Math.Max(0, y0)));
        var b = lanes.ToScreenSpace(new osuTK.Vector2(x + Screens.Edit.Camera.CameraLaneEditor.LANE_WIDTH, Math.Min(lanes.DrawHeight, y1)));
        var r = new osu.Framework.Graphics.Primitives.RectangleF(a.X, a.Y, b.X - a.X, b.Y - a.Y);
        return r.Width > 4 && r.Height > 4 ? r : null;
    }

    /// <summary>A drawable's rectangle grown to everything it draws (labels that stick out of its box), in screen space.</summary>
    public static osu.Framework.Graphics.Primitives.RectangleF? DeepRect(Drawable? d)
    {
        if (d == null || !TutorialAnchors.IsShown(d)) return null;
        var r = d.ScreenSpaceDrawQuad.AABBFloat;
        if (d is CompositeDrawable cd)
        {
            foreach (var c in cd.ChildrenOfType<Drawable>())
            {
                if (!c.IsPresent) continue;
                var q = c.ScreenSpaceDrawQuad.AABBFloat;
                if (q.Width < 1 || q.Height < 1) continue;
                r = osu.Framework.Graphics.Primitives.RectangleF.Union(r, q);
            }
        }

        return r;
    }

    /// <summary>The union of several drawables' rectangles (those on screen), or null.</summary>
    public static osu.Framework.Graphics.Primitives.RectangleF? Union(params Drawable?[] ds)
    {
        osu.Framework.Graphics.Primitives.RectangleF? r = null;
        foreach (var d in ds)
        {
            if (d == null || !TutorialAnchors.IsShown(d)) continue;
            var q = d.ScreenSpaceDrawQuad.AABBFloat;
            r = r is { } x ? osu.Framework.Graphics.Primitives.RectangleF.Union(x, q) : q;
        }

        return r;
    }

    // ---- finding UI ----------------------------------------------------------------------

    /// <summary>The clickable that shows <paramref name="text"/> inside <paramref name="root"/> (button, check box, tab header), or the text itself.</summary>
    public static Drawable? FindText(Drawable? root, string text, bool contains = false)
    {
        if (root == null) return null;

        bool matches(string s) => contains ? s.Contains(text, StringComparison.OrdinalIgnoreCase) : s == text;

        Drawable? sprite = root.ChildrenOfType<SpriteText>().FirstOrDefault(t => TutorialAnchors.IsShown(t) && matches(t.Text.ToString()));

        // multi-word labels in a TextFlowContainer (FlatButton) are one sprite per word
        sprite ??= root.ChildrenOfType<TextFlowContainer>().FirstOrDefault(f => TutorialAnchors.IsShown(f)
                                                                                && matches(string.Join("", f.ChildrenOfType<SpriteText>().Select(t => t.Text.ToString())).Trim()));
        if (sprite == null) return null;

        for (Drawable? p = sprite.Parent; p != null && p != root; p = p.Parent)
        {
            if (p is ClickableContainer) return p;
        }

        return sprite;
    }

    public static T? Find<T>(Drawable? root, Func<T, bool>? where = null) where T : Drawable =>
        root?.ChildrenOfType<T>().FirstOrDefault(d => TutorialAnchors.IsShown(d) && (where == null || where(d)));

    /// <summary>The binding in effect for an editor action, e.g. "Ctrl+Z" (or "not set", like the options).</summary>
    public static string KeyOf(EditorAction action)
    {
        string s = EditorKeyBindings.ShortcutOf(action);
        return string.IsNullOrEmpty(s) ? "(not set)" : s;
    }
}

/// <summary>Common requirements: where a step needs the user, a hint how to get back, and the "Take me back" action.</summary>
public static class TutorialNeed
{
    /// <summary>The tutorial song open in the editor (and on <paramref name="tab"/> when given).</summary>
    public static Func<TutorialContext, string?> Editor(EditorTab? tab = null) => c =>
    {
        if (c.Playtest != null) return "You are in the playtest. Press Esc to see the results, then Esc again to get back to the editor.";
        if (c.Editor == null)
        {
            return c.AnyEditor != null
                ? "This is another song. Click \"Take me back\" to reopen the tutorial song."
                : "You left the editor. Click \"Take me back\" to reopen the tutorial song (your saved work is still there).";
        }

        if (tab is EditorTab t && c.Editor.CurrentTab != t)
            return $"This step happens on the {t.DisplayName()} tab. Press {TutorialContext.KeyOf(tabAction(t))} or click \"Take me back\".";

        return null;
    };

    public static Action<TutorialContext> RestoreEditor(EditorTab? tab = null) => c =>
    {
        if (c.Editor == null)
            c.Manager.OpenTutorialSong();
        else if (tab is EditorTab t)
            c.Editor.SelectTab(t);
    };

    /// <summary>A dialog of type <typeparamref name="T"/> open on top (inside the tutorial editor).</summary>
    public static Func<TutorialContext, string?> Dialog<T>(string name, string howToOpen) where T : DialogWindow => c =>
    {
        if (Editor()(c) is { } notInEditor) return notInEditor;
        if (c.Dialogs.OpenDialogs.OfType<T>().Any()) return c.Dialogs.Current is T ? null : "Another window is on top. Close it (Esc) to get back to the " + name + ".";

        return $"The {name} window was closed. {howToOpen} or click \"Take me back\" to open it again.";
    };

    public static EditorAction tabAction(EditorTab tab) => tab switch
    {
        EditorTab.Camera => EditorAction.TabCamera,
        EditorTab.Timing => EditorAction.TabTiming,
        EditorTab.Effects => EditorAction.TabEffects,
        EditorTab.SongSetup => EditorAction.SongSetup,
        _ => EditorAction.TabCompose,
    };

    /// <summary>Song select on screen.</summary>
    public static string? SongSelect(TutorialContext c) =>
        c.SongSelect != null ? null : c.Playtest != null ? "Press Esc twice to leave the playtest." : "Go back to song select: press Esc in the editor (or click \"Take me back\").";

    public static void RestoreSongSelect(TutorialContext c) => c.Manager.ReturnToSongSelect();
}
