using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Pre-allocated ring buffer for recent log entries (lock-free, O(1) add).
/// Thread-safe, bounded memory usage, oldest entries are evicted when full.
/// Default in-memory implementation of IProxyLogStore.
/// Optimized with bit-masking for power-of-2 capacity.
/// </summary>
public sealed class ProxyLogStore : IProxyLogStore
{
    private readonly LogEntry[] _buffer;
    private readonly int _bufferMask; // Bit mask for fast modulo (requires power-of-2 capacity)
    private readonly int _bufferLength; // Cached length for performance
    private int _head; // modified only via Interlocked
    private int _count; // approximate, for capacity info
    private long _evictedCount;

    /// <summary>
    /// Creates a ring buffer with specified capacity (default 512).
    /// Capacity is rounded up to the next power of 2 for bit-masking optimization.
    /// </summary>
    /// <param name="capacity">Maximum number of entries to keep. Minimum: 128.</param>
    public ProxyLogStore(int capacity = 512)
    {
        // Round up to next power of 2 for bit-masking optimization
        var actualCapacity = NextPowerOf2(Math.Max(128, capacity));
        _buffer = new LogEntry[actualCapacity];
        _bufferLength = actualCapacity;
        _bufferMask = actualCapacity - 1; // For fast modulo: x & mask == x % capacity
    }

    /// <summary>
    /// Rounds up to the next power of 2.
    /// </summary>
    private static int NextPowerOf2(int value)
    {
        if (value <= 1) return 2;
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }

    /// <summary>
    /// Fast modulo using bit-masking (requires power-of-2 capacity).
    /// This is ~2-3x faster than the % operator.
    /// </summary>
    private int FastModulo(int index) => index & _bufferMask;

    /// <summary>
    /// Add a log entry. Lock-free, O(1).
    /// When buffer is full, oldest entry is overwritten.
    /// Notifies all WebSocket subscribers.
    /// </summary>
    /// <param name="entry">Log entry to add.</param>
    public void Add(LogEntry entry)
    {
        var index = Interlocked.Increment(ref _head) - 1;
        _buffer[FastModulo(index)] = entry;

        // Track approximate count for reporting
        var snapshotCount = Volatile.Read(ref _head);
        if (snapshotCount < _bufferLength)
            Volatile.Write(ref _count, snapshotCount);
        else
            Interlocked.Increment(ref _evictedCount);

        // Notify WebSocket subscribers
        ProxyLogStoreExtensions.NotifySubscribers(this, entry);
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
        var actualCount = Math.Min(head, _bufferLength);
        var take = Math.Min(actualCount, Math.Max(1, count));
        var entries = new List<LogEntry>(take);

        for (int i = 0; i < take; i++)
        {
            var idx = head - 1 - i;
            // Fast wrap-around using bit masking
            entries.Add(_buffer[FastModulo(idx)]);
        }

        return new ProxyLogStoreSnapshot
        {
            Entries = entries,
            EvictedCount = Volatile.Read(ref _evictedCount),
            BufferSize = actualCount,
            BufferCapacity = _bufferLength
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
