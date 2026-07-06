namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Lightweight proxy log metadata item for list display.
/// Maps from proxy_logs_meta SQLite table — no large fields (body, headers).
/// Large fields are loaded separately via /api/logs/detail/{id} when a user expands a row.
/// </summary>
public class ProxyLogMetaItem
{
    /// <summary>SQLite row ID (auto-increment).</summary>
    public long Id { get; set; }

    /// <summary>Local timestamp when the event occurred.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Log event type: ProxyRequest or ProxyResponse.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Log level: Information, Warning, Error, etc.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Route identifier.</summary>
    public string? RouteId { get; set; }

    /// <summary>Cluster identifier.</summary>
    public string? ClusterId { get; set; }

    /// <summary>HTTP method (GET, POST, etc.).</summary>
    public string? Method { get; set; }

    /// <summary>Upstream request path + query string.</summary>
    public string? UpstreamPath { get; set; }

    /// <summary>HTTP status code (for response events).</summary>
    public int? StatusCode { get; set; }

    /// <summary>Elapsed time in milliseconds (for response events).</summary>
    public double? ElapsedMs { get; set; }

    /// <summary>Trace identifier for correlating request and response.</summary>
    public string? TraceId { get; set; }

    /// <summary>Whether request body exists in proxy_logs_body table.</summary>
    public bool HasRequestBody { get; set; }

    /// <summary>Whether response body exists in proxy_logs_body table.</summary>
    public bool HasResponseBody { get; set; }

    /// <summary>Downstream URL after transforms.</summary>
    public string? DownstreamUrl { get; set; }
}
