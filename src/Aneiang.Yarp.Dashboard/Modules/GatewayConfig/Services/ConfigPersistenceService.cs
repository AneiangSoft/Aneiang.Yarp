using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    private readonly DynamicYarpConfigService? _dynamicConfig;
    private readonly IConfigHistoryRepository _historyRepo;
    private readonly IRouteRepository _routeRepo;
    private readonly IClusterRepository _clusterRepo;
    private readonly IOptionsMonitor<ConfigHistoryOptions> _options;
    private readonly ILogger<ConfigPersistenceService> _logger;
    private readonly List<ConfigSnapshot> _history = new();
    private readonly object _historyLock = new();
    private bool _historyLoaded;

    public ConfigPersistenceService(
        ILogger<ConfigPersistenceService> logger,
        IConfigHistoryRepository historyRepo,
        IRouteRepository routeRepo,
        IClusterRepository clusterRepo,
        IOptionsMonitor<ConfigHistoryOptions> options,
        DynamicYarpConfigService? dynamicConfig = null)
    {
        _historyRepo = historyRepo;
        _routeRepo = routeRepo;
        _clusterRepo = clusterRepo;
        _options = options;
        _logger = logger;
        _dynamicConfig = dynamicConfig;
    }

    private int GetMaxSnapshots() => Math.Max(1, _options.CurrentValue.MaxSnapshots);

    /// <summary>Load persisted snapshot history from repository on first access.</summary>
    private async Task EnsureHistoryLoadedAsync()
    {
        if (_historyLoaded) return;
        _historyLoaded = true;

        try
        {
            var entities = await _historyRepo.GetConfigHistoryListAsync(GetMaxSnapshots());
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

        _logger.LogInformation(
            "ExportFullConfigAsync: dynamicConfig={HasSvc}, routes={RouteCount}, clusters={ClusterCount}",
            _dynamicConfig != null, yarpRoutes.Count, yarpClusters.Count);

        var routesDict = new Dictionary<string, JsonElement>();
        foreach (var r in yarpRoutes)
        {
            var json = Aneiang.Yarp.Serialization.YarpJsonConfig.SerializeRoute(r);
            using var doc = JsonDocument.Parse(json);
            routesDict[r.RouteId ?? string.Empty] = doc.RootElement.Clone();
        }

        var clustersDict = new Dictionary<string, JsonElement>();
        foreach (var c in yarpClusters)
        {
            var json = Aneiang.Yarp.Serialization.YarpJsonConfig.SerializeCluster(c);
            using var doc = JsonDocument.Parse(json);
            clustersDict[c.ClusterId ?? string.Empty] = doc.RootElement.Clone();
        }

        var fullConfig = new Dictionary<string, object>
        {
            ["ReverseProxy"] = new Dictionary<string, object>
            {
                ["Routes"] = routesDict,
                ["Clusters"] = clustersDict
            }
        };

        var fullJson = JsonSerializer.Serialize(fullConfig, Aneiang.Yarp.Serialization.YarpJsonConfig.IndentedOptions);
        using var fullDoc = JsonDocument.Parse(fullJson);
        return fullDoc.RootElement.Clone();
    }

    public async Task<bool> ImportFullConfigAsync(JsonElement config, string? clientIp = null)
    {
        try
        {
            await SaveSnapshotAsync("Before import", clientIp);

            bool hasReverseProxy = config.TryGetProperty("reverseProxy", out var rpCamel) ||
                                   config.TryGetProperty("ReverseProxy", out rpCamel);
            if (!hasReverseProxy) return false;
            var reverseProxy = rpCamel;

            if (_dynamicConfig == null) return false;

            bool hasClusters = reverseProxy.TryGetProperty("clusters", out var cCamel) ||
                               reverseProxy.TryGetProperty("Clusters", out cCamel);
            if (hasClusters)
            {
                foreach (var cluster in cCamel.EnumerateObject())
                {
                    var clusterConfig = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeCluster(cluster.Value);
                    if (clusterConfig == null) continue;
                    clusterConfig = clusterConfig with { ClusterId = cluster.Name };
                    if (clusterConfig.Destinations == null || clusterConfig.Destinations.Count == 0) continue;
                    await _dynamicConfig.TryAddClusterConfig(clusterConfig, "import", "dashboard-user");
                }
            }

            bool hasRoutes = reverseProxy.TryGetProperty("routes", out var rCamel) ||
                             reverseProxy.TryGetProperty("Routes", out rCamel);
            if (hasRoutes)
            {
                foreach (var route in rCamel.EnumerateObject())
                {
                    var routeConfig = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeRoute(route.Value);
                    if (routeConfig == null) continue;
                    routeConfig = routeConfig with { RouteId = route.Name };
                    if (string.IsNullOrWhiteSpace(routeConfig.ClusterId)) continue;
                    await _dynamicConfig.TryAddRouteConfig(routeConfig, "import", "dashboard-user");
                }
            }

            _logger.LogInformation("Configuration imported successfully");
            await SaveSnapshotAsync("After import", clientIp);
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

        var maxSnapshots = GetMaxSnapshots();
        lock (_historyLock)
        {
            _history.Add(snapshot);
            while (_history.Count > maxSnapshots)
                _history.RemoveAt(0);
        }

        var entity = snapshot.ToEntity("dashboard-user");
        await _historyRepo.SaveConfigHistoryAsync(entity);
        await _historyRepo.DeleteOldConfigHistoryAsync(maxSnapshots);

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
                        var routeConfig = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeRoute(route.Value);
                        if (routeConfig == null) continue;
                        yarpRoutes.Add(routeConfig with { RouteId = route.Name });
                    }
                }

                if (reverseProxy.TryGetProperty("clusters", out var clC) || reverseProxy.TryGetProperty("Clusters", out clC))
                {
                    foreach (var cluster in clC.EnumerateObject())
                    {
                        var clusterConfig = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeCluster(cluster.Value);
                        if (clusterConfig == null) continue;
                        yarpClusters.Add(clusterConfig with { ClusterId = cluster.Name });
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

        if (config.TryGetProperty("ReverseProxy", out var reverseProxy) ||
            config.TryGetProperty("reverseProxy", out reverseProxy))
        {
            if (!(reverseProxy.TryGetProperty("Routes", out _) || reverseProxy.TryGetProperty("routes", out _)))
                errors.Add("Missing 'ReverseProxy.Routes'");
            if (!(reverseProxy.TryGetProperty("Clusters", out _) || reverseProxy.TryGetProperty("clusters", out _)))
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
