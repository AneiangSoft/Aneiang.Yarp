using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Interface for proxy log storage implementations.
/// Allows pluggable storage backends (in-memory, file, database, etc.).
/// </summary>
public interface IProxyLogStore
{
    /// <summary>
    /// Add a log entry. Thread-safe.
    /// </summary>
    /// <param name="entry">Log entry to add.</param>
    void Add(LogEntry entry);

    /// <summary>
    /// Returns a snapshot of recent entries (newest first).
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>Snapshot containing entries, evicted count, and buffer size.</returns>
    ProxyLogStoreSnapshot GetRecent(int count = 200);

    /// <summary>
    /// Clear all entries. Thread-safe.
    /// </summary>
    void Clear();

    /// <summary>
    /// Number of log entries dropped because the persistence Channel was full.
    /// Logs are operational data — when the Channel is full (high throughput),
    /// newest entries are dropped rather than blocking the proxy request thread.
    /// This counter tracks how many entries were lost, for frontend display.
    /// </summary>
    long DroppedCount { get; }
}
