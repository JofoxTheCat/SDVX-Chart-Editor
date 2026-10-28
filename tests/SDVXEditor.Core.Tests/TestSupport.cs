using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Tests;

/// <summary>Model comparison independent of the format writers: serialise the object graph, sort keys, diff.</summary>
public static class ModelSnapshot
{
    private static readonly JsonSerializerOptions options = new() { WriteIndented = false };

    public static JsonNode Of(object model) => Canonical(JsonSerializer.SerializeToNode(model, model.GetType(), options));

    private static JsonNode Canonical(JsonNode? n)
    {
        switch (n)
        {
            case JsonObject o:
                var sorted = new JsonObject();
                foreach (var kv in o.OrderBy(kv => kv.Key, StringComparer.Ordinal)) sorted[kv.Key] = Canonical(kv.Value);
                return sorted;
            case JsonArray a:
                return new JsonArray(a.Select(Canonical).ToArray());
            case null:
                return JsonValue.Create("<null>")!;
            default:
                return n.DeepClone();
        }
    }

    /// <summary>First differing path between two snapshots, or null if equal.</summary>
    public static string? FirstDifference(JsonNode a, JsonNode b, string path = "$")
    {
        if (a is JsonObject oa && b is JsonObject ob)
        {
            foreach (var key in oa.Select(k => k.Key).Union(ob.Select(k => k.Key)))
            {
                if (!oa.ContainsKey(key) || !ob.ContainsKey(key)) return $"{path}.{key} missing on one side";
                var d = FirstDifference(oa[key]!, ob[key]!, $"{path}.{key}");
                if (d != null) return d;
            }
            return null;
        }
        if (a is JsonArray aa && b is JsonArray ab)
        {
            for (int i = 0; i < Math.Min(aa.Count, ab.Count); i++)
            {
                var d = FirstDifference(aa[i]!, ab[i]!, $"{path}[{i}]");
                if (d != null) return d;
            }
            return aa.Count == ab.Count ? null : $"{path}: count {aa.Count} vs {ab.Count}";
        }
        return JsonNode.DeepEquals(a, b) ? null : $"{path}: {a.ToJsonString()} vs {b.ToJsonString()}";
    }

    public static void AssertEqual(object expected, object actual, string what)
    {
        var diff = FirstDifference(Of(expected), Of(actual));
        Assert.That(diff, Is.Null, $"{what}: {diff}");
    }
}

public static class RealCharts
{
    public const string Root = @"D:\SDVX-DEV\Nautica Charts";

    /// <summary>Skips the calling test on machines without the local chart library (fresh clone, CI).</summary>
    public static string RequireRoot()
    {
        if (!Directory.Exists(Root)) Assert.Ignore($"local chart library not found: {Root}");
        return Root;
    }

    public static IEnumerable<string> Files(string ext)
        => Directory.Exists(Root)
            ? Directory.EnumerateFiles(Root, "*" + ext, SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

    /// <summary>Test case source: every chart file of an extension, or a single placeholder that ignores itself.</summary>
    public static IEnumerable<TestCaseData> Cases(string ext, string test)
    {
        var files = Files(ext).ToList();
        if (files.Count == 0)
        {
            yield return new TestCaseData("").SetName($"{test}({ext}, no local charts)");
            yield break;
        }
        foreach (var f in files)
            yield return new TestCaseData(f).SetName($"{test}({Path.GetRelativePath(Root, f)})");
    }

    public static void RequireFile(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) Assert.Ignore("local chart library not found");
    }
}

public static class ChartFacts
{
    public static string Describe(Chart c)
        => $"bt {string.Join(",", c.Notes.Bt.Select(l => l.Count))} fx {string.Join(",", c.Notes.Fx.Select(l => l.Count))} "
           + string.Join(" ", Enumerable.Range(0, 2).Select(l =>
               $"L{l}: {c.Notes.Lasers[l].Count} sections / {c.Notes.Lasers[l].Sum(s => s.Points.Count)} points / {c.Notes.Lasers[l].Sum(s => s.SlamCount)} slams"));
}
