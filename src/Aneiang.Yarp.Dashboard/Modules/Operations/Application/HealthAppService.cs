using Aneiang.Yarp.Dashboard.Modules.Operations.Dtos;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Application;

/// <summary>
/// Application service for health checks and system snapshots.
/// Previously embedded in <see cref="Controllers.OperationsHealthController"/>.
/// </summary>
public interface IHealthAppService
{
    HealthSummaryData GetHealthSummary();
    SystemSnapshot ExportSnapshot();
}

/// <inheritdoc/>
public class HealthAppService : IHealthAppService
{
    private readonly IDashboardClusterQueryService _clusterQuery;

    public HealthAppService(IDashboardClusterQueryService clusterQuery)
    {
        _clusterQuery = clusterQuery;
    }

    public HealthSummaryData GetHealthSummary()
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

        return new HealthSummaryData
        {
            HealthScore = healthScore,
            TotalClusters = clusters.Count,
            TotalDestinations = totalDestinations,
            HealthyCount = healthyDestinations,
            UnhealthyCount = unhealthyDestinations,
            UnknownCount = unknownDestinations,
            Status = healthScore >= 90 ? "Healthy" : healthScore >= 70 ? "Warning" : "Critical"
        };
    }

    public SystemSnapshot ExportSnapshot()
    {
        var clusters = _clusterQuery.GetClusters();
        return new SystemSnapshot
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
    }
}
