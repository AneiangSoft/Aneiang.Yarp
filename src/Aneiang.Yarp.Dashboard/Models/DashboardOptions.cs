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

    // ─── Custom delegate (highest priority) ───────────────

    /// <summary>Custom auth delegate. If set, takes precedence over all other auth modes.</summary>
    public Func<HttpContext, Task<bool>>? AuthorizeRequest { get; set; }
}
