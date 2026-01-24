namespace SmartInspectConsole.Core.Events;

/// <summary>
/// Event arguments for client connection/disconnection events.
/// </summary>
public class ClientEventArgs : EventArgs
{
    /// <summary>
    /// Gets the client identifier.
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// Gets the client banner received during handshake.
    /// </summary>
    public string? ClientBanner { get; }

    /// <summary>
    /// Gets the application name from the log header, if available.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// Gets the host name from the log header, if available.
    /// </summary>
    public string? HostName { get; set; }

    public ClientEventArgs(string clientId, string? clientBanner = null)
    {
        ClientId = clientId;
        ClientBanner = clientBanner;
    }
}
