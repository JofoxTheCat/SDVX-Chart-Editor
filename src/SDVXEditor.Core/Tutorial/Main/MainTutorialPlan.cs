namespace SDVXEditor.Core.Tutorial;

/// <summary>
/// The main tutorial's budget (the classic one has <see cref="TutorialPlan"/>): per chapter the number of steps and
/// the minutes a beginner needs. Johannes first allowed ~15 minutes; after playing it (2026-10-27: "explain more, more
/// details") every feature a step touches got its own explanation and dense steps were split into numbered tasks, and
/// the ceiling was lifted (the coordinator: beyond 15 if the explanations need it). The Game's chapters take
/// their minutes from here, the smoke part <c>tutorial</c> fails when a chapter's step count differs, and
/// <c>TutorialTests.MainPlanFitsItsBudget</c> keeps the total in range.
/// <para>Same estimates as the classic plan: ~6–8 s per information step, 10–20 s per click or key, 30–60 s per group
/// of ghost notes, 1–1.5 min per free-mapping section.</para>
/// </summary>
public static class MainTutorialPlan
{
    /// <summary>The planned length of the whole tutorial (the ceiling is MaxMinutes).</summary>
    public const double TargetMinutes = 18.75;

    public const double MaxMinutes = 20;

    public static IReadOnlyList<(string Title, int Steps, double Minutes)> Chapters { get; } = new[]
    {
        ("Song Setup & Timing", 18, 3.25),
        ("Chips, Holds and FX", 17, 3.0),
        ("Lasers", 10, 2.5),
        ("Navigating", 10, 2.0),
        ("Camera & Background", 14, 2.75),
        ("Effects & Sounds", 10, 1.75),
        ("Test & Difficulty", 8, 2.0),
        ("Save, Export & Share", 8, 1.5),
    };

    public static int TotalSteps => Chapters.Sum(c => c.Steps);

    public static double TotalMinutes => Chapters.Sum(c => c.Minutes);

    public static double MinutesOf(int chapter) => Chapters[chapter].Minutes;
}
