using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using SDVXEditor.Game.Graphics.Textures;

namespace SDVXEditor.Game.Graphics.Backgrounds;

/// <summary>
/// A song background from disk: the chart's background image, or — when the chart has none — its jacket,
/// blurred so the upscale does not show. Falls back to a hue gradient while nothing is loadable.
/// </summary>
public sealed class ImageBackgroundSource : IBackgroundSource
{
    private readonly string? path;
    private readonly bool blurredJacket;
    private readonly float hue;

    /// <param name="backgroundPath">The chart's background image, preferred.</param>
    /// <param name="jacketPath">Used blurred when there is no background image.</param>
    /// <param name="hue">Placeholder gradient seed when neither loads.</param>
    public ImageBackgroundSource(string? backgroundPath, string? jacketPath, float hue)
    {
        blurredJacket = backgroundPath == null;
        path = backgroundPath ?? jacketPath;
        this.hue = hue;
    }

    public string Key => path != null ? $"image:{(blurredJacket ? "blur:" : "")}{path}" : $"hue:{hue}";

    public Drawable CreateDrawable() => new ImageBackground(path, blurredJacket ? ImageVariant.BlurredJacket : ImageVariant.Background, hue);

    private partial class ImageBackground : CompositeDrawable, IDeferredBackground
    {
        public bool IsReady { get; private set; }

        public event Action? Ready;

        public ImageBackground(string? path, ImageVariant variant, float hue)
        {
            RelativeSizeAxes = Axes.Both;

            var gradient = GradientBackgroundSource.FromHue(hue).CreateDrawable();

            if (path == null)
            {
                InternalChild = gradient;
                IsReady = true;
                return;
            }

            var image = new AsyncImage(path, variant, 0) { RelativeSizeAxes = Axes.Both };
            InternalChildren = new[] { gradient, image };

            image.Completed += _ =>
            {
                IsReady = true;
                Ready?.Invoke();
            };
        }
    }
}
