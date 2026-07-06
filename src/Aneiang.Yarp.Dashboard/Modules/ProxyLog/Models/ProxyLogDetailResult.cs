namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Full log detail result — combines meta fields and body fields for a single log entry.
/// Returned by /api/logs/detail/{id} when a user expands a row in the log viewer.
/// </summary>
public class ProxyLogDetailResult
{
    /// <summary>SQLite row ID from proxy_logs_meta.</summary>
    public long Id { get; set; }

    /// <summary>Local timestamp.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Log event type.</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Log level.</summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>Route identifier.</summary>
    public string? RouteId { get; set; }

    /// <summary>Cluster identifier.</summary>
    public string? ClusterId { get; set; }

    /// <summary>HTTP method.</summary>
    public string? Method { get; set; }

    /// <summary>Upstream request path.</summary>
    public string? UpstreamPath { get; set; }

    /// <summary>HTTP status code.</summary>
    public int StatusCode { get; set; }

    /// <summary>Elapsed time in milliseconds.</summary>
    public double ElapsedMs { get; set; }

    /// <summary>Trace identifier.</summary>
    public string? TraceId { get; set; }

    /// <summary>Whether request body exists.</summary>
    public bool HasRequestBody { get; set; }

    /// <summary>Whether response body exists.</summary>
    public bool HasResponseBody { get; set; }

    /// <summary>Downstream URL.</summary>
    public string? DownstreamUrl { get; set; }

    // --- Body fields (loaded from proxy_logs_body) ---

    /// <summary>Brief log message.</summary>
    public string? Message { get; set; }

    /// <summary>Request body content.</summary>
    public string? RequestBody { get; set; }

    /// <summary>Response body content.</summary>
    public string? ResponseBody { get; set; }

    /// <summary>Request headers (deserialized from JSON).</summary>
    public HeaderList? RequestHeaders { get; set; }

    /// <summary>Response headers (deserialized from JSON).</summary>
    public HeaderList? ResponseHeaders { get; set; }

    /// <summary>Downstream body.</summary>
    public string? DownstreamBody { get; set; }

    /// <summary>Exception details.</summary>
    public string? Exception { get; set; }
}
