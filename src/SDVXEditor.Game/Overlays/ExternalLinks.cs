using osu.Framework.Platform;
using SDVXEditor.Core.Library;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;
using SDVXEditor.Game.Screens.Edit.Dialogs;

namespace SDVXEditor.Game.Overlays;

/// <summary>
/// Opens web links the osu! way: never straight away, always after a short confirmation that names the domain
/// ("Open soundcloud.com in your web browser?" · Open / Copy link / Cancel). Used by the Support the Artist buttons
/// (song sources, <see cref="ShowSupport"/>) and the Thanks list. Cached by <see cref="EditorGame"/>.
/// <para>
/// A smoke run never starts a browser: <see cref="RecordOnly"/> makes the last step record the URL in
/// <see cref="Opened"/> instead of calling <see cref="GameHost.OpenUrlExternally"/>.
/// </para>
/// </summary>
public sealed class ExternalLinks
{
    public const string ConfirmTitle = "Open external link";
    public const string SupportTitle = "Support the Artist";
    public const string OpenLabel = "Open in browser";
    public const string CopyLabel = "Copy link";
    public const string CancelLabel = "Cancel";

    private readonly DialogOverlay dialogs;
    private readonly GameHost host;
    private readonly Func<Clipboard?> clipboard;

    /// <summary>True in a smoke run: confirmed links are recorded, not opened.</summary>
    public bool RecordOnly { get; }

    /// <summary>Every link confirmed with "Open in browser" (in a smoke run: instead of opening it).</summary>
    public List<string> Opened { get; } = new List<string>();

    /// <summary>The last link put on the clipboard with "Copy link".</summary>
    public string? LastCopied { get; private set; }

    public ExternalLinks(DialogOverlay dialogs, GameHost host, Func<Clipboard?> clipboard, bool recordOnly)
    {
        this.dialogs = dialogs;
        this.host = host;
        this.clipboard = clipboard;
        RecordOnly = recordOnly;
    }

    /// <summary>
    /// The confirmation for one https link. Anything but an absolute https URL is refused (nothing opens) —
    /// callers validate first, this is the last line.
    /// </summary>
    public bool Confirm(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;

        string domain = MusicLinks.DisplayHost(url);
        dialogs.Push(new ChoiceDialog(ConfirmTitle,
            new[] { $"Open {domain} in your web browser?", MusicLinks.ShortForm(url, 70) },
            new[]
            {
                new DialogChoice(OpenLabel, () => open(url), IsDefault: true),
                new DialogChoice(CopyLabel, () => copy(url)),
                new DialogChoice(CancelLabel, null),
            }));
        return true;
    }

    /// <summary>
    /// Support the Artist: the song's openable sources (<see cref="MusicLinks.Openable"/>). One link → its confirmation;
    /// several → a small list with the service names, each leading to the confirmation. False when there is none.
    /// </summary>
    public bool ShowSupport(IEnumerable<string>? sources, string? songTitle = null)
    {
        var links = MusicLinks.Openable(sources);
        if (links.Count == 0) return false;
        if (links.Count == 1) return Confirm(links[0].Url);

        var choices = new List<DialogChoice>();
        var labels = new HashSet<string>();
        foreach (var (url, service) in links)
        {
            string label = SupportChoiceLabel(url, service);
            for (int i = 2; !labels.Add(label); i++) label = $"{SupportChoiceLabel(url, service)} ({i})"; // buttons are keyed by label
            choices.Add(new DialogChoice(label, () => Confirm(url)));
        }

        choices.Add(new DialogChoice(CancelLabel, null));
        string intro = string.IsNullOrWhiteSpace(songTitle) ? "Listen to or buy this song on:" : $"Listen to or buy \"{songTitle}\" on:";
        dialogs.Push(new ChoiceDialog(SupportTitle, new[] { intro }, choices, vertical: true, width: 420));
        return true;
    }

    /// <summary>The popup's button text for a source: "SoundCloud — soundcloud.com/fax0508".</summary>
    public static string SupportChoiceLabel(string url, MusicService service) => $"{service.Name} — {MusicLinks.ShortForm(url, 44)}";

    private void open(string url)
    {
        Opened.Add(url);
        if (!RecordOnly)
            host.OpenUrlExternally(url);
    }

    private void copy(string url)
    {
        LastCopied = url;
        if (!RecordOnly)
            clipboard()?.SetText(url);
    }
}
