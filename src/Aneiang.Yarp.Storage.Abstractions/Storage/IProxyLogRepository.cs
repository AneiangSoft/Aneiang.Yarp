using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Storage;

/// <summary>
/// Repository interface for proxy log persistence and query operations.
/// Handles both write (batch insert) and read (history, detail, stats) operations
/// against the proxy_logs_meta and proxy_logs_body SQLite tables.
/// </summary>
public interface IProxyLogRepository
{
    /// <summary>
    /// Batch insert log entries into proxy_logs_meta and proxy_logs_body tables.
    /// Each LogEntry is split: lightweight fields go to meta, large fields go to body.
    /// Uses a single transaction for the entire batch.
    /// </summary>
    Task WriteBatchAsync(IEnumerable<ProxyLogMetaEntity> metaEntries, IEnumerable<ProxyLogBodyEntity> bodyEntries, CancellationToken ct = default);

    /// <summary>
    /// Get a single log detail (full body fields) by meta ID.
    /// Returns null if the meta ID doesn't exist or has no body row.
    /// </summary>
    Task<ProxyLogBodyEntity?> GetBodyByMetaIdAsync(long metaId, CancellationToken ct = default);

    /// <summary>
    /// Get a single log meta row by ID.
    /// </summary>
    Task<ProxyLogMetaEntity?> GetMetaByIdAsync(long metaId, CancellationToken ct = default);

    /// <summary>
    /// Query historical log metadata with pagination and filtering.
    /// Only reads from the lightweight meta table — no large fields loaded.
    /// </summary>
    Task<(List<ProxyLogMetaEntity> Items, int TotalCount)> SearchAsync(
        int page, int pageSize,
        string? routeId = null, string? clusterId = null, string? level = null,
        int? statusCodeMin = null, int? statusCodeMax = null,
        DateTime? startTime = null, DateTime? endTime = null,
        string? keyword = null, string? eventType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete expired log metadata (and cascade-delete associated body rows).
    /// meta retention: entries older than metaRetentionDays are deleted.
    /// body retention: body rows older than bodyRetentionDays are deleted (even if meta still exists).
    /// </summary>
    Task CleanupAsync(int metaRetentionDays, int bodyRetentionDays, CancellationToken ct = default);

    /// <summary>
    /// Execute WAL checkpoint (TRUNCATE mode) to keep WAL file size bounded.
    /// Should be called after cleanup operations.
    /// </summary>
    Task CheckpointAsync(CancellationToken ct = default);

    /// <summary>
    /// Get aggregate statistics from the meta table for the last N minutes.
    /// Used by DashboardApiController.GetStats() instead of scanning the in-memory buffer.
    /// </summary>
    Task<ProxyLogStatsResult> GetStatsAsync(int recentMinutes, CancellationToken ct = default);

    /// <summary>
    /// Get traffic data aggregated by time bucket for operations dashboard.
    /// Returns time-bucketed counts of requests and errors.
    /// </summary>
    Task<List<ProxyLogTrafficBucket>> GetTrafficDataAsync(DateTime startTime, CancellationToken ct = default);

    /// <summary>
    /// Get top error routes ranked by error count for operations dashboard.
    /// </summary>
    Task<List<ProxyLogRouteIssue>> GetTopIssuesAsync(DateTime startTime, int count, CancellationToken ct = default);

    /// <summary>
    /// Get alert summary: count of 5xx errors in the last N minutes.
    /// </summary>
    Task<int> GetRecent5xxCountAsync(int minutes, CancellationToken ct = default);
}

/// <summary>
/// Aggregate statistics result from SQLite meta table query.
/// </summary>
public class ProxyLogStatsResult
{
    public long TotalRequests { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
    public double AvgLatencyMs { get; set; }
    public double P50LatencyMs { get; set; }
    public double P90LatencyMs { get; set; }
    public double P99LatencyMs { get; set; }
    public int RequestsPerMinute { get; set; }
}

/// <summary>
/// Time-bucketed traffic data for operations dashboard.
/// </summary>
public class ProxyLogTrafficBucket
{
    public DateTime TimeBucket { get; set; }
    public int RequestCount { get; set; }
    public int ErrorCount { get; set; }
}

/// <summary>
/// Route-level issue summary for operations dashboard.
/// </summary>
public class ProxyLogRouteIssue
{
    public string? RouteId { get; set; }
    public int TotalCount { get; set; }
    public int ErrorCount { get; set; }
}
