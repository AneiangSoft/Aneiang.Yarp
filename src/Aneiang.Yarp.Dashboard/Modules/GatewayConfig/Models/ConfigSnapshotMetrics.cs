namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;

/// <summary>Runtime metrics for the asynchronous configuration snapshot queue.</summary>
public sealed record ConfigSnapshotMetrics(
    int QueueLength,
    long EnqueuedCount,
    long ProcessedCount,
    long FailedCount,
    long DroppedCount);
