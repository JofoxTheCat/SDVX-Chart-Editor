using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Shaders;
using osuTK;

namespace SDVXEditor.Game.Graphics;

/// <summary>
/// Draws a list of solid-colour quads in one draw node. The owner refills the list (local
/// coordinates) whenever its content changes; nothing is allocated per object, so a playfield can
/// show thousands of notes, laser segments and grid lines without one drawable each.
/// </summary>
public partial class QuadBatch : Drawable, ITexturedShaderDrawable
{
    private readonly List<(Quad Quad, ColourInfo Colour)> quads = new List<(Quad, ColourInfo)>();

    public IShader TextureShader { get; private set; } = null!;

    public int Count => quads.Count;

    /// <summary>The quads added since the last <see cref="Clear"/> (read-only view, e.g. for post-effect ghost copies).</summary>
    public IReadOnlyList<(Quad Quad, ColourInfo Colour)> Quads => quads;

    /// <summary>Replaces every quad's colour by <paramref name="map"/> of it (preview colour conversion, lane tint).</summary>
    public void MapColours(Func<ColourInfo, ColourInfo> map, int from = 0, int to = -1)
    {
        int end = to < 0 ? quads.Count : Math.Min(to, quads.Count);
        for (int i = from; i < end; i++) quads[i] = (quads[i].Quad, map(quads[i].Colour));
        Invalidate(Invalidation.DrawNode);
    }

    public QuadBatch()
    {
        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(ShaderManager shaders)
    {
        TextureShader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
    }

    public void Clear()
    {
        quads.Clear();
        Invalidate(Invalidation.DrawNode);
    }

    public void Add(Quad quad, ColourInfo colour)
    {
        quads.Add((quad, colour));
        Invalidate(Invalidation.DrawNode);
    }

    /// <summary>Axis-aligned rectangle in local coordinates.</summary>
    public void AddRect(float x, float y, float width, float height, ColourInfo colour)
    {
        if (width <= 0 || height <= 0) return;
        Add(new Quad(x, y, width, height), colour);
    }

    /// <summary>A horizontal band moving from x1±w/2 at y1 to x2±w/2 at y2 (a laser segment).</summary>
    public void AddBand(float x1, float y1, float x2, float y2, float halfWidth, ColourInfo colour) =>
        Add(new Quad(new Vector2(x2 - halfWidth, y2), new Vector2(x2 + halfWidth, y2), new Vector2(x1 - halfWidth, y1), new Vector2(x1 + halfWidth, y1)), colour);

    /// <summary>Outline of a rectangle (selection box, selected-note frame).</summary>
    public void AddFrame(float x, float y, float width, float height, float thickness, ColourInfo colour)
    {
        AddRect(x, y, width, thickness, colour);
        AddRect(x, y + height - thickness, width, thickness, colour);
        AddRect(x, y + thickness, thickness, height - 2 * thickness, colour);
        AddRect(x + width - thickness, y + thickness, thickness, height - 2 * thickness, colour);
    }

    protected override DrawNode CreateDrawNode() => new QuadBatchDrawNode(this);

    private class QuadBatchDrawNode : TexturedShaderDrawNode
    {
        private readonly List<(Quad Quad, ColourInfo Colour)> quads = new List<(Quad, ColourInfo)>();

        protected new QuadBatch Source => (QuadBatch)base.Source;

        public QuadBatchDrawNode(QuadBatch source)
            : base(source)
        {
        }

        public override void ApplyState()
        {
            base.ApplyState();
            quads.Clear();
            quads.AddRange(Source.quads);
        }

        protected override void Draw(IRenderer renderer)
        {
            base.Draw(renderer);

            if (quads.Count == 0) return;

            BindTextureShader(renderer);

            float alpha = DrawColourInfo.Colour.TopLeft.Linear.A;

            foreach (var (quad, colour) in quads)
            {
                var c = colour;
                if (alpha < 1) c = c.MultiplyAlpha(alpha);
                renderer.DrawQuad(renderer.WhitePixel, quad * DrawInfo.Matrix, c);
            }

            UnbindTextureShader(renderer);
        }
    }
}
