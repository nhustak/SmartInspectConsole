/**
 * SmartInspect JS - Browser logging client for SmartInspect Console
 *
 * @example Quick Start
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
export { WebSocketConnection } from './WebSocketConnection';

// Types
export type {
  LogEntryType,
  ViewerId,
  WatchType,
  ProcessFlowType,
  ControlCommandType,
  ConnectionState,
  LogEntryMessage,
  WatchMessage,
  ProcessFlowMessage,
  ControlCommandMessage,
  Message,
  SmartInspectOptions,
  SmartInspectEvents
} from './types';
