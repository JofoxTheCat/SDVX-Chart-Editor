using osu.Framework.Graphics;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// Named UI elements the tutorial can point at. UI code registers its drawables with one line
/// (<c>TutorialAnchors.Register("compose.tool.btchip", tile)</c> or <c>new Foo { … }.WithTutorialAnchor("…")</c>);
/// the tutorial asks <see cref="Resolve"/> for the one that is on screen right now. Drawables are held weakly and a
/// name may have several (two editors, a re-created panel): the newest one that is loaded, alive and visible wins.
/// Places that already expose their parts publicly are registered as resolvers by the tutorial itself
/// (<see cref="RegisterResolver"/>), so UI files that other work touches need no edit.
/// </summary>
public static class TutorialAnchors
{
    private static readonly Dictionary<string, List<WeakReference<Drawable>>> drawables = new Dictionary<string, List<WeakReference<Drawable>>>();
    private static readonly Dictionary<string, Func<Drawable?>> resolvers = new Dictionary<string, Func<Drawable?>>();
    private static readonly object gate = new object();

    /// <summary>Registers <paramref name="drawable"/> under <paramref name="name"/> (call from a constructor or load).</summary>
    public static void Register(string name, Drawable drawable)
    {
        lock (gate)
        {
            if (!drawables.TryGetValue(name, out var list))
                drawables[name] = list = new List<WeakReference<Drawable>>();

            list.RemoveAll(w => !w.TryGetTarget(out var d) || (d.IsLoaded && d.Parent == null));
            list.Add(new WeakReference<Drawable>(drawable));
        }
    }

    /// <summary>A lookup for a name that has no registration line (e.g. a control inside a dialog, found by its text).</summary>
    public static void RegisterResolver(string name, Func<Drawable?> resolve)
    {
        lock (gate) resolvers[name] = resolve;
    }

    /// <summary>The drawable for <paramref name="name"/> that is on screen now, or null.</summary>
    public static Drawable? Resolve(string name)
    {
        lock (gate)
        {
            if (drawables.TryGetValue(name, out var list))
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].TryGetTarget(out var d) && IsShown(d)) return d;
                }
            }
        }

        Func<Drawable?>? resolver;
        lock (gate) resolvers.TryGetValue(name, out resolver);
        var r = resolver?.Invoke();
        return r != null && IsShown(r) ? r : null;
    }

    /// <summary>Every registered name (tests, docs).</summary>
    public static IReadOnlyList<string> Names
    {
        get
        {
            lock (gate) return drawables.Keys.Concat(resolvers.Keys).Distinct().OrderBy(n => n).ToList();
        }
    }

    /// <summary>Loaded, not disposed, and it and every parent present (alpha &gt; 0, not hidden).</summary>
    public static bool IsShown(Drawable d)
    {
        if (!d.IsLoaded) return false;

        bool inGame = false;
        for (Drawable? p = d; p != null; p = p.Parent)
        {
            if (!p.IsPresent) return false;
            inGame |= p is osu.Framework.Game;
        }

        // removed (or disposed) drawables are cut off from the game
        if (!inGame) return false;

        var quad = d.ScreenSpaceDrawQuad.AABBFloat;
        return quad.Width > 0.5f && quad.Height > 0.5f;
    }
}

public static class TutorialAnchorExtensions
{
    /// <summary>Registers the drawable as a tutorial anchor and returns it (for object initialisers).</summary>
    public static T WithTutorialAnchor<T>(this T drawable, string name) where T : Drawable
    {
        TutorialAnchors.Register(name, drawable);
        return drawable;
    }
}
