namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;

/// <summary>
/// Structured log entry stored in ring buffer.
/// Captures full request/response pairs for proxy troubleshooting.
/// </summary>
public class LogEntry
{
    /// <summary>Local timestamp when the event occurred.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Log event type: ProxyRequest, ProxyResponse.</summary>
    public LogEventType EventType { get; init; }

    /// <summary>Log level: Information, Warning, Error.</summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>
    /// Brief log message shown in the log list.
    /// Nullable for ProxyRequest/ProxyResponse entries — the frontend derives the display text
    /// from EventType + Method + UpstreamPath + StatusCode when Message is null,
    /// saving ~50-100 bytes per LogEntry (redundant string elimination).
    /// </summary>
    public string? Message { get; init; }

    /// <summary>Trace identifier for correlating request and response.</summary>
    public string? TraceId { get; init; }

    /// <summary>Route identifier associated with this log entry.</summary>
    public string? RouteId { get; init; }

    /// <summary>Cluster identifier associated with this log entry.</summary>
    public string? ClusterId { get; init; }

    /// <summary>Upstream request HTTP method (e.g. GET, POST).</summary>
    public string? Method { get; init; }

    /// <summary>Upstream request path.</summary>
    public string? UpstreamPath { get; init; }

    /// <summary>Downstream URL after transforms.</summary>
    public string? DownstreamUrl { get; init; }

    /// <summary>Downstream HTTP method after transforms.</summary>
    public string? DownstreamMethod { get; init; }

    /// <summary>Downstream request body after transforms (captured by DownstreamCaptureTransform).</summary>
    public string? DownstreamBody { get; set; }

    /// <summary>Indicates if downstream body was truncated.</summary>
    public bool DownstreamBodyTruncated { get; init; }

    /// <summary>HTTP status code (for response events).</summary>
    public int? StatusCode { get; init; }

    /// <summary>Elapsed time in milliseconds (for response events).</summary>
    public double? ElapsedMs { get; init; }

    /// <summary>Request headers (sanitized). Uses HeaderList to avoid Dictionary hash table overhead.</summary>
    public HeaderList? RequestHeaders { get; set; }

    /// <summary>Response headers. Uses HeaderList to avoid Dictionary hash table overhead.</summary>
    public HeaderList? ResponseHeaders { get; set; }

    /// <summary>Request body (truncated if exceeds limit).</summary>
    public string? RequestBody { get; set; }

    /// <summary>Indicates if request body was truncated.</summary>
    public bool RequestBodyTruncated { get; init; }

    /// <summary>Response body (truncated if exceeds limit).</summary>
    public string? ResponseBody { get; set; }

    /// <summary>Indicates if response body was truncated.</summary>
    public bool ResponseBodyTruncated { get; init; }

    /// <summary>Exception details (stack trace), null if no exception.</summary>
    public string? Exception { get; set; }
}

