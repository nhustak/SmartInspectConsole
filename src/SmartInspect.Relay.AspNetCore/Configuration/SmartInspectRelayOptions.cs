namespace SmartInspect.Relay.AspNetCore.Configuration;

/// <summary>
/// Configuration options for the SmartInspect relay.
/// </summary>
public class SmartInspectRelayOptions
{
    /// <summary>
    /// Forwarding mode. Default is WebSocket (connect to SmartInspect Console).
    /// </summary>
    public RelayMode Mode { get; set; } = RelayMode.WebSocket;

    // ===== WebSocket Mode Settings =====

    /// <summary>
    /// Host address of the SmartInspect Console. Default: localhost.
    /// Only used when Mode is WebSocket.
    /// </summary>
    public string ConsoleHost { get; set; } = "localhost";

    /// <summary>
    /// WebSocket port of the SmartInspect Console. Default: 4229.
    /// Only used when Mode is WebSocket.
    /// </summary>
    public int ConsolePort { get; set; } = 4229;

    // ===== Callback Mode Settings =====

    /// <summary>
    /// Callback for forwarding log entry messages.
    /// Parameters: level, title, data, viewerType
    /// Only used when Mode is Callback.
    /// </summary>
    public Action<string, string, string?, string?>? OnLogEntry { get; set; }

    /// <summary>
    /// Callback for forwarding watch messages.
    /// Parameters: name, value, watchType
    /// Only used when Mode is Callback.
    /// </summary>
    public Action<string, string, string>? OnWatch { get; set; }

    /// <summary>
    /// Callback for forwarding process flow messages.
    /// Parameters: flowType, title
    /// Only used when Mode is Callback.
    /// </summary>
    public Action<string, string>? OnProcessFlow { get; set; }

    /// <summary>
    /// Callback for forwarding control commands.
    /// Parameters: command
    /// Only used when Mode is Callback.
    /// </summary>
    public Action<string>? OnControl { get; set; }

    // ===== Common Settings =====

    /// <summary>
    /// Maximum number of messages to buffer when the console is unavailable.
    /// Default: 10000.
    /// </summary>
    public int BufferSize { get; set; } = 10000;

    /// <summary>
    /// Delay in milliseconds between reconnection attempts.
    /// Default: 5000 (5 seconds).
    /// Only used when Mode is WebSocket.
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 5000;

    /// <summary>
    /// Maximum number of reconnection attempts. 0 = unlimited.
    /// Default: 0 (unlimited).
    /// Only used when Mode is WebSocket.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 0;

    /// <summary>
    /// Enable CORS for the relay endpoints.
    /// Default: true.
    /// </summary>
    public bool EnableCors { get; set; } = true;

    /// <summary>
    /// CORS origins to allow. Empty array = allow all origins.
    /// Default: empty (all origins).
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Specifies how the relay forwards browser logs.
/// </summary>
public enum RelayMode
{
    /// <summary>
    /// Forward logs to SmartInspect Console via WebSocket connection.
    /// Use this when running the SmartInspect Console application.
    /// </summary>
    WebSocket,

    /// <summary>
    /// Forward logs through callback functions.
    /// Use this when your website already uses SmartInspect for server-side logging.
    /// Configure OnLogEntry, OnWatch, OnProcessFlow, OnControl callbacks.
    /// </summary>
    Callback
}
