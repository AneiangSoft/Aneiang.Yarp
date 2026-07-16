namespace Aneiang.Yarp.Dashboard.Infrastructure.Deployment;

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
