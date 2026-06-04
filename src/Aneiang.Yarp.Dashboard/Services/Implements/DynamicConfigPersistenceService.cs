using System.Text.Json;
using Aneiang.Yarp.Dashboard.Storage;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services.Implements;

/// <summary>
/// Service for persisting and loading dynamic gateway configurations using structured storage.
/// Routes and clusters are stored in separate tables for better query performance.
/// Uses in-memory caching to avoid blocking async calls.
/// </summary>
public class DynamicConfigPersistenceService : IDynamicConfigPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IStructuredDataStore _store;
    private readonly ILogger<DynamicConfigPersistenceService> _logger;
    private GatewayDynamicConfig? _cachedConfig;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private bool _initialized;

    public DynamicConfigPersistenceService(IStructuredDataStore store, ILogger<DynamicConfigPersistenceService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>
    /// Pre-loads config into memory cache during startup to avoid sync-over-async.
    /// </summary>
    public async Task PreloadAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            _cachedConfig = await LoadConfigInternalAsync(ct);
            _initialized = true;
            _logger.LogInformation(
                "Preloaded dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                _cachedConfig.Routes.Count, _cachedConfig.Clusters.Count);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<GatewayDynamicConfig> LoadConfigInternalAsync(CancellationToken ct)
    {
        try
        {
            // Load routes and clusters from structured tables
            var routeEntities = await _store.GetAllRoutesAsync(ct);
            var clusterEntities = await _store.GetAllClustersAsync(ct);

            var config = new GatewayDynamicConfig
            {
                Routes = routeEntities.ToRouteConfigs(),
                Clusters = new List<DynamicClusterConfig>()
            };

            // Load destinations for each cluster
            foreach (var clusterEntity in clusterEntities)
            {
                var cluster = clusterEntity.ToClusterConfig();
                var destEntities = await _store.GetDestinationsAsync(clusterEntity.ClusterId, ct);
                cluster.Destinations = destEntities.ToDestinations();
                config.Clusters.Add(cluster);
            }

            _logger.LogDebug("Loaded {RouteCount} routes and {ClusterCount} clusters from structured store",
                config.Routes.Count, config.Clusters.Count);

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dynamic config from structured store");
            return new GatewayDynamicConfig();
        }
    }

    /// <inheritdoc />
    public GatewayDynamicConfig LoadConfig()
    {
        if (_initialized && _cachedConfig != null)
            return _cachedConfig;

        // Fallback: load directly from store
        return LoadConfigInternalAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void SaveConfig(GatewayDynamicConfig config)
    {
        SaveConfigAsync(config).GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public void DeleteConfig()
    {
        DeleteConfigAsync().GetAwaiter().GetResult();
    }

    /// <inheritdoc />
    public bool ConfigFileExists()
    {
        try
        {
            var routes = _store.GetAllRoutesAsync().GetAwaiter().GetResult();
            return routes.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<GatewayDynamicConfig> LoadConfigAsync()
    {
        if (_initialized && _cachedConfig != null)
            return _cachedConfig;
        return await LoadConfigInternalAsync(CancellationToken.None);
    }

    /// <inheritdoc />
    public async Task SaveConfigAsync(GatewayDynamicConfig config)
    {
        try
        {
            config.LastModified = DateTime.UtcNow;

            // Save routes to structured table
            var routeEntities = config.Routes.Select(r => r.ToEntity()).ToList();
            await _store.SaveRoutesAsync(routeEntities);

            // Save clusters to structured table
            var clusterEntities = config.Clusters.Select(c => c.ToEntity()).ToList();
            await _store.SaveClustersAsync(clusterEntities);

            // Save destinations for each cluster
            foreach (var cluster in config.Clusters)
            {
                var destEntities = cluster.Destinations.Select(d => d.ToEntity(cluster.ClusterId)).ToList();
                await _store.SaveDestinationsAsync(cluster.ClusterId, destEntities);
            }

            _cachedConfig = config;
            _logger.LogInformation(
                "Saved dynamic config: {RouteCount} routes, {ClusterCount} clusters",
                config.Routes.Count, config.Clusters.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save dynamic config");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteConfigAsync()
    {
        try
        {
            // Delete all routes
            var routes = await _store.GetAllRoutesAsync();
            foreach (var route in routes)
            {
                await _store.DeleteRouteAsync(route.RouteId);
            }

            // Delete all clusters (cascades to destinations)
            var clusters = await _store.GetAllClustersAsync();
            foreach (var cluster in clusters)
            {
                await _store.DeleteClusterAsync(cluster.ClusterId);
            }

            _cachedConfig = new GatewayDynamicConfig();
            _logger.LogInformation("Deleted all dynamic config from structured store");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete dynamic config");
            throw;
        }
    }
}
