using System.Drawing;
using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Core.Packets;

/// <summary>
/// Represents a log entry packet - the main logging packet type.
/// </summary>
public class LogEntry : Packet
{
    /// <summary>
    /// The fixed header size for LogEntry packets (48 bytes).
    /// </summary>
    public const int LogEntryHeaderSize = 48;

    public override PacketType PacketType => PacketType.LogEntry;

    /// <summary>
    /// Gets or sets the type of this log entry.
    /// </summary>
    public LogEntryType LogEntryType { get; set; }

    /// <summary>
    /// Gets or sets the viewer ID for displaying the data.
    /// </summary>
    public ViewerId ViewerId { get; set; }

    /// <summary>
    /// Gets or sets the application name.
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the session name.
    /// </summary>
    public string SessionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the log entry title/message.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the host name.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the binary data payload.
    /// </summary>
    public byte[]? Data { get; set; }

    /// <summary>
    /// Gets or sets the process ID.
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Gets or sets the thread ID.
    /// </summary>
    public int ThreadId { get; set; }

    /// <summary>
    /// Gets or sets the display color.
    /// </summary>
    public Color Color { get; set; }

    /// <summary>
    /// Gets the data as a UTF-8 string if available.
    /// </summary>
    public string? DataAsString => Data != null ? System.Text.Encoding.UTF8.GetString(Data) : null;
}
