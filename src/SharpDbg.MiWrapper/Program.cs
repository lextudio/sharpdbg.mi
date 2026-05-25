using SharpDbg.MiWrapper;

// ── Argument parsing ─────────────────────────────────────────────────────────
string? sharpdbgPath = null;
string? engineLogPath = null;

foreach (var arg in args)
{
    if (arg.StartsWith("--sharpdbg="))
        sharpdbgPath = arg["--sharpdbg=".Length..];
    else if (arg.StartsWith("--engineLogging="))
        engineLogPath = arg["--engineLogging=".Length..];
}

// Locate sharpdbg: explicit > same directory > PATH
if (string.IsNullOrEmpty(sharpdbgPath))
{
    var selfDir = AppContext.BaseDirectory;
    var candidate = Path.Combine(selfDir, OperatingSystem.IsWindows() ? "sharpdbg.exe" : "sharpdbg");
    sharpdbgPath = File.Exists(candidate) ? candidate : "sharpdbg";
}

// ── Logging ──────────────────────────────────────────────────────────────────
StreamWriter? logWriter = null;
if (!string.IsNullOrEmpty(engineLogPath))
{
    var dir = Path.GetDirectoryName(engineLogPath);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    logWriter = new StreamWriter(engineLogPath, append: true) { AutoFlush = true };
}

void Log(string msg)
{
    logWriter?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {msg}");
}

// ── Startup ──────────────────────────────────────────────────────────────────
Log($"sharpdbg-mi starting. sharpdbg={sharpdbgPath}");

// Suppress Console.In/Out buffering
Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = System.Text.Encoding.UTF8;

using var dap = new DapClient(sharpdbgPath, engineLogPath is null ? null : engineLogPath + ".sharpdbg", Log);
var mi = new MiInterpreter(dap, Log) { Out = Console.Out };

// Signal readiness (some clients expect this)
Console.WriteLine("=thread-group-added,id=\"i1\"");
Console.Out.Flush();

// ── Main MI read loop ────────────────────────────────────────────────────────
string? line;
while ((line = Console.ReadLine()) != null)
{
    line = line.Trim();
    if (string.IsNullOrEmpty(line)) continue;
    Log($"[MI<] {line}");

    var cmd = MiCommand.Parse(line);
    await mi.HandleCommandAsync(cmd);

    // -gdb-exit terminates the loop
    if (cmd.Name == "gdb-exit") break;
    if (!dap.IsAlive) break;
}

Log("sharpdbg-mi exiting");
logWriter?.Dispose();
