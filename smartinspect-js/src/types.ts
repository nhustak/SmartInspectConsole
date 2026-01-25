/**
 * Log entry types matching SmartInspect protocol
 */
export type LogEntryType =
  | 'message'
  | 'warning'
  | 'error'
  | 'debug'
  | 'verbose'
  | 'fatal'
  | 'separator'
  | 'enterMethod'
  | 'leaveMethod'
  | 'comment'
  | 'checkpoint'
  | 'assert'
  | 'text'
  | 'object'
  | 'binary';

/**
 * Viewer types for displaying log data
 */
export type ViewerId =
  | 'title'
  | 'data'
  | 'list'
  | 'valueList'
  | 'inspector'
  | 'table'
  | 'web'
  | 'binary'
  | 'json'
  | 'xml'
  | 'html'
  | 'sql'
  | 'python';

/**
 * Watch value types
 */
export type WatchType =
  | 'string'
  | 'integer'
  | 'float'
  | 'boolean'
  | 'char'
  | 'address'
  | 'timestamp'
  | 'object';

/**
 * Process flow types
 */
export type ProcessFlowType =
  | 'enterMethod'
  | 'leaveMethod'
  | 'enterThread'
  | 'leaveThread'
  | 'enterProcess'
  | 'leaveProcess';

/**
 * Control command types
 */
export type ControlCommandType =
  | 'clearLog'
  | 'clearWatches'
  | 'clearAll'
  | 'clearProcessFlow';

/**
 * Connection state
 */
export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * Log entry message sent to console
 */
export interface LogEntryMessage {
  type: 'logEntry';
  logEntryType: LogEntryType;
  session: string;
  appName?: string;
  title: string;
  data?: string;
  viewerId?: ViewerId;
  color?: string;
  timestamp?: string;
  threadId?: number;
}

/**
 * Watch message sent to console
 */
export interface WatchMessage {
  type: 'watch';
  name: string;
  value: string;
  watchType: WatchType;
}

/**
 * Process flow message sent to console
 */
export interface ProcessFlowMessage {
  type: 'processFlow';
  flowType: ProcessFlowType;
  title: string;
}

/**
 * Control command message sent to console
 */
export interface ControlCommandMessage {
  type: 'control';
  command: ControlCommandType;
}

/**
 * Any message type
 */
export type Message = LogEntryMessage | WatchMessage | ProcessFlowMessage | ControlCommandMessage;

/**
 * Connection type for SmartInspect
 */
export type ConnectionType = 'websocket' | 'http';

/**
 * HTTP connection options (when connectionType is 'http')
 */
export interface HttpOptions {
  /** API key for authentication (optional) */
  apiKey?: string;
  /** Batch flush interval in milliseconds (default: 1000) */
  flushInterval?: number;
  /** Maximum batch size before forced flush (default: 50) */
  maxBatchSize?: number;
  /** Maximum buffer size when endpoint unavailable (default: 1000) */
  maxBufferSize?: number;
  /** Retry configuration */
  retry?: {
    maxAttempts?: number;
    baseDelay?: number;
    maxDelay?: number;
  };
  /** Enable gzip compression for large payloads (default: true) */
  enableCompression?: boolean;
  /** Minimum payload size for compression (default: 1024) */
  compressionThreshold?: number;
  /** Unique client identifier (auto-generated if not provided) */
  clientId?: string;
  /** Include page metadata in requests (default: true) */
  includeMetadata?: boolean;
}

/**
 * Configuration options for SmartInspect
 */
export interface SmartInspectOptions {
  /** Application name shown in console */
  appName?: string;
  /** Connection type: 'websocket' (default) or 'http' */
  connectionType?: ConnectionType;
  /** HTTP connection options (when connectionType is 'http') */
  httpOptions?: HttpOptions;
  /** Auto-connect on creation */
  autoConnect?: boolean;
  /** Reconnect automatically on disconnect (WebSocket only) */
  autoReconnect?: boolean;
  /** Reconnect delay in milliseconds (WebSocket only) */
  reconnectDelay?: number;
  /** Maximum reconnect attempts, 0 = unlimited (WebSocket only) */
  maxReconnectAttempts?: number;
  /** Enable buffering when disconnected (WebSocket only) */
  bufferWhenDisconnected?: boolean;
  /** Maximum buffer size (WebSocket only) */
  maxBufferSize?: number;
}

/**
 * Event handlers
 */
export interface SmartInspectEvents {
  onConnected?: () => void;
  onDisconnected?: () => void;
  onError?: (error: Error) => void;
  onStateChange?: (state: ConnectionState) => void;
}
