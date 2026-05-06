using System.Text.Json;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Logging;

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

    private readonly DynamicConfigPersistenceService _filePersistence;
    private readonly ILogger<ConfigPersistenceService> _logger;
    private readonly List<ConfigSnapshot> _history = new();
    private readonly int _maxHistorySize;
    private readonly object _historyLock = new();

    /// <summary>
    /// Initializes a new instance of ConfigPersistenceService.
    /// </summary>
    public ConfigPersistenceService(
        DynamicConfigPersistenceService filePersistence,
        ILogger<ConfigPersistenceService> logger,
        int maxHistorySize = 50)
    {
        _filePersistence = filePersistence;
        _logger = logger;
        _maxHistorySize = maxHistorySize;
    }

    /// <summary>
    /// Export full configuration in standard YARP format.
    /// </summary>
    public async Task<JsonElement> ExportFullConfigAsync()
    {
        var dynamicConfig = _filePersistence.LoadConfig();
        
        var fullConfig = new Dictionary<string, object>
        {
            ["ReverseProxy"] = new Dictionary<string, object>
            {
                ["Routes"] = dynamicConfig.Routes.ToDictionary(
                    r => r.RouteId,
                    r => new
                    {
                        r.ClusterId,
                        Match = new { Path = r.MatchPath },
                        r.Transforms,
                        r.Order
                    }
                ),
                ["Clusters"] = dynamicConfig.Clusters.ToDictionary(
                    c => c.ClusterId,
                    c => new
                    {
                        Destinations = c.Destinations.ToDictionary(
                            d => d.Key,
                            d => new { Address = d.Value }
                        ),
                        c.LoadBalancingPolicy,
                        c.HealthCheck
                    }
                )
            }
        };

        var json = JsonSerializer.Serialize(fullConfig, _jsonOptions);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Import full configuration from standard YARP format.
    /// </summary>
    public async Task<bool> ImportFullConfigAsync(JsonElement config)
    {
        try
        {
            // Save current snapshot before import
            await SaveSnapshotAsync("Before import");

            var dynamicConfig = _filePersistence.LoadConfig();

            // Parse and import routes
            if (config.TryGetProperty("ReverseProxy", out var reverseProxy))
            {
                if (reverseProxy.TryGetProperty("Routes", out var routes))
                {
                    dynamicConfig.Routes.Clear();
                    foreach (var route in routes.EnumerateObject())
                    {
                        var routeObj = new DynamicRouteConfig
                        {
                            RouteId = route.Name,
                            ClusterId = route.Value.GetProperty("ClusterId").GetString() ?? string.Empty,
                            MatchPath = route.Value.TryGetProperty("Match", out var match) 
                                && match.TryGetProperty("Path", out var path)
                                ? path.GetString() ?? string.Empty
                                : string.Empty,
                            Transforms = route.Value.TryGetProperty("Transforms", out var transforms)
                                ? JsonSerializer.Deserialize<List<Dictionary<string, string>>>(transforms.GetRawText(), _jsonOptions)
                                : new List<Dictionary<string, string>>(),
                            Order = route.Value.TryGetProperty("Order", out var order) ? order.GetInt32() : 50
                        };
                        dynamicConfig.Routes.Add(routeObj);
                    }
                }

                if (reverseProxy.TryGetProperty("Clusters", out var clusters))
                {
                    dynamicConfig.Clusters.Clear();
                    foreach (var cluster in clusters.EnumerateObject())
                    {
                        var destinations = new Dictionary<string, string>();
                        if (cluster.Value.TryGetProperty("Destinations", out var dests))
                        {
                            foreach (var dest in dests.EnumerateObject())
                            {
                                var address = dest.Value.TryGetProperty("Address", out var addr)
                                    ? addr.GetString() ?? string.Empty
                                    : string.Empty;
                                destinations[dest.Name] = address;
                            }
                        }

                        var clusterObj = new DynamicClusterConfig
                        {
                            ClusterId = cluster.Name,
                            Destinations = destinations,
                            LoadBalancingPolicy = cluster.Value.TryGetProperty("LoadBalancingPolicy", out var lb)
                                ? lb.GetString()
                                : null
                        };
                        dynamicConfig.Clusters.Add(clusterObj);
                    }
                }
            }

            // Save imported config
            _filePersistence.SaveConfig(dynamicConfig);

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
    public async Task<ConfigSnapshot> SaveSnapshotAsync(string? description = null)
    {
        var config = await ExportFullConfigAsync();
        
        var snapshot = new ConfigSnapshot
        {
            Description = description ?? "Manual snapshot",
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
    public async Task<bool> RollbackAsync(string versionId)
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
            await SaveSnapshotAsync("Before rollback to " + versionId);

            var configJson = JsonSerializer.Serialize(snapshot.Config, _jsonOptions);
            var config = JsonSerializer.Deserialize<GatewayDynamicConfig>(configJson, _jsonOptions);
            
            if (config != null)
            {
                _filePersistence.SaveConfig(config);
                
                _logger.LogInformation("Rolled back to version: {VersionId}", versionId);
                return true;
            }

            return false;
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
