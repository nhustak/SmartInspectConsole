using SmartInspectConsole.Contracts;
using SmartInspectConsole.Core.Packets;

namespace SmartInspectConsole.Backend;

public sealed class SmartInspectLogBackend : ISmartInspectLogBackend
{
    private readonly object _sync = new();
    private readonly List<StoredLogEntry> _entries = [];
    private readonly Dictionary<string, StoredLogEntry> _entriesById = new(StringComparer.Ordinal);
    private readonly Dictionary<LogEntry, string> _entryIdsByPacket = new();
    private readonly Dictionary<string, ApplicationRuntimeState> _applicationsByClientId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApplicationRuntimeState> _applicationsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Watch> _watchesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ProcessFlow> _processFlows = [];
    private readonly Dictionary<string, ListenerRuntimeState> _listeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly SmartInspectBackendOptions _options;
    private readonly string _runId = Guid.NewGuid().ToString("N");

    private long _nextSequence;
    private long _totalReceived;
    private long _totalDroppedByRetention;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;
    private DateTime? _lastEntryUtc;
    private int _queueDepth;
    private int _maxLogEntries;

    public SmartInspectLogBackend(SmartInspectBackendOptions? options = null)
    {
        _options = options ?? new SmartInspectBackendOptions();
        _maxLogEntries = Math.Max(1_000, _options.MaxLogEntries);
    }

    public string RunId => _runId;

    public int MaxLogEntries
    {
        get
        {
            lock (_sync)
            {
                return _maxLogEntries;
            }
        }
        set
        {
            lock (_sync)
            {
                _maxLogEntries = Math.Max(1_000, value);
                TrimEntriesIfNeeded();
            }
        }
    }

    public string AppendLogEntry(LogEntry entry, string clientId)
    {
        lock (_sync)
        {
            _totalReceived++;

            var application = GetOrCreateApplicationState(clientId, entry.AppName, entry.HostName);
            application.MessageCount++;
            application.LastSeenUtc = entry.Timestamp;

            var sequence = ++_nextSequence;
            var entryId = $"{_runId}:{sequence}";
            var stored = new StoredLogEntry(entryId, sequence, entry);

            _entries.Add(stored);
            _entriesById[entryId] = stored;
            _entryIdsByPacket[entry] = entryId;
            _lastEntryUtc = entry.Timestamp;

            TrimEntriesIfNeeded();
            return entryId;
        }
    }

    public void UpsertWatch(Watch watch)
    {
        lock (_sync)
        {
            _watchesByName[watch.Name] = watch;
        }
    }

    public void AppendProcessFlow(ProcessFlow processFlow)
    {
        lock (_sync)
        {
            _processFlows.Add(processFlow);
        }
    }

    public void RecordClientConnected(string clientId, string transport)
    {
        lock (_sync)
        {
            if (!_applicationsByClientId.TryGetValue(clientId, out var application))
            {
                application = new ApplicationRuntimeState
                {
                    ClientId = clientId,
                    ApplicationKey = clientId,
                    AppName = clientId,
                    HostName = transport
                };
                _applicationsByClientId[clientId] = application;
            }

            application.IsConnected = true;
            application.ConnectedAtUtc ??= DateTime.UtcNow;
            application.DisconnectedAtUtc = null;
        }
    }

    public void RecordClientDisconnected(string clientId)
    {
        lock (_sync)
        {
            if (_applicationsByClientId.TryGetValue(clientId, out var application))
            {
                application.IsConnected = false;
                application.DisconnectedAtUtc = DateTime.UtcNow;
            }
        }
    }

    public void RecordLogHeader(string clientId, LogHeader header)
    {
        header.ParseContent();

        lock (_sync)
        {
            var appName = string.IsNullOrWhiteSpace(header.AppName) ? "Unknown" : header.AppName;
            var hostName = string.IsNullOrWhiteSpace(header.HostName) ? "Unknown" : header.HostName;
            var application = GetOrCreateApplicationState(clientId, appName, hostName);
            application.IsConnected = true;
            application.LastSeenUtc = DateTime.UtcNow;
        }
    }

    public void SetApplicationMuted(string applicationKey, bool isMuted)
    {
        lock (_sync)
        {
            if (_applicationsByKey.TryGetValue(applicationKey, out var application))
            {
                application.IsMuted = isMuted;
            }
        }
    }

    public void RemoveApplication(string clientId, string applicationKey)
    {
        lock (_sync)
        {
            _applicationsByClientId.Remove(clientId);

            if (_applicationsByKey.TryGetValue(applicationKey, out var application) &&
                string.Equals(application.ClientId, clientId, StringComparison.Ordinal))
            {
                _applicationsByKey.Remove(applicationKey);
            }
        }
    }

    public void SetQueueDepth(int queueDepth)
    {
        lock (_sync)
        {
            _queueDepth = Math.Max(0, queueDepth);
        }
    }

    public void SetListenerStatus(string transport, bool enabled, string endpoint, int clientCount)
    {
        lock (_sync)
        {
            _listeners[transport] = new ListenerRuntimeState
            {
                Transport = transport,
                Enabled = enabled,
                Endpoint = endpoint,
                ClientCount = clientCount
            };
        }
    }

    public LogQueryResponse QueryLogs(LogQueryRequest request)
    {
        lock (_sync)
        {
            var limit = NormalizeLimit(request.Limit);
            var appNames = request.AppNames?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sessionNames = request.SessionNames?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var hostNames = request.HostNames?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var processIds = request.ProcessIds?.ToHashSet();
            var threadIds = request.ThreadIds?.ToHashSet();
            var levels = request.Levels?.Where(v => !string.IsNullOrWhiteSpace(v)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var cursorSequence = TryParseCursor(request.Cursor);

            var items = new List<LogEntryDto>(limit);
            foreach (var stored in _entries.AsEnumerable().Reverse())
            {
                if (cursorSequence.HasValue && stored.Sequence >= cursorSequence.Value)
                {
                    continue;
                }

                if (!Matches(stored.Packet, appNames, sessionNames, hostNames, processIds, threadIds, levels, request.Text))
                {
                    continue;
                }

                items.Add(ToDto(stored, request.IncludeData));
                if (items.Count == limit)
                {
                    break;
                }
            }

            var hasMore = false;
            string? nextCursor = null;
            if (items.Count > 0)
            {
                var lastSequence = items[^1].Sequence;
                hasMore = _entries.Any(e => e.Sequence < lastSequence && Matches(
                    e.Packet,
                    appNames,
                    sessionNames,
                    hostNames,
                    processIds,
                    threadIds,
                    levels,
                    request.Text));

                if (hasMore)
                {
                    nextCursor = lastSequence.ToString();
                }
            }

            return new LogQueryResponse
            {
                Items = items,
                ReturnedCount = items.Count,
                HasMore = hasMore,
                NextCursor = nextCursor,
                AppliedLimit = limit,
                RunId = _runId
            };
        }
    }

    public LogEntryDto? GetLogEntry(string entryId, bool includeData)
    {
        lock (_sync)
        {
            return _entriesById.TryGetValue(entryId, out var stored)
                ? ToDto(stored, includeData)
                : null;
        }
    }

    public IReadOnlyList<ApplicationSummaryDto> ListApplications(bool connectedOnly = false, bool mutedOnly = false)
    {
        lock (_sync)
        {
            return _applicationsByKey.Values
                .Where(app => !connectedOnly || app.IsConnected)
                .Where(app => !mutedOnly || app.IsMuted)
                .OrderBy(app => app.AppName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(app => app.HostName, StringComparer.OrdinalIgnoreCase)
                .Select(app => new ApplicationSummaryDto
                {
                    ClientId = app.ClientId,
                    ApplicationKey = app.ApplicationKey,
                    AppName = app.AppName,
                    HostName = app.HostName,
                    IsConnected = app.IsConnected,
                    IsMuted = app.IsMuted,
                    MessageCount = app.MessageCount,
                    ConnectedAtUtc = app.ConnectedAtUtc,
                    LastSeenUtc = app.LastSeenUtc,
                    DisconnectedAtUtc = app.DisconnectedAtUtc
                })
                .ToList();
        }
    }

    public LiveContextDto GetLiveContext()
    {
        lock (_sync)
        {
            return new LiveContextDto
            {
                RunId = _runId,
                StartedAtUtc = _startedAtUtc,
                ListenerStatus = _listeners.Values
                    .OrderBy(v => v.Transport, StringComparer.OrdinalIgnoreCase)
                    .Select(v => new ListenerStatusDto
                    {
                        Transport = v.Transport,
                        Enabled = v.Enabled,
                        Endpoint = v.Endpoint,
                        ClientCount = v.ClientCount
                    })
                    .ToList(),
                LogEntryCount = _entries.Count,
                WatchCount = _watchesByName.Count,
                ProcessFlowCount = _processFlows.Count,
                ConnectedApplicationCount = _applicationsByKey.Values.Count(app => app.IsConnected),
                MaxLogEntries = _maxLogEntries,
                QueueDepth = _queueDepth,
                LastEntryUtc = _lastEntryUtc,
                TotalReceived = _totalReceived,
                TotalRetained = _entries.Count,
                TotalDroppedByRetention = _totalDroppedByRetention
            };
        }
    }

    public void RemoveLogEntries(IEnumerable<LogEntry> entries)
    {
        lock (_sync)
        {
            foreach (var entry in entries)
            {
                if (!_entryIdsByPacket.TryGetValue(entry, out var entryId))
                {
                    continue;
                }

                _entryIdsByPacket.Remove(entry);
                if (_entriesById.Remove(entryId, out var stored))
                {
                    _entries.Remove(stored);
                }
            }
        }
    }

    public void ClearLog()
    {
        lock (_sync)
        {
            _entries.Clear();
            _entriesById.Clear();
            _entryIdsByPacket.Clear();
            _lastEntryUtc = null;
        }
    }

    public void ClearWatches()
    {
        lock (_sync)
        {
            _watchesByName.Clear();
        }
    }

    public void ClearProcessFlow()
    {
        lock (_sync)
        {
            _processFlows.Clear();
        }
    }

    public void ClearAll()
    {
        ClearLog();
        ClearWatches();
        ClearProcessFlow();
    }

    private int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return _options.QueryDefaultLimit;
        }

        return Math.Min(limit, _options.QueryMaxLimit);
    }

    private static long? TryParseCursor(string? cursor)
    {
        return long.TryParse(cursor, out var sequence) ? sequence : null;
    }

    private static bool Matches(
        LogEntry entry,
        HashSet<string>? appNames,
        HashSet<string>? sessionNames,
        HashSet<string>? hostNames,
        HashSet<int>? processIds,
        HashSet<int>? threadIds,
        HashSet<string>? levels,
        string? text)
    {
        if (appNames is { Count: > 0 } && !appNames.Contains(entry.AppName))
        {
            return false;
        }

        if (sessionNames is { Count: > 0 } && !sessionNames.Contains(entry.SessionName))
        {
            return false;
        }

        if (hostNames is { Count: > 0 } && !hostNames.Contains(entry.HostName))
        {
            return false;
        }

        if (processIds is { Count: > 0 } && !processIds.Contains(entry.ProcessId))
        {
            return false;
        }

        if (threadIds is { Count: > 0 } && !threadIds.Contains(entry.ThreadId))
        {
            return false;
        }

        if (levels is { Count: > 0 } && !levels.Contains(entry.LogEntryType.ToString()))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (entry.Title.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return entry.DataAsString?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false;
    }

    private LogEntryDto ToDto(StoredLogEntry stored, bool includeData)
    {
        return new LogEntryDto
        {
            EntryId = stored.EntryId,
            Sequence = stored.Sequence,
            RunId = _runId,
            TimestampUtc = stored.Packet.Timestamp,
            ElapsedMs = stored.Packet.ElapsedTime == TimeSpan.Zero ? null : stored.Packet.ElapsedTime.TotalMilliseconds,
            Type = stored.Packet.LogEntryType.ToString(),
            ViewerId = stored.Packet.ViewerId.ToString(),
            AppName = stored.Packet.AppName,
            SessionName = stored.Packet.SessionName,
            HostName = stored.Packet.HostName,
            ProcessId = stored.Packet.ProcessId,
            ThreadId = stored.Packet.ThreadId,
            Title = stored.Packet.Title,
            DataText = includeData ? stored.Packet.DataAsString : null,
            HasData = stored.Packet.Data is { Length: > 0 }
        };
    }

    private ApplicationRuntimeState GetOrCreateApplicationState(string clientId, string appName, string hostName)
    {
        var normalizedAppName = string.IsNullOrWhiteSpace(appName) ? "Unknown" : appName;
        var normalizedHostName = string.IsNullOrWhiteSpace(hostName) ? "Unknown" : hostName;
        var applicationKey = BuildApplicationKey(normalizedAppName, normalizedHostName);

        if (_applicationsByClientId.TryGetValue(clientId, out var existingByClient))
        {
            existingByClient.AppName = normalizedAppName;
            existingByClient.HostName = normalizedHostName;
            existingByClient.ApplicationKey = applicationKey;
            _applicationsByKey[applicationKey] = existingByClient;
            return existingByClient;
        }

        if (_applicationsByKey.TryGetValue(applicationKey, out var existingByKey))
        {
            existingByKey.ClientId = clientId;
            existingByKey.ConnectedAtUtc ??= DateTime.UtcNow;
            _applicationsByClientId[clientId] = existingByKey;
            return existingByKey;
        }

        var application = new ApplicationRuntimeState
        {
            ClientId = clientId,
            ApplicationKey = applicationKey,
            AppName = normalizedAppName,
            HostName = normalizedHostName,
            IsConnected = true,
            ConnectedAtUtc = DateTime.UtcNow
        };

        _applicationsByClientId[clientId] = application;
        _applicationsByKey[applicationKey] = application;
        return application;
    }

    private void TrimEntriesIfNeeded()
    {
        while (_entries.Count > _maxLogEntries)
        {
            var removed = _entries[0];
            _entries.RemoveAt(0);
            _entriesById.Remove(removed.EntryId);
            _entryIdsByPacket.Remove(removed.Packet);
            _totalDroppedByRetention++;
        }
    }

    private static string BuildApplicationKey(string appName, string hostName)
    {
        return $"{appName}@{hostName}";
    }

    private sealed record StoredLogEntry(string EntryId, long Sequence, LogEntry Packet);

    private sealed class ApplicationRuntimeState
    {
        public string ClientId { get; set; } = string.Empty;
        public string ApplicationKey { get; set; } = string.Empty;
        public string AppName { get; set; } = string.Empty;
        public string HostName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public bool IsMuted { get; set; }
        public long MessageCount { get; set; }
        public DateTime? ConnectedAtUtc { get; set; }
        public DateTime? LastSeenUtc { get; set; }
        public DateTime? DisconnectedAtUtc { get; set; }
    }

    private sealed class ListenerRuntimeState
    {
        public string Transport { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string Endpoint { get; set; } = string.Empty;
        public int ClientCount { get; set; }
    }
}
