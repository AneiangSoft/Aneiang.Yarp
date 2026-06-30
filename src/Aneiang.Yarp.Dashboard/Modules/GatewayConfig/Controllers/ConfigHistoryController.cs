using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Waf.Services;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers;

/// <summary>
/// Configuration history, export/import, snapshot, rollback, diff, validation, and WAF endpoints.
/// </summary>
[Route("api/config")]
[ApiController]
public class ConfigHistoryController : ConfigControllerBase
{
    private readonly ILogger<ConfigHistoryController> _logger;
    private readonly IConfigDiffService _diffService;
    private readonly IWafSettingsPersistenceService? _wafPersistence;

    public ConfigHistoryController(
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        ILogger<ConfigHistoryController> logger,
        IMemoryCache memoryCache,
        IConfigSnapshotScheduler snapshotScheduler,
        IOptionsMonitor<ConfigHistoryOptions> configHistoryOptions,
        IConfigDiffService diffService,
        IWafSettingsPersistenceService? wafPersistence = null)
        : base(persistenceService, dynamicConfig, memoryCache, snapshotScheduler, configHistoryOptions)
    {
        _logger = logger;
        _diffService = diffService;
        _wafPersistence = wafPersistence;
    }

    // ── Export / Import ──

    /// <summary>
    /// Export full configuration in standard YARP format.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportConfig()
    {
        try
        {
            var config = await PersistenceService.ExportFullConfigAsync();

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
            var validationResult = PersistenceService.ValidateConfig(config);
            if (!validationResult.Valid)
            {
                return BadRequest(new
                {
                    code = 400,
                    message = "Invalid configuration format",
                    errors = validationResult.Errors
                });
            }

            var success = await PersistenceService.ImportFullConfigAsync(config, GetClientIp());

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

    // ── History ──

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

            var history = (await PersistenceService.GetHistoryAsync())
                .OrderByDescending(s => s.Timestamp)
                .ToList();

            var latestVersionId = history.FirstOrDefault()?.VersionId;
            var allItems = history.Select(s =>
            {
                var routeCount = _diffService.ParseSnapshotRoutes(s.Config).Count;
                var clusterCount = _diffService.ParseSnapshotClusters(s.Config).Count;
                var resolvedChangeType = _diffService.ResolveHistoryChangeType(s.Description);
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
            await PersistenceService.ClearHistoryAsync();
            return Ok(new { code = 200, message = "Configuration history cleared" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear configuration history");
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Failed to clear history", ex) });
        }
    }

    // ── Rollback ──

    /// <summary>
    /// Rollback configuration to a specific version.
    /// </summary>
    [HttpPost("rollback/{versionId}")]
    public async Task<IActionResult> RollbackConfig(string versionId)
    {
        try
        {
            var success = await PersistenceService.RollbackAsync(versionId, GetClientIp());

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

    // ── Snapshot ──

    /// <summary>
    /// Create a manual configuration snapshot.
    /// </summary>
    [HttpPost("snapshot")]
    public async Task<IActionResult> CreateSnapshot([FromBody] SnapshotRequest? request)
    {
        try
        {
            var description = request?.Description ?? "Manual snapshot";
            var snapshot = await PersistenceService.SaveSnapshotAsync(description, GetClientIp());
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
        var metrics = SnapshotScheduler.GetMetrics();
        var options = ConfigHistoryOptions.CurrentValue;
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

    // ── Diff ──

    /// <summary>
    /// Diff a historical version against the current live configuration.
    /// </summary>
    [HttpGet("diff/{versionId}")]
    public async Task<IActionResult> ConfigDiff(string versionId)
    {
        try
        {
            var history = await PersistenceService.GetHistoryAsync();
            var historySnapshot = history
                .FirstOrDefault(s => string.Equals(s.VersionId, versionId, StringComparison.OrdinalIgnoreCase));

            if (historySnapshot == null)
                return NotFound(new { code = 404, message = $"Version '{versionId}' not found" });

            var snapshotRoutes = _diffService.ParseSnapshotRoutes(historySnapshot.Config);
            var snapshotClusters = _diffService.ParseSnapshotClusters(historySnapshot.Config);

            var currentRoutes = DynamicConfig.GetRoutes();
            var currentClusters = DynamicConfig.GetClusters();

            var routeDiffs = _diffService.BuildRouteDiffs(snapshotRoutes, currentRoutes);
            var clusterDiffs = _diffService.BuildClusterDiffs(snapshotClusters, currentClusters);

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
                data = new { summary, routes = routeDiffs, clusters = clusterDiffs }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to diff config for version: {VersionId}", versionId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Diff failed", ex) });
        }
    }

    // ── Validate ──

    /// <summary>
    /// Validate configuration format.
    /// </summary>
    [HttpPost("validate")]
    public IActionResult ValidateConfig([FromBody] JsonElement config)
    {
        try
        {
            var result = PersistenceService.ValidateConfig(config);
            return Ok(new { code = 200, valid = result.Valid, errors = result.Errors });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate configuration");
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Validation failed", ex) });
        }
    }

    // ── WAF Settings ──

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

            return Ok(new { code = 200, data = data });
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
