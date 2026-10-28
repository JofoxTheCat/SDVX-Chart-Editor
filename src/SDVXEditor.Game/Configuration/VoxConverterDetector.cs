using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using SDVXEditor.Core.Formats.Vox;

namespace SDVXEditor.Game.Configuration;

public enum ConverterDetectState
{
    /// <summary>Nothing asked for the converter yet.</summary>
    NotStarted,
    Checking,

    /// <summary>The converter loaded; <see cref="ConverterDetection.Version"/> is its version.</summary>
    Detected,

    /// <summary>The host could not start, or ksh2vox could not be loaded.</summary>
    Failed,
}

/// <summary>One converter detection: the host's self-check (<see cref="Info"/>) or why there is none (<see cref="Error"/>).</summary>
public sealed record ConverterDetection(ConverterDetectState State, VoxHostInfo? Info = null, string? Error = null)
{
    public static readonly ConverterDetection NotStarted = new ConverterDetection(ConverterDetectState.NotStarted);

    /// <summary>The ksh2vox version in use, or null while unknown (not detected yet, failed, not loaded).</summary>
    public string? Version => State == ConverterDetectState.Detected ? Info?.ConverterVersion : null;
}

/// <summary>
/// The ksh2vox converter in use, detected once per session through <see cref="VoxExporter.GetInfoAsync"/> (a Python
/// host process): the converter folder is the <see cref="EditorSetting.VoxConverterDir"/> setting, else the submodule.
/// Shared by the options' Export section and the export badges (whose VOX column depends on the version). It starts on
/// the first <see cref="EnsureDetected"/> and runs again on <see cref="Detect"/> or when the converter / Python setting
/// changes after a first detection.
/// </summary>
public partial class VoxConverterDetector : Component
{
    private readonly Bindable<ConverterDetection> current = new Bindable<ConverterDetection>(ConverterDetection.NotStarted);

    /// <summary>The latest result (changes on the update thread).</summary>
    public IBindable<ConverterDetection> Current => current;

    /// <summary>The detection in flight or last finished (null before the first one).</summary>
    public Task<ConverterDetection>? CurrentTask { get; private set; }

    [Resolved]
    private EditorConfigManager config { get; set; } = null!;

    private Bindable<string> converterDir = null!;
    private Bindable<string> python = null!;
    private int generation;

    [BackgroundDependencyLoader]
    private void load()
    {
        converterDir = config.GetBindable<string>(EditorSetting.VoxConverterDir);
        python = config.GetBindable<string>(EditorSetting.VoxPythonPath);
        converterDir.BindValueChanged(_ => settingChanged());
        python.BindValueChanged(_ => settingChanged());
    }

    private void settingChanged()
    {
        if (CurrentTask != null) Schedule(() => Detect());
    }

    /// <summary>Starts the detection unless one ran or runs already (update thread).</summary>
    public Task<ConverterDetection> EnsureDetected() => CurrentTask ?? Detect();

    /// <summary>Detects the converter again with the current settings (update thread); an older run in flight is ignored.</summary>
    public Task<ConverterDetection> Detect()
    {
        int gen = ++generation;
        string? dir = nullIfEmpty(config.Get<string>(EditorSetting.VoxConverterDir));
        string? py = nullIfEmpty(config.Get<string>(EditorSetting.VoxPythonPath));
        current.Value = new ConverterDetection(ConverterDetectState.Checking);

        var task = Task.Run(async () =>
        {
            ConverterDetection result;
            try
            {
                await using var exporter = new VoxExporter(new VoxExporterSettings { ConverterDir = dir, PythonPath = py, Log = l => Logger.Log("vox: " + l) });
                var info = await exporter.GetInfoAsync().ConfigureAwait(false);
                result = info.ConverterLoaded
                    ? new ConverterDetection(ConverterDetectState.Detected, info)
                    : new ConverterDetection(ConverterDetectState.Failed, info, info.ConverterError ?? "unknown reason");
            }
            catch (Exception e)
            {
                result = new ConverterDetection(ConverterDetectState.Failed, null, e.Message);
            }

            Logger.Log($"vox: converter detection: {result.State} {result.Version ?? result.Error}");
            Schedule(() =>
            {
                if (gen == generation) current.Value = result;
            });
            return result;
        });

        CurrentTask = task;
        return task;
    }

    private static string? nullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
