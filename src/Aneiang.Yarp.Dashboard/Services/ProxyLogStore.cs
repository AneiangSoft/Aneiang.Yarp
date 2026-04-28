using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>Pre-allocated ring buffer for recent log entries (lock-based, O(1) add) / 预分配环形缓冲区保存最近日志.</summary>
public sealed class ProxyLogStore
{
    private readonly LogEntry[] _buffer;
    private int _head;
    private int _count;
    private long _evictedCount;

    /// <summary>Creates a ring buffer with specified capacity (default 500) / 创建指定容量的环形缓冲区.</summary>
    public ProxyLogStore(int capacity = 500) => _buffer = new LogEntry[Math.Max(100, capacity)];

    /// <summary>Add a log entry. Thread-safe, O(1) / 添加日志条目，线程安全.</summary>
    public void Add(LogEntry entry)
    {
        lock (_buffer)
        {
            _buffer[_head] = entry;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length) _count++; else _evictedCount++;
        }
    }

    /// <summary>Returns a snapshot of recent entries (newest first) / 返回最近条目快照（最新在前）.</summary>
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

    /// <summary>Clear all entries / 清空所有条目.</summary>
    public void Clear()
    {
        lock (_buffer) { Array.Clear(_buffer, 0, _buffer.Length); _head = 0; _count = 0; _evictedCount = 0; }
    }
}
