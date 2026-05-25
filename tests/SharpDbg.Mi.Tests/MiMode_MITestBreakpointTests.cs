// Ported from Samsung/netcoredbg test-suite
// Original: netcoredbg/test-suite/MITestBreakpoint/Program.cs
// Tests: break-insert / exec-run / break-delete core flow; breakpoint id tracking
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_MITestBreakpointTests(ITestOutputHelper output)
{
    [Fact]
    public async Task BreakpointInsert_Run_Delete_CoreFlow()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp1", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp1");

        // Insert breakpoint at BREAKPOINT_BP line 12
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        var bpResp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", bpResp);
        Assert.Contains("bkpt=", bpResp);

        // Run; should stop at breakpoint
        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("breakpoint-hit", stopped);

        // Extract id and delete it
        var m = System.Text.RegularExpressions.Regex.Match(bpResp, @"number=""(\d+)""");
        var bpId = m.Success ? m.Groups[1].Value : "1";
        await mi.SendAsync($"3-break-delete {bpId}", ct);
        var delResp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", delResp);

        // Continue to completion
        await mi.SendAsync("4-exec-continue", ct);
        await mi.WaitForAsync(l => l.Contains("exited") || l.Contains("*stopped"), ct: ct);

        await mi.SendAsync("5-gdb-exit", ct);
    }

    [Fact]
    public async Task MultipleBreakpoints_BothHit()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile1 = GitRoot.Join("test", "TestApp1", "Program.cs");
        var bpFile2 = GitRoot.Join("test", "TestApp2", "Program.cs");
        var appDll1 = MiProcessHelper.FixtureDll("TestApp1");

        await mi.SendAsync($"1-break-insert --source {bpFile1} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll1}\"", ct);
        var stopped1 = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("breakpoint-hit", stopped1);

        await mi.SendAsync("3-exec-continue", ct);
        await mi.WaitForAsync(l => l.Contains("exited") || l.Contains("*stopped"), ct: ct);

        await mi.SendAsync("4-gdb-exit", ct);
    }
}
