using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SDVXEditor.Core.Patterns;

/// <summary>
/// Readable, diff-friendly JSON for model files: objects are indented, but any value that contains no object
/// nested more than one level deep (number arrays, a tree's arrays, a linear term) is written on one line.
/// </summary>
public static class ModelJson
{
    public static string Format(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(sb, node, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    public static string Serialize<T>(T value, JsonSerializerOptions options)
        => Format(JsonSerializer.SerializeToNode(value, options));

    private static int Depth(JsonNode? n) => n switch
    {
        JsonObject o => 1 + (o.Count == 0 ? 0 : o.Max(kv => Depth(kv.Value))),
        JsonArray a => a.Count == 0 ? 0 : a.Max(Depth) + (a.Any(e => e is JsonObject or JsonArray) ? 1 : 0),
        _ => 0,
    };

    private static void Write(StringBuilder sb, JsonNode? node, int indent)
    {
        if (node is null)
        {
            sb.Append("null");
            return;
        }
        if (Depth(node) <= 1)
        {
            sb.Append(node.ToJsonString(Inline));
            return;
        }
        string pad = new(' ', 2 * (indent + 1)), end = new(' ', 2 * indent);
        if (node is JsonObject obj)
        {
            sb.Append("{\n");
            int i = 0;
            foreach (var (key, value) in obj)
            {
                sb.Append(pad).Append(JsonSerializer.Serialize(key)).Append(": ");
                Write(sb, value, indent + 1);
                sb.Append(++i < obj.Count ? ",\n" : "\n");
            }
            sb.Append(end).Append('}');
        }
        else if (node is JsonArray arr)
        {
            sb.Append("[\n");
            for (int i = 0; i < arr.Count; i++)
            {
                sb.Append(pad);
                Write(sb, arr[i], indent + 1);
                sb.Append(i + 1 < arr.Count ? ",\n" : "\n");
            }
            sb.Append(end).Append(']');
        }
        else sb.Append(node.ToJsonString(Inline));
    }

    private static readonly JsonSerializerOptions Inline = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
