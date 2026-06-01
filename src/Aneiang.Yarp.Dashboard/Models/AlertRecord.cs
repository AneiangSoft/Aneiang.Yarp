using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>
/// Stores a single alert event for history retrieval.
/// </summary>
public class AlertRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AlertType { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? ClusterId { get; set; }
    public string? DestinationId { get; set; }
    public string? RouteId { get; set; }
    public string? ClientIp { get; set; }
    public string? BlockReason { get; set; }
    public string? RequestUri { get; set; }
    public string? ErrorMessage { get; set; }
    public int? AttemptCount { get; set; }
    public int? LastStatusCode { get; set; }
}

/// <summary>
/// In-memory ring buffer for alert history.
/// </summary>
public class AlertHistoryStore
{
    private readonly ConcurrentQueue<AlertRecord> _alerts = new();
    private readonly int _maxRecords;

    public AlertHistoryStore(int maxRecords = 500)
    {
        _maxRecords = maxRecords;
    }

    public void Add(AlertRecord record)
    {
        _alerts.Enqueue(record);
        while (_alerts.Count > _maxRecords && _alerts.TryDequeue(out _)) { }
    }

    public IReadOnlyList<AlertRecord> GetRecent(int count = 100)
    {
        return _alerts.TakeLast(count).Reverse().ToList();
    }

    public IReadOnlyList<AlertRecord> GetAll()
    {
        return _alerts.Reverse().ToList();
    }

    public void Clear() => _alerts.Clear();

    public int Count => _alerts.Count;
}
