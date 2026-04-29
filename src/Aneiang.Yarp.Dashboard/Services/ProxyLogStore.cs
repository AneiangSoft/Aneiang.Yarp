using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Pre-allocated ring buffer for recent log entries (lock-based, O(1) add).
/// Thread-safe, bounded memory usage, oldest entries are evicted when full.
/// </summary>
public sealed class ProxyLogStore
{
    private readonly LogEntry[] _buffer;
    private int _head;
    private int _count;
    private long _evictedCount;

    /// <summary>
    /// Creates a ring buffer with specified capacity (default 500).
    /// </summary>
    /// <param name="capacity">Maximum number of entries to keep. Minimum: 100.</param>
    public ProxyLogStore(int capacity = 500) => _buffer = new LogEntry[Math.Max(100, capacity)];

    /// <summary>
    /// Add a log entry. Thread-safe, O(1).
    /// When buffer is full, oldest entry is evicted.
    /// </summary>
    /// <param name="entry">Log entry to add.</param>
    public void Add(LogEntry entry)
    {
        lock (_buffer)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++; else _evictedCount++;
        }
    }

    /// <summary>
    /// Returns a snapshot of recent entries (newest first).
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>Snapshot containing entries, evicted count, and buffer size.</returns>
    public ProxyLogStoreSnapshot GetRecent(int count = 200)
    {
        lock (_buffer)
        {
            int take = Math.Min(_count, Math.Max(1, count));
            var entries = new List<LogEntry>(take);
            for (int i = 0; i < take; i++)
                entries.Add(_buffer[(_head - 1 - i + _buffer.Length) % _buffer.Length]);

            return new ProxyLogStoreSnapshot { Entries = entries, EvictedCount = _evictedCount, BufferSize = _count };
        }
    }

    /// <summary>
    /// Clear all entries. Thread-safe.
    /// </summary>
    public void Clear()
    {
        lock (_buffer) { Array.Clear(_buffer, 0, _buffer.Length); _head = 0; _count = 0; _evictedCount = 0; }
    }
}
