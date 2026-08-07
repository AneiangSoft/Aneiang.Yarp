using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Audit.Controllers;

/// <summary>
/// API controller for accessing configuration change audit logs.
/// Exposes the in-memory IConfigChangeAuditLog data to the dashboard UI.
/// </summary>
[Route("api/audit-logs")]
public class AuditLogController : Controller
{
    private readonly IConfigChangeAuditLog _auditLog;

    /// <summary>Initializes a new instance of AuditLogController.</summary>
    public AuditLogController(IConfigChangeAuditLog auditLog)
    {
        _auditLog = auditLog;
    }

    /// <summary>
    /// Get recent audit log entries.
    /// </summary>
    /// <param name="action">Optional filter by action type (e.g. "AddRoute", "RemoveCluster").</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Page size (max 200).</param>
    /// <returns>Audit log entries in reverse chronological order.</returns>
    [HttpGet]
    public IActionResult GetAuditLogs(
        [FromQuery] string? action = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var effectivePage = Math.Max(1, page);
        var effectivePageSize = Math.Clamp(pageSize, 1, 200);
        var (entries, total) = _auditLog.GetPage(effectivePage, effectivePageSize, action);

        return Json(new
        {
            code = 200,
            data = new
            {
                entries,
                total,
                page = effectivePage,
                pageSize = effectivePageSize,
                totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)effectivePageSize),
                evicted = _auditLog.EvictedCount
            }
        });
    }
}
