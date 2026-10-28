using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osuTK;
using SDVXEditor.Core.Editing;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// "kson ✓  ksh ✓  vox ✗" for one setting (<see cref="ExportSupport"/>): three small pills, green =
/// kept, yellow = partly kept, red = dropped by that export. The VOX pill follows the ksh2vox version the
/// session detected (<see cref="VoxConverterDetector"/>); until it is known, or when detection failed, it shows
/// the conservative values marked "vox?". Hovering the VOX pill says what the converter keeps or loses.
/// </summary>
public partial class ExportBadge : FillFlowContainer
{
    public static readonly Colour4 Yes = Colour4.FromHex("3fae5a");
    public static readonly Colour4 Partial = Colour4.FromHex("c99a2e");
    public static readonly Colour4 No = Colour4.FromHex("b23a3a");

    public string FeatureId { get; }

    /// <summary>The row the badge shows now.</summary>
    public FeatureSupport Shown { get; private set; }

    /// <summary>The converter version the VOX pill is based on; null = not known (conservative values, "?").</summary>
    public string? ConverterVersion { get; private set; }

    /// <summary>Tooltip text of the VOX pill.</summary>
    public string VoxTooltip { get; private set; } = "";

    private readonly float size;

    [Resolved(CanBeNull = true)]
    private VoxConverterDetector? converter { get; set; }

    private IBindable<ConverterDetection>? detection;

    public ExportBadge(string featureId, float size = 12)
    {
        FeatureId = featureId;
        this.size = size;
        AutoSizeAxes = Axes.Both;
        Direction = FillDirection.Horizontal;
        Spacing = new Vector2(3, 0);
        Shown = ExportSupport.Get(featureId);
        show(ConverterDetection.NotStarted);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        if (converter == null) return;

        detection = converter.Current.GetBoundCopy();
        detection.BindValueChanged(e => show(e.NewValue), true);
        converter.EnsureDetected();
    }

    private void show(ConverterDetection d)
    {
        ConverterVersion = d.Version;
        Shown = ExportSupport.Get(FeatureId, ConverterVersion);
        VoxTooltip = TooltipFor(Shown, d);
        Children = new Drawable[]
        {
            Pill("kson", Shown.Kson, size),
            Pill("ksh", Shown.Ksh, size, Shown.Note.Length > 0 ? Shown.Note : null),
            Pill(ConverterVersion == null ? "vox?" : "vox", Shown.Vox, size, VoxTooltip),
        };
    }

    /// <summary>What the VOX path keeps of <paramref name="f"/>, with the converter version it is based on.</summary>
    public static string TooltipFor(FeatureSupport f, ConverterDetection d) => d.State switch
    {
        ConverterDetectState.Detected when d.Version != null => $"VOX (ksh2vox {d.Version}): {f.VoxNote}",
        ConverterDetectState.Failed => $"VOX: {f.VoxNote} (converter not detected: {d.Error}; showing what ksh2vox < {ExportSupport.VoxMappingVersion} keeps)",
        _ => $"VOX: {f.VoxNote} (converter version not known yet; showing what ksh2vox < {ExportSupport.VoxMappingVersion} keeps)",
    };

    public static Colour4 ColourOf(Support s) => s switch
    {
        Support.Yes => Yes,
        Support.Partial => Partial,
        _ => No,
    };

    public static Drawable Pill(string label, Support s, float size = 12, string? tooltip = null) => new TooltipBox
    {
        TooltipText = tooltip ?? string.Empty,
        AutoSizeAxes = Axes.Both,
        Masking = true,
        CornerRadius = 3,
        Children = new Drawable[]
        {
            new Box { RelativeSizeAxes = Axes.Both, Colour = ColourOf(s).Opacity(0.85f) },
            new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(3, 0),
                Padding = new MarginPadding { Horizontal = 4, Vertical = 1 },
                Children = new Drawable[]
                {
                    new SpriteText { Text = label, Font = EditorFont.Bold(size), Colour = Colour4.White },
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(size * 0.75f),
                        Icon = s switch
                        {
                            Support.Yes => FontAwesome.Solid.Check,
                            Support.Partial => FontAwesome.Solid.Adjust,
                            _ => FontAwesome.Solid.Times,
                        },
                        Colour = Colour4.White,
                    },
                },
            },
        },
    };

    private partial class TooltipBox : Container, IHasTooltip
    {
        public LocalisableString TooltipText { get; set; }
    }
}

/// <summary>The camera / background / effect rows of the export loss matrix as a table with notes, for the detected ksh2vox version.</summary>
public partial class ExportMatrixPanel : FillFlowContainer
{
    private readonly Func<FeatureSupport, bool>? filter;

    [Resolved(CanBeNull = true)]
    private VoxConverterDetector? converter { get; set; }

    private IBindable<ConverterDetection>? detection;

    /// <summary>The header line, e.g. "… VOX via ksh2vox 0.20.1." (smoke test).</summary>
    public string HeaderText { get; private set; } = "";

    public ExportMatrixPanel(Func<FeatureSupport, bool>? filter = null, float width = 520)
    {
        this.filter = filter;
        Width = width;
        AutoSizeAxes = Axes.Y;
        Direction = FillDirection.Vertical;
        Spacing = new Vector2(0, 4);
        show(ConverterDetection.NotStarted);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        if (converter == null) return;

        detection = converter.Current.GetBoundCopy();
        detection.BindValueChanged(e => show(e.NewValue), true);
        converter.EnsureDetected();
    }

    /// <summary>"VOX via ksh2vox 0.20.1", or why the version is not known.</summary>
    public static string VoxHeader(ConverterDetection d) => d.State switch
    {
        ConverterDetectState.Detected when d.Version != null => $"VOX via ksh2vox {d.Version}",
        ConverterDetectState.Failed => $"VOX via ksh2vox ? (not detected: {d.Error}; values of ksh2vox < {ExportSupport.VoxMappingVersion})",
        _ => $"VOX via ksh2vox ? (detecting…; values of ksh2vox < {ExportSupport.VoxMappingVersion} until then)",
    };

    private void show(ConverterDetection d)
    {
        Clear();
        HeaderText = $"What each export keeps. kson keeps everything (editor-only data goes to impl). {VoxHeader(d)}.";

        Add(new TextFlowContainer(t =>
        {
            t.Font = EditorFont.Regular(13);
            t.Colour = EditorColours.TextMuted;
        })
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Text = HeaderText,
        });

        foreach (var f in ExportSupport.For(d.Version).Where(f => filter?.Invoke(f) ?? true))
        {
            string note = f.Note.Length > 0 ? $"{f.Note}. VOX: {f.VoxNote}" : $"VOX: {f.VoxNote}";
            Add(new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new SpriteText { Text = f.Label, Font = EditorFont.Bold(14), Colour = Colour4.White },
                    new ExportBadge(f.Id) { Anchor = Anchor.TopRight, Origin = Anchor.TopRight },
                    new TextFlowContainer(t =>
                    {
                        t.Font = EditorFont.Regular(12);
                        t.Colour = EditorColours.TextMuted;
                    })
                    {
                        Y = 18,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Text = note,
                    },
                },
            });
        }
    }
}
