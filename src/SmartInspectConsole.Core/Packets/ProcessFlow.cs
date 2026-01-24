using SmartInspectConsole.Core.Enums;

namespace SmartInspectConsole.Core.Packets;

/// <summary>
/// Represents a process flow packet for tracking method entry/exit and threads.
/// </summary>
public class ProcessFlow : Packet
{
    /// <summary>
    /// The fixed header size for ProcessFlow packets (28 bytes).
    /// </summary>
    public const int ProcessFlowHeaderSize = 28;

    public override PacketType PacketType => PacketType.ProcessFlow;

    /// <summary>
    /// Gets or sets the process flow type.
    /// </summary>
    public ProcessFlowType ProcessFlowType { get; set; }

    /// <summary>
    /// Gets or sets the title (method name, thread name, etc.).
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the host name.
    /// </summary>
    public string HostName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the process ID.
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Gets or sets the thread ID.
    /// </summary>
    public int ThreadId { get; set; }
}
