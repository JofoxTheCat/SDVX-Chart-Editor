using System.Text;

namespace SDVXEditor.Core.Library;

/// <summary>
/// Minimal title/artist reader for the formats a new song usually starts from: ID3v2 (mp3),
/// Vorbis comments (ogg/opus via Ogg pages, flac metadata blocks). Anything it cannot read yields nulls —
/// the importer then falls back to the file name. Reads at most the first few MiB.
/// </summary>
public static class AudioTags
{
    private const int maxRead = 4 * 1024 * 1024;

    public static (string? Title, string? Artist) Read(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            byte[] head = new byte[Math.Min(fs.Length, maxRead)];
            int n = fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            var data = head.AsSpan(0, n);
            if (data.StartsWith("ID3"u8)) return id3v2(data);
            if (data.StartsWith("fLaC"u8)) return flac(data);
            if (data.StartsWith("OggS"u8)) return ogg(data);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException) { }
        return (null, null);
    }

    // ---------------------------------------------------------------- ID3v2.3 / 2.4

    private static (string?, string?) id3v2(ReadOnlySpan<byte> d)
    {
        if (d.Length < 10) return (null, null);
        int ver = d[3];
        bool unsync = (d[5] & 0x80) != 0;
        int size = synchsafe(d.Slice(6, 4));
        int pos = 10;
        int end = Math.Min(d.Length, 10 + size);
        if ((d[5] & 0x40) != 0 && pos + 4 <= end) // extended header
            pos += ver == 4 ? synchsafe(d.Slice(pos, 4)) : 4 + be32(d.Slice(pos, 4));
        string? title = null, artist = null;
        if (ver < 3 || unsync) return (null, null); // v2.2 frames / unsynchronised tags: not worth it here
        while (pos + 10 <= end)
        {
            var id = Encoding.ASCII.GetString(d.Slice(pos, 4));
            if (id[0] == '\0') break;
            int fsize = ver == 4 ? synchsafe(d.Slice(pos + 4, 4)) : be32(d.Slice(pos + 4, 4));
            pos += 10;
            if (fsize <= 0 || pos + fsize > end) break;
            if (id == "TIT2") title = id3Text(d.Slice(pos, fsize));
            else if (id == "TPE1") artist = id3Text(d.Slice(pos, fsize));
            pos += fsize;
            if (title != null && artist != null) break;
        }
        return (clean(title), clean(artist));
    }

    private static string? id3Text(ReadOnlySpan<byte> f)
    {
        if (f.Length < 2) return null;
        var body = f[1..];
        string s;
        switch (f[0])
        {
            case 0:
                s = Encoding.Latin1.GetString(body);
                break;
            case 1:
                var text = stripBom(body, out bool be);
                s = (be ? Encoding.BigEndianUnicode : Encoding.Unicode).GetString(text);
                break;
            case 2:
                s = Encoding.BigEndianUnicode.GetString(body);
                break;
            default:
                s = Encoding.UTF8.GetString(body);
                break;
        }
        int nul = s.IndexOf('\0');
        return nul >= 0 ? s[..nul] : s;
    }

    private static ReadOnlySpan<byte> stripBom(ReadOnlySpan<byte> b, out bool bigEndian)
    {
        bigEndian = false;
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            bigEndian = true;
            return b[2..];
        }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return b[2..];
        return b;
    }

    private static int synchsafe(ReadOnlySpan<byte> b) => (b[0] & 0x7F) << 21 | (b[1] & 0x7F) << 14 | (b[2] & 0x7F) << 7 | (b[3] & 0x7F);
    private static int be32(ReadOnlySpan<byte> b) => b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3];

    // ---------------------------------------------------------------- FLAC

    private static (string?, string?) flac(ReadOnlySpan<byte> d)
    {
        int pos = 4;
        while (pos + 4 <= d.Length)
        {
            bool last = (d[pos] & 0x80) != 0;
            int type = d[pos] & 0x7F;
            int len = d[pos + 1] << 16 | d[pos + 2] << 8 | d[pos + 3];
            pos += 4;
            if (pos + len > d.Length) break;
            if (type == 4) return vorbisComments(d.Slice(pos, len));
            pos += len;
            if (last) break;
        }
        return (null, null);
    }

    // ---------------------------------------------------------------- Ogg (Vorbis / Opus)

    private static (string?, string?) ogg(ReadOnlySpan<byte> d)
    {
        // Reassemble the first packets from page segments; the comment header is packet #2 and may span pages.
        var packets = new List<byte[]>();
        var current = new List<byte>();
        int pos = 0;
        while (pos + 27 <= d.Length && packets.Count < 2)
        {
            if (!d.Slice(pos, 4).SequenceEqual("OggS"u8)) break;
            int segs = d[pos + 26];
            if (pos + 27 + segs > d.Length) break;
            var table = d.Slice(pos + 27, segs);
            int dataPos = pos + 27 + segs;
            foreach (byte lace in table)
            {
                if (dataPos + lace > d.Length) return (null, null);
                current.AddRange(d.Slice(dataPos, lace).ToArray());
                dataPos += lace;
                if (lace < 255)
                {
                    packets.Add(current.ToArray());
                    current.Clear();
                    if (packets.Count == 2) break;
                }
            }
            pos = dataPos;
        }
        if (packets.Count < 2) return (null, null);
        var p = packets[1].AsSpan();
        if (p.Length > 7 && p[0] == 3 && p.Slice(1, 6).SequenceEqual("vorbis"u8)) return vorbisComments(p[7..]);
        if (p.Length > 8 && p[..8].SequenceEqual("OpusTags"u8)) return vorbisComments(p[8..]);
        return (null, null);
    }

    private static (string?, string?) vorbisComments(ReadOnlySpan<byte> d)
    {
        int pos = 0;
        if (d.Length < 8) return (null, null);
        int vendor = le32(d);
        pos = 4 + vendor;
        if (vendor < 0 || pos + 4 > d.Length) return (null, null);
        int count = le32(d[pos..]);
        pos += 4;
        string? title = null, artist = null;
        for (int i = 0; i < count && pos + 4 <= d.Length; i++)
        {
            int len = le32(d[pos..]);
            pos += 4;
            if (len < 0 || pos + len > d.Length) break;
            string kv = Encoding.UTF8.GetString(d.Slice(pos, len));
            pos += len;
            int eq = kv.IndexOf('=');
            if (eq <= 0) continue;
            string key = kv[..eq].ToUpperInvariant();
            if (key == "TITLE") title ??= kv[(eq + 1)..];
            else if (key == "ARTIST") artist ??= kv[(eq + 1)..];
        }
        return (clean(title), clean(artist));
    }

    private static int le32(ReadOnlySpan<byte> b) => b[0] | b[1] << 8 | b[2] << 16 | b[3] << 24;

    private static string? clean(string? s)
    {
        s = s?.Trim().Trim('\0').Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
