// Ported from Samsung/netcoredbg test-suite
// Original: netcoredbg/test-suite/MITestEvaluate/Program.cs
// Tests: var-evaluate-expression for decimals, arrays, arithmetic, strings
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_EvaluateTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Evaluate_ArithmeticExpression()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppExpression");

        // BREAK1 is at: int c = tc.b + b;
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 28", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        // a=10, b=11 → a+b should be 21
        await mi.SendAsync("3-var-evaluate-expression a+b", ct);
        var resp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", resp);
        Assert.Contains("value=\"21\"", resp);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Evaluate_ArrayIndexing()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppExpression");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 28", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        // array1 = {10,20,30,40,50}, index 2 → 30
        await mi.SendAsync("3-var-evaluate-expression array1[2]", ct);
        var resp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", resp);
        Assert.Contains("value=\"30\"", resp);

        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Evaluate_StringConcatenation()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;

        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        var appDll = MiProcessHelper.FixtureDll("TestAppExpression");

        await mi.SendAsync($"1-break-insert --source {bpFile} --line 28", ct);
        await mi.WaitForResultAsync(ct: ct);

        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);

        await mi.SendAsync("3-var-evaluate-expression str1+str2", ct);
        var resp = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("^done", resp);
        Assert.Contains("string1string2", resp);

        await mi.SendAsync("4-gdb-exit", ct);
    }
}
