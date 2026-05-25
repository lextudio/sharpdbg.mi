// Tests using the mi-integration test fixture apps (TestApp1, TestApp2)
// Covers basic MI flows across multiple fixture programs
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_ImportedFixturesTests(ITestOutputHelper output)
{
    [Fact]
    public async Task TestApp1_Breakpoint_Hits()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp1", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp1");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("breakpoint-hit", stopped);

        await mi.SendAsync("3-gdb-exit", ct);
    }

    [Fact]
    public async Task TestApp2_Breakpoint_Hits()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp2", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp2");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("breakpoint-hit", stopped);

        await mi.SendAsync("3-gdb-exit", ct);
    }

    [Fact]
    public async Task TestApp_ThreadInfo_ReturnsThreads()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp1", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp1");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("3-thread-info", ct);
        var resp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", resp);
        Assert.Contains("threads=", resp);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task TestApp_StackListFrames_ReturnsFrames()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp1", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp1");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("3-stack-list-frames", ct);
        var resp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", resp);
        Assert.Contains("stack=", resp);
        Assert.Contains("frame=", resp);

        await mi.SendAsync("4-gdb-exit", ct);
    }
}
