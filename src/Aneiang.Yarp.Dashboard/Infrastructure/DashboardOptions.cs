using Microsoft.AspNetCore.Http;
using Aneiang.Yarp.Models;

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
///       "Auth": {
///         "AuthMode": "DefaultJwt",
///         "JwtPassword": "YourSecurePassword"
///       }
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

    // ──────────────── Core properties ────────────────

    /// <summary>
    /// Route prefix for all dashboard pages. Default: "apigateway".
    /// </summary>
    public string RoutePrefix { get; set; } = "apigateway";

    /// <summary>
    /// Dashboard UI locale. Default: "zh-CN".
    /// </summary>
    public string Locale { get; set; } = "zh-CN";

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

