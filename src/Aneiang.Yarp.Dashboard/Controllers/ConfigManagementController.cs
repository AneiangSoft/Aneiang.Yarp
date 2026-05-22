using System.Text.Json;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// Configuration management API for import/export/save/rollback operations.
/// </summary>
[Route("apigateway/api/config")]
[ApiController]
public class ConfigManagementController : ControllerBase
{
    private readonly ConfigPersistenceService _persistenceService;
    private readonly DynamicYarpConfigService _dynamicConfig;
    private readonly ILogger<ConfigManagementController> _logger;

    public ConfigManagementController(
        ConfigPersistenceService persistenceService,
        DynamicYarpConfigService dynamicConfig,
        ILogger<ConfigManagementController> logger)
    {
        _persistenceService = persistenceService;
        _dynamicConfig = dynamicConfig;
        _logger = logger;
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
                exportedAt = DateTime.UtcNow
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
    public IActionResult GetConfigHistory()
    {
        try
        {
            var history = _persistenceService.GetHistory();
            
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
}
