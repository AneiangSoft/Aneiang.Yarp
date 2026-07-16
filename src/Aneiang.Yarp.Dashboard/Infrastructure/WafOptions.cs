namespace Aneiang.Yarp.Dashboard.Infrastructure;

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
