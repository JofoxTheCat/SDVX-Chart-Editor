using osu.Framework.Audio.Track;
using osu.Framework.Bindables;

namespace SDVXEditor.Game.Screens.Edit;

/// <summary>
/// The editor transport: current time, play state and playback rate, driven by the song's
/// <see cref="Track"/> when there is one. Every view reads <see cref="CurrentTime"/>; setting it from
/// outside is a seek. The rate (25–100 %) goes to the track's <c>Tempo</c>, which BASS_FX applies
/// without changing the pitch. Past the end of the audio (or without audio) the clock runs on its own.
/// <para>
/// <see cref="CurrentTime"/> is the time that is drawn and judged. The track runs
/// <see cref="AudioOffset"/> ms ahead of it (<see cref="TrackTime"/>): with a positive offset the
/// picture waits for sound that reaches the listener late (osu!'s universal offset).
/// </para>
/// </summary>
public sealed class EditorClock
{
    /// <summary>Allowed difference between our interpolated time and the track before we jump to the track.</summary>
    private const double max_drift_ms = 40;

    public readonly BindableDouble CurrentTime = new BindableDouble { MinValue = 0 };
    public readonly BindableBool IsPlaying = new BindableBool();

    /// <summary>Playback rate (0.25 - 1). Pitch is kept (track tempo, not frequency). osu!stable's editor offers no more than 100 % either.</summary>
    public readonly BindableDouble Rate = new BindableDouble(1) { MinValue = 0.25, MaxValue = 1 };

    /// <summary>Universal audio offset in ms (bound to the editor setting): track position = <see cref="CurrentTime"/> + offset.</summary>
    public readonly BindableDouble AudioOffset = new BindableDouble();

    /// <summary>
    /// Loop playback range in ms (null = off): while playing, reaching <c>End</c> jumps back to <c>Start</c>; starting
    /// playback outside the range starts at <c>Start</c>.
    /// </summary>
    public readonly Bindable<(double Start, double End)?> Loop = new Bindable<(double Start, double End)?>();

    /// <summary>How often the loop jumped back (smoke test).</summary>
    public int LoopWraps { get; private set; }

    private Track? track;
    private bool updatingFromTrack;
    private bool startRequested;
    private double minimumLength;

    public EditorClock()
    {
        CurrentTime.BindValueChanged(e =>
        {
            if (updatingFromTrack) return;
            seekTrack(e.NewValue);
        });

        IsPlaying.BindValueChanged(e =>
        {
            startRequested = false;
            if (e.NewValue && Loop.Value is { } loop && (CurrentTime.Value < loop.Start - 1 || CurrentTime.Value >= loop.End))
                CurrentTime.Value = loop.Start;
            if (track == null) return;

            if (e.NewValue && TrackTime >= 0 && TrackTime < trackLength)
            {
                track.Seek(TrackTime);
                track.Start();
            }
            else
                track.Stop();
        });

        Rate.BindValueChanged(e =>
        {
            if (track != null) track.Tempo.Value = e.NewValue;
        });

        AudioOffset.BindValueChanged(_ => seekTrack(CurrentTime.Value));
    }

    /// <summary>Position in the audio file that belongs to <see cref="CurrentTime"/> (the track is kept there).</summary>
    public double TrackTime => CurrentTime.Value + AudioOffset.Value;

    /// <summary>The song audio; null while there is none (the clock then runs freely).</summary>
    public Track? Track
    {
        get => track;
        set
        {
            track?.Stop();
            track = value;
            if (track == null) return;

            track.Tempo.Value = Rate.Value;
            track.Seek(Math.Max(0, TrackTime));
            if (IsPlaying.Value && TrackTime >= 0) track.Start();
        }
    }

    private double trackLength => track is { IsLoaded: true } ? track.Length : 0;

    /// <summary>Length of the loaded song audio in ms; 0 while there is none or it is still loading.</summary>
    public double AudioLengthMs => trackLength;

    /// <summary>Time (ms) of the chart's last object; set by the editor after every chart change.</summary>
    public double LastObjectMs { get; set; }

    /// <summary>Time (ms) the chart's own end (last object plus a measure, or an explicit end); used only without audio.</summary>
    public double ChartEndMs { get; set; }

    /// <summary>Without audio the scrubbable range is the chart end plus this, but never shorter than <see cref="MIN_LENGTH_WITHOUT_AUDIO_MS"/>.</summary>
    public const double MIN_LENGTH_WITHOUT_AUDIO_MS = 10_000;

    /// <summary>True when the chart has objects after the end of the audio (the bottom bar marks the part without audio).</summary>
    public bool ChartBeyondAudio => trackLength > 0 && LastObjectMs > trackLength + 1;

    /// <summary>
    /// Length of the scrubbable range: the audio file's length once it is loaded (extended to the last object when
    /// the chart goes on after the audio); without audio the chart end plus 2 s. Setting it only raises a minimum
    /// (smoke tests that need room behind the chart); 0 removes it.
    /// </summary>
    public double LengthMs
    {
        get
        {
            double natural = trackLength > 0
                ? Math.Max(trackLength, LastObjectMs)
                : Math.Max(Math.Max(ChartEndMs, LastObjectMs) + 2000, MIN_LENGTH_WITHOUT_AUDIO_MS);
            return Math.Max(natural, minimumLength);
        }
        set => minimumLength = value;
    }

    /// <summary>Advances the clock; called once per frame by the editor screen.</summary>
    public void Update(double elapsedMs)
    {
        if (!IsPlaying.Value)
            return;

        double offset = AudioOffset.Value;
        double next = CurrentTime.Value + elapsedMs * Rate.Value;

        // before the audio starts (a negative offset at time 0) the clock runs on its own and starts the track on the way
        if (track != null && trackLength > 0 && next + offset >= 0 && TrackTime < trackLength && !track.HasCompleted)
        {
            if (track.IsRunning)
            {
                startRequested = false;

                // smooth between the audio thread's updates, but never drift away from the audio
                double audio = track.CurrentTime - offset;
                double diff = audio - next;
                next = Math.Abs(diff) > max_drift_ms ? audio : next + diff * 0.1;
            }
            else
            {
                // the audio finished loading after play was pressed, or is still starting: (re)start it once and wait
                if (!startRequested)
                {
                    track.Seek(Math.Max(0, next + offset));
                    track.Start();
                    startRequested = true;
                }

                if (TrackTime >= 0) next = CurrentTime.Value;
            }
        }

        if (Loop.Value is { } range && range.End > range.Start && next >= range.End)
        {
            LoopWraps++;
            CurrentTime.Value = range.Start; // a seek: the track follows
            return;
        }

        if (next >= LengthMs)
        {
            next = LengthMs;
            IsPlaying.Value = false;
        }

        set(next);
    }

    /// <summary>Stop button: pause and return to the start.</summary>
    public void Stop()
    {
        IsPlaying.Value = false;
        CurrentTime.Value = 0;
    }

    private void set(double value)
    {
        updatingFromTrack = true;
        try { CurrentTime.Value = value; }
        finally { updatingFromTrack = false; }
    }

    private void seekTrack(double ms)
    {
        startRequested = false;
        if (track == null) return;

        double pos = ms + AudioOffset.Value;
        if (pos < trackLength)
        {
            track.Seek(Math.Max(0, pos));
            if (pos < 0) track.Stop();
            else if (IsPlaying.Value && !track.IsRunning) track.Start();
        }
        else
            track.Stop();
    }
}
