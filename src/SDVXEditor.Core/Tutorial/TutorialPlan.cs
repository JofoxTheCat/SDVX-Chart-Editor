namespace SDVXEditor.Core.Tutorial;

/// <summary>
/// The tutorial's budget: per chapter the number of steps and the time a beginner needs (minutes, shown in the
/// chapter list). Johannes asked for a tutorial of about ten minutes in all. The Game's chapter definitions take
/// their <c>Minutes</c> from here, and the smoke part <c>tutorial</c> checks that every chapter has exactly this many
/// steps, so the plan, the chapters and docs/TUTORIAL.md cannot drift apart silently (NUnit: <c>TutorialTests</c>).
/// <para>
/// The estimates count reading the card plus doing the step at beginner pace: ~6–8 s for an information step,
/// 10–20 s for a click or key, 30–60 s for placing a group of ghost notes, 1–1.5 min for a free-mapping section.
/// </para>
/// </summary>
public static class TutorialPlan
{
    /// <summary>The target length of the whole tutorial.</summary>
    public const double TargetMinutes = 10;

    public static IReadOnlyList<(string Title, int Steps, double Minutes)> Chapters { get; } = new[]
    {
        ("Song Setup & Timing", 16, 2.0),
        ("Chips, Holds and FX", 12, 1.5),
        ("Lasers", 8, 1.75),
        ("Navigating", 8, 1.0),
        ("Camera & Background", 7, 1.0),
        ("Effects & Sounds", 5, 0.75),
        ("Test & Difficulty", 8, 1.25),
        ("Save, Export & Share", 5, 0.75),
    };

    public static int TotalSteps => Chapters.Sum(c => c.Steps);

    public static double TotalMinutes => Chapters.Sum(c => c.Minutes);

    public static double MinutesOf(int chapter) => Chapters[chapter].Minutes;
}
