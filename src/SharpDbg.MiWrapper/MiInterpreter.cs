using System.Text;
using System.Text.Json.Nodes;

namespace SharpDbg.MiWrapper;

/// <summary>
/// Translates MI commands → DAP requests and DAP events → MI output.
/// All MI output is written to <see cref="Out"/>.
/// </summary>
internal sealed class MiInterpreter
{
    public TextWriter Out { get; set; } = Console.Out;

    private readonly DapClient _dap;
    private readonly Action<string> _log;

    // Per-file breakpoint tracking (DAP setBreakpoints replaces the full list for a file)
    private readonly Dictionary<string, List<BreakpointEntry>> _fileBreakpoints = new(StringComparer.OrdinalIgnoreCase);
    private int _nextBpId = 1;
    private int _activeThreadId = 1;

    private readonly TaskCompletionSource _initializedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private record BreakpointEntry(int Id, string File, int Line, string? Condition);

    public MiInterpreter(DapClient dap, Action<string> log)
    {
        _dap = dap;
        _log = log;
        dap.OnEvent += HandleDapEvent;
    }

    // ── DAP event → MI output ────────────────────────────────────────────────

    private void HandleDapEvent(string eventName, JsonObject? body)
    {
        switch (eventName)
        {
            case "initialized":
                _initializedTcs.TrySetResult();
                break;

            case "stopped":
            {
                var reason = body?["reason"]?.GetValue<string>() ?? "signal-received";
                var threadId = body?["threadId"]?.GetValue<int>() ?? _activeThreadId;
                _activeThreadId = threadId;
                var miReason = reason switch
                {
                    "breakpoint" => "breakpoint-hit",
                    "step" => "end-stepping-range",
                    "exception" => "exception-received",
                    "pause" or "entry" => "signal-received",
                    "goto" => "location-reached",
                    _ => reason,
                };
                // Try to get frame info from body
                var frame = body?["text"]?.GetValue<string>();
                Emit($"*stopped,reason=\"{miReason}\",thread-id=\"{threadId}\",stopped-threads=\"all\",frame={{}}");
                break;
            }

            case "continued":
                Emit("*running,thread-id=\"all\"");
                break;

            case "thread":
            {
                var tid = body?["threadId"]?.GetValue<int>() ?? 0;
                var started = body?["reason"]?.GetValue<string>() == "started";
                if (started)
                    Emit($"=thread-created,id=\"{tid}\",group-id=\"i1\"");
                else
                    Emit($"=thread-exited,id=\"{tid}\",group-id=\"i1\"");
                break;
            }

            case "module":
            {
                var id = body?["module"]?["id"]?.GetValue<string>() ?? "";
                var name = body?["module"]?["name"]?.GetValue<string>() ?? "";
                var path = body?["module"]?["path"]?.GetValue<string>() ?? name;
                Emit($"=library-loaded,id=\"{MiEscape(id)}\",target-name=\"{MiEscape(name)}\",host-name=\"{MiEscape(path)}\",symbols-loaded=\"1\",thread-group=\"i1\"");
                break;
            }

            case "output":
            {
                var category = body?["category"]?.GetValue<string>() ?? "stdout";
                var output = body?["output"]?.GetValue<string>() ?? "";
                var prefix = category == "stderr" ? "&" : "~";
                foreach (var line in output.Split('\n'))
                {
                    if (line.Length > 0)
                        Emit($"{prefix}\"{MiEscape(line)}\"");
                }
                break;
            }

            case "exited":
            case "terminated":
            {
                var code = body?["exitCode"]?.GetValue<int>() ?? 0;
                Emit($"*stopped,reason=\"exited\",exit-code=\"{code}\"");
                Emit("=thread-group-exited,id=\"i1\"");
                break;
            }
        }
    }

    // ── MI command dispatch ──────────────────────────────────────────────────

    public async Task HandleCommandAsync(MiCommand cmd)
    {
        try
        {
            switch (cmd.Name)
            {
                case "gdb-exit":
                    await _dap.SendRequestAsync("disconnect", new JsonObject { ["terminateDebuggee"] = true });
                    Reply(cmd.Token, "exit");
                    break;

                case "gdb-version":
                case "gdb-show":
                    Reply(cmd.Token, "done", "value=\"sharpdbg-mi 0.1\"");
                    break;

                case "exec-run":
                    await ExecRunAsync(cmd);
                    break;

                case "exec-continue":
                    await ExecContinueAsync(cmd);
                    break;

                case "exec-next":
                    await ExecStepAsync(cmd, "next");
                    break;

                case "exec-step":
                    await ExecStepAsync(cmd, "stepIn");
                    break;

                case "exec-finish":
                    await ExecStepAsync(cmd, "stepOut");
                    break;

                case "exec-interrupt":
                {
                    var threadId = GetThreadId(cmd);
                    await _dap.SendRequestAsync("pause", new JsonObject { ["threadId"] = threadId });
                    Reply(cmd.Token, "done");
                    break;
                }

                case "break-insert":
                    await BreakInsertAsync(cmd);
                    break;

                case "break-delete":
                    await BreakDeleteAsync(cmd);
                    break;

                case "break-list":
                    BreakList(cmd);
                    break;

                case "stack-list-frames":
                    await StackListFramesAsync(cmd);
                    break;

                case "stack-list-variables":
                case "stack-list-locals":
                    await StackListVariablesAsync(cmd);
                    break;

                case "var-create":
                    await VarCreateAsync(cmd);
                    break;

                case "var-evaluate-expression":
                    await VarEvaluateAsync(cmd);
                    break;

                case "thread-info":
                    await ThreadInfoAsync(cmd);
                    break;

                case "thread-select":
                {
                    if (cmd.Positional.Count > 0 && int.TryParse(cmd.Positional[0], out var tid))
                        _activeThreadId = tid;
                    Reply(cmd.Token, "done");
                    break;
                }

                case "interpreter-exec":
                    // e.g. interpreter-exec console "expression"
                    await InterpreterExecAsync(cmd);
                    break;

                case "enable-pretty-printing":
                case "gdb-set":
                    Reply(cmd.Token, "done");
                    break;

                default:
                    _log($"[MI] Unknown command: {cmd.Name}");
                    Reply(cmd.Token, "error", "msg=\"Unsupported command\"");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log($"[MI] Error handling {cmd.Name}: {ex.Message}");
            Reply(cmd.Token, "error", $"msg=\"{MiEscape(ex.Message)}\"");
        }
    }

    // ── Command implementations ──────────────────────────────────────────────

    private async Task ExecRunAsync(MiCommand cmd)
    {
        // -exec-run --program=/path --args="..." [--stop-at-entry]
        var program = cmd.Options.GetValueOrDefault("program") ?? "";
        var rawArgs = cmd.Options.GetValueOrDefault("args") ?? "";
        var args = SplitArgs(rawArgs);
        var cwd = cmd.Options.GetValueOrDefault("cwd");
        var stopAtEntry = cmd.Options.ContainsKey("stop-at-entry");

        if (string.IsNullOrEmpty(program))
        {
            Reply(cmd.Token, "error", "msg=\"Missing --program argument\"");
            return;
        }

        // Do the DAP initialize handshake first
        await _dap.SendRequestAsync("initialize", new JsonObject
        {
            ["clientID"] = "sharpdbg-mi",
            ["adapterID"] = "sharpdbg",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["pathFormat"] = "path",
        });

        // Wait for the initialized event (may already have fired)
        await _initializedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var launchArgs = new JsonObject
        {
            ["program"] = program,
            ["stopAtEntry"] = stopAtEntry,
        };
        if (args.Length > 0)
        {
            var arr = new JsonArray();
            foreach (var a in args) arr.Add(a);
            launchArgs["args"] = arr;
        }
        if (!string.IsNullOrEmpty(cwd))
            launchArgs["cwd"] = cwd;

        await _dap.SendRequestAsync("launch", launchArgs);
        await _dap.SendRequestAsync("configurationDone");

        Reply(cmd.Token, "running");
        Emit("*running,thread-id=\"all\"");
    }

    private async Task ExecContinueAsync(MiCommand cmd)
    {
        var threadId = GetThreadId(cmd);
        await _dap.SendRequestAsync("continue", new JsonObject { ["threadId"] = threadId });
        Reply(cmd.Token, "running");
        Emit("*running,thread-id=\"all\"");
    }

    private async Task ExecStepAsync(MiCommand cmd, string dapCommand)
    {
        var threadId = GetThreadId(cmd);
        await _dap.SendRequestAsync(dapCommand, new JsonObject { ["threadId"] = threadId });
        Reply(cmd.Token, "running");
        Emit("*running,thread-id=\"all\"");
    }

    private async Task BreakInsertAsync(MiCommand cmd)
    {
        // -break-insert [--source file] [--line N] [--condition COND] [-t] [location]
        string? file = cmd.Options.GetValueOrDefault("source") ?? cmd.Options.GetValueOrDefault("f");
        string? lineStr = cmd.Options.GetValueOrDefault("line") ?? cmd.Options.GetValueOrDefault("l");
        string? condition = cmd.Options.GetValueOrDefault("condition") ?? cmd.Options.GetValueOrDefault("c");

        // Positional: file:line or just a location
        if (string.IsNullOrEmpty(file) && cmd.Positional.Count > 0)
        {
            var loc = cmd.Positional[0];
            var colon = loc.LastIndexOf(':');
            if (colon > 0)
            {
                file = loc[..colon];
                lineStr = loc[(colon + 1)..];
            }
        }

        if (string.IsNullOrEmpty(file) || !int.TryParse(lineStr, out var line))
        {
            Reply(cmd.Token, "error", "msg=\"Cannot parse breakpoint location\"");
            return;
        }

        var id = _nextBpId++;
        if (!_fileBreakpoints.TryGetValue(file, out var list))
            _fileBreakpoints[file] = list = new();
        list.Add(new BreakpointEntry(id, file, line, condition));

        await SendSetBreakpointsAsync(file, list);

        Reply(cmd.Token, "done", $"bkpt={{number=\"{id}\",type=\"breakpoint\",disp=\"keep\",enabled=\"y\",addr=\"0x0\",file=\"{MiEscape(file)}\",fullname=\"{MiEscape(file)}\",line=\"{line}\"}}");
    }

    private async Task BreakDeleteAsync(MiCommand cmd)
    {
        // -break-delete N [N ...]
        var ids = new HashSet<int>();
        foreach (var p in cmd.Positional)
            if (int.TryParse(p, out var id)) ids.Add(id);
        // Also handle comma-separated in raw
        foreach (var p in cmd.Raw.Split(',', ' '))
            if (int.TryParse(p.Trim(), out var id)) ids.Add(id);

        var affectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (file, list) in _fileBreakpoints)
        {
            var before = list.Count;
            list.RemoveAll(b => ids.Contains(b.Id));
            if (list.Count != before) affectedFiles.Add(file);
        }

        foreach (var file in affectedFiles)
            await SendSetBreakpointsAsync(file, _fileBreakpoints[file]);

        Reply(cmd.Token, "done");
    }

    private void BreakList(MiCommand cmd)
    {
        var sb = new StringBuilder("body={BreakpointTable={nr_rows=\"");
        var all = _fileBreakpoints.Values.SelectMany(x => x).ToList();
        sb.Append(all.Count).Append("\",nr_cols=\"6\",hdr=[],body=[");
        var first = true;
        foreach (var b in all)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append($"bkpt={{number=\"{b.Id}\",type=\"breakpoint\",enabled=\"y\",addr=\"0x0\",file=\"{MiEscape(b.File)}\",line=\"{b.Line}\"}}");
        }
        sb.Append("]}}");
        Reply(cmd.Token, "done", sb.ToString());
    }

    private async Task SendSetBreakpointsAsync(string file, List<BreakpointEntry> entries)
    {
        var bps = new JsonArray();
        foreach (var e in entries)
        {
            var bp = new JsonObject { ["line"] = e.Line };
            if (!string.IsNullOrEmpty(e.Condition))
                bp["condition"] = e.Condition;
            bps.Add(bp);
        }
        await _dap.SendRequestAsync("setBreakpoints", new JsonObject
        {
            ["source"] = new JsonObject { ["path"] = file },
            ["breakpoints"] = bps,
        });
    }

    private async Task StackListFramesAsync(MiCommand cmd)
    {
        var threadId = GetThreadId(cmd);
        var resp = await _dap.SendRequestAsync("stackTrace", new JsonObject
        {
            ["threadId"] = threadId,
            ["startFrame"] = 0,
            ["levels"] = 100,
        });

        var frames = resp["body"]?["stackFrames"]?.AsArray() ?? new JsonArray();
        var sb = new StringBuilder("stack=[");
        var i = 0;
        foreach (var f in frames)
        {
            if (i > 0) sb.Append(',');
            var name = MiEscape(f?["name"]?.GetValue<string>() ?? "??");
            var filePath = MiEscape(f?["source"]?["path"]?.GetValue<string>() ?? "");
            var lineno = f?["line"]?.GetValue<int>() ?? 0;
            sb.Append($"frame={{level=\"{i}\",addr=\"0x0\",func=\"{name}\",file=\"{filePath}\",fullname=\"{filePath}\",line=\"{lineno}\"}}");
            i++;
        }
        sb.Append(']');
        Reply(cmd.Token, "done", sb.ToString());
    }

    private async Task StackListVariablesAsync(MiCommand cmd)
    {
        var threadId = GetThreadId(cmd);
        var frameIndex = 0;
        if (cmd.Options.TryGetValue("frame", out var fs) && int.TryParse(fs, out var fi)) frameIndex = fi;

        // Get the frameId for this thread+frame
        var stackResp = await _dap.SendRequestAsync("stackTrace", new JsonObject
        {
            ["threadId"] = threadId,
            ["startFrame"] = frameIndex,
            ["levels"] = 1,
        });
        var frameId = stackResp["body"]?["stackFrames"]?[0]?["id"]?.GetValue<int>() ?? 0;

        var scopesResp = await _dap.SendRequestAsync("scopes", new JsonObject { ["frameId"] = frameId });
        var scopes = scopesResp["body"]?["scopes"]?.AsArray() ?? new JsonArray();

        // For stack-list-locals get locals scope, for stack-list-variables get all
        var onlyLocals = cmd.Name == "stack-list-locals";
        var sb = new StringBuilder("variables=[");
        var first = true;

        foreach (var scope in scopes)
        {
            var scopeName = scope?["name"]?.GetValue<string>() ?? "";
            if (onlyLocals && !scopeName.Equals("Locals", StringComparison.OrdinalIgnoreCase)) continue;
            var varsRef = scope?["variablesReference"]?.GetValue<int>() ?? 0;
            if (varsRef == 0) continue;

            var varsResp = await _dap.SendRequestAsync("variables", new JsonObject { ["variablesReference"] = varsRef });
            var vars = varsResp["body"]?["variables"]?.AsArray() ?? new JsonArray();
            foreach (var v in vars)
            {
                if (!first) sb.Append(',');
                first = false;
                var name = MiEscape(v?["name"]?.GetValue<string>() ?? "");
                var value = MiEscape(v?["value"]?.GetValue<string>() ?? "");
                var type = MiEscape(v?["type"]?.GetValue<string>() ?? "");
                sb.Append($"{{name=\"{name}\",value=\"{value}\",type=\"{type}\"}}");
            }
        }
        sb.Append(']');
        Reply(cmd.Token, "done", sb.ToString());
    }

    private async Task VarCreateAsync(MiCommand cmd)
    {
        // -var-create - * EXPR  or  -var-create NAME FRAME EXPR
        // Positional: [name, frame, expr]  or expr is last
        var expr = cmd.Positional.Count > 0 ? cmd.Positional[^1] : cmd.Raw.Trim();
        if (expr == "*" || expr == "-") { Reply(cmd.Token, "error", "msg=\"empty expression\""); return; }

        // Get the active frame id
        var frameId = await GetActiveFrameIdAsync();

        var resp = await _dap.SendRequestAsync("evaluate", new JsonObject
        {
            ["expression"] = expr,
            ["frameId"] = frameId,
            ["context"] = "watch",
        });

        if (resp["success"]?.GetValue<bool>() == false)
        {
            var err = MiEscape(resp["message"]?.GetValue<string>() ?? "error");
            Reply(cmd.Token, "error", $"msg=\"{err}\"");
            return;
        }

        var result = MiEscape(resp["body"]?["result"]?.GetValue<string>() ?? "");
        var type = MiEscape(resp["body"]?["type"]?.GetValue<string>() ?? "");
        var name = cmd.Positional.Count > 0 && cmd.Positional[0] != "-" ? cmd.Positional[0] : expr;
        Reply(cmd.Token, "done", $"name=\"{MiEscape(name)}\",value=\"{result}\",type=\"{type}\",numchild=\"0\"");
    }

    private async Task VarEvaluateAsync(MiCommand cmd)
    {
        var expr = cmd.Positional.Count > 0 ? cmd.Positional[0] : cmd.Raw.Trim();
        var frameId = await GetActiveFrameIdAsync();

        var resp = await _dap.SendRequestAsync("evaluate", new JsonObject
        {
            ["expression"] = expr,
            ["frameId"] = frameId,
            ["context"] = "watch",
        });

        if (resp["success"]?.GetValue<bool>() == false)
        {
            Reply(cmd.Token, "error", $"msg=\"{MiEscape(resp["message"]?.GetValue<string>() ?? "error")}\"");
            return;
        }
        var result = MiEscape(resp["body"]?["result"]?.GetValue<string>() ?? "");
        Reply(cmd.Token, "done", $"value=\"{result}\"");
    }

    private async Task ThreadInfoAsync(MiCommand cmd)
    {
        var resp = await _dap.SendRequestAsync("threads");
        var threads = resp["body"]?["threads"]?.AsArray() ?? new JsonArray();
        var sb = new StringBuilder("threads=[");
        var first = true;
        foreach (var t in threads)
        {
            if (!first) sb.Append(',');
            first = false;
            var id = t?["id"]?.GetValue<int>() ?? 0;
            var name = MiEscape(t?["name"]?.GetValue<string>() ?? $"Thread {id}");
            sb.Append($"{{id=\"{id}\",target-id=\"{name}\",state=\"stopped\",name=\"{name}\"}}");
        }
        sb.Append(']');
        Reply(cmd.Token, "done", sb.ToString());
    }

    private async Task InterpreterExecAsync(MiCommand cmd)
    {
        // interpreter-exec console "expr" — treat as evaluate
        var expr = cmd.Positional.Count > 1 ? cmd.Positional[1] : (cmd.Positional.Count == 1 ? cmd.Positional[0] : "");
        if (string.IsNullOrEmpty(expr)) { Reply(cmd.Token, "done"); return; }

        var frameId = await GetActiveFrameIdAsync();
        var resp = await _dap.SendRequestAsync("evaluate", new JsonObject
        {
            ["expression"] = expr,
            ["frameId"] = frameId,
            ["context"] = "repl",
        });
        var result = resp["body"]?["result"]?.GetValue<string>() ?? "";
        if (!string.IsNullOrEmpty(result))
            Emit($"~\"{MiEscape(result)}\"");
        Reply(cmd.Token, "done");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private int GetThreadId(MiCommand cmd)
    {
        if (cmd.Options.TryGetValue("thread", out var ts) && int.TryParse(ts, out var tid)) return tid;
        return _activeThreadId > 0 ? _activeThreadId : 1;
    }

    private async Task<int> GetActiveFrameIdAsync()
    {
        try
        {
            var resp = await _dap.SendRequestAsync("stackTrace", new JsonObject
            {
                ["threadId"] = _activeThreadId > 0 ? _activeThreadId : 1,
                ["startFrame"] = 0,
                ["levels"] = 1,
            });
            return resp["body"]?["stackFrames"]?[0]?["id"]?.GetValue<int>() ?? 0;
        }
        catch { return 0; }
    }

    private void Reply(string token, string resultClass, string? fields = null)
    {
        var line = fields is null ? $"{token}^{resultClass}" : $"{token}^{resultClass},{fields}";
        Out.WriteLine(line);
        Out.Flush();
    }

    private void Emit(string record)
    {
        Out.WriteLine(record);
        Out.Flush();
    }

    private static string MiEscape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private static string[] SplitArgs(string raw)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        foreach (var c in raw)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
                continue;
            }
            sb.Append(c);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return [.. list];
    }
}
