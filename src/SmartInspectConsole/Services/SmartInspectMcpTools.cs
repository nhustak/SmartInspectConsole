using ModelContextProtocol.Server;
using SmartInspectConsole.Backend;
using SmartInspectConsole.Contracts;

namespace SmartInspectConsole.Services;

[McpServerToolType]
internal sealed class SmartInspectMcpTools
{
    private readonly ISmartInspectLogBackend _backend;

    public SmartInspectMcpTools(ISmartInspectLogBackend backend)
    {
        _backend = backend;
    }

    [McpServerTool]
    public IReadOnlyList<ApplicationSummaryDto> ListApplications(
        bool connectedOnly = false,
        bool mutedOnly = false)
    {
        return _backend.ListApplications(connectedOnly, mutedOnly);
    }

    [McpServerTool]
    public LiveContextDto GetLiveContext()
    {
        return _backend.GetLiveContext();
    }

    [McpServerTool]
    public LogQueryResponse QueryLogs(
        string[]? appNames = null,
        string[]? sessionNames = null,
        string[]? hostNames = null,
        int[]? processIds = null,
        int[]? threadIds = null,
        string[]? levels = null,
        string? text = null,
        string? cursor = null,
        int limit = 100,
        bool includeData = false,
        bool flaggedOnly = false)
    {
        return _backend.QueryLogs(new LogQueryRequest
        {
            AppNames = appNames,
            SessionNames = sessionNames,
            HostNames = hostNames,
            ProcessIds = processIds,
            ThreadIds = threadIds,
            Levels = levels,
            Text = text,
            Cursor = cursor,
            Limit = limit,
            IncludeData = includeData,
            FlaggedOnly = flaggedOnly
        });
    }

    [McpServerTool]
    public LogEntryDto GetLogEntry(string entryId, bool includeData = false)
    {
        return _backend.GetLogEntry(entryId, includeData)
            ?? throw new InvalidOperationException($"Log entry '{entryId}' was not found.");
    }

    [McpServerTool]
    public IReadOnlyList<FlaggedEntryDto> ListFlaggedLogs(string? category = null, int limit = 100)
    {
        return _backend.ListFlaggedEntries(category, limit);
    }

    [McpServerTool]
    public FlaggedEntryDto FlagLogEntry(string entryId, string? category = null, string? reason = null)
    {
        return _backend.FlagEntry(new FlagEntryRequest
        {
            EntryId = entryId,
            Category = category,
            Reason = reason
        });
    }

    [McpServerTool]
    public UnflagEntryResultDto UnflagLogEntry(string entryId)
    {
        return new UnflagEntryResultDto
        {
            EntryId = entryId,
            Success = _backend.UnflagEntry(entryId)
        };
    }
}
