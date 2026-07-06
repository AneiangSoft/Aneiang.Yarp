using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.Waf.Models;

/// <summary>
/// Stores a single WAF security event for history retrieval.
/// Memory optimization (v2.4): Id changed from string (32 chars) to Guid struct (16 bytes).
/// JSON serialization preserves "N" format (no hyphens) via JsonPropertyName.
/// Large fields (RequestUri, MatchedValue) are set to null on dequeued events to release GC pressure.
/// </summary>
public class WafSecurityEvent
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ClientIp { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string? RequestUri { get; set; }
    public string? RequestMethod { get; set; }
    public string? RouteUid { get; set; }
    public string? RouteKeySnapshot { get; set; }
    public string? ClusterUid { get; set; }
    public string? ClusterKeySnapshot { get; set; }
    public string? MatchedValue { get; set; }
    public bool Blocked { get; set; } = true;
    public int? StatusCode { get; set; }
}

/// <summary>
/// In-memory ring buffer for WAF security events.
/// Memory optimization (v2.4):
/// - Buffer capacity reduced from 1000 to 64 (real-time display only, persistence to SQLite)
/// - Bounded Channel with DropNewest for persistence pipeline
/// - Dequeued events have large fields set to null before discard
/// </summary>
public class WafEventStore
{
    private readonly ConcurrentQueue<WafSecurityEvent> _events = new();
    private readonly int _maxRecords;

    // Bounded Channel for persistence — DropNewest so WAF event logging never blocks proxy
    private readonly Channel<WafSecurityEvent> _persistenceChannel;
    private long _droppedCount;

    public WafEventStore(int maxRecords = 64)
    {
        _maxRecords = maxRecords;

        _persistenceChannel = Channel.CreateBounded<WafSecurityEvent>(
            new BoundedChannelOptions(500)
            {
                FullMode = BoundedChannelFullMode.DropNewest,
                SingleReader = true,
                SingleWriter = false
            });
    }

    /// <summary>Expose the persistence Channel reader for WafEventPersistenceService to consume.</summary>
    public ChannelReader<WafSecurityEvent> PersistenceReader => _persistenceChannel.Reader;

    /// <summary>Number of events dropped because the persistence Channel was full.</summary>
    public long DroppedCount => Volatile.Read(ref _droppedCount);

    public void Add(WafSecurityEvent evt)
    {
        _events.Enqueue(evt);

        // Release large fields on dequeued events to reduce memory pressure
        while (_events.Count > _maxRecords && _events.TryDequeue(out var old))
        {
            old.RequestUri = null;
            old.MatchedValue = null;
        }

        // Write to persistence Channel (DropNewest — never block proxy thread)
        if (!_persistenceChannel.Writer.TryWrite(evt))
            Interlocked.Increment(ref _droppedCount);
    }

    public IReadOnlyList<WafSecurityEvent> GetRecent(int count = 100)
    {
        return _events.TakeLast(count).Reverse().ToList();
    }

    public IReadOnlyList<WafSecurityEvent> GetAll()
    {
        return _events.Reverse().ToList();
    }

    public void Clear() => _events.Clear();

    public int Count => _events.Count;
}
