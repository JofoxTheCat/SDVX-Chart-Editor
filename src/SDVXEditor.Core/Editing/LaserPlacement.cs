using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// A laser section being placed with the laser tool (osu!lazer slider placement): lane, width, start pulse and the
/// anchors so far (relative to <see cref="Y"/>). <see cref="Preview"/> is the baked section as it would be added.
/// </summary>
/// <remarks>
/// A slam is placed with two clicks on one beat (<see cref="LaserPathEditing.AddAnchor"/>). When those are the only
/// clicks, the draft is one slam anchor and finishes as exactly that: a lone-slam section (one point V → Vf, no tail),
/// the same section dragging a slam gives (<see cref="LaserEditing.NewSection"/>). Official charts have 118,940 such
/// sections (all 8,602 .vox); an earlier 0.1.0 build added a one-snap-tick tail, which official charts never do.
/// </remarks>
public sealed class LaserPlacement
{
    public LaserPlacement(int lane, bool wide, long y, LaserPath path)
    {
        Lane = lane;
        Wide = wide;
        Y = y;
        Path = path;
    }

    public int Lane { get; }
    public bool Wide { get; }
    public long Y { get; }
    public LaserPath Path { get; }

    private LaserSection? preview;

    /// <summary>A copy with other anchors / start pulse (same lane and width).</summary>
    public LaserPlacement With(long y, LaserPath path) => new(Lane, Wide, y, path);

    /// <summary>The section finishing would add, or null while the draft is a single plain anchor.</summary>
    public LaserSection? Result => Path.Count >= 2 || Path.Count == 1 && Path.Anchors[0].IsSlam ? Preview : null;

    /// <summary>The section this draft would add (a one-anchor draft is a single point; a lone slam is the slam alone).</summary>
    public LaserSection Preview => preview ??= Path.Count >= 2 || Path.Anchors[0].IsSlam
        ? LaserPaths.ToSection(Path, Y, Wide ? 2 : 1)
        : new LaserSection(Y, new[] { new LaserPoint(0, Path.Anchors[0].V) }, Wide ? 2 : 1);
}
