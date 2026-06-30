using Aneiang.Yarp.Models;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Builds immutable snapshots from the mutable working set and pushes them to
/// <see cref="AneiangProxyConfigProvider"/> so YARP can hot-reload.
/// Policy metadata is merged into route metadata and transforms are normalized
/// for the published (YARP-facing) copy only; the authoritative records are untouched.
/// </summary>
internal class DynamicConfigPublisher : IDynamicConfigPublisher
{
    private readonly AneiangProxyConfigProvider _configProvider;
    private readonly ILogger<DynamicConfigPublisher> _logger;

    public DynamicConfigPublisher(
        AneiangProxyConfigProvider configProvider,
        ILogger<DynamicConfigPublisher> logger)
    {
        _configProvider = configProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Publish(GatewayDynamicConfig config, long version)
    {
        var publishRoutes = new List<DynamicRouteConfig>(config.Routes.Count);
        foreach (var dynRoute in config.Routes)
        {
            var mergedMetadata = DynamicYarpConfigHelpers.MergeRouteMetadata(dynRoute.Config.Metadata, dynRoute.Metadata);
            var publishedConfig = DynamicYarpConfigHelpers.NormalizeTransforms(dynRoute.Config with { Metadata = mergedMetadata });
            publishRoutes.Add(new DynamicRouteConfig
            {
                Config = publishedConfig,
                RouteUid = dynRoute.RouteUid,
                ClusterUid = dynRoute.ClusterUid,
                DisplayName = dynRoute.DisplayName,
                Source = dynRoute.Source,
                CreatedAt = dynRoute.CreatedAt,
                CreatedBy = dynRoute.CreatedBy,
                Metadata = dynRoute.Metadata
            });
        }

        var publishClusters = new List<DynamicClusterConfig>(config.Clusters.Count);
        foreach (var dynCluster in config.Clusters)
        {
            var nativeConfig = SanitizeCluster(dynCluster.Config);

            // Merge the domain-model health check into the native config when the native side
            // does not already carry one.
            if (nativeConfig.HealthCheck == null && dynCluster.HealthCheck != null)
            {
                var built = DynamicYarpConfigHelpers.BuildClusterHealthCheck(dynCluster.HealthCheck);
                if (built != null)
                    nativeConfig = nativeConfig with { HealthCheck = built };
            }

            publishClusters.Add(new DynamicClusterConfig
            {
                Config = nativeConfig,
                ClusterUid = dynCluster.ClusterUid,
                DisplayName = dynCluster.DisplayName,
                HealthCheck = dynCluster.HealthCheck,
                Source = dynCluster.Source,
                CreatedAt = dynCluster.CreatedAt,
                CreatedBy = dynCluster.CreatedBy,
                LastHeartbeat = dynCluster.LastHeartbeat,
                CircuitBreaker = dynCluster.CircuitBreaker
            });
        }

        _configProvider.ApplyFromDynamic(publishRoutes, publishClusters, version);
    }

    /// <inheritdoc />
    public ClusterConfig SanitizeCluster(ClusterConfig cluster)
    {
        if (cluster.Destinations == null || cluster.Destinations.Count == 0)
            return cluster;

        var validDests = new Dictionary<string, DestinationConfig>(
            cluster.Destinations.Where(d => !string.IsNullOrWhiteSpace(d.Value?.Address)));

        if (validDests.Count == cluster.Destinations.Count)
            return cluster;

        _logger.LogWarning(
            "Dropped {InvalidCount} invalid destinations from cluster {ClusterId}",
            cluster.Destinations.Count - validDests.Count,
            cluster.ClusterId ?? "unknown");

        return cluster with { Destinations = validDests };
    }
}
