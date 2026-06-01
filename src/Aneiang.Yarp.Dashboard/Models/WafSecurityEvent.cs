using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>
/// Stores a single WAF security event for history retrieval.
/// </summary>
public class WafSecurityEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ClientIp { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string? RequestUri { get; set; }
    public string? RequestMethod { get; set; }
    public string? MatchedValue { get; set; }
    public bool Blocked { get; set; } = true;
    public int? StatusCode { get; set; }
}

/// <summary>
/// In-memory ring buffer for WAF security events.
/// </summary>
public class WafEventStore
{
    private readonly ConcurrentQueue<WafSecurityEvent> _events = new();
    private readonly int _maxRecords;

    public WafEventStore(int maxRecords = 1000)
    {
        _maxRecords = maxRecords;
    }

    public void Add(WafSecurityEvent evt)
    {
        _events.Enqueue(evt);
        while (_events.Count > _maxRecords && _events.TryDequeue(out _)) { }
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
