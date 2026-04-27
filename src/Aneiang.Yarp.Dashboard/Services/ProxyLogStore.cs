using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services
{
    /// <summary>
    /// Pre-allocated array-based ring buffer for storing recent log entries.
    /// Lock-based (contention is negligible for dashboard logging volume)
    /// and avoids per-read allocations of ConcurrentQueue.ToArray().
    /// </summary>
    public sealed class ProxyLogStore
    {
        private readonly LogEntry[] _buffer;
        private int _head;
        private int _count;
        private long _evictedCount;

        /// <summary>
        /// Creates a ring buffer with the specified capacity (default 500).
        /// </summary>
        public ProxyLogStore(int capacity = 500)
        {
            _buffer = new LogEntry[Math.Max(100, capacity)];
        }

        /// <summary>
        /// Adds a log entry. Thread-safe. O(1).
        /// </summary>
        public void Add(LogEntry entry)
        {
            lock (_buffer)
            {
                _buffer[_head] = entry;
                _head = (_head + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                    _count++;
                else
                    _evictedCount++;
            }
        }

        /// <summary>
        /// Returns a snapshot of the most recent entries (newest first).
        /// Only allocates a list of size min(count, available), not the full buffer.
        /// </summary>
        public ProxyLogStoreSnapshot GetRecent(int count = 200)
        {
            lock (_buffer)
            {
                int available = _count;
                int take = Math.Min(available, Math.Max(1, count));
                var entries = new List<LogEntry>(take);

                for (int i = 0; i < take; i++)
                {
                    int idx = (_head - 1 - i + _buffer.Length) % _buffer.Length;
                    entries.Add(_buffer[idx]);
                }

                return new ProxyLogStoreSnapshot
                {
                    Entries = entries,
                    EvictedCount = _evictedCount,
                    BufferSize = available
                };
            }
        }

        /// <summary>
        /// Clears all entries from the buffer.
        /// </summary>
        public void Clear()
        {
            lock (_buffer)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _head = 0;
                _count = 0;
                _evictedCount = 0;
            }
        }
    }
}
