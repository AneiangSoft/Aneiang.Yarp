using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Application;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Access statistics endpoints with multi-tier priority:
/// 1) LockFreeStatistics snapshot (zero-allocation) -> 2) SQL aggregation -> 3) in-memory fallback.
/// </summary>
public class DashboardStatsController : Controller
{
    private readonly IStatsAppService _statsService;

    public DashboardStatsController(IStatsAppService statsService)
    {
        _statsService = statsService;
    }

    [HttpGet("api/stats")]
    public async Task<IActionResult> GetStats()
    {
        var data = await _statsService.GetStatsAsync();
        return Json(ApiResponse.Ok(data));
    }
}
