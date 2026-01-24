namespace SmartInspectConsole.Core.Enums;

/// <summary>
/// Represents the type of a control command.
/// </summary>
public enum ControlCommandType
{
    /// <summary>Clear all log entries</summary>
    ClearLog = 0,

    /// <summary>Clear all watches</summary>
    ClearWatches = 1,

    /// <summary>Clear all auto views</summary>
    ClearAutoViews = 2,

    /// <summary>Reset the entire console</summary>
    ClearAll = 3,

    /// <summary>Clear all process flow entries</summary>
    ClearProcessFlow = 4
}
