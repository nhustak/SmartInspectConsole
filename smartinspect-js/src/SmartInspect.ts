import { WebSocketConnection } from './WebSocketConnection';
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
  private connection: WebSocketConnection;
  private sessions: Map<string, Session> = new Map();
  private _appName: string;
  private _enabled: boolean = true;

  /**
   * Creates a new SmartInspect instance
   * @param appName Application name to show in console
   * @param options Configuration options
   */
  constructor(appName: string, options?: SmartInspectOptions) {
    this._appName = appName || 'JavaScript App';
    this.connection = new WebSocketConnection();

    // Apply options
    if (options) {
      if (options.autoReconnect !== undefined) {
        this.connection.autoReconnect = options.autoReconnect;
      }
      if (options.reconnectDelay !== undefined) {
        this.connection.reconnectDelay = options.reconnectDelay;
      }
      if (options.maxReconnectAttempts !== undefined) {
        this.connection.maxReconnectAttempts = options.maxReconnectAttempts;
      }
      if (options.bufferWhenDisconnected !== undefined) {
        this.connection.bufferWhenDisconnected = options.bufferWhenDisconnected;
      }
      if (options.maxBufferSize !== undefined) {
        this.connection.maxBufferSize = options.maxBufferSize;
      }
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
   * Connect to SmartInspect Console
   * @param url WebSocket URL (default: ws://localhost:4229)
   */
  async connect(url: string = 'ws://localhost:4229'): Promise<void> {
    return this.connection.connect(url);
  }

  /**
   * Disconnect from SmartInspect Console
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
