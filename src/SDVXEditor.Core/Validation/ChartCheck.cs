using SDVXEditor.Core.Charts;
using SDVXEditor.Core.Editing;
using SDVXEditor.Core.Timing;

namespace SDVXEditor.Core.Validation;

// =====================================================================================
// Chart check (osu! AIMod style): independent rules, each turning the chart into a list of
// issues with a position, a severity and a message. The editor shows them as a clickable
// list (jump to the position, select the objects) and runs them after save / export as a
// non-blocking warning. Rules never change the chart.
//
// Adding rules from another file: implement IChartCheck in a public, non-abstract class with
// a public parameterless constructor anywhere in SDVXEditor.Core — ChartChecker finds it by
// reflection, no list to edit. Code outside Core can call ChartChecker.Register.
// =====================================================================================

public enum IssueSeverity
{
    /// <summary>Not wrong, but worth knowing (a format will round something, a convention is not followed).</summary>
    Info,

    /// <summary>Probably unintended or lost somewhere (an export, the game), the chart still loads.</summary>
    Warning,

    /// <summary>Broken: the model is invalid or a reader/the game will misbehave.</summary>
    Error,
}

/// <summary>
/// One finding. <see cref="Pulse"/> null = not tied to a time (metadata). <see cref="Notes"/> /
/// <see cref="LaserPoints"/> name the objects so "jump to" can select them.
/// </summary>
public sealed record ChartIssue(string RuleId, IssueSeverity Severity, string Message, long? Pulse = null)
{
    public IReadOnlyList<NoteRef> Notes { get; init; } = Array.Empty<NoteRef>();
    public IReadOnlyList<LaserPointRef> LaserPoints { get; init; } = Array.Empty<LaserPointRef>();

    /// <summary>Group shown in the list ("Timing", "Notes", "Lasers", "Effects", "Metadata", "Formats"); set by the checker from the rule.</summary>
    public string Category { get; init; } = "";
}

/// <summary>What a rule may look at. Everything but the chart is optional: rules that need it skip when it is missing.</summary>
public sealed class ChartCheckContext
{
    public ChartCheckContext(Chart chart, TimingMap? timing = null)
    {
        Chart = chart;
        Timing = timing ?? new TimingMap(chart.Beat, chart.Audio.Bgm.OffsetMs);
    }

    public Chart Chart { get; }
    public TimingMap Timing { get; }

    /// <summary>Length of the song audio in ms (track time, i.e. without the chart offset), null = unknown.</summary>
    public double? AudioLengthMs { get; init; }

    /// <summary>The song folder (file existence checks), null = scratch chart.</summary>
    public string? SongFolder { get; init; }

    /// <summary>"12:3" (1-based measure:beat) of a pulse, for messages.</summary>
    public string Where(long pulse)
    {
        var p = Timing.PositionOf(Math.Max(0, pulse));
        return $"{p.Measure + 1}:{p.Beat + 1}";
    }

    private HashSet<long>? slamPulses;

    /// <summary>Absolute pulses of every slam on either laser lane.</summary>
    public IReadOnlySet<long> SlamPulses => slamPulses ??= Chart.Notes.Lasers
        .SelectMany(lane => lane.SelectMany(s => s.Points.Where(p => p.IsSlam).Select(p => s.Y + p.Ry)))
        .ToHashSet();
}

/// <summary>
/// A chart-check rule. Implementations in SDVXEditor.Core with a public parameterless constructor are found
/// automatically (<see cref="ChartChecker.Rules"/>); keep <see cref="Id"/> unique and stable (it prefixes the issue
/// ids, e.g. <c>laser.overlap</c>).
/// </summary>
public interface IChartCheck
{
    /// <summary>Stable id prefix ("laser", "effect.vox" …).</summary>
    string Id { get; }

    /// <summary>Group in the list: Timing, Notes, Lasers, Effects, Camera, Metadata, Formats.</summary>
    string Category { get; }

    IEnumerable<ChartIssue> Run(ChartCheckContext context);
}

/// <summary>The result of one run, sorted by position (issues without one first) then severity.</summary>
public sealed class ChartCheckReport
{
    public ChartCheckReport(IReadOnlyList<ChartIssue> issues) => Issues = issues;

    public IReadOnlyList<ChartIssue> Issues { get; }
    public int Errors => Issues.Count(i => i.Severity == IssueSeverity.Error);
    public int Warnings => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    public int Infos => Issues.Count(i => i.Severity == IssueSeverity.Info);
    public bool IsClean => Issues.Count == 0;

    /// <summary>"2 errors, 5 warnings" (infos only when nothing worse), "no problems".</summary>
    public string Summary
    {
        get
        {
            if (Issues.Count == 0) return "no problems found";
            var parts = new List<string>();
            if (Errors > 0) parts.Add(Errors == 1 ? "1 error" : $"{Errors} errors");
            if (Warnings > 0) parts.Add(Warnings == 1 ? "1 warning" : $"{Warnings} warnings");
            if (Infos > 0) parts.Add(Infos == 1 ? "1 note" : $"{Infos} notes");
            return string.Join(", ", parts);
        }
    }
}

public static class ChartChecker
{
    private static readonly object gate = new();
    private static readonly List<IChartCheck> registered = new();
    private static List<IChartCheck>? discovered;

    /// <summary>
    /// Every rule in effect: the ones in SDVXEditor.Core (found by reflection: public, non-abstract, parameterless
    /// constructor, implements <see cref="IChartCheck"/>), then registered ones, ordered by id.
    /// </summary>
    public static IReadOnlyList<IChartCheck> Rules
    {
        get
        {
            lock (gate)
            {
                discovered ??= typeof(IChartCheck).Assembly.GetTypes()
                    .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(IChartCheck).IsAssignableFrom(t)
                                && t.GetConstructor(Type.EmptyTypes) != null)
                    .Select(t => (IChartCheck)Activator.CreateInstance(t)!)
                    .ToList();
                return discovered.Concat(registered).GroupBy(r => r.Id).Select(g => g.First()).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            }
        }
    }

    /// <summary>Adds a rule from outside Core (an id already present is ignored).</summary>
    public static void Register(IChartCheck rule)
    {
        lock (gate)
        {
            if (!registered.Any(r => r.Id == rule.Id)) registered.Add(rule);
        }
    }

    /// <summary>Runs every rule. A rule that throws becomes one error issue instead of breaking the run.</summary>
    public static ChartCheckReport Run(ChartCheckContext context, IEnumerable<IChartCheck>? rules = null)
    {
        var issues = new List<ChartIssue>();
        foreach (var rule in rules ?? Rules)
        {
            try
            {
                foreach (var issue in rule.Run(context))
                    issues.Add(issue with { Category = issue.Category.Length > 0 ? issue.Category : rule.Category });
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                issues.Add(new ChartIssue(rule.Id + ".crashed", IssueSeverity.Error, $"The check \"{rule.Id}\" failed: {e.Message}") { Category = rule.Category });
            }
        }

        var sorted = issues.OrderBy(i => i.Pulse.HasValue ? 1 : 0).ThenBy(i => i.Pulse ?? 0)
                           .ThenByDescending(i => i.Severity).ThenBy(i => i.RuleId, StringComparer.Ordinal).ToList();
        return new ChartCheckReport(sorted);
    }

    /// <summary>Convenience: <see cref="Run(ChartCheckContext, IEnumerable{IChartCheck})"/> on a chart alone.</summary>
    public static ChartCheckReport Run(Chart chart) => Run(new ChartCheckContext(chart));
}
