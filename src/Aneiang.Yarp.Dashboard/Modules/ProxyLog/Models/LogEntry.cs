using System.Text.Json.Serialization;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Log event type enumeration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogEventType
{
    /// <summary>YARP internal event.</summary>
    YarpEvent,

    /// <summary>Incoming proxy request.</summary>
    ProxyRequest,

    /// <summary>Proxy response.</summary>
    ProxyResponse
}

/// <summary>
/// Structured log entry stored in ring buffer.
/// Designed for low-allocation, high-throughput logging in gateway scenarios.
/// Supports filtering, aggregation, and troubleshooting.
/// </summary>
public class LogEntry
{
    /// <summary>Local timestamp when the event occurred.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// Log event type: YarpEvent, ProxyRequest, ProxyResponse.
    /// </summary>
    public LogEventType EventType { get; init; }

    /// <summary>
    /// Log level: Information, Warning, Error, Critical, Debug.
    /// </summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>
    /// Logger category, e.g. Yarp.ReverseProxy.*, Gateway.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Brief log message shown in the log list.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Trace identifier for correlating request and response.
    /// </summary>
    public string? TraceId { get; init; }

    /// <summary>
    /// Route identifier associated with this log entry.
    /// </summary>
    public string? RouteId { get; init; }

    /// <summary>
    /// Cluster identifier associated with this log entry.
    /// </summary>
    public string? ClusterId { get; init; }

    /// <summary>
    /// Upstream request HTTP method (e.g. GET, POST).
    /// </summary>
    public string? Method { get; init; }

    /// <summary>
    /// Upstream request path.
    /// </summary>
    public string? UpstreamPath { get; init; }

    /// <summary>
    /// Downstream URL after transforms.
    /// </summary>
    public string? DownstreamUrl { get; init; }

    /// <summary>
    /// Downstream HTTP method after transforms.
    /// </summary>
    public string? DownstreamMethod { get; init; }

    /// <summary>
    /// Downstream request body after transforms/encryption (captured by DownstreamCaptureTransform).
    /// </summary>
    public string? DownstreamBody { get; init; }

    /// <summary>
    /// Indicates if downstream body was truncated.
    /// </summary>
    public bool DownstreamBodyTruncated { get; init; }

    /// <summary>
    /// HTTP status code (for response events).
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Elapsed time in milliseconds (for response events).
    /// </summary>
    public double? ElapsedMs { get; init; }

    /// <summary>
    /// Request headers (sanitized).
    /// </summary>
    public Dictionary<string, string>? RequestHeaders { get; init; }

    /// <summary>
    /// Response headers.
    /// </summary>
    public Dictionary<string, string>? ResponseHeaders { get; init; }

    /// <summary>
    /// Request body (truncated if exceeds limit).
    /// </summary>
    public string? RequestBody { get; init; }

    /// <summary>
    /// Indicates if request body was truncated.
    /// </summary>
    public bool RequestBodyTruncated { get; init; }

    /// <summary>
    /// Response body (truncated if exceeds limit).
    /// </summary>
    public string? ResponseBody { get; init; }

    /// <summary>
    /// Indicates if response body was truncated.
    /// </summary>
    public bool ResponseBodyTruncated { get; init; }

    /// <summary>
    /// Detailed content shown in expand panel (legacy support).
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Exception details (stack trace), null if no exception.
    /// </summary>
    public string? Exception { get; init; }
}

/// <summary>
/// Snapshot returned by log store for UI polling.
/// </summary>
public class ProxyLogStoreSnapshot
{
    /// <summary>
    /// Log entries in reverse chronological order (newest first).
    /// </summary>
    public List<LogEntry> Entries { get; set; } = new();

    /// <summary>
    /// Total number of entries that have been evicted from the buffer since startup.
    /// </summary>
    public long EvictedCount { get; set; }

    /// <summary>
    /// Current number of entries in the buffer.
    /// </summary>
    public int BufferSize { get; set; }

    /// <summary>
    /// Maximum capacity of the ring buffer.
    /// </summary>
    public int BufferCapacity { get; set; }
}
