namespace SDVXEditor.Core.Editing;

/// <summary>
/// Dragging the tail of a hold (select tool, or a note tool of the hold's kind): the grabbed hold's end follows the
/// pointer, every other selected hold changes by the same delta (osu!-like). Only the grabbed hold can become a chip,
/// and only by dragging its tail onto its start; the others stop at one snap tick (or their own length, if shorter).
/// </summary>
public static class HoldTailResize
{
    /// <summary>The grabbed hold's new length for the (snapped) pointer pulse; at or before its start it is a chip.</summary>
    public static long GrabbedLength(long start, long pointerPulse) => Math.Max(0, pointerPulse - start);

    /// <summary>Another selected hold's new length for the grabbed hold's delta, never below <paramref name="minLength"/> (or its own length).</summary>
    public static long OtherLength(long length, long delta, long minLength)
    {
        long floor = Math.Min(length, Math.Max(1, minLength));
        return Math.Max(floor, length + delta);
    }

    /// <summary>
    /// Whether a press is on the tail: within <paramref name="grabPx"/> of the end and nearer the end than the start
    /// (a very short hold keeps its head for moving).
    /// </summary>
    public static bool IsTailGrab(double pxFromEnd, double pxFromStart, double grabPx) => pxFromEnd <= grabPx && pxFromEnd < pxFromStart;
}
