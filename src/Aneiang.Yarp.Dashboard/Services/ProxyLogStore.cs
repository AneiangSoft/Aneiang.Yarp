using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Pre-allocated ring buffer for recent log entries (lock-free, O(1) add).
/// Thread-safe, bounded memory usage, oldest entries are evicted when full.
/// Default in-memory implementation of IProxyLogStore.
/// </summary>
public sealed class ProxyLogStore : IProxyLogStore
{
    private readonly LogEntry[] _buffer;
    private int _head; // modified only via Interlocked
    private int _count; // approximate, for capacity info
    private long _evictedCount;

    /// <summary>
    /// Creates a ring buffer with specified capacity (default 500).
    /// </summary>
    /// <param name="capacity">Maximum number of entries to keep. Minimum: 100.</param>
    public ProxyLogStore(int capacity = 500) => _buffer = new LogEntry[Math.Max(100, capacity)];

    /// <summary>
    /// Add a log entry. Lock-free, O(1).
    /// When buffer is full, oldest entry is overwritten.
    /// </summary>
    /// <param name="entry">Log entry to add.</param>
    public void Add(LogEntry entry)
    {
        var index = Interlocked.Increment(ref _head) - 1;
        _buffer[index % _buffer.Length] = entry;

        // Track approximate count for reporting
        var snapshotCount = Volatile.Read(ref _head);
        if (snapshotCount < _buffer.Length)
            Volatile.Write(ref _count, snapshotCount);
        else
            Interlocked.Increment(ref _evictedCount);
    }

    /// <summary>
    /// Returns a snapshot of recent entries (newest first).
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>Snapshot containing entries, evicted count, and buffer size.</returns>
    public ProxyLogStoreSnapshot GetRecent(int count = 200)
    {
        // Take a consistent snapshot of _head
        var head = Volatile.Read(ref _head);
        var actualCount = Math.Min(head, _buffer.Length);
        var take = Math.Min(actualCount, Math.Max(1, count));
        var entries = new List<LogEntry>(take);

        for (int i = 0; i < take; i++)
        {
            var idx = (head - 1 - i + _buffer.Length) % _buffer.Length;
            entries.Add(_buffer[idx]);
        }

        return new ProxyLogStoreSnapshot
        {
            Entries = entries,
            EvictedCount = Volatile.Read(ref _evictedCount),
            BufferSize = actualCount
        };
    }

    /// <summary>
    /// Clear all entries. Thread-safe.
    /// </summary>
    public void Clear()
    {
        // Reset head to 0; new writes will overwrite old entries
        Interlocked.Exchange(ref _head, 0);
        Volatile.Write(ref _count, 0);
        Interlocked.Exchange(ref _evictedCount, 0);
    }
}
