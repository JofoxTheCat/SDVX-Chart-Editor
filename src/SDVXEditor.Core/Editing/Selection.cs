using SDVXEditor.Core.Charts;

namespace SDVXEditor.Core.Editing;

/// <summary>
/// The editor's current selection of notes and laser points. A slam's two ends can also be selected one by one
/// (<see cref="SlamEnds"/>): a whole point in <see cref="LaserPoints"/> stands for both ends, an end in
/// <see cref="SlamEnds"/> for that end only. Like laser points, slam ends act on their whole section for move / copy /
/// mirror; delete removes just the end (<see cref="SelectionOps.Delete"/>).
/// </summary>
public sealed class Selection
{
    private readonly HashSet<NoteRef> notes = new();
    private readonly HashSet<LaserPointRef> laserPoints = new();
    private readonly HashSet<LaserSlamEndRef> slamEnds = new();

    public event Action? Changed;

    public IReadOnlyCollection<NoteRef> Notes => notes;
    public IReadOnlyCollection<LaserPointRef> LaserPoints => laserPoints;
    public IReadOnlyCollection<LaserSlamEndRef> SlamEnds => slamEnds;
    public bool IsEmpty => notes.Count == 0 && laserPoints.Count == 0 && slamEnds.Count == 0;
    public int Count => notes.Count + laserPoints.Count + slamEnds.Count;

    public bool Contains(NoteRef n) => notes.Contains(n);
    public bool Contains(LaserPointRef p) => laserPoints.Contains(p);
    public bool Contains(LaserSlamEndRef e) => slamEnds.Contains(e);

    /// <summary>True when that end of the point is selected, on its own or with the whole point.</summary>
    public bool IsEndSelected(LaserPointRef p, bool end) => laserPoints.Contains(p) || slamEnds.Contains(new LaserSlamEndRef(p.Lane, p.Section, p.Point, end));

    /// <summary>True when the point or one of its ends is selected.</summary>
    public bool Touches(LaserPointRef p) => IsEndSelected(p, false) || slamEnds.Contains(new LaserSlamEndRef(p.Lane, p.Section, p.Point, true));

    /// <summary>
    /// Every laser point the selection touches, as point refs (whole points and the points of selected slam ends, once
    /// each) — what a modding timestamp or the density column shows.
    /// </summary>
    public IEnumerable<LaserPointRef> TouchedLaserPoints => laserPoints.Concat(slamEnds.Select(e => e.PointRef)).Distinct();

    public void Add(NoteRef n) { if (notes.Add(n)) Changed?.Invoke(); }
    public void Add(LaserPointRef p) { if (laserPoints.Add(p)) Changed?.Invoke(); }
    public void Remove(NoteRef n) { if (notes.Remove(n)) Changed?.Invoke(); }
    public void Remove(LaserPointRef p) { if (laserPoints.Remove(p)) Changed?.Invoke(); }

    /// <summary>Adds one slam end (nothing when its whole point is already selected).</summary>
    public void Add(LaserSlamEndRef e)
    {
        if (laserPoints.Contains(e.PointRef)) return;
        if (slamEnds.Add(e)) Changed?.Invoke();
    }

    public void Remove(LaserSlamEndRef e) { if (slamEnds.Remove(e)) Changed?.Invoke(); }

    /// <summary>
    /// Ctrl+click on a slam end: selected → deselected (a whole selected point keeps its other end), else selected.
    /// </summary>
    public void Toggle(LaserSlamEndRef e)
    {
        var whole = e.PointRef;
        if (laserPoints.Remove(whole)) slamEnds.Add(e with { End = !e.End });
        else if (!slamEnds.Remove(e)) slamEnds.Add(e);
        Changed?.Invoke();
    }

    public void Toggle(NoteRef n)
    {
        if (!notes.Remove(n)) notes.Add(n);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (IsEmpty) return;
        notes.Clear();
        laserPoints.Clear();
        slamEnds.Clear();
        Changed?.Invoke();
    }

    /// <summary>Replaces the selection with everything starting in [from, to] (osu!-style drag box).</summary>
    public void SelectRange(Chart chart, long from, long to, bool bt = true, bool fx = true, bool lasers = true)
    {
        notes.Clear();
        laserPoints.Clear();
        slamEnds.Clear();
        if (from > to) (from, to) = (to, from);
        void Lanes(NoteKind kind, List<Note>[] lanes)
        {
            for (int l = 0; l < lanes.Length; l++)
                foreach (var n in lanes[l])
                    if (n.Y >= from && n.Y <= to) notes.Add(new NoteRef(kind, l, n));
        }
        if (bt) Lanes(NoteKind.Bt, chart.Notes.Bt);
        if (fx) Lanes(NoteKind.Fx, chart.Notes.Fx);
        if (lasers)
            for (int l = 0; l < 2; l++)
                foreach (var s in chart.Notes.Lasers[l])
                    foreach (var p in s.Points)
                        if (s.Y + p.Ry >= from && s.Y + p.Ry <= to) laserPoints.Add(new LaserPointRef(l, s, p));
        Changed?.Invoke();
    }

    /// <summary>Earliest selected pulse, or null when empty.</summary>
    public long? StartPulse => notes.Select(n => n.Note.Y).Concat(TouchedLaserPoints.Select(p => p.Section.Y + p.Point.Ry))
                                    .Cast<long?>().DefaultIfEmpty(null).Min();

    /// <summary>Latest selected pulse including hold ends, or null when empty.</summary>
    public long? EndPulse => notes.Select(n => n.Note.EndY).Concat(TouchedLaserPoints.Select(p => p.Section.Y + p.Point.Ry))
                                  .Cast<long?>().DefaultIfEmpty(null).Max();

    /// <summary>Laser sections touched by the selection (by a point or a slam end).</summary>
    public IEnumerable<(int Lane, LaserSection Section)> Sections =>
        laserPoints.Select(p => (p.Lane, p.Section)).Concat(slamEnds.Select(e => (e.Lane, e.Section))).Distinct();
}
