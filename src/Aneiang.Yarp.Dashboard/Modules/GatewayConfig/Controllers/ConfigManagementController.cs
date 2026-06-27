using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

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
    private readonly IGatewayIdentityService _identityService;
    private readonly IConfigSnapshotScheduler _snapshotScheduler;
    private readonly IOptionsMonitor<ConfigHistoryOptions> _configHistoryOptions;
    private readonly IWafSettingsPersistenceService? _wafPersistence;

    public ConfigManagementController(
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        ILogger<ConfigManagementController> logger,
        IMemoryCache memoryCache,
        IGatewayIdentityService identityService,
        IConfigSnapshotScheduler snapshotScheduler,
        IOptionsMonitor<ConfigHistoryOptions> configHistoryOptions,
        IWafSettingsPersistenceService? wafPersistence = null)
    {
        _persistenceService = persistenceService;
        _dynamicConfig = dynamicConfig;
        _logger = logger;
        _memoryCache = memoryCache;
        _identityService = identityService;
        _snapshotScheduler = snapshotScheduler;
        _configHistoryOptions = configHistoryOptions;
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

    private async Task SnapshotLowRiskMutationAsync(string description)
    {
        var options = _configHistoryOptions.CurrentValue;
        if (!options.AutoSnapshotBeforeMutation)
        {
            return;
        }

        var clientIp = GetClientIp();
        if (options.AsyncSnapshotForLowRiskMutation)
        {
            _snapshotScheduler.QueueSnapshot(description, clientIp);
            return;
        }

        await _persistenceService.SaveSnapshotAsync(description, clientIp);
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

    private static bool ContainsDifferentId(JsonElement config, string expectedId, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (config.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var actualId = value.GetString();
                if (!string.IsNullOrWhiteSpace(actualId) && !string.Equals(actualId, expectedId, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Export failed", ex) });
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
            return BadRequest(new { code = 400, message = SafeErrorMessages.Create(HttpContext, "Import failed", ex) });
        }
    }

    /// <summary>
    /// Save or update a single cluster from a full native YARP cluster config.
    /// Accepts both camelCase and PascalCase keys, plus JSON comments and trailing commas.
    /// </summary>
    [HttpPut("clusters/{clusterId}")]
    public async Task<IActionResult> SaveCluster(string clusterId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Save cluster requested: {ClusterId}", clusterId);

            ClusterConfig? cluster;
            try
            {
                cluster = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeCluster(config);
            }
            catch (Exception parseEx)
            {
                return BadRequest(new { code = 400, message = "Invalid cluster configuration: " + parseEx.Message });
            }

            if (cluster == null)
                return BadRequest(new { code = 400, message = "Cluster configuration is required" });

            cluster = cluster with { ClusterId = clusterId };

            if (cluster.Destinations == null || cluster.Destinations.Count == 0)
                return BadRequest(new { code = 400, message = "At least one destination is required" });

            _snapshotScheduler.QueueSnapshot($"After cluster '{clusterId}' saved via dashboard", GetClientIp());

            var result = await _dynamicConfig.TryAddClusterConfig(cluster, "dashboard", "dashboard-user");

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, clusterId = clusterId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cluster: {ClusterId}", clusterId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Save failed", ex) });
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Delete failed", ex) });
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

            var result = await _identityService.RenameClusterAsync(
                clusterId,
                newClusterId,
                destinations,
                loadBalancingPolicy,
                clientIp: GetClientIp(),
                operatorName: "dashboard-user",
                ct: HttpContext.RequestAborted);

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, oldClusterId = clusterId, newClusterId = newClusterId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename cluster: {ClusterId}", clusterId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Rename failed", ex) });
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

            var result = await _identityService.RenameRouteAsync(
                routeId,
                newRouteId,
                request,
                clientIp: GetClientIp(),
                operatorName: "dashboard-user",
                ct: HttpContext.RequestAborted);

            if (result.Success)
            {
                InvalidateQueryCaches();
            }
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = newRouteId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Rename failed", ex) });
        }
    }

    /// <summary>
    /// Save or update a single route from a full native YARP route config.
    /// Accepts both camelCase and PascalCase keys, plus JSON comments and trailing commas.
    /// All advanced properties (full Match criteria, Auth/Cors/RateLimiter/Timeout policies,
    /// transforms, metadata) are preserved.
    /// </summary>
    [HttpPut("routes/{routeId}")]
    public async Task<IActionResult> SaveRoute(string routeId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Save route requested: {RouteId}", routeId);

            RouteConfig? route;
            try
            {
                route = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeRoute(config);
            }
            catch (Exception parseEx)
            {
                return BadRequest(new { code = 400, message = "Invalid route configuration: " + parseEx.Message });
            }

            if (route == null)
                return BadRequest(new { code = 400, message = "Route configuration is required" });

            route = route with { RouteId = routeId };

            if (string.IsNullOrWhiteSpace(route.ClusterId))
                return BadRequest(new { code = 400, message = "ClusterId is required" });

            if (route.Match == null || (string.IsNullOrWhiteSpace(route.Match.Path) && (route.Match.Hosts == null || route.Match.Hosts.Count == 0)))
                return BadRequest(new { code = 400, message = "Match.Path or Match.Hosts is required" });

            await EnsureClusterForRouteAsync(route.ClusterId!, config);

            await SnapshotLowRiskMutationAsync($"After route '{routeId}' saved via dashboard");

            var result = await _dynamicConfig.TryAddRouteConfig(route, "dashboard", "dashboard-user");

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = routeId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Save failed", ex) });
        }
    }

    /// <summary>
    /// Ensures the cluster referenced by a route exists. If it does not and the payload carries
    /// a "destinations" object, the cluster is created from those destinations.
    /// </summary>
    private async Task EnsureClusterForRouteAsync(string clusterId, JsonElement config)
    {
        if (_dynamicConfig.GetCluster(clusterId) != null)
            return;

        if (!(config.TryGetProperty("destinations", out var destsProp) || config.TryGetProperty("Destinations", out destsProp)))
            return;
        if (destsProp.ValueKind != JsonValueKind.Object)
            return;

        var destinations = new Dictionary<string, string>();
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
                destinations[dest.Name] = address;
        }

        if (destinations.Count > 0)
            await _dynamicConfig.TryAddCluster(clusterId, destinations, null, null, "dashboard", "dashboard-user");
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Delete failed", ex) });
        }
    }

    /// <summary>
    /// Get configuration history snapshots.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetConfigHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] string? changeType = null)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var history = (await _persistenceService.GetHistoryAsync())
                .OrderByDescending(s => s.Timestamp)
                .ToList();

            var latestVersionId = history.FirstOrDefault()?.VersionId;
            var allItems = history.Select(s =>
            {
                var routeCount = ParseSnapshotRoutes(s.Config).Count;
                var clusterCount = ParseSnapshotClusters(s.Config).Count;
                var resolvedChangeType = ResolveHistoryChangeType(s.Description);
                return new
                {
                    s.VersionId,
                    s.Timestamp,
                    s.Description,
                    s.ClientIp,
                    RouteCount = routeCount,
                    ClusterCount = clusterCount,
                    TotalItems = routeCount + clusterCount,
                    ConfigSize = s.Config.ValueKind == JsonValueKind.Undefined ? 0 : s.Config.GetRawText().Length,
                    IsLatest = string.Equals(s.VersionId, latestVersionId, StringComparison.OrdinalIgnoreCase),
                    ChangeType = resolvedChangeType
                };
            });

            if (!string.IsNullOrWhiteSpace(q))
            {
                allItems = allItems.Where(s =>
                    (s.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    s.VersionId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (s.ClientIp?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            if (!string.IsNullOrWhiteSpace(changeType))
            {
                allItems = allItems.Where(s => string.Equals(s.ChangeType, changeType, StringComparison.OrdinalIgnoreCase));
            }

            var total = allItems.Count();
            var result = allItems
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            return Ok(new
            {
                code = 200,
                data = result,
                count = result.Count,
                pagination = new
                {
                    page,
                    pageSize,
                    total,
                    totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration history");
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Failed to get history", ex) });
        }
    }

    /// <summary>
    /// Clear all configuration history snapshots.
    /// </summary>
    [HttpDelete("history")]
    public async Task<IActionResult> ClearConfigHistory()
    {
        try
        {
            await _persistenceService.ClearHistoryAsync();
            return Ok(new { code = 200, message = "Configuration history cleared" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear configuration history");
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Failed to clear history", ex) });
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Rollback failed", ex) });
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Snapshot failed", ex) });
        }
    }

    /// <summary>
    /// Gets runtime metrics for the asynchronous configuration snapshot queue.
    /// </summary>
    [HttpGet("snapshot/metrics")]
    public IActionResult GetSnapshotMetrics()
    {
        var metrics = _snapshotScheduler.GetMetrics();
        var options = _configHistoryOptions.CurrentValue;
        return Ok(new
        {
            code = 200,
            data = new
            {
                metrics.QueueLength,
                metrics.EnqueuedCount,
                metrics.ProcessedCount,
                metrics.FailedCount,
                metrics.DroppedCount,
                options.AutoSnapshotBeforeMutation,
                options.AsyncSnapshotForLowRiskMutation,
                options.MaxSnapshots,
                options.SnapshotQueueCapacity
            }
        });
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Diff failed", ex) });
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Validation failed", ex) });
        }
    }

    // ── Diff Helpers ──

    private static string ResolveHistoryChangeType(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return "manual";
        var text = description.ToLowerInvariant();
        if (text.Contains("rollback")) return "rollback";
        if (text.Contains("import")) return "import";
        if (text.Contains("deleted") || text.Contains("remove")) return "delete";
        if (text.Contains("renamed")) return "rename";
        if (text.Contains("saved") || text.Contains("update")) return "update";
        return "manual";
    }

    private static JsonElement GetReverseProxySection(JsonElement config)
    {
        if (config.ValueKind == JsonValueKind.Object &&
            (config.TryGetProperty("reverseProxy", out var rp) || config.TryGetProperty("ReverseProxy", out rp)))
        {
            return rp;
        }

        return default;
    }

    private static List<SnapshotRoute> ParseSnapshotRoutes(JsonElement config)
    {
        var routes = new List<SnapshotRoute>();
        var reverseProxy = GetReverseProxySection(config);
        if (reverseProxy.ValueKind != JsonValueKind.Object) return routes;
        if (!(reverseProxy.TryGetProperty("routes", out var routesElement) || reverseProxy.TryGetProperty("Routes", out routesElement)))
            return routes;

        if (routesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var route in routesElement.EnumerateArray())
                routes.Add(ParseSnapshotRoute(route, null));
        }
        else if (routesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var route in routesElement.EnumerateObject())
                routes.Add(ParseSnapshotRoute(route.Value, route.Name));
        }

        return routes.Where(r => !string.IsNullOrWhiteSpace(r.RouteId)).ToList();
    }

    private static SnapshotRoute ParseSnapshotRoute(JsonElement route, string? fallbackRouteId)
    {
        var routeId = route.TryGetProperty("routeId", out var rid) ? rid.GetString() ?? string.Empty :
            route.TryGetProperty("RouteId", out var ridp) ? ridp.GetString() ?? string.Empty : fallbackRouteId ?? string.Empty;

        var clusterId = route.TryGetProperty("clusterId", out var cid) ? cid.GetString() ?? string.Empty :
            route.TryGetProperty("ClusterId", out var cidp) ? cidp.GetString() ?? string.Empty : string.Empty;

        string matchPath = string.Empty;
        if (route.TryGetProperty("match", out var match) || route.TryGetProperty("Match", out match))
        {
            if (match.TryGetProperty("path", out var path) || match.TryGetProperty("Path", out path))
                matchPath = path.GetString() ?? string.Empty;
        }

        var order = route.TryGetProperty("order", out var ord) ? ord.GetInt32() :
            route.TryGetProperty("Order", out var ordp) ? ordp.GetInt32() : 0;

        return new SnapshotRoute
        {
            RouteId = routeId,
            ClusterId = clusterId,
            MatchPath = matchPath,
            Order = order
        };
    }

    private static List<SnapshotCluster> ParseSnapshotClusters(JsonElement config)
    {
        var clusters = new List<SnapshotCluster>();
        var reverseProxy = GetReverseProxySection(config);
        if (reverseProxy.ValueKind != JsonValueKind.Object) return clusters;
        if (!(reverseProxy.TryGetProperty("clusters", out var clustersElement) || reverseProxy.TryGetProperty("Clusters", out clustersElement)))
            return clusters;

        if (clustersElement.ValueKind != JsonValueKind.Object) return clusters;

        foreach (var kvp in clustersElement.EnumerateObject())
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
                    "removed",
                    $"ClusterId: {sr.ClusterId}, Path: {sr.MatchPath}, Order: {sr.Order}"));
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
                    "added",
                    null,
                    $"ClusterId: {live.ClusterId}, Path: {live.Match?.Path}, Order: {live.Order}"));
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
                var snapshotDest = string.Join(", ", sc.Destinations.Select(d => $"{d.Key}={d.Value}"));
                diffs.Add(GetDiffItem("cluster", sc.ClusterId, "removed",
                    $"Destinations: [{snapshotDest}], Policy: {sc.LoadBalancingPolicy ?? "none"}"));
            }
        }

        var snapshotSet = snapshotClusters.Select(s => s.ClusterId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var live in currentClusters)
        {
            if (!snapshotSet.Contains(live.ClusterId ?? string.Empty))
            {
                var dest = string.Join(", ", live.Destinations?.Select(d => $"{d.Key}={d.Value.Address}") ?? Enumerable.Empty<string>());
                diffs.Add(GetDiffItem("cluster", $"{live.ClusterId} ({dest})", "added",
                    null,
                    $"Destinations: [{dest}], Policy: {live.LoadBalancingPolicy ?? "none"}"));
            }
        }

        return diffs;
    }

    private static object GetDiffItem(string entityType, string path, string diffType, string? oldValue = null, string? newValue = null)
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Operation failed", ex) });
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
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Operation failed", ex) });
        }
    }
}
