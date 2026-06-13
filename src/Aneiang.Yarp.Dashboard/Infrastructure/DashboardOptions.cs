using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Infrastructure;

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
    /// Maximum number of queued requests when rate limit is exceeded. Default: 0 (reject immediately).
    /// Only effective when EnableRateLimiting is true.
    /// </summary>
    public int RateLimitQueueLimit { get; set; } = 0;

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

    // ─── Circuit Breaker ────────────────────────────────

    /// <summary>
    /// Default circuit breaker settings applied to all routes.
    /// Individual routes can override these via route metadata.
    /// </summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    // ─── Retry ─────────────────────────────────────────

    /// <summary>
    /// Default retry settings applied to all routes.
    /// Individual routes can override these via route metadata.
    /// </summary>
    public RetryOptions Retry { get; set; } = new();

    // ─── Rate Limiting ─────────────────────────────────

    /// <summary>
    /// Default rate limiting settings applied to all routes.
    /// Individual routes can override these via route metadata.
    /// </summary>
    public RateLimitOptions RateLimit { get; set; } = new();

    // ─── WAF ──────────────────────────────────────────

    /// <summary>
    /// Web Application Firewall settings.
    /// </summary>
    public WafOptions Waf { get; set; } = new();
}

/// <summary>
/// WAF rule type.
/// </summary>
public enum WafRuleType
{
    /// <summary>Regular expression match.</summary>
    Regex,
    /// <summary>SQL injection detection.</summary>
    SqlInjection,
    /// <summary>XSS script detection.</summary>
    Xss,
    /// <summary>Path traversal detection.</summary>
    PathTraversal
}

/// <summary>
/// WAF target to check.
/// </summary>
public enum WafTarget
{
    /// <summary>Request URI path.</summary>
    Uri,
    /// <summary>Query string parameters.</summary>
    QueryString,
    /// <summary>Request body.</summary>
    RequestBody,
    /// <summary>Request headers.</summary>
    Header
}

/// <summary>
/// WAF action when rule matches.
/// </summary>
public enum WafAction
{
    /// <summary>Log the violation only.</summary>
    Log,
    /// <summary>Block the request (403 Forbidden).</summary>
    Block
}

/// <summary>
/// Web Application Firewall configuration.
/// </summary>
public class WafOptions
{
    /// <summary>Enable WAF protection. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Dashboard route prefix. WAF skips requests whose path starts with this prefix.
    /// Default: "apigateway" (same as DashboardOptions.RoutePrefix).
    /// </summary>
    public string DashboardRoutePrefix { get; set; } = "apigateway";

    /// <summary>IP whitelist (allowed IPs). If non-empty, all non-whitelisted IPs are blocked.</summary>
    public List<string> IpWhitelist { get; set; } = new();

    /// <summary>IP blacklist (blocked IPs). These IPs are always blocked.</summary>
    public List<string> IpBlacklist { get; set; } = new();

    /// <summary>Maximum request body size in bytes. Default: 10MB.</summary>
    public long MaxRequestBodySize { get; set; } = 10 * 1024 * 1024;

    /// <summary>Maximum number of request headers. Default: 64.</summary>
    public int MaxHeaderCount { get; set; } = 64;

    /// <summary>Maximum size of a single header in bytes. Default: 8192.</summary>
    public int MaxHeaderSize { get; set; } = 8192;

    /// <summary>Enable built-in SQL injection detection. Default: true.</summary>
    public bool EnableSqlInjectionDetection { get; set; } = true;

    /// <summary>Enable built-in XSS detection. Default: true.</summary>
    public bool EnableXssDetection { get; set; } = true;

    /// <summary>Enable built-in path traversal detection. Default: true.</summary>
    public bool EnablePathTraversalDetection { get; set; } = true;

    /// <summary>Enable IP whitelist/blacklist checks. Default: true.</summary>
    public bool EnableIpCheck { get; set; } = true;

    /// <summary>Enable request size validation. Default: true.</summary>
    public bool EnableRequestSizeValidation { get; set; } = true;

    /// <summary>
    /// Additional CSP script-src directives, e.g., for Monaco Editor CDN or other trusted external sources.
    /// Example: <c>"https://cdn.example.com"</c>
    /// Configure to allow external script sources without weakening the default <c>script-src 'self'</c>.
    /// </summary>
    public string? ExtraScriptSources { get; set; }
}

/// <summary>
/// Circuit breaker configuration options.
/// Can be configured globally in DashboardOptions or per-route via metadata.
/// </summary>
public class CircuitBreakerOptions
{
    /// <summary>Enable circuit breaker for proxy routes. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of consecutive failures before opening the circuit.
    /// Route metadata key: CircuitBreaker:FailureThreshold. Default: 5.
    /// </summary>
    public int DefaultFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Seconds to wait before transitioning from Open to HalfOpen.
    /// Route metadata key: CircuitBreaker:RecoveryTimeoutSeconds. Default: 30.
    /// </summary>
    public int DefaultRecoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of requests allowed in HalfOpen state before deciding circuit state.
    /// Route metadata key: CircuitBreaker:HalfOpenMaxAttempts. Default: 1.
    /// </summary>
    public int HalfOpenMaxAttempts { get; set; } = 1;

    /// <summary>
    /// Maximum number of circuit breaker entries to track.
    /// Prevents memory exhaustion from too many unique circuit keys.
    /// Default: 1000.
    /// </summary>
    public int MaxCircuitCount { get; set; } = 1000;
}

/// <summary>
/// Retry policy configuration options.
/// Can be configured globally in DashboardOptions or per-route via metadata.
/// </summary>
public class RetryOptions
{
    /// <summary>Enable retry for failed proxy requests. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Maximum number of retry attempts.
    /// Route metadata key: Retry:MaxRetries. Default: 3.
    /// </summary>
    public int DefaultMaxRetries { get; set; } = 3;

    /// <summary>
    /// Base backoff delay in milliseconds (exponential: base * 2^attempt).
    /// Route metadata key: Retry:BackoffBaseMs. Default: 100.
    /// </summary>
    public int BackoffBaseMs { get; set; } = 100;

    /// <summary>
    /// Maximum jitter to add to backoff delay in milliseconds.
    /// Route metadata key: Retry:BackoffJitterMs. Default: 50.
    /// </summary>
    public int BackoffJitterMs { get; set; } = 50;

    /// <summary>
    /// Maximum total time allowed for retries in seconds.
    /// Route metadata key: Retry:TimeoutSeconds. Default: 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to try different destinations on retry.
    /// Route metadata key: Retry:UseDifferentDestination. Default: false.
    /// </summary>
    public bool UseDifferentDestination { get; set; } = false;

    /// <summary>
    /// Whether to retry non-idempotent requests (POST, PATCH).
    /// Route metadata key: Retry:RetryNonIdempotent. Default: false.
    /// </summary>
    public bool RetryNonIdempotent { get; set; } = false;

    /// <summary>
    /// Status codes that trigger a retry.
    /// Route metadata key: Retry:RetryOnStatusCodes (comma-separated). Default: 502,503,504.
    /// </summary>
    public List<int> DefaultRetryStatusCodes { get; set; } = new() { 502, 503, 504 };
}

/// <summary>
/// Rate limiting algorithm type.
/// </summary>
public enum RateLimitAlgorithm
{
    /// <summary>Fixed window counter algorithm.</summary>
    FixedWindow,
    /// <summary>Sliding window log algorithm.</summary>
    SlidingWindow,
    /// <summary>Token bucket algorithm.</summary>
    TokenBucket,
    /// <summary>Concurrency limit (max parallel requests).</summary>
    Concurrency
}

/// <summary>
/// Rate limiting configuration options.
/// Can be configured globally in DashboardOptions or per-route via metadata.
/// </summary>
public class RateLimitOptions
{
    /// <summary>Enable rate limiting for proxy routes. Default: false.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Rate limiting algorithm. Default: FixedWindow.</summary>
    public RateLimitAlgorithm Algorithm { get; set; } = RateLimitAlgorithm.FixedWindow;

    /// <summary>
    /// Maximum requests allowed per time window (FixedWindow/SlidingWindow).
    /// Route metadata key: RateLimit:PermitLimit. Default: 100.
    /// </summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>
    /// Time window duration. Default: "1m" (1 minute).
    /// Route metadata key: RateLimit:Window. Default: "1m".
    /// </summary>
    public string Window { get; set; } = "1m";

    /// <summary>
    /// Number of queued requests when limit is exceeded. Default: 0 (reject immediately).
    /// Route metadata key: RateLimit:QueueLimit. Default: 0.
    /// </summary>
    public int QueueLimit { get; set; } = 0;

    /// <summary>
    /// Token bucket: bucket capacity. Route metadata key: RateLimit:TokenBucketCapacity. Default: 100.
    /// </summary>
    public int TokenBucketCapacity { get; set; } = 100;

    /// <summary>
    /// Token bucket: tokens added per second. Route metadata key: RateLimit:TokenBucketRefillRate. Default: 10.
    /// </summary>
    public double TokenBucketRefillRate { get; set; } = 10;

    /// <summary>
    /// Concurrency limit: maximum parallel requests. Route metadata key: RateLimit:ConcurrencyLimit. Default: 50.
    /// </summary>
    public int ConcurrencyLimit { get; set; } = 50;

    /// <summary>
    /// Partition key for rate limiting: "IpAddress", "UserId", "Route", or "Global".
    /// Route metadata key: RateLimit:PartitionKey. Default: "IpAddress".
    /// </summary>
    public string PartitionKey { get; set; } = "IpAddress";
}
