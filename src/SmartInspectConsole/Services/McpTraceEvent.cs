namespace SmartInspectConsole.Services;

public sealed record McpTraceEvent
{
    public required DateTime TimestampUtc { get; init; }
    public required string Title { get; init; }
    public required string Data { get; init; }
    public required bool IsError { get; init; }
}
