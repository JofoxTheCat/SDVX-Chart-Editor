using osu.Framework;
using osu.Framework.Platform;
using SDVXEditor.Game;

namespace SDVXEditor.Desktop;

public static class Program
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "AttachConsole")]
    private static extern bool attachConsole(int processId);

    [STAThread]
    public static int Main(string[] args)
    {
        StartupOptions options;

        try
        {
            options = StartupArgs.Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine(StartupArgs.Usage);
            return 2;
        }

        // The host keeps framework.ini in the user storage (Windows: %APPDATA%, Linux: ~/.local/share).
        options = options with
        {
            FirstRun = !new[] { Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData }
                .Any(f => File.Exists(Path.Combine(Environment.GetFolderPath(f), "SDVXEditor", "framework.ini"))),
        };

        // Alt alone must never put the window into the Windows menu loop (Alt can be the compose hold modifier): SDL3
        // swallows SC_KEYMENU unless this hint is "1"; it is the default, set explicitly so no environment can flip it.
        Environment.SetEnvironmentVariable("SDL_WINDOWS_ENABLE_MENU_MNEMONICS", "0");

        if (!options.SmokeTest)
        {
            using (GameHost host = Host.GetSuitableDesktopHost("SDVXEditor"))
            using (var game = new EditorGame(options))
                host.Run(game);

            return 0;
        }

        return runSmokeTest(options);
    }

    /// <summary>
    /// --smoke-test: exit code 0 only when the test ran to its end, 1 on any failure (the host rethrows an
    /// exception from the update thread here) or when the window was closed early; a one-line result goes to
    /// stdout (pass) or stderr (fail) as the last line, so scripts and CI can grep it.
    /// </summary>
    private static int runSmokeTest(StartupOptions options)
    {
        // a WinExe has no console: attach to the parent's (cmd, PowerShell) so the result line is visible there;
        // redirected output (pipes, files, Git Bash) works either way
        if (OperatingSystem.IsWindows() && !Console.IsOutputRedirected)
            attachConsole(-1);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        GameHost? host = null;
        EditorGame? game = null;
        int code;
        string line;

        try
        {
            // Own storage (%APPDATA%\SDVXEditor-smoke): smoke runs used to share editor.ini/framework.ini with
            // the user's editor, so a run in the background reset his hitsound skin, volume and paths.
            // Smoke runs open in the background while the user works or plays: SDL must not activate (focus) the window.
            Environment.SetEnvironmentVariable("SDL_WINDOW_ACTIVATE_WHEN_SHOWN", "0");
            Environment.SetEnvironmentVariable("SDL_WINDOW_ACTIVATE_WHEN_RAISED", "0");
            // SDVXEDITOR_SMOKE_STORAGE=<suffix> gives parallel runs separate storages (SDVXEditor-smoke-<suffix>):
            // every run deletes its storage's editor.ini at start, so two runs sharing one disturbed each other.
            string? storageSuffix = Environment.GetEnvironmentVariable("SDVXEDITOR_SMOKE_STORAGE");
            host = Host.GetSuitableDesktopHost(string.IsNullOrWhiteSpace(storageSuffix)
                ? "SDVXEditor-smoke"
                : "SDVXEditor-smoke-" + string.Concat(storageSuffix.Trim().Where(char.IsLetterOrDigit)));
            game = new EditorGame(options);
            host.Run(game);

            (code, line) = game.SmokeTestPassed
                ? (0, FormattableString.Invariant($"SMOKE PASSED in {watch.Elapsed.TotalSeconds:0.0} s ({game.SmokeTestSummary})"))
                : (1, FormattableString.Invariant($"SMOKE FAILED after {watch.Elapsed.TotalSeconds:0.0} s: the game exited before the smoke test finished ({game.SmokeTestSummary})"));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            string message = (e as EditorGame.SmokeTestException)?.Message ?? $"{e.GetType().Name}: {e.Message}";
            (code, line) = (1, FormattableString.Invariant($"SMOKE FAILED after {watch.Elapsed.TotalSeconds:0.0} s: {message}"));
        }

        // Disposal can still hang (it once deadlocked on a screen whose async load waited for the stopped audio
        // thread): a smoke run must end, so a watchdog reports that and exits instead of leaving a frozen window.
        var disposed = new ManualResetEventSlim();
        new Thread(() =>
        {
            if (disposed.Wait(TimeSpan.FromSeconds(20))) return;
            Console.Error.WriteLine(line);
            Console.Error.WriteLine("SMOKE FAILED: shutdown hung for 20 s after the game stopped (dispose deadlock)");
            Environment.Exit(1);
        }) { IsBackground = true, Name = "smoke shutdown watchdog" }.Start();

        try
        {
            game?.Dispose();
            host?.Dispose();
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            if (code == 0) (code, line) = (1, $"SMOKE FAILED: disposing the game threw {e.GetType().Name}: {e.Message}");
        }

        disposed.Set();
        (code == 0 ? Console.Out : Console.Error).WriteLine(line);
        Console.Out.Flush();
        Console.Error.Flush();

        // the result is out: end the process here even if some library left a foreground thread running
        Environment.Exit(code);
        return code;
    }
}
