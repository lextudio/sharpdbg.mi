// Ported from Samsung/netcoredbg test-suite
// Original: netcoredbg/test-suite/MITestVariables/Program.cs
// Tests: stack-list-variables, var-create, var-evaluate-expression for local variable reads
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_VariablesTests(ITestOutputHelper output)
{
    [Fact]
    public async Task StackListVariables_ReturnsLocals()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp1", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp1");

        // BREAKPOINT_BP is on i++ — line 12
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("3-stack-list-variables --thread 1 --frame 0 --all-values", ct);
        var vars = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", vars);
        Assert.Contains("variables=", vars);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task VarCreate_EvaluatesLocalVariable()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp1", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp1");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        // Evaluate local variable 'i'
        await mi.SendAsync("3-var-create - * i", ct);
        var resp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", resp);
        Assert.Contains("value=", resp);
        // i is initialized to 1 before the i++ line
        Assert.Contains("\"1\"", resp);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task VarEvaluate_ArithmeticExpression()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestApp1", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestApp1");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 12", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("3-var-evaluate-expression 1+2", ct);
        var resp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", resp);
        Assert.Contains("value=\"3\"", resp);

        await mi.SendAsync("4-gdb-exit", ct);
    }
}
