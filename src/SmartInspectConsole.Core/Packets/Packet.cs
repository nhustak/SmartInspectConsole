using System.Drawing;
using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Core.Packets;

/// <summary>
/// Base class for all SmartInspect packets.
/// </summary>
public abstract class Packet
{
    /// <summary>
    /// The packet header size in bytes (2 bytes type + 4 bytes size).
    /// </summary>
    public const int HeaderSize = 6;

    /// <summary>
    /// Gets the packet type.
    /// </summary>
    public abstract PacketType PacketType { get; }

    /// <summary>
    /// Gets or sets the timestamp when this packet was created.
    /// </summary>
    public DateTime Timestamp { get; set; }
}
