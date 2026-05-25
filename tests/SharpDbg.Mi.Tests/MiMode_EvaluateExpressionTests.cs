// Ported from Samsung/netcoredbg test-suite
// Original: netcoredbg/test-suite/MITestExpression/Program.cs
// Tests: expression evaluation (a+b, tc.a+b, str1+str2, d+a, a+1, static helpers, bool, nullable)
using SharpDbg.Mi.Tests.Helpers;

namespace SharpDbg.Mi.Tests;

public class MiMode_EvaluateExpressionTests(ITestOutputHelper output)
{
    private async Task StopAtBreak1Async(MiSession mi, string bpFile, string appDll, CancellationToken ct)
    {
        // BREAK1: int c = tc.b + b; (line 28)
        await mi.SendAsync($"1-break-insert --source {bpFile} --line 28", ct);
        await mi.WaitForResultAsync(ct: ct);
        await mi.SendAsync($"2-exec-run --program=dotnet --args=\"{appDll}\"", ct);
        await mi.WaitForStoppedAsync(ct: ct);
    }

    [Fact]
    public async Task Expression_APlusB()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;
        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        await StopAtBreak1Async(mi, bpFile, MiProcessHelper.FixtureDll("TestAppExpression"), ct);

        await mi.SendAsync("3-var-evaluate-expression a+b", ct);
        var r = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("value=\"21\"", r);
        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Expression_TcAPlusB()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;
        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        await StopAtBreak1Async(mi, bpFile, MiProcessHelper.FixtureDll("TestAppExpression"), ct);

        // tc = new TestStruct(a+1, b) = TestStruct(11, 11), so tc.a+b = 11+11 = 22
        await mi.SendAsync("3-var-evaluate-expression tc.a+b", ct);
        var r = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("value=\"22\"", r);
        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Expression_Str1PlusStr2()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;
        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        await StopAtBreak1Async(mi, bpFile, MiProcessHelper.FixtureDll("TestAppExpression"), ct);

        await mi.SendAsync("3-var-evaluate-expression str1+str2", ct);
        var r = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("string1string2", r);
        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Expression_APlusOne()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;
        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        await StopAtBreak1Async(mi, bpFile, MiProcessHelper.FixtureDll("TestAppExpression"), ct);

        await mi.SendAsync("3-var-evaluate-expression a+1", ct);
        var r = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("value=\"11\"", r);
        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Expression_StaticProperty_Greeting()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;
        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        await StopAtBreak1Async(mi, bpFile, MiProcessHelper.FixtureDll("TestAppExpression"), ct);

        await mi.SendAsync("3-var-evaluate-expression Program.Greeting", ct);
        var r = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("hello", r);
        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Expression_BoolFlag()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;
        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        await StopAtBreak1Async(mi, bpFile, MiProcessHelper.FixtureDll("TestAppExpression"), ct);

        await mi.SendAsync("3-var-evaluate-expression isTrue", ct);
        var r = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("true", r);
        await mi.SendAsync("4-gdb-exit", ct);
    }

    [Fact]
    public async Task Expression_NullCoalesce()
    {
        await using var mi = await MiSession.StartAsync(output);
        var ct = TestContext.Current.CancellationToken;
        var bpFile = GitRoot.Join("test", "TestAppExpression", "Program.cs");
        await StopAtBreak1Async(mi, bpFile, MiProcessHelper.FixtureDll("TestAppExpression"), ct);

        // optionalValue is null, fallbackValue is 5 → optionalValue ?? fallbackValue = 5
        await mi.SendAsync("3-var-evaluate-expression optionalValue??fallbackValue", ct);
        var r = await mi.WaitForResultAsync(ct: ct);
        Assert.Contains("value=\"5\"", r);
        await mi.SendAsync("4-gdb-exit", ct);
    }
}
