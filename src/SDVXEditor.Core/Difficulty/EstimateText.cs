using System.Globalization;

namespace SDVXEditor.Core.LevelEstimation;

/// <summary>
/// How a level estimate is worded in the UI (song select, Song Setup, playtest HUD). Model internals (feature ids,
/// training ranges) never reach the screen in raw form (UI audit B6 / U19).
/// </summary>
public static class EstimateText
{
    /// <summary>Highest level the game has; estimates above it read "20+".</summary>
    public const double GameMaxLevel = 20;

    /// <summary>"17.3", or "20+" above the game's highest level (the model's range reaches 21).</summary>
    public static string Level(double level)
        => level > GameMaxLevel + 0.05 ? "20+" : level.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>
    /// "17.3★ (±0.8)", "20+★ (±0.8)", plus ", uncertain" for a low-confidence result; "…" while pending, "-" without notes.
    /// </summary>
    public static string Short(double? level, DifficultyResult? result)
    {
        if (level is not double l) return result == null ? "…" : "-";

        string s = Level(l) + "★";
        if (result != null)
        {
            s += " (±" + result.Band.ToString("0.0", CultureInfo.InvariantCulture) + ")";
            if (result.LowConfidence) s += ", uncertain";
        }

        return s;
    }

    /// <summary>The low-confidence reasons in plain words, one phrase each (empty when confident).</summary>
    public static IReadOnlyList<string> Reasons(DifficultyResult result)
        => result.LowConfidenceReasons.Select(Reason).Distinct().ToList();

    /// <summary>One low-confidence reason in plain words.</summary>
    public static string Reason(string reason)
    {
        const string far = "features far outside the training range: ";
        const string missing = "features missing from this build: ";

        if (reason.StartsWith(far, StringComparison.Ordinal))
        {
            var what = reason[far.Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                                          .Select(Feature).Distinct().ToList();
            return "unusual for an official chart: " + string.Join(", ", what);
        }

        if (reason.StartsWith(missing, StringComparison.Ordinal)) return "some measurements are unavailable in this build";
        if (reason.StartsWith("uncalibrated fallback model", StringComparison.Ordinal)) return "rough estimate (the calibrated model did not load)";
        return reason switch
        {
            "estimate outside the level range" => "harder or easier than any official chart",
            "chart has no notes" => "the chart has no notes",
            _ => reason,
        };
    }

    /// <summary>A model feature id in plain words ("s_0.05_0.1_w" → "short bursts of notes").</summary>
    public static string Feature(string id)
    {
        if (id.StartsWith("s_", StringComparison.Ordinal))
        {
            // strain features s_<decay>_<…>_<aggregate>: the decay time decides what they describe
            string decay = id.Split('_') is { Length: > 1 } parts ? parts[1] : "";
            return decay switch
            {
                "0.05" => "short bursts of notes",
                "0.125" => "dense note streams",
                _ => "sustained note density",
            };
        }

        if (id.StartsWith("pat.", StringComparison.Ordinal)) return "note patterns";

        return id switch
        {
            "n_events" or "n_notes" => "number of notes",
            "dur" => "song length",
            "dens" or "notesps" or "pk1" or "p95_1" or "p80_1" or "p50_1" or "pk4" or "p90_4" or "npk1" => "note density",
            "hold_frac" => "share of long notes",
            "laser_frac" or "lsec_ps" or "both_lasers_frac" or "wide_frac" => "amount of laser",
            "slam_ps" or "slams" or "slam_pk2" => "slams",
            "travel_ps" or "travel" or "travel_pk2" or "rev_ps" => "laser movement",
            "simult" => "buttons during lasers",
            "ch2" or "ch3" => "chords",
            "nested_frac" => "notes inside long notes",
            "fx_frac" => "FX notes",
            "jacks_ps" => "repeated notes on one lane",
            "nbpm" or "bpm_ratio" => "tempo changes",
            "bpm_max" or "bpm_min" => "tempo",
            "hand_max_t10" or "hand_min_t10" or "hand_max_w" or "hand_min_w" => "one-hand load",
            "knob_t10" or "knob_w" => "knob load",
            "button_t10" or "button_w" => "button load",
            "pattern trees" => "note patterns",
            _ => "other chart features",
        };
    }

    /// <summary>The top contributors in plain words, duplicates merged ("short bursts of notes, note density").</summary>
    public static string Drivers(DifficultyResult result, int count = 3)
        => string.Join(", ", result.TopContributors.Select(c => Feature(c.Feature)).Distinct().Take(count));
}
