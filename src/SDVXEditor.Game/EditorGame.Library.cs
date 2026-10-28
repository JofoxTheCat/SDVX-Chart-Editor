using osu.Framework.Logging;
using SDVXEditor.Game.Graphics.Textures;
using SDVXEditor.Game.Screens.SongSelect;
using SDVXEditor.Game.Songs;

namespace SDVXEditor.Game;

/// <summary>Song library wiring: the <see cref="LibraryManager"/>, the image texture cache and file drops onto the window.</summary>
public partial class EditorGame
{
    /// <summary>The song library (editor songs, --songs, the launcher's Nautica folder).</summary>
    public LibraryManager Library { get; private set; } = null!;

    private ImageTextureCache? textureCache;

    private readonly List<string> droppedPaths = new List<string>();
    private bool dropFlushScheduled;

    private void loadLibrary()
    {
        dependencies.Cache(Library = LibraryManager.FromStartup(libraryStartupOptions()));
        dependencies.Cache(textureCache = new ImageTextureCache(Host.Renderer));

        if (Host.Window != null)
            Host.Window.DragDrop += onDragDrop;
    }

    /// <summary>
    /// The window reports one call per dropped file, off the update thread. Collect them briefly so a
    /// multi-file drop becomes one import batch, then hand them to song select.
    /// </summary>
    private void onDragDrop(string path)
    {
        lock (droppedPaths)
        {
            droppedPaths.Add(path);
            if (dropFlushScheduled) return;

            dropFlushScheduled = true;
        }

        Scheduler.AddDelayed(flushDrops, 100);
    }

    /// <summary>--import: waits until song select is up, then imports like a drop (name clash → Replace / Keep both).</summary>
    private void importOnStart(string path)
    {
        if (screenStack.CurrentScreen is SongSelectScreen songSelect && songSelect.IsLoaded)
            _ = songSelect.ImportFiles(new[] { path });
        else
            Scheduler.AddDelayed(() => importOnStart(path), 100);
    }

    private void flushDrops()
    {
        List<string> paths;

        lock (droppedPaths)
        {
            paths = droppedPaths.ToList();
            droppedPaths.Clear();
            dropFlushScheduled = false;
        }

        if (screenStack.CurrentScreen is SongSelectScreen songSelect)
            _ = songSelect.ImportFiles(paths);
        else
            Logger.Log($"drop ignored outside song select: {string.Join(", ", paths)}", LoggingTarget.Runtime, LogLevel.Important);
    }
}
