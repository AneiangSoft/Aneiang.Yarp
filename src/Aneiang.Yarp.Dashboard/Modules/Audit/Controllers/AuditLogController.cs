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
    /// <param name="count">Maximum number of entries to return (default 50, max 200).</param>
    /// <param name="action">Optional filter by action type (e.g. "AddRoute", "RemoveCluster").</param>
    /// <returns>Audit log entries in reverse chronological order.</returns>
    [HttpGet]
    public IActionResult GetAuditLogs([FromQuery] int count = 50, [FromQuery] string? action = null)
    {
        count = Math.Clamp(count, 1, 200);

        var entries = _auditLog.GetRecent(count);

        if (!string.IsNullOrEmpty(action))
        {
            entries = entries.Where(e =>
                e.Action.Equals(action, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Json(new
        {
            code = 200,
            data = new
            {
                entries,
                total = _auditLog.Count,
                evicted = _auditLog.EvictedCount
            }
        });
    }

    /// <summary>
    /// Get available action types for filtering.
    /// </summary>
    [HttpGet("actions")]
    public IActionResult GetActionTypes()
    {
        var entries = _auditLog.GetRecent(200);
        var actionTypes = entries
            .Select(e => e.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

        return Json(new { code = 200, data = actionTypes });
    }
}
