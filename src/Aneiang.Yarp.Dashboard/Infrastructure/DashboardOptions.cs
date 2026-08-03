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
