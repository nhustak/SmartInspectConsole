import type { Message, ConnectionState, SmartInspectEvents } from '../types';

/**
 * Interface for SmartInspect connections.
 * Allows swapping between WebSocket and HTTP transports.
 */
export interface IConnection {
  /** Current connection state */
  readonly connectionState: ConnectionState;

  /** Whether the connection is active */
  readonly isConnected: boolean;

  /** Event handlers */
  events: SmartInspectEvents;

  /** Connect to the endpoint */
  connect(url: string): Promise<void>;

  /** Disconnect from the endpoint */
  disconnect(): void;

  /** Send a message */
  send(message: Message): void;
}
