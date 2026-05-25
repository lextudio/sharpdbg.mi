// Ported from Samsung/netcoredbg test-suite
// Original: netcoredbg/test-suite/MITestStepping/Program.cs
// Tests: exec-next (step over), exec-step (step into), exec-finish (step out)
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_SteppingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ExecNext_StepOver_Works()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppStepping", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppStepping");

        // BREAK_STEP_OVER is on Bar() call — line 17
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 17", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        // Step over — should stay in Main, not enter Bar
        await mi.SendAsync("3-exec-next", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("end-stepping-range", stopped);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task ExecStep_StepInto_Works()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppStepping", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppStepping");

        // BREAK_STEP_INTO is on Foo() call — line 13
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 13", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        // Step into Foo
        await mi.SendAsync("3-exec-step", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("end-stepping-range", stopped);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task ExecFinish_StepOut_Works()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppStepping", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppStepping");

        // Step into Foo, then step out back to Main
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 13", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("3-exec-step", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("4-exec-finish", ct);
        var stopped = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("end-stepping-range", stopped);

        await mi.SendAsync("5-gdb-exit", ct);
    }
}
