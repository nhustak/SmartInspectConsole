using ModelContextProtocol.Server;
using SmartInspectConsole.Contracts;

namespace SmartInspectConsole.Mcp;

[McpServerToolType]
internal sealed class SmartInspectMcpTools
{
    private readonly SmartInspectLocalApiClient _apiClient;

    public SmartInspectMcpTools(SmartInspectLocalApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [McpServerTool]
    public Task<IReadOnlyList<ApplicationSummaryDto>> ListApplications(
        bool connectedOnly = false,
        bool mutedOnly = false,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.ListApplicationsAsync(connectedOnly, mutedOnly, cancellationToken);
    }

    [McpServerTool]
    public Task<LiveContextDto> GetLiveContext(CancellationToken cancellationToken = default)
    {
        return _apiClient.GetLiveContextAsync(cancellationToken);
    }

    [McpServerTool]
    public Task<LogQueryResponse> QueryLogs(
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
        bool flaggedOnly = false,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.QueryLogsAsync(new LogQueryRequest
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
        }, cancellationToken);
    }

    [McpServerTool]
    public Task<LogEntryDto> GetLogEntry(
        string entryId,
        bool includeData = false,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.GetLogEntryAsync(entryId, includeData, cancellationToken);
    }

    [McpServerTool]
    public Task<IReadOnlyList<FlaggedEntryDto>> ListFlaggedLogs(
        string? category = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.ListFlaggedEntriesAsync(category, limit, cancellationToken);
    }

    [McpServerTool]
    public Task<FlaggedEntryDto> FlagLogEntry(
        string entryId,
        string? category = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return _apiClient.FlagEntryAsync(new FlagEntryRequest
        {
            EntryId = entryId,
            Category = category,
            Reason = reason
        }, cancellationToken);
    }

    [McpServerTool]
    public async Task<UnflagEntryResultDto> UnflagLogEntry(
        string entryId,
        CancellationToken cancellationToken = default)
    {
        var success = await _apiClient.UnflagEntryAsync(entryId, cancellationToken);
        return new UnflagEntryResultDto
        {
            EntryId = entryId,
            Success = success
        };
    }
}
