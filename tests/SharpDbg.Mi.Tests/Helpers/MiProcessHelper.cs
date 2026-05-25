using System.Diagnostics;

namespace SharpDbg.Mi.Tests.Helpers;

/// <summary>
/// Launches sharpdbg-mi (the MI-to-DAP wrapper) as a child process for integration tests.
/// sharpdbg-mi in turn launches sharpdbg for the actual debug engine.
/// </summary>
public static class MiProcessHelper
{
    // sharpdbg-mi binary produced by SharpDbg.MiWrapper build
    private static readonly string MiWrapperExe = GitRoot.Join(
        "artifacts", "bin", "SharpDbg.MiWrapper", "debug",
        OperatingSystem.IsWindows() ? "sharpdbg-mi.exe" : "sharpdbg-mi");

    // sharpdbg CLI binary (the DAP server that sharpdbg-mi drives)
    private static readonly string SharpDbgExe = GitRoot.Join(
        "sharpdbg", "artifacts", "bin", "SharpDbg.Cli", "debug",
        OperatingSystem.IsWindows() ? "SharpDbg.Cli.exe" : "SharpDbg.Cli");

    public static Process Start()
    {
        if (!File.Exists(MiWrapperExe))
            throw new FileNotFoundException("sharpdbg-mi not found — build SharpDbg.MiWrapper first", MiWrapperExe);
        if (!File.Exists(SharpDbgExe))
            throw new FileNotFoundException("sharpdbg not found — build SharpDbg.Cli first", SharpDbgExe);

        var psi = new ProcessStartInfo(MiWrapperExe, $"--sharpdbg=\"{SharpDbgExe}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start sharpdbg-mi");
        return process;
    }

    /// <summary>Path to a test fixture app's DLL given its project name.</summary>
    public static string FixtureDll(string appName) =>
        GitRoot.Join("artifacts", "bin", appName, "debug", $"{appName}.dll");
}
