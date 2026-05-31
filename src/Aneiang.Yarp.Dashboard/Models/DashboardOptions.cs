using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Models;

/// <summary>Dashboard authorization mode.</summary>
public enum DashboardAuthMode
{
    /// <summary>No authorization.</summary>
    None,

    /// <summary>API key via header or query.</summary>
    ApiKey,

    /// <summary>JWT with custom username + password.</summary>
    CustomJwt,

    /// <summary>JWT with fixed username "admin" + password.</summary>
    DefaultJwt
}

/// <summary>
/// Dashboard options. Binds from <c>Gateway:Dashboard</c> config section.
/// </summary>
/// <example>
/// <code>
/// // appsettings.json:
/// {
///   "Gateway": {
///     "Dashboard": {
///       "EnableProxyLogging": true,
///       "RoutePrefix": "apigateway",
///       "AuthMode": "DefaultJwt",
///       "JwtPassword": "YourSecurePassword"
///     }
///   }
/// }
/// </code>
/// </example>
public class DashboardOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:Dashboard";

    /// <summary>
    /// Enable or disable proxy request/response logging.
    /// When disabled, the middleware skips all capture logic,
    /// the EventSource listener is not active,
    /// and the log menu/panel is hidden from the UI.
    /// Default: true.
    /// </summary>
    public bool EnableProxyLogging { get; set; } = true;

    /// <summary>
    /// Route prefix for all dashboard pages. All dashboard URLs will be under /{RoutePrefix}/.
    /// Default: "apigateway".
    /// </summary>
    public string RoutePrefix { get; set; } = "apigateway";

    /// <summary>
    /// Authorization mode for accessing the dashboard.
    /// </summary>
    public DashboardAuthMode AuthMode { get; set; } = DashboardAuthMode.None;

    // ─── API Key mode ────────────────────────────────────

    /// <summary>API key value. Clients pass via header (default: X-Api-Key) or query param <c>api-key</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Header name for ApiKey mode. Default: X-Api-Key.</summary>
    public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

    // ─── JWT mode ────────────────────────────────────────

    /// <summary>JWT signing secret. Auto-generated if not set (invalidated on restart).</summary>
    public string? JwtSecret { get; set; }

    /// <summary>Username for CustomJwt mode.</summary>
    public string? JwtUsername { get; set; }

    /// <summary>Password for JWT login (required for both CustomJwt and DefaultJwt).</summary>
    public string? JwtPassword { get; set; }

    // ─── Custom delegate (highest priority) ───────────────

    /// <summary>
    /// Dashboard UI locale. Supported values: "zh-CN", "en-US".
    /// Users can switch the language on the page at runtime via a toggle button;
    /// the choice is persisted in localStorage. This option sets the default
    /// language for first-time visitors. Default: "zh-CN".
    /// </summary>
    public string Locale { get; set; } = "zh-CN";

    // ─── Log sampling and filtering ───────────────────────

    /// <summary>
    /// Maximum number of log entries kept in the in-memory ring buffer.
    /// Minimum: 100. When the buffer is full, oldest entries are overwritten.
    /// Default: 500.
    /// </summary>
    public int LogBufferCapacity { get; set; } = 500;

    // ─── Rate limiting ──────────────────────────────────

    /// <summary>
    /// Enable built-in rate limiting middleware for proxy routes.
    /// When enabled, a default fixed-window rate limiter is applied to all proxy requests.
    /// Default: false.
    /// </summary>
    public bool EnableRateLimiting { get; set; }

    /// <summary>
    /// Maximum number of requests allowed per time window. Default: 100.
    /// Only effective when EnableRateLimiting is true.
    /// </summary>
    public int RateLimitPermitLimit { get; set; } = 100;

    /// <summary>
    /// Time window for rate limiting. Default: "1m" (1 minute).
    /// Only effective when EnableRateLimiting is true.
    /// </summary>
    public string RateLimitWindow { get; set; } = "1m";

    /// <summary>
    /// Maximum number of queued requests when rate limit is exceeded. Default: 10.
    /// Only effective when EnableRateLimiting is true.
    /// </summary>
    public int RateLimitQueueLimit { get; set; } = 10;

    /// <summary>
    /// Enable or disable log sampling. When enabled, only a percentage of requests are logged.
    /// Default: false.
    /// </summary>
    public bool EnableLogSampling { get; set; } = false;

    /// <summary>
    /// Sampling rate (0.0 to 1.0). 1.0 means log all requests, 0.5 means log 50%.
    /// Only effective when EnableLogSampling is true. Default: 1.0.
    /// </summary>
    public double LogSamplingRate { get; set; } = 1.0;

    /// <summary>
    /// Only log error requests (status code >= 400). Default: false.
    /// </summary>
    public bool LogErrorsOnly { get; set; } = false;

    /// <summary>
    /// Minimum log level to capture. Supported values: Debug, Information, Warning, Error, Critical.
    /// Logs below this level are discarded immediately to save memory and CPU.
    /// Default: "Debug" (capture all).
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
    /// Maximum request/response body length to log (in bytes). Default: 8192 (8KB).
    /// Bodies exceeding this limit will be truncated.
    /// </summary>
    public int LogMaxBodyLength { get; set; } = 8192;

    /// <summary>
    /// Enable async logging via Channel for better throughput.
    /// When enabled, logs are enqueued and processed in background batches,
    /// reducing latency on the request path. Default: true.
    /// </summary>
    public bool EnableAsyncLogging { get; set; } = true;

    /// <summary>
    /// Header names to exclude from logging (case-insensitive).
    /// Default: ["Authorization", "Cookie", "Set-Cookie"]
    /// </summary>
    public List<string>? LogHeaderBlacklist { get; set; }

    /// <summary>
    /// Query parameter names to exclude from logging (case-insensitive).
    /// </summary>
    public List<string>? LogQueryBlacklist { get; set; }

    /// <summary>
    /// JSON field names to sanitize in request/response body (case-insensitive).
    /// Default: ["password", "token", "secret", "apikey", "api-key"]
    /// </summary>
    public List<string>? LogJsonFieldSanitizeList { get; set; }

    // ─── Custom delegate (highest priority) ───────────────

    /// <summary>Custom auth delegate. If set, takes precedence over all other auth modes.</summary>
    public Func<HttpContext, Task<bool>>? AuthorizeRequest { get; set; }

    // ─── Webhook Notifications ──────────────────────────

    /// <summary>
    /// List of webhook URLs to notify on configuration changes.
    /// Each URL will receive a POST request with the change details.
    /// Platform is auto-detected from the URL host.
    /// </summary>
    public List<string>? WebhookUrls { get; set; }

    /// <summary>
    /// Optional secret for HMAC-SHA256 webhook payload signature (generic fallback).
    /// If set, an X-Webhook-Signature header will be included.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Per-platform webhook signing secrets.
    /// Key: platform identifier (e.g. "dingtalk", "feishu", "wecom").
    /// Value: platform-specific signing secret.
    /// </summary>
    public Dictionary<string, string?>? WebhookSecrets { get; set; }

    /// <summary>
    /// List of enabled webhook event types. When null or empty, all events trigger notifications.
    /// Supported values: AddRoute, UpdateRoute, RemoveRoute, AddCluster, UpdateCluster, RemoveCluster, RenameCluster, RollbackConfig.
    /// </summary>
    public List<string>? WebhookEnabledEvents { get; set; }

    // ─── Health Check ──────────────────────────────────

    /// <summary>
    /// Enable passive health checking for all clusters by default.
    /// When enabled, YARP will automatically mark destinations as unhealthy after consecutive failures.
    /// Default: false.
    /// </summary>
    public bool EnablePassiveHealthCheck { get; set; }

    /// <summary>
    /// Default passive health check policy. Default: "ConsecutiveFailures".
    /// Only effective when EnablePassiveHealthCheck is true.
    /// </summary>
    public string PassiveHealthCheckPolicy { get; set; } = "ConsecutiveFailures";

    /// <summary>
    /// Default reactivation period for passive health checks. Default: "00:00:30" (30 seconds).
    /// Only effective when EnablePassiveHealthCheck is true.
    /// </summary>
    public string PassiveHealthCheckReactivationPeriod { get; set; } = "00:00:30";

}
