using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Core.Events;

/// <summary>
/// Event arguments for when a packet is received.
/// </summary>
public class PacketReceivedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the received packet.
    /// </summary>
    public Packet Packet { get; }

    /// <summary>
    /// Gets the client identifier that sent the packet.
    /// </summary>
    public string ClientId { get; }

    public PacketReceivedEventArgs(Packet packet, string clientId)
    {
        Packet = packet;
        ClientId = clientId;
    }
}
