using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Core.Packets;

/// <summary>
/// Represents a control command packet for administrative operations.
/// </summary>
public class ControlCommand : Packet
{
    /// <summary>
    /// The fixed header size for ControlCommand packets (8 bytes).
    /// </summary>
    public const int ControlCommandHeaderSize = 8;

    public override PacketType PacketType => PacketType.ControlCommand;

    /// <summary>
    /// Gets or sets the control command type.
    /// </summary>
    public ControlCommandType ControlCommandType { get; set; }

    /// <summary>
    /// Gets or sets the optional command data.
    /// </summary>
    public byte[]? Data { get; set; }
}
