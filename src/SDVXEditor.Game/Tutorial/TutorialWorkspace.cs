using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using osu.Framework.Logging;
using SDVXEditor.Core.Formats.Kson;
using SDVXEditor.Core.Tutorial;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// A tutorial's song on disk: ONE folder per tutorial in the editor songs folder ("Tutorial", "Tutorial (classic)").
/// Chapter 0 creates it (the user drops the audio); every later chapter rewrites its chart files in place to that
/// chapter's starting state. A marker file (<see cref="MarkerFile"/>) says which tutorial and chapter wrote it and
/// what the charts looked like then, so the tutorial only ever touches a folder it made and can tell whether the user
/// changed it since. The template (audio, tutorial.json, …) is embedded read-only in SDVXEditor.Resources.
/// </summary>
public sealed partial class TutorialWorkspace
{
    /// <summary>In the song folder: which tutorial/chapter wrote it and the hashes of the charts it wrote.</summary>
    public const string MarkerFile = ".sdvxeditor-tutorial.json";

    public const string StagingFolderName = "Tutorial files";

    private readonly string editorRoot;

    public TutorialWorkspace(string editorRoot, TutorialCourse course)
    {
        this.editorRoot = editorRoot;
        Course = course;
        Song = course.LoadSong(course.ReadText);
    }

    public TutorialCourse Course { get; }

    public ITutorialSong Song { get; }

    /// <summary>The classic tutorial's definition (its chapters read BPM, speed-up … from it).</summary>
    public TutorialSongDefinition Definition => (TutorialSongDefinition)Song;

    public string FolderName => Course.FolderName;

    /// <summary>The tutorial's one song folder.</summary>
    public string Folder => Path.Combine(editorRoot, FolderName);

    /// <summary>Where chapter 0 puts the audio for the drag &amp; drop exercise (beside the songs folder, never inside it).</summary>
    public string StagingFolder => StagingFolderFor(editorRoot);

    public static string StagingFolderFor(string editorRoot) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(editorRoot).TrimEnd('\\', '/')) ?? editorRoot, StagingFolderName);

    /// <summary>The audio file chapter 0 asks the user to drop: its name becomes the song's title and folder name.</summary>
    public string StagedAudio => Path.Combine(StagingFolder, FolderName + Path.GetExtension(Song.Audio));

    /// <summary>The embedded audio (null when the build has none).</summary>
    public Stream? OpenAudio() => Course.OpenResource(Song.Audio);

    // ---- the folder's state ------------------------------------------------------------------

    public sealed record Marker(string Course, int Chapter, Dictionary<string, string> Charts);

    /// <summary>What is in the tutorial folder now, and what starting a chapter would throw away.</summary>
    public sealed record FolderState
    {
        public bool Exists { get; init; }

        /// <summary>The folder was made by this tutorial (marker of this course, or recognisably the tutorial's own files).</summary>
        public bool Ours { get; init; }

        public Marker? Marker { get; init; }

        /// <summary>Chart files whose content differs from what the tutorial wrote (the user's saved work).</summary>
        public IReadOnlyList<string> ChangedCharts { get; init; } = Array.Empty<string>();

        /// <summary>Chart files the tutorial did not write (a difficulty the user added).</summary>
        public IReadOnlyList<string> ExtraCharts { get; init; } = Array.Empty<string>();

        /// <summary>Other files the user put there (jacket, …): never deleted, kept across chapters.</summary>
        public IReadOnlyList<string> OtherFiles { get; init; } = Array.Empty<string>();

        public bool HasUserWork => ChangedCharts.Count > 0 || ExtraCharts.Count > 0;
    }

    private static bool isChart(string file) => Path.GetExtension(file) is ".kson" or ".ksh";

    public FolderState State()
    {
        if (!Directory.Exists(Folder)) return new FolderState();

        var marker = readMarker(Folder);
        var files = Directory.EnumerateFiles(Folder).Select(Path.GetFileName).OfType<string>().Where(f => f != MarkerFile).ToList();
        bool ours = marker != null ? marker.Course == Course.Id : recognised(Folder, files);
        var charts = files.Where(isChart).ToList();
        var known = marker?.Charts ?? new Dictionary<string, string>();

        return new FolderState
        {
            Exists = true,
            Ours = ours,
            Marker = marker,
            ChangedCharts = charts.Where(f => known.TryGetValue(f, out string? h) && h != hash(Path.Combine(Folder, f))).ToList(),
            ExtraCharts = charts.Where(f => !known.ContainsKey(f) && marker?.Chapter != 0).ToList(),
            OtherFiles = files.Where(f => !isChart(f) && !string.Equals(f, Song.Audio, StringComparison.OrdinalIgnoreCase)
                                          && !string.Equals(f, Path.GetFileName(StagedAudio), StringComparison.OrdinalIgnoreCase)).ToList(),
        };
    }

    /// <summary>A folder without a marker that holds nothing but this tutorial's audio (byte-identical) and charts.</summary>
    private bool recognised(string folder, IReadOnlyList<string> files)
    {
        using var audio = OpenAudio();
        if (audio == null) return false;

        bool hasAudio = false;
        foreach (string f in files)
        {
            if (isChart(f)) continue;
            if (!sameContent(Path.Combine(folder, f), audio)) return false;
            hasAudio = true;
        }

        return hasAudio;
    }

    private static bool sameContent(string path, Stream template)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != template.Length) return false;
        template.Position = 0;
        using var f = File.OpenRead(path);
        return SHA256.HashData(f).AsSpan().SequenceEqual(SHA256.HashData(template));
    }

    /// <summary>Runs a file operation, retrying for up to ~2 s while another reader holds the file (IOException).</summary>
    private static void retry(Action op)
    {
        for (int attempt = 0;; attempt++)
        {
            try
            {
                op();
                return;
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(100);
            }
        }
    }

    private static string hash(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f));
    }

    private static Marker? readMarker(string folder)
    {
        string path = Path.Combine(folder, MarkerFile);
        if (!File.Exists(path)) return null;

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path));
            var charts = new Dictionary<string, string>();
            if (node?["charts"] is JsonObject o)
                foreach (var (k, v) in o)
                    if (v is JsonValue jv && jv.TryGetValue(out string? h)) charts[k] = h;
            return new Marker(node?["course"]?.GetValue<string>() ?? "", node?["chapter"]?.GetValue<int>() ?? -1, charts);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Records which chapter the folder holds and the charts as they are now (the user's later saves differ from these).</summary>
    public void WriteMarker(int chapter)
    {
        if (!Directory.Exists(Folder)) return;

        var charts = new JsonObject();
        foreach (string f in Directory.EnumerateFiles(Folder).Where(isChart))
            charts[Path.GetFileName(f)] = hash(f);
        var node = new JsonObject { ["course"] = Course.Id, ["chapter"] = chapter, ["charts"] = charts };
        File.WriteAllText(Path.Combine(Folder, MarkerFile), node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Chapter 0 lets the import create the folder: mark it as ours once it exists.</summary>
    public bool AdoptIfNeeded(int chapter)
    {
        if (!Directory.Exists(Folder) || File.Exists(Path.Combine(Folder, MarkerFile))) return false;
        if (!Directory.EnumerateFiles(Folder).Any(isChart)) return false;
        if (!recognised(Folder, Directory.EnumerateFiles(Folder).Select(Path.GetFileName).OfType<string>().ToList())) return false;

        WriteMarker(chapter);
        return true;
    }

    // ---- preparing a chapter -----------------------------------------------------------------

    /// <summary>
    /// Makes the chapter's starting state. Chapter 0: the folder is removed (the user creates it from the audio) and
    /// the audio is staged. Other chapters: the folder's chart files are rewritten to the chapter's starting charts —
    /// unless the folder already holds this chapter and <paramref name="reset"/> is false (then the user continues).
    /// Files that are not charts (a jacket the user added) are kept. Throws when a folder of that name exists that the
    /// tutorial did not make.
    /// </summary>
    public string Prepare(int chapter, bool reset)
    {
        var state = State();
        if (state.Exists && !state.Ours)
            throw new InvalidOperationException($"A song folder \"{FolderName}\" exists that the tutorial did not create. Rename or move it (song select → right-click → Show folder) and start again.");

        if (chapter == 0)
        {
            // the import itself creates the folder; an old one would make the drop land in "... (2)"
            if (state.Exists) deleteOwnFolder();
            Directory.CreateDirectory(StagingFolder);
            writeAudio(StagedAudio);
            return Folder;
        }

        if (!reset && state.Marker?.Chapter == chapter && Directory.EnumerateFiles(Folder, "*.kson").Any())
            return Folder;

        Directory.CreateDirectory(Folder);
        writeAudio(Path.Combine(Folder, Song.Audio));
        // song select may still be reading a chart of the finished chapter (library refresh, skill radar): retry briefly
        foreach (string f in Directory.EnumerateFiles(Folder).Where(isChart)) retry(() => File.Delete(f));
        foreach (var (file, chart) in Course.ChartsFor(Song, Course.ReadText, chapter))
            retry(() => KsonWriter.WriteFile(chart, Path.Combine(Folder, file)));
        // chapter 0 named the audio after the folder; the chapters' charts use the template's name
        string staged = Path.Combine(Folder, Path.GetFileName(StagedAudio));
        if (!string.Equals(Path.GetFileName(StagedAudio), Song.Audio, StringComparison.OrdinalIgnoreCase) && File.Exists(staged)) File.Delete(staged);
        WriteMarker(chapter);
        return Folder;
    }

    /// <summary>Removes this tutorial's folder (only if it is ours) and its staged audio.</summary>
    public void DeleteAll()
    {
        if (State() is { Exists: true, Ours: true }) deleteOwnFolder();
        if (File.Exists(StagedAudio)) File.Delete(StagedAudio);
        if (Directory.Exists(StagingFolder) && !Directory.EnumerateFileSystemEntries(StagingFolder).Any()) Directory.Delete(StagingFolder);
    }

    private void deleteOwnFolder()
    {
        // only ever this tutorial's folder: State().Ours was checked by the callers, the name is checked here
        if (!string.Equals(Path.GetFileName(Folder.TrimEnd('\\', '/')), FolderName, StringComparison.Ordinal))
            throw new InvalidOperationException("refusing to delete a non-tutorial folder: " + Folder);
        Directory.Delete(Folder, true);
    }

    private void writeAudio(string path)
    {
        using var source = OpenAudio();
        if (source == null)
        {
            // a build without the audio still works: a silent WAV of the song's length (BASS reads the format from the content)
            if (!File.Exists(path)) File.WriteAllBytes(path, silentWav(Song.LengthMs + 2000));
            return;
        }

        if (File.Exists(path) && sameContent(path, source)) return;

        source.Position = 0;
        using var target = File.Create(path);
        source.CopyTo(target);
    }

    // ---- migration from the per-chapter folders ------------------------------------------------

    [GeneratedRegex(@"^Tutorial \(Chapter [0-9]+\)$")]
    private static partial Regex legacyFolderName();

    /// <summary>
    /// Up to 2026-10-27 every chapter had its own folder "Tutorial (Chapter N)" and chapter 0 staged
    /// "Tutorial (Chapter 0).ogg". Removes those — only a folder with that exact name that holds nothing but the classic
    /// tutorial's audio (byte-identical) and .kson charts, and the staged file only when it is that audio. Anything
    /// else is left alone and logged. Returns the number of folders removed.
    /// </summary>
    public static int MigrateLegacy(string editorRoot)
    {
        var classic = new TutorialWorkspace(editorRoot, TutorialCourses.Classic);
        int removed = 0;

        if (Directory.Exists(editorRoot))
        {
            foreach (string dir in Directory.EnumerateDirectories(editorRoot))
            {
                string name = Path.GetFileName(dir);
                if (!legacyFolderName().IsMatch(name)) continue;

                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                bool onlyOurs = Directory.EnumerateDirectories(dir).All(d => Path.GetFileName(d).StartsWith('.'))
                                && files.All(f => Path.GetExtension(f) == ".kson" || Path.GetFileName(f) == MarkerFile || classic.isAudio(f))
                                && files.Any(classic.isAudio);
                if (!onlyOurs)
                {
                    Logger.Log($"tutorial: kept \"{name}\": it holds files the tutorial did not write");
                    continue;
                }

                Directory.Delete(dir, true);
                removed++;
                Logger.Log($"tutorial: removed the old per-chapter folder \"{name}\"");
            }
        }

        string staged = Path.Combine(StagingFolderFor(editorRoot), "Tutorial (Chapter 0).ogg");
        if (File.Exists(staged) && classic.isAudio(staged))
        {
            File.Delete(staged);
            Logger.Log("tutorial: removed the old staged \"Tutorial (Chapter 0).ogg\"");
        }

        return removed;
    }

    private bool isAudio(string path)
    {
        using var audio = OpenAudio();
        return audio != null && sameContent(path, audio);
    }

    private static byte[] silentWav(double ms)
    {
        int rate = 22050;
        int samples = (int)(rate * ms / 1000);
        using var mem = new MemoryStream();
        using var w = new BinaryWriter(mem);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + samples * 2);
        w.Write("WAVEfmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(samples * 2);
        w.Write(new byte[samples * 2]);
        w.Flush();
        return mem.ToArray();
    }
}
