using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Application;
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
/// Business logic is delegated to <see cref="IConfigHistoryAppService"/>.
/// </summary>
[Route("api/config")]
[ApiController]
public class ConfigHistoryController : ConfigControllerBase
{
    private readonly IConfigHistoryAppService _appService;

    public ConfigHistoryController(
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        IMemoryCache memoryCache,
        IConfigSnapshotScheduler snapshotScheduler,
        IOptionsMonitor<ConfigHistoryOptions> configHistoryOptions,
        IConfigHistoryAppService appService)
        : base(persistenceService, dynamicConfig, memoryCache, snapshotScheduler, configHistoryOptions)
    {
        _appService = appService;
    }

    #region Export / Import

    [HttpGet("export")]
    public async Task<IActionResult> ExportConfig()
    {
        var data = await _appService.ExportConfigAsync();
        return Ok(ApiResponse.Ok(data));
    }

    [HttpPost("import")]
    public async Task<IActionResult> ImportConfig([FromBody] JsonElement config)
    {
        var (success, message, data) = await _appService.ImportConfigAsync(config, GetClientIp() ?? "");
        if (success) InvalidateQueryCaches();
        return success
            ? Ok(ApiResponse.Ok(data, message ?? "Configuration imported successfully"))
            : BadRequest(ApiResponse.Fail(message ?? "Failed to import configuration"));
    }

    #endregion

    #region History

    [HttpGet("history")]
    public async Task<IActionResult> GetConfigHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? q = null,
        [FromQuery] string? changeType = null)
    {
        var data = await _appService.GetConfigHistoryAsync(page, pageSize, q, changeType);
        return Ok(ApiResponse.Ok(data));
    }

    [HttpDelete("history")]
    public async Task<IActionResult> ClearConfigHistory()
    {
        await _appService.ClearHistoryAsync();
        return Ok(ApiResponse.Ok("Configuration history cleared"));
    }

    #endregion

    #region Rollback

    [HttpPost("rollback/{versionId}")]
    public async Task<IActionResult> RollbackConfig(string versionId)
    {
        var (success, message) = await _appService.RollbackAsync(versionId, GetClientIp() ?? "");
        if (success) InvalidateQueryCaches();
        return success
            ? Ok(ApiResponse.Ok(message))
            : BadRequest(ApiResponse.Fail(message));
    }

    [HttpPost("snapshot")]
    public async Task<IActionResult> CreateSnapshot([FromBody] SnapshotRequest? request)
    {
        var data = await _appService.CreateSnapshotAsync(request?.Description, GetClientIp() ?? "");
        return Ok(ApiResponse.Ok(data));
    }

    [HttpGet("snapshot/metrics")]
    public IActionResult GetSnapshotMetrics()
    {
        var data = _appService.GetSnapshotMetrics();
        return Ok(ApiResponse.Ok(data));
    }

    #endregion

    #region Diff

    [HttpGet("diff/{versionId}")]
    public async Task<IActionResult> ConfigDiff(string versionId)
    {
        var data = await _appService.ConfigDiffAsync(versionId);
        return Ok(ApiResponse.Ok(data));
    }

    [HttpPost("validate")]
    public IActionResult ValidateConfig([FromBody] JsonElement config)
    {
        var data = _appService.ValidateConfig(config);
        return Ok(new { code = 200, success = true, valid = ((dynamic)data).valid, errors = ((dynamic)data).errors });
    }

    #endregion

    #region WAF Settings

    [HttpGet("waf")]
    public IActionResult GetWafSettings()
    {
        var data = _appService.GetWafSettings();
        return Ok(ApiResponse.Ok(data));
    }

    [HttpPut("waf")]
    public async Task<IActionResult> UpdateWafSettings([FromBody] WafSettingsData request)
    {
        if (request == null)
            throw new ValidationException("Request body is required");

        var (success, error) = await _appService.UpdateWafSettingsAsync(request);
        if (!success)
            throw new ServerException(error ?? "Failed to save WAF settings");

        return Ok(ApiResponse.Ok("WAF settings saved successfully"));
    }

    #endregion
}
