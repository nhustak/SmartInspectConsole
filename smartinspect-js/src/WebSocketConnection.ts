import type { Message, ConnectionState, SmartInspectEvents } from './types';

/**
 * Manages WebSocket connection to SmartInspect Console
 */
export class WebSocketConnection {
  private ws: WebSocket | null = null;
  private url: string = '';
  private state: ConnectionState = 'disconnected';
  private reconnectAttempts: number = 0;
  private reconnectTimer: number | null = null;
  private buffer: Message[] = [];

  // Configuration
  public autoReconnect: boolean = true;
  public reconnectDelay: number = 2000;
  public maxReconnectAttempts: number = 0;
  public bufferWhenDisconnected: boolean = true;
  public maxBufferSize: number = 1000;

  // Event handlers
  public events: SmartInspectEvents = {};

  /**
   * Gets the current connection state
   */
  get connectionState(): ConnectionState {
    return this.state;
  }

  /**
   * Gets whether the connection is active
   */
  get isConnected(): boolean {
    return this.state === 'connected';
  }

  /**
   * Connect to SmartInspect Console
   * @param url WebSocket URL (e.g., 'ws://localhost:4229')
   */
  async connect(url: string): Promise<void> {
    if (this.ws) {
      this.disconnect();
    }

    this.url = url;
    this.setState('connecting');

    return new Promise((resolve, reject) => {
      try {
        this.ws = new WebSocket(url);

        this.ws.onopen = () => {
          this.setState('connected');
          this.reconnectAttempts = 0;
          this.flushBuffer();
          resolve();
        };

        this.ws.onclose = () => {
          this.handleDisconnect();
        };

        this.ws.onerror = (event) => {
          const error = new Error('WebSocket connection error');
          this.events.onError?.(error);
          if (this.state === 'connecting') {
            reject(error);
          }
        };

        this.ws.onmessage = (event) => {
          // Handle incoming messages if needed (future: bidirectional)
        };
      } catch (error) {
        this.setState('disconnected');
        reject(error);
      }
    });
  }

  /**
   * Disconnect from SmartInspect Console
   */
  disconnect(): void {
    this.autoReconnect = false;
    this.clearReconnectTimer();

    if (this.ws) {
      this.ws.onclose = null;
      this.ws.close();
      this.ws = null;
    }

    this.setState('disconnected');
  }

  /**
   * Send a message to the console
   */
  send(message: Message): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(message));
    } else if (this.bufferWhenDisconnected) {
      this.addToBuffer(message);
    }
  }

  /**
   * Handle disconnection
   */
  private handleDisconnect(): void {
    this.ws = null;

    if (this.autoReconnect) {
      this.attemptReconnect();
    } else {
      this.setState('disconnected');
    }
  }

  /**
   * Attempt to reconnect
   */
  private attemptReconnect(): void {
    if (this.maxReconnectAttempts > 0 && this.reconnectAttempts >= this.maxReconnectAttempts) {
      this.setState('disconnected');
      return;
    }

    this.setState('reconnecting');
    this.reconnectAttempts++;

    this.clearReconnectTimer();
    this.reconnectTimer = window.setTimeout(() => {
      this.connect(this.url).catch(() => {
        // Will trigger another reconnect via onclose
      });
    }, this.reconnectDelay);
  }

  /**
   * Clear reconnect timer
   */
  private clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) {
      window.clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }
  }

  /**
   * Set connection state and fire event
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
   * Add message to buffer
   */
  private addToBuffer(message: Message): void {
    this.buffer.push(message);

    // Trim buffer if it exceeds max size
    while (this.buffer.length > this.maxBufferSize) {
      this.buffer.shift();
    }
  }

  /**
   * Flush buffered messages
   */
  private flushBuffer(): void {
    if (this.ws && this.ws.readyState === WebSocket.OPEN) {
      for (const message of this.buffer) {
        this.ws.send(JSON.stringify(message));
      }
      this.buffer = [];
    }
  }
}
