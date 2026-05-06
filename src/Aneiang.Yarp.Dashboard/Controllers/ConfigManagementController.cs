using System.Text.Json;
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

            var success = await _persistenceService.ImportFullConfigAsync(config);
            
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
                if (destsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dest in destsProp.EnumerateObject())
                    {
                        var address = dest.Value.ValueKind == JsonValueKind.String
                            ? dest.Value.GetString() ?? string.Empty
                            : dest.Value.TryGetProperty("address", out var addrProp)
                                ? addrProp.GetString() ?? string.Empty
                                : string.Empty;
                        destinations[dest.Name] = address;
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

            var result = _dynamicConfig.TryAddCluster(clusterId, destinations, loadBalancingPolicy, healthCheck, "dashboard", "dashboard-user");

            // Save snapshot
            await _persistenceService.SaveSnapshotAsync($"Cluster '{clusterId}' saved via dashboard");

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

            var result = _dynamicConfig.TryRemoveCluster(clusterId);

            if (result.Success)
            {
                // Save snapshot
                await _persistenceService.SaveSnapshotAsync($"Cluster '{clusterId}' deleted via dashboard");
                return Ok(new { code = 200, message = result.Message, clusterId = clusterId });
            }
            else
            {
                return BadRequest(new { code = 400, message = result.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete cluster: {ClusterId}", clusterId);
            return StatusCode(500, new { code = 500, message = $"Delete failed: {ex.Message}" });
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

            // Parse required fields
            if (!config.TryGetProperty("clusterId", out var clusterIdProp))
            {
                return BadRequest(new { code = 400, message = "clusterId is required" });
            }

            if (!config.TryGetProperty("matchPath", out var matchPathProp) &&
                !config.TryGetProperty("match", out var matchProp))
            {
                return BadRequest(new { code = 400, message = "matchPath or match is required" });
            }

            // Get clusterId
            var clusterId = clusterIdProp.GetString() ?? string.Empty;

            // Get matchPath (support both direct matchPath and nested match.path)
            string matchPath = string.Empty;
            bool hasMatchPath = config.TryGetProperty("matchPath", out var matchPathProp2);
            bool hasMatch = config.TryGetProperty("match", out var matchProp2);
            
            if (hasMatchPath && matchPathProp2.ValueKind != JsonValueKind.Undefined)
            {
                matchPath = matchPathProp2.GetString() ?? string.Empty;
            }
            else if (hasMatch && matchProp2.TryGetProperty("path", out var pathProp))
            {
                matchPath = pathProp.GetString() ?? string.Empty;
            }
            else
            {
                return BadRequest(new { code = 400, message = "match path is required" });
            }

            // Get destinations for cluster
            Dictionary<string, string> destinations = new();
            if (config.TryGetProperty("destinations", out var destsProp))
            {
                if (destsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dest in destsProp.EnumerateObject())
                    {
                        var address = dest.Value.ValueKind == JsonValueKind.String
                            ? dest.Value.GetString() ?? string.Empty
                            : dest.Value.TryGetProperty("address", out var addrProp)
                                ? addrProp.GetString() ?? string.Empty
                                : string.Empty;
                        destinations[dest.Name] = address;
                    }
                }
            }

            // If no destinations, try to get from cluster or use default
            if (destinations.Count == 0)
            {
                // Check if cluster exists and use its destinations
                var existingCluster = _dynamicConfig.GetCluster(clusterId);
                if (existingCluster?.Destinations != null)
                {
                    foreach (var dest in existingCluster.Destinations)
                    {
                        destinations[dest.Key] = dest.Value.Address ?? string.Empty;
                    }
                }

                if (destinations.Count == 0)
                {
                    return BadRequest(new { code = 400, message = "destinations are required for new cluster" });
                }
            }

            // Get order
            int? order = null;
            if (config.TryGetProperty("order", out var orderProp))
            {
                order = orderProp.GetInt32();
            }

            // Get transforms
            List<Dictionary<string, string>>? transforms = null;
            if (config.TryGetProperty("transforms", out var transformsProp))
            {
                transforms = transformsProp.Deserialize<List<Dictionary<string, string>>>();
            }

            // Create request
            var request = new RegisterRouteRequest
            {
                RouteName = routeId,
                ClusterName = clusterId,
                MatchPath = matchPath,
                DestinationAddress = destinations.Values.FirstOrDefault() ?? string.Empty,
                Order = order,
                Transforms = transforms
            }; // Note: For multi-destination clusters, we update cluster separately

            var result = _dynamicConfig.TryAddRoute(request, "dashboard", "dashboard-user");

            // Also update cluster if multiple destinations
            if (destinations.Count > 1)
            {
                _dynamicConfig.TryAddCluster(clusterId, destinations, null, null, "dashboard", "dashboard-user");
            }

            // Save snapshot
            await _persistenceService.SaveSnapshotAsync($"Route '{routeId}' saved via dashboard");

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
    public async Task<IActionResult> DeleteRoute(string routeId)
    {
        try
        {
            _logger.LogInformation("Delete route requested: {RouteId}", routeId);

            var result = _dynamicConfig.TryRemoveRoute(routeId);

            if (result.Success)
            {
                // Save snapshot
                await _persistenceService.SaveSnapshotAsync($"Route '{routeId}' deleted via dashboard");
                return Ok(new { code = 200, message = result.Message, routeId = routeId });
            }
            else
            {
                return BadRequest(new { code = 400, message = result.Message });
            }
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
                s.Description
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
            var success = await _persistenceService.RollbackAsync(versionId);
            
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
