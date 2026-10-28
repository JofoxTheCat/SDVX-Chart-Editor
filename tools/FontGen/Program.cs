using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using SharpFNT;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts.Unicode;
using BmCharacter = SharpFNT.Character;

namespace SDVXEditor.Tools.FontGen;

/// <summary>
/// Turns pinned Noto fonts into osu-framework glyph stores (AngelCode BMFont binary <c>.bin</c> plus PNG
/// pages, the format <c>osu.Framework.IO.Stores.GlyphStore</c> reads). Output is deterministic: same
/// sources + same tool = byte-identical files. See docs/FONTS.md for the why.
/// </summary>
public static class Program
{
    /// <summary>A source file, pinned by commit and SHA-256 so a regeneration can never pick up a different font.</summary>
    private sealed record Source(string FileName, string Url, string Sha256);

    private const string noto_cjk_commit = "f8d157532fbfaeda587e826d4cd5b21a49186f7c";
    private const string noto_site_commit = "d3abdda968b38bb6eb0ec59f6f9f58b8c2139bca";

    private static readonly Source[] sources =
    {
        new("NotoSans-Regular.ttf", $"https://raw.githubusercontent.com/notofonts/notofonts.github.io/{noto_site_commit}/fonts/NotoSans/unhinted/ttf/NotoSans-Regular.ttf",
            "f3961a9cde016d41a4879aecda1474d3a36d6bf54fa0e4643de029cc2248b0e8"),
        new("NotoSans-Bold.ttf", $"https://raw.githubusercontent.com/notofonts/notofonts.github.io/{noto_site_commit}/fonts/NotoSans/unhinted/ttf/NotoSans-Bold.ttf",
            "87cb2d84472a7d66da659ee47b6cdb9552326e8c128245231f191b6ac72529d9"),
        new("NotoSans-OFL.txt", "https://raw.githubusercontent.com/notofonts/latin-greek-cyrillic/NotoSans-v2.015/OFL.txt",
            "cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a"),
        new("NotoSansJP-Regular.otf", $"https://raw.githubusercontent.com/notofonts/noto-cjk/{noto_cjk_commit}/Sans/SubsetOTF/JP/NotoSansJP-Regular.otf",
            "dff723ba59d57d136764a04b9b2d03205544f7cd785a711442d6d2d085ac5073"),
        new("NotoSansJP-Bold.otf", $"https://raw.githubusercontent.com/notofonts/noto-cjk/{noto_cjk_commit}/Sans/SubsetOTF/JP/NotoSansJP-Bold.otf",
            "1b0edfb500b73a4fa8a4fcaae1bbbd403994e08e73e3e0da37e70d3853f42c5f"),
        new("NotoSansJP-OFL.txt", $"https://raw.githubusercontent.com/notofonts/noto-cjk/{noto_cjk_commit}/Sans/LICENSE",
            "6a73f9541c2de74158c0e7cf6b0a58ef774f5a780bf191f2d7ec9cc53efe2bf2"),
    };

    /// <summary>
    /// Resolution of the CJK store relative to Roboto's (Roboto is rendered at a 100 px cell, ~84 px em).
    /// The editor never draws text near that size, and the CJK set is ~8,000 glyphs, so it is rendered
    /// smaller and the store is registered with a matching scale (EditorFont.CjkResolution must agree;
    /// the value is also written into the .bin's info block as the font size).
    /// </summary>
    private const float default_cjk_resolution = 0.6f;

    private const int page_size = 2048;
    private const int glyph_spacing = 2;
    private const int glyph_padding = 1;

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "inspect")
            return inspect(args.Skip(1).ToArray());

        string repo = findRepoRoot();
        string outDir = System.IO.Path.Combine(repo, "src", "SDVXEditor.Resources", "Fonts");
        string cacheDir = System.IO.Path.Combine(repo, "artifacts", "fontgen");
        float cjkResolution = default_cjk_resolution;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out": outDir = System.IO.Path.GetFullPath(args[++i]); break;
                case "--cache": cacheDir = System.IO.Path.GetFullPath(args[++i]); break;
                case "--cjk-resolution": cjkResolution = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                default:
                    Console.Error.WriteLine($"unknown argument {args[i]}\nusage: FontGen [--out <Fonts dir>] [--cache <download dir>] [--cjk-resolution <0..1>] | inspect <Family.Asset>...");
                    return 2;
            }
        }

        Directory.CreateDirectory(cacheDir);
        foreach (var s in sources)
            fetch(s, cacheDir).GetAwaiter().GetResult();

        var roboto = new Dictionary<string, BitmapFont>
        {
            ["Regular"] = loadFrameworkFont("Roboto.Roboto-Regular"),
            ["Bold"] = loadFrameworkFont("Roboto.Roboto-Bold"),
        };

        // Characters Roboto already has never reach a fallback (the framework asks the requested family first).
        var robotoChars = new HashSet<int>(roboto["Regular"].Characters.Keys.Intersect(roboto["Bold"].Characters.Keys));

        foreach (string weight in new[] { "Regular", "Bold" })
        {
            var reference = roboto[weight];
            int capHeight = reference.Characters['H'].Height;
            int baseLine = reference.Common.Base;

            // Noto Sans: scaled so its cap height equals Roboto's, then everything else follows from that em.
            var notoSans = loadFont(System.IO.Path.Combine(cacheDir, $"NotoSans-{weight}.ttf"));
            float em = 100 * capHeight / measureCapHeight(notoSans, 100);

            var latinChars = candidates(latinRanges()).Where(c => !robotoChars.Contains(c) && hasGlyph(notoSans, c)).ToList();
            var latin = generate($"NotoSans-{weight}", notoSans, em, 1f, baseLine, reference, latinChars,
                System.IO.Path.Combine(outDir, "NotoSans"), System.IO.Path.Combine(cacheDir, "NotoSans-OFL.txt"));

            // Noto Sans JP: same em as Noto Sans (ideographs are designed against the em, not the cap height).
            var notoJp = loadFont(System.IO.Path.Combine(cacheDir, $"NotoSansJP-{weight}.otf"));
            var cjkChars = candidates(cjkRanges()).Where(c => !robotoChars.Contains(c) && !latin.Contains(c) && hasGlyph(notoJp, c)).ToList();
            generate($"NotoSansJP-{weight}", notoJp, em, cjkResolution, baseLine, reference, cjkChars,
                System.IO.Path.Combine(outDir, "NotoSansJP"), System.IO.Path.Combine(cacheDir, "NotoSansJP-OFL.txt"));
        }

        return 0;
    }

    #region Character sets

    private static IEnumerable<(int From, int To)> latinRanges() => new[]
    {
        (0x00A0, 0x02FF), // Latin-1 supplement, Latin Extended-A/B, IPA, spacing modifiers
        (0x0370, 0x03FF), // Greek and Coptic
        (0x0400, 0x052F), // Cyrillic (+ supplement)
        (0x1E00, 0x1FFF), // Latin Extended Additional (Vietnamese), Greek Extended
        (0x2000, 0x20CF), // general punctuation, super/subscripts, currency
        (0x2100, 0x218F), // letterlike symbols, number forms
    };

    private static IEnumerable<(int From, int To)> cjkRanges()
    {
        // Everything Shift_JIS (cp932) can encode: JIS X 0208 level 1+2 kanji, kana, symbols, plus the NEC/IBM
        // extensions. This is the exact repertoire a song title can have once ksh2vox wrote it into the game's
        // cp932 music_db, so it is the natural subset for an SDVX tool.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp932 = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var bytes = new byte[2];

        for (int lead = 0x81; lead <= 0xFC; lead++)
        {
            if (lead is >= 0xA0 and <= 0xDF) continue;

            for (int trail = 0x40; trail <= 0xFC; trail++)
            {
                if (trail == 0x7F) continue;

                bytes[0] = (byte)lead;
                bytes[1] = (byte)trail;
                string s;

                try { s = cp932.GetString(bytes); }
                catch (DecoderFallbackException) { continue; }

                if (s.Length == 1)
                    yield return (s[0], s[0]);
            }
        }

        yield return (0xFF61, 0xFF9F); // halfwidth katakana (cp932 single bytes 0xA1-0xDF)
        yield return (0x2010, 0x2BFF); // punctuation, arrows, maths, box drawing, shapes, misc symbols (only what the font has)
        yield return (0x3000, 0x30FF); // CJK symbols, hiragana, katakana (complete blocks, not only the JIS subset)
        yield return (0x31F0, 0x31FF); // katakana phonetic extensions
        yield return (0x3200, 0x33FF); // enclosed CJK, CJK compatibility (㈱, ㌔, ...)
        yield return (0xFF00, 0xFFEF); // full- and halfwidth forms (～ U+FF5E, ！, ？)
    }

    private static IEnumerable<int> candidates(IEnumerable<(int From, int To)> ranges)
    {
        var set = new SortedSet<int>();

        foreach (var (from, to) in ranges)
        {
            for (int c = from; c <= to; c++)
            {
                switch (CharUnicodeInfo.GetUnicodeCategory(c))
                {
                    // Combining marks need shaping the framework does not do; controls/format/private use draw nothing useful.
                    case UnicodeCategory.NonSpacingMark:
                    case UnicodeCategory.EnclosingMark:
                    case UnicodeCategory.SpacingCombiningMark:
                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
                    case UnicodeCategory.Surrogate:
                    case UnicodeCategory.PrivateUse:
                    case UnicodeCategory.OtherNotAssigned:
                    case UnicodeCategory.LineSeparator:
                    case UnicodeCategory.ParagraphSeparator:
                        continue;
                }

                set.Add(c);
            }
        }

        return set;
    }

    #endregion

    #region Generation

    private sealed class Entry
    {
        public int CodePoint;
        public Image<La16>? Image; // null = whitespace
        public int XOffset, YOffset, XAdvance;
        public int Width => Image?.Width ?? 1;
        public int Height => Image?.Height ?? 1;
        public int Page, X, Y;
    }

    /// <returns>The code points written.</returns>
    private static HashSet<int> generate(string asset, Font sourceFont, float em, float resolution, int robotoBase, BitmapFont reference,
                                         List<int> chars, string dir, string licenceSource)
    {
        var font = sourceFont.Family.CreateFont(em * resolution, sourceFont.IsBold ? FontStyle.Bold : FontStyle.Regular);
        var options = new TextOptions(font) { Dpi = 72, KerningMode = KerningMode.None, Origin = Vector2.Zero };

        // Baseline of a single laid-out line, measured on a glyph that sits exactly on it.
        float layoutBaseline = TextBuilder.GenerateGlyphs("H", options).Bounds.Bottom;
        int fontBase = (int)MathF.Round(robotoBase * resolution);

        var glyphs = new Entry[chars.Count];

        Parallel.For(0, chars.Count, i =>
        {
            string text = char.ConvertFromUtf32(chars[i]);
            var paths = TextBuilder.GenerateGlyphs(text, options);
            int advance = (int)MathF.Round(TextMeasurer.MeasureAdvance(text, options).Width);
            var g = new Entry { CodePoint = chars[i], XAdvance = advance };

            var b = paths.Bounds;

            if (paths.Count() == 0 || b.Width <= 0 || b.Height <= 0)
            {
                g.YOffset = fontBase; // invisible 1x1, like BMFont's space
                glyphs[i] = g;
                return;
            }

            int left = (int)MathF.Floor(b.Left) - glyph_padding;
            int top = (int)MathF.Floor(b.Top) - glyph_padding;
            int width = (int)MathF.Ceiling(b.Right) + glyph_padding - left;
            int height = (int)MathF.Ceiling(b.Bottom) + glyph_padding - top;

            using var rgba = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255, 0));
            var translated = paths.Translate(-left, -top);
            rgba.Mutate(ctx => ctx.Fill(new DrawingOptions { GraphicsOptions = new GraphicsOptions { Antialias = true } }, Color.White, translated));

            var image = new Image<La16>(width, height);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image[x, y] = new La16(255, rgba[x, y].A);

            g.Image = image;
            g.XOffset = left;
            g.YOffset = fontBase + (int)MathF.Round(top - layoutBaseline);
            glyphs[i] = g;
        });

        int pageCount = pack(glyphs);

        Directory.CreateDirectory(dir);
        foreach (string stale in Directory.GetFiles(dir, $"{asset}*"))
            File.Delete(stale);

        int digits = (pageCount - 1).ToString(CultureInfo.InvariantCulture).Length;
        string pageName(int p) => $"{asset}_{p.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0')}.png";

        var encoder = new PngEncoder { ColorType = PngColorType.GrayscaleWithAlpha, BitDepth = PngBitDepth.Bit8, CompressionLevel = PngCompressionLevel.BestCompression, SkipMetadata = true };
        long bytes = 0;

        for (int p = 0; p < pageCount; p++)
        {
            int usedHeight = glyphs.Where(g => g.Page == p).Max(g => g.Y + g.Height);
            using var page = new Image<La16>(page_size, usedHeight, new La16(255, 0));

            foreach (var g in glyphs.Where(g => g.Page == p && g.Image != null))
                page.Mutate(ctx => ctx.DrawImage(g.Image!, new Point(g.X, g.Y), 1f));

            string path = System.IO.Path.Combine(dir, pageName(p));
            page.Save(path, encoder);
            bytes += new FileInfo(path).Length;
        }

        var bm = new BitmapFont
        {
            Info = new BitmapFontInfo
            {
                Face = asset, Size = (int)MathF.Round(reference.Info.Size * resolution), Smooth = true, Unicode = true, Bold = sourceFont.IsBold,
                StretchHeight = 100, SuperSamplingLevel = 1, SpacingHorizontal = glyph_spacing, SpacingVertical = glyph_spacing,
            },
            Common = new BitmapFontCommon
            {
                LineHeight = (int)MathF.Round(reference.Common.LineHeight * resolution), Base = fontBase, ScaleWidth = page_size, ScaleHeight = page_size,
                AlphaChannel = ChannelData.Glyph, RedChannel = ChannelData.One, GreenChannel = ChannelData.One, BlueChannel = ChannelData.One,
            },
            Pages = Enumerable.Range(0, pageCount).ToDictionary(p => p, pageName),
            Characters = new SortedDictionary<int, BmCharacter>(),
            KerningPairs = new Dictionary<KerningPair, int>(),
        };

        foreach (var g in glyphs)
        {
            bm.Characters[g.CodePoint] = new BmCharacter
            {
                X = g.X, Y = g.Y, Width = g.Width, Height = g.Height, XOffset = g.XOffset, YOffset = g.YOffset, XAdvance = g.XAdvance,
                Page = g.Page, Channel = Channel.All,
            };
            g.Image?.Dispose();
        }

        string binPath = System.IO.Path.Combine(dir, $"{asset}.bin");
        bm.Save(binPath, FormatHint.Binary);
        bytes += new FileInfo(binPath).Length;

        File.Copy(licenceSource, System.IO.Path.Combine(dir, "OFL.txt"), true);

        Console.WriteLine($"{asset}: {glyphs.Length} glyphs, em {em * resolution:0.##} px, {pageCount} page(s), {bytes / 1024.0 / 1024.0:0.00} MiB");
        return new HashSet<int>(chars);
    }

    /// <summary>Shelf packing, tallest first; deterministic (ties broken by code point).</summary>
    private static int pack(Entry[] glyphs)
    {
        int page = 0, x = 0, y = 0, shelf = 0;

        foreach (var g in glyphs.OrderByDescending(g => g.Height).ThenBy(g => g.CodePoint))
        {
            if (x + g.Width > page_size)
            {
                x = 0;
                y += shelf + glyph_spacing;
                shelf = 0;
            }

            if (y + g.Height > page_size)
            {
                page++;
                x = y = shelf = 0;
            }

            g.Page = page;
            g.X = x;
            g.Y = y;
            x += g.Width + glyph_spacing;
            shelf = Math.Max(shelf, g.Height);
        }

        return page + 1;
    }

    private static bool hasGlyph(Font font, int codePoint)
        => font.TryGetGlyphs(new CodePoint(codePoint), out var found) && found.Count > 0 && found[0].GlyphMetrics.GlyphId != 0;

    private static float measureCapHeight(Font font, float size)
    {
        var f = font.Family.CreateFont(size, font.IsBold ? FontStyle.Bold : FontStyle.Regular);
        return TextBuilder.GenerateGlyphs("H", new TextOptions(f) { Dpi = 72 }).Bounds.Height;
    }

    #endregion

    #region Plumbing

    private static Font loadFont(string path)
    {
        var collection = new FontCollection();
        var family = collection.Add(path, out var description);
        return family.CreateFont(100, description.Style);
    }

    private static async Task fetch(Source source, string cacheDir)
    {
        string path = System.IO.Path.Combine(cacheDir, source.FileName);

        if (!File.Exists(path) || sha256(path) != source.Sha256)
        {
            Console.WriteLine($"downloading {source.Url}");
            using var http = new HttpClient();
            byte[] data = await http.GetByteArrayAsync(source.Url).ConfigureAwait(false);
            await File.WriteAllBytesAsync(path, data).ConfigureAwait(false);
        }

        string actual = sha256(path);
        if (actual != source.Sha256)
            throw new InvalidDataException($"{source.FileName}: SHA-256 {actual} does not match the pinned {source.Sha256}");
    }

    private static string sha256(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static string findRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(d.FullName, "SDVXEditor.sln")))
                return d.FullName;
        }

        throw new DirectoryNotFoundException("SDVXEditor.sln not found above the tool; pass --out and --cache.");
    }

    private static string frameworkDll => typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "OsuFrameworkDll").Value!;

    private static BitmapFont loadFrameworkFont(string name)
    {
        var dll = Assembly.LoadFrom(frameworkDll);
        using var s = dll.GetManifestResourceStream($"osu.Framework.Resources.Fonts.{name}.bin") ?? throw new FileNotFoundException(name);
        return BitmapFont.FromStream(s, FormatHint.Binary, false);
    }

    private static int inspect(string[] names)
    {
        foreach (string name in names)
        {
            var f = name.EndsWith(".bin", StringComparison.Ordinal) ? BitmapFont.FromFile(name, FormatHint.Binary) : loadFrameworkFont(name);
            Console.WriteLine($"{name}: size={f.Info.Size} lineHeight={f.Common.LineHeight} base={f.Common.Base} pages={f.Pages.Count} {f.Common.ScaleWidth}x{f.Common.ScaleHeight} chars={f.Characters.Count}");

            foreach (char c in " HxgÅΛμあ漢～")
            {
                if (!f.Characters.TryGetValue(c, out var ch)) { Console.WriteLine($"  '{c}' missing"); continue; }

                Console.WriteLine($"  '{c}' w={ch.Width} h={ch.Height} xo={ch.XOffset} yo={ch.YOffset} adv={ch.XAdvance} page={ch.Page}");
            }
        }

        return 0;
    }

    #endregion
}
