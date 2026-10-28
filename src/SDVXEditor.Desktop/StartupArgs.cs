using SDVXEditor.Core.Charts;
using SDVXEditor.Game;

namespace SDVXEditor.Desktop;

/// <summary>Minimal CLI parser; unknown arguments are an error so typos surface.</summary>
public static class StartupArgs
{
    public const string Usage =
        "usage: SDVXEditor [--launcher-config <path>] [--songs <dir>] [--open <song folder> [--chart <slot>]] [--import <file>] [--smoke-test]\n"
        + "  --chart: 0-4 or nov/adv/exh/inf/mxm (default: the highest difficulty of the song)\n"
        + "  --import: a .svz package, .zip, song folder, .ksh/.kson or audio file to import on start";

    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        string? launcherConfig = null;
        string? songs = null;
        string? open = null;
        int? chart = null;
        bool smokeTest = false;
        string? import = null;

        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--launcher-config":
                    launcherConfig = value(args, ref i);
                    break;

                case "--songs":
                    songs = value(args, ref i);
                    break;

                case "--open":
                    open = value(args, ref i);
                    if (!Directory.Exists(open))
                        throw new ArgumentException($"--open: folder not found: {open}");
                    break;

                case "--chart":
                    chart = parseSlot(value(args, ref i));
                    break;

                case "--import":
                    import = value(args, ref i);
                    if (!File.Exists(import) && !Directory.Exists(import))
                        throw new ArgumentException($"--import: not found: {import}");
                    break;

                case "--smoke-test":
                    smokeTest = true;
                    break;

                default:
                    throw new ArgumentException($"unknown argument: {args[i]}");
            }
        }

        if (chart != null && open == null)
            throw new ArgumentException("--chart needs --open");

        return new StartupOptions(launcherConfig, songs, smokeTest, open == null ? null : Path.GetFullPath(open), chart)
        {
            ImportPath = import == null ? null : Path.GetFullPath(import),
        };
    }

    private static int parseSlot(string text)
    {
        if (int.TryParse(text, out int slot) && slot is >= 0 and <= 4)
            return slot;

        return Difficulty.FromName(text) ?? throw new ArgumentException($"--chart: not a difficulty slot: {text}");
    }

    private static string value(IReadOnlyList<string> args, ref int i)
    {
        if (i + 1 >= args.Count)
            throw new ArgumentException($"{args[i]} needs a value");

        return args[++i];
    }
}
