namespace SDVXEditor.Game.Configuration;

public partial class EditorConfigManager
{
    private void initialiseOptionsDefaults()
    {
        SetDefault(EditorSetting.EditorBindings, string.Empty);
        SetDefault(EditorSetting.ShowFps, false);
        SetDefault(EditorSetting.SongsFolder, string.Empty);
        SetDefault(EditorSetting.LauncherConfigPath, string.Empty);
        SetDefault(EditorSetting.ExportFolder, string.Empty);
        SetDefault(EditorSetting.Language, "en");
        SetDefault(EditorSetting.ShowLaserAnchorsOnHover, true);
        SetDefault(EditorSetting.WaveformStyle, WaveformStyle.Spectral);
        SetDefault(EditorSetting.WaveformOpacity, 0.85, 0.2, 1, 0.05);
        SetDefault(EditorSetting.HoldModifier, Core.Input.HoldModifierKey.Ctrl);
        SetDefault(EditorSetting.DragSnapModifier, Core.Input.DragSnapKey.Shift);
        SetDefault(EditorSetting.ConfineMouse, osu.Framework.Input.ConfineMouseMode.Never);
    }

    /// <summary>Every setting with its current value (smoke test: snapshot / compare / restore).</summary>
    public IReadOnlyDictionary<EditorSetting, object?> Snapshot() =>
        ConfigStore.ToDictionary(kv => kv.Key, kv => valueProperty(kv.Value).GetValue(kv.Value));

    /// <summary>Puts back values taken with <see cref="Snapshot"/>; returns the settings that had changed.</summary>
    public IReadOnlyList<EditorSetting> Restore(IReadOnlyDictionary<EditorSetting, object?> snapshot)
    {
        var changed = new List<EditorSetting>();

        foreach (var (key, value) in snapshot)
        {
            if (!ConfigStore.TryGetValue(key, out var bindable)) continue;

            var property = valueProperty(bindable);
            if (Equals(property.GetValue(bindable), value)) continue;

            property.SetValue(bindable, value);
            changed.Add(key);
        }

        return changed;
    }

    private static System.Reflection.PropertyInfo valueProperty(object bindable) =>
        bindable.GetType().GetProperties().FirstOrDefault(p => p.Name == "Value" && p.CanWrite && p.GetIndexParameters().Length == 0)
        ?? throw new InvalidOperationException($"{bindable.GetType().Name} has no Value");
}
