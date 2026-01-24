namespace SmartInspectConsole.Core.Enums;

/// <summary>
/// Represents the type of a process flow entry.
/// </summary>
public enum ProcessFlowType
{
    /// <summary>Enter a method</summary>
    EnterMethod = 0,

    /// <summary>Leave a method</summary>
    LeaveMethod = 1,

    /// <summary>Enter a thread</summary>
    EnterThread = 2,

    /// <summary>Leave a thread</summary>
    LeaveThread = 3,

    /// <summary>Enter a process</summary>
    EnterProcess = 4,

    /// <summary>Leave a process</summary>
    LeaveProcess = 5
}
