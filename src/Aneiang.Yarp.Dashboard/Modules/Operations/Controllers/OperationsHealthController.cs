using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Health check and system snapshot endpoints.
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsHealthController : ControllerBase
{
    private readonly IDashboardClusterQueryService _clusterQuery;

    public OperationsHealthController(IDashboardClusterQueryService clusterQuery)
    {
        _clusterQuery = clusterQuery;
    }

    [HttpGet("health-summary")]
    public IActionResult GetHealthSummary()
    {
        var clusters = _clusterQuery.GetClusters();
        var totalDestinations = 0;
        var healthyDestinations = 0;
        var unhealthyDestinations = 0;
        var unknownDestinations = 0;

        foreach (var cluster in clusters)
        {
            if (cluster.Destinations != null)
            {
                foreach (var dest in cluster.Destinations)
                {
                    totalDestinations++;
                    var health = dest.Health;
                    if (string.IsNullOrEmpty(health))
                        unknownDestinations++;
                    else if (health.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
                        healthyDestinations++;
                    else if (health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
                        unhealthyDestinations++;
                    else
                        unknownDestinations++;
                }
            }
        }

        var healthScore = totalDestinations > 0
            ? Math.Round((double)healthyDestinations / totalDestinations * 100, 1)
            : 100;

        var data = new HealthSummaryData
        {
            HealthScore = healthScore,
            TotalClusters = clusters.Count,
            TotalDestinations = totalDestinations,
            HealthyCount = healthyDestinations,
            UnhealthyCount = unhealthyDestinations,
            UnknownCount = unknownDestinations,
            Status = healthScore >= 90 ? "Healthy" : healthScore >= 70 ? "Warning" : "Critical"
        };

        return Ok(new { code = 200, data });
    }

    [HttpGet("snapshot")]
    public IActionResult ExportSnapshot()
    {
        var clusters = _clusterQuery.GetClusters();
        var snapshot = new SystemSnapshot
        {
            ExportedAt = DateTime.Now,
            ClusterCount = clusters.Count,
            RouteCount = 0,
            Clusters = clusters.Select(c => new ClusterSnapshot
            {
                Id = c.ClusterId,
                DestinationCount = c.Destinations?.Count ?? 0
            }).ToList(),
            Routes = new List<RouteSnapshot>()
        };

        return Ok(new { code = 200, data = snapshot });
    }

    private class HealthSummaryData
    {
        public double HealthScore { get; set; }
        public int TotalClusters { get; set; }
        public int TotalDestinations { get; set; }
        public int HealthyCount { get; set; }
        public int UnhealthyCount { get; set; }
        public int UnknownCount { get; set; }
        public string Status { get; set; } = "Healthy";
    }

    private class SystemSnapshot
    {
        public DateTime ExportedAt { get; set; }
        public int ClusterCount { get; set; }
        public int RouteCount { get; set; }
        public List<ClusterSnapshot> Clusters { get; set; } = new();
        public List<RouteSnapshot> Routes { get; set; } = new();
    }

    private class ClusterSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public int DestinationCount { get; set; }
    }

    private class RouteSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string? ClusterId { get; set; }
    }
}
