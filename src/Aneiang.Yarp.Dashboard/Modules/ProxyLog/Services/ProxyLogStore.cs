using System.Threading.Channels;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Pre-allocated ring buffer for recent log entries (lock-free, O(1) add).
/// Thread-safe, bounded memory usage, oldest entries are evicted when full.
/// Default in-memory implementation of IProxyLogStore.
/// Optimized with bit-masking for power-of-2 capacity.
/// 
/// Memory optimization changes (v2.4):
/// - Buffer capacity reduced (default 50 → aligned to 64 internally)
/// - Large fields on old entries are set to null when overwritten to release GC pressure
/// - Bounded Channel with DropNewest for persistence pipeline (never blocks proxy thread)
/// - DroppedCount tracks Channel-full drops for frontend warning
/// - Removed dead _count field (was stuck at _bufferLength after buffer fills)
/// </summary>
public sealed class ProxyLogStore : IProxyLogStore
{
    private readonly LogEntry[] _buffer;
    private readonly int _bufferMask; // Bit mask for fast modulo (requires power-of-2 capacity)
    private readonly int _bufferLength; // Cached length for performance
    private int _head; // modified only via Interlocked
    private long _evictedCount;

    // Bounded Channel for persistence pipeline — DropNewest so logging never blocks proxy
    private readonly Channel<LogEntry> _persistenceChannel;
    private long _droppedCount; // Tracks entries dropped when Channel is full

    /// <summary>
    /// Creates a ring buffer with specified capacity (default 50, aligned to 64 internally).
    /// Capacity is rounded up to the next power of 2 for bit-masking optimization.
    /// Also creates a bounded Channel (capacity 1000, DropNewest) for persistence.
    /// </summary>
    /// <param name="capacity">Maximum number of entries to keep in memory buffer. Minimum: 16.</param>
    /// <param name="channelCapacity">Capacity of the persistence Channel. Default: 1000.</param>
    public ProxyLogStore(int capacity = 50, int channelCapacity = 1000)
    {
        // Round up to next power of 2 for bit-masking optimization
        var actualCapacity = NextPowerOf2(Math.Max(16, capacity));
        _buffer = new LogEntry[actualCapacity];
        _bufferLength = actualCapacity;
        _bufferMask = actualCapacity - 1; // For fast modulo: x & mask == x % capacity

        // Create bounded Channel with DropNewest — never block proxy request thread
        _persistenceChannel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropNewest,
                SingleReader = true, // Only AsyncLogPersistenceService reads
                SingleWriter = false // Multiple middleware threads write
            });
    }

    /// <summary>
    /// Expose the persistence Channel reader for AsyncLogPersistenceService to consume.
    /// </summary>
    public ChannelReader<LogEntry> PersistenceReader => _persistenceChannel.Reader;

    /// <summary>
    /// Number of log entries dropped because the persistence Channel was full.
    /// </summary>
    public long DroppedCount => Volatile.Read(ref _droppedCount);

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
    /// Large fields on the old entry are set to null to release memory.
    /// Entry is also written to the persistence Channel (DropNewest if full).
    /// Notifies all WebSocket subscribers.
    /// </summary>
    /// <param name="entry">Log entry to add.</param>
    public void Add(LogEntry entry)
    {
        var index = Interlocked.Increment(ref _head) - 1;
        var slot = FastModulo(index);

        // Release large field references on the old entry being overwritten
        var old = _buffer[slot];
        if (old != null)
        {
            old.RequestBody = null;
            old.ResponseBody = null;
            old.RequestHeaders = null;
            old.ResponseHeaders = null;
            old.DownstreamBody = null;
            old.Exception = null;
        }

        _buffer[slot] = entry;

        // Track evictions (no _count — it was dead code, stuck at _bufferLength after fill)
        if (Volatile.Read(ref _head) > _bufferLength)
            Interlocked.Increment(ref _evictedCount);

        // Write to persistence Channel (DropNewest — never block proxy thread)
        if (!_persistenceChannel.Writer.TryWrite(entry))
        {
            // Channel full — entry dropped, increment counter for frontend warning
            Interlocked.Increment(ref _droppedCount);
        }

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
            DroppedCount = Volatile.Read(ref _droppedCount),
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
        Interlocked.Exchange(ref _evictedCount, 0);
    }
}
