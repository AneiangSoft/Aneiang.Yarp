using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

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

    // ──────────────── Sub-option objects ────────────────

    /// <summary>Authentication configuration.</summary>
    public DashboardAuthOptions Auth { get; set; } = new();

    /// <summary>Proxy logging configuration.</summary>
    public ProxyLogOptions ProxyLog { get; set; } = new();

    // ──────────────── Core facade properties ────────────────

    /// <summary>
    /// Route prefix for all dashboard pages. Default: "apigateway".
    /// </summary>
    public string RoutePrefix { get; set; } = "apigateway";

    /// <summary>
    /// Dashboard UI locale. Default: "zh-CN".
    /// </summary>
    public string Locale { get; set; } = "zh-CN";

    // ──────────────── Auth facade (backward compat) ────────────────

    /// <summary>Authorization mode. Delegates to <see cref="Auth"/>.</summary>
    public DashboardAuthMode AuthMode { get => Auth.AuthMode; set => Auth.AuthMode = value; }

    /// <summary>API key. Delegates to <see cref="Auth"/>.</summary>
    public string? ApiKey { get => Auth.ApiKey; set => Auth.ApiKey = value; }

    /// <summary>API key header name. Delegates to <see cref="Auth"/>.</summary>
    public string ApiKeyHeaderName { get => Auth.ApiKeyHeaderName; set => Auth.ApiKeyHeaderName = value; }

    /// <summary>JWT secret. Delegates to <see cref="Auth"/>.</summary>
    public string? JwtSecret { get => Auth.JwtSecret; set => Auth.JwtSecret = value; }

    /// <summary>JWT username. Delegates to <see cref="Auth"/>.</summary>
    public string? JwtUsername { get => Auth.JwtUsername; set => Auth.JwtUsername = value; }

    /// <summary>JWT password. Delegates to <see cref="Auth"/>.</summary>
    public string? JwtPassword { get => Auth.JwtPassword; set => Auth.JwtPassword = value; }

    /// <summary>Enable 2FA. Delegates to <see cref="Auth"/>.</summary>
    public bool EnableTwoFactor { get => Auth.EnableTwoFactor; set => Auth.EnableTwoFactor = value; }

    /// <summary>TOTP secret. Delegates to <see cref="Auth"/>.</summary>
    public string? TwoFactorSecret { get => Auth.TwoFactorSecret; set => Auth.TwoFactorSecret = value; }

    /// <summary>Min password length. Delegates to <see cref="Auth"/>.</summary>
    public int MinPasswordLength { get => Auth.MinPasswordLength; set => Auth.MinPasswordLength = value; }

    /// <summary>Custom auth delegate. Delegates to <see cref="Auth"/>.</summary>
    public Func<HttpContext, Task<bool>>? AuthorizeRequest { get => Auth.AuthorizeRequest; set => Auth.AuthorizeRequest = value; }

    // ──────────────── ProxyLog facade (backward compat) ────────────────

    /// <summary>Enable proxy logging. Delegates to <see cref="ProxyLog"/>.</summary>
    public bool EnableProxyLogging { get => ProxyLog.EnableProxyLogging; set => ProxyLog.EnableProxyLogging = value; }

    /// <summary>Log buffer capacity. Delegates to <see cref="ProxyLog"/>.</summary>
    public int LogBufferCapacity { get => ProxyLog.LogBufferCapacity; set => ProxyLog.LogBufferCapacity = value; }

    /// <summary>Log persistence enabled. Delegates to <see cref="ProxyLog"/>.</summary>
    public bool LogPersistenceEnabled { get => ProxyLog.LogPersistenceEnabled; set => ProxyLog.LogPersistenceEnabled = value; }

    /// <summary>Log meta retention days. Delegates to <see cref="ProxyLog"/>.</summary>
    public int LogMetaRetentionDays { get => ProxyLog.LogMetaRetentionDays; set => ProxyLog.LogMetaRetentionDays = value; }

    /// <summary>Log body retention days. Delegates to <see cref="ProxyLog"/>.</summary>
    public int LogBodyRetentionDays { get => ProxyLog.LogBodyRetentionDays; set => ProxyLog.LogBodyRetentionDays = value; }

    /// <summary>Enable log sampling. Delegates to <see cref="ProxyLog"/>.</summary>
    public bool EnableLogSampling { get => ProxyLog.EnableLogSampling; set => ProxyLog.EnableLogSampling = value; }

    /// <summary>Log sampling rate. Delegates to <see cref="ProxyLog"/>.</summary>
    public double LogSamplingRate { get => ProxyLog.LogSamplingRate; set => ProxyLog.LogSamplingRate = value; }

    /// <summary>Log errors only. Delegates to <see cref="ProxyLog"/>.</summary>
    public bool LogErrorsOnly { get => ProxyLog.LogErrorsOnly; set => ProxyLog.LogErrorsOnly = value; }

    /// <summary>Min log level. Delegates to <see cref="ProxyLog"/>.</summary>
    public string MinLogLevel { get => ProxyLog.MinLogLevel; set => ProxyLog.MinLogLevel = value; }

    /// <summary>Log route whitelist. Delegates to <see cref="ProxyLog"/>.</summary>
    public List<string>? LogRouteWhitelist { get => ProxyLog.LogRouteWhitelist; set => ProxyLog.LogRouteWhitelist = value; }

    /// <summary>Log route blacklist. Delegates to <see cref="ProxyLog"/>.</summary>
    public List<string>? LogRouteBlacklist { get => ProxyLog.LogRouteBlacklist; set => ProxyLog.LogRouteBlacklist = value; }

    /// <summary>Max body length. Delegates to <see cref="ProxyLog"/>.</summary>
    public int LogMaxBodyLength { get => ProxyLog.LogMaxBodyLength; set => ProxyLog.LogMaxBodyLength = value; }

    /// <summary>Enable request body capture. Delegates to <see cref="ProxyLog"/>.</summary>
    public bool EnableProxyRequestBodyCapture { get => ProxyLog.EnableProxyRequestBodyCapture; set => ProxyLog.EnableProxyRequestBodyCapture = value; }

    /// <summary>Enable response body capture. Delegates to <see cref="ProxyLog"/>.</summary>
    public bool EnableProxyResponseBodyCapture { get => ProxyLog.EnableProxyResponseBodyCapture; set => ProxyLog.EnableProxyResponseBodyCapture = value; }

    /// <summary>Max body buffer bytes. Delegates to <see cref="ProxyLog"/>.</summary>
    public int LogMaxBodyBufferBytes { get => ProxyLog.LogMaxBodyBufferBytes; set => ProxyLog.LogMaxBodyBufferBytes = value; }

    /// <summary>Enable async logging. Delegates to <see cref="ProxyLog"/>.</summary>
    public bool EnableAsyncLogging { get => ProxyLog.EnableAsyncLogging; set => ProxyLog.EnableAsyncLogging = value; }

    /// <summary>Header blacklist. Delegates to <see cref="ProxyLog"/>.</summary>
    public List<string>? LogHeaderBlacklist { get => ProxyLog.LogHeaderBlacklist; set => ProxyLog.LogHeaderBlacklist = value; }

    /// <summary>Query blacklist. Delegates to <see cref="ProxyLog"/>.</summary>
    public List<string>? LogQueryBlacklist { get => ProxyLog.LogQueryBlacklist; set => ProxyLog.LogQueryBlacklist = value; }

    /// <summary>JSON field sanitize list. Delegates to <see cref="ProxyLog"/>.</summary>
    public List<string>? LogJsonFieldSanitizeList { get => ProxyLog.LogJsonFieldSanitizeList; set => ProxyLog.LogJsonFieldSanitizeList = value; }

    // ──────────────── Rate Limit (feature toggle) ────────────────

    /// <summary>
    /// Enable built-in rate limiting middleware for proxy routes. Default: false.
    /// </summary>
    public bool EnableRateLimiting { get; set; }

    /// <summary>Maximum requests per window. Default: 100.</summary>
    public int RateLimitPermitLimit { get; set; } = 100;

    /// <summary>Rate limit window. Default: "1m".</summary>
    public string RateLimitWindow { get; set; } = "1m";

    /// <summary>Queue limit. Default: 0.</summary>
    public int RateLimitQueueLimit { get; set; } = 0;

    // ──────────────── Passive Health Check ────────────────

    /// <summary>Enable passive health checking. Default: false.</summary>
    public bool EnablePassiveHealthCheck { get; set; }

    /// <summary>Health check policy. Default: "ConsecutiveFailures".</summary>
    public string PassiveHealthCheckPolicy { get; set; } = "ConsecutiveFailures";

    /// <summary>Reactivation period. Default: "00:00:30".</summary>
    public string PassiveHealthCheckReactivationPeriod { get; set; } = "00:00:30";

    // ──────────────── Sub-option objects (feature settings) ────────────────

    /// <summary>Default circuit breaker settings.</summary>
    public CircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>Default retry settings.</summary>
    public RetryOptions Retry { get; set; } = new();

    /// <summary>Default rate limiting settings.</summary>
    public RateLimitOptions RateLimit { get; set; } = new();

    /// <summary>WAF settings.</summary>
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

// ──────────────── Options sync helpers ────────────────
// These IConfigureOptions implementations sync flat DashboardOptions
// values to the sub-option objects for backward compatibility.

internal sealed class AuthOptionsSync : IConfigureOptions<DashboardAuthOptions>
{
    private readonly DashboardOptions _dash;
    public AuthOptionsSync(IOptions<DashboardOptions> dash) => _dash = dash.Value;

    public void Configure(DashboardAuthOptions auth)
    {
        if (_dash.AuthMode != DashboardAuthMode.None && auth.AuthMode == DashboardAuthMode.None)
            auth.AuthMode = _dash.AuthMode;
        auth.ApiKey ??= _dash.ApiKey;
        auth.JwtSecret ??= _dash.JwtSecret;
        auth.JwtUsername ??= _dash.JwtUsername;
        auth.JwtPassword ??= _dash.JwtPassword;
        auth.TwoFactorSecret ??= _dash.TwoFactorSecret;
        auth.AuthorizeRequest ??= _dash.AuthorizeRequest;
    }
}

internal sealed class ProxyLogOptionsSync : IConfigureOptions<ProxyLogOptions>
{
    private readonly DashboardOptions _dash;
    public ProxyLogOptionsSync(IOptions<DashboardOptions> dash) => _dash = dash.Value;

    public void Configure(ProxyLogOptions proxyLog)
    {
        // If flat property was set on DashboardOptions but ProxyLog sub-option wasn't,
        // copy the value over (backward compat for flat config)
        if (_dash.EnableProxyLogging && !proxyLog.EnableProxyLogging)
            proxyLog.EnableProxyLogging = true;
    }
}
