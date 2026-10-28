using ManagedBass;
using osu.Framework.Logging;
using SDVXEditor.Core.Audio;

namespace SDVXEditor.Game.Screens.Edit.Components;

/// <summary>
/// Decodes a song with BASS and runs the <see cref="SpectralAnalysis"/> in the background, once per audio file:
/// the result is kept for the last few files (key = full path + size + modification time), so switching
/// difficulties or reopening the song does not decode it again. A whole song takes a few hundred ms
/// (measured: 136 s ogg ≈ 0.3 s analysis after decoding).
/// </summary>
public static class SpectralAnalysisCache
{
    private const int max_entries = 4;

    private static readonly object gate = new object();
    private static readonly List<(string Key, Task<SpectralAnalysis?> Task)> entries = new List<(string, Task<SpectralAnalysis?>)>();

    /// <summary>The analysis of <paramref name="path"/> (shared task; null result = not decodable).</summary>
    public static Task<SpectralAnalysis?> Get(string path)
    {
        string key;

        try
        {
            var info = new FileInfo(path);
            key = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        }
        catch (Exception e)
        {
            return Task.FromResult(fail(path, e));
        }

        lock (gate)
        {
            int i = entries.FindIndex(e => e.Key == key);
            if (i >= 0)
            {
                var hit = entries[i];
                entries.RemoveAt(i);
                entries.Add(hit);
                return hit.Task;
            }

            var task = Task.Factory.StartNew(() => analyse(path), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            entries.Add((key, task));
            while (entries.Count > max_entries) entries.RemoveAt(0);
            return task;
        }
    }

    private static SpectralAnalysis? fail(string path, Exception e)
    {
        Logger.Log($"spectral waveform: {Path.GetFileName(path)} not analysed: {e.Message}");
        return null;
    }

    private static SpectralAnalysis? analyse(string path)
    {
        int stream = 0;

        try
        {
            // decode-only stream: needs BASS initialised (the framework's audio manager does that), no device output
            stream = Bass.CreateStream(path, 0, 0, BassFlags.Decode | BassFlags.Float | BassFlags.Prescan);
            if (stream == 0) throw new InvalidOperationException($"BASS could not open it ({Bass.LastError})");

            var info = Bass.ChannelGetInfo(stream);
            var builder = new SpectralAnalysis.Builder(info.Frequency, Math.Max(1, info.Channels));
            float[] buffer = new float[32768];

            while (true)
            {
                int bytes = Bass.ChannelGetData(stream, buffer, buffer.Length * sizeof(float));
                if (bytes <= 0) break;
                builder.Add(buffer.AsSpan(0, bytes / sizeof(float)));
            }

            var result = builder.Build();
            Logger.Log($"spectral waveform: {Path.GetFileName(path)} analysed ({result.LengthMs / 1000:0.0} s, {info.Frequency} Hz, {info.Channels} ch)");
            return result;
        }
        catch (Exception e)
        {
            return fail(path, e);
        }
        finally
        {
            if (stream != 0) Bass.StreamFree(stream);
        }
    }
}
