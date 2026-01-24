namespace SmartInspectConsole.Core.Enums;

/// <summary>
/// Represents the type of a packet in the SmartInspect protocol.
/// </summary>
public enum PacketType : short
{
    /// <summary>Control command (clear log, clear watches, etc.)</summary>
    ControlCommand = 1,

    /// <summary>Log entry (the main logging packet)</summary>
    LogEntry = 4,

    /// <summary>Watch (variable monitoring)</summary>
    Watch = 5,

    /// <summary>Process flow (method enter/leave, thread tracking)</summary>
    ProcessFlow = 6,

    /// <summary>Log header (connection metadata)</summary>
    LogHeader = 7
}
