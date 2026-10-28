using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SDVXEditor.Core.Formats.Vox;

/// <summary>How to start a Python interpreter: executable plus fixed leading arguments (<c>py -3</c>).</summary>
public sealed record PythonCommand(string FileName, IReadOnlyList<string> PrefixArgs, string Source)
{
    public override string ToString() => PrefixArgs.Count == 0 ? FileName : $"{FileName} {string.Join(' ', PrefixArgs)}";
}

/// <summary>Result of <see cref="PythonLocator.Find"/>: the chosen interpreter, or why none was usable.</summary>
public sealed record PythonLookup(PythonCommand? Command, Version? Version, IReadOnlyList<string> Tried)
{
    public bool Found => Command != null;
}

/// <summary>
/// Finds a Python interpreter for the VOX host. Order: explicit setting &gt; the bundled embeddable
/// runtime (<c>python/runtime/python.exe</c>, fetched by <c>tools/fetch_python.ps1</c>) &gt; <c>py -3</c>
/// and <c>python</c> on PATH (Windows) or <c>python3</c>/<c>python</c> (Linux/macOS).
/// Every candidate is probed with <c>--version</c>: the Windows Store "python.exe" alias exists on PATH
/// but only opens the Store, so mere existence proves nothing.
/// </summary>
public static class PythonLocator
{
    /// <summary>ksh2vox uses PEP 604 annotations (<c>str | None</c>) evaluated at def time.</summary>
    public static readonly Version MinimumVersion = new(3, 10);

    public const string BundledRuntimeRelative = "python/runtime/python.exe";

    private static readonly bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static IEnumerable<PythonCommand> Candidates(string? setting, string? appBaseDir = null)
    {
        if (!string.IsNullOrWhiteSpace(setting))
        {
            // An explicit setting is the only candidate: silently falling back would run a
            // different interpreter than the one the user chose.
            yield return new PythonCommand(setting.Trim().Trim('"'), Array.Empty<string>(), "setting");
            yield break;
        }
        if (isWindows && FindUpwards(appBaseDir ?? AppContext.BaseDirectory, BundledRuntimeRelative) is { } bundled)
            yield return new PythonCommand(bundled, Array.Empty<string>(), "bundled");
        if (isWindows)
        {
            if (FindOnPath("py.exe") is { } py) yield return new PythonCommand(py, new[] { "-3" }, "py launcher");
            if (FindOnPath("python.exe") is { } python) yield return new PythonCommand(python, Array.Empty<string>(), "PATH");
        }
        else
        {
            if (FindOnPath("python3") is { } p3) yield return new PythonCommand(p3, Array.Empty<string>(), "PATH");
            if (FindOnPath("python") is { } p) yield return new PythonCommand(p, Array.Empty<string>(), "PATH");
        }
    }

    /// <summary>First candidate that runs and is at least <see cref="MinimumVersion"/>.</summary>
    public static PythonLookup Find(string? setting, string? appBaseDir = null, TimeSpan? probeTimeout = null)
    {
        var tried = new List<string>();
        foreach (var cmd in Candidates(setting, appBaseDir))
        {
            var v = Probe(cmd, probeTimeout ?? TimeSpan.FromSeconds(10), out string outcome);
            tried.Add($"{cmd} ({cmd.Source}): {outcome}");
            if (v != null && v >= MinimumVersion) return new PythonLookup(cmd, v, tried);
        }
        if (tried.Count == 0) tried.Add("no Python found (no setting, no bundled runtime, nothing on PATH)");
        return new PythonLookup(null, null, tried);
    }

    /// <summary>Runs <c>--version</c>; returns the version or null, with a readable outcome.</summary>
    public static Version? Probe(PythonCommand cmd, TimeSpan timeout, out string outcome)
    {
        try
        {
            var psi = new ProcessStartInfo(cmd.FileName)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in cmd.PrefixArgs) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            if (p == null)
            {
                outcome = "could not start";
                return null;
            }
            var so = p.StandardOutput.ReadToEndAsync();
            var se = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { p.Kill(true); } catch (InvalidOperationException) { }
                outcome = "timed out";
                return null;
            }
            string text = (so.Result + " " + se.Result).Trim();
            var m = Regex.Match(text, @"Python (\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success)
            {
                outcome = $"not a Python ({(text.Length > 80 ? text[..80] : text)})";
                return null;
            }
            var v = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                                m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
            outcome = v >= MinimumVersion ? $"Python {v}" : $"Python {v} is older than {MinimumVersion}";
            return v;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            outcome = $"cannot run: {e.Message}";
            return null;
        }
    }

    /// <summary>Full path of an executable on PATH, or null.</summary>
    public static string? FindOnPath(string exe)
    {
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            try
            {
                string p = Path.Combine(dir.Trim('"'), exe);
                if (File.Exists(p)) return p;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>
    /// Looks for <paramref name="relative"/> in <paramref name="start"/> and each parent directory.
    /// In a dev checkout the app runs from <c>bin/Debug/net9.0</c>, several levels below the repo root.
    /// </summary>
    public static string? FindUpwards(string start, string relative, int maxLevels = 8)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        for (int i = 0; dir != null && i <= maxLevels; i++, dir = dir.Parent)
        {
            string p = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p) || Directory.Exists(p)) return p;
        }
        return null;
    }
}
