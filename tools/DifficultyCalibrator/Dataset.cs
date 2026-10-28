using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Formats.Vox;
using SDVXEditor.Core.LevelEstimation;
using SDVXEditor.Core.Patterns;

namespace SDVXEditor.Tools.DifficultyCalibrator;

/// <summary>One official chart with its labels. Nothing of it is written anywhere; only aggregates leave the tool.</summary>
public sealed record Sample(
    int SongId,
    int Slot,
    double Level,
    int VoxVersion,
    int[]? Radar,                          // notes, peak, tsumami, tricky, hand-trip, one-hand (null = none in the db)
    Dictionary<string, double> Inputs,     // DifficultyFeatures (+ slot_1..4)
    Dictionary<string, double> Patterns,   // PatternAnalysis, default hand model
    Chart Chart);

public static class Dataset
{
    public static readonly string[] SlotTags = { "novice", "advanced", "exhaust", "infinite", "maximum", "ultimate" };
    public static readonly string[] RadarTags = { "notes", "peak", "tsumami", "tricky", "hand-trip", "one-hand" };

    public static string? FindDataDir(string root)
    {
        foreach (string c in new[] { root, Path.Combine(root, "data"), Path.Combine(root, "contents", "data") })
            if (Directory.Exists(Path.Combine(c, "music")) && File.Exists(Path.Combine(c, "others", "music_db.xml")))
                return c;
        return null;
    }

    /// <summary>song id → slot tag → (difnum, radar or null). Only ASCII tags are read, so Latin-1 decoding is enough.</summary>
    public static Dictionary<int, Dictionary<string, (int Difnum, int[]? Radar)>> LoadMusicDb(string path)
    {
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        var db = new Dictionary<int, Dictionary<string, (int, int[]?)>>();
        foreach (Match m in Regex.Matches(text, "<music id=\"(\\d+)\">(.*?)</music>", RegexOptions.Singleline))
        {
            int id = int.Parse(m.Groups[1].Value);
            var levels = new Dictionary<string, (int, int[]?)>();
            foreach (string tag in SlotTags)
            {
                var d = Regex.Match(m.Groups[2].Value, $"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline);
                if (!d.Success) continue;
                var dn = Regex.Match(d.Groups[1].Value, "<difnum[^>]*>(\\d+)<");
                if (!dn.Success) continue;
                int[]? radar = null;
                var r = Regex.Match(d.Groups[1].Value, "<radar>(.*?)</radar>", RegexOptions.Singleline);
                if (r.Success)
                {
                    radar = new int[6];
                    for (int a = 0; a < 6; a++)
                    {
                        string alt = RadarTags[a].Replace('-', '_');
                        var v = Regex.Match(r.Groups[1].Value, $"<(?:{Regex.Escape(RadarTags[a])}|{alt})[^>]*>(\\d+)<");
                        radar[a] = v.Success ? int.Parse(v.Groups[1].Value) : 0;
                    }
                }
                levels[tag] = (int.Parse(dn.Groups[1].Value), radar);
            }
            db[id] = levels;
        }
        return db;
    }

    public sealed record LoadResult(List<Sample> Samples, int Files, int Parsed, List<(string File, string Error)> Failures, int Empty, int Unpaired);

    public static LoadResult Load(string dataDir)
    {
        var db = LoadMusicDb(Path.Combine(dataDir, "others", "music_db.xml"));
        var files = Directory.EnumerateDirectories(Path.Combine(dataDir, "music"))
                             .SelectMany(d => Directory.EnumerateFiles(d, "*.vox"))
                             .OrderBy(f => f, StringComparer.Ordinal).ToList();
        var samples = new ConcurrentBag<Sample>();
        var failures = new ConcurrentBag<(string, string)>();
        int parsed = 0, empty = 0, unpaired = 0;
        Parallel.ForEach(files, file =>
        {
            Chart chart;
            ChartFeatureSet set;
            PatternFeatureSet patterns;
            int version;
            try
            {
                string text = Encoding.Latin1.GetString(File.ReadAllBytes(file));
                var vm = Regex.Match(text, "#FORMAT VERSION\\s*\\r?\\n\\s*(\\d+)");
                version = vm.Success ? int.Parse(vm.Groups[1].Value) : 0;
                chart = VoxReader.Read(text);
                set = DifficultyFeatures.Extract(chart);
                patterns = PatternAnalysis.Analyze(chart);
            }
            catch (Exception e)
            {
                failures.Add((Path.GetFileName(file), e.GetType().Name + ": " + e.Message));
                return;
            }
            Interlocked.Increment(ref parsed);
            if (set.IsEmpty || patterns.IsEmpty) { Interlocked.Increment(ref empty); return; }

            string folder = Path.GetFileName(Path.GetDirectoryName(file)!);
            string stem = Path.GetFileNameWithoutExtension(file);
            int us = folder.IndexOf('_');
            if (us <= 0 || !int.TryParse(folder[..us], out int songId) || stem.Length < 3) { Interlocked.Increment(ref unpaired); return; }
            int tagIndex = stem[^2..] switch { "1n" => 0, "2a" => 1, "3e" => 2, "4i" => 3, "5m" => 4, "6u" => 5, _ => -1 };
            if (tagIndex < 0 || !db.TryGetValue(songId, out var levels) || !levels.TryGetValue(SlotTags[tagIndex], out var entry) || entry.Difnum <= 0)
            {
                Interlocked.Increment(ref unpaired);
                return;
            }
            int slot = Math.Min(tagIndex, 4);
            chart.Meta.DifficultySlot = slot;
            var inputs = set.ToDictionary();
            for (int s = 1; s <= 4; s++) inputs["slot_" + s] = slot == s ? 1 : 0;
            // ultimate shares the MXM slot in the db radar convention; keep its radar only if it is its own entry
            samples.Add(new Sample(songId, slot, entry.Difnum / 10.0, version, entry.Radar, inputs, patterns.ToDictionary(), chart));
        });
        var data = samples.OrderBy(s => s.SongId).ThenBy(s => s.Slot).ToList();
        return new LoadResult(data, files.Count, parsed, failures.ToList(), empty, unpaired);
    }

    public static Dictionary<int, int> FoldAssignment(IEnumerable<int> songIds, int folds, int seed)
    {
        var ids = songIds.Distinct().OrderBy(i => i).ToList();
        var rng = new Random(seed);
        for (int i = ids.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }
        var fold = new Dictionary<int, int>();
        for (int i = 0; i < ids.Count; i++) fold[ids[i]] = i % folds;
        return fold;
    }
}
