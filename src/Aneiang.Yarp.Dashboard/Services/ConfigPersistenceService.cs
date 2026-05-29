using System.Text.Json;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services.Implements;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Service for managing configuration snapshots, import/export, and version history.
/// </summary>
public class ConfigPersistenceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IDynamicConfigPersistenceService _filePersistence;
    private readonly DynamicYarpConfigService? _dynamicConfig;
    private readonly ILogger<ConfigPersistenceService> _logger;
    private readonly List<ConfigSnapshot> _history = new();
    private readonly int _maxHistorySize;
    private readonly object _historyLock = new();

    /// <summary>
    /// Initializes a new instance of ConfigPersistenceService.
    /// </summary>
    public ConfigPersistenceService(
        IDynamicConfigPersistenceService filePersistence,
        ILogger<ConfigPersistenceService> logger,
        DynamicYarpConfigService? dynamicConfig = null,
        int maxHistorySize = 50)
    {
        _filePersistence = filePersistence;
        _logger = logger;
        _dynamicConfig = dynamicConfig;
        _maxHistorySize = maxHistorySize;
    }

    /// <summary>
    /// Export full configuration in standard YARP format.
    /// </summary>
    public async Task<JsonElement> ExportFullConfigAsync()
    {
        // Get complete config from YARP in-memory (includes both static and dynamic)
        var yarpRoutes = _dynamicConfig?.GetRoutes() ?? Array.Empty<RouteConfig>();
        var yarpClusters = _dynamicConfig?.GetClusters() ?? Array.Empty<ClusterConfig>();
        
        // Build routes dictionary
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
        
        // Build clusters dictionary with proper handling of null destinations
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
            // Save current snapshot before import
            await SaveSnapshotAsync("Before import", clientIp);

            var dynamicConfig = _filePersistence.LoadConfig();

            // Parse and import routes - support both camelCase (export format) and PascalCase (YARP standard)
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
                // Parse routes - try both "routes" and "Routes"
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
                    // Merge: remove existing routes with same ID, then add imported ones
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

                        // Remove existing route with same ID if present (merge/update)
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

                // Parse clusters - try both "clusters" and "Clusters"
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
                    // Merge: remove existing clusters with same ID, then add imported ones
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

                        // Remove existing cluster with same ID if present (merge/update)
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

            // Save merged config to file
            await _filePersistence.SaveConfigAsync(dynamicConfig);

            // Apply imported routes/clusters to YARP in-memory runtime (merge, not replace)
            if (_dynamicConfig != null)
            {
                // Import clusters first (routes depend on clusters)
                foreach (var c in dynamicConfig.Clusters)
                {
                    if (c.Destinations != null && c.Destinations.Count > 0)
                    {
                        await _dynamicConfig.TryAddCluster(c.ClusterId, c.Destinations, c.LoadBalancingPolicy, null, "import", "dashboard-user");
                    }
                }

                // Import routes
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
            
            // Keep only recent snapshots
            while (_history.Count > _maxHistorySize)
            {
                _history.RemoveAt(0);
            }
        }

        _logger.LogInformation(
            "Configuration snapshot saved: {VersionId}, Description: {Description}",
            snapshot.VersionId,
            snapshot.Description);

        return snapshot;
    }

    /// <summary>
    /// Get configuration history.
    /// </summary>
    public IReadOnlyList<ConfigSnapshot> GetHistory()
    {
        lock (_historyLock)
        {
            return _history.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Rollback to a specific version.
    /// </summary>
    public async Task<bool> RollbackAsync(string versionId, string? clientIp = null)
    {
        ConfigSnapshot? snapshot;
        
        lock (_historyLock)
        {
            snapshot = _history.FirstOrDefault(s => s.VersionId == versionId);
        }

        if (snapshot == null)
        {
            _logger.LogWarning("Snapshot not found: {VersionId}", versionId);
            return false;
        }

        try
        {
            // Save current state before rollback
            await SaveSnapshotAsync("Before rollback to " + versionId, clientIp);

            // Parse snapshot config - it's in YARP standard format with ReverseProxy wrapper
            // Note: Snapshot is saved with CamelCase naming policy, so properties are: reverseProxy, routes, clusters, etc.
            // But we also support PascalCase for imported configs
            var config = snapshot.Config;
            
            // Debug: log snapshot content
            _logger.LogInformation("Rollback snapshot JSON: {Json}", JsonSerializer.Serialize(config, _jsonOptions));
            
            // Try both camelCase (snapshot format) and PascalCase (YARP standard)
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
            
            // Build YARP RouteConfig and ClusterConfig lists for batch update
            var yarpRoutes = new List<RouteConfig>();
            var yarpClusters = new List<ClusterConfig>();
            
            if (hasReverseProxy)
            {
                // Parse routes - try both "routeses" and "Routes"
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
                        // Try both camelCase and PascalCase for properties
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
                
                // Parse clusters - try both "clusters" and "Clusters"
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
                        // Try both camelCase and PascalCase for loadBalancingPolicy
                        var loadBalancingPolicy = cluster.Value.TryGetProperty("loadBalancingPolicy", out var lbCamel) ? lbCamel.GetString()
                            : cluster.Value.TryGetProperty("LoadBalancingPolicy", out var lbPascal) ? lbPascal.GetString()
                            : null;
                        
                        // Parse destinations - try both "destinations" and "Destinations"
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
                                // Destination value can be string or object with "address"/"Address"
                                var address = string.Empty;
                                if (dest.Value.ValueKind == JsonValueKind.String)
                                {
                                    address = dest.Value.GetString() ?? string.Empty;
                                }
                                else if (dest.Value.ValueKind == JsonValueKind.Object)
                                {
                                    // Try both camelCase and PascalCase
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
            
            // Debug: log parsed data
            _logger.LogInformation("Parsed {Routes} routes and {Clusters} clusters from snapshot", yarpRoutes.Count, yarpClusters.Count);
            foreach (var c in yarpClusters)
            {
                _logger.LogInformation("Cluster '{Id}': {Dests} destinations", c.ClusterId, c.Destinations?.Count ?? 0);
                if (c.Destinations != null)
                {
                    foreach (var d in c.Destinations)
                    {
                        _logger.LogInformation("  Destination '{Name}': Address='{Address}'", d.Key, d.Value.Address);
                    }
                }
            }
            
            // Use batch update to avoid multiple file saves
            if (_dynamicConfig != null && (yarpRoutes.Count > 0 || yarpClusters.Count > 0))
            {
                _logger.LogInformation("Rollback: replacing config with {Routes} routes and {Clusters} clusters", yarpRoutes.Count, yarpClusters.Count);
                await _dynamicConfig.ReplaceAllConfig(yarpRoutes, yarpClusters, "rollback", "dashboard-user");
            }
            else if (_dynamicConfig != null)
            {
                // No config to restore, clear everything
                await _dynamicConfig.ReplaceAllConfig(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>(), "rollback", "dashboard-user");
            }
            else
            {
                // No DynamicYarpConfigService available, just save to file
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
