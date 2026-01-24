using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Core.Packets;

/// <summary>
/// Represents a log header packet containing connection metadata.
/// </summary>
public class LogHeader : Packet
{
    public override PacketType PacketType => PacketType.LogHeader;

    /// <summary>
    /// Gets or sets the raw content (key=value pairs separated by CRLF).
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the application name from the header.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// Gets or sets the host name from the header.
    /// </summary>
    public string? HostName { get; set; }

    /// <summary>
    /// Parses the content into key-value pairs.
    /// </summary>
    public Dictionary<string, string> ParseContent()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(Content))
            return result;

        var lines = Content.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var idx = line.IndexOf('=');
            if (idx > 0)
            {
                var key = line[..idx].Trim();
                var value = line[(idx + 1)..].Trim();
                result[key] = value;
            }
        }

        // Extract known values
        if (result.TryGetValue("appname", out var appName))
            AppName = appName;
        if (result.TryGetValue("hostname", out var hostName))
            HostName = hostName;

        return result;
    }
}
