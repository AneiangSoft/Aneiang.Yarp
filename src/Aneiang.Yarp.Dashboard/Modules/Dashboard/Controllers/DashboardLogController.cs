using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Proxy log query, history, detail, and settings endpoints.
/// </summary>
public class DashboardLogController : Controller
{
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IProxyLogPersistenceService _persistenceService;
    private readonly ILogSettingsService _logSettings;
    private readonly DashboardOptions _options;

    public DashboardLogController(
        IDashboardLogQueryService logQuery,
        IProxyLogPersistenceService persistenceService,
        ILogSettingsService logSettings,
        IOptions<DashboardOptions> dashboardOptions)
    {
        _logQuery = logQuery;
        _persistenceService = persistenceService;
        _logSettings = logSettings;
        _options = dashboardOptions.Value;
    }

    /// <summary>Recent YARP proxy logs.</summary>
    [HttpGet("api/logs")]
    public IActionResult GetLogs([FromQuery] int count = 100, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        var snapshot = page.HasValue || pageSize.HasValue
            ? _logQuery.GetLogsPage(page ?? 1, pageSize ?? count)
            : _logQuery.GetLogs(count);
        return Json(ApiResponse.Ok(snapshot));
    }

    /// <summary>Clear all logs.</summary>
    [HttpDelete("api/logs")]
    public IActionResult ClearLogs()
    {
        _logQuery.ClearLogs();
        return Json(ApiResponse.Ok("Logs cleared"));
    }

    /// <summary>Historical log metadata from SQLite (paginated, filtered).</summary>
    [HttpGet("api/logs/history")]
    public async Task<IActionResult> GetLogHistory([FromQuery] ProxyLogSearchRequest request, CancellationToken ct)
    {
        if (!_options.LogPersistenceEnabled)
            return Json(ApiResponse.Ok(new ProxyLogSearchResult { Items = new List<ProxyLogMetaItem>(), TotalCount = 0 }));

        request.PageSize = Math.Clamp(request.PageSize, 1, 200);
        request.Page = Math.Max(1, request.Page);
        var result = await _logQuery.GetHistoryLogsAsync(request, ct);
        return Json(ApiResponse.Ok(result));
    }

    /// <summary>Single log detail (full body/headers) from SQLite.</summary>
    [HttpGet("api/logs/detail/{id}")]
    public async Task<IActionResult> GetLogDetail(long id, CancellationToken ct)
    {
        if (!_options.LogPersistenceEnabled)
            return NotFound(ApiResponse.Fail("Log persistence is not enabled", 404));

        var detail = await _logQuery.GetLogDetailAsync(id, ct);
        if (detail == null)
            return NotFound(ApiResponse.Fail("Log entry not found", 404));

        return Json(ApiResponse.Ok(detail));
    }

    /// <summary>Log persistence stats (dropped/written counts).</summary>
    [HttpGet("api/logs/stats")]
    public IActionResult GetLogStats()
    {
        return Json(ApiResponse.Ok(new
        {
            droppedCount = _persistenceService.DroppedCount,
            writtenCount = _persistenceService.WrittenCount,
            persistenceEnabled = _options.LogPersistenceEnabled,
            bufferCapacity = _options.LogBufferCapacity
        }));
    }

    /// <summary>Get current log settings (SQLite overrides -> IOptionsMonitor -> defaults).</summary>
    [HttpGet("api/logs/settings")]
    public async Task<IActionResult> GetLogSettings(CancellationToken ct)
    {
        var settings = await _logSettings.LoadAsync(ct);
        return Json(ApiResponse.Ok(settings));
    }

    /// <summary>Update log settings. Only provided fields are updated.</summary>
    [HttpPut("api/logs/settings")]
    public async Task<IActionResult> UpdateLogSettings([FromBody] LogSettingsUpdateRequest request, CancellationToken ct)
    {
        if (request == null)
            return BadRequest(ApiResponse.Fail("Request body is required"));

        if (request.LogSamplingRate.HasValue && (request.LogSamplingRate.Value < 0 || request.LogSamplingRate.Value > 1))
            return BadRequest(ApiResponse.Fail("LogSamplingRate must be between 0.0 and 1.0"));

        if (request.LogMetaRetentionDays.HasValue && (request.LogMetaRetentionDays.Value < 1 || request.LogMetaRetentionDays.Value > 365))
            return BadRequest(ApiResponse.Fail("LogMetaRetentionDays must be between 1 and 365"));

        if (request.MinLogLevel != null)
        {
            var validLevels = new[] { "Debug", "Information", "Warning", "Error", "Critical" };
            if (!validLevels.Contains(request.MinLogLevel, StringComparer.OrdinalIgnoreCase))
                return BadRequest(ApiResponse.Fail($"MinLogLevel must be one of: {string.Join(", ", validLevels)}"));
        }

        var updated = await _logSettings.SaveAsync(request, ct);
        return Json(ApiResponse.Ok(updated));
    }

    /// <summary>Reset log settings to defaults (clears SQLite overrides).</summary>
    [HttpPut("api/logs/settings/reset")]
    public async Task<IActionResult> ResetLogSettings(CancellationToken ct)
    {
        var defaults = await _logSettings.ResetAsync(ct);
        return Json(ApiResponse.Ok(defaults));
    }
}
