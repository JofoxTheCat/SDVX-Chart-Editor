using System.Text.Json;
using System.Text.Json.Nodes;

namespace SDVXEditor.Core.Formats.Kson;

/// <summary>Small helpers for reading loosely typed kson nodes.</summary>
internal static class KsonJson
{
    public static double Num(JsonNode? n, double fallback = 0)
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue(out double d)) return d;
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out string? s) &&
            double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
            return d;
        if (v.TryGetValue(out bool b)) return b ? 1 : 0;
        if (v.TryGetValue(out JsonElement e) && e.ValueKind == JsonValueKind.Number) return e.GetDouble();
        return fallback;
    }

    public static long Int(JsonNode? n, long fallback = 0)
    {
        double d = Num(n, double.NaN);
        return double.IsNaN(d) ? fallback : (long)Math.Round(d);
    }

    public static string Str(JsonNode? n, string fallback = "")
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue(out string? s)) return s ?? fallback;
        return v.ToJsonString();
    }

    public static bool Bool(JsonNode? n, bool fallback = false)
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue(out bool b)) return b;
        return Num(n, fallback ? 1 : 0) != 0;
    }

    public static bool IsNumber(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.Number;
    public static bool IsString(JsonNode? n) => n is JsonValue v && v.GetValueKind() == JsonValueKind.String;

    public static JsonNode? At(JsonArray a, int i) => i < a.Count ? a[i] : null;

    /// <summary>Formats a double as an integer node when whole (kson writes uint levels, pulses etc.).</summary>
    public static JsonNode NumNode(double d)
        => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? JsonValue.Create((long)d) : JsonValue.Create(d);

    /// <summary>Deep merge of <paramref name="src"/> into <paramref name="dst"/>; existing values in dst win.</summary>
    public static void MergeMissing(JsonObject dst, JsonObject src)
    {
        foreach (var (key, value) in src)
        {
            if (!dst.ContainsKey(key))
                dst[key] = value?.DeepClone();
            else if (dst[key] is JsonObject d && value is JsonObject s)
                MergeMissing(d, s);
        }
    }

    /// <summary>Gets or creates the object at <paramref name="path"/> below <paramref name="root"/>.</summary>
    public static JsonObject Path(JsonObject root, IEnumerable<string> path)
    {
        var cur = root;
        foreach (string p in path)
        {
            if (cur[p] is not JsonObject next)
            {
                next = new JsonObject();
                cur[p] = next;
            }
            cur = next;
        }
        return cur;
    }
}
