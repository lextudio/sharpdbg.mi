using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SharpDbg.MiWrapper;

/// <summary>
/// Launches sharpdbg as a child process and speaks DAP over its stdin/stdout pipes.
/// Provides async request/response correlation and an event callback.
/// </summary>
internal sealed class DapClient : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _writer;
    private readonly StreamReader _reader;
    private int _seq = 0;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly Action<string> _log;

    public event Action<string, JsonObject?>? OnEvent;

    public DapClient(string sharpdbgPath, string? engineLogPath, Action<string> log)
    {
        _log = log;
        var psi = new ProcessStartInfo(sharpdbgPath, "--interpreter=vscode")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!string.IsNullOrEmpty(engineLogPath))
            psi.ArgumentList.Add($"--engineLogging={engineLogPath}");

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start sharpdbg");
        _writer = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = false };
        _reader = new StreamReader(_process.StandardOutput.BaseStream, Encoding.UTF8);

        // Drain stderr to the log
        _process.ErrorDataReceived += (_, e) => { if (e.Data != null) log($"[sharpdbg stderr] {e.Data}"); };
        _process.BeginErrorReadLine();

        // Background reader loop
        Task.Run(ReadLoop);
    }

    private async Task ReadLoop()
    {
        try
        {
            while (!_process.HasExited)
            {
                var msg = await ReadMessageAsync();
                if (msg is null) break;
                var type = msg["type"]?.GetValue<string>();
                if (type == "response")
                {
                    var reqSeq = msg["request_seq"]?.GetValue<int>() ?? 0;
                    if (_pending.TryRemove(reqSeq, out var tcs))
                        tcs.TrySetResult(msg);
                }
                else if (type == "event")
                {
                    var eventName = msg["event"]?.GetValue<string>() ?? "";
                    var body = msg["body"]?.AsObject();
                    OnEvent?.Invoke(eventName, body);
                }
            }
        }
        catch (Exception ex)
        {
            _log($"[DapClient] ReadLoop error: {ex.Message}");
        }
        // Drain pending waiters on disconnect
        foreach (var tcs in _pending.Values)
            tcs.TrySetCanceled();
    }

    private async Task<JsonObject?> ReadMessageAsync()
    {
        // Read headers until blank line
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await _reader.ReadLineAsync();
            if (line is null) return null;
            if (line.Length == 0) break;
            var idx = line.IndexOf(':');
            if (idx > 0)
                headers[line[..idx].Trim()] = line[(idx + 1)..].Trim();
        }
        if (!headers.TryGetValue("Content-Length", out var lenStr) || !int.TryParse(lenStr, out var len))
            return null;

        var buf = new char[len];
        var read = 0;
        while (read < len)
            read += await _reader.ReadAsync(buf, read, len - read);

        var json = new string(buf);
        _log($"[DAP<] {json}");
        return JsonNode.Parse(json)?.AsObject();
    }

    public async Task<JsonObject> SendRequestAsync(string command, JsonObject? args = null, CancellationToken ct = default)
    {
        var seq = Interlocked.Increment(ref _seq);
        var msg = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command,
        };
        if (args != null)
            msg["arguments"] = args;

        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[seq] = tcs;
        ct.Register(() => tcs.TrySetCanceled());

        await WriteMessageAsync(msg);
        return await tcs.Task;
    }

    private async Task WriteMessageAsync(JsonObject msg)
    {
        var json = msg.ToJsonString();
        _log($"[DAP>] {json}");
        var bytes = Encoding.UTF8.GetByteCount(json);
        await _writer.WriteAsync($"Content-Length: {bytes}\r\n\r\n{json}");
        await _writer.FlushAsync();
    }

    public bool IsAlive => !_process.HasExited;

    public void Dispose()
    {
        try { _process.Kill(); } catch { }
        _process.Dispose();
    }
}
