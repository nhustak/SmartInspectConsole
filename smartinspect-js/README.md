# SmartInspect JS

Browser-based logging client for SmartInspect Console via WebSocket or HTTP.

**Version 1.1.0**

## Quick Start

```typescript
import { SiAuto } from 'smartinspect-js';

// Connect to SmartInspect Console
await SiAuto.si.connect('ws://localhost:4229');

// Log messages
SiAuto.main.logMessage('Hello from browser!');
SiAuto.main.logWarning('Something might be wrong');
SiAuto.main.logError('Something went wrong!');
```

## Installation

```bash
npm install smartinspect-js
```

Or include via script tag:

```html
<script src="path/to/smartinspect.min.js"></script>
<script>
  const { SiAuto } = SmartInspectJS;
  SiAuto.si.connect('ws://localhost:4229');
  SiAuto.main.logMessage('Hello!');
</script>
```

## Features

### Log Levels

```typescript
session.logMessage('Info message');
session.logDebug('Debug info');
session.logVerbose('Detailed info');
session.logWarning('Warning message');
session.logError('Error message');
session.logFatal('Fatal error');
```

### Log Data

```typescript
// Log objects (auto-serialized to JSON)
session.logObject('User', { id: 1, name: 'John' });

// Log with specific viewers
session.logJson('API Response', data);
session.logXml('Config', xmlString);
session.logHtml('Template', htmlString);
session.logSql('Query', sqlString);

// Log exceptions
try {
  throw new Error('Something went wrong');
} catch (e) {
  session.logException('Caught error', e);
}
```

### Watches

Track values over time:

```typescript
session.watch('counter', count);
session.watch('isLoggedIn', true);
session.watch('user', { name: 'John' });

// Typed watches
session.watchInt('counter', 42);
session.watchFloat('percentage', 0.75);
session.watchBool('enabled', true);
session.watchString('status', 'active');
```

### Process Flow

Track method execution:

```typescript
session.enterMethod('processOrder');
// ... do work ...
session.leaveMethod('processOrder');

// Or use trackMethod for automatic tracking
const done = session.trackMethod('handleClick');
// ... do work ...
done(); // Automatically logs leaveMethod
```

### Multiple Sessions

Organize logs by session:

```typescript
import { SmartInspect } from 'smartinspect-js';

const si = new SmartInspect('MyApp');
await si.connect();

const mainSession = si.addSession('Main');
const networkSession = si.addSession('Network');
const uiSession = si.addSession('UI');

mainSession.logMessage('App started');
networkSession.logMessage('Fetching data...');
uiSession.logMessage('Rendering component');
```

### Control Commands

```typescript
si.clearLog();        // Clear log entries
si.clearWatches();    // Clear watches
si.clearProcessFlow(); // Clear process flow
si.clearAll();        // Clear everything
```

### Connection Options

```typescript
const si = new SmartInspect('MyApp', {
  autoReconnect: true,
  reconnectDelay: 2000,
  maxReconnectAttempts: 5,
  bufferWhenDisconnected: true,
  maxBufferSize: 1000
});

// Event handlers
si.events = {
  onConnected: () => console.log('Connected!'),
  onDisconnected: () => console.log('Disconnected'),
  onError: (err) => console.error('Error:', err),
  onStateChange: (state) => console.log('State:', state)
};
```

### HTTP Connection (Production)

For production environments where WebSockets may be blocked by firewalls or proxies, use HTTP mode with the SmartInspect Relay:

```typescript
const si = new SmartInspect('MyApp', {
  connectionType: 'http',
  httpOptions: {
    endpoint: 'https://yoursite.com/api/v1/logs',
    apiKey: 'optional-api-key',         // Optional authentication
    flushInterval: 2000,                // Batch flush interval (ms)
    maxBatchSize: 100,                  // Max messages per batch
    compression: true                   // Gzip compress large batches
  }
});

await si.connect();
si.mainSession.logMessage('Hello from production!');
```

**HTTP Mode Features:**
- Message batching with configurable interval and batch size
- Priority flush for error/fatal messages (sent immediately)
- Automatic page unload handling via `navigator.sendBeacon()`
- Exponential backoff retry on failures
- Optional gzip compression for large payloads

See the main SmartInspect Console README for relay setup instructions.

## SmartInspect Console Setup

1. Start SmartInspect Console
2. Go to Tools → Settings
3. Configure WebSocket port (default: 4229)
4. Start listening

The console listens on:
- TCP: 4228 (for native clients)
- Named Pipe: smartinspect
- WebSocket: 4229 (for browser clients)

## Building from Source

```bash
npm install
npm run build
```

This creates:
- `dist/smartinspect.js` - UMD bundle
- `dist/smartinspect.min.js` - Minified UMD bundle
- `dist/smartinspect.esm.js` - ES Module bundle
- `dist/smartinspect.d.ts` - TypeScript declarations

## License

MIT
