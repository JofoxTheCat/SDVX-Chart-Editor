namespace SDVXEditor.Core.Gameplay;

/// <summary>
/// The playtest's rules engine, independent of rendering and devices. The caller reports button presses,
/// releases and knob movement with their song time and calls <see cref="Update"/> once per frame; the judge
/// emits <see cref="JudgementEvent"/>s and feeds the <see cref="ScoreProcessor"/>.
/// <list type="bullet">
/// <item><b>Chips:</b> a press judges the earliest unjudged chip of its lane within the ERROR window
/// (<see cref="JudgementWindows.JudgeChip"/>); a chip later than the ERROR window is a miss.</item>
/// <item><b>Hold ticks:</b> CRITICAL when the button is down at the tick, else ERROR.</item>
/// <item><b>Laser cursor</b> (after USC's laser handling): when a section starts the cursor snaps onto it; the cursor
/// moves with the knob input and locks onto the laser while it is within <see cref="JudgementWindows.LaserLeniency"/>
/// and the laser is still, or the input turns the laser's way (kept for <see cref="JudgementWindows.LaserAssistMs"/>
/// without input). A tick is CRITICAL when the cursor is locked or within the leniency.</item>
/// <item><b>Slams:</b> knob input in the slam's direction from half the slam window early up to the full window late;
/// a hit puts the cursor at the slam's end value. That input does not move the cursor itself.</item>
/// <item><b>Autoplay</b> judges everything CRITICAL at its exact time and keeps the cursors on the lasers.</item>
/// </list>
/// </summary>
public sealed class GameplayJudge
{
    public PlayableChart Chart { get; }
    public JudgementWindows Windows { get; }
    public ScoreProcessor Score { get; }

    public bool Autoplay { get; set; }

    public event Action<JudgementEvent>? Judged;

    /// <summary>Buttons currently down (0-5), including autoplay's virtual presses.</summary>
    public bool[] Held { get; } = new bool[PlayLanes.Buttons];

    /// <summary>Laser cursor positions (0..1 laser value units).</summary>
    public double[] Cursor { get; } = { 0, 1 };

    /// <summary>True while a cursor is on an active laser section.</summary>
    public bool[] CursorActive { get; } = new bool[2];

    /// <summary>True while a cursor is locked onto its laser.</summary>
    public bool[] Locked { get; } = new bool[2];

    /// <summary>The section a cursor is on (null between sections).</summary>
    public LaserObject?[] ActiveLaser { get; } = new LaserObject?[2];

    public double LastUpdateMs { get; private set; } = double.NegativeInfinity;

    private readonly bool[] userHeld = new bool[PlayLanes.Buttons];
    private readonly int[] nextChip = new int[PlayLanes.Buttons];
    private readonly int[] nextHoldTick = new int[PlayLanes.Buttons];
    private readonly int[] nextLaserTick = new int[2];
    private readonly int[] nextSlam = new int[2];
    private readonly int[] nextSection = new int[2];
    private readonly double[] pendingDelta = new double[2];
    private readonly double[] lastMatchMs = { double.NegativeInfinity, double.NegativeInfinity };
    private readonly double[] autoPressUntil = new double[PlayLanes.Buttons];

    public GameplayJudge(PlayableChart chart, JudgementWindows? windows = null, bool autoplay = false, GaugeType gauge = GaugeType.Normal)
    {
        Chart = chart;
        Windows = (windows ?? JudgementWindows.Default).Sanitised();
        Autoplay = autoplay;
        Score = ScoreProcessor.For(chart, gauge);
    }

    /// <summary>All counted units judged.</summary>
    public bool Finished =>
        Enumerable.Range(0, PlayLanes.Buttons).All(l => nextChip[l] >= Chart.Chips[l].Count && nextHoldTick[l] >= Chart.HoldTicks[l].Count)
        && Enumerable.Range(0, 2).All(s => nextLaserTick[s] >= Chart.LaserTicks[s].Count && nextSlam[s] >= Chart.Slams[s].Count);

    // ---- input -------------------------------------------------------------

    public void Press(int lane, double ms)
    {
        if (lane is < 0 or >= PlayLanes.Buttons || userHeld[lane]) return;
        userHeld[lane] = true;
        if (Autoplay) return;

        var chips = Chart.Chips[lane];
        skipJudged(chips, ref nextChip[lane]);
        for (int i = nextChip[lane]; i < chips.Count; i++)
        {
            var o = chips[i];
            if (o.Judged) continue;
            double offset = ms - o.Ms;
            if (offset < -Windows.ErrorMs) break;
            if (Windows.JudgeChip(offset) is { } result)
            {
                judge(o, result, offset, ms);
                break;
            }
        }
    }

    public void Release(int lane, double ms)
    {
        if (lane is < 0 or >= PlayLanes.Buttons) return;
        userHeld[lane] = false;
    }

    /// <summary>Knob input for laser <paramref name="side"/> (0/1) in laser value units, positive = right.</summary>
    public void MoveLaser(int side, double delta, double ms)
    {
        if (side is < 0 or > 1 || delta == 0 || double.IsNaN(delta)) return;
        int dir = Math.Sign(delta);

        if (!Autoplay)
        {
            var slams = Chart.Slams[side];
            for (int i = nextSlam[side]; i < slams.Count; i++)
            {
                var s = slams[i];
                if (s.Judged || s.SlamInputSeen) continue;
                double offset = ms - s.Ms;
                if (offset < -Windows.SlamMs / 2) break;
                if (offset > Windows.SlamMs) continue;
                if (s.Slam.Direction != dir) break;
                s.SlamInputSeen = true;
                s.SlamInputOffset = offset;
                // the turn that hits a slam does not drag the cursor off the laser
                return;
            }
        }

        if (ActiveLaser[side] is { } active && dir == active.DirectionAt(Chart.Timing.MsToPulse(ms)))
            lastMatchMs[side] = ms;

        pendingDelta[side] += delta;
    }

    // ---- frame ------------------------------------------------------------

    public void Update(double ms)
    {
        LastUpdateMs = ms;

        for (int lane = 0; lane < PlayLanes.Buttons; lane++)
        {
            updateChips(lane, ms);
            Held[lane] = userHeld[lane] || (Autoplay && (autoPressUntil[lane] > ms || holdActive(lane, ms)));
            updateHoldTicks(lane, ms);
        }

        for (int side = 0; side < 2; side++)
        {
            updateCursor(side, ms);
            updateSlams(side, ms);
            updateLaserTicks(side, ms);
        }
    }

    private void updateChips(int lane, double ms)
    {
        var chips = Chart.Chips[lane];
        skipJudged(chips, ref nextChip[lane]);
        for (int i = nextChip[lane]; i < chips.Count; i++)
        {
            var o = chips[i];
            if (o.Judged) continue;

            if (Autoplay)
            {
                if (o.Ms > ms) break;
                judge(o, HitResult.SCritical, 0, o.Ms);
                autoPressUntil[lane] = o.Ms + 60;
                continue;
            }

            if (ms - o.Ms <= Windows.ErrorMs) break;
            judge(o, HitResult.Error, double.NaN, ms);
        }

        skipJudged(chips, ref nextChip[lane]);
    }

    private bool holdActive(int lane, double ms)
    {
        foreach (var h in Chart.Holds[lane])
        {
            if (h.Ms > ms) break;
            if (h.EndMs >= ms) return true;
        }

        return false;
    }

    private void updateHoldTicks(int lane, double ms)
    {
        var ticks = Chart.HoldTicks[lane];
        skipJudged(ticks, ref nextHoldTick[lane]);
        while (nextHoldTick[lane] < ticks.Count && ticks[nextHoldTick[lane]].Ms <= ms)
        {
            var o = ticks[nextHoldTick[lane]++];
            if (o.Judged) continue;
            bool down = Autoplay || userHeld[lane];
            judge(o, down ? HitResult.Critical : HitResult.Error, double.NaN, o.Ms);
        }
    }

    private void updateCursor(int side, double ms)
    {
        double delta = pendingDelta[side];
        pendingDelta[side] = 0;

        var sections = Chart.Lasers[side];
        while (nextSection[side] < sections.Count && sections[nextSection[side]].EndMs < ms)
            nextSection[side]++;

        LaserObject? active = null;
        if (nextSection[side] < sections.Count && sections[nextSection[side]].Ms <= ms)
            active = sections[nextSection[side]];

        double pulse = Chart.Timing.MsToPulse(ms);

        if (active == null)
        {
            ActiveLaser[side] = null;
            CursorActive[side] = false;
            Locked[side] = false;
            Cursor[side] = Math.Clamp(Cursor[side] + delta, 0, 1);
            return;
        }

        double target = active.ValueAt(Math.Clamp(pulse, active.Section.Y, active.Section.EndY));

        if (!ReferenceEquals(active, ActiveLaser[side]))
        {
            // a new section: the cursor appears on it
            ActiveLaser[side] = active;
            CursorActive[side] = true;
            Cursor[side] = target;
            Locked[side] = true;
            lastMatchMs[side] = ms;
            return;
        }

        if (Autoplay)
        {
            Cursor[side] = target;
            Locked[side] = true;
            return;
        }

        int laserDir = active.DirectionAt(pulse);
        int inputDir = Math.Sign(delta);
        if (inputDir != 0 && inputDir == laserDir) lastMatchMs[side] = ms;

        double before = Cursor[side];
        double after = Math.Clamp(before + delta, 0, 1);
        double len = Windows.LaserLeniency;
        bool near = Math.Abs(before - target) <= len || Math.Abs(after - target) <= len;
        bool following = laserDir == 0
                         || inputDir == laserDir
                         || (inputDir == 0 && ms - lastMatchMs[side] <= Windows.LaserAssistMs);

        if (near && following)
        {
            Cursor[side] = target;
            Locked[side] = true;
        }
        else
        {
            Cursor[side] = after;
            Locked[side] = false;
        }
    }

    private void updateSlams(int side, double ms)
    {
        var slams = Chart.Slams[side];
        skipJudged(slams, ref nextSlam[side]);
        while (nextSlam[side] < slams.Count)
        {
            var o = slams[nextSlam[side]];
            if (o.Judged)
            {
                nextSlam[side]++;
                continue;
            }

            if (Autoplay || o.SlamInputSeen)
            {
                if (o.Ms > ms) break;
                judge(o, HitResult.Critical, Autoplay ? 0 : o.SlamInputOffset, o.Ms);
                if (ReferenceEquals(ActiveLaser[side], o.Laser) || ActiveLaser[side] == null)
                {
                    Cursor[side] = o.Slam.To;
                    Locked[side] = true;
                    lastMatchMs[side] = ms;
                }
                nextSlam[side]++;
                continue;
            }

            if (ms - o.Ms <= Windows.SlamMs) break;
            judge(o, HitResult.Error, double.NaN, ms);
            nextSlam[side]++;
        }
    }

    private void updateLaserTicks(int side, double ms)
    {
        var ticks = Chart.LaserTicks[side];
        skipJudged(ticks, ref nextLaserTick[side]);
        while (nextLaserTick[side] < ticks.Count && ticks[nextLaserTick[side]].Ms <= ms)
        {
            var o = ticks[nextLaserTick[side]++];
            if (o.Judged) continue;

            bool hit = Autoplay;
            if (!hit)
            {
                // the cursor is evaluated now (at most one frame after the tick); a locked cursor is on the laser by definition
                bool sameSection = ReferenceEquals(ActiveLaser[side], o.Laser);
                hit = (sameSection && Locked[side])
                      || Math.Abs(Cursor[side] - o.Laser!.ValueAt(o.Pulse)) <= Windows.LaserLeniency;
            }

            judge(o, hit ? HitResult.Critical : HitResult.Error, double.NaN, o.Ms);
        }
    }

    private static void skipJudged(IReadOnlyList<ScoreObject> list, ref int index)
    {
        while (index < list.Count && list[index].Judged) index++;
    }

    private void judge(ScoreObject o, HitResult result, double offset, double ms)
    {
        o.Judged = true;
        o.Result = result;
        var e = new JudgementEvent(o, result, offset, ms);
        Score.Apply(e);
        Judged?.Invoke(e);
    }

    /// <summary>
    /// Runs a whole chart headless with autoplay from its start to the end at a fixed frame step
    /// (tests and the smoke test's expectations).
    /// </summary>
    public static ScoreProcessor SimulateAutoplay(PlayableChart chart, double frameMs = 1000.0 / 240)
    {
        var judge = new GameplayJudge(chart, autoplay: true);
        double start = double.IsNegativeInfinity(chart.StartMs) ? Math.Min(0, chart.All.Select(o => o.Ms).DefaultIfEmpty(0).Min()) - 100 : chart.StartMs;
        for (double t = start; t <= chart.LastObjectMs + 500; t += frameMs)
            judge.Update(t);
        return judge.Score;
    }
}
