// Ported from Samsung/netcoredbg test-suite
// Original: netcoredbg/test-suite/MIExampleTest/Program.cs
// Tests: break-insert, exec-run, stop-at-entry, break-delete, exec-continue, gdb-exit
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_LaunchAndBreakpointTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BreakInsert_ExecRun_StopsAtBreakpoint()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        var bpResp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", bpResp);
        Assert.Contains("bkpt=", bpResp);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("breakpoint-hit", stopped);

        await mi.SendAsync("3-exec-continue", ct);
        // process exits — wait for exited or another stopped
        await mi.WaitForAsync(l => l.Contains("exited") || l.Contains("*stopped"), ct: ct);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task StopAtEntry_Stops_BeforeFirstLine()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var appDll = MiProcessHelper.FixtureDll("TestApp");

        await mi.SendAsync($"1-exec-run --program=dotnet --args=\"{appDll}\" --stop-at-entry", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("*stopped", stopped);

        await mi.SendAsync("2-gdb-exit", ct);
    }

    [Fact]
    public async Task BreakDelete_Removes_Breakpoint()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        var bpResp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", bpResp);
        // Extract breakpoint id
        var idMatch = System.Text.RegularExpressions.Regex.Match(bpResp, @"number=""(\d+)""");
        var bpId = idMatch.Success ? idMatch.Groups[1].Value : "1";

        await mi.SendAsync($"2-break-delete {bpId}", ct);
        var delResp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", delResp);

        // Program should now run to completion without stopping at the breakpoint
        await mi.SendAsync($"3-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        var result = await mi.WaitForAsync(l => l.Contains("exited") || l.Contains("*stopped"), ct: ct);
        // If we stopped, it's not a breakpoint-hit (it would be exited or entry)
        if (result.Contains("*stopped"))
            Assert.DoesNotContain("breakpoint-hit", result);

        await mi.SendAsync("4-gdb-exit", ct);
    }
}
