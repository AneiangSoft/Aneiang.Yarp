using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal class DynamicConfigPersister : IDynamicConfigPersister
{
    private readonly IRouteRepository _routeRepo;
    private readonly IClusterRepository _clusterRepo;
    private readonly ILogger<DynamicConfigPersister> _logger;

    public DynamicConfigPersister(
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        ILogger<DynamicConfigPersister> logger)
    {
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _logger = logger;
    }

    public async Task<GatewayDynamicConfig> LoadAsync()
    {
        try
        {
            var routeEntities = await _routeRepo.GetAllRoutesAsync();
            var clusterEntities = await _clusterRepo.GetAllClustersAsync();

            var config = new GatewayDynamicConfig
            {
                Routes = routeEntities.ToRouteConfigs()
            };

            foreach (var clusterEntity in clusterEntities)
            {
                var cluster = clusterEntity.ToClusterConfig();
                var destEntities = await _clusterRepo.GetDestinationsAsync(clusterEntity.ClusterId);
                cluster.Config = cluster.Config with
                {
                    Destinations = destEntities.ToDestinations()
                        .ToDictionary(d => d.Key, d => new DestinationConfig { Address = d.Value })
                };
                config.Clusters.Add(cluster);
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load config from repository, starting with empty config");
            return new GatewayDynamicConfig();
        }
    }

    public async Task SaveAsync(GatewayDynamicConfig config, string operationName, string? targetName = null)
    {
        try
        {
            if (await TryPersistIncrementalAsync(config, operationName, targetName))
                return;

            var targetRouteIds = new HashSet<string>(
                config.Routes.Select(r => r.Config.RouteId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            var targetClusterIds = new HashSet<string>(
                config.Clusters.Select(c => c.Config.ClusterId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);

            // Clean up stale routes
            var existingRoutes = await _routeRepo.GetAllRoutesAsync();
            foreach (var existing in existingRoutes)
            {
                if (!targetRouteIds.Contains(existing.RouteId))
                {
                    await _routeRepo.DeleteRouteAsync(existing.RouteId);
                    _logger.LogDebug("Deleted stale route '{RouteId}'", existing.RouteId);
                }
            }

            // Save current routes
            var routeEntities = config.Routes.Select(r => r.ToEntity()).ToList();
            await _routeRepo.SaveRoutesAsync(routeEntities);

            // Clean up stale clusters + destinations
            var existingClusters = await _clusterRepo.GetAllClustersAsync();
            foreach (var existing in existingClusters)
            {
                if (!targetClusterIds.Contains(existing.ClusterId))
                {
                    await _clusterRepo.DeleteDestinationsAsync(existing.ClusterId);
                    await _clusterRepo.DeleteClusterAsync(existing.ClusterId);
                    _logger.LogDebug("Deleted stale cluster '{ClusterId}'", existing.ClusterId);
                }
            }

            // Save current clusters
            var clusterEntities = config.Clusters.Select(c => c.ToEntity()).ToList();
            await _clusterRepo.SaveClustersAsync(clusterEntities);

            // Save destinations for each cluster
            foreach (var cluster in config.Clusters)
            {
                var clusterId = cluster.Config.ClusterId ?? string.Empty;
                var destEntities = DestinationsToDict(cluster.Config.Destinations)
                    .Select(d => new KeyValuePair<string, string>(d.Key, d.Value.Address ?? string.Empty).ToEntity(clusterId))
                    .ToList();
                await _clusterRepo.SaveDestinationsAsync(clusterId, destEntities);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to persist dynamic config for {OperationName}. Target: {TargetName}, RouteCount: {RouteCount}, ClusterCount: {ClusterCount}",
                operationName,
                targetName ?? "n/a",
                config.Routes.Count,
                config.Clusters.Count);
            throw;
        }
    }

    private async Task<bool> TryPersistIncrementalAsync(GatewayDynamicConfig config, string operationName, string? targetName)
    {
        if (string.IsNullOrWhiteSpace(targetName)) return false;

        if (operationName is "AddOrUpdateRoute" or "UpdateRouteMetadata")
        {
            var route = config.Routes.FirstOrDefault(r =>
                string.Equals(r.Config.RouteId, targetName, StringComparison.OrdinalIgnoreCase));
            if (route == null) return false;

            await _routeRepo.SaveRouteAsync(route.ToEntity());

            // When TryAddRoute creates an implicit cluster, persist it too.
            if (operationName == "AddOrUpdateRoute" && !string.IsNullOrEmpty(route.Config.ClusterId))
            {
                var cluster = config.Clusters.FirstOrDefault(c =>
                    string.Equals(c.Config.ClusterId, route.Config.ClusterId, StringComparison.OrdinalIgnoreCase));
                if (cluster != null)
                {
                    var clusterId = cluster.Config.ClusterId ?? string.Empty;
                    await _clusterRepo.SaveClusterAsync(cluster.ToEntity());
                    await _clusterRepo.SaveDestinationsAsync(
                        clusterId,
                        DestinationsToDict(cluster.Config.Destinations)
                            .Select(d => new KeyValuePair<string, string>(d.Key, d.Value.Address ?? string.Empty).ToEntity(clusterId))
                            .ToList());
                }
            }

            return true;
        }

        if (operationName is "AddCluster" or "UpdateCluster" or "CreateCluster" or "UpdateClusterCircuitBreaker")
        {
            var cluster = config.Clusters.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, targetName, StringComparison.OrdinalIgnoreCase));
            if (cluster == null) return false;

            var clusterId = cluster.Config.ClusterId ?? string.Empty;
            await _clusterRepo.SaveClusterAsync(cluster.ToEntity());
            await _clusterRepo.SaveDestinationsAsync(
                clusterId,
                DestinationsToDict(cluster.Config.Destinations)
                    .Select(d => new KeyValuePair<string, string>(d.Key, d.Value.Address ?? string.Empty).ToEntity(clusterId))
                    .ToList());
            return true;
        }

        return false;
    }

    private static Dictionary<string, DestinationConfig> DestinationsToDict(IReadOnlyDictionary<string, DestinationConfig>? dests)
        => dests is { Count: > 0 }
            ? new Dictionary<string, DestinationConfig>(dests)
            : new Dictionary<string, DestinationConfig>();
}
