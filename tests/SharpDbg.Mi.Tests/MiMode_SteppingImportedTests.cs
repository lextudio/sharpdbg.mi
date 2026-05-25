// Ported from Samsung/netcoredbg test-suite (stepping-focused imported fixtures)
// Original: netcoredbg/test-suite/MITestStepping/Program.cs (additional scenarios)
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_SteppingImportedTests(ITestOutputHelper output)
{
    [Fact]
    public async Task StepIn_And_StepOver_Sequence()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppStepping", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppStepping");

        // Break on the Foo() call (line 13), then step in, then step over inside Foo
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 13", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        // Step into Foo
        await mi.SendAsync("3-exec-step", ct);
        var inFoo = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("end-stepping-range", inFoo);

        // Step over one line inside Foo (Console.WriteLine)
        await mi.SendAsync("4-exec-next", ct);
        var afterStep = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("end-stepping-range", afterStep);

        await mi.SendAsync("5-gdb-exit", ct);
    }

    [Fact]
    public async Task MultiStep_TraversesLines()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppStepping", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppStepping");

        // Break at Bar() call (line 17) — step over twice to get through x=1 and x+=2
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 17", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("3-exec-step", ct);  // step into Bar
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("4-exec-next", ct);  // step over x = 1
        var s1 = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("end-stepping-range", s1);

        await mi.SendAsync("5-exec-next", ct);  // step over x += 2
        var s2 = await mi.WaitForStoppedAsync(ct: ct);
        Assert.Contains("end-stepping-range", s2);

        await mi.SendAsync("6-gdb-exit", ct);
    }
}
