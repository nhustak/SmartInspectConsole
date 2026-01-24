# SmartInspect Console

A WPF-based replacement console for receiving and displaying real-time logging data from SmartInspectCore applications.

## Overview

SmartInspect Console is a replacement for the original Gurock SmartInspect Console. It receives log packets from SmartInspectCore applications via TCP (port 4228) and Named Pipes (`smartinspect`), displaying them in a real-time viewer.

## Features

- **Real-time Logging**: Receive and display log entries as they arrive
- **Multiple Protocols**: Listen on both TCP (port 4228) and Named Pipes simultaneously
- **Session Filtering**: Filter log entries by session
- **Text Search**: Search through log entries by title or content
- **Watches Panel**: Monitor variable values in real-time
- **Process Flow Panel**: Track method entry/exit and thread flow
- **Detail View**: View complete log entry details including data payload
- **Control Commands**: Handle clear commands from clients

## Project Structure

```
SmartInspectConsole/
├── SmartInspectConsole.sln
└── src/
    ├── SmartInspectConsole/              # WPF Application
    │   ├── Converters/                   # Value converters
    │   ├── ViewModels/                   # MVVM view models
    │   └── Views/                        # XAML views
    │
    └── SmartInspectConsole.Core/         # Protocol Library
        ├── Enums/                        # Protocol enumerations
        ├── Events/                       # Event argument classes
        ├── Listeners/                    # TCP and Pipe listeners
        ├── Packets/                      # Packet data classes
        └── Parsing/                      # Binary packet parser
```

## Building

```bash
cd C:\ProjDotNet\SmartInspectConsole
dotnet build
```

## Running

```bash
dotnet run --project src/SmartInspectConsole
```

## Usage

1. Start the SmartInspect Console
2. The console automatically starts listening on:
   - TCP port 4228
   - Named pipe `smartinspect`
3. Connect your SmartInspectCore application:

```csharp
// Using SmartInspectCore
SiAuto.Si.Enabled = true;
SiAuto.Main.LogMessage("Hello from my app!");

// Or with explicit configuration
var si = new SmartInspect("MyApp");
si.Connections = "pipe()";  // or "tcp()"
si.Enabled = true;
var session = si.AddSession("Main");
session.LogMessage("Connected!");
```

## Protocol Compatibility

The console is fully compatible with SmartInspectCore's binary protocol:

| Packet Type | Supported |
|-------------|-----------|
| LogEntry | Yes |
| Watch | Yes |
| ProcessFlow | Yes |
| ControlCommand | Yes |
| LogHeader | Yes |

## UI Layout

```
┌────────────────────────────────────────────────────────────────────┐
│ File  View  Help                                                   │
├────────────────────────────────────────────────────────────────────┤
│ [Start] [Stop] [Clear] | Filter: [___________] | Session: [____▼] │
├───────────────────────────────────────┬────────────────────────────┤
│ Log Entries                           │ Details                    │
│ ┌─────────────────────────────────┐   │ Title: ...                 │
│ │ Time    │ Type │ Session │ Title│   │ Session: ...               │
│ │─────────│──────│─────────│──────│   │ Timestamp: ...             │
│ │ 10:23   │ 💬   │ Main    │ ...  │   │ Data: ...                  │
│ └─────────────────────────────────┘   │                            │
├───────────────────────────────────────┤                            │
│ Watches                               │                            │
│ │ Name │ Value │ Type │ Time │        │                            │
├───────────────────────────────────────┤                            │
│ Process Flow                          │                            │
│ │ Type │ Title │ Thread │ Time │      │                            │
├────────────────────────────────────────────────────────────────────┤
│ Ready │ TCP: Port 4228 │ Pipe: smartinspect │ Entries: 0          │
└────────────────────────────────────────────────────────────────────┘
```

## Requirements

- .NET 9.0 or later
- Windows (WPF application)

## License

MIT License
