namespace SDVXEditor.Core.Library;

/// <summary>A music site a song source may point at, with the hosts that belong to it.</summary>
/// <param name="Name">Shown on buttons and in the support popup ("SoundCloud").</param>
/// <param name="Hosts">
/// Host names, lower case. A host matches itself and every subdomain of it (<c>bandcamp.com</c> also takes
/// <c>artist.bandcamp.com</c>, <c>youtube.com</c> also <c>m.youtube.com</c>), never a look-alike
/// (<c>evilyoutube.com</c>, <c>youtube.com.example.org</c>).
/// </param>
public sealed record MusicService(string Name, IReadOnlyList<string> Hosts);

/// <summary>Outcome of <see cref="MusicLinks.Validate"/>: the normalised https URL and its service, or why it was refused.</summary>
public sealed record MusicLinkResult(bool Ok, string? Url, MusicService? Service, string? Error)
{
    public static MusicLinkResult Fail(string error) => new(false, null, null, error);
}

/// <summary>
/// Song sources ("where to listen / buy"): links to music sites only. The allow-list, the validation and the
/// normalisation every caller shares — Song Setup refuses anything else, and the support buttons only ever open
/// links that pass here again (a chart file may carry anything).
/// <para>
/// Rules: <c>https</c> only (a bare <c>soundcloud.com/x</c> or an <c>http://</c> link is upgraded, any other scheme is
/// refused), no user info, no port, a host on the allow-list (<see cref="Services"/>, subdomains included), at most
/// <see cref="MaxLength"/> characters, no whitespace. Normalising lower-cases the host, drops a default port and
/// removes share/tracking query parameters (<see cref="TrackingParameters"/>, <c>utm_*</c>); everything that
/// identifies the track (YouTube <c>v</c>, <c>list</c>, <c>t</c>) and the fragment stay.
/// </para>
/// </summary>
public static class MusicLinks
{
    public const int MaxLength = 2048;

    /// <summary>Most sources a song keeps (Song Setup refuses more).</summary>
    public const int MaxSources = 16;

    /// <summary>
    /// The music sites. Order matters: the first service with a matching host wins, so the narrower hosts
    /// (<c>music.youtube.com</c>) come before the wider ones (<c>youtube.com</c>).
    /// </summary>
    public static readonly IReadOnlyList<MusicService> Services = new[]
    {
        new MusicService("YouTube Music", new[] { "music.youtube.com" }),
        new MusicService("YouTube", new[] { "youtube.com", "youtu.be" }),
        new MusicService("SoundCloud", new[] { "soundcloud.com", "snd.sc" }), // on.soundcloud.com / m.soundcloud.com = subdomains
        new MusicService("Spotify", new[] { "spotify.com", "spotify.link", "spoti.fi" }), // open.spotify.com = subdomain
        new MusicService("Deezer", new[] { "deezer.com", "deezer.page.link", "dzr.page.link" }), // link.deezer.com = subdomain
        new MusicService("Apple Music", new[] { "music.apple.com", "itunes.apple.com" }),
        new MusicService("Bandcamp", new[] { "bandcamp.com" }), // <artist>.bandcamp.com
        new MusicService("Tidal", new[] { "tidal.com" }), // listen.tidal.com
        new MusicService("Amazon Music", amazonMusicHosts()),
        new MusicService("Beatport", new[] { "beatport.com" }),
        new MusicService("Niconico", new[] { "nicovideo.jp", "nico.ms" }), // www. / sp.nicovideo.jp
        new MusicService("Audiomack", new[] { "audiomack.com" }),
    };

    /// <summary>Query parameters that only say who shared the link or where it was clicked.</summary>
    public static readonly IReadOnlyList<string> TrackingParameters = new[]
    {
        "si", "fbclid", "gclid", "igshid", "mc_cid", "mc_eid", "ref_src", "feature", "pp",
    };

    private static string[] amazonMusicHosts()
    {
        string[] tlds = { "com", "co.uk", "de", "co.jp", "fr", "it", "es", "ca", "com.au", "in", "com.br", "com.mx", "nl" };
        return tlds.Select(t => "music.amazon." + t).ToArray();
    }

    /// <summary>The service <paramref name="host"/> belongs to, or null (host compared case-insensitively, trailing dot ignored).</summary>
    public static MusicService? ServiceForHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        string h = host.Trim().TrimEnd('.').ToLowerInvariant();
        foreach (var s in Services)
            foreach (string allowed in s.Hosts)
                if (h == allowed || h.EndsWith("." + allowed, StringComparison.Ordinal))
                    return s;
        return null;
    }

    /// <summary>
    /// Checks a link typed or pasted by the user (or read from a chart) and returns it normalised.
    /// <paramref name="stripTracking"/> removes share/tracking query parameters.
    /// </summary>
    public static MusicLinkResult Validate(string? input, bool stripTracking = true)
    {
        string text = (input ?? "").Trim();
        if (text.Length == 0) return MusicLinkResult.Fail("Enter a link, e.g. https://soundcloud.com/artist/track");
        if (text.Length > MaxLength) return MusicLinkResult.Fail($"That link is too long (over {MaxLength} characters).");
        if (text.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return MusicLinkResult.Fail("A link cannot contain spaces. Paste one link per row.");

        int colon = text.IndexOf("://", StringComparison.Ordinal);
        if (colon < 0)
        {
            // "soundcloud.com/x" → https; "javascript:…", "mailto:…", "file:…" stay refused below
            if (text.Contains(':') && !looksLikeHostWithPort(text)) return MusicLinkResult.Fail("Only https:// links to music sites are allowed.");
            text = "https://" + text;
        }
        else
        {
            string scheme = text[..colon].ToLowerInvariant();
            if (scheme == "http") text = "https" + text[colon..];
            else if (scheme != "https") return MusicLinkResult.Fail("Only https:// links to music sites are allowed.");
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host))
            return MusicLinkResult.Fail("That is not a valid link.");
        if (uri.UserInfo.Length > 0) return MusicLinkResult.Fail("Links with a user name (user@host) are not allowed.");
        if (!uri.IsDefaultPort) return MusicLinkResult.Fail("Links with a port number are not allowed.");
        if (uri.HostNameType is not UriHostNameType.Dns) return MusicLinkResult.Fail("Use the site's name, not an IP address.");

        string host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        var service = ServiceForHost(host);
        if (service == null)
            return MusicLinkResult.Fail($"{host} is not a music site. Allowed: {string.Join(", ", Services.Select(s => s.Name))}.");

        string query = stripTracking ? cleanQuery(uri.Query) : uri.Query;
        string path = uri.AbsolutePath; // already escaped by Uri
        string url = "https://" + host + path + query + uri.Fragment;
        return new MusicLinkResult(true, url, service, null);
    }

    /// <summary>"localhost:8080/x" style input: a colon before the first slash followed only by digits.</summary>
    private static bool looksLikeHostWithPort(string text)
    {
        int slash = text.IndexOf('/');
        string head = slash < 0 ? text : text[..slash];
        int c = head.IndexOf(':');
        return c > 0 && head[(c + 1)..].All(char.IsAsciiDigit);
    }

    private static string cleanQuery(string query)
    {
        if (query.Length <= 1) return "";
        var kept = query[1..].Split('&', StringSplitOptions.RemoveEmptyEntries).Where(p =>
        {
            string key = Uri.UnescapeDataString(p.Split('=', 2)[0]).ToLowerInvariant();
            return !key.StartsWith("utm_", StringComparison.Ordinal) && !TrackingParameters.Contains(key);
        }).ToList();
        return kept.Count == 0 ? "" : "?" + string.Join("&", kept);
    }

    /// <summary>
    /// The links of <paramref name="sources"/> that may be opened: valid ones, normalised, duplicates removed, in
    /// order. A chart file may hold anything; only these reach a button.
    /// </summary>
    public static IReadOnlyList<(string Url, MusicService Service)> Openable(IEnumerable<string>? sources)
    {
        var list = new List<(string, MusicService)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string s in sources ?? Array.Empty<string>())
        {
            var r = Validate(s);
            if (r.Ok && seen.Add(r.Url!)) list.Add((r.Url!, r.Service!));
            if (list.Count >= MaxSources) break;
        }
        return list;
    }

    /// <summary>Host of a link without a leading <c>www.</c> — what a confirmation shows ("soundcloud.com").</summary>
    public static string DisplayHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        string host = uri.IdnHost.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    /// <summary>Short one-line form for lists: host + path, without the scheme, cut to <paramref name="max"/> characters.</summary>
    public static string ShortForm(string url, int max = 60)
    {
        string s = url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? url[8..] : url;
        if (s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        s = s.TrimEnd('/');
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }
}
