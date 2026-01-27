import { WebSocketConnection, HttpConnection, type IConnection } from './connections';
import { Session } from './Session';
import type {
  SmartInspectOptions,
  SmartInspectEvents,
  ConnectionState,
  ControlCommandType,
  ControlCommandMessage
} from './types';

/**
 * Main SmartInspect client class
 */
export class SmartInspect {
  private connection: IConnection;
  private sessions: Map<string, Session> = new Map();
  private _appName: string;
  private _enabled: boolean = true;
  private _connectionType: 'websocket' | 'http';

  /**
   * Creates a new SmartInspect instance
   * @param appName Application name to show in console
   * @param options Configuration options
   */
  constructor(appName: string, options?: SmartInspectOptions) {
    this._appName = appName || 'JavaScript App';
    this._connectionType = options?.connectionType || 'websocket';

    // Create appropriate connection based on type
    if (this._connectionType === 'http') {
      this.connection = new HttpConnection(options?.httpOptions);
    } else {
      const wsConnection = new WebSocketConnection();

      // Apply WebSocket-specific options
      if (options) {
        if (options.autoReconnect !== undefined) {
          wsConnection.autoReconnect = options.autoReconnect;
        }
        if (options.reconnectDelay !== undefined) {
          wsConnection.reconnectDelay = options.reconnectDelay;
        }
        if (options.maxReconnectAttempts !== undefined) {
          wsConnection.maxReconnectAttempts = options.maxReconnectAttempts;
        }
        if (options.bufferWhenDisconnected !== undefined) {
          wsConnection.bufferWhenDisconnected = options.bufferWhenDisconnected;
        }
        if (options.maxBufferSize !== undefined) {
          wsConnection.maxBufferSize = options.maxBufferSize;
        }
      }

      this.connection = wsConnection;
    }
  }

  /**
   * Gets or sets the application name
   */
  get appName(): string {
    return this._appName;
  }

  set appName(value: string) {
    this._appName = value;
  }

  /**
   * Gets or sets whether logging is enabled
   */
  get enabled(): boolean {
    return this._enabled;
  }

  set enabled(value: boolean) {
    this._enabled = value;
    // Update all sessions
    for (const session of this.sessions.values()) {
      session.active = value;
    }
  }

  /**
   * Gets the connection type ('websocket' or 'http')
   */
  get connectionType(): 'websocket' | 'http' {
    return this._connectionType;
  }

  /**
   * Gets the current connection state
   */
  get connectionState(): ConnectionState {
    return this.connection.connectionState;
  }

  /**
   * Gets whether connected to the console
   */
  get isConnected(): boolean {
    return this.connection.isConnected;
  }

  /**
   * Sets event handlers
   */
  set events(handlers: SmartInspectEvents) {
    this.connection.events = handlers;
  }

  /**
   * Connect to SmartInspect Console or Relay
   * @param url Connection URL
   *   - WebSocket: 'ws://localhost:4229' (default)
   *   - HTTP: 'https://logs.example.com/api/v1'
   */
  async connect(url?: string): Promise<void> {
    const defaultUrl = this._connectionType === 'http'
      ? 'http://localhost:5000/api/v1'
      : 'ws://localhost:4229';
    return this.connection.connect(url || defaultUrl);
  }

  /**
   * Try to connect to SmartInspect Console without throwing on failure.
   * Useful when logging is optional (e.g., client machines without Console).
   * Messages will be buffered and sent if/when connection succeeds.
   *
   * @param url Connection URL (optional, uses default if not provided)
   * @returns true if connected, false if connection failed
   */
  async tryConnect(url?: string): Promise<boolean> {
    try {
      await this.connect(url);
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Disconnect from SmartInspect Console or Relay
   */
  disconnect(): void {
    this.connection.disconnect();
  }

  /**
   * Add a new logging session
   * @param name Session name
   * @returns The new session
   */
  addSession(name: string): Session {
    let session = this.sessions.get(name);
    if (!session) {
      session = new Session(this.connection, name, this._appName);
      session.active = this._enabled;
      this.sessions.set(name, session);
    }
    return session;
  }

  /**
   * Get an existing session by name
   * @param name Session name
   * @returns The session or undefined
   */
  getSession(name: string): Session | undefined {
    return this.sessions.get(name);
  }

  /**
   * Remove a session
   * @param name Session name
   */
  removeSession(name: string): void {
    this.sessions.delete(name);
  }

  /**
   * Get or create the default 'Main' session
   */
  get mainSession(): Session {
    return this.addSession('Main');
  }

  // ==================== Control Commands ====================

  /**
   * Clears the log in the console
   */
  clearLog(): void {
    this.sendControl('clearLog');
  }

  /**
   * Clears the watches in the console
   */
  clearWatches(): void {
    this.sendControl('clearWatches');
  }

  /**
   * Clears the process flow in the console
   */
  clearProcessFlow(): void {
    this.sendControl('clearProcessFlow');
  }

  /**
   * Clears everything in the console
   */
  clearAll(): void {
    this.sendControl('clearAll');
  }

  /**
   * Sends a control command
   */
  private sendControl(command: ControlCommandType): void {
    const message: ControlCommandMessage = {
      type: 'control',
      command
    };
    this.connection.send(message);
  }
}

/**
 * Global SmartInspect instance for quick access
 */
export const SiAuto = {
  /** The global SmartInspect instance */
  si: new SmartInspect('JavaScript App'),

  /** The main session for quick logging */
  get main(): Session {
    return SiAuto.si.mainSession;
  }
};
