namespace SDVXEditor.Core.Audio;

/// <summary>Chart events that can play a hitsound in the editor.</summary>
public enum HitsoundSlot
{
    BtChip,
    BtHold,
    FxChip,
    FxHold,
    Slam,
    LaserStart,
}

/// <summary>
/// Which sample each <see cref="HitsoundSlot"/> plays, stored as one editable settings line:
/// <c>bt=soft-hitnormal, bthold=soft-hitnormal, fx=drum-hitnormal, fxhold=drum-hitnormal, slam=normal-hitnormal, laser=none</c>.
/// A value is the file name (without extension) of a sample in the hitsound skin folder,
/// <see cref="None"/> (silent) or <see cref="Synth"/> (the editor's own synthesised tick/thud).
/// Unknown keys and invalid values are ignored; a slot that is not named keeps its default.
/// </summary>
public sealed class HitsoundMap
{
    public const string None = "none";
    public const string Synth = "synth";

    private static readonly (HitsoundSlot Slot, string Key, string Default)[] slots =
    {
        (HitsoundSlot.BtChip, "bt", "soft-hitnormal"),
        (HitsoundSlot.BtHold, "bthold", "soft-hitnormal"),
        (HitsoundSlot.FxChip, "fx", "drum-hitnormal"),
        (HitsoundSlot.FxHold, "fxhold", "drum-hitnormal"),
        (HitsoundSlot.Slam, "slam", "normal-hitnormal"),
        (HitsoundSlot.LaserStart, "laser", None),
    };

    public static readonly string DefaultText = new HitsoundMap().ToString();

    private readonly Dictionary<HitsoundSlot, string> map = slots.ToDictionary(s => s.Slot, s => s.Default);

    public string this[HitsoundSlot slot] => map[slot];

    public static string KeyOf(HitsoundSlot slot) => slots.First(s => s.Slot == slot).Key;

    public static HitsoundMap Parse(string? text)
    {
        var m = new HitsoundMap();
        if (string.IsNullOrWhiteSpace(text)) return m;

        foreach (string part in text.Split(',', ';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            string key = part[..eq].Trim().ToLowerInvariant();
            string value = part[(eq + 1)..].Trim();
            int i = Array.FindIndex(slots, s => s.Key == key);
            if (i >= 0 && IsValidValue(value)) m.map[slots[i].Slot] = value.ToLowerInvariant() is None or Synth ? value.ToLowerInvariant() : value;
        }

        return m;
    }

    /// <summary>A plain file stem: no path, no extension games (the sample is looked up as .wav/.ogg/.mp3 in one folder).</summary>
    public static bool IsValidValue(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ' ' or '(' or ')') && value.Trim() == value;

    public override string ToString() => string.Join(", ", slots.Select(s => $"{s.Key}={map[s.Slot]}"));
}
