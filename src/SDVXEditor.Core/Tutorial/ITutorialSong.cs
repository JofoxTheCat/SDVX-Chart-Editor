namespace SDVXEditor.Core.Tutorial;

/// <summary>
/// What every tutorial song tells the shared tutorial machinery (workspace, steps, overlay): its audio file, the
/// display names and the time of a pulse. Each tutorial (classic, main) has its own definition behind it.
/// </summary>
public interface ITutorialSong
{
    string Title { get; }
    string Artist { get; }

    /// <summary>Audio file name inside the embedded template (and in the prepared song folder).</summary>
    string Audio { get; }

    /// <summary>Where measure 1 starts in the audio, ms.</summary>
    long OffsetMs { get; }

    /// <summary>Song time (ms, audio clock) of a pulse.</summary>
    double MsAt(long pulse);

    /// <summary>End of the charted measures, ms.</summary>
    double LengthMs { get; }
}
