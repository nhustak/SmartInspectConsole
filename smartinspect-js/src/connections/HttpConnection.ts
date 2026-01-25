import type { Message, ConnectionState, SmartInspectEvents, LogEntryMessage } from '../types';
import type { IConnection } from './IConnection';

/**
 * Configuration options for HTTP connection
 */
export interface HttpConnectionOptions {
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
    maxAttempts?: number;      // default: 3
    baseDelay?: number;        // default: 1000ms
    maxDelay?: number;         // default: 30000ms
  };

  /** Enable gzip compression for large payloads (default: true) */
  enableCompression?: boolean;

  /** Minimum payload size for compression in bytes (default: 1024) */
  compressionThreshold?: number;

  /** Unique client identifier (auto-generated if not provided) */
  clientId?: string;

  /** Include page metadata (URL, user agent) in requests */
  includeMetadata?: boolean;
}

/**
 * Batch request sent to relay server
 */
interface BatchRequest {
  messages: Message[];
  clientId: string;
  metadata?: {
    userAgent: string;
    url: string;
    timestamp: string;
  };
}

/**
 * HTTP POST-based connection for production environments.
 * Supports batching, retry, compression, and sendBeacon for page unload.
 */
export class HttpConnection implements IConnection {
  private state: ConnectionState = 'disconnected';
  private buffer: Message[] = [];
  private flushTimer: number | null = null;
  private endpoint: string = '';
  private unloadHandler: (() => void) | null = null;
  private visibilityHandler: (() => void) | null = null;

  // Configuration with defaults
  public apiKey?: string;
  public flushInterval: number = 1000;
  public maxBatchSize: number = 50;
  public maxBufferSize: number = 1000;
  public retry = { maxAttempts: 3, baseDelay: 1000, maxDelay: 30000 };
  public enableCompression: boolean = true;
  public compressionThreshold: number = 1024;
  public clientId: string;
  public includeMetadata: boolean = true;

  public events: SmartInspectEvents = {};

  constructor(options?: HttpConnectionOptions) {
    this.clientId = options?.clientId || this.generateClientId();

    if (options) {
      if (options.apiKey !== undefined) this.apiKey = options.apiKey;
      if (options.flushInterval !== undefined) this.flushInterval = options.flushInterval;
      if (options.maxBatchSize !== undefined) this.maxBatchSize = options.maxBatchSize;
      if (options.maxBufferSize !== undefined) this.maxBufferSize = options.maxBufferSize;
      if (options.retry) this.retry = { ...this.retry, ...options.retry };
      if (options.enableCompression !== undefined) this.enableCompression = options.enableCompression;
      if (options.compressionThreshold !== undefined) this.compressionThreshold = options.compressionThreshold;
      if (options.includeMetadata !== undefined) this.includeMetadata = options.includeMetadata;
    }
  }

  get connectionState(): ConnectionState {
    return this.state;
  }

  get isConnected(): boolean {
    return this.state === 'connected';
  }

  /**
   * Connect to the relay endpoint
   * @param url Base URL for the relay (e.g., 'https://logs.example.com/api/v1')
   */
  async connect(url: string): Promise<void> {
    this.endpoint = url.endsWith('/') ? url.slice(0, -1) : url;
    this.setState('connecting');

    try {
      // Verify endpoint is reachable via health check
      const response = await fetch(`${this.endpoint}/health`, {
        method: 'GET',
        headers: this.buildHeaders()
      });

      if (!response.ok) {
        throw new Error(`Health check failed: ${response.status}`);
      }

      this.setState('connected');
      this.startFlushTimer();
      this.setupUnloadHandlers();
    } catch (error) {
      this.setState('disconnected');
      throw error;
    }
  }

  /**
   * Disconnect from the relay endpoint
   */
  disconnect(): void {
    this.stopFlushTimer();
    this.removeUnloadHandlers();

    // Final sync flush using beacon
    this.flushSync();

    this.setState('disconnected');
  }

  /**
   * Send a message to the relay
   */
  send(message: Message): void {
    this.buffer.push(message);

    // Immediate flush for critical messages
    if (this.isCriticalMessage(message)) {
      this.flush();
      return;
    }

    // Flush if batch size reached
    if (this.buffer.length >= this.maxBatchSize) {
      this.flush();
    }

    // Trim buffer if too large
    while (this.buffer.length > this.maxBufferSize) {
      this.buffer.shift();
    }
  }

  /**
   * Flush buffered messages to the relay (async)
   */
  private async flush(): Promise<void> {
    if (this.buffer.length === 0) return;

    const messages = [...this.buffer];
    this.buffer = [];

    const payload = this.buildPayload(messages);
    const body = JSON.stringify(payload);

    try {
      const response = await this.sendWithRetry(body);

      if (!response.ok) {
        // Re-buffer messages on failure
        this.buffer = [...messages, ...this.buffer];
        this.trimBuffer();
      }
    } catch (error) {
      // Re-buffer messages on network error
      this.buffer = [...messages, ...this.buffer];
      this.trimBuffer();
      this.events.onError?.(error as Error);
    }
  }

  /**
   * Synchronous flush using sendBeacon (for page unload)
   */
  private flushSync(): void {
    if (this.buffer.length === 0) return;

    const messages = [...this.buffer];
    this.buffer = [];

    const payload = this.buildPayload(messages);
    const body = JSON.stringify(payload);

    // Use sendBeacon for reliable delivery on page close
    if (typeof navigator !== 'undefined' && navigator.sendBeacon) {
      const blob = new Blob([body], { type: 'application/json' });
      navigator.sendBeacon(`${this.endpoint}/logs`, blob);
    }
  }

  /**
   * Send request with retry and exponential backoff
   */
  private async sendWithRetry(body: string): Promise<Response> {
    let lastError: Error | null = null;

    for (let attempt = 0; attempt <= this.retry.maxAttempts; attempt++) {
      try {
        const headers = this.buildHeaders();
        let finalBody: BodyInit = body;

        // Compress if enabled and payload is large enough
        if (this.enableCompression && body.length > this.compressionThreshold && this.canCompress()) {
          try {
            const compressed = await this.compress(body);
            // Convert to ArrayBuffer slice for TypeScript compatibility
            const arrayBuffer = compressed.buffer.slice(
              compressed.byteOffset,
              compressed.byteOffset + compressed.byteLength
            ) as ArrayBuffer;
            finalBody = new Blob([arrayBuffer], { type: 'application/json' });
            headers['Content-Encoding'] = 'gzip';
          } catch {
            // Fall back to uncompressed if compression fails
            finalBody = body;
          }
        }

        const response = await fetch(`${this.endpoint}/logs`, {
          method: 'POST',
          headers,
          body: finalBody
        });

        return response;
      } catch (error) {
        lastError = error as Error;

        if (attempt < this.retry.maxAttempts) {
          const delay = Math.min(
            this.retry.baseDelay * Math.pow(2, attempt),
            this.retry.maxDelay
          );
          await this.sleep(delay);
        }
      }
    }

    throw lastError || new Error('Request failed after retries');
  }

  /**
   * Build request headers
   */
  private buildHeaders(): Record<string, string> {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json'
    };

    if (this.apiKey) {
      headers['X-Api-Key'] = this.apiKey;
    }

    headers['X-Request-Id'] = this.generateRequestId();

    return headers;
  }

  /**
   * Build batch payload
   */
  private buildPayload(messages: Message[]): BatchRequest {
    const payload: BatchRequest = {
      messages,
      clientId: this.clientId
    };

    if (this.includeMetadata && typeof window !== 'undefined') {
      payload.metadata = {
        userAgent: navigator.userAgent,
        url: window.location.href,
        timestamp: new Date().toISOString()
      };
    }

    return payload;
  }

  /**
   * Check if message is critical (error/fatal) and needs immediate flush
   */
  private isCriticalMessage(message: Message): boolean {
    if (message.type === 'logEntry') {
      const logMessage = message as LogEntryMessage;
      return logMessage.logEntryType === 'fatal' || logMessage.logEntryType === 'error';
    }
    return false;
  }

  /**
   * Start periodic flush timer
   */
  private startFlushTimer(): void {
    this.stopFlushTimer();
    this.flushTimer = window.setInterval(() => this.flush(), this.flushInterval);
  }

  /**
   * Stop flush timer
   */
  private stopFlushTimer(): void {
    if (this.flushTimer !== null) {
      window.clearInterval(this.flushTimer);
      this.flushTimer = null;
    }
  }

  /**
   * Setup handlers for page unload to flush remaining messages
   */
  private setupUnloadHandlers(): void {
    if (typeof window === 'undefined') return;

    this.unloadHandler = () => this.flushSync();
    this.visibilityHandler = () => {
      if (document.visibilityState === 'hidden') {
        this.flushSync();
      }
    };

    window.addEventListener('beforeunload', this.unloadHandler);
    document.addEventListener('visibilitychange', this.visibilityHandler);
  }

  /**
   * Remove unload handlers
   */
  private removeUnloadHandlers(): void {
    if (typeof window === 'undefined') return;

    if (this.unloadHandler) {
      window.removeEventListener('beforeunload', this.unloadHandler);
      this.unloadHandler = null;
    }

    if (this.visibilityHandler) {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
      this.visibilityHandler = null;
    }
  }

  /**
   * Set connection state and fire events
   */
  private setState(state: ConnectionState): void {
    const previousState = this.state;
    this.state = state;

    if (previousState !== state) {
      this.events.onStateChange?.(state);

      if (state === 'connected') {
        this.events.onConnected?.();
      } else if (state === 'disconnected') {
        this.events.onDisconnected?.();
      }
    }
  }

  /**
   * Trim buffer to max size
   */
  private trimBuffer(): void {
    while (this.buffer.length > this.maxBufferSize) {
      this.buffer.shift();
    }
  }

  /**
   * Check if compression is available
   */
  private canCompress(): boolean {
    return typeof CompressionStream !== 'undefined';
  }

  /**
   * Compress data using gzip
   */
  private async compress(data: string): Promise<Uint8Array> {
    const encoder = new TextEncoder();
    const stream = new CompressionStream('gzip');
    const writer = stream.writable.getWriter();
    writer.write(encoder.encode(data));
    writer.close();

    const chunks: Uint8Array[] = [];
    const reader = stream.readable.getReader();

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      if (value) chunks.push(value);
    }

    const totalLength = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
    const result = new Uint8Array(totalLength);
    let offset = 0;
    for (const chunk of chunks) {
      result.set(chunk, offset);
      offset += chunk.length;
    }

    return result;
  }

  /**
   * Generate unique client ID
   */
  private generateClientId(): string {
    return `browser-${Date.now()}-${Math.random().toString(36).substring(2, 11)}`;
  }

  /**
   * Generate unique request ID
   */
  private generateRequestId(): string {
    return `req-${Date.now()}-${Math.random().toString(36).substring(2, 8)}`;
  }

  /**
   * Sleep for specified milliseconds
   */
  private sleep(ms: number): Promise<void> {
    return new Promise(resolve => setTimeout(resolve, ms));
  }
}
