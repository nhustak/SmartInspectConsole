using System.Net.Http.Json;
using SmartInspectConsole.Contracts;

namespace SmartInspectConsole.Mcp;

internal sealed class SmartInspectLocalApiClient
{
    private readonly HttpClient _httpClient;

    public SmartInspectLocalApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<ApplicationSummaryDto>> ListApplicationsAsync(bool connectedOnly, bool mutedOnly, CancellationToken cancellationToken)
    {
        var uri = $"/api/local/v1/applications?connectedOnly={connectedOnly.ToString().ToLowerInvariant()}&mutedOnly={mutedOnly.ToString().ToLowerInvariant()}";
        return await _httpClient.GetFromJsonAsync<IReadOnlyList<ApplicationSummaryDto>>(uri, cancellationToken)
            ?? [];
    }

    public async Task<LiveContextDto> GetLiveContextAsync(CancellationToken cancellationToken)
    {
        var result = await _httpClient.GetFromJsonAsync<LiveContextDto>("/api/local/v1/context/live", cancellationToken);
        return result ?? throw new InvalidOperationException("The local API returned no live context.");
    }

    public async Task<LogQueryResponse> QueryLogsAsync(LogQueryRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/local/v1/logs/query", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LogQueryResponse>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("The local API returned no log query response.");
    }

    public async Task<LogEntryDto> GetLogEntryAsync(string entryId, bool includeData, CancellationToken cancellationToken)
    {
        var uri = $"/api/local/v1/logs/{Uri.EscapeDataString(entryId)}?includeData={includeData.ToString().ToLowerInvariant()}";
        var result = await _httpClient.GetFromJsonAsync<LogEntryDto>(uri, cancellationToken);
        return result ?? throw new InvalidOperationException($"The local API did not return log entry '{entryId}'.");
    }

    public async Task<IReadOnlyList<FlaggedEntryDto>> ListFlaggedEntriesAsync(string? category, int limit, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(category)
            ? $"/api/local/v1/flags?limit={limit}"
            : $"/api/local/v1/flags?category={Uri.EscapeDataString(category)}&limit={limit}";

        return await _httpClient.GetFromJsonAsync<IReadOnlyList<FlaggedEntryDto>>(query, cancellationToken)
            ?? [];
    }

    public async Task<FlaggedEntryDto> FlagEntryAsync(FlagEntryRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("/api/local/v1/flags", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<FlaggedEntryDto>(cancellationToken: cancellationToken);
        return result ?? throw new InvalidOperationException("The local API returned no flagged-entry response.");
    }

    public async Task<bool> UnflagEntryAsync(string entryId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.DeleteAsync($"/api/local/v1/flags/{Uri.EscapeDataString(entryId)}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }
}
