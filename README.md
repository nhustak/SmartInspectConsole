# SmartInspect Console

**Version 2026.1.25.1**

A WPF-based replacement console for receiving and displaying real-time logging data from SmartInspectCore logging system.

## Overview

I've been using SmartInspect for a looooonnngggg time.  I found it way back in my Delphi days (I was one of the early adopters).
I still use it, despite the fact the console hasn't had a refresh in...lord...20 years?
It looked like Code Partners was going to advance it but it seems to have stalled.

So it occurred to me...Claude could probably solve this pain point for me.

The code in this project is 100% Claude Code.

So here we go - if you use SmartInspect, you should love this.   I have no idea if I'm breaking copyright here.  If I am, well they can give me a take down and I'll pull it.
I hope they see it for what it is - something to make the console logs far more usable and maybe get them some sales.

You still must buy the product and you should.  It is one of the most useful tools I have.
https://code-partners.com/offerings/smartinspect/

For instance, I have it setup with a memory buffer.  If the app crashes, it grabs that buffer and puts in the email.  I can then pull it up in the console (this console can't yet) and review the logs for what was happening over the last few minutes.


SmartInspect Console is a replacement for the original Gurock SmartInspect Console. It receives log packets from SmartInspectCore applications via TCP (port 4228) and Named Pipes (`smartinspect`), displaying them in a real-time viewer.

## Features

### Core Functionality
- **Real-time Logging**: Receive and display log entries as they arrive
- **Multiple Protocols**: Listen on both TCP (port 4228) and Named Pipes simultaneously
- **Multiple Views/Tabs**: Create multiple filtered views of the same log data
- **Drag-and-Drop Tab Reordering**: Rearrange tabs by dragging them; order persists across sessions
- **Session Filtering**: Filter log entries by session name
- **Text Search**: Search through log entries by title or content
- **Log Level Filtering**: Filter by minimum log level (Debug, Verbose, Message, Warning, Error, Fatal)
- **Auto-Scroll**: Toggle auto-scroll to newest entries per view
- **Clear View**: Clear only the current view's log entries (button on each tab's toolbar)
- **Clear All**: Clear all log entries across all views (toolbar button)
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
- **Configurable WebSocket Port**: Change the WebSocket port for browser clients (default: 4229)
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
   - TCP port 4228 (SmartInspectCore binary protocol)
   - Named pipe `smartinspect` (SmartInspectCore binary protocol)
   - WebSocket port 4229 (smartinspect-js JSON protocol)
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

## JavaScript/Browser Logging (smartinspect-js)

**Note: This only works with this new console - not the original Gurock SmartInspect Console.**

The `smartinspect-js` library allows browser-based JavaScript and TypeScript applications to send logs to the SmartInspect Console via WebSocket (port 4229).

### Installation

```bash
npm install smartinspect-js
```

Or include directly in your HTML:
```html
<script src="path/to/smartinspect.js"></script>
```

### Usage

```typescript
import { SmartInspect, Session } from 'smartinspect-js';

// Create and configure SmartInspect instance
const si = new SmartInspect('My Browser App');
si.connect('ws://localhost:4229');

// Create a session for logging
const session = si.addSession('Main');

// Log messages at different levels
session.logDebug('Debug information');
session.logVerbose('Verbose details');
session.logMessage('General message');
session.logWarning('Warning message');
session.logError('Error occurred');
session.logFatal('Fatal error!');

// Log with data
session.logMessage('User data', { userId: 123, name: 'John' });

// Enter/Leave method tracking
session.enterMethod('processData');
// ... do work ...
session.leaveMethod('processData');

// Watch variables
session.watch('counter', 42);
session.watch('status', 'active');
```

### Features

- WebSocket-based communication (no CORS issues with localhost)
- Full logging level support (Debug, Verbose, Message, Warning, Error, Fatal)
- Method entry/exit tracking for process flow visualization
- Variable watching for real-time monitoring
- Automatic reconnection on connection loss
- TypeScript support with full type definitions

### Console Configuration

The console listens on WebSocket port 4229 by default. This can be changed in Settings (File > Settings).

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
│ Ready │ TCP: 4228 │ Pipe: smartinspect │ WS: 4229 │ Entries: 0  │
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
