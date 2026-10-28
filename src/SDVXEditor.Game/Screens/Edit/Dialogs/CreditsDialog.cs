using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osuTK;
using SDVXEditor.Core.Credits;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Overlays;
using SDVXEditor.Resources;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>
/// Thanks (menu bar → Thanks, song select footer → Thanks): the people and projects behind the editor, read from the
/// embedded <c>SDVXEditor.Resources/Credits/credits.json</c> (<see cref="CreditsFile"/>; edit names there, no code
/// change). Links go through the same confirmation as every other web link (<see cref="ExternalLinks.Confirm"/>).
/// </summary>
public partial class CreditsDialog : DialogWindow
{
    public const string ResourceName = "SDVXEditor.Resources.Credits.credits.json";

    private static CreditsFile? cached;

    /// <summary>The embedded Thanks list (read once; an unreadable file gives an empty list and a log line).</summary>
    public static CreditsFile Credits => cached ??= readCredits();

    private static CreditsFile readCredits()
    {
        try
        {
            using var s = SDVXEditorResources.ResourceAssembly.GetManifestResourceStream(ResourceName);
            if (s == null) return CreditsFile.Empty;
            using var r = new StreamReader(s);
            return CreditsFile.Parse(r.ReadToEnd());
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException or IOException)
        {
            Logger.Log($"credits.json could not be read: {e.Message}");
            return CreditsFile.Empty;
        }
    }

    /// <summary>The clickable links of the list, by their URL (smoke test clicks one).</summary>
    public IReadOnlyDictionary<string, DialogLink> LinkButtons => linkButtons;

    private readonly Dictionary<string, DialogLink> linkButtons = new Dictionary<string, DialogLink>();

    [Resolved]
    private ExternalLinks links { get; set; } = null!;

    public CreditsDialog()
        : base(Credits.Title, new Vector2(480, 470))
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var flow = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 3),
            Padding = new MarginPadding { Horizontal = 12, Vertical = 10 },
        };

        if (Credits.Intro.Length > 0)
        {
            flow.Add(new TextFlowContainer(t =>
            {
                t.Font = EditorFont.Dialog();
                t.Colour = EditorColours.Dialog.Text;
            })
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Text = Credits.Intro,
                Margin = new MarginPadding { Bottom = 6 },
            });
        }

        foreach (var section in Credits.Sections)
        {
            flow.Add(DialogText.Label(section.Title, bold: true, size: 14).With(t => t.Margin = new MarginPadding { Top = 8 }));
            flow.Add(new Box { RelativeSizeAxes = Axes.X, Height = 1, Colour = EditorColours.Dialog.GroupBorder, Margin = new MarginPadding { Bottom = 2 } });

            foreach (var entry in section.Entries)
                flow.Add(row(entry));
        }

        Body.Children = new Drawable[]
        {
            new Container
            {
                Position = new Vector2(4, 4),
                Size = new Vector2(472, 418),
                Masking = true,
                BorderThickness = 1,
                BorderColour = EditorColours.Dialog.ListBorder,
                CornerRadius = 1,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = EditorColours.Dialog.Window },
                    new BasicScrollContainer { RelativeSizeAxes = Axes.Both, Child = flow },
                },
            },
            new DialogButton("Close", Accept)
            {
                IsDefault = true,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-4, -6),
                Size = new Vector2(120, 30),
            },
        };
    }

    private Drawable row(CreditEntry entry)
    {
        var line = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 1),
            Padding = new MarginPadding { Left = 8, Bottom = 3 },
        };

        line.Add(new TextFlowContainer(t =>
        {
            t.Font = EditorFont.Dialog();
            t.Colour = EditorColours.Dialog.Text;
        })
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Text = entry.Role.Length > 0 ? $"{entry.Role}: {entry.Name}" : entry.Name,
        });

        if (entry.Link is { } url)
        {
            var link = new DialogLink(MusicLinks.ShortForm(url, 64), () => links.Confirm(url));
            linkButtons.TryAdd(url, link);
            line.Add(link);
        }

        return line;
    }
}
