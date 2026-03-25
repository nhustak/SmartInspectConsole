namespace SmartInspectConsole.Contracts;

public sealed record LogQueryRequest
{
    public IReadOnlyList<string>? AppNames { get; init; }
    public IReadOnlyList<string>? SessionNames { get; init; }
    public IReadOnlyList<string>? HostNames { get; init; }
    public IReadOnlyList<int>? ProcessIds { get; init; }
    public IReadOnlyList<int>? ThreadIds { get; init; }
    public IReadOnlyList<string>? Levels { get; init; }
    public string? Text { get; init; }
    public string? Cursor { get; init; }
    public int Limit { get; init; } = 100;
    public bool IncludeData { get; init; }
    public bool FlaggedOnly { get; init; }
}

public sealed record LogQueryResponse
{
    public required IReadOnlyList<LogEntryDto> Items { get; init; }
    public required int ReturnedCount { get; init; }
    public required bool HasMore { get; init; }
    public string? NextCursor { get; init; }
    public required int AppliedLimit { get; init; }
    public required string RunId { get; init; }
}

public sealed record LogEntryDto
{
    public required string EntryId { get; init; }
    public required long Sequence { get; init; }
    public required string RunId { get; init; }
    public required DateTime TimestampUtc { get; init; }
    public double? ElapsedMs { get; init; }
    public required string Type { get; init; }
    public required string ViewerId { get; init; }
    public required string AppName { get; init; }
    public required string SessionName { get; init; }
    public required string HostName { get; init; }
    public required int ProcessId { get; init; }
    public required int ThreadId { get; init; }
    public required string Title { get; init; }
    public string? DataText { get; init; }
    public required bool HasData { get; init; }
    public required bool IsFlagged { get; init; }
}

public sealed record ApplicationSummaryDto
{
    public required string ClientId { get; init; }
    public required string ApplicationKey { get; init; }
    public required string AppName { get; init; }
    public required string HostName { get; init; }
    public required bool IsConnected { get; init; }
    public required bool IsMuted { get; init; }
    public required long MessageCount { get; init; }
    public DateTime? ConnectedAtUtc { get; init; }
    public DateTime? LastSeenUtc { get; init; }
    public DateTime? DisconnectedAtUtc { get; init; }
}

public sealed record ListenerStatusDto
{
    public required string Transport { get; init; }
    public required bool Enabled { get; init; }
    public required string Endpoint { get; init; }
    public required int ClientCount { get; init; }
}

public sealed record LiveContextDto
{
    public required string RunId { get; init; }
    public required DateTime StartedAtUtc { get; init; }
    public required IReadOnlyList<ListenerStatusDto> ListenerStatus { get; init; }
    public required int LogEntryCount { get; init; }
    public required int WatchCount { get; init; }
    public required int ProcessFlowCount { get; init; }
    public required int ConnectedApplicationCount { get; init; }
    public required int MaxLogEntries { get; init; }
    public required int QueueDepth { get; init; }
    public DateTime? LastEntryUtc { get; init; }
    public required long TotalReceived { get; init; }
    public required long TotalRetained { get; init; }
    public required long TotalDroppedByRetention { get; init; }
}

public sealed record FlagEntryRequest
{
    public required string EntryId { get; init; }
    public string? Category { get; init; }
    public string? Reason { get; init; }
}

public sealed record FlaggedEntrySnapshotDto
{
    public required DateTime TimestampUtc { get; init; }
    public required string AppName { get; init; }
    public required string SessionName { get; init; }
    public required string HostName { get; init; }
    public required string Title { get; init; }
    public required string Type { get; init; }
    public string? DataText { get; init; }
}

public sealed record FlaggedEntryDto
{
    public required string EntryId { get; init; }
    public required DateTime FlaggedAtUtc { get; init; }
    public string? Category { get; init; }
    public string? Reason { get; init; }
    public required bool IsTrimmedFromLiveStore { get; init; }
    public required FlaggedEntrySnapshotDto EntrySnapshot { get; init; }
}

public sealed record UnflagEntryResultDto
{
    public required string EntryId { get; init; }
    public required bool Success { get; init; }
}
