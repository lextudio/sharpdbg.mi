# MI Mode for SharpDbg

## Problem

GDB's Machine Interface (MI) protocol is used by many editors and IDEs (VS Code with `cpptools` or `coreclr-debug`, Emacs `dap-mode`, Neovim `nvim-dap`, etc.) to drive debuggers.  `netcoredbg` speaks MI natively. SharpDbg is a pure DAP server.

An earlier branch (`add-mi-mode`) embedded MI parsing directly inside SharpDbg's infrastructure, requiring changes to `ManagedDebugger`, `BreakpointManager`, and the expression evaluator. That approach created maintenance burden and tight coupling with the upstream repo.

## New Approach: Standalone MI-to-DAP Wrapper

Instead of modifying SharpDbg, we ship a separate executable — **`sharpdbg-mi`** — that:

1. Accepts GDB/MI commands on **stdin**, writes MI output to **stdout**.
2. Launches `sharpdbg` as a child process and connects to it over **DAP** using its stdin/stdout pipes.
3. Translates each MI command into one or more DAP requests, waits for the response, and formats the result back as MI.
4. Listens for DAP events from SharpDbg (stopped, thread, module, output, …) and forwards them as MI async-output records.

```
editor / IDE
    │  GDB/MI (stdin/stdout)
    ▼
sharpdbg-mi   ◄──── this project
    │  DAP (stdin/stdout pipe)
    ▼
sharpdbg --interpreter=vscode
    │  ICorDebug
    ▼
.NET runtime
```

SharpDbg itself is **unmodified**. `sharpdbg-mi` is a pure translation layer.

## Project Layout

```
src/SharpDbg.MiWrapper/
    SharpDbg.MiWrapper.csproj   – console exe, net10.0
    Program.cs                  – arg parsing, main loop
    DapClient.cs                – launches sharpdbg, Content-Length DAP framing, seq tracking
    MiParser.cs                 – tokenises and parses MI command lines
    MiInterpreter.cs            – dispatches MI commands → DAP calls, formats responses
```

## Protocol Mapping

### MI command → DAP request

| MI command | DAP request |
|---|---|
| `-gdb-exit` | `disconnect` |
| `-exec-run [--stop-at-entry]` | `launch` → `configurationDone` |
| `-exec-continue [--thread N]` | `continue` |
| `-exec-next [--thread N]` | `next` |
| `-exec-step [--thread N]` | `stepIn` |
| `-exec-finish [--thread N]` | `stepOut` |
| `-exec-interrupt` | `pause` |
| `-break-insert [-t] [--source S] [--line N] [location]` | `setBreakpoints` (incremental add) |
| `-break-delete N[,N…]` | `setBreakpoints` (remove from tracked set) |
| `-break-list` | synthetic — return cached breakpoint list |
| `-stack-list-frames [--thread N]` | `stackTrace` |
| `-stack-list-variables --thread N --frame F` | `scopes` → `variables` |
| `-stack-list-locals --thread N --frame F` | same as above, locals scope only |
| `-var-create - * EXPR` | `evaluate` (frameId from active frame) |
| `-var-evaluate-expression NAME` | `evaluate` |
| `-thread-info [N]` | `threads` |
| `-thread-select N` | _(local tracking only)_ |
| `-gdb-show version` | synthetic response |
| `-interpreter-exec console "CMD"` | forwarded as `evaluate` with `context=repl` |

### DAP event → MI async output

| DAP event | MI record |
|---|---|
| `initialized` | _(triggers `configurationDone`)_ |
| `stopped` | `*stopped,reason="…",thread-id="N",frame={…}` |
| `continued` | `*running,thread-id="all"` |
| `thread` (started) | `=thread-created,id="N",group-id="i1"` |
| `thread` (exited) | `=thread-exited,id="N",group-id="i1"` |
| `module` | `=library-loaded,id="…",target-name="…",host-name="…"` |
| `output` (stdout) | `~"…"` |
| `output` (stderr/telemetry) | `&"…"` |
| `terminated` / `exited` | `*stopped,reason="exited",exit-code="0"` then `^exit` |

## Usage

```sh
# VS Code launch.json (coreclr)
{
    "type": "coreclr",
    "request": "launch",
    "program": "${workspaceFolder}/bin/Debug/net10.0/MyApp.dll",
    "miDebuggerPath": "/path/to/sharpdbg-mi",
    "miDebuggerArgs": "--sharpdbg=/path/to/sharpdbg"
}
```

Or standalone:

```sh
sharpdbg-mi --sharpdbg=/path/to/sharpdbg [--engineLogging=/tmp/mi.log]
```

If `--sharpdbg` is omitted, the wrapper looks for `sharpdbg` on `PATH`.

## Breakpoint Management

DAP's `setBreakpoints` replaces the entire breakpoint list for a source file atomically. The wrapper maintains a per-file set of active breakpoints so it can compute the correct full list for each incremental MI `break-insert` / `break-delete` call.

## Limitations

- Only `launch` is implemented initially; `attach` can be added with `-target-attach PID`.
- Watchpoints, catchpoints, and tracepoints are not supported (SharpDbg doesn't implement them).
- MI variable objects (`-var-*`) are mapped to stateless `evaluate` calls; there is no persistent variable-object tree.
- Conditional breakpoints: supported if SharpDbg supports them (pass `condition` in `setBreakpoints`).
