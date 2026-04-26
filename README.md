# SmartInspect Console v1.0.0.72

Last updated: 2026-03-26

A modern Windows console for SmartInspect logs with live filtering, multi-view analysis, browser ingestion, and production-grade load handling.

This project exists because the original SmartInspect Console never really got the refresh it deserved. SmartInspect itself is still one of the most useful tools in the box, and this app is meant to make those logs far easier to work with in 2026.

This project is now being driven with Codex, which is my tool of choice for development work on this codebase.

You still need the actual SmartInspect product and you absolutely should buy it. It is one of the most useful tools I have ever used, and this project exists because the tool itself is that good:
[https://code-partners.com/offerings/smartinspect/](https://code-partners.com/offerings/smartinspect/)

## Current State

SmartInspect Console is a WPF replacement for the original Gurock SmartInspect Console. It currently supports:

- Native SmartInspect clients over TCP on port `4228`
- Native SmartInspect clients over named pipe `smartinspect`
- Browser and JavaScript clients over WebSocket on port `4229`
- In-process MCP server at `http://127.0.0.1:42331/mcp`
- Local debug/query API at `http://127.0.0.1:42331/api/local/v1`
- HTTP relay forwarding for production/browser scenarios
- Multi-tab log analysis with persistent layout and filter state
- High-volume ingestion with queue diagnostics, batch rendering, and automatic retention trimming

The desktop app now includes an in-app load test launcher under `Help > Run Load Test`.

## Screenshots

Drop screenshots into [docs/images](C:\Project\Utility\SmartInspectConsole\docs\images) and keep them in the repo so the README can render them directly on GitHub.

Recommended captures:

- main desktop window
- detail payload viewer
- connections / watches / process flow panels
- settings dialog
- load test running against the console

I will rename and place the actual images in the README once the files are there.

## Highlights

### Log Ingestion

- TCP listener for standard SmartInspect traffic
- Named pipe listener for local desktop/service scenarios
- WebSocket listener for browser clients
- HTTP relay service and embeddable ASP.NET Core relay package
- SmartInspect binary protocol support for `LogEntry`, `Watch`, `ProcessFlow`, `ControlCommand`, and `LogHeader`

### Log Viewing

- Multiple saved views over the same underlying log stream
- Per-view text filtering, session filtering, and minimum level filtering
- Per-view auto-scroll
- Drag-and-drop tab reordering with persisted order
- View duplication, rename, edit, and remove support
- Column visibility toggles and separator display
- Context menu actions for mute, copy, clear, and view management
- Global 12-hour AM/PM versus 24-hour time display setting
- Dedicated `MCP Trace` tab for protocol tracing that can be toggled on and off from the app

### Details / Payload Inspection

- Separate detail tabs for opened entries
- Automatic data detection for text, JSON, XML, key/value, and binary payloads
- Pretty formatting for JSON and XML
- Hex/binary display for non-text payloads
- One-click copy of formatted payload data
- Cleaner detail panel presentation without the old header strip

### Watches / Process Flow / Connections

- Live watch display
- Live process flow display
- Connected application list with message counts and mute toggles
- Immediate connection counts in the status bar for TCP, pipe, and WebSocket listeners
- Connection identification/merging by app and host once client identity is known

### Performance / Stability

- Batched UI ingestion instead of per-packet UI updates
- Adaptive batch sizing under backlog
- Coalesced/deferred filter refresh work
- Cached payload string decoding for expensive text filters
- Improved auto-scroll behavior under sustained load
- Queue depth and render diagnostics in the status bar
- Automatic retention limit with chunked trim-back behavior
- Default retention lowered from `100,000` to `20,000` entries for better responsiveness

### Local API / MCP

- The WPF app remains the single running host and the single source of truth for live logs
- MCP is hosted inside the same app process, not as a separate companion executable
- Local API exposes bounded log/application/context queries for debugging and contract validation
- MCP tools are built on top of the same backend/query contracts used by the local API
- Flagged log entries can be queried directly through the backend/API/MCP surface

### App / UX

- Dark and light themes
- Layout persistence
- Window placement persistence
- Edit View dialog remembers its size and position
- Edit View dialog opens at a larger default size
- Layout export/import
- Version displayed in the main window title
- Settings dialog for listener ports, pipe name, debug mode, retention cap, and confirm-before-clear
- Settings dialog includes a global 12-hour versus 24-hour time format option
- Help menu entry to launch the built-in load tester
- Tools menu includes `Trace MCP Calls` for capturing MCP traffic into the live log UI

### View Filter Editing

- Quick-pick filter tags add or remove values directly in the filter text boxes
- Filter text remains the source of truth for selected values
- Pressing `Enter` in the Edit View dialog no longer saves and closes the dialog

## Load Handling Notes

One of the major goals of the recent work was stopping the app from eventually locking up under production-level log volume.

Recent improvements include:

- reduced repeated whole-view refresh work
- removed repeated full filtered-count rescans
- cached decoded payload text instead of re-decoding repeatedly
- improved auto-scroll event handling
- added batching diagnostics
- tuned retention trimming to trim back to `90%` of the cap instead of deleting `1:1`

With the current defaults:

- max retained log entries defaults to `20,000`
- once the limit is exceeded, the app trims back to roughly `18,000`

That gives the app a visible rolling window without constant micro-trimming.

## Repository Layout

```text
SmartInspectConsole/
├── README.md
├── docs/
│   └── load-tester.md
├── smartinspect-js/
│   ├── src/
│   └── dist/
├── src/
│   ├── SmartInspectConsole/             # WPF desktop app
│   ├── SmartInspectConsole.Core/        # SmartInspect protocol + listeners
│   ├── SmartInspectConsole.LoadTester/  # Stress/load test utility
│   ├── SmartInspect.Relay.AspNetCore/   # Embeddable ASP.NET Core relay
│   └── SmartInspectConsole.Relay/       # Standalone relay service
└── SmartInspectConsole.slnx
```

## Building

```powershell
dotnet build .\src\SmartInspectConsole\SmartInspectConsole.csproj
```

## Running

```powershell
dotnet run --project .\src\SmartInspectConsole
```

When the desktop app starts, it automatically starts listening on:

- `TCP 4228`
- named pipe `smartinspect`
- `WebSocket 4229`
- local API + MCP on `127.0.0.1:42331`

## Codex MCP Setup

Add this to `C:\Users\nhust\.codex\config.toml`:

```toml
[mcp_servers.smartinspect]
url = 'http://127.0.0.1:42331/mcp'
```

Then the workflow is just:

- start `SmartInspectConsole`
- start Codex

## SmartInspect .NET Usage

```csharp
SiAuto.Si.AppName = "MyApp";
SiAuto.Si.Connections = @"pipe(reconnect=""true"", reconnect.interval=""5s"")";
SiAuto.Si.Enabled = true;

SiAuto.Main.LogMessage("Hello from SmartInspect");
```

A more defensive connection string can include memory buffering and fallback targets:

```csharp
SiAuto.Si.Connections =
    @"mem(maxsize=2048, astext=true),
      pipe(reconnect=""true"", reconnect.interval=""5s""),
      tcp(host=""localhost"", reconnect=""true"", reconnect.interval=""5s"")";
```

## Browser / JavaScript Usage

The `smartinspect-js` package targets this console, not the legacy Gurock console.

### WebSocket Example

```typescript
import { SmartInspect } from 'smartinspect-js';

const si = new SmartInspect('My Browser App');
await si.connect('ws://localhost:4229');

const session = si.addSession('Main');
session.logMessage('Browser connected');
session.logError('Something failed');
session.watch('counter', 42);
session.enterMethod('loadPage');
session.leaveMethod('loadPage');
```

### HTTP Relay Example

```typescript
import { SmartInspect } from 'smartinspect-js';

const si = new SmartInspect('My Production App', {
  connectionType: 'http',
  httpOptions: {
    endpoint: 'https://logs.example.com/api/v1/logs',
    flushInterval: 2000,
    maxBatchSize: 100,
    compression: true
  }
});

await si.connect();
si.mainSession.logMessage('Hello from production');
```

## Relay Options

### Embedded in an ASP.NET Core App

```csharp
builder.Services.AddSmartInspectRelay(options =>
{
    options.ConsoleHost = "localhost";
    options.ConsolePort = 4229;
});

app.MapSmartInspectRelay("/api/v1");
```

### Standalone Relay

```powershell
dotnet run --project .\src\SmartInspectConsole.Relay
```

Default relay endpoints:

| Endpoint | Method | Purpose |
|---|---|---|
| `/api/v1/logs` | `POST` | Accepts one log message or a batch |
| `/api/v1/health` | `GET` | Basic health check |
| `/api/v1/status` | `GET` | Relay connection and buffering status |

## Load Tester

The repo includes a dedicated stress tool at `src/SmartInspectConsole.LoadTester`.

Manual examples:

```powershell
dotnet run --project .\src\SmartInspectConsole.LoadTester -- --transport tcp --clients 8 --messages-per-second 2000 --payload-bytes 1024 --duration-seconds 300
dotnet run --project .\src\SmartInspectConsole.LoadTester -- --transport pipe --pipe smartinspect --clients 4 --messages-per-second 1000 --payload-bytes 1024 --duration-seconds 180
```

You can also launch a default TCP load test from the desktop app:

- `Help > Run Load Test`

That starts the existing load tester in a visible PowerShell window against the app's current TCP port.

## Soak Harness

For full-stack long-running validation, use the soak harness at `tools/soak/run-soak.ps1`.

It exercises:

- direct TCP traffic
- direct named-pipe traffic
- direct browser/WebSocket traffic via `smartinspect-js`
- standalone relay HTTP traffic forwarded to the desktop app over WebSocket
- a final mixed-traffic leg across all paths

See [docs/soak-test.md](C:\Project\Utility\SmartInspectConsole\docs\soak-test.md) for the run shape, output files, and override options.

## Current Caveats

- The Connections panel only shows clients once they are identified by client metadata such as `LogHeader`. Raw transport counts can be higher than the visible identified connection list.
- WebSocket clients may be connected before they have sent enough identifying data to appear by application name in the Connections panel.
- MCP trace entries are intentionally excluded from the built-in `All` view and are meant to be inspected in the dedicated `MCP Trace` tab.
- Import/export of `.sil` files is supported in the desktop app, but memory-buffer crash retrieval workflows are still not implemented end-to-end.
- There is currently no deployment pipeline, installer, or pre-compiled release package included in this repository yet. At the moment, you build and run it from source.

## Requirements

- Windows
- .NET 10 SDK for the desktop app
- .NET 9/10 compatible environment for the supporting projects

## License

MIT License
