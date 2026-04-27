namespace Aneiang.Yarp.Dashboard.Models
{
    /// <summary>
    /// Lightweight log entry stored in the ring buffer.
    /// Designed as a value type to minimize heap allocations.
    /// </summary>
    public readonly record struct LogEntry
    {
        /// <summary>UTC timestamp when the log was written.</summary>
        public DateTime Timestamp { get; init; }

        /// <summary>Log level (Information, Warning, Error, etc.).</summary>
        public string Level { get; init; }

        /// <summary>Logger category, e.g. Yarp.ReverseProxy.*.</summary>
        public string Category { get; init; }

        /// <summary>Log message body.</summary>
        public string Message { get; init; }

        /// <summary>Full exception details (stack trace), null if none.</summary>
        public string? Exception { get; init; }
    }

    /// <summary>
    /// Snapshot returned by the log store, containing the
    /// latest log entries and metadata for the UI to determine polling behavior.
    /// </summary>
    public class ProxyLogStoreSnapshot
    {
        /// <summary>Log entries in reverse chronological order (newest first).</summary>
        public List<LogEntry> Entries { get; set; } = new();

        /// <summary>Total entries discarded since the ring buffer was created.</summary>
        public long EvictedCount { get; set; }

        /// <summary>Current number of entries in the buffer.</summary>
        public int BufferSize { get; set; }
    }
}
