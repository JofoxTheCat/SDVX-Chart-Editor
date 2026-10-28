using System.Text.Json;
using System.Text.Json.Nodes;

namespace SDVXEditor.Core.Credits;

/// <summary>One credited person or project: a name, an optional role and an optional https link.</summary>
public sealed record CreditEntry(string Name, string Role = "", string? Link = null);

/// <summary>A heading of the Thanks list with its entries.</summary>
public sealed record CreditSection(string Title, IReadOnlyList<CreditEntry> Entries);

/// <summary>
/// The Thanks list (menu bar → Thanks, song select → Thanks), read from the embedded
/// <c>SDVXEditor.Resources/Credits/credits.json</c> so names can be edited without touching code:
/// <code>
/// { "title": "Thanks",
///   "intro": "optional line under the title",
///   "sections": [ { "title": "Tutorial",
///                   "entries": [ { "name": "Fax", "role": "Tutorial song", "link": "https://soundcloud.com/fax0508" } ] } ] }
/// </code>
/// The reader is forgiving: a section without a title or entries, an entry without a name are skipped; a link that
/// is not <c>https://</c> is dropped (the entry stays, without a link). Unknown keys are ignored.
/// </summary>
public sealed record CreditsFile(string Title, string Intro, IReadOnlyList<CreditSection> Sections)
{
    public static CreditsFile Empty { get; } = new("Thanks", "", Array.Empty<CreditSection>());

    public static CreditsFile Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                   ?? throw new FormatException("credits.json: the top level must be an object");

        var sections = new List<CreditSection>();
        foreach (var s in root["sections"] as JsonArray ?? new JsonArray())
        {
            if (s is not JsonObject so) continue;
            string title = str(so["title"]);
            var entries = new List<CreditEntry>();
            foreach (var e in so["entries"] as JsonArray ?? new JsonArray())
            {
                if (e is not JsonObject eo) continue;
                string name = str(eo["name"]);
                if (name.Length == 0) continue;
                entries.Add(new CreditEntry(name, str(eo["role"]), SafeLink(str(eo["link"]))));
            }

            if (title.Length > 0 && entries.Count > 0) sections.Add(new CreditSection(title, entries));
        }

        string t = str(root["title"]);
        return new CreditsFile(t.Length > 0 ? t : "Thanks", str(root["intro"]), sections);
    }

    /// <summary>The link if it is an absolute <c>https://</c> URL with a host and no user info, else null.</summary>
    public static string? SafeLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        return Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0 && uri.UserInfo.Length == 0
            ? uri.AbsoluteUri
            : null;
    }

    private static string str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";
}
