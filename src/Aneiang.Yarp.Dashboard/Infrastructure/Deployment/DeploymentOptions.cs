namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

/// <summary>
/// Deployment / startup mode controlling how Dashboard and Proxy share or split ports.
/// </summary>
public enum DeploymentMode
{
    /// <summary>Auto-detect based on the number of configured Kestrel endpoints.</summary>
    Auto,

    /// <summary>Force single-port mode: Dashboard UI and Proxy forward share the same port.</summary>
    AllInOne,

    /// <summary>Force single-process multi-port mode with per-endpoint role routing.</summary>
    Split,

    /// <summary>Disable Dashboard UI/API; only YARP proxy forwarding is active.</summary>
    ProxyOnly,

    /// <summary>Disable YARP proxy forwarding; only Dashboard UI/API is active.</summary>
    DashboardOnly
}

/// <summary>
/// Deployment options bound from <c>Gateway:Deployment</c> configuration section.
/// Controls how the dashboard and proxy share ports, role-based routing, health checks, and config hot-reload.
/// </summary>
public class DeploymentOptions
{
    /// <summary>Config section name.</summary>
    public const string SectionName = "Gateway:Deployment";

    /// <summary>Startup mode. Default: <see cref="DeploymentMode.Auto"/>.</summary>
    public DeploymentMode Mode { get; set; } = DeploymentMode.Auto;

    /// <summary>Map from Kestrel endpoint name (e.g. "Proxy") to role (e.g. "Proxy"/"Dashboard"/"Admin"/"Health"/"All").</summary>
    public Dictionary<string, string> EndpointRoles { get; set; } = new();

    /// <summary>When true, endpoints with role "Admin" must bind to 127.0.0.1; otherwise startup fails.</summary>
    public bool RequireLoopbackForAdmin { get; set; } = false;

    /// <summary>When true, endpoints with role "Dashboard" must bind to 127.0.0.1; otherwise startup fails. Default: true.</summary>
    public bool RequireLoopbackForDashboard { get; set; } = true;

    /// <summary>Health check configuration.</summary>
    public HealthCheckDeploymentOptions HealthCheck { get; set; } = new();

    /// <summary>Configuration file hot-reload configuration.</summary>
    public HotReloadOptions HotReload { get; set; } = new();

    /// <summary>Resolved endpoint list populated at startup by <see cref="EndpointRoleResolver"/>.</summary>
    public List<EndpointRoleMap> ResolvedEndpoints { get; set; } = new();
}

/// <summary>
/// Health check endpoint configuration for K8s liveness/readiness probes and load balancer health checks.
/// </summary>
public class HealthCheckDeploymentOptions
{
    /// <summary>Enable health check endpoints. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Combined health check path. Default: "/health".</summary>
    public string Path { get; set; } = "/health";

    /// <summary>Readiness check path. Default: "/ready".</summary>
    public string ReadyPath { get; set; } = "/ready";

    /// <summary>Liveness check path. Default: "/live".</summary>
    public string LivePath { get; set; } = "/live";

    /// <summary>Optional shared-secret token. When set, clients must provide it via <c>X-Health-Token</c> header or <c>?token=</c> query.</summary>
    public string? Token { get; set; }

    /// <summary>Optional IP allow-list. When non-empty, only listed IPs can access the health endpoints.</summary>
    public List<string> AllowedIps { get; set; } = new();

    /// <summary>Check SQLite database connectivity in readiness probe. Default: true.</summary>
    public bool CheckDatabase { get; set; } = true;

    /// <summary>Check whether dynamic YARP config has been loaded. Default: true.</summary>
    public bool CheckConfigLoaded { get; set; } = true;
}

/// <summary>
/// Configuration file hot-reload configuration.
/// </summary>
public class HotReloadOptions
{
    /// <summary>Enable config file hot-reload. Default: true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Debounce window in milliseconds before applying the reload. Default: 500.</summary>
    public int DebounceMilliseconds { get; set; } = 500;

    /// <summary>List of file name patterns to watch. Supports <c>{Environment}</c> placeholder.</summary>
    public List<string> WatchedFiles { get; set; } = new()
    {
        "appsettings.json",
        "appsettings.{Environment}.json"
    };

    /// <summary>Fallback polling interval in seconds. Set to 0 to disable. Default: 30.</summary>
    public int FallbackPollSeconds { get; set; } = 30;

    /// <summary>Automatically roll back to the last good snapshot when reload fails. Default: true.</summary>
    public bool RollbackOnFailure { get; set; } = true;

    /// <summary>Maximum number of config snapshots to retain. Default: 5.</summary>
    public int MaxSnapshots { get; set; } = 5;
}

/// <summary>
/// Resolved mapping from a Kestrel endpoint to its deployment role.
/// Populated at startup by <see cref="EndpointRoleResolver"/>.
/// </summary>
public class EndpointRoleMap
{
    /// <summary>Kestrel endpoint name from configuration (e.g. "Proxy").</summary>
    public string EndpointName { get; init; } = string.Empty;

    /// <summary>Listening port.</summary>
    public int Port { get; init; }

    /// <summary>Bound IP address (e.g. "0.0.0.0" or "127.0.0.1").</summary>
    public string IpAddress { get; init; } = string.Empty;

    /// <summary>Role: Proxy / Dashboard / Admin / Health / All.</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>True when bound to a publicly routable IP (0.0.0.0, *, ::, empty).</summary>
    public bool IsPubliclyBound { get; init; }
}
