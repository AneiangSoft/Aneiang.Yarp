using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Service for managing configuration snapshots, import/export, and version history using dedicated repositories.
/// </summary>
public class ConfigPersistenceService : IConfigPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private const int MaxHistorySize = 50;

    private readonly DynamicYarpConfigService? _dynamicConfig;
    private readonly IConfigHistoryRepository _historyRepo;
    private readonly IRouteRepository _routeRepo;
    private readonly IClusterRepository _clusterRepo;
    private readonly ILogger<ConfigPersistenceService> _logger;
    private readonly List<ConfigSnapshot> _history = new();
    private readonly object _historyLock = new();
    private bool _historyLoaded;

    public ConfigPersistenceService(
        ILogger<ConfigPersistenceService> logger,
        IConfigHistoryRepository historyRepo,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        DynamicYarpConfigService? dynamicConfig = null)
    {
        _historyRepo = historyRepo;
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _logger = logger;
        _dynamicConfig = dynamicConfig;
    }

    /// <summary>Load persisted snapshot history from repository on first access.</summary>
    private async Task EnsureHistoryLoadedAsync()
    {
        if (_historyLoaded) return;
        _historyLoaded = true;

        try
        {
            var entities = await _historyRepo.GetConfigHistoryListAsync(MaxHistorySize);
            lock (_historyLock)
            {
                foreach (var entity in entities)
                {
                    _history.Add(entity.ToConfigSnapshot());
                }
                _logger.LogDebug("Loaded {Count} snapshots from repository", entities.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted snapshots");
        }
    }

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
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<bool> ImportFullConfigAsync(JsonElement config, string? clientIp = null)
    {
        try
        {
            await SaveSnapshotAsync("Before import", clientIp);

            // Build GatewayDynamicConfig from import data using repository
            var dynamicConfig = await LoadConfigFromRepositoryAsync();

            bool hasReverseProxy = config.TryGetProperty("reverseProxy", out var rpCamel) ||
                                   config.TryGetProperty("ReverseProxy", out rpCamel);
            if (!hasReverseProxy) return false;
            var reverseProxy = rpCamel;

            bool hasRoutes = reverseProxy.TryGetProperty("routes", out var rCamel) ||
                             reverseProxy.TryGetProperty("Routes", out rCamel);
            if (hasRoutes)
            {
                var routesElement = rCamel;
                foreach (var route in routesElement.EnumerateObject())
                {
                    var clusterId = route.Value.TryGetProperty("clusterId", out var cidC) ? cidC.GetString() ?? string.Empty
                        : route.Value.TryGetProperty("ClusterId", out var cidP) ? cidP.GetString() ?? string.Empty
                        : string.Empty;

                    var matchPath = string.Empty;
                    if (route.Value.TryGetProperty("match", out var mC) && mC.TryGetProperty("path", out var pC))
                        matchPath = pC.GetString() ?? string.Empty;
                    else if (route.Value.TryGetProperty("Match", out var mP) && mP.TryGetProperty("Path", out var pP))
                        matchPath = pP.GetString() ?? string.Empty;

                    List<Dictionary<string, string>>? transforms = null;
                    if (route.Value.TryGetProperty("transforms", out var tfC) || route.Value.TryGetProperty("Transforms", out tfC))
                        transforms = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(tfC.GetRawText(), _jsonOptions);

                    var order = route.Value.TryGetProperty("order", out var oC) ? oC.GetInt32()
                        : route.Value.TryGetProperty("Order", out var oP) ? oP.GetInt32()
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

            bool hasClusters = reverseProxy.TryGetProperty("clusters", out var cCamel) ||
                               reverseProxy.TryGetProperty("Clusters", out cCamel);
            if (hasClusters)
            {
                var clustersElement = cCamel;
                foreach (var cluster in clustersElement.EnumerateObject())
                {
                    var destinations = new Dictionary<string, string>();
                    if (cluster.Value.TryGetProperty("destinations", out var dC) || cluster.Value.TryGetProperty("Destinations", out dC))
                    {
                        foreach (var dest in dC.EnumerateObject())
                        {
                            var address = dest.Value.ValueKind == JsonValueKind.String
                                ? dest.Value.GetString() ?? string.Empty
                                : dest.Value.TryGetProperty("address", out var aC) ? aC.GetString() ?? string.Empty
                                : dest.Value.TryGetProperty("Address", out var aP) ? aP.GetString() ?? string.Empty
                                : string.Empty;
                            destinations[dest.Name] = address;
                        }
                    }

                    var lb = cluster.Value.TryGetProperty("loadBalancingPolicy", out var lbC) ? lbC.GetString()
                        : cluster.Value.TryGetProperty("LoadBalancingPolicy", out var lbP) ? lbP.GetString()
                        : null;

                    dynamicConfig.Clusters.RemoveAll(c => c.ClusterId == cluster.Name);
                    dynamicConfig.Clusters.Add(new DynamicClusterConfig
                    {
                        ClusterId = cluster.Name,
                        Destinations = destinations,
                        LoadBalancingPolicy = lb
                    });
                }
            }

            // Persist imported config via ReplaceAllConfig
            if (_dynamicConfig != null)
            {
                foreach (var c in dynamicConfig.Clusters)
                {
                    if (c.Destinations?.Count > 0)
                        await _dynamicConfig.TryAddCluster(c.ClusterId, c.Destinations, c.LoadBalancingPolicy, null, "import", "dashboard-user");
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
                _history.RemoveAt(0);
        }

        var entity = snapshot.ToEntity("dashboard-user");
        _ = _historyRepo.SaveConfigHistoryAsync(entity);

        _logger.LogInformation("Snapshot saved: {VersionId}, Description: {Description}", snapshot.VersionId, snapshot.Description);
        return snapshot;
    }

    public async Task<IReadOnlyList<ConfigSnapshot>> GetHistoryAsync()
    {
        await EnsureHistoryLoadedAsync();
        lock (_historyLock) { return _history.ToList().AsReadOnly(); }
    }

    public IReadOnlyList<ConfigSnapshot> GetHistory()
    {
        if (_historyLoaded && _history.Count > 0)
            lock (_historyLock) { return _history.ToList().AsReadOnly(); }
        return Array.Empty<ConfigSnapshot>();
    }

    public async Task ClearHistoryAsync()
    {
        await EnsureHistoryLoadedAsync();
        lock (_historyLock)
        {
            _history.Clear();
        }

        await _historyRepo.ClearConfigHistoryAsync();
        _logger.LogInformation("Configuration history cleared");
    }

    public async Task<bool> RollbackAsync(string versionId, string? clientIp = null)
    {
        await EnsureHistoryLoadedAsync();

        ConfigSnapshot? snapshot;
        lock (_historyLock) { snapshot = _history.FirstOrDefault(s => s.VersionId == versionId); }

        if (snapshot == null)
        {
            var entity = await _historyRepo.GetConfigHistoryAsync(versionId);
            if (entity != null) snapshot = entity.ToConfigSnapshot();
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
            bool hasReverseProxy = config.TryGetProperty("reverseProxy", out var rpC) || config.TryGetProperty("ReverseProxy", out rpC);
            var yarpRoutes = new List<RouteConfig>();
            var yarpClusters = new List<ClusterConfig>();

            if (hasReverseProxy)
            {
                var reverseProxy = rpC;
                if (reverseProxy.TryGetProperty("routes", out var rC) || reverseProxy.TryGetProperty("Routes", out rC))
                {
                    foreach (var route in rC.EnumerateObject())
                    {
                        var clusterId = route.Value.TryGetProperty("clusterId", out var cidC) ? cidC.GetString() ?? string.Empty
                            : route.Value.TryGetProperty("ClusterId", out var cidP) ? cidP.GetString() ?? string.Empty : string.Empty;

                        var matchPath = string.Empty;
                        if (route.Value.TryGetProperty("match", out var mC) && mC.TryGetProperty("path", out var pC))
                            matchPath = pC.GetString() ?? string.Empty;
                        else if (route.Value.TryGetProperty("Match", out var mP) && mP.TryGetProperty("Path", out var pP))
                            matchPath = pP.GetString() ?? string.Empty;

                        var order = route.Value.TryGetProperty("order", out var oC) ? oC.GetInt32()
                            : route.Value.TryGetProperty("Order", out var oP) ? oP.GetInt32() : 50;

                        var transforms = (route.Value.TryGetProperty("transforms", out var tfC) || route.Value.TryGetProperty("Transforms", out tfC))
                            ? JsonSerializer.Deserialize<List<Dictionary<string, string>>>(tfC.GetRawText(), _jsonOptions)?
                                .Select(d => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(d)).ToList()
                            : null;

                        yarpRoutes.Add(new RouteConfig
                        {
                            RouteId = route.Name,
                            ClusterId = clusterId,
                            Match = new RouteMatch { Path = matchPath },
                            Order = order,
                            Transforms = transforms
                        });
                    }
                }

                if (reverseProxy.TryGetProperty("clusters", out var clC) || reverseProxy.TryGetProperty("Clusters", out clC))
                {
                    foreach (var cluster in clC.EnumerateObject())
                    {
                        var lb = cluster.Value.TryGetProperty("loadBalancingPolicy", out var lbC) ? lbC.GetString()
                            : cluster.Value.TryGetProperty("LoadBalancingPolicy", out var lbP) ? lbP.GetString() : null;

                        var destinations = new Dictionary<string, DestinationConfig>();
                        if (cluster.Value.TryGetProperty("destinations", out var dC) || cluster.Value.TryGetProperty("Destinations", out dC))
                        {
                            foreach (var dest in dC.EnumerateObject())
                            {
                                var address = dest.Value.ValueKind == JsonValueKind.String ? dest.Value.GetString() ?? string.Empty
                                    : dest.Value.TryGetProperty("address", out var aC) ? aC.GetString() ?? string.Empty
                                    : dest.Value.TryGetProperty("Address", out var aP) ? aP.GetString() ?? string.Empty : string.Empty;
                                destinations[dest.Name] = new DestinationConfig { Address = address };
                            }
                        }

                        yarpClusters.Add(new ClusterConfig
                        {
                            ClusterId = cluster.Name,
                            Destinations = destinations,
                            LoadBalancingPolicy = lb
                        });
                    }
                }
            }

            if (_dynamicConfig != null)
                await _dynamicConfig.ReplaceAllConfig(yarpRoutes, yarpClusters, "rollback", "dashboard-user");

            _logger.LogInformation("Rolled back to version: {VersionId}", versionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback to version: {VersionId}", versionId);
            return false;
        }
    }

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

        return new ValidationResult { Valid = errors.Count == 0, Errors = errors };
    }

    private async Task<GatewayDynamicConfig> LoadConfigFromRepositoryAsync()
    {
        try
        {
            var routeEntities = await _routeRepo.GetAllRoutesAsync();
            var clusterEntities = await _clusterRepo.GetAllClustersAsync();

            var config = new GatewayDynamicConfig { Routes = EntityMapper.ToRouteConfigs(routeEntities) };

            foreach (var ce in clusterEntities)
            {
                var dynCluster = EntityMapper.ToClusterConfig(ce);
                var destEntities = await _clusterRepo.GetDestinationsAsync(ce.ClusterId);
                dynCluster.Destinations = EntityMapper.ToDestinations(destEntities);
                config.Clusters.Add(dynCluster);
            }

            return config;
        }
        catch
        {
            return new GatewayDynamicConfig();
        }
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
