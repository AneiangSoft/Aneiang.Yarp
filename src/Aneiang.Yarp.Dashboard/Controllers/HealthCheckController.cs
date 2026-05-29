using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>Health check status and management API.</summary>
[Route("api/health-check")]
[ApiController]
public class HealthCheckController(DynamicYarpConfigService dynamicConfig) : ControllerBase
{
    /// <summary>Get health check configuration for all clusters.</summary>
    [HttpGet("clusters")]
    public IActionResult GetClusterHealthConfigs()
    {
        var config = dynamicConfig.GetDynamicConfig();
        if (config == null)
            return Ok(new { code = 200, data = Array.Empty<object>() });

        var healthConfigs = config.Clusters.Select(c => new
        {
            clusterId = c.ClusterId,
            healthCheck = c.HealthCheck,
            lastHeartbeat = c.LastHeartbeat
        }).ToList();

        return Ok(new { code = 200, data = healthConfigs });
    }

    /// <summary>Get passive health check status.</summary>
    [HttpGet("status")]
    public IActionResult GetHealthStatus()
    {
        var clusters = dynamicConfig.GetClusters();
        var status = clusters.Select(c => new
        {
            clusterId = c.ClusterId,
            healthCheck = c.HealthCheck != null ? new
            {
                active = c.HealthCheck.Active != null ? new
                {
                    enabled = c.HealthCheck.Active.Enabled ?? false,
                    path = c.HealthCheck.Active.Path,
                    interval = c.HealthCheck.Active.Interval?.ToString(),
                    timeout = c.HealthCheck.Active.Timeout?.ToString(),
                    policy = c.HealthCheck.Active.Policy
                } : null,
                passive = c.HealthCheck.Passive != null ? new
                {
                    enabled = c.HealthCheck.Passive.Enabled ?? false,
                    policy = c.HealthCheck.Passive.Policy,
                    reactivationPeriod = c.HealthCheck.Passive.ReactivationPeriod?.ToString()
                } : null,
                availableDestinationsPolicy = c.HealthCheck.AvailableDestinationsPolicy
            } : null,
            destinationCount = c.Destinations?.Count ?? 0
        }).ToList();

        return Ok(new { code = 200, data = status });
    }
}
