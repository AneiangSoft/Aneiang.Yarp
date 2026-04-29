namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>
/// Lightweight log entry stored in ring buffer.
/// Designed for low-allocation, high-throughput logging in gateway scenarios.
/// </summary>
public readonly record struct LogEntry
{
    /// <summary>Local timestamp when the event occurred.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Log level: Information, Warning, Error, Critical, Debug.
    /// </summary>
    public string Level { get; init; }

    /// <summary>
    /// Logger category, e.g. Yarp.ReverseProxy.*, Gateway.
    /// </summary>
    public string Category { get; init; }

    /// <summary>
    /// Brief log message shown in the log list.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Detailed content shown in expand panel (request/response body, downstream URL, etc.).
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Exception details (stack trace), null if no exception.
    /// </summary>
    public string? Exception { get; init; }
}

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
    /// Current number of entries in the buffer.
    /// </summary>
    public int BufferSize { get; set; }
}
