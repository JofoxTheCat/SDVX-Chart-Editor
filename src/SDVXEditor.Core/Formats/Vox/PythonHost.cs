using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SDVXEditor.Core.Formats.Vox;

/// <summary>An error from the Python host, told apart by <see cref="Kind"/> rather than by its text.</summary>
public class PythonHostException : Exception
{
    /// <summary>
    /// Host-side kinds (<c>converter_missing</c>, <c>convert_failed</c>, <c>bad_params</c>, <c>id_required</c>, …)
    /// or client-side ones: <c>start_failed</c>, <c>crashed</c>, <c>timeout</c>, <c>cancelled</c>, <c>protocol</c>.
    /// </summary>
    public string Kind { get; }

    public JsonNode? Detail { get; }

    /// <summary>Last lines the host wrote to stderr — usually the actual reason of a crash.</summary>
    public IReadOnlyList<string> StderrTail { get; }

    public PythonHostException(string kind, string message, JsonNode? detail = null, IReadOnlyList<string>? stderrTail = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        Detail = detail;
        StderrTail = stderrTail ?? Array.Empty<string>();
    }

    public override string ToString()
        => StderrTail.Count == 0 ? base.ToString() : base.ToString() + Environment.NewLine + "stderr:" + Environment.NewLine + string.Join(Environment.NewLine, StderrTail);
}

/// <summary>
/// Client for a long-lived Python process speaking newline-delimited JSON over stdin/stdout
/// (<c>python/vox_host.py</c>). One call at a time: the host is single-threaded, so a timeout or a
/// cancellation cannot be delivered to it — the process is killed instead and the next call needs a new host.
/// </summary>
public sealed class PythonHost : IDisposable, IAsyncDisposable
{
    private const int stderrKeep = 80;

    private readonly Process process;
    private readonly SemaphoreSlim callLock = new(1, 1);
    private readonly object writeLock = new();
    private readonly object pendingLock = new();
    private readonly Queue<string> stderrTail = new();
    private readonly TaskCompletionSource<JsonObject> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<string>? log;
    private Pending? pending;
    private long nextId;
    private volatile bool exited;
    private string? exitReason;

    private sealed class Pending
    {
        public required long Id;
        public required TaskCompletionSource<JsonNode?> Tcs;
        public Action<string, JsonObject>? OnEvent;
    }

    /// <summary>The host's <c>ready</c> event (python version, converter version, load error).</summary>
    public JsonObject? ReadyInfo { get; private set; }

    public bool IsRunning => !exited;

    public int ProcessId { get; }

    public IReadOnlyList<string> StderrTail
    {
        get { lock (stderrTail) return stderrTail.ToArray(); }
    }

    private PythonHost(Process process, Action<string>? log)
    {
        this.process = process;
        this.log = log;
        ProcessId = process.Id;
    }

    /// <summary>Starts the host and waits for its <c>ready</c> event.</summary>
    public static async Task<PythonHost> StartAsync(PythonCommand python, string scriptPath, IEnumerable<string> scriptArgs,
                                                    TimeSpan readyTimeout, Action<string>? log = null, CancellationToken ct = default)
    {
        if (!File.Exists(scriptPath))
            throw new PythonHostException("start_failed", $"host script not found: {scriptPath}");
        var utf8 = new UTF8Encoding(false);
        var psi = new ProcessStartInfo(python.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(scriptPath))!,
        };
        foreach (var a in python.PrefixArgs) psi.ArgumentList.Add(a);
        // -E: ignore PYTHONPATH/PYTHONHOME of whatever environment started us. Not -I: that also hides
        // the user site-packages, where a system Python typically has Pillow.
        psi.ArgumentList.Add("-E");
        psi.ArgumentList.Add(scriptPath);
        foreach (var a in scriptArgs) psi.ArgumentList.Add(a);

        Process? p;
        try
        {
            p = Process.Start(psi);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            throw new PythonHostException("start_failed", $"cannot start {python}: {e.Message}", inner: e);
        }
        if (p == null) throw new PythonHostException("start_failed", $"cannot start {python}");

        var host = new PythonHost(p, log);
        host.run();
        try
        {
            host.ReadyInfo = await host.ready.Task.WaitAsync(readyTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            host.kill("no ready event");
            throw new PythonHostException("start_failed", $"the Python host did not report ready within {readyTimeout.TotalSeconds:0} s",
                                          stderrTail: host.StderrTail);
        }
        catch (OperationCanceledException)
        {
            host.kill("cancelled");
            throw;
        }
        catch (PythonHostException)
        {
            host.Dispose();
            throw;
        }
        return host;
    }

    private void run()
    {
        process.EnableRaisingEvents = true;
        _ = Task.Run(readStderr);
        _ = Task.Run(readStdout);
    }

    private async Task readStderr()
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lock (stderrTail)
                {
                    stderrTail.Enqueue(line);
                    while (stderrTail.Count > stderrKeep) stderrTail.Dequeue();
                }
                log?.Invoke(line);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    private async Task readStdout()
    {
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                if (line.Length == 0) continue;
                JsonObject? msg;
                try
                {
                    msg = JsonNode.Parse(line) as JsonObject;
                }
                catch (JsonException)
                {
                    msg = null;
                }
                if (msg == null)
                {
                    // Protocol violation: something wrote to the host's stdout. Keep it for diagnosis.
                    log?.Invoke("[stdout, not protocol] " + line);
                    lock (stderrTail) stderrTail.Enqueue("[stdout, not protocol] " + line);
                    continue;
                }
                dispatch(msg);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }

        // stdout closed = the host is gone (or going). Give stderr a moment to drain for the message.
        try { process.WaitForExit(2000); } catch (InvalidOperationException) { }
        exited = true;
        string code;
        try { code = process.HasExited ? process.ExitCode.ToString() : "?"; }
        catch (InvalidOperationException) { code = "?"; }
        var reason = exitReason != null
            ? new PythonHostException(exitReason == "timeout" ? "timeout" : exitReason == "cancelled" ? "cancelled" : "crashed",
                                      $"the Python host was stopped ({exitReason})", stderrTail: StderrTail)
            : new PythonHostException("crashed", $"the Python host exited unexpectedly (exit code {code})", stderrTail: StderrTail);
        ready.TrySetException(reason);
        Pending? p;
        lock (pendingLock)
        {
            p = pending;
            pending = null;
        }
        p?.Tcs.TrySetException(reason);
    }

    private void dispatch(JsonObject msg)
    {
        if (msg["event"] is JsonValue ev && ev.TryGetValue(out string? evName))
        {
            if (evName == "ready")
            {
                ready.TrySetResult(msg);
                return;
            }
            Pending? p;
            lock (pendingLock) p = pending;
            if (p?.OnEvent != null && idOf(msg) == p.Id)
            {
                try
                {
                    p.OnEvent(evName, msg);
                }
                catch (Exception e)
                {
                    // A UI bug must never break an export (same rule as ksh2vox's progress callback).
                    log?.Invoke($"[host client] event handler threw: {e.Message}");
                }
            }
            return;
        }

        long? id = idOf(msg);
        Pending? match;
        lock (pendingLock)
        {
            match = pending != null && id == pending.Id ? pending : null;
            if (match != null) pending = null;
        }
        if (match == null)
        {
            log?.Invoke("[host client] response for no pending call: " + msg.ToJsonString());
            return;
        }
        if (msg["error"] is JsonObject err)
        {
            match.Tcs.TrySetException(new PythonHostException(
                err["kind"]?.GetValue<string>() ?? "error",
                err["message"]?.GetValue<string>() ?? "unknown error",
                err["detail"]?.DeepClone(), StderrTail));
        }
        else
        {
            match.Tcs.TrySetResult(msg["result"]?.DeepClone());
        }
    }

    private static long? idOf(JsonObject msg)
        => msg["id"] is JsonValue v && v.TryGetValue(out long id) ? id : null;

    /// <summary>
    /// Calls one host method. <paramref name="onEvent"/> receives events tagged with this call's id
    /// (event name, whole message). On timeout or cancellation the host process is killed.
    /// </summary>
    public async Task<JsonNode?> CallAsync(string method, JsonObject? parameters = null, Action<string, JsonObject>? onEvent = null,
                                           TimeSpan? timeout = null, CancellationToken ct = default)
    {
        await callLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (exited) throw new PythonHostException("crashed", "the Python host is not running", stderrTail: StderrTail);
            long id = Interlocked.Increment(ref nextId);
            var p = new Pending { Id = id, Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously), OnEvent = onEvent };
            lock (pendingLock) pending = p;
            var req = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new JsonObject() };
            try
            {
                lock (writeLock)
                {
                    process.StandardInput.WriteLine(req.ToJsonString());
                    process.StandardInput.Flush();
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
                lock (pendingLock) pending = null;
                throw new PythonHostException("crashed", $"cannot write to the Python host: {e.Message}", stderrTail: StderrTail, inner: e);
            }

            try
            {
                return await (timeout is { } t ? p.Tcs.Task.WaitAsync(t, ct) : p.Tcs.Task.WaitAsync(ct)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                kill("timeout");
                throw new PythonHostException("timeout", $"'{method}' did not finish within {timeout!.Value.TotalSeconds:0} s; the host was stopped",
                                              stderrTail: StderrTail);
            }
            catch (OperationCanceledException)
            {
                kill("cancelled");
                throw;
            }
        }
        finally
        {
            callLock.Release();
        }
    }

    private void kill(string reason)
    {
        exitReason ??= reason;
        exited = true;
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    /// <summary>Asks the host to exit, waits briefly, then kills it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (!exited)
        {
            try
            {
                await CallAsync("shutdown", timeout: TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (PythonHostException) { }
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }
        exitReason ??= "disposed";
        kill("disposed");
        process.Dispose();
        callLock.Dispose();
    }

    public void Dispose()
    {
        if (!exited)
        {
            try
            {
                lock (writeLock)
                {
                    process.StandardInput.WriteLine("{\"id\":0,\"method\":\"shutdown\"}");
                    process.StandardInput.Flush();
                    process.StandardInput.Close();
                }
                process.WaitForExit(2000);
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
        }
        exitReason ??= "disposed";
        kill("disposed");
        process.Dispose();
    }
}
