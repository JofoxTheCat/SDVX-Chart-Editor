using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Video;
using osu.Framework.Logging;
using osu.Framework.Timing;
using osuTK;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.Textures;
using SDVXEditor.Game.Songs;

namespace SDVXEditor.Game.Screens.Edit.Camera;

/// <summary>
/// The background view's preview, like the camera preview: a 16:9 frame with what K-Shoot would show behind the
/// lanes — the background image (a file of the song folder, or a swatch for a KSM preset, which only K-Shoot can
/// draw), the animation layer over it, a still of the movie at its offset — and beside it what each part is and
/// what the game shows instead: the VOX bg_no as "game background N, first used by song …" (read from the game's own
/// music_db.xml through the launcher config). Rebuilt when one of those values changes.
/// </summary>
public partial class BackgroundPreview : CompositeDrawable
{
    public const float FRAME_WIDTH = 352, FRAME_HEIGHT = FRAME_WIDTH * 9 / 16;

    private readonly EditorSession session;

    [Resolved(CanBeNull = true)]
    private LibraryManager? library { get; set; }

    private Container frame = null!;
    private TextFlowContainer info = null!;
    private string shownKey = "";

    /// <summary>The bg_no line as shown (smoke test).</summary>
    public string BgNoText { get; private set; } = "";

    /// <summary>The info text as shown (smoke test).</summary>
    public string InfoText { get; private set; } = "";

    /// <summary>What the frame shows: "image", "preset", "layer", "movie" parts present (smoke test).</summary>
    public IReadOnlyList<string> Parts { get; private set; } = Array.Empty<string>();

    public BackgroundPreview(EditorSession session)
    {
        this.session = session;
        RelativeSizeAxes = Axes.X;
        Height = FRAME_HEIGHT + 22;
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        InternalChildren = new Drawable[]
        {
            new SpriteText { Text = "background preview (what K-Shoot shows; the game uses bg_no)", Font = EditorFont.Regular(12), Colour = EditorColours.TextMuted },
            frame = new Container
            {
                Y = 18,
                Size = new Vector2(FRAME_WIDTH, FRAME_HEIGHT),
                Masking = true,
                CornerRadius = 4,
                BorderThickness = 1,
                BorderColour = Colour4.White.Opacity(0.25f),
            },
            info = new TextFlowContainer(t =>
            {
                t.Font = EditorFont.Regular(13);
                t.Colour = EditorColours.Text;
            })
            {
                X = FRAME_WIDTH + 14,
                Y = 18,
                RelativeSizeAxes = Axes.X,
                Width = 1,
                Padding = new MarginPadding { Right = FRAME_WIDTH + 14 },
                AutoSizeAxes = Axes.Y,
                ParagraphSpacing = 0.35f,
            },
        };
    }

    private string? folder => session.Song?.FolderPath;

    private string? fileIn(string name)
    {
        if (folder == null || string.IsNullOrWhiteSpace(name)) return null;
        try
        {
            string p = Path.GetFullPath(Path.Combine(folder, name));
            return File.Exists(p) ? p : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    protected override void Update()
    {
        base.Update();
        var bg = session.Chart.Bg;
        int? bgNo = session.Chart.VoxExtras.BgNo;
        string key = $"{folder}|{string.Join(";", bg.LegacyBg)}|{bg.Layer?.Filename}|{bg.Layer?.DurationMs}|{bg.Movie?.Filename}|{bg.Movie?.OffsetMs}|{bgNo}";
        if (key == shownKey) return;
        shownKey = key;
        rebuild(bg, bgNo);
    }

    private static Colour4 presetColour(string name) => name switch
    {
        "desert" => Colour4.FromHex("c8a060"),
        "grass" => Colour4.FromHex("4f9a4a"),
        "night" => Colour4.FromHex("1c2350"),
        "deepsea" => Colour4.FromHex("0c3a5c"),
        "sky" => Colour4.FromHex("5fa8e0"),
        "sunset" => Colour4.FromHex("e07a4a"),
        "ocean" => Colour4.FromHex("2a70b0"),
        "flame" => Colour4.FromHex("c03a20"),
        "cyber" => Colour4.FromHex("3a2a80"),
        "space" => Colour4.FromHex("101020"),
        "mars" => Colour4.FromHex("a04a30"),
        "cloudy" => Colour4.FromHex("8890a0"),
        "fantasy" => Colour4.FromHex("a060c0"),
        _ => Colour4.FromHex("303038"),
    };

    private void rebuild(BgInfo bg, int? bgNo)
    {
        var parts = new List<string>();
        var lines = new List<string>();
        frame.Clear();
        frame.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black });

        // 1. background image (ksh bg=a;b: the second one shows from 70 % gauge)
        string image = bg.LegacyBg.ElementAtOrDefault(0) ?? "";
        if (image.Length == 0)
            lines.Add("image: none - K-Shoot uses its default background");
        else if (CameraCommands.BgPresets.Contains(image))
        {
            parts.Add("preset");
            var c = presetColour(image);
            frame.Add(new Box { RelativeSizeAxes = Axes.Both, Colour = ColourInfo.GradientVertical(c.Lighten(0.3f), c.Darken(0.6f)) });
            frame.Add(label($"KSM preset \"{image}\""));
            lines.Add($"image: K-Shoot preset \"{image}\" (drawn by K-Shoot only; the colour is a stand-in)");
        }
        else if (fileIn(image) is { } path)
        {
            parts.Add("image");
            frame.Add(new AsyncImage(path, ImageVariant.Background) { RelativeSizeAxes = Axes.Both });
            lines.Add($"image: {image} (song folder)");
        }
        else
        {
            frame.Add(label($"\"{image}\" not found"));
            lines.Add($"image: {image} - not in the song folder and no preset name");
        }

        if (bg.LegacyBg.ElementAtOrDefault(1) is { Length: > 0 } second)
            lines.Add($"from 70 % gauge: {second}");

        // 2. animation layer (a sprite sheet or preset, drawn over the image)
        if (bg.Layer is { Filename.Length: > 0 } layer)
        {
            string loop = layer.DurationMs == 0 ? "tempo synced" : layer.DurationMs < 0 ? $"{-layer.DurationMs} ms loop, reversed" : $"{layer.DurationMs} ms loop";
            if (CameraCommands.LayerPresets.Contains(layer.Filename))
            {
                parts.Add("layer");
                frame.Add(label($"layer preset \"{layer.Filename}\"", Anchor.BottomLeft));
                lines.Add($"layer: K-Shoot preset \"{layer.Filename}\" ({loop})");
            }
            else if (fileIn(layer.Filename) is { } lp)
            {
                parts.Add("layer");
                frame.Add(new AsyncImage(lp, ImageVariant.Background) { RelativeSizeAxes = Axes.Both, Alpha = 0.75f });
                lines.Add($"layer: {layer.Filename} ({loop})");
            }
            else lines.Add($"layer: {layer.Filename} - not found");
        }

        // 3. movie: a still at its offset
        if (bg.Movie is { Filename.Length: > 0 } movie)
        {
            if (fileIn(movie.Filename) is { } mp && movieStill(mp, movie.OffsetMs) is { } still)
            {
                parts.Add("movie");
                frame.Add(still);
                lines.Add($"movie: {movie.Filename}, still at {Math.Max(0, -movie.OffsetMs)} ms (offset {movie.OffsetMs} ms)");
            }
            else lines.Add($"movie: {movie.Filename} - not found or not playable");
        }

        BgNoText = MusicDbBackgrounds.Describe(bgNo, MusicDbBackgrounds.Load(musicDbPath));
        lines.Add("in the game: " + BgNoText);

        Parts = parts;
        InfoText = string.Join("\n", lines);
        info.Text = InfoText;
    }

    private string? musicDbPath => library?.Launcher?.Config?.GameDir is { } game ? Path.Combine(game, "data", "others", "music_db.xml") : null;

    private static Drawable label(string text, Anchor anchor = Anchor.Centre) => new Container
    {
        Anchor = anchor,
        Origin = anchor,
        AutoSizeAxes = Axes.Both,
        Margin = new MarginPadding(6),
        Masking = true,
        CornerRadius = 3,
        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = Colour4.Black.Opacity(0.55f) },
            new SpriteText { Text = text, Font = EditorFont.Bold(13), Colour = Colour4.White, Margin = new MarginPadding { Horizontal = 6, Vertical = 2 } },
        },
    };

    /// <summary>
    /// A paused video at the chart start (ksh <c>vo</c> = where the movie starts relative to the chart: a negative offset
    /// means the chart starts that far into the movie). Null when the framework cannot open it.
    /// </summary>
    private static Drawable? movieStill(string path, long offsetMs)
    {
        try
        {
            var clock = new StopwatchClock(false);
            clock.Seek(Math.Max(0, -offsetMs));
            return new Video(path, false)
            {
                RelativeSizeAxes = Axes.Both,
                FillMode = FillMode.Fit,
                Clock = new FramedClock(clock),
            };
        }
        catch (Exception e)
        {
            Logger.Log($"movie preview failed: {path} ({e.Message})");
            return null;
        }
    }
}
