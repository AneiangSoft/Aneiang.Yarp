namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

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

    /// <summary>
    /// Automatically mount built-in dashboard/proxy middleware when <c>UseAneiangYarpDashboard()</c> is called.
    /// Set to false for advanced custom pipeline ordering. Default: true.
    /// </summary>
    public bool AutoUseMiddleware { get; set; } = true;

    /// <summary>Health check configuration.</summary>
    public HealthCheckDeploymentOptions HealthCheck { get; set; } = new();

    /// <summary>Resolved endpoint list populated at startup by <see cref="EndpointRoleResolver"/>.</summary>
    public List<EndpointRoleMap> ResolvedEndpoints { get; set; } = new();
}

