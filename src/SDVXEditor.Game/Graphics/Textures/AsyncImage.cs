using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;

namespace SDVXEditor.Game.Graphics.Textures;

/// <summary>
/// An image from disk that loads in the background through <see cref="ImageTextureCache"/> and fades
/// in over whatever placeholder sits underneath. Fills its size (cropping the overflow).
/// </summary>
public partial class AsyncImage : CompositeDrawable
{
    private readonly string? path;
    private readonly ImageVariant variant;
    private readonly double fadeDuration;

    [Resolved]
    private ImageTextureCache cache { get; set; } = null!;

    /// <summary>Raised on the update thread once the texture is shown (not raised for a missing image).</summary>
    public event Action<AsyncImage>? Loaded;

    public bool HasTexture { get; private set; }

    /// <summary>Raised on the update thread when loading finished, with or without a texture.</summary>
    public event Action<AsyncImage>? Completed;

    public bool IsCompleted { get; private set; }

    public AsyncImage(string? path, ImageVariant variant, double fadeDuration = 200)
    {
        this.path = path;
        this.variant = variant;
        this.fadeDuration = fadeDuration;
        Masking = true;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        var task = cache.GetAsync(path, variant);

        if (task.IsCompletedSuccessfully)
        {
            show(task.Result, 0);
            return;
        }

        task.ContinueWith(t =>
        {
            if (IsDisposed) return;

            try
            {
                Schedule(() => show(t.IsCompletedSuccessfully ? t.Result : null, fadeDuration));
            }
            catch (ObjectDisposedException)
            {
                // the panel scrolled away and was disposed while the image loaded
            }
        }, TaskScheduler.Default);
    }

    private void show(Texture? texture, double fade)
    {
        if (IsDisposed) return;

        if (texture == null)
        {
            IsCompleted = true;
            Completed?.Invoke(this);
            return;
        }

        var sprite = new Sprite
        {
            RelativeSizeAxes = Axes.Both,
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            FillMode = FillMode.Fill,
            Texture = texture,
            Alpha = 0,
        };

        AddInternal(sprite);
        sprite.FadeIn(fade);
        HasTexture = true;
        IsCompleted = true;
        Loaded?.Invoke(this);
        Completed?.Invoke(this);
    }
}
