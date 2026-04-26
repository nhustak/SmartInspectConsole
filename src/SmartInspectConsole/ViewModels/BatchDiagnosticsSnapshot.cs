namespace SmartInspectConsole.ViewModels;

/// <summary>
/// Lightweight diagnostics snapshot for the UI ingestion pipeline.
/// </summary>
public sealed class BatchDiagnosticsSnapshot
{
    public int LogQueueDepth { get; init; }
    public int WatchQueueDepth { get; init; }
    public int ProcessFlowQueueDepth { get; init; }
    public int LastBatchSize { get; init; }
    public double LastBatchDurationMs { get; init; }
    public long TotalLogEntriesReceived { get; init; }
    public long TotalLogEntriesRendered { get; init; }
    public long TotalLogEntriesDroppedFromPendingQueue { get; init; }
    public long TotalWatchUpdatesRendered { get; init; }
    public long TotalProcessFlowsRendered { get; init; }
    public int MaxObservedLogQueueDepth { get; init; }

    public override string ToString()
        => $"Queues L:{LogQueueDepth:N0} W:{WatchQueueDepth:N0} P:{ProcessFlowQueueDepth:N0} | Batch {LastBatchSize:N0} in {LastBatchDurationMs:F1} ms | Rendered {TotalLogEntriesRendered:N0}/{TotalLogEntriesReceived:N0} | Dropped {TotalLogEntriesDroppedFromPendingQueue:N0}";
}
