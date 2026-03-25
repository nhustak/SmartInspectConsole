# SmartInspectConsole MCP and Shared Backend Implementation Plan

## Intent

This plan keeps the WPF console as the primary running host while extracting a reusable backend that can serve:

- the existing WPF UI
- a local API for debugging and contract validation
- MCP tools for Codex access
- a future headless service host without redesign

The core constraint is that live logs must have one backend source of truth. MCP and HTTP should never read directly from WPF view models or UI collections.

## Current State

The current ownership is concentrated in `MainViewModel`:

- listener lifecycle
- packet ingestion
- batch processing
- retention trimming
- connected application tracking
- mute state
- filter value discovery
- clear/control actions
- file import/export coordination

This is workable for the current WPF app but creates the wrong dependency direction for MCP and API access.

## Target Architecture

### Recommended project boundaries

#### 1. `SmartInspectConsole.Core`

Keep protocol-facing concerns only:

- packet models
- packet parsing and serialization
- file I/O for `.sil`
- transport listeners (`TCP`, `pipe`, `WebSocket`)
- listener event contracts

This project should stay free of runtime state ownership and query logic.

#### 2. `SmartInspectConsole.Backend`

Add a new backend runtime project that becomes the source of truth for all live state:

- ingestion runtime
- in-memory log store
- retention policy
- connection/application registry
- mute state
- query engine
- control actions
- runtime status and diagnostics
- flagged entry management

This project must not reference WPF or UI collection types.

#### 3. `SmartInspectConsole.Contracts`

Add a contracts project for shared DTOs and request/response models used by:

- local HTTP API
- MCP tools
- future service host

This keeps API and MCP shapes stable and reduces duplicate contract definitions.

#### 4. `SmartInspectConsole`

Keep this as the WPF host and operator UI only:

- visual state
- per-tab filters and display behavior
- binding and commands
- state persistence for UI layout/preferences

The WPF app should consume backend snapshots and invoke backend services. It should stop owning canonical log state.

#### 5. `SmartInspect.Relay.AspNetCore`

Reuse this as the HTTP hosting surface, but extend it beyond ingestion relay behavior so it can expose local debug/query/control endpoints backed by shared services.

#### 6. Future `SmartInspectConsole.Service`

When a headless host is needed later, it should be another composition root using:

- `SmartInspectConsole.Core`
- `SmartInspectConsole.Backend`
- `SmartInspectConsole.Contracts`
- `SmartInspect.Relay.AspNetCore`

No redesign should be needed if the runtime state already lives in `SmartInspectConsole.Backend`.

## Backend Services to Introduce

### `ILogIngestionRuntime`

Owns listener lifecycle and runtime ingestion flow.

Responsibilities:

- start and stop listeners
- subscribe to listener packet events
- batch incoming packets
- assign stable runtime entry identity
- hand packets to the store
- update runtime metrics
- apply retention policy

Suggested members:

```csharp
public interface ILogIngestionRuntime
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    bool IsRunning { get; }
    RuntimeInstanceId CurrentRunId { get; }
}
```

### `ILogStore`

Owns live in-memory state.

Responsibilities:

- retain log entries
- retain watches
- retain process flow entries
- lookup by stable entry id
- expose immutable snapshots or controlled readers
- enforce retention trimming

Suggested members:

```csharp
public interface ILogStore
{
    long TotalReceived { get; }
    int LogEntryCount { get; }
    int WatchCount { get; }
    int ProcessFlowCount { get; }

    StoredLogEntry Append(LogEntry packet, string clientId);
    void UpsertWatch(Watch packet);
    void AppendProcessFlow(ProcessFlow packet);
    bool TryGetEntry(LogEntryId entryId, out StoredLogEntry? entry);
    LogStoreSnapshot GetSnapshot();
    RetentionResult ApplyRetention(int maxEntries);
    void ClearLog();
    void ClearWatches();
    void ClearProcessFlow();
}
```

### `ILogQueryService`

Owns all non-UI query behavior.

Responsibilities:

- bounded latest-first querying
- text and metadata filters
- cursor generation and validation
- by-application access
- flagged-only queries

Suggested members:

```csharp
public interface ILogQueryService
{
    LogQueryResponse Query(LogQueryRequest request);
    LogEntryDto? GetEntry(LogEntryId entryId, bool includeData);
}
```

### `IApplicationRegistry`

Owns logical application and transport-session tracking.

Responsibilities:

- client connected/disconnected state
- app/host identity mapping
- message counts
- mute state
- application summaries

Suggested members:

```csharp
public interface IApplicationRegistry
{
    void OnClientConnected(string clientId, string transport);
    void OnClientDisconnected(string clientId);
    void ApplyHeader(string clientId, LogHeader header);
    void ObserveLogEntry(string clientId, LogEntry entry);
    IReadOnlyList<ApplicationSummaryDto> ListApplications(bool connectedOnly = false);
    void SetMuted(string applicationKey, bool muted);
}
```

### `IFlaggedEntryService`

Owns flagged entry metadata and lookup.

Responsibilities:

- flag/unflag by stable entry id
- list flagged entries
- preserve a compact snapshot even after retention trimming

Suggested members:

```csharp
public interface IFlaggedEntryService
{
    FlaggedEntryDto Flag(FlagEntryRequest request);
    bool Unflag(LogEntryId entryId);
    IReadOnlyList<FlaggedEntryDto> List(FlaggedEntryQuery query);
    bool IsFlagged(LogEntryId entryId);
}
```

### `IControlActionService`

Owns runtime actions that change state.

Responsibilities:

- clear log
- clear watches
- clear process flow
- mute/unmute applications
- remove/reset connection state if retained as an operator action

Suggested members:

```csharp
public interface IControlActionService
{
    void ClearLog();
    void ClearWatches();
    void ClearProcessFlow();
    void ClearAll();
    void MuteApplication(string applicationKey);
    void UnmuteApplication(string applicationKey);
}
```

### `IRuntimeSnapshotService`

Provides live context for UI, API, and MCP.

Responsibilities:

- listener status
- queue depth
- counts
- retention config
- run metadata

Suggested members:

```csharp
public interface IRuntimeSnapshotService
{
    LiveContextDto GetLiveContext();
}
```

### `IConsoleStateStore`

Persists non-log durable state.

Responsibilities:

- retention cap
- muted application keys
- future flag metadata persistence if desired
- host settings

This should remain separate from WPF-only layout preferences already stored in `AppState`.

### `IBackendEventStream`

Provides change notifications to hosts.

Responsibilities:

- notify WPF that new entries arrived
- notify API/MCP-hosted observers if streaming is ever added later
- keep eventing backend-owned rather than UI-owned

Use a direct .NET event, channel, or observer pattern. Do not introduce a workaround adapter layer just to bridge WPF collections.

## Stable Identity Strategy

This is a prerequisite for MCP and flagged logs.

### Recommendation

Assign stable identity at ingestion time in the backend:

- `RunId`: a GUID created when the host process starts the runtime
- `Sequence`: an incrementing `long` assigned to each retained log entry as it enters the store
- `EntryId`: string form such as `{runId}:{sequence}`

### Why this is required

Current packet models do not carry a durable identity. Identity derived from timestamp, app name, title, or thread id is not stable enough for:

- MCP lookup by entry
- flagged entry references
- pagination/cursor continuity
- retention-aware diagnostics

### Contract rule

All log APIs and MCP tools should use backend-issued `EntryId`. Do not expose UI row indexes or WPF collection positions.

## DTOs and Contracts

### Log querying

```csharp
public sealed record LogQueryRequest
{
    public IReadOnlyList<string>? AppNames { get; init; }
    public IReadOnlyList<string>? SessionNames { get; init; }
    public IReadOnlyList<string>? HostNames { get; init; }
    public IReadOnlyList<int>? ProcessIds { get; init; }
    public IReadOnlyList<int>? ThreadIds { get; init; }
    public IReadOnlyList<string>? Levels { get; init; }
    public string? Text { get; init; }
    public string? TitlePattern { get; init; }
    public bool TitleIsRegex { get; init; }
    public bool TitleCaseSensitive { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public string? Cursor { get; init; }
    public int Limit { get; init; } = 100;
    public bool IncludeData { get; init; }
    public bool FlaggedOnly { get; init; }
}
```

```csharp
public sealed record LogQueryResponse
{
    public required IReadOnlyList<LogEntryDto> Items { get; init; }
    public required int ReturnedCount { get; init; }
    public required bool HasMore { get; init; }
    public string? NextCursor { get; init; }
    public required int AppliedLimit { get; init; }
    public required string RunId { get; init; }
}
```

```csharp
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
```

### Applications

```csharp
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
```

`ApplicationKey` should represent the logical application identity used for mute and query grouping. It should be distinct from transient transport `ClientId`.

### Live context

```csharp
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
```

```csharp
public sealed record ListenerStatusDto
{
    public required string Transport { get; init; }
    public required bool Enabled { get; init; }
    public required string Endpoint { get; init; }
    public required int ClientCount { get; init; }
}
```

### Flagged entries

```csharp
public sealed record FlaggedEntryDto
{
    public required string EntryId { get; init; }
    public required DateTime FlaggedAtUtc { get; init; }
    public string? FlaggedBy { get; init; }
    public string? Reason { get; init; }
    public string? Category { get; init; }
    public required FlaggedEntrySnapshotDto EntrySnapshot { get; init; }
    public required bool IsTrimmedFromLiveStore { get; init; }
}
```

```csharp
public sealed record FlaggedEntrySnapshotDto
{
    public required DateTime TimestampUtc { get; init; }
    public required string AppName { get; init; }
    public required string HostName { get; init; }
    public required string Title { get; init; }
    public required string Type { get; init; }
}
```

```csharp
public sealed record FlagEntryRequest
{
    public required string EntryId { get; init; }
    public string? Reason { get; init; }
    public string? Category { get; init; }
}
```

## Local API Shape

The local API should be useful for:

- manual debugging
- validating query contracts
- inspecting the live in-memory backend outside the UI
- exercising the same request shapes MCP will use

### Host constraints

- loopback only by default
- explicit versioned route prefix
- bounded outputs
- no endpoint should dump the entire store by default

### Recommended endpoints

#### Applications

- `GET /api/local/v1/applications`
  - query params: `connectedOnly`, `mutedOnly`
  - returns `ApplicationSummaryDto[]`

#### Logs

- `POST /api/local/v1/logs/query`
  - body: `LogQueryRequest`
  - returns `LogQueryResponse`

- `GET /api/local/v1/logs/{entryId}`
  - query params: `includeData`
  - returns `LogEntryDto`

#### Live context

- `GET /api/local/v1/context/live`
  - returns `LiveContextDto`

#### Control

- `POST /api/local/v1/control/clear-log`
- `POST /api/local/v1/control/clear-watches`
- `POST /api/local/v1/control/clear-process-flow`
- `POST /api/local/v1/control/clear-all`

#### Application actions

- `POST /api/local/v1/applications/{applicationKey}/mute`
- `DELETE /api/local/v1/applications/{applicationKey}/mute`

#### Flags

- `GET /api/local/v1/flags`
  - query params: `category`, `limit`

- `POST /api/local/v1/flags`
  - body: `FlagEntryRequest`

- `DELETE /api/local/v1/flags/{entryId}`

### Safe defaults

- default `limit = 100`
- hard maximum `limit = 1000`
- `includeData = false` unless explicitly requested
- latest-first ordering by default
- invalid cursor returns a structured client error

## MCP Tool Shape

MCP should sit on top of the same backend services and contracts as the local API.

### Recommended first tools

#### `list_applications`

Purpose:

- easy log access by application
- discover the exact application names and host pairs currently active

Input:

- `connected_only` optional
- `muted_only` optional

Output:

- list of `ApplicationSummaryDto`

#### `query_logs`

Purpose:

- fetch recent logs by application
- support safe bounded filtering

Input:

- `application`
- `host_name` optional
- `text` optional
- `levels` optional
- `cursor` optional
- `limit` optional
- `include_data` optional
- `flagged_only` optional

Behavior:

- latest-first
- default to `limit = 100`
- enforce hard max

#### `get_log_entry`

Purpose:

- fetch one known entry by id

Input:

- `entry_id`
- `include_data` optional

#### `get_live_context`

Purpose:

- expose listener and retention status

Input:

- none

#### `list_flagged_logs`

Purpose:

- follow-up support for direct flagged-log retrieval

Input:

- `category` optional
- `limit` optional

#### `flag_log_entry`

Input:

- `entry_id`
- `reason` optional
- `category` optional

#### `unflag_log_entry`

Input:

- `entry_id`

### MCP implementation note

For the first iteration, MCP can call backend services directly in-process. The local API should still be implemented because it is valuable for manual testing and contract validation. The contracts should remain identical between the two paths.

## Migration Path from `MainViewModel`

### Phase 1: extract store ownership

Move these responsibilities out of `MainViewModel` into backend services:

- `LogEntries`
- `Watches`
- `ProcessFlows`
- retention cap and trimming
- connection registry
- mute list
- runtime counters and diagnostics

At this stage, WPF can still mirror backend state into UI collections.

### Phase 2: move ingestion hot path

Extract the logic currently centered around:

- `OnPacketReceived`
- `ProcessPendingItems`
- `HandleWatch`
- `HandleProcessFlow`
- `HandleLogHeader`
- connection update accumulation

The backend runtime should own this entire path. `MainViewModel` should stop receiving listener packets directly.

### Phase 3: move control actions

Replace direct view-model methods with backend action calls:

- `ClearLog`
- `ClearWatches`
- `ClearProcessFlow`
- `ClearAll`
- mute/unmute actions
- connection removal if still needed as an operator action

### Phase 4: keep WPF-specific state in WPF

The following should remain view-model owned because they are UI concerns:

- tab/view definitions
- filter input state for each tab
- layout/panel visibility
- selected row/detail tab
- copy-to-clipboard commands

The filter engine used by WPF can remain local initially, but backend query behavior should not depend on WPF types or `CollectionView`.

### Phase 5: add backend query service

Once the store is backend-owned, add query contracts and a query service. The WPF app can continue to use live collection binding while API and MCP use backend queries.

### Phase 6: host local API

Expose query and control endpoints from the same process. Validate payloads and bounds here before adding MCP.

### Phase 7: add flags

Add flagged entries only after stable `EntryId` exists and retention rules are defined. Flagging earlier would produce a fragile contract.

## Reuse for a Future Headless Host

The design stays reusable if the following rules hold:

### Rule 1

`SmartInspectConsole.Backend` must not reference:

- `Dispatcher`
- `Application.Current`
- `ObservableCollection`
- WPF view models
- message boxes

### Rule 2

All shared state lives in backend services, not in UI collections.

### Rule 3

Hosts compose the backend differently:

- WPF host: binds to snapshots and subscribes to change notifications
- local API host: exposes HTTP routes
- future service host: runs listeners and API without WPF

### Rule 4

Contracts remain host-neutral and versioned.

If these rules are followed, a future headless service only needs a new startup project and hosting config.

## Recommended Order of Implementation

Implement the lowest-risk foundational pieces first.

### Step 1

Add `SmartInspectConsole.Contracts` and define DTOs for:

- log queries
- application summaries
- live context
- flagged entries

This is low risk and gives the project a stable target.

### Step 2

Add backend-issued stable `EntryId` and `RunId` support in a new `SmartInspectConsole.Backend`.

Do this before any API or MCP work.

### Step 3

Create `ILogStore` and move retention logic there.

This removes the most important source-of-truth problem.

### Step 4

Move packet batching and ingestion from `MainViewModel` into `ILogIngestionRuntime`.

### Step 5

Move connection tracking and mute behavior into `IApplicationRegistry`.

### Step 6

Update WPF to consume backend state and invoke backend services.

At this point, the app should behave the same, but the source of truth is no longer the view model.

### Step 7

Implement `ILogQueryService` with safe bounded query behavior.

### Step 8

Expose local API endpoints for:

- applications
- log query
- single entry lookup
- live context
- control actions

### Step 9

Add MCP tools:

- `list_applications`
- `query_logs`
- `get_log_entry`
- `get_live_context`

### Step 10

Add flagged-entry support and its API/MCP tools.

This is intentionally later because it depends on stable identity and retention policy decisions.

## File-by-File Implementation Checklist

### New projects

- `src/SmartInspectConsole.Contracts/`
- `src/SmartInspectConsole.Backend/`

### Existing project updates

#### `src/SmartInspectConsole/`

- remove backend ownership from `MainViewModel`
- inject backend services into the WPF host
- keep only UI-facing state and commands

#### `src/SmartInspectConsole.Core/`

- keep listeners and packets here
- add only minimal packet metadata support if necessary
- do not add query logic here

#### `src/SmartInspect.Relay.AspNetCore/`

- add local query/control endpoints
- wire endpoints to shared backend services

#### `src/SmartInspectConsole.Relay/`

- decide whether this remains ingestion-only or also hosts local debug endpoints
- if retained, keep it as a composition root, not the source of truth

## Technical Risks

### Retention semantics

Current retention is destructive FIFO trimming. That is acceptable for a live console but becomes contract-sensitive once external callers use cursors and entry ids.

Risks:

- cursor points to trimmed data
- flagged entry references disappear
- clients misinterpret missing entries as errors

Recommendation:

- return explicit cursor invalidation when necessary
- keep a `TotalDroppedByRetention` count
- preserve compact flagged snapshots even when live entries are trimmed

### Stable identity

Current packet models do not expose a durable entry id.

Risks:

- no safe MCP lookup by entry
- flagged logs cannot reference a stable target
- pagination continuity becomes unreliable

Recommendation:

- assign backend identity at ingestion time
- never use UI index or collection position as identity

### Safe exposure of live in-memory data

Reading directly from mutable WPF collections is unsafe and couples transport/API consumers to presentation concerns.

Risks:

- race conditions during trimming
- inconsistent snapshots during enumeration
- hidden WPF-thread dependencies leaking into non-UI callers

Recommendation:

- backend returns immutable DTO snapshots
- query service owns synchronization
- API/MCP never sees WPF collection types

### Application identity and reconnect handling

Current logic merges based on `clientId` and falls back to `AppName@HostName`.

Risks:

- reconnects produce duplicate logical identities
- late `LogHeader` arrival changes identity after messages are counted
- muting by the wrong key creates confusing operator behavior

Recommendation:

- distinguish transport session identity from logical application identity
- introduce explicit `ApplicationKey`
- keep merge logic in the backend registry only

### Query cost under high volume

The current UI path is optimized for rendering batches, not for repeated external queries.

Risks:

- expensive regex scans across large live buffers
- large payload materialization for `DataAsString`
- API/MCP calls competing with ingestion hot path

Recommendation:

- latest-first bounded querying
- `includeData = false` by default
- hard maximum limits
- avoid decoding large payloads unless requested

## Recommended First Milestone

The first useful milestone for Codex support should be:

1. shared backend store exists
2. stable `EntryId` exists
3. applications can be listed
4. logs can be queried by application with bounded limits
5. WPF still behaves the same

That milestone satisfies the first real use case without committing to flagged-entry behavior too early.
