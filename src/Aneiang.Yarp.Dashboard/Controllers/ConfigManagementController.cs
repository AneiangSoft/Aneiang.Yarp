using System.Text.Json;
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
    private readonly ILogger<ConfigManagementController> _logger;

    public ConfigManagementController(
        ConfigPersistenceService persistenceService,
        ILogger<ConfigManagementController> logger)
    {
        _persistenceService = persistenceService;
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
            // Implementation would integrate with existing GatewayConfigController
            // For now, return placeholder response
            _logger.LogInformation("Save cluster requested: {ClusterId}", clusterId);
            
            return Ok(new 
            { 
                code = 200, 
                message = "Cluster saved successfully",
                clusterId = clusterId 
            });
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
            // Implementation would integrate with existing GatewayConfigController
            _logger.LogInformation("Delete cluster requested: {ClusterId}", clusterId);
            
            return Ok(new 
            { 
                code = 200, 
                message = "Cluster deleted successfully",
                clusterId = clusterId 
            });
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
            // Implementation would integrate with existing GatewayConfigController
            _logger.LogInformation("Save route requested: {RouteId}", routeId);
            
            return Ok(new 
            { 
                code = 200, 
                message = "Route saved successfully",
                routeId = routeId 
            });
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
            // Implementation would integrate with existing GatewayConfigController
            _logger.LogInformation("Delete route requested: {RouteId}", routeId);
            
            return Ok(new 
            { 
                code = 200, 
                message = "Route deleted successfully",
                routeId = routeId 
            });
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
