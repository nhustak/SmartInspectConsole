# SmartInspect Console

**Version 2026.1.24.2**

A WPF-based replacement console for receiving and displaying real-time logging data from SmartInspectCore logging system.

## Overview

I've been using SmartInspect for a looooonnngggg time.  I found it way back in my Delphi days (I was one of the early adopters).
I still use it, despite the fact it hasn't had a refresh in...lord...20 years?   A company bought it recently and were supposedly working on it.
I was waiting to see - the console was never really finished and sucks.
Well I got tired of it...and realized Claude could help me here.

Yes, the code in this project is 100% Claude Code.

So here we go - if you use SmartInspect, you should love this.   I have no idea if I'm breaking copyright here.  If I am, well they can give me a take down and I'll pull it.
Reality is it's been abandoned and it still is.  IMHO, this brings it back to life.
You still must buy the product and you should.  It is one of the most useful tools I have.

For instance, I have it setup with a memory buffer.  If the app crashes, it grabs that buffer and puts in the email.  I can then pull it up in the console (this console can't yet) and review the logs for what was happening over the last few minutes.

https://code-partners.com/offerings/smartinspect/

SmartInspect Console is a replacement for the original Gurock SmartInspect Console. It receives log packets from SmartInspectCore applications via TCP (port 4228) and Named Pipes (`smartinspect`), displaying them in a real-time viewer.

## Features

### Core Functionality
- **Real-time Logging**: Receive and display log entries as they arrive
- **Multiple Protocols**: Listen on both TCP (port 4228) and Named Pipes simultaneously
- **Multiple Views/Tabs**: Create multiple filtered views of the same log data
- **Session Filtering**: Filter log entries by session name
- **Text Search**: Search through log entries by title or content
- **Log Level Filtering**: Filter by minimum log level (Debug, Verbose, Message, Warning, Error, Fatal)
- **Auto-Scroll**: Toggle auto-scroll to newest entries per view
- **Watches Panel**: Monitor variable values in real-time
- **Process Flow Panel**: Track method entry/exit and thread flow
- **Control Commands**: Handle clear commands from clients

### Detail View
- **Smart Data Detection**: Auto-detect JSON, XML, Key-Value pairs, Binary, or plain Text
- **Format Dropdown**: Manually override format detection
- **JSON Formatting**: Pretty-print JSON data with proper indentation
- **XML Formatting**: Format XML documents
- **Binary Hex View**: Display binary data in hex dump format
- **Key-Value Formatting**: Align key-value pairs for readability
- **Copy to Clipboard**: One-click copy of formatted data
- **Multiple Detail Tabs**: Open multiple log entries in separate tabs

### UI Features
- **Dark/Light Themes**: Toggle between dark and light themes
- **Column Visibility**: Show/hide columns (Time, Elapsed, App, Session, Title, Thread)
- **Separator Display**: Visual horizontal line separators in log list
- **Icon Legend**: Reference dialog showing all log entry type icons and colors
- **State Persistence**: Saves window position, size, theme, view configurations
- **Layout Export/Import**: Export and import layout configurations

### Settings
- **Configurable TCP Port**: Change the listening port (default: 4228)
- **Configurable Pipe Name**: Change the pipe name (default: smartinspect)
- **Per-View Settings**: Each view maintains its own filter and display settings

## Project Structure

```
SmartInspectConsole/
├── SmartInspectConsole.sln
└── src/
    ├── SmartInspectConsole/              # WPF Application
    │   ├── Behaviors/                    # Attached behaviors (AutoScroll)
    │   ├── Converters/                   # Value converters
    │   ├── Resources/                    # Theme files (Dark/Light)
    │   ├── Services/                     # App state persistence
    │   ├── ViewModels/                   # MVVM view models
    │   └── Views/                        # XAML views and dialogs
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

// Advanced: Memory buffer with auto-reconnect and failover
// This keeps a 2048KB memory buffer that can be retrieved on crash,
// with automatic reconnection to pipe and TCP fallback
si.Connections = "mem(maxsize=2048, astext=true), " +
                 "pipe(reconnect=true, reconnect.interval=5s), " +
                 "tcp(host=localhost, reconnect=true, reconnect.interval=5s)";
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

## TODO

- [ ] Import and export log files (.sil format)
- [ ] Search/filter across all columns
- [ ] Bookmarks for important log entries
- [ ] Log entry highlighting rules
- [ ] Memory buffer retrieval from crashed applications
- [ ] Statistics/metrics dashboard
- [ ] Log file watching (tail -f style)

## Requirements

- .NET 10.0 or later
- Windows (WPF application)

## License

MIT License
