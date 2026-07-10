namespace Aneiang.Yarp.Dashboard.Infrastructure;

/// <summary>
/// Proxy logging configuration options.
/// Extracted from DashboardOptions to isolate log-related configuration.
/// </summary>
public class ProxyLogOptions
{
    /// <summary>
    /// Enable or disable proxy request/response logging.
    /// Default: true.
    /// </summary>
    public bool EnableProxyLogging { get; set; } = true;

    /// <summary>
    /// Maximum number of log entries kept in the in-memory ring buffer.
    /// Default: 50 (aligned to 64 internally).
    /// </summary>
    public int LogBufferCapacity { get; set; } = 50;

    /// <summary>
    /// Enable log persistence to SQLite. Default: true.
    /// </summary>
    public bool LogPersistenceEnabled { get; set; } = true;

    /// <summary>
    /// Number of days to retain lightweight log metadata. Default: 7.
    /// </summary>
    public int LogMetaRetentionDays { get; set; } = 7;

    /// <summary>
    /// Number of days to retain large-field log details. Default: 3.
    /// </summary>
    public int LogBodyRetentionDays { get; set; } = 3;

    /// <summary>
    /// Enable or disable log sampling. Default: false.
    /// </summary>
    public bool EnableLogSampling { get; set; } = false;

    /// <summary>
    /// Sampling rate (0.0 to 1.0). Default: 1.0.
    /// </summary>
    public double LogSamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Only log error requests (status code >= 400). Default: false.
    /// </summary>
    public bool LogErrorsOnly { get; set; } = false;

    /// <summary>
    /// Minimum log level to capture. Default: "Debug".
    /// </summary>
    public string MinLogLevel { get; set; } = "Debug";

    /// <summary>
    /// Whitelist of route IDs to log. If empty, all routes are considered.
    /// </summary>
    public List<string>? LogRouteWhitelist { get; set; }

    /// <summary>
    /// Blacklist of route IDs to exclude from logging.
    /// </summary>
    public List<string>? LogRouteBlacklist { get; set; }

    /// <summary>
    /// Maximum request/response body length to log (in bytes). Default: 8192.
    /// </summary>
    public int LogMaxBodyLength { get; set; } = 8192;

    /// <summary>
    /// Enable request body capture on proxy hot path. Default: false.
    /// </summary>
    public bool EnableProxyRequestBodyCapture { get; set; } = false;

    /// <summary>
    /// Enable response body capture on proxy hot path. Default: false.
    /// </summary>
    public bool EnableProxyResponseBodyCapture { get; set; } = false;

    /// <summary>
    /// Maximum body bytes to buffer for proxy body capture. Default: 65536 (64KB).
    /// </summary>
    public int LogMaxBodyBufferBytes { get; set; } = 64 * 1024;

    /// <summary>
    /// Enable async logging via Channel. Default: true.
    /// </summary>
    public bool EnableAsyncLogging { get; set; } = true;

    /// <summary>
    /// Header names to exclude from logging (case-insensitive).
    /// </summary>
    public List<string>? LogHeaderBlacklist { get; set; }

    /// <summary>
    /// Query parameter names to exclude from logging (case-insensitive).
    /// </summary>
    public List<string>? LogQueryBlacklist { get; set; }

    /// <summary>
    /// JSON field names to sanitize in request/response body (case-insensitive).
    /// </summary>
    public List<string>? LogJsonFieldSanitizeList { get; set; }
}
