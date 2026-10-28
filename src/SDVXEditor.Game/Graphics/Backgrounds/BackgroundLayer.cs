using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;

namespace SDVXEditor.Game.Graphics.Backgrounds;

/// <summary>
/// Supplies the full-screen background of song select and the editor (the song's
/// background image later; a placeholder gradient for now).
/// </summary>
public interface IBackgroundSource
{
    /// <summary>Identity: equal keys mean "same background", no cross-fade.</summary>
    string Key { get; }

    Drawable CreateDrawable();
}

/// <summary>A background drawable whose content arrives later (an image loading from disk).</summary>
public interface IDeferredBackground
{
    bool IsReady { get; }

    /// <summary>Raised once the content can be shown (may be raised from any thread).</summary>
    event Action Ready;
}

/// <summary>Placeholder background: a diagonal two-colour gradient with a soft vignette band.</summary>
public sealed class GradientBackgroundSource : IBackgroundSource
{
    private readonly Colour4 a;
    private readonly Colour4 b;

    public GradientBackgroundSource(Colour4 a, Colour4 b)
    {
        this.a = a;
        this.b = b;
    }

    /// <summary>Background derived from a hue (0-1), so every fake song looks different.</summary>
    public static GradientBackgroundSource FromHue(float hue) => new GradientBackgroundSource(
        Colour4.FromHSV(hue, 0.55f, 0.55f),
        Colour4.FromHSV((hue + 0.12f) % 1, 0.65f, 0.25f));

    public string Key => $"gradient:{a.ToHex()}:{b.ToHex()}";

    public Drawable CreateDrawable() => new Container
    {
        RelativeSizeAxes = Axes.Both,
        Children = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientHorizontal(a, b),
            },
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientVertical(Colour4.White.Opacity(0.08f), Colour4.Black.Opacity(0.35f)),
            },
        },
    };
}

/// <summary>Shows the current <see cref="IBackgroundSource"/> with a dim, cross-fading on change.</summary>
public partial class BackgroundLayer : CompositeDrawable
{
    public readonly Bindable<IBackgroundSource?> Source = new Bindable<IBackgroundSource?>();

    /// <summary>0 = undimmed, 1 = black.</summary>
    public readonly BindableFloat Dim = new BindableFloat { MinValue = 0, MaxValue = 1 };

    private Container layers = null!;
    private Box dim = null!;

    public BackgroundLayer()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
            layers = new Container { RelativeSizeAxes = Axes.Both },
            dim = new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        Dim.BindValueChanged(e => dim.FadeTo(e.NewValue, 200), true);
        Source.BindValueChanged(e =>
        {
            if (e.OldValue?.Key == e.NewValue?.Key && layers.Count > 0)
                return;

            var previous = layers.ToList();

            void retire()
            {
                foreach (var old in previous)
                    old.Delay(300).Expire();
            }

            if (e.NewValue == null)
            {
                retire();
                return;
            }

            var drawable = e.NewValue.CreateDrawable();
            drawable.Alpha = 0;
            layers.Add(drawable);

            if (drawable is IDeferredBackground deferred)
            {
                // keep the old picture until the new one has its texture (or failed / took too long)
                bool done = false;
                void reveal()
                {
                    if (done || drawable.Parent == null) return;
                    done = true;
                    drawable.FadeIn(300);
                    retire();
                }

                deferred.Ready += () => Schedule(reveal);
                Scheduler.AddDelayed(reveal, 1500);
                if (deferred.IsReady) reveal();
                return;
            }

            drawable.FadeIn(300);
            retire();
        }, true);
    }
}
