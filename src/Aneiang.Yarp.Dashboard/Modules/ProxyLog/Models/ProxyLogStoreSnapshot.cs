namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Snapshot returned by log store for UI polling.
/// </summary>
public class ProxyLogStoreSnapshot
{
    /// <summary>
    /// Log entries in reverse chronological order (newest first).
    /// </summary>
    public List<LogEntry> Entries { get; set; } = new();

    /// <summary>
    /// Total number of entries that have been evicted from the buffer since startup.
    /// </summary>
    public long EvictedCount { get; set; }

    /// <summary>
    /// Number of log entries dropped because the persistence Channel was full.
    /// These entries were written to the in-memory buffer but never persisted to SQLite.
    /// </summary>
    public long DroppedCount { get; set; }

    /// <summary>
    /// Current number of entries in the buffer.
    /// </summary>
    public int BufferSize { get; set; }

    /// <summary>
    /// Maximum capacity of the ring buffer.
    /// </summary>
    public int BufferCapacity { get; set; }

    /// <summary>Current page number when paged query is used.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Page size when paged query is used.</summary>
    public int PageSize { get; set; }

    /// <summary>Total entries matching the query before paging.</summary>
    public int Total { get; set; }

    /// <summary>Total pages matching the query.</summary>
    public int TotalPages { get; set; }
}
