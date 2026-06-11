using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Service for managing configuration snapshots, import/export, and version history using structured storage.
/// Snapshots are persisted via <see cref="IStructuredDataStore"/> so they survive restarts.
/// </summary>
public class ConfigPersistenceService : IConfigPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private const int MaxHistorySize = 50;

    private readonly IDynamicConfigPersistenceService _filePersistence;
    private readonly DynamicYarpConfigService? _dynamicConfig;
    private readonly IStructuredDataStore _store;
    private readonly ILogger<ConfigPersistenceService> _logger;
    private readonly List<ConfigSnapshot> _history = new();
    private readonly object _historyLock = new();
    private bool _historyLoaded;

    /// <summary>
    /// Initializes a new instance of ConfigPersistenceService.
    /// </summary>
    public ConfigPersistenceService(
        IDynamicConfigPersistenceService filePersistence,
        ILogger<ConfigPersistenceService> logger,
        IStructuredDataStore store,
        DynamicYarpConfigService? dynamicConfig = null)
    {
        _filePersistence = filePersistence;
        _logger = logger;
        _dynamicConfig = dynamicConfig;
        _store = store;
    }

    /// <summary>Load persisted snapshot history from structured store on first access.</summary>
    private async Task EnsureHistoryLoadedAsync()
    {
        if (_historyLoaded) return;
        _historyLoaded = true;

        try
        {
            var entities = await _store.GetConfigHistoryListAsync(MaxHistorySize);
            lock (_historyLock)
            {
                foreach (var entity in entities)
                {
                    var snapshot = entity.ToConfigSnapshot();
                    _history.Add(snapshot);
                }
                _logger.LogDebug("Loaded {Count} snapshots from structured store", entities.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted snapshots");
        }
    }

    /// <summary>
    /// Export full configuration in standard YARP format.
    /// </summary>
    public async Task<JsonElement> ExportFullConfigAsync()
    {
        var yarpRoutes = _dynamicConfig?.GetRoutes() ?? Array.Empty<RouteConfig>();
        var yarpClusters = _dynamicConfig?.GetClusters() ?? Array.Empty<ClusterConfig>();

        var routesDict = yarpRoutes.ToDictionary(
            r => r.RouteId ?? string.Empty,
            r => (object)new
            {
                ClusterId = r.ClusterId ?? string.Empty,
                Match = new { Path = r.Match?.Path ?? string.Empty },
                Transforms = r.Transforms,
                Order = r.Order ?? 50
            }
        );

        var clustersDict = new Dictionary<string, object>();
        foreach (var c in yarpClusters)
        {
            var dests = c.Destinations?.ToDictionary(
                d => d.Key,
                d => (object)new { Address = d.Value.Address ?? string.Empty }
            ) ?? new Dictionary<string, object>();
            clustersDict[c.ClusterId ?? string.Empty] = new
            {
                Destinations = dests,
                LoadBalancingPolicy = c.LoadBalancingPolicy,
                HealthCheck = c.HealthCheck
            };
        }

        var fullConfig = new Dictionary<string, object>
        {
            ["ReverseProxy"] = new Dictionary<string, object>
            {
                ["Routes"] = routesDict,
                ["Clusters"] = clustersDict
            }
        };

        var json = JsonSerializer.Serialize(fullConfig, _jsonOptions);
        _logger.LogInformation("Exported config: {Routes} routes, {Clusters} clusters", yarpRoutes.Count(), yarpClusters.Count());
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Import full configuration from standard YARP format.
    /// </summary>
    public async Task<bool> ImportFullConfigAsync(JsonElement config, string? clientIp = null)
    {
        try
        {
            await SaveSnapshotAsync("Before import", clientIp);

            var dynamicConfig = _filePersistence.LoadConfig();

            bool hasReverseProxy;
            JsonElement reverseProxy = default;
            if (config.TryGetProperty("reverseProxy", out var rpCamel))
            {
                hasReverseProxy = true;
                reverseProxy = rpCamel;
            }
            else if (config.TryGetProperty("ReverseProxy", out var rpPascal))
            {
                hasReverseProxy = true;
                reverseProxy = rpPascal;
            }
            else
            {
                hasReverseProxy = false;
            }

            if (hasReverseProxy)
            {
                bool hasRoutes;
                JsonElement routesElement = default;
                if (reverseProxy.TryGetProperty("routes", out var rCamel))
                {
                    hasRoutes = true;
                    routesElement = rCamel;
                }
                else if (reverseProxy.TryGetProperty("Routes", out var rPascal))
                {
                    hasRoutes = true;
                    routesElement = rPascal;
                }
                else
                {
                    hasRoutes = false;
                }

                if (hasRoutes)
                {
                    foreach (var route in routesElement.EnumerateObject())
                    {
                        var clusterId = route.Value.TryGetProperty("clusterId", out var cidCamel) ? cidCamel.GetString() ?? string.Empty
                            : route.Value.TryGetProperty("ClusterId", out var cidPascal) ? cidPascal.GetString() ?? string.Empty
                            : string.Empty;

                        var matchPath = string.Empty;
                        if (route.Value.TryGetProperty("match", out var matchCamel) && matchCamel.TryGetProperty("path", out var pathCamel))
                            matchPath = pathCamel.GetString() ?? string.Empty;
                        else if (route.Value.TryGetProperty("Match", out var matchPascal) && matchPascal.TryGetProperty("Path", out var pathPascal))
                            matchPath = pathPascal.GetString() ?? string.Empty;

                        List<Dictionary<string, string>>? transforms = null;
                        if (route.Value.TryGetProperty("transforms", out var tfCamel))
                            transforms = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(tfCamel.GetRawText(), _jsonOptions);
                        else if (route.Value.TryGetProperty("Transforms", out var tfPascal))
                            transforms = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(tfPascal.GetRawText(), _jsonOptions);

                        var order = route.Value.TryGetProperty("order", out var orderCamel) ? orderCamel.GetInt32()
                            : route.Value.TryGetProperty("Order", out var orderPascal) ? orderPascal.GetInt32()
                            : 50;

                        dynamicConfig.Routes.RemoveAll(r => r.RouteId == route.Name);
                        dynamicConfig.Routes.Add(new DynamicRouteConfig
                        {
                            RouteId = route.Name,
                            ClusterId = clusterId,
                            MatchPath = matchPath,
                            Transforms = transforms ?? new List<Dictionary<string, string>>(),
                            Order = order
                        });
                    }
                }

                bool hasClusters;
                JsonElement clustersElement = default;
                if (reverseProxy.TryGetProperty("clusters", out var cCamel))
                {
                    hasClusters = true;
                    clustersElement = cCamel;
                }
                else if (reverseProxy.TryGetProperty("Clusters", out var cPascal))
                {
                    hasClusters = true;
                    clustersElement = cPascal;
                }
                else
                {
                    hasClusters = false;
                }

                if (hasClusters)
                {
                    foreach (var cluster in clustersElement.EnumerateObject())
                    {
                        var destinations = new Dictionary<string, string>();
                        bool hasDests;
                        JsonElement destsElement = default;
                        if (cluster.Value.TryGetProperty("destinations", out var dCamel))
                        {
                            hasDests = true;
                            destsElement = dCamel;
                        }
                        else if (cluster.Value.TryGetProperty("Destinations", out var dPascal))
                        {
                            hasDests = true;
                            destsElement = dPascal;
                        }
                        else
                        {
                            hasDests = false;
                        }

                        if (hasDests)
                        {
                            foreach (var dest in destsElement.EnumerateObject())
                            {
                                var address = string.Empty;
                                if (dest.Value.ValueKind == JsonValueKind.String)
                                {
                                    address = dest.Value.GetString() ?? string.Empty;
                                }
                                else if (dest.Value.ValueKind == JsonValueKind.Object)
                                {
                                    if (dest.Value.TryGetProperty("address", out var addrCamel))
                                        address = addrCamel.GetString() ?? string.Empty;
                                    else if (dest.Value.TryGetProperty("Address", out var addrPascal))
                                        address = addrPascal.GetString() ?? string.Empty;
                                }
                                destinations[dest.Name] = address;
                            }
                        }

                        var loadBalancingPolicy = cluster.Value.TryGetProperty("loadBalancingPolicy", out var lbCamel) ? lbCamel.GetString()
                            : cluster.Value.TryGetProperty("LoadBalancingPolicy", out var lbPascal) ? lbPascal.GetString()
                            : null;

                        dynamicConfig.Clusters.RemoveAll(c => c.ClusterId == cluster.Name);
                        dynamicConfig.Clusters.Add(new DynamicClusterConfig
                        {
                            ClusterId = cluster.Name,
                            Destinations = destinations,
                            LoadBalancingPolicy = loadBalancingPolicy
                        });
                    }
                }
            }

            await _filePersistence.SaveConfigAsync(dynamicConfig);

            if (_dynamicConfig != null)
            {
                foreach (var c in dynamicConfig.Clusters)
                {
                    if (c.Destinations != null && c.Destinations.Count > 0)
                    {
                        await _dynamicConfig.TryAddCluster(c.ClusterId, c.Destinations, c.LoadBalancingPolicy, null, "import", "dashboard-user");
                    }
                }

                foreach (var r in dynamicConfig.Routes)
                {
                    var request = new RegisterRouteRequest
                    {
                        RouteName = r.RouteId,
                        ClusterName = r.ClusterId,
                        MatchPath = r.MatchPath,
                        Order = r.Order,
                        Transforms = r.Transforms
                    };
                    await _dynamicConfig.TryAddRoute(request, "import", "dashboard-user");
                }
            }

            _logger.LogInformation("Configuration imported successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import configuration");
            return false;
        }
    }

    /// <summary>
    /// Save a configuration snapshot for version management.
    /// </summary>
    public async Task<ConfigSnapshot> SaveSnapshotAsync(string? description = null, string? clientIp = null)
    {
        await EnsureHistoryLoadedAsync();

        var config = await ExportFullConfigAsync();

        var snapshot = new ConfigSnapshot
        {
            Description = description ?? "Manual snapshot",
            ClientIp = clientIp,
            Config = config
        };

        lock (_historyLock)
        {
            _history.Add(snapshot);
            while (_history.Count > MaxHistorySize)
            {
                _history.RemoveAt(0);
            }
        }

        // Persist to structured store
        var entity = snapshot.ToEntity("dashboard-user");
        _ = _store.SaveConfigHistoryAsync(entity);

        _logger.LogInformation(
            "Configuration snapshot saved: {VersionId}, Description: {Description}",
            snapshot.VersionId,
            snapshot.Description);

        return snapshot;
    }

    /// <summary>
    /// Get configuration history.
    /// </summary>
    public async Task<IReadOnlyList<ConfigSnapshot>> GetHistoryAsync()
    {
        await EnsureHistoryLoadedAsync();
        lock (_historyLock)
        {
            return _history.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Get configuration history (synchronous wrapper for compatibility).
    /// </summary>
    public IReadOnlyList<ConfigSnapshot> GetHistory()
    {
        if (_historyLoaded && _history.Count > 0)
        {
            lock (_historyLock)
            {
                return _history.ToList().AsReadOnly();
            }
        }
        return Array.Empty<ConfigSnapshot>();
    }

    /// <summary>
    /// Rollback to a specific version.
    /// </summary>
    public async Task<bool> RollbackAsync(string versionId, string? clientIp = null)
    {
        await EnsureHistoryLoadedAsync();

        ConfigSnapshot? snapshot;
        lock (_historyLock)
        {
            snapshot = _history.FirstOrDefault(s => s.VersionId == versionId);
        }

        if (snapshot == null)
        {
            // Try to load from structured store
            var entity = await _store.GetConfigHistoryAsync(versionId);
            if (entity != null)
            {
                snapshot = entity.ToConfigSnapshot();
            }
        }

        if (snapshot == null)
        {
            _logger.LogWarning("Snapshot not found: {VersionId}", versionId);
            return false;
        }

        try
        {
            await SaveSnapshotAsync("Before rollback to " + versionId, clientIp);

            var config = snapshot.Config;

            bool hasReverseProxy;
            JsonElement reverseProxy = default;
            if (config.TryGetProperty("reverseProxy", out var rpCamel))
            {
                hasReverseProxy = true;
                reverseProxy = rpCamel;
            }
            else if (config.TryGetProperty("ReverseProxy", out var rpPascal))
            {
                hasReverseProxy = true;
                reverseProxy = rpPascal;
            }
            else
            {
                hasReverseProxy = false;
            }

            var yarpRoutes = new List<RouteConfig>();
            var yarpClusters = new List<ClusterConfig>();

            if (hasReverseProxy)
            {
                bool hasRoutes;
                JsonElement routesElement = default;
                if (reverseProxy.TryGetProperty("routes", out var rCamel))
                {
                    hasRoutes = true;
                    routesElement = rCamel;
                }
                else if (reverseProxy.TryGetProperty("Routes", out var rPascal))
                {
                    hasRoutes = true;
                    routesElement = rPascal;
                }
                else
                {
                    hasRoutes = false;
                }

                if (hasRoutes)
                {
                    foreach (var route in routesElement.EnumerateObject())
                    {
                        var clusterId = route.Value.TryGetProperty("clusterId", out var cidCamel) ? cidCamel.GetString() ?? string.Empty
                            : route.Value.TryGetProperty("ClusterId", out var cidPascal) ? cidPascal.GetString() ?? string.Empty
                            : string.Empty;

                        var matchPath = string.Empty;
                        if (route.Value.TryGetProperty("match", out var matchCamel) && matchCamel.TryGetProperty("path", out var pathCamel))
                            matchPath = pathCamel.GetString() ?? string.Empty;
                        else if (route.Value.TryGetProperty("Match", out var matchPascal) && matchPascal.TryGetProperty("Path", out var pathPascal))
                            matchPath = pathPascal.GetString() ?? string.Empty;

                        var order = route.Value.TryGetProperty("order", out var orderCamel) ? orderCamel.GetInt32()
                            : route.Value.TryGetProperty("Order", out var orderPascal) ? orderPascal.GetInt32()
                            : 50;

                        IReadOnlyList<IReadOnlyDictionary<string, string>>? transforms = null;
                        if (route.Value.TryGetProperty("transforms", out var transformsCamel))
                        {
                            var list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(transformsCamel.GetRawText(), _jsonOptions);
                            transforms = list?.Select(d => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(d)).ToList();
                        }
                        else if (route.Value.TryGetProperty("Transforms", out var transformsPascal))
                        {
                            var list = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(transformsPascal.GetRawText(), _jsonOptions);
                            transforms = list?.Select(d => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(d)).ToList();
                        }

                        var routeConfig = new RouteConfig
                        {
                            RouteId = route.Name,
                            ClusterId = clusterId,
                            Match = new RouteMatch { Path = matchPath },
                            Order = order,
                            Transforms = transforms
                        };
                        yarpRoutes.Add(routeConfig);
                    }
                }

                bool hasClusters;
                JsonElement clustersElement = default;
                if (reverseProxy.TryGetProperty("clusters", out var cCamel))
                {
                    hasClusters = true;
                    clustersElement = cCamel;
                }
                else if (reverseProxy.TryGetProperty("Clusters", out var cPascal))
                {
                    hasClusters = true;
                    clustersElement = cPascal;
                }
                else
                {
                    hasClusters = false;
                }

                if (hasClusters)
                {
                    foreach (var cluster in clustersElement.EnumerateObject())
                    {
                        var loadBalancingPolicy = cluster.Value.TryGetProperty("loadBalancingPolicy", out var lbCamel) ? lbCamel.GetString()
                            : cluster.Value.TryGetProperty("LoadBalancingPolicy", out var lbPascal) ? lbPascal.GetString()
                            : null;

                        var destinations = new Dictionary<string, DestinationConfig>();
                        bool hasDests;
                        JsonElement destsElement = default;
                        if (cluster.Value.TryGetProperty("destinations", out var dCamel))
                        {
                            hasDests = true;
                            destsElement = dCamel;
                        }
                        else if (cluster.Value.TryGetProperty("Destinations", out var dPascal))
                        {
                            hasDests = true;
                            destsElement = dPascal;
                        }
                        else
                        {
                            hasDests = false;
                        }

                        if (hasDests)
                        {
                            foreach (var dest in destsElement.EnumerateObject())
                            {
                                var address = string.Empty;
                                if (dest.Value.ValueKind == JsonValueKind.String)
                                {
                                    address = dest.Value.GetString() ?? string.Empty;
                                }
                                else if (dest.Value.ValueKind == JsonValueKind.Object)
                                {
                                    if (dest.Value.TryGetProperty("address", out var addrCamel))
                                        address = addrCamel.GetString() ?? string.Empty;
                                    else if (dest.Value.TryGetProperty("Address", out var addrPascal))
                                        address = addrPascal.GetString() ?? string.Empty;
                                }
                                destinations[dest.Name] = new DestinationConfig { Address = address };
                            }
                        }

                        var clusterConfig = new ClusterConfig
                        {
                            ClusterId = cluster.Name,
                            Destinations = destinations,
                            LoadBalancingPolicy = loadBalancingPolicy
                        };
                        yarpClusters.Add(clusterConfig);
                    }
                }
            }

            if (_dynamicConfig != null && (yarpRoutes.Count > 0 || yarpClusters.Count > 0))
            {
                _logger.LogInformation("Rollback: replacing config with {Routes} routes and {Clusters} clusters", yarpRoutes.Count, yarpClusters.Count);
                await _dynamicConfig.ReplaceAllConfig(yarpRoutes, yarpClusters, "rollback", "dashboard-user");
            }
            else if (_dynamicConfig != null)
            {
                await _dynamicConfig.ReplaceAllConfig(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>(), "rollback", "dashboard-user");
            }
            else
            {
                var emptyConfig = new GatewayDynamicConfig();
                await _filePersistence.SaveConfigAsync(emptyConfig);
            }

            _logger.LogInformation("Rolled back to version: {VersionId}", versionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback to version: {VersionId}", versionId);
            return false;
        }
    }

    /// <summary>
    /// Validate configuration against basic YARP structure requirements.
    /// </summary>
    public ValidationResult ValidateConfig(JsonElement config)
    {
        var errors = new List<string>();

        if (config.TryGetProperty("ReverseProxy", out var reverseProxy))
        {
            if (!reverseProxy.TryGetProperty("Routes", out _))
                errors.Add("Missing 'ReverseProxy.Routes'");

            if (!reverseProxy.TryGetProperty("Clusters", out _))
                errors.Add("Missing 'ReverseProxy.Clusters'");
        }
        else
        {
            errors.Add("Missing 'ReverseProxy' section");
        }

        return new ValidationResult
        {
            Valid = errors.Count == 0,
            Errors = errors
        };
    }
}

/// <summary>
/// Validation result for configuration.
/// </summary>
public class ValidationResult
{
    public bool Valid { get; set; }
    public List<string> Errors { get; set; } = new();
}
