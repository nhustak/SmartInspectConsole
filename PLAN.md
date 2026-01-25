# SmartInspect Console - Browser Integration Plan

## Overview

Enable JavaScript/browser applications to send logs to SmartInspect Console.

## Approaches

### 1. Direct WebSocket (smartinspect-js) ✅ BUILT - NEEDS TESTING

**Use case:** Local development, browser can reach console directly

**Flow:**
```
Browser ──WebSocket:4229──→ SmartInspect Console
```

**Components:**
- `smartinspect-js/` - TypeScript/JavaScript client library
- `SmartInspectWebSocketListener.cs` - Console WebSocket server
- `JsonPacketConverter.cs` - JSON to packet conversion

**Status:**
- [x] WebSocket listener in console
- [x] JSON protocol converter
- [x] JavaScript client library (TypeScript)
- [x] Example HTML page (`examples/basic.html`)
- [x] Settings UI for WebSocket port
- [ ] Build and test end-to-end
- [ ] Publish to npm (future)

---

### 2. WebAPI Relay Library 📋 PLANNED

**Use case:** Production web apps where browser cannot reach console directly

**Flow:**
```
Browser ──POST /api/log──→ ASP.NET Core Backend ──TCP/Pipe──→ SmartInspect Console
```

**Components needed:**
- **Browser JS** - Lightweight client that POSTs JSON to backend API
- **ASP.NET Core Library** - NuGet package with:
  - Middleware or controller endpoints to receive log POSTs
  - SmartInspect client to relay to console
  - Optional: batching, filtering, throttling

**Design considerations:**
- Should work with existing SmartInspectCore NuGet package
- Minimal configuration required
- Support for session tracking across requests
- Rate limiting to prevent abuse

**Status:**
- [ ] Design API contract (JSON format)
- [ ] Create browser-side JS library
- [ ] Create ASP.NET Core relay library
- [ ] Example integration

---

### 3. SignalR Version ❌ DEFERRED

**Decision:** Too complicated for project goals. Raw WebSocket and HTTP POST cover the use cases adequately.

**Reconsidered if:**
- Need bidirectional communication (console → browser)
- Need automatic reconnection with state recovery beyond what we built
- Scaling becomes a concern

---

## Current Priority

1. **Test existing WebSocket implementation** - Build smartinspect-js, run console, test with basic.html
2. **Design WebAPI relay** - Define the API contract and library structure
3. **Build WebAPI relay** - Implement the backend relay library

---

## JSON Protocol

Both approaches use the same JSON format:

```json
// Log Entry
{
  "type": "log",
  "logEntryType": "message|warning|error|debug|verbose|fatal|separator",
  "session": "Main",
  "title": "Log message here",
  "data": "Optional data payload",
  "viewerId": "data|json|xml|html|sql",
  "color": "#FF0000"
}

// Watch
{
  "type": "watch",
  "name": "variableName",
  "value": "current value",
  "watchType": "string|int|float|bool|object"
}

// Process Flow
{
  "type": "flow",
  "flowType": "enter|leave",
  "title": "methodName"
}

// Control Command
{
  "type": "control",
  "command": "clearlog|clearwatches|clearprocessflow|clearall"
}
```

---

## File Locations

| Component | Location |
|-----------|----------|
| WebSocket Listener | `src/SmartInspectConsole.Core/Listeners/SmartInspectWebSocketListener.cs` |
| JSON Converter | `src/SmartInspectConsole.Core/Protocol/JsonPacketConverter.cs` |
| JS Client Library | `smartinspect-js/` |
| Example HTML | `smartinspect-js/examples/basic.html` |
| WebAPI Relay (planned) | `src/SmartInspectConsole.WebRelay/` (TBD) |
