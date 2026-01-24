namespace SmartInspectConsole.Core.Enums;

/// <summary>
/// Represents the variable type of a watch.
/// </summary>
public enum WatchType
{
    /// <summary>Character value</summary>
    Char = 0,

    /// <summary>String value</summary>
    String = 1,

    /// <summary>Integer value</summary>
    Integer = 2,

    /// <summary>Floating point value</summary>
    Float = 3,

    /// <summary>Boolean value</summary>
    Boolean = 4,

    /// <summary>Memory address</summary>
    Address = 5,

    /// <summary>Timestamp value</summary>
    Timestamp = 6,

    /// <summary>Object value</summary>
    Object = 7
}
