using System.Drawing;
using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Core.Packets;

/// <summary>
/// Represents a log entry packet - the main logging packet type.
/// </summary>
public class LogEntry : Packet
{
    private byte[]? _data;
    private string? _dataAsString;
    private bool _hasDecodedData;

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
    /// Gets the title normalized to a single display line for list views.
    /// </summary>
    public string SingleLineTitle
    {
        get
        {
            if (string.IsNullOrEmpty(Title))
                return string.Empty;

            return Title
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ');
        }
    }

    /// <summary>
    /// Gets or sets the host name.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the binary data payload.
    /// </summary>
    public byte[]? Data
    {
        get => _data;
        set
        {
            _data = value;
            _dataAsString = null;
            _hasDecodedData = false;
        }
    }

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
    /// Gets or sets the elapsed time since the previous log entry.
    /// </summary>
    public TimeSpan ElapsedTime { get; set; }

    /// <summary>
    /// Gets the elapsed time formatted for display.
    /// Shows milliseconds for short durations, seconds for longer ones.
    /// </summary>
    public string ElapsedTimeFormatted
    {
        get
        {
            if (ElapsedTime == TimeSpan.Zero)
                return "-";

            if (ElapsedTime.TotalMilliseconds < 1000)
                return $"{ElapsedTime.TotalMilliseconds:F1}ms";

            if (ElapsedTime.TotalSeconds < 60)
                return $"{ElapsedTime.TotalSeconds:F2}s";

            return ElapsedTime.ToString(@"mm\:ss\.fff");
        }
    }

    /// <summary>
    /// Gets the data as a UTF-8 string if available.
    /// </summary>
    public string? DataAsString
    {
        get
        {
            if (_hasDecodedData)
                return _dataAsString;

            _dataAsString = _data != null ? System.Text.Encoding.UTF8.GetString(_data) : null;
            _hasDecodedData = true;
            return _dataAsString;
        }
    }
}
