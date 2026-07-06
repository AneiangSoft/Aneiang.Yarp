namespace Aneiang.Yarp.Storage.Entities;

/// <summary>
/// Large-field proxy log body entity.
/// Maps to proxy_logs_body SQLite table — stores request/response bodies, headers, and exception.
/// Only loaded when a user expands a single log entry for detail viewing.
/// FK to proxy_logs_meta with CASCADE delete — when meta is cleaned up, body is automatically deleted.
/// </summary>
public class ProxyLogBodyEntity
{
    /// <summary>FK to proxy_logs_meta.Id (also the PK of this table).</summary>
    public long MetaId { get; set; }

    /// <summary>Brief log message (nullable — derivable from EventType+Method+UpstreamPath+StatusCode).</summary>
    public string? Message { get; set; }

    /// <summary>Request body content (truncated if exceeds LogMaxBodyLength).</summary>
    public string? RequestBody { get; set; }

    /// <summary>Response body content (truncated if exceeds LogMaxBodyLength).</summary>
    public string? ResponseBody { get; set; }

    /// <summary>Request headers as JSON string (sanitized).</summary>
    public string? RequestHeaders { get; set; }

    /// <summary>Response headers as JSON string.</summary>
    public string? ResponseHeaders { get; set; }

    /// <summary>Downstream response body (captured by DownstreamCaptureTransform).</summary>
    public string? DownstreamBody { get; set; }

    /// <summary>Exception details (stack trace), null if no exception.</summary>
    public string? Exception { get; set; }
}
