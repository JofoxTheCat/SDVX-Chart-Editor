using osu.Framework.Audio.Track;

namespace SDVXEditor.Game.Screens.Play;

/// <summary>
/// The playtest's song clock. Unlike the editor transport it may start before 0 (lead-in before the audio) and it
/// only moves forward while running. Song time runs <see cref="Rate"/> × real time (the editor's playback rate,
/// applied to the track's tempo so the pitch is kept) and the track is kept at song time +
/// <see cref="AudioOffset"/> (the universal offset, as in <see cref="Edit.EditorClock"/>).
/// </summary>
public sealed class PlaytestClock
{
    private const double max_drift_ms = 40;

    public double CurrentTime { get; private set; }
    public bool IsRunning { get; private set; }
    public double Rate { get; }
    public double AudioOffset { get; }
    public Track? Track { get; }

    private bool startRequested;
    private double waitedMs;

    /// <summary>How long the clock waits for a starting track before running on without it.</summary>
    private const double max_start_wait_ms = 400;

    public PlaytestClock(Track? track, double rate, double audioOffset, double startTime)
    {
        Track = track;
        Rate = Math.Clamp(rate, 0.05, 4);
        AudioOffset = audioOffset;
        CurrentTime = startTime;
        if (track != null)
        {
            track.Stop();
            track.Tempo.Value = Rate;
        }
    }

    private double trackLength => Track is { IsLoaded: true } t ? t.Length : 0;

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        startRequested = false;
    }

    public void Stop()
    {
        IsRunning = false;
        Track?.Stop();
    }

    public void Seek(double time)
    {
        CurrentTime = time;
        startRequested = false;
        if (Track == null) return;
        Track.Stop();
        double pos = time + AudioOffset;
        if (pos >= 0 && pos < trackLength) Track.Seek(pos);
    }

    /// <summary>Advances by <paramref name="elapsedRealMs"/> of real time; call once per frame.</summary>
    public void Update(double elapsedRealMs)
    {
        if (!IsRunning) return;

        double next = CurrentTime + elapsedRealMs * Rate;
        var track = Track;

        if (track != null && trackLength > 0)
        {
            double pos = next + AudioOffset;
            if (pos >= 0 && pos < trackLength && !track.HasCompleted)
            {
                if (track.IsRunning)
                {
                    startRequested = false;
                    double audio = track.CurrentTime - AudioOffset;
                    double diff = audio - next;
                    // follow the audio, but never step backwards: judged time only moves forward
                    if (Math.Abs(diff) > max_drift_ms) next = Math.Max(CurrentTime, audio);
                    else next = Math.Max(CurrentTime, next + diff * 0.1);
                }
                else
                {
                    if (!startRequested)
                    {
                        track.Seek(pos);
                        track.Start();
                        startRequested = true;
                        waitedMs = 0;
                    }

                    // wait (briefly) for the audio to actually start before moving on, so there is no jump back later
                    waitedMs += elapsedRealMs;
                    if (waitedMs < max_start_wait_ms)
                        next = Math.Max(CurrentTime, Math.Min(next, track.CurrentTime - AudioOffset));
                }
            }
        }

        CurrentTime = next;
    }
}
