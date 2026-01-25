import type { WebSocketConnection } from './WebSocketConnection';
import type {
  LogEntryType,
  ViewerId,
  WatchType,
  ProcessFlowType,
  LogEntryMessage,
  WatchMessage,
  ProcessFlowMessage
} from './types';

/**
 * A logging session that sends messages to SmartInspect Console
 */
export class Session {
  private connection: WebSocketConnection;
  private _name: string;
  private _appName: string;
  private _active: boolean = true;
  private _defaultColor: string | undefined;

  /**
   * Creates a new Session
   * @param connection The WebSocket connection
   * @param name Session name
   * @param appName Application name
   */
  constructor(connection: WebSocketConnection, name: string, appName: string) {
    this.connection = connection;
    this._name = name;
    this._appName = appName;
  }

  /**
   * Gets or sets the session name
   */
  get name(): string {
    return this._name;
  }

  set name(value: string) {
    this._name = value;
  }

  /**
   * Gets or sets whether the session is active
   */
  get active(): boolean {
    return this._active;
  }

  set active(value: boolean) {
    this._active = value;
  }

  /**
   * Gets or sets the default color for log entries
   */
  get defaultColor(): string | undefined {
    return this._defaultColor;
  }

  set defaultColor(value: string | undefined) {
    this._defaultColor = value;
  }

  // ==================== Basic Logging ====================

  /**
   * Logs a message
   */
  logMessage(title: string, data?: unknown, color?: string): void {
    this.log('message', title, data, color);
  }

  /**
   * Logs a warning
   */
  logWarning(title: string, data?: unknown, color?: string): void {
    this.log('warning', title, data, color);
  }

  /**
   * Logs an error
   */
  logError(title: string, data?: unknown, color?: string): void {
    this.log('error', title, data, color);
  }

  /**
   * Logs a debug message
   */
  logDebug(title: string, data?: unknown, color?: string): void {
    this.log('debug', title, data, color);
  }

  /**
   * Logs a verbose message
   */
  logVerbose(title: string, data?: unknown, color?: string): void {
    this.log('verbose', title, data, color);
  }

  /**
   * Logs a fatal error
   */
  logFatal(title: string, data?: unknown, color?: string): void {
    this.log('fatal', title, data, color);
  }

  /**
   * Logs a separator line
   */
  logSeparator(title?: string): void {
    this.log('separator', title ?? '');
  }

  /**
   * Logs an object (serialized to JSON)
   */
  logObject(title: string, obj: unknown, color?: string): void {
    const data = this.serializeValue(obj);
    this.sendLogEntry('object', title, data, 'json', color);
  }

  /**
   * Logs an exception/error
   */
  logException(title: string, error: Error, color?: string): void {
    const data = {
      name: error.name,
      message: error.message,
      stack: error.stack
    };
    this.sendLogEntry('error', title, JSON.stringify(data, null, 2), 'json', color ?? '#FF0000');
  }

  /**
   * Logs text with a specific viewer
   */
  logText(title: string, text: string, viewerId: ViewerId = 'data', color?: string): void {
    this.sendLogEntry('text', title, text, viewerId, color);
  }

  /**
   * Logs JSON data
   */
  logJson(title: string, data: unknown, color?: string): void {
    const json = typeof data === 'string' ? data : JSON.stringify(data, null, 2);
    this.sendLogEntry('text', title, json, 'json', color);
  }

  /**
   * Logs XML data
   */
  logXml(title: string, xml: string, color?: string): void {
    this.sendLogEntry('text', title, xml, 'xml', color);
  }

  /**
   * Logs HTML data
   */
  logHtml(title: string, html: string, color?: string): void {
    this.sendLogEntry('text', title, html, 'html', color);
  }

  /**
   * Logs SQL data
   */
  logSql(title: string, sql: string, color?: string): void {
    this.sendLogEntry('text', title, sql, 'sql', color);
  }

  /**
   * Logs a checkpoint marker
   */
  checkpoint(title?: string): void {
    this.sendLogEntry('checkpoint', title ?? 'Checkpoint');
  }

  /**
   * Logs an assertion
   */
  assert(condition: boolean, title: string): void {
    if (!condition) {
      this.sendLogEntry('assert', title, undefined, undefined, '#FF0000');
    }
  }

  // ==================== Process Flow ====================

  /**
   * Logs entering a method
   */
  enterMethod(methodName: string): void {
    this.sendProcessFlow('enterMethod', methodName);
  }

  /**
   * Logs leaving a method
   */
  leaveMethod(methodName: string): void {
    this.sendProcessFlow('leaveMethod', methodName);
  }

  /**
   * Tracks a method execution (enter + leave)
   * @returns A function to call when the method completes
   */
  trackMethod(methodName: string): () => void {
    this.enterMethod(methodName);
    return () => this.leaveMethod(methodName);
  }

  // ==================== Watches ====================

  /**
   * Logs a watch value
   */
  watch(name: string, value: unknown): void {
    const { stringValue, watchType } = this.getWatchTypeAndValue(value);
    this.sendWatch(name, stringValue, watchType);
  }

  /**
   * Logs a string watch
   */
  watchString(name: string, value: string): void {
    this.sendWatch(name, value, 'string');
  }

  /**
   * Logs an integer watch
   */
  watchInt(name: string, value: number): void {
    this.sendWatch(name, Math.floor(value).toString(), 'integer');
  }

  /**
   * Logs a float watch
   */
  watchFloat(name: string, value: number): void {
    this.sendWatch(name, value.toString(), 'float');
  }

  /**
   * Logs a boolean watch
   */
  watchBool(name: string, value: boolean): void {
    this.sendWatch(name, value.toString(), 'boolean');
  }

  /**
   * Logs an object watch
   */
  watchObject(name: string, value: unknown): void {
    this.sendWatch(name, this.serializeValue(value), 'object');
  }

  // ==================== Internal Methods ====================

  /**
   * Core log method
   */
  private log(type: LogEntryType, title: string, data?: unknown, color?: string): void {
    if (!this._active) return;

    const dataStr = data !== undefined ? this.serializeValue(data) : undefined;
    const viewerId: ViewerId | undefined = dataStr ? 'data' : undefined;

    this.sendLogEntry(type, title, dataStr, viewerId, color);
  }

  /**
   * Sends a log entry message
   */
  private sendLogEntry(
    type: LogEntryType,
    title: string,
    data?: string,
    viewerId?: ViewerId,
    color?: string
  ): void {
    if (!this._active) return;

    const message: LogEntryMessage = {
      type: 'logEntry',
      logEntryType: type,
      session: this._name,
      appName: this._appName,
      title,
      data,
      viewerId,
      color: color ?? this._defaultColor
    };

    this.connection.send(message);
  }

  /**
   * Sends a process flow message
   */
  private sendProcessFlow(flowType: ProcessFlowType, title: string): void {
    if (!this._active) return;

    const message: ProcessFlowMessage = {
      type: 'processFlow',
      flowType,
      title
    };

    this.connection.send(message);
  }

  /**
   * Sends a watch message
   */
  private sendWatch(name: string, value: string, watchType: WatchType): void {
    if (!this._active) return;

    const message: WatchMessage = {
      type: 'watch',
      name,
      value,
      watchType
    };

    this.connection.send(message);
  }

  /**
   * Serializes a value to string
   */
  private serializeValue(value: unknown): string {
    if (value === null) return 'null';
    if (value === undefined) return 'undefined';
    if (typeof value === 'string') return value;
    if (typeof value === 'number' || typeof value === 'boolean') return value.toString();

    try {
      return JSON.stringify(value, null, 2);
    } catch {
      return String(value);
    }
  }

  /**
   * Determines watch type from value
   */
  private getWatchTypeAndValue(value: unknown): { stringValue: string; watchType: WatchType } {
    if (value === null || value === undefined) {
      return { stringValue: String(value), watchType: 'string' };
    }

    const type = typeof value;

    switch (type) {
      case 'string':
        return { stringValue: value as string, watchType: 'string' };
      case 'number':
        if (Number.isInteger(value)) {
          return { stringValue: (value as number).toString(), watchType: 'integer' };
        }
        return { stringValue: (value as number).toString(), watchType: 'float' };
      case 'boolean':
        return { stringValue: (value as boolean).toString(), watchType: 'boolean' };
      case 'object':
        return { stringValue: this.serializeValue(value), watchType: 'object' };
      default:
        return { stringValue: String(value), watchType: 'string' };
    }
  }
}
