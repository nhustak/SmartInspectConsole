namespace SmartInspectConsole.Core.Enums;

/// <summary>
/// Represents the type of a log entry, determining its icon and behavior in the console.
/// </summary>
public enum LogEntryType
{
    // Control types (0-99)
    Separator = 0,
    EnterMethod = 1,
    LeaveMethod = 2,
    ResetCallstack = 3,

    // Message types (100-199)
    Message = 100,
    Warning = 101,
    Error = 102,
    InternalError = 103,
    Comment = 104,
    VariableValue = 105,
    Checkpoint = 106,
    Debug = 107,
    Verbose = 108,
    Fatal = 109,
    Conditional = 110,
    Assert = 111,

    // Data types (200-299)
    Text = 200,
    Binary = 201,
    Graphic = 202,
    Source = 203,
    Object = 204,
    WebContent = 205,
    System = 206,
    MemoryStatistic = 207,
    DatabaseResult = 208,
    DatabaseStructure = 209
}
