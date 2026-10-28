using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using SDVXEditor.Game.Configuration;

namespace SDVXEditor.Game.Screens.SongSelect;

/// <summary>
/// Plays the selected song from its preview point (kson <c>bgm.preview</c> / ksh <c>po</c>/<c>plength</c>),
/// fading in and out and looping the preview window. Follows the music volume setting (the master volume
/// is already on the audio manager). Switching songs is debounced so scrolling through the wheel does not
/// start a track per panel.
/// </summary>
public partial class SongPreviewPlayer : Component
{
    private const double fade_in = 600;
    private const double fade_out = 300;
    private const double default_length = 15_000;
    private const double start_delay = 250;

    [Resolved]
    private AudioManager audio { get; set; } = null!;

    [Resolved]
    private EditorConfigManager config { get; set; } = null!;

    private IBindable<double> musicVolume = null!;

    private Playing? current;
    private string? requestedKey;
    private ScheduledDelegate? pendingStart;

    /// <summary>Song key of the track that is playing (or loading), null when silent. For tests.</summary>
    public string? CurrentKey => current?.Key;

    public bool IsPlaying => current?.Track.IsRunning == true;

    [BackgroundDependencyLoader]
    private void load()
    {
        musicVolume = config.GetBindable<double>(EditorSetting.VolumeMusic);
    }

    /// <summary>Starts the preview of <paramref name="item"/>'s song; no-op if that song already plays.</summary>
    public void Play(ISongListItem? item)
    {
        string? key = item?.AudioPath == null ? null : item.SongKey;
        if (key == requestedKey) return;

        requestedKey = key;
        pendingStart?.Cancel();
        stopCurrent();

        if (item?.AudioPath is not string path || !File.Exists(path))
            return;

        double offset = Math.Max(0, item.PreviewOffsetMs);
        double length = item.PreviewDurationMs > 1000 ? item.PreviewDurationMs : default_length;

        pendingStart = Scheduler.AddDelayed(() => start(key!, path, offset, length), start_delay);
    }

    public void Stop()
    {
        requestedKey = null;
        pendingStart?.Cancel();
        stopCurrent();
    }

    /// <summary>
    /// Stops the preview at once — no fade, because a suspended song select is no longer updated and a
    /// scheduled fade-out would never finish — and returns the audio position it had reached (ms in the
    /// file), or null when nothing was audible yet.
    /// </summary>
    public double? StopImmediately()
    {
        requestedKey = null;
        pendingStart?.Cancel();

        if (current == null) return null;

        var p = current;
        current = null;
        double? position = p.Track.IsRunning ? p.Track.CurrentTime : null;
        p.Dispose();
        return position;
    }

    private void start(string key, string path, double offset, double length)
    {
        Task.Run(() =>
        {
            var resources = new ResourceStore<byte[]>(new StorageBackedResourceStore(new NativeStorage(Path.GetDirectoryName(path)!)));
            var store = audio.GetTrackStore(resources);
            // the track store returns null for a file it cannot open or decode; the preview is optional,
            // so that must not take the song select (and the whole app) down
            var track = store.Get(Path.GetFileName(path))
                        ?? throw new InvalidOperationException($"the audio file could not be opened ({Path.GetFileName(path)})");
            return new Playing(key, track, store, resources, offset, length);
        }).ContinueWith(t =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                Logger.Log($"preview audio failed for {path}: {t.Exception?.GetBaseException().Message}");
                return;
            }

            Schedule(() =>
            {
                var playing = t.Result;

                if (requestedKey != key || IsDisposed)
                {
                    playing.Dispose();
                    return;
                }

                stopCurrent();
                current = playing;
                playing.Track.AddAdjustment(AdjustableProperty.Volume, musicVolume);
                playing.Track.AddAdjustment(AdjustableProperty.Volume, playing.Fade);
                restart(playing);
            });
        });
    }

    private void restart(Playing p)
    {
        if (current != p) return;

        double end = p.Track.Length > 0 ? Math.Min(p.Offset + p.Length, p.Track.Length) : p.Offset + p.Length;
        double from = p.Track.Length > 0 && p.Offset >= p.Track.Length ? 0 : p.Offset;

        p.Track.Seek(from);
        p.Fade.Value = 0;
        p.Track.Start();
        this.TransformBindableTo(p.Fade, 1, fade_in);

        double window = Math.Max(2000, end - from);
        Scheduler.AddDelayed(() =>
        {
            if (current != p) return;

            this.TransformBindableTo(p.Fade, 0, fade_out);
            Scheduler.AddDelayed(() => restart(p), fade_out + 100);
        }, window - fade_out);
    }

    private void stopCurrent()
    {
        if (current == null) return;

        var p = current;
        current = null;
        this.TransformBindableTo(p.Fade, 0, fade_out);
        Scheduler.AddDelayed(p.Dispose, fade_out + 50);
    }

    protected override void Dispose(bool isDisposing)
    {
        current?.Dispose();
        current = null;
        base.Dispose(isDisposing);
    }

    private sealed class Playing : IDisposable
    {
        public string Key { get; }
        public Track Track { get; }
        public double Offset { get; }
        public double Length { get; }
        public readonly BindableDouble Fade = new BindableDouble();

        private readonly ITrackStore store;
        private readonly ResourceStore<byte[]> resources;

        public Playing(string key, Track track, ITrackStore store, ResourceStore<byte[]> resources, double offset, double length)
        {
            Key = key;
            Track = track;
            this.store = store;
            this.resources = resources;
            Offset = offset;
            Length = length;
        }

        public void Dispose()
        {
            Track.Stop();
            Track.Dispose();
            store.Dispose();
            resources.Dispose();
        }
    }
}
