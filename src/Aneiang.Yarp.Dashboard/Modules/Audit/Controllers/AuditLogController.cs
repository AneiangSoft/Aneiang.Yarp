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
    /// <param name="count">Maximum number of entries to return for legacy callers.</param>
    /// <param name="action">Optional filter by action type (e.g. "AddRoute", "RemoveCluster").</param>
    /// <param name="page">Page number for paged callers.</param>
    /// <param name="pageSize">Page size for paged callers.</param>
    /// <returns>Audit log entries in reverse chronological order.</returns>
    [HttpGet]
    public IActionResult GetAuditLogs(
        [FromQuery] int count = 50,
        [FromQuery] string? action = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var effectivePage = Math.Max(1, page ?? 1);
        var effectivePageSize = Math.Clamp(pageSize ?? count, 1, 200);
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
