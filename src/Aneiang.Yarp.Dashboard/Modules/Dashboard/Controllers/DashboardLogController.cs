using Aneiang.Yarp.Dashboard.Infrastructure;
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
    private readonly LogSettingsService _logSettings;
    private readonly DashboardOptions _options;

    public DashboardLogController(
        IDashboardLogQueryService logQuery,
        IProxyLogPersistenceService persistenceService,
        LogSettingsService logSettings,
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
        return Json(new { code = 200, data = snapshot });
    }

    /// <summary>Clear all logs.</summary>
    [HttpDelete("api/logs")]
    public IActionResult ClearLogs()
    {
        _logQuery.ClearLogs();
        return Json(new { code = 200, message = "Logs cleared" });
    }

    /// <summary>Historical log metadata from SQLite (paginated, filtered).</summary>
    [HttpGet("api/logs/history")]
    public async Task<IActionResult> GetLogHistory([FromQuery] ProxyLogSearchRequest request, CancellationToken ct)
    {
        if (!_options.LogPersistenceEnabled)
            return Json(new { code = 200, data = new ProxyLogSearchResult { Items = new List<ProxyLogMetaItem>(), TotalCount = 0 } });

        request.PageSize = Math.Clamp(request.PageSize, 1, 200);
        request.Page = Math.Max(1, request.Page);
        var result = await _logQuery.GetHistoryLogsAsync(request, ct);
        return Json(new { code = 200, data = result });
    }

    /// <summary>Single log detail (full body/headers) from SQLite.</summary>
    [HttpGet("api/logs/detail/{id}")]
    public async Task<IActionResult> GetLogDetail(long id, CancellationToken ct)
    {
        if (!_options.LogPersistenceEnabled)
            return Json(new { code = 404, message = "Log persistence is not enabled" });

        var detail = await _logQuery.GetLogDetailAsync(id, ct);
        if (detail == null)
            return Json(new { code = 404, message = "Log entry not found" });

        return Json(new { code = 200, data = detail });
    }

    /// <summary>Log persistence stats (dropped/written counts).</summary>
    [HttpGet("api/logs/stats")]
    public IActionResult GetLogStats()
    {
        return Json(new
        {
            code = 200,
            data = new
            {
                droppedCount = _persistenceService.DroppedCount,
                writtenCount = _persistenceService.WrittenCount,
                persistenceEnabled = _options.LogPersistenceEnabled,
                bufferCapacity = _options.LogBufferCapacity
            }
        });
    }

    /// <summary>Get current log settings (SQLite overrides → IOptionsMonitor → defaults).</summary>
    [HttpGet("api/logs/settings")]
    public async Task<IActionResult> GetLogSettings(CancellationToken ct)
    {
        var settings = await _logSettings.LoadAsync(ct);
        return Json(new { code = 200, data = settings });
    }

    /// <summary>Update log settings. Only provided fields are updated.</summary>
    [HttpPut("api/logs/settings")]
    public async Task<IActionResult> UpdateLogSettings([FromBody] LogSettingsUpdateRequest request, CancellationToken ct)
    {
        if (request == null)
            return Json(new { code = 400, message = "Request body is required" });

        if (request.LogSamplingRate.HasValue && (request.LogSamplingRate.Value < 0 || request.LogSamplingRate.Value > 1))
            return Json(new { code = 400, message = "LogSamplingRate must be between 0.0 and 1.0" });

        if (request.LogMetaRetentionDays.HasValue && (request.LogMetaRetentionDays.Value < 1 || request.LogMetaRetentionDays.Value > 365))
            return Json(new { code = 400, message = "LogMetaRetentionDays must be between 1 and 365" });

        if (request.MinLogLevel != null)
        {
            var validLevels = new[] { "Debug", "Information", "Warning", "Error", "Critical" };
            if (!validLevels.Contains(request.MinLogLevel, StringComparer.OrdinalIgnoreCase))
                return Json(new { code = 400, message = $"MinLogLevel must be one of: {string.Join(", ", validLevels)}" });
        }

        var updated = await _logSettings.SaveAsync(request, ct);
        return Json(new { code = 200, data = updated });
    }

    /// <summary>Reset log settings to defaults (clears SQLite overrides).</summary>
    [HttpPut("api/logs/settings/reset")]
    public async Task<IActionResult> ResetLogSettings(CancellationToken ct)
    {
        var defaults = await _logSettings.ResetAsync(ct);
        return Json(new { code = 200, data = defaults });
    }
}
