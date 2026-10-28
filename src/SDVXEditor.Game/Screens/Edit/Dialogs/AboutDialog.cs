using System.Reflection;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using SDVXEditor.Game.Graphics.UserInterface.Dialog;

namespace SDVXEditor.Game.Screens.Edit.Dialogs;

/// <summary>Help → About: version, runtime and the licences of what ships with the editor (docs/FONTS.md has the font sources).</summary>
public partial class AboutDialog : DialogWindow
{
    /// <summary>The editor version (informational version without a build-metadata suffix).</summary>
    public static string Version
    {
        get
        {
            var asm = typeof(AboutDialog).Assembly;
            string? v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return v?.Split('+')[0] ?? asm.GetName().Version?.ToString(3) ?? "?";
        }
    }

    /// <summary>The lines the dialog shows (smoke test checks the licences are there).</summary>
    public static IReadOnlyList<string> Lines => new[]
    {
        $"SDVX Chart Editor {Version}",
        $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription}",
        "",
        "Third-party components:",
        "osu-framework (ppy Pty Ltd) — MIT licence",
        "Roboto (Christian Robertson / Google) — Apache License 2.0",
        "Noto Sans, Noto Sans JP (Google / Adobe) — SIL Open Font License 1.1",
        "ImageSharp (Six Labors) — Six Labors Split License 1.0 (Apache 2.0 terms)",
        "BASS audio library (un4seen) via ManagedBass — free for non-commercial use",
        "SDL3 (libsdl.org) — zlib licence",
        "",
        "VOX export runs ksh2vox (SDVX-Chart-Converter) when it is installed.",
        "Charts and songs belong to their authors.",
    };

    public AboutDialog()
        : base("About SDVX Chart Editor", new Vector2(520, 24 + Lines.Count * 18 + 50))
    {
    }

    [BackgroundDependencyLoader]
    private void load()
    {
        var flow = new FillFlowContainer
        {
            Position = new Vector2(14, 12),
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 2),
        };
        for (int i = 0; i < Lines.Count; i++)
            flow.Add(DialogText.Label(Lines[i].Length == 0 ? " " : Lines[i], bold: i == 0));

        Body.Children = new Drawable[]
        {
            flow,
            new DialogButton("OK", Accept)
            {
                IsDefault = true,
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                Position = new Vector2(-12, -10),
                Size = new Vector2(100, 28),
            },
        };
    }
}
