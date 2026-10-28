using SDVXEditor.Core.Gameplay;

namespace SDVXEditor.Game.Configuration;

public partial class EditorConfigManager
{
    private void initialisePlaytestDefaults()
    {
        SetDefault(EditorSetting.PlaytestBindings, PlayBindings.DefaultText);
        SetDefault(EditorSetting.PlaytestModSpeed, 600.0, 50, 2000, 25);
        SetDefault(EditorSetting.PlaytestAutoplay, false);
        SetDefault(EditorSetting.PlaytestKnobSensitivity, 1.0, -10, 10, 0.05);
        SetDefault(EditorSetting.PlaytestKnobAxes, "0,1");
        SetDefault(EditorSetting.PlaytestMouseKnobs, false);
        SetDefault(EditorSetting.PlaytestKeyLaserSpeed, 4.0, 0.5, 20, 0.5);
        SetDefault(EditorSetting.PlaytestWindows, "46,92,138,84");
        SetDefault(EditorSetting.PlaytestHitsounds, true);
        SetDefault(EditorSetting.PlaytestAssistTick, false);
        SetDefault(EditorSetting.PlaytestGauge, GaugeType.Normal);
    }
}
