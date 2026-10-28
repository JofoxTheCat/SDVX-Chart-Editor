using System.Globalization;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using SDVXEditor.Core.Audio;
using SDVXEditor.Game.Configuration;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>
/// Timing → Audio Offset &amp; Hitsounds: the universal (per machine) audio offset, applied to everything
/// that is drawn and judged, and the hitsound skin (Auto = the active osu! skin, Built-in, or any folder
/// of the osu! Skins directory) with what each chart event resolved to. The offset part is a stub of the
/// wizard: entered by hand for now; a tap-along measurement will fill it in later.
/// </summary>
public partial class AudioOffsetDialog : DialogWindow
{
    private readonly HitsoundPlayer? hitsounds;

    public NumericUpDown OffsetInput { get; private set; } = null!;

    public ComboBox<string> SkinSelect { get; private set; } = null!;

    private readonly BindableDouble offset = new BindableDouble();
    private readonly Bindable<string> skin = new Bindable<string>(OsuSkinLocator.AutoSetting);
    private FillFlowContainer info = null!;

    public AudioOffsetDialog(HitsoundPlayer? hitsounds)
        : base("Audio Offset & Hitsounds", new Vector2(540, 340))
    {
        this.hitsounds = hitsounds;
    }

    [BackgroundDependencyLoader]
    private void load(EditorConfigManager config)
    {
        config.BindWith(EditorSetting.AudioOffset, offset);
        config.BindWith(EditorSetting.HitsoundSkinFolder, skin);

        var choices = new List<string> { OsuSkinLocator.AutoSetting, OsuSkinLocator.BuiltInSetting };
        choices.AddRange(OsuSkinLocator.ListSkins());
        if (!choices.Contains(skin.Value)) choices.Add(skin.Value);

        string? autoSkin = OsuSkinLocator.FindSkinFolder() is string f ? Path.GetFileName(f) : null;

        Body.Children = new Drawable[]
        {
            new FillFlowContainer
            {
                Position = new Vector2(12, 10),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
                Children = new Drawable[]
                {
                    DialogText.Label("Universal audio offset (ms), for this computer", bold: true),
                    DialogText.Label("Positive: the sound reaches you later than the picture (e.g. Bluetooth)."),
                    DialogText.Label("Notes, judgement line and playtest wait for it. 0 = trust the audio clock."),
                },
            },
            OffsetInput = new NumericUpDown
            {
                Position = new Vector2(12, 62),
                Width = 110,
                Current = offset,
                Increment = 1,
            },
            new DialogButton("Reset", () => offset.Value = 0)
            {
                Position = new Vector2(132, 62),
                Size = new Vector2(80, 23),
            },
            new FillFlowContainer
            {
                Position = new Vector2(12, 100),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
                Children = new Drawable[]
                {
                    DialogText.Label("Hitsound skin", bold: true),
                    DialogText.Label("Samples are read from your own osu! install, never copied."),
                },
            },
            SkinSelect = new ComboBox<string>(choices)
            {
                Position = new Vector2(12, 136),
                Width = 320,
                Formatter = v => v == OsuSkinLocator.AutoSetting ? $"Auto (osu! current skin{(autoSkin != null ? ": " + autoSkin : ", none found")})"
                    : v == OsuSkinLocator.BuiltInSetting ? "Built-in"
                    : v,
            },
            info = new FillFlowContainer
            {
                Position = new Vector2(12, 170),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 3),
            },
            new DialogButton("Close", Accept)
            {
                IsDefault = true,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-8, -8),
                Size = new Vector2(110, 30),
            },
        };

        SkinSelect.Current.BindTo(skin);
        if (hitsounds != null) hitsounds.SamplesResolved += refreshInfo;
        refreshInfo();
    }

    private void refreshInfo() => Schedule(() =>
    {
        info.Clear();
        if (hitsounds != null)
        {
            foreach (var r in hitsounds.Resolution)
                info.Add(DialogText.Label($"{HitsoundMap.KeyOf(r.Key)}: {r.Value}"));
            info.Add(DialogText.Label(string.Format(CultureInfo.InvariantCulture, "Started {0:0} ms ahead of their note (output latency, automatic).", hitsounds.LeadMs)));
        }

        info.Add(DialogText.Label("Offset: a tap-along measurement is planned; for now enter it by hand."));
    });

    protected override void Dispose(bool isDisposing)
    {
        if (hitsounds != null) hitsounds.SamplesResolved -= refreshInfo;
        base.Dispose(isDisposing);
    }
}
