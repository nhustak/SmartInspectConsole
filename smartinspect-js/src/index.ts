/**
 * SmartInspect JS - Browser logging client for SmartInspect Console
 *
 * @example Quick Start (WebSocket - Development)
 * ```typescript
 * import { SiAuto } from 'smartinspect-js';
 *
 * // Connect to SmartInspect Console
 * await SiAuto.si.connect('ws://localhost:4229');
 *
 * // Log messages
 * SiAuto.main.logMessage('Hello from browser!');
 * SiAuto.main.logWarning('Something might be wrong');
 * SiAuto.main.logError('Something is wrong!');
 *
 * // Log objects
 * SiAuto.main.logObject('User', { id: 1, name: 'John' });
 *
 * // Track watches
 * SiAuto.main.watch('counter', 42);
 *
 * // Track method execution
 * SiAuto.main.enterMethod('handleClick');
 * // ... do work ...
 * SiAuto.main.leaveMethod('handleClick');
 * ```
 *
 * @example HTTP Relay (Production)
 * ```typescript
 * import { SmartInspect } from 'smartinspect-js';
 *
 * const si = new SmartInspect('MyApp', {
 *   connectionType: 'http',
 *   httpOptions: {
 *     endpoint: 'https://logs.example.com/api/v1',
 *     apiKey: 'your-api-key',
 *     flushInterval: 2000,
 *     maxBatchSize: 100
 *   }
 * });
 *
 * await si.connect();
 * si.mainSession.logMessage('Hello from production!');
 * ```
 *
 * @example Multiple Sessions
 * ```typescript
 * import { SmartInspect } from 'smartinspect-js';
 *
 * const si = new SmartInspect('MyApp');
 * await si.connect('ws://localhost:4229');
 *
 * const mainSession = si.addSession('Main');
 * const networkSession = si.addSession('Network');
 * const uiSession = si.addSession('UI');
 *
 * mainSession.logMessage('App started');
 * networkSession.logMessage('Fetching data...');
 * uiSession.logMessage('Rendering component');
 * ```
 *
 * @packageDocumentation
 */

// Main classes
export { SmartInspect, SiAuto } from './SmartInspect';
export { Session } from './Session';

// Connection classes
export {
  type IConnection,
  WebSocketConnection,
  HttpConnection,
  type HttpConnectionOptions
} from './connections';

// Re-export WebSocketConnection from root for backwards compatibility
export { WebSocketConnection as default } from './connections';

// Types
export type {
  LogEntryType,
  ViewerId,
  WatchType,
  ProcessFlowType,
  ControlCommandType,
  ConnectionState,
  ConnectionType,
  HttpOptions,
  LogEntryMessage,
  WatchMessage,
  ProcessFlowMessage,
  ControlCommandMessage,
  Message,
  SmartInspectOptions,
  SmartInspectEvents
} from './types';
