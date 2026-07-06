namespace Aneiang.Yarp.Storage.Entities;

/// <summary>
/// Lightweight proxy log metadata entity.
/// Maps to proxy_logs_meta SQLite table — stores only small fields for list display and stats aggregation.
/// Large fields (body, headers, exception) are stored separately in proxy_logs_body.
/// Each row is approximately 200-500 bytes.
/// </summary>
public class ProxyLogMetaEntity
{
    /// <summary>SQLite auto-increment row ID.</summary>
    public long Id { get; set; }

    /// <summary>Local timestamp (ISO 8601 format).</summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>Log event type: 'ProxyRequest' or 'ProxyResponse'.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Log level: Information, Warning, Error, etc.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Route identifier (nullable).</summary>
    public string? RouteId { get; set; }

    /// <summary>Cluster identifier (nullable).</summary>
    public string? ClusterId { get; set; }

    /// <summary>HTTP method (GET, POST, etc.). Nullable for error entries.</summary>
    public string? Method { get; set; }

    /// <summary>Upstream request path + query string.</summary>
    public string? UpstreamPath { get; set; }

    /// <summary>HTTP status code. 0 if not applicable (ProxyRequest entries).</summary>
    public int StatusCode { get; set; }

    /// <summary>Elapsed time in milliseconds. 0 for ProxyRequest entries.</summary>
    public double ElapsedMs { get; set; }

    /// <summary>Trace identifier for correlating request and response.</summary>
    public string? TraceId { get; set; }

    /// <summary>Whether request body exists in proxy_logs_body table.</summary>
    public int HasRequestBody { get; set; }

    /// <summary>Whether response body exists in proxy_logs_body table.</summary>
    public int HasResponseBody { get; set; }

    /// <summary>Downstream URL after transforms.</summary>
    public string? DownstreamUrl { get; set; }
}
