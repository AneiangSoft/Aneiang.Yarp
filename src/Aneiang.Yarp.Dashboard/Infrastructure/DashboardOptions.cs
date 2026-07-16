using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure;


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