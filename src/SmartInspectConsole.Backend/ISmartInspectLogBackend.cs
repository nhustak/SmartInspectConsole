using SmartInspectConsole.Contracts;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Backend;

public interface ISmartInspectLogBackend
{
    string RunId { get; }
    int MaxLogEntries { get; set; }

    string AppendLogEntry(LogEntry entry, string clientId);
    void UpsertWatch(Watch watch);
    void AppendProcessFlow(ProcessFlow processFlow);
    void RecordClientConnected(string clientId, string transport);
    void RecordClientDisconnected(string clientId);
    void RecordLogHeader(string clientId, LogHeader header);
    void SetApplicationMuted(string applicationKey, bool isMuted);
    void RemoveApplication(string clientId, string applicationKey);
    void SetQueueDepth(int queueDepth);
    void SetListenerStatus(string transport, bool enabled, string endpoint, int clientCount);

    LogQueryResponse QueryLogs(LogQueryRequest request);
    LogEntryDto? GetLogEntry(string entryId, bool includeData);
    IReadOnlyList<ApplicationSummaryDto> ListApplications(bool connectedOnly = false, bool mutedOnly = false);
    LiveContextDto GetLiveContext();

    void RemoveLogEntries(IEnumerable<LogEntry> entries);
    void ClearLog();
    void ClearWatches();
    void ClearProcessFlow();
    void ClearAll();
}
