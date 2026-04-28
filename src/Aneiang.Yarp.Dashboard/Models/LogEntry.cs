namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>Lightweight log entry stored in ring buffer (value type) / 存储在环形缓冲区中的轻量日志条目.</summary>
public readonly record struct LogEntry
{
    /// <summary>UTC timestamp / UTC 时间戳.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Log level (Information, Warning, Error, etc.) / 日志级别.</summary>
    public string Level { get; init; }

    /// <summary>Logger category, e.g. Yarp.ReverseProxy.* / 日志类别.</summary>
    public string Category { get; init; }

    /// <summary>Log message / 日志消息.</summary>
    public string Message { get; init; }

    /// <summary>Exception details (stack trace), null if none / 异常详情.</summary>
    public string? Exception { get; init; }
}

/// <summary>Snapshot returned by log store (for UI polling) / 日志快照（供 UI 轮询）.</summary>
public class ProxyLogStoreSnapshot
{
    /// <summary>Entries in reverse chronological order / 条目（倒序）.</summary>
    public List<LogEntry> Entries { get; set; } = new();

    /// <summary>Total entries discarded / 已丢弃条目总数.</summary>
    public long EvictedCount { get; set; }

    /// <summary>Current buffer count / 当前缓冲区条目数.</summary>
    public int BufferSize { get; set; }
}
