using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Models
{
    /// <summary>
    /// Dashboard authorization mode.
    /// </summary>
    public enum DashboardAuthMode
    {
        /// <summary>No authorization.</summary>
        None,
        /// <summary>Authenticate via API key (header or query parameter).</summary>
        ApiKey,
        /// <summary>Authenticate via JWT with custom username and password (both configurable).</summary>
        CustomJwt,
        /// <summary>Authenticate via JWT with fixed username "admin" and configurable password.</summary>
        DefaultJwt
    }

    /// <summary>
    /// Options for configuring the Aneiang.Yarp.Dashboard.
    /// </summary>
    public class DashboardOptions
    {
        /// <summary>
        /// Configuration section name for binding from config files.
        /// </summary>
        public const string SectionName = "Gateway:Dashboard";

        /// <summary>
        /// Route prefix for all dashboard pages and APIs.
        /// <para>Default: <c>"apigateway"</c></para>
        /// </summary>
        public string RoutePrefix { get; set; } = "apigateway";

        /// <summary>
        /// Authorization mode.
        /// <para>Default: <see cref="DashboardAuthMode.None"/></para>
        /// </summary>
        public DashboardAuthMode AuthMode { get; set; } = DashboardAuthMode.None;

        // ─── API Key mode ─────────────────────────────────────

        /// <summary>
        /// API key value. Only used when <see cref="AuthMode"/> is <see cref="DashboardAuthMode.ApiKey"/>.
        /// <para>Clients can pass the key via header (default: <c>X-Api-Key</c>) or query parameter <c>api-key</c>.</para>
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Header name for API key authentication.
        /// <para>Default: <c>"X-Api-Key"</c></para>
        /// </summary>
        public string ApiKeyHeaderName { get; set; } = "X-Api-Key";

        // ─── JWT mode (CustomJwt / DefaultJwt) ────────────────

        /// <summary>
        /// Secret key for signing JWT tokens.
        /// <para>If not set, a random key is auto-generated (tokens invalidated on restart).</para>
        /// </summary>
        public string? JwtSecret { get; set; }

        /// <summary>
        /// Username for <see cref="DashboardAuthMode.CustomJwt"/> mode.
        /// <para>Not used in <see cref="DashboardAuthMode.DefaultJwt"/> mode (username is fixed to <c>"admin"</c>).</para>
        /// </summary>
        public string? JwtUsername { get; set; }

        /// <summary>
        /// Password for JWT login. Required for both <see cref="DashboardAuthMode.CustomJwt"/> and <see cref="DashboardAuthMode.DefaultJwt"/>.
        /// </summary>
        public string? JwtPassword { get; set; }

        // ─── Fully custom delegate (highest priority) ─────────

        /// <summary>
        /// Fully custom authorization delegate. If set, takes precedence over all other auth modes.
        /// </summary>
        public Func<HttpContext, Task<bool>>? AuthorizeRequest { get; set; }

        // ─── Standard ASP.NET Core policy (lowest priority) ──

        /// <summary>
        /// Callback to build a standard ASP.NET Core authorization policy.
        /// </summary>
        public Action<AuthorizationPolicyBuilder>? ConfigurePolicy { get; set; }

        /// <summary>
        /// Convenience role restriction.
        /// </summary>
        public string[]? AllowedRoles { get; set; }
    }
}
