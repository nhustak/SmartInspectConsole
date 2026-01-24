using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Core.Packets;

/// <summary>
/// Represents a watch packet for variable monitoring.
/// </summary>
public class Watch : Packet
{
    /// <summary>
    /// The fixed header size for Watch packets (20 bytes).
    /// </summary>
    public const int WatchHeaderSize = 20;

    public override PacketType PacketType => PacketType.Watch;

    /// <summary>
    /// Gets or sets the watch variable name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the watch value as a string.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the watch type.
    /// </summary>
    public WatchType WatchType { get; set; }
}
