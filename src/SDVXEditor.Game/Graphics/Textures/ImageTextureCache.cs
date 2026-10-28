using System.Collections.Concurrent;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SDVXEditor.Game.Graphics.Textures;

/// <summary>How a cached image is prepared before it becomes a texture.</summary>
/// <param name="MaxSize">Longest side in pixels after downscaling (never upscaled).</param>
/// <param name="BlurSigma">Gaussian blur in pixels of the downscaled image; 0 = none.</param>
public readonly record struct ImageVariant(int MaxSize, float BlurSigma = 0)
{
    /// <summary>Wheel thumbnails (panels are 160x125 design pixels).</summary>
    public static readonly ImageVariant Thumbnail = new ImageVariant(192);

    /// <summary>Full-screen song backgrounds.</summary>
    public static readonly ImageVariant Background = new ImageVariant(1920);

    /// <summary>A jacket stretched to full screen as background: small and blurred, the blur hides the upscale.</summary>
    public static readonly ImageVariant BlurredJacket = new ImageVariant(480, 10);

    public bool IsLarge => MaxSize > 512;
}

/// <summary>
/// Loads images from arbitrary paths (song folders anywhere on disk) into textures: decoded and
/// downscaled off the update thread with ImageSharp, cached per path + file time + variant.
/// <para>
/// Small variants (thumbnails) stay cached for the session — a 192 px texture is ~150 KB, so even a
/// thousand songs stay well inside GPU memory. Large variants keep only the most recent few; an
/// evicted one is disposed a few seconds later, long after any cross-fade that still showed it.
/// </para>
/// </summary>
public sealed class ImageTextureCache : IDisposable
{
    private const int large_capacity = 6;
    private static readonly TimeSpan dispose_delay = TimeSpan.FromSeconds(5);

    private readonly IRenderer renderer;
    private readonly SemaphoreSlim decodeSlots = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 2, 1, 3));
    private readonly ConcurrentDictionary<string, Task<Texture?>> entries = new ConcurrentDictionary<string, Task<Texture?>>();
    private readonly LinkedList<string> largeOrder = new LinkedList<string>();
    private readonly object largeLock = new object();
    private bool disposed;

    public ImageTextureCache(IRenderer renderer)
    {
        this.renderer = renderer;
    }

    /// <summary>
    /// The texture for <paramref name="path"/> in <paramref name="variant"/>, or null when the file is
    /// missing or not a readable image. Never throws for bad files; the task may complete on any thread.
    /// </summary>
    public Task<Texture?> GetAsync(string? path, ImageVariant variant)
    {
        if (string.IsNullOrEmpty(path) || disposed)
            return Task.FromResult<Texture?>(null);

        long stamp;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return Task.FromResult<Texture?>(null);

            stamp = info.LastWriteTimeUtc.Ticks ^ info.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Task.FromResult<Texture?>(null);
        }

        string key = $"{variant.MaxSize}|{variant.BlurSigma}|{stamp}|{Path.GetFullPath(path)}";
        var task = entries.GetOrAdd(key, _ => Task.Run(() => load(path, variant)));

        if (variant.IsLarge)
            touchLarge(key);

        return task;
    }

    private async Task<Texture?> load(string path, ImageVariant variant)
    {
        await decodeSlots.WaitAsync().ConfigureAwait(false);

        try
        {
            if (disposed) return null;

            var image = Image.Load<Rgba32>(path);

            try
            {
                if (Math.Max(image.Width, image.Height) > variant.MaxSize)
                    image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(variant.MaxSize, variant.MaxSize), Mode = ResizeMode.Max }));

                if (variant.BlurSigma > 0)
                    image.Mutate(x => x.GaussianBlur(variant.BlurSigma));

                var texture = renderer.CreateTexture(image.Width, image.Height);
                // the upload owns the image from here and disposes it once it reached the GPU
                texture.SetData(new TextureUpload(image));
                return texture;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or UnknownImageFormatException or InvalidImageContentException or NotSupportedException or ImageFormatException)
        {
            Logger.Log($"image not loadable: {path} ({e.Message})");
            return null;
        }
        finally
        {
            decodeSlots.Release();
        }
    }

    private void touchLarge(string key)
    {
        string? evict = null;

        lock (largeLock)
        {
            largeOrder.Remove(key);
            largeOrder.AddFirst(key);

            if (largeOrder.Count > large_capacity)
            {
                evict = largeOrder.Last!.Value;
                largeOrder.RemoveLast();
            }
        }

        if (evict != null && entries.TryRemove(evict, out var old))
            _ = disposeLater(old);
    }

    private static async Task disposeLater(Task<Texture?> task)
    {
        try
        {
            var texture = await task.ConfigureAwait(false);
            await Task.Delay(dispose_delay).ConfigureAwait(false);
            texture?.Dispose();
        }
        catch (Exception e)
        {
            Logger.Log($"texture disposal failed: {e.Message}");
        }
    }

    public void Dispose()
    {
        disposed = true;

        foreach (var task in entries.Values)
        {
            if (task.IsCompletedSuccessfully)
                task.Result?.Dispose();
        }

        entries.Clear();
    }
}
