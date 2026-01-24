using SmartInspectConsole.Core.Events;

namespace SmartInspectConsole.Core.Listeners;

/// <summary>
/// Interface for packet listeners (TCP, Pipe, etc.).
/// </summary>
public interface IPacketListener : IDisposable
{
    /// <summary>
    /// Raised when a packet is received.
    /// </summary>
    event EventHandler<PacketReceivedEventArgs>? PacketReceived;

    /// <summary>
    /// Raised when a client connects.
    /// </summary>
    event EventHandler<ClientEventArgs>? ClientConnected;

    /// <summary>
    /// Raised when a client disconnects.
    /// </summary>
    event EventHandler<ClientEventArgs>? ClientDisconnected;

    /// <summary>
    /// Raised when an error occurs.
    /// </summary>
    event EventHandler<Exception>? Error;

    /// <summary>
    /// Gets whether the listener is currently running.
    /// </summary>
    bool IsListening { get; }

    /// <summary>
    /// Starts the listener.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the listener.
    /// </summary>
    Task StopAsync();
}
