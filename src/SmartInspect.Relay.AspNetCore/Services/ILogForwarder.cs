namespace SmartInspect.Relay.AspNetCore.Services;

/// <summary>
/// Interface for forwarding browser log messages to a destination.
/// </summary>
public interface ILogForwarder : IDisposable
{
    /// <summary>
    /// Whether the forwarder is connected and ready to send messages.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Total number of messages successfully forwarded.
    /// </summary>
    long MessagesForwarded { get; }

    /// <summary>
    /// Number of messages currently buffered (waiting to be sent).
    /// </summary>
    int MessagesBuffered { get; }

    /// <summary>
    /// Timestamp of the last successfully forwarded message.
    /// </summary>
    DateTime? LastForwardedAt { get; }

    /// <summary>
    /// Start the forwarder (establish connections, etc.).
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the forwarder gracefully.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Forward a single JSON message.
    /// </summary>
    /// <param name="json">The JSON message to forward.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the message was forwarded, false if buffered.</returns>
    Task<bool> ForwardAsync(string json, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forward multiple JSON messages.
    /// </summary>
    /// <param name="messages">The JSON messages to forward.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of messages successfully forwarded (vs buffered).</returns>
    Task<int> ForwardBatchAsync(IEnumerable<string> messages, CancellationToken cancellationToken = default);
}
