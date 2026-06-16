using System.Text.Json;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers;

/// <summary>
/// Configuration management API for import/export/save/rollback operations.
/// </summary>
[Route("api/config")]
[ApiController]
public class ConfigManagementController : ControllerBase
{
    private readonly IConfigPersistenceService _persistenceService;
    private readonly IDynamicYarpConfigService _dynamicConfig;
    private readonly ILogger<ConfigManagementController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly IWafSettingsPersistenceService? _wafPersistence;

    public ConfigManagementController(
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        ILogger<ConfigManagementController> logger,
        IMemoryCache memoryCache,
        IWafSettingsPersistenceService? wafPersistence = null)
    {
        _persistenceService = persistenceService;
        _dynamicConfig = dynamicConfig;
        _logger = logger;
        _memoryCache = memoryCache;
        _wafPersistence = wafPersistence;
    }

    /// <summary>
    /// Invalidates the dashboard query caches so the UI immediately reflects
    /// the latest configuration after any mutation (save/delete/rename/rollback/import).
    /// </summary>
    private void InvalidateQueryCaches()
    {
        _memoryCache.Remove("dashboard:routes:query");
        _memoryCache.Remove("dashboard:clusters:query");
    }

    /// <summary>
    /// Gets the client IP address from the request, considering proxy headers.
    /// </summary>
    private string? GetClientIp()
    {
        var ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(ip))
        {
            // X-Forwarded-For may contain multiple IPs, take the first one
            return ip.Split(',', StringSplitOptions.TrimEntries)[0];
        }

        ip = HttpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(ip)) return ip;

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    /// <summary>
    /// Export full configuration in standard YARP format.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportConfig()
    {
        try
        {
            var config = await _persistenceService.ExportFullConfigAsync();
            
            return Ok(new
            {
                code = 200,
                data = config,
                format = "yarp-standard",
                exportedAt = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export configuration");
            return StatusCode(500, new { code = 500, message = $"Export failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Import full configuration from standard YARP format.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportConfig([FromBody] JsonElement config)
    {
        try
        {
            // Validate format
            var validationResult = _persistenceService.ValidateConfig(config);
            if (!validationResult.Valid)
            {
                return BadRequest(new 
                { 
                    code = 400, 
                    message = "Invalid configuration format",
                    errors = validationResult.Errors 
                });
            }

            var success = await _persistenceService.ImportFullConfigAsync(config, GetClientIp());
            
            if (success) InvalidateQueryCaches();
            return success
                ? Ok(new { code = 200, message = "Configuration imported successfully" })
                : BadRequest(new { code = 400, message = "Failed to import configuration" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import configuration");
            return BadRequest(new { code = 400, message = $"Import failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Save or update a single cluster.
    /// </summary>
    [HttpPut("clusters/{clusterId}")]
    public async Task<IActionResult> SaveCluster(string clusterId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Save cluster requested: {ClusterId}", clusterId);

            // Parse destinations from config
            Dictionary<string, string> destinations = new();
            string? loadBalancingPolicy = null;
            HealthCheckConfig? healthCheck = null;

            if (config.TryGetProperty("destinations", out var destsProp))
            {
                // Handle both object format {"d1": "http://..."} and array format [{"name":...,"address":...}]
                // Also handle YARP standard format with PascalCase "Address"
                if (destsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dest in destsProp.EnumerateObject())
                    {
                        var address = dest.Value.ValueKind == JsonValueKind.String
                            ? dest.Value.GetString() ?? string.Empty
                            : dest.Value.TryGetProperty("address", out var addrProp)
                                ? addrProp.GetString() ?? string.Empty
                                : dest.Value.TryGetProperty("Address", out var addrPascalProp)  // YARP standard format
                                    ? addrPascalProp.GetString() ?? string.Empty
                                    : string.Empty;
                        if (!string.IsNullOrEmpty(address))
                        {
                            destinations[dest.Name] = address;
                        }
                    }
                }
                else if (destsProp.ValueKind == JsonValueKind.Array)
                {
                    var idx = 0;
                    foreach (var dest in destsProp.EnumerateArray())
                    {
                        var name = dest.TryGetProperty("name", out var nameProp)
                            ? nameProp.GetString() ?? $"d{idx}"
                            : $"d{idx}";
                        var address = dest.TryGetProperty("address", out var addrProp)
                            ? addrProp.GetString() ?? string.Empty
                            : string.Empty;
                        destinations[name] = address;
                        idx++;
                    }
                }
            }

            if (destinations.Count == 0)
            {
                return BadRequest(new { code = 400, message = "At least one destination is required" });
            }

            // Parse load balancing policy
            if (config.TryGetProperty("loadBalancingPolicy", out var lbProp))
            {
                loadBalancingPolicy = lbProp.GetString();
            }

            // Parse health check config
            if (config.TryGetProperty("healthCheck", out var hcProp))
            {
                healthCheck = new HealthCheckConfig();
                if (hcProp.TryGetProperty("active", out var activeProp))
                {
                    healthCheck.Active = activeProp.GetBoolean();
                }
                if (hcProp.TryGetProperty("endpoint", out var endpointProp))
                {
                    healthCheck.Endpoint = endpointProp.GetString();
                }
                if (hcProp.TryGetProperty("interval", out var intervalProp))
                {
                    healthCheck.Interval = TimeSpan.Parse(intervalProp.GetString() ?? "15s");
                }
                if (hcProp.TryGetProperty("timeout", out var timeoutProp))
                {
                    healthCheck.Timeout = TimeSpan.Parse(timeoutProp.GetString() ?? "10s");
                }
            }

            // Save snapshot BEFORE modification (so rollback restores previous state)
            await _persistenceService.SaveSnapshotAsync($"Before cluster '{clusterId}' saved via dashboard", GetClientIp());
            
            var result = await _dynamicConfig.TryAddCluster(clusterId, destinations, loadBalancingPolicy, healthCheck, "dashboard", "dashboard-user");

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, clusterId = clusterId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cluster: {ClusterId}", clusterId);
            return StatusCode(500, new { code = 500, message = $"Save failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Delete a cluster.
    /// </summary>
    [HttpDelete("clusters/{clusterId}")]
    public async Task<IActionResult> DeleteCluster(string clusterId)
    {
        try
        {
            _logger.LogInformation("Delete cluster requested: {ClusterId}", clusterId);

            // Save snapshot BEFORE deletion
            await _persistenceService.SaveSnapshotAsync($"Before cluster '{clusterId}' deleted via dashboard", GetClientIp());
            
            var result = await _dynamicConfig.TryRemoveCluster(clusterId);
            
            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, clusterId = clusterId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete cluster: {ClusterId}", clusterId);
            return StatusCode(500, new { code = 500, message = $"Delete failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Rename a cluster. Updates all referencing routes atomically.
    /// </summary>
    [HttpPut("clusters/{clusterId}/rename")]
    public async Task<IActionResult> RenameCluster(string clusterId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Rename cluster requested: {OldClusterId}", clusterId);

            // Get new cluster ID
            string? newClusterId = null;
            if (config.TryGetProperty("newClusterId", out var newIdProp))
                newClusterId = newIdProp.GetString();
            else if (config.TryGetProperty("clusterId", out var newIdProp2))
                newClusterId = newIdProp2.GetString();

            if (string.IsNullOrWhiteSpace(newClusterId))
                return BadRequest(new { code = 400, message = "newClusterId is required" });

            // Parse destinations
            Dictionary<string, string> destinations = new();
            if (config.TryGetProperty("destinations", out var destsProp) || config.TryGetProperty("Destinations", out destsProp))
            {
                if (destsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dest in destsProp.EnumerateObject())
                    {
                        var address = dest.Value.ValueKind == JsonValueKind.String
                            ? dest.Value.GetString() ?? string.Empty
                            : dest.Value.TryGetProperty("address", out var addrProp)
                                ? addrProp.GetString() ?? string.Empty
                                : dest.Value.TryGetProperty("Address", out var addrPascalProp)
                                    ? addrPascalProp.GetString() ?? string.Empty
                                    : string.Empty;
                        if (!string.IsNullOrEmpty(address))
                        {
                            destinations[dest.Name] = address;
                        }
                    }
                }
            }

            if (destinations.Count == 0)
                return BadRequest(new { code = 400, message = "destinations is required and must have at least one entry" });

            // Parse optional fields
            string? loadBalancingPolicy = null;
            if (config.TryGetProperty("loadBalancingPolicy", out var lbpProp))
                loadBalancingPolicy = lbpProp.GetString();

            // Save snapshot BEFORE rename
            await _persistenceService.SaveSnapshotAsync($"Before cluster '{clusterId}' renamed to '{newClusterId}' via dashboard", GetClientIp());

            var result = await _dynamicConfig.TryRenameCluster(
                clusterId, newClusterId, destinations, loadBalancingPolicy,
                source: "dashboard", createdBy: "dashboard-user");

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, oldClusterId = clusterId, newClusterId = newClusterId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename cluster: {ClusterId}", clusterId);
            return StatusCode(500, new { code = 500, message = $"Rename failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Rename a route atomically (replace route ID, keep cluster reference and all settings).
    /// </summary>
    [HttpPut("routes/{routeId}/rename")]
    public async Task<IActionResult> RenameRoute(string routeId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Rename route requested: {OldRouteId}", routeId);

            // Get new route ID
            string? newRouteId = null;
            if (config.TryGetProperty("newRouteId", out var newIdProp))
                newRouteId = newIdProp.GetString();
            else if (config.TryGetProperty("routeId", out var ridProp))
                newRouteId = ridProp.GetString();

            if (string.IsNullOrWhiteSpace(newRouteId))
                return BadRequest(new { code = 400, message = "newRouteId is required" });

            if (string.Equals(routeId, newRouteId, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { code = 400, message = "New route ID is the same as the old one" });

            // Parse optional fields from config (same format as SaveRoute)
            string? matchPath = null;
            string? clusterId = null;
            int? order = null;
            List<Dictionary<string, string>>? transforms = null;

            if (config.TryGetProperty("matchPath", out var mp) || config.TryGetProperty("MatchPath", out mp))
                matchPath = mp.GetString();

            if (config.TryGetProperty("clusterId", out var ci) || config.TryGetProperty("ClusterId", out ci))
                clusterId = ci.GetString();

            if (config.TryGetProperty("order", out var o) || config.TryGetProperty("Order", out o))
                order = o.GetInt32();

            if (config.TryGetProperty("transforms", out var t) || config.TryGetProperty("Transforms", out t))
                transforms = t.Deserialize<List<Dictionary<string, string>>>();

            var request = new RegisterRouteRequest
            {
                RouteName = newRouteId,
                ClusterName = clusterId ?? string.Empty,
                MatchPath = matchPath ?? string.Empty,
                Order = order,
                Transforms = transforms
            };

            await _persistenceService.SaveSnapshotAsync(
                $"Before route '{routeId}' renamed to '{newRouteId}' via dashboard", GetClientIp());

            var result = await _dynamicConfig.TryRenameRoute(routeId, newRouteId, request,
                source: "dashboard", createdBy: "dashboard-user");

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = newRouteId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = $"Rename failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Save or update a single route.
    /// </summary>
    [HttpPut("routes/{routeId}")]
    public async Task<IActionResult> SaveRoute(string routeId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Save route requested: {RouteId}", routeId);

            // Parse required fields - support both camelCase and PascalCase (YARP standard)
            string clusterId = string.Empty;
            if (config.TryGetProperty("clusterId", out var clusterIdProp))
            {
                clusterId = clusterIdProp.GetString() ?? string.Empty;
            }
            else if (config.TryGetProperty("ClusterId", out var clusterIdPascalProp))  // YARP standard format
            {
                clusterId = clusterIdPascalProp.GetString() ?? string.Empty;
            }
            else
            {
                return BadRequest(new { code = 400, message = "clusterId/ClusterId is required" });
            }

            // Get matchPath - support multiple formats
            string matchPath = string.Empty;
            // Format 1: direct matchPath (camelCase)
            if (config.TryGetProperty("matchPath", out var matchPathProp) && matchPathProp.ValueKind != JsonValueKind.Undefined)
            {
                matchPath = matchPathProp.GetString() ?? string.Empty;
            }
            // Format 2: nested match.path (YARP standard)
            else if (config.TryGetProperty("Match", out var matchPascalProp) && matchPascalProp.TryGetProperty("Path", out var pathPascalProp))
            {
                matchPath = pathPascalProp.GetString() ?? string.Empty;
            }
            // Format 3: nested match.path (camelCase)
            else if (config.TryGetProperty("match", out var matchProp) && matchProp.TryGetProperty("path", out var pathProp))
            {
                matchPath = pathProp.GetString() ?? string.Empty;
            }
            else
            {
                return BadRequest(new { code = 400, message = "Match.Path is required" });
            }

            // Check if cluster exists
            var existingCluster = _dynamicConfig.GetCluster(clusterId);
            string destinationAddress = string.Empty;
            
            if (existingCluster == null)
            {
                // Cluster doesn't exist - need to provide a default destination address
                // Try to get from config if provided
                if (config.TryGetProperty("destinations", out var destsProp) || config.TryGetProperty("Destinations", out destsProp))
                {
                    if (destsProp.ValueKind == JsonValueKind.Object)
                    {
                        var firstDest = destsProp.EnumerateObject().FirstOrDefault();
                        if (firstDest.Value.ValueKind == JsonValueKind.String)
                        {
                            destinationAddress = firstDest.Value.GetString() ?? string.Empty;
                        }
                        else if (firstDest.Value.TryGetProperty("address", out var addrProp))
                        {
                            destinationAddress = addrProp.GetString() ?? string.Empty;
                        }
                        else if (firstDest.Value.TryGetProperty("Address", out var addrPascalProp))
                        {
                            destinationAddress = addrPascalProp.GetString() ?? string.Empty;
                        }
                    }
                }
                
                if (string.IsNullOrEmpty(destinationAddress))
                {
                    return BadRequest(new { code = 400, message = $"Cluster '{clusterId}' doesn't exist. 'destinations' is required to create a new cluster." });
                }
            }
            // else: Cluster exists - destinationAddress will be ignored, no need to validate

            // Get order - support both camelCase and PascalCase
            int? order = null;
            if (config.TryGetProperty("order", out var orderProp) || config.TryGetProperty("Order", out orderProp))
            {
                order = orderProp.GetInt32();
            }

            // Get transforms - support both camelCase and PascalCase
            List<Dictionary<string, string>>? transforms = null;
            JsonElement transformsProp;
            if (config.TryGetProperty("transforms", out transformsProp) || config.TryGetProperty("Transforms", out transformsProp))
            {
                transforms = transformsProp.Deserialize<List<Dictionary<string, string>>>();
            }

            // Create request
            // Note: DestinationAddress is only used when creating a new cluster
            var request = new RegisterRouteRequest
            {
                RouteName = routeId,
                ClusterName = clusterId,
                MatchPath = matchPath,
                DestinationAddress = destinationAddress,
                Order = order,
                Transforms = transforms
            };

            // Save snapshot BEFORE modification
            await _persistenceService.SaveSnapshotAsync($"Before route '{routeId}' saved via dashboard", GetClientIp());
            
            var result = await _dynamicConfig.TryAddRoute(request, "dashboard", "dashboard-user");

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = routeId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = $"Save failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Delete a route.
    /// </summary>
    [HttpDelete("routes/{routeId}")]
    public async Task<IActionResult> DeleteRoute(string routeId, [FromQuery] bool removeOrphanedCluster = false)
    {
        try
        {
            _logger.LogInformation("Delete route requested: {RouteId}", routeId);

            // Save snapshot BEFORE deletion
            await _persistenceService.SaveSnapshotAsync($"Before route '{routeId}' deleted via dashboard", GetClientIp());
            
            var result = await _dynamicConfig.TryRemoveRoute(routeId, null, removeOrphanedCluster);
            
            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = routeId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = $"Delete failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Get configuration history snapshots.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetConfigHistory()
    {
        try
        {
            var history = await _persistenceService.GetHistoryAsync();
            
            var result = history.Select(s => new
            {
                s.VersionId,
                s.Timestamp,
                s.Description,
                s.ClientIp
            }).ToList();

            return Ok(new 
            { 
                code = 200, 
                data = result,
                count = result.Count 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration history");
            return StatusCode(500, new { code = 500, message = $"Failed to get history: {ex.Message}" });
        }
    }

    /// <summary>
    /// Rollback configuration to a specific version.
    /// </summary>
    [HttpPost("rollback/{versionId}")]
    public async Task<IActionResult> RollbackConfig(string versionId)
    {
        try
        {
            var success = await _persistenceService.RollbackAsync(versionId, GetClientIp());
            
            if (success) InvalidateQueryCaches();
            return success
                ? Ok(new { code = 200, message = $"Rolled back to version: {versionId}" })
                : BadRequest(new { code = 400, message = $"Version not found: {versionId}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback to version: {VersionId}", versionId);
            return StatusCode(500, new { code = 500, message = $"Rollback failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Create a manual configuration snapshot.
    /// </summary>
    [HttpPost("snapshot")]
    public async Task<IActionResult> CreateSnapshot([FromBody] SnapshotRequest? request)
    {
        try
        {
            var description = request?.Description ?? "Manual snapshot";
            var snapshot = await _persistenceService.SaveSnapshotAsync(description, GetClientIp());
            return Ok(new { code = 200, data = new { snapshot.VersionId, snapshot.Description, snapshot.Timestamp } });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot");
            return StatusCode(500, new { code = 500, message = $"Snapshot failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Diff a historical version against the current live configuration.
    /// Returns structured diff data for the diff panel.
    /// </summary>
    [HttpGet("diff/{versionId}")]
    public async Task<IActionResult> ConfigDiff(string versionId)
    {
        try
        {
            var history = await _persistenceService.GetHistoryAsync();
            var historySnapshot = history
                .FirstOrDefault(s => string.Equals(s.VersionId, versionId, StringComparison.OrdinalIgnoreCase));

            if (historySnapshot == null)
                return NotFound(new { code = 404, message = $"Version '{versionId}' not found" });

            // Parse snapshot routes and clusters from the stored JSON
            var snapshotRoutes = ParseSnapshotRoutes(historySnapshot.Config);
            var snapshotClusters = ParseSnapshotClusters(historySnapshot.Config);

            // Get current live config
            var currentRoutes = _dynamicConfig.GetRoutes();
            var currentClusters = _dynamicConfig.GetClusters();

            // Build diff
            var routeDiffs = BuildRouteDiffs(snapshotRoutes, currentRoutes);
            var clusterDiffs = BuildClusterDiffs(snapshotClusters, currentClusters);

            var summary = new
            {
                versionId,
                description = historySnapshot.Description,
                timestamp = historySnapshot.Timestamp,
                routesChanged = routeDiffs.Count,
                clustersChanged = clusterDiffs.Count,
                totalChanges = routeDiffs.Count + clusterDiffs.Count
            };

            return Ok(new
            {
                code = 200,
                data = new
                {
                    summary,
                    routes = routeDiffs,
                    clusters = clusterDiffs
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to diff config for version: {VersionId}", versionId);
            return StatusCode(500, new { code = 500, message = $"Diff failed: {ex.Message}" });
        }
    }


    /// <summary>
    /// Validate configuration format.
    /// </summary>
    [HttpPost("validate")]
    public IActionResult ValidateConfig([FromBody] JsonElement config)
    {
        try
        {
            var result = _persistenceService.ValidateConfig(config);
            
            return Ok(new 
            { 
                code = 200, 
                valid = result.Valid,
                errors = result.Errors 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate configuration");
            return StatusCode(500, new { code = 500, message = $"Validation failed: {ex.Message}" });
        }
    }

    // ── Diff Helpers ──

    private static List<SnapshotRoute> ParseSnapshotRoutes(JsonElement config)
    {
        var routes = new List<SnapshotRoute>();

        // Try to find routes in the YARP-standard nested structure
        JsonElement routesArray = default;
        if (config.TryGetProperty("reverseProxy", out var rp) || config.TryGetProperty("ReverseProxy", out rp))
        {
            if (rp.TryGetProperty("routes", out var r) || rp.TryGetProperty("Routes", out r))
            {
                routesArray = r;
            }
        }

        if (routesArray.ValueKind != JsonValueKind.Array) return routes;

        foreach (var route in routesArray.EnumerateArray())
        {
            var routeId = route.TryGetProperty("routeId", out var rid) ? rid.GetString() ?? string.Empty :
                route.TryGetProperty("RouteId", out var ridp) ? ridp.GetString() ?? string.Empty : string.Empty;

            var clusterId = route.TryGetProperty("clusterId", out var cid) ? cid.GetString() ?? string.Empty :
                route.TryGetProperty("ClusterId", out var cidp) ? cidp.GetString() ?? string.Empty : string.Empty;

            string matchPath = string.Empty;
            if (route.TryGetProperty("match", out var match) || route.TryGetProperty("Match", out match))
            {
                if (match.TryGetProperty("path", out var path) || match.TryGetProperty("Path", out path))
                {
                    matchPath = path.GetString() ?? string.Empty;
                }
            }

            var order = route.TryGetProperty("order", out var ord) ? ord.GetInt32() :
                route.TryGetProperty("Order", out var ordp) ? ordp.GetInt32() : 0;

            routes.Add(new SnapshotRoute
            {
                RouteId = routeId,
                ClusterId = clusterId,
                MatchPath = matchPath,
                Order = order
            });
        }

        return routes;
    }

    private static List<SnapshotCluster> ParseSnapshotClusters(JsonElement config)
    {
        var clusters = new List<SnapshotCluster>();

        JsonElement clustersObj = default;
        if (config.TryGetProperty("reverseProxy", out var rp) || config.TryGetProperty("ReverseProxy", out rp))
        {
            if (rp.TryGetProperty("clusters", out var c) || rp.TryGetProperty("Clusters", out c))
            {
                clustersObj = c;
            }
        }

        if (clustersObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var kvp in clustersObj.EnumerateObject())
            {
                var clusterId = kvp.Name;
                var cluster = kvp.Value;

                var lbPolicy = (cluster.TryGetProperty("loadBalancingPolicy", out var lbp)
                    ? lbp.GetString() : null)
                    ?? (cluster.TryGetProperty("LoadBalancingPolicy", out var lbpp)
                    ? lbpp.GetString() : null);

                var dests = new Dictionary<string, string>();
                if (cluster.TryGetProperty("destinations", out var d) || cluster.TryGetProperty("Destinations", out d))
                {
                    if (d.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var dest in d.EnumerateObject())
                        {
                            var addr = dest.Value.ValueKind == JsonValueKind.String
                                ? dest.Value.GetString() ?? string.Empty
                                : dest.Value.TryGetProperty("address", out var a) ? a.GetString() ?? string.Empty :
                                  dest.Value.TryGetProperty("Address", out var ap) ? ap.GetString() ?? string.Empty : string.Empty;
                            dests[dest.Name] = addr;
                        }
                    }
                }

                clusters.Add(new SnapshotCluster
                {
                    ClusterId = clusterId,
                    LoadBalancingPolicy = lbPolicy,
                    Destinations = dests
                });
            }
        }

        return clusters;
    }

    private static List<object> BuildRouteDiffs(
        List<SnapshotRoute> snapshotRoutes,
        IReadOnlyList<global::Yarp.ReverseProxy.Configuration.RouteConfig> currentRoutes)
    {
        var diffs = new List<object>();
        var currentSet = currentRoutes.ToDictionary(r => r.RouteId ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var sr in snapshotRoutes)
        {
            if (currentSet.TryGetValue(sr.RouteId, out var live))
            {
                // Check for changes
                if (sr.ClusterId != (live.ClusterId ?? string.Empty) ||
                    sr.MatchPath != (live.Match?.Path ?? string.Empty) ||
                    sr.Order != (live.Order ?? 0))
                {
                    diffs.Add(GetDiffItem("route",
                        $"{sr.RouteId} ({sr.MatchPath} → {sr.ClusterId})",
                        "modified",
                        $"ClusterId: {sr.ClusterId}, Path: {sr.MatchPath}, Order: {sr.Order}",
                        $"ClusterId: {live.ClusterId}, Path: {live.Match?.Path}, Order: {live.Order}"));
                }
            }
            else
            {
                // Route exists in snapshot but not in current → was removed
                diffs.Add(GetDiffItem("route",
                    $"{sr.RouteId} ({sr.MatchPath} → {sr.ClusterId})",
                    "removed", null, null));
            }
        }

        // Routes in current but not in snapshot → were added
        var snapshotSet = snapshotRoutes.Select(s => s.RouteId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var live in currentRoutes)
        {
            if (!snapshotSet.Contains(live.RouteId ?? string.Empty))
            {
                diffs.Add(GetDiffItem("route",
                    $"{live.RouteId} ({live.Match?.Path} → {live.ClusterId})",
                    "added", null, null));
            }
        }

        return diffs;
    }

    private static List<object> BuildClusterDiffs(
        List<SnapshotCluster> snapshotClusters,
        IReadOnlyList<global::Yarp.ReverseProxy.Configuration.ClusterConfig> currentClusters)
    {
        var diffs = new List<object>();
        var currentSet = currentClusters.ToDictionary(c => c.ClusterId ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        foreach (var sc in snapshotClusters)
        {
            if (currentSet.TryGetValue(sc.ClusterId, out var live))
            {
                // Build destination string for comparison
                var snapshotDest = string.Join(", ", sc.Destinations.Select(d => $"{d.Key}={d.Value}"));
                var currentDest = string.Join(", ", live.Destinations?.Select(d => $"{d.Key}={d.Value.Address}") ?? Enumerable.Empty<string>());

                if (snapshotDest != currentDest ||
                    sc.LoadBalancingPolicy != live.LoadBalancingPolicy)
                {
                    diffs.Add(GetDiffItem("cluster",
                        sc.ClusterId,
                        "modified",
                        $"Destinations: [{snapshotDest}], Policy: {sc.LoadBalancingPolicy ?? "none"}",
                        $"Destinations: [{currentDest}], Policy: {live.LoadBalancingPolicy ?? "none"}"));
                }
            }
            else
            {
                diffs.Add(GetDiffItem("cluster", sc.ClusterId, "removed", null, null));
            }
        }

        var snapshotSet = snapshotClusters.Select(s => s.ClusterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var live in currentClusters)
        {
            if (!snapshotSet.Contains(live.ClusterId ?? string.Empty))
            {
                var dest = string.Join(", ", live.Destinations?.Select(d => $"{d.Key}={d.Value.Address}") ?? Enumerable.Empty<string>());
                diffs.Add(GetDiffItem("cluster", $"{live.ClusterId} ({dest})", "added", null, null));
            }
        }

        return diffs;
    }

    private static object GetDiffItem(string entityType, string path, string diffType, string? oldValue, string? newValue)
    {
        return new
        {
            type = diffType,
            path = $"[{entityType}] {path}",
            oldValue,
            newValue
        };
    }

    /// <summary>Snapshot route entry extracted from snapshot config.</summary>
    private class SnapshotRoute
    {
        public string RouteId { get; set; } = string.Empty;
        public string ClusterId { get; set; } = string.Empty;
        public string MatchPath { get; set; } = string.Empty;
        public int Order { get; set; }
    }

    /// <summary>Snapshot cluster entry extracted from snapshot config.</summary>
    private class SnapshotCluster
    {
        public string ClusterId { get; set; } = string.Empty;
        public string? LoadBalancingPolicy { get; set; }
        public Dictionary<string, string> Destinations { get; set; } = new();
    }

    // ── WAF Settings Endpoints ──

    /// <summary>
    /// Get current WAF settings.
    /// </summary>
    [HttpGet("waf")]
    public IActionResult GetWafSettings()
    {
        try
        {
            var data = _wafPersistence?.Load() ?? new WafSettingsData
            {
                EnableIpCheck = true,
                EnableRequestSizeValidation = true,
                MaxRequestBodySize = 10 * 1024 * 1024,
                MaxHeaderCount = 64,
                MaxHeaderSize = 8192,
                EnableSqlInjectionDetection = true,
                EnableXssDetection = true,
                EnablePathTraversalDetection = true
            };

            return Ok(new
            {
                code = 200,
                data = data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get WAF settings");
            return StatusCode(500, new { code = 500, message = $"Failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Update WAF settings.
    /// </summary>
    [HttpPut("waf")]
    public async Task<IActionResult> UpdateWafSettings([FromBody] WafSettingsData request)
    {
        try
        {
            if (request == null)
                return BadRequest(new { code = 400, message = "Request body is required" });

            if (_wafPersistence == null)
                return StatusCode(500, new { code = 500, message = "WAF persistence service not available" });

            var success = await _wafPersistence.SaveAsync(request);
            if (!success)
                return StatusCode(500, new { code = 500, message = "Failed to save WAF settings" });

            _logger.LogInformation("WAF settings updated: Enabled={Enabled}, IPCheck={IpCheck}, SQLi={Sqli}, XSS={Xss}, PathTraversal={Pt}",
                request.Enabled, request.EnableIpCheck, request.EnableSqlInjectionDetection,
                request.EnableXssDetection, request.EnablePathTraversalDetection);

            return Ok(new { code = 200, message = "WAF settings saved successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update WAF settings");
            return StatusCode(500, new { code = 500, message = $"Failed: {ex.Message}" });
        }
    }
}
