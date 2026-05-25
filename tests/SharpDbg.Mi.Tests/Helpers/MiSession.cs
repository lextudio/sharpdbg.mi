using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SharpDbg.Mi.Tests.Helpers;

/// <summary>
/// Thin wrapper around a sharpdbg-mi process that sends MI commands and reads MI output.
/// </summary>
public sealed class MiSession : IAsyncDisposable
{
    private readonly Process _process;
    public StreamWriter Writer { get; }
    public StreamReader Reader { get; }
    private readonly ITestOutputHelper? _output;

    private MiSession(Process process, ITestOutputHelper? output)
    {
        _process = process;
        Writer = process.StandardInput;
        Reader = process.StandardOutput;
        _output = output;
    }

    public static async Task<MiSession> StartAsync(ITestOutputHelper? output = null, CancellationToken ct = default)
    {
        var process = MiProcessHelper.Start();
        var session = new MiSession(process, output);
        // Drain stderr in background
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync(ct)) != null)
                output?.WriteLine($"[stderr] {line}");
        }, ct);
        // Wait for the readiness banner
        await session.ReadLineAsync(ct: ct);
        return session;
    }

    public async Task SendAsync(string command, CancellationToken ct = default)
    {
        _output?.WriteLine($"[MI>] {command}");
        await Writer.WriteLineAsync(command.AsMemory(), ct);
        await Writer.FlushAsync(ct);
    }

    public async Task<string> ReadLineAsync(int timeoutMs = 15_000, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var line = await Reader.ReadLineAsync(cts.Token)
            ?? throw new EndOfStreamException("sharpdbg-mi closed stdout unexpectedly");
        _output?.WriteLine($"[MI<] {line}");
        return line;
    }

    /// <summary>
    /// Read lines until a line matching <paramref name="predicate"/> is found. Returns that line.
    /// </summary>
    public async Task<string> WaitForAsync(Func<string, bool> predicate, int timeoutMs = 20_000, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var remaining = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            var line = await ReadLineAsync(timeoutMs: Math.Max(remaining, 1000), ct: ct);
            if (predicate(line)) return line;
        }
        throw new TimeoutException($"Timed out waiting for MI output after {timeoutMs}ms");
    }

    /// <summary>Read until a *stopped or ^done/^error result appears.</summary>
    public Task<string> WaitForStoppedAsync(int timeoutMs = 20_000, CancellationToken ct = default) =>
        WaitForAsync(l => l.StartsWith("*stopped") || l.StartsWith("^error"), timeoutMs, ct);

    /// <summary>Read until a result record (^done / ^running / ^error) appears.</summary>
    public Task<string> WaitForResultAsync(int timeoutMs = 10_000, CancellationToken ct = default) =>
        WaitForAsync(l => Regex.IsMatch(l, @"^\d*\^(done|running|error|exit)"), timeoutMs, ct);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Writer.WriteLineAsync("-gdb-exit");
            await Writer.FlushAsync();
        }
        catch { /* already dead */ }
        try { _process.Kill(entireProcessTree: true); } catch { }
        _process.Dispose();
    }
}
