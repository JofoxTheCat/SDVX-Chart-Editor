using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Tutorial;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Resources;

namespace SDVXEditor.Game.Tutorial;

/// <summary>
/// One tutorial: its song (embedded template), chapters, budget, the ONE song folder it works in and where its ticks
/// are stored. Two exist while Johannes collects feedback on the first one: <see cref="TutorialCourses.Main"/> (Fax's
/// song, the one to keep) and <see cref="TutorialCourses.Classic"/> (the synthesised placeholder song, temporary —
/// docs/TUTORIAL.md lists what to delete with it).
/// </summary>
public sealed class TutorialCourse
{
    public required string Id { get; init; }

    /// <summary>Shown on the course tab of the chapter list ("Tutorial (new, [REDACTED] by Fax)").</summary>
    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>The one song folder in the editor songs folder ("Tutorial", "Tutorial (classic)").</summary>
    public required string FolderName { get; init; }

    /// <summary>Embedded resource prefix of the template (audio, tutorial.json, solution.kson).</summary>
    public required string ResourcePrefix { get; init; }

    public required EditorSetting ProgressSetting { get; init; }

    public required IReadOnlyList<TutorialChapter> Chapters { get; init; }

    /// <summary>The budget (Core <see cref="TutorialPlan"/> / <see cref="MainTutorialPlan"/>): the smoke test holds the chapters to it.</summary>
    public required IReadOnlyList<(string Title, int Steps, double Minutes)> Plan { get; init; }

    /// <summary>Kept only until it is removed (the chapter list says so).</summary>
    public bool Temporary { get; init; }

    /// <summary>Parses the song definition from the template (<paramref name="text"/> reads a template file, null when missing).</summary>
    public required Func<Func<string, string?>, ITutorialSong> LoadSong { get; init; }

    /// <summary>The chart files (name, chart) chapter N starts with; empty for chapter 0 (it starts from the bare audio).</summary>
    public required Func<ITutorialSong, Func<string, string?>, int, IReadOnlyList<(string File, Chart Chart)>> ChartsFor { get; init; }

    public int Count => Chapters.Count;

    public TutorialChapter Get(int number) => Chapters[Math.Clamp(number, 0, Chapters.Count - 1)];

    /// <summary>A template file as text (null when the build has none).</summary>
    internal string? ReadText(string name)
    {
        using var s = OpenResource(name);
        if (s == null) return null;
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }

    internal Stream? OpenResource(string name) => SDVXEditorResources.ResourceAssembly.GetManifestResourceStream(ResourcePrefix + name);

    public override string ToString() => Id;
}

/// <summary>The tutorials the editor offers, the new one first.</summary>
public static class TutorialCourses
{
    public static TutorialCourse Main { get; } = new TutorialCourse
    {
        Id = "main",
        Name = "Tutorial (new, [REDACTED] by Fax)",
        Description = "Map Fax's \"[REDACTED] (Cut Ver.)\" step by step — the whole editor in about 12 minutes.",
        FolderName = "Tutorial",
        ResourcePrefix = "SDVXEditor.Resources.Tutorial.Main.",
        ProgressSetting = EditorSetting.TutorialMainProgress,
        Chapters = TutorialChapters.Main,
        Plan = MainTutorialPlan.Chapters,
        LoadSong = text => text("tutorial.json") is { } json ? MainTutorialSong.Parse(json) : new MainTutorialSong(),
        ChartsFor = (song, text, chapter) =>
        {
            if (chapter == 0) return Array.Empty<(string, Chart)>();
            var def = (MainTutorialSong)song;
            var solution = MainTutorialCharts.Solution(def, text("solution.kson"));
            var list = new List<(string, Chart)> { ("exh.kson", MainTutorialCharts.ForChapter(def, solution, chapter)) };
            if (chapter >= 3) list.Add(("adv.kson", MainTutorialCharts.Reference(def, solution)));
            return list;
        },
    };

    /// <summary>The first tutorial (synthesised placeholder song). TEMPORARY: kept for Johannes' feedback and the mapper's first run.</summary>
    public static TutorialCourse Classic { get; } = new TutorialCourse
    {
        Id = "classic",
        Name = "Tutorial (classic)",
        Description = "The first tutorial on the short placeholder song (about 10 minutes). Temporary — it will be removed.",
        FolderName = "Tutorial (classic)",
        ResourcePrefix = "SDVXEditor.Resources.Tutorial.",
        ProgressSetting = EditorSetting.TutorialProgress,
        Chapters = TutorialChapters.Classic,
        Plan = TutorialPlan.Chapters,
        Temporary = true,
        LoadSong = text => text("tutorial.json") is { } json ? TutorialSongDefinition.Parse(json) : new TutorialSongDefinition(),
        ChartsFor = (song, _, chapter) =>
        {
            if (chapter == 0) return Array.Empty<(string, Chart)>();
            var def = (TutorialSongDefinition)song;
            var list = new List<(string, Chart)> { ("exh.kson", TutorialCharts.ForChapter(def, chapter)) };
            if (chapter >= 3) list.Add(("adv.kson", TutorialCharts.Reference(def)));
            return list;
        },
    };

    public static IReadOnlyList<TutorialCourse> All { get; } = new[] { Main, Classic };

    public static TutorialCourse? ById(string? id) => All.FirstOrDefault(c => c.Id == id);
}
