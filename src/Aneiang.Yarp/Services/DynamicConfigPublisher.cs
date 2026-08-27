using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services.ProxyConfigHealth;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Builds immutable snapshots from the mutable working set and pushes them to
/// <see cref="AneiangProxyConfigProvider"/> so YARP can hot-reload.
/// Transforms are normalized for the published (YARP-facing) copy only;
/// the authoritative records are untouched.
/// </summary>
internal class DynamicConfigPublisher : IDynamicConfigPublisher
{
    private readonly AneiangProxyConfigProvider _configProvider;
    private readonly IGatewaySnapshotCompiler _snapshotCompiler;
    private readonly IGatewaySnapshotPublisher _snapshotPublisher;
    private readonly IConfigValidator _configValidator;
    private readonly IProxyConfigErrorStore _errorStore;
    private readonly ILogger<DynamicConfigPublisher> _logger;

    public DynamicConfigPublisher(
        AneiangProxyConfigProvider configProvider,
        IGatewaySnapshotCompiler snapshotCompiler,
        IGatewaySnapshotPublisher snapshotPublisher,
        IConfigValidator configValidator,
        IProxyConfigErrorStore errorStore,
        ILogger<DynamicConfigPublisher> logger)
    {
        _configProvider = configProvider;
        _snapshotCompiler = snapshotCompiler;
        _snapshotPublisher = snapshotPublisher;
        _configValidator = configValidator;
        _errorStore = errorStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Publish(GatewayDynamicConfig config, long version)
    {
        var publishRoutes = new List<DynamicRouteConfig>(config.Routes.Count);
        foreach (var dynRoute in config.Routes)
        {
            // Skip disabled routes: keep data in working set but don't forward traffic
            if (!dynRoute.Enabled) continue;

            var publishedConfig = DynamicYarpConfigHelpers.NormalizeTransforms(dynRoute.Config);
            publishRoutes.Add(new DynamicRouteConfig
            {
                Config = publishedConfig,
                RouteUid = dynRoute.RouteUid,
                ClusterUid = dynRoute.ClusterUid,
                DisplayName = dynRoute.DisplayName,
                Source = dynRoute.Source,
                CreatedAt = dynRoute.CreatedAt,
                CreatedBy = dynRoute.CreatedBy,
                Enabled = true
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
                LastHeartbeat = dynCluster.LastHeartbeat
            });
        }

        var snapshotVersion = Math.Max(_snapshotPublisher.Current.Version + 1, Math.Max(1, version));
        var gatewaySnapshot = _snapshotCompiler.CompileAsync(
                publishRoutes.Select(x => x.Config).ToList(),
                publishClusters.Select(x => x.Config).ToList(),
                snapshotVersion)
            .GetAwaiter()
            .GetResult();
        _snapshotPublisher.Publish(gatewaySnapshot);

        var compiledRoutes = gatewaySnapshot.Routes
            .ToDictionary(x => x.RouteId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var route in publishRoutes)
        {
            if (compiledRoutes.TryGetValue(route.Config.RouteId ?? string.Empty, out var compiled))
                route.Config = compiled;
        }

        var compiledClusters = gatewaySnapshot.Clusters
            .ToDictionary(x => x.ClusterId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in publishClusters)
        {
            if (compiledClusters.TryGetValue(cluster.Config.ClusterId ?? string.Empty, out var compiled))
                cluster.Config = compiled;
        }

        // Pre-validate against YARP's IConfigValidator for precise RouteId/ClusterId attribution.
        // Non-blocking: errors are recorded for UI surfacing; the IConfigChangeListener remains
        // the source of truth for the actual reload outcome.
        PreValidate(publishRoutes, publishClusters);

        _configProvider.ApplyFromDynamic(publishRoutes, publishClusters, version);
    }

    /// <summary>
    /// Run YARP's <see cref="IConfigValidator"/> over the about-to-be-published routes and
    /// clusters, recording any failures into <see cref="IProxyConfigErrorStore"/> with
    /// <c>Source="prevalidate"</c>. This replaces (not appends to) the previous prevalidate
    /// set so the store reflects the latest cycle only.
    /// </summary>
    private void PreValidate(
        IReadOnlyList<DynamicRouteConfig> routes,
        IReadOnlyList<DynamicClusterConfig> clusters)
    {
        var errors = new List<ProxyConfigError>();
        var now = DateTime.UtcNow;

        foreach (var route in routes)
        {
            IList<Exception> routeErrors;
            try
            {
                routeErrors = _configValidator.ValidateRouteAsync(route.Config).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Prevalidate ValidateRouteAsync threw for {RouteId}.",
                    route.Config.RouteId);
                continue;
            }

            foreach (var ex in routeErrors)
            {
                errors.Add(new ProxyConfigError
                {
                    RouteId = route.Config.RouteId,
                    Message = ex.Message?.Trim() ?? ex.GetType().Name,
                    ExceptionType = ex.GetType().FullName,
                    OccurredAt = now,
                    Source = "prevalidate"
                });
            }
        }

        foreach (var cluster in clusters)
        {
            IList<Exception> clusterErrors;
            try
            {
                clusterErrors = _configValidator.ValidateClusterAsync(cluster.Config).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Prevalidate ValidateClusterAsync threw for {ClusterId}.",
                    cluster.Config.ClusterId);
                continue;
            }

            foreach (var ex in clusterErrors)
            {
                errors.Add(new ProxyConfigError
                {
                    ClusterId = cluster.Config.ClusterId,
                    Message = ex.Message?.Trim() ?? ex.GetType().Name,
                    ExceptionType = ex.GetType().FullName,
                    OccurredAt = now,
                    Source = "prevalidate"
                });
            }
        }

        _errorStore.ReplaceErrors("prevalidate", errors);
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
