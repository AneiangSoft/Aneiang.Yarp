using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Gateway basic info endpoint.
/// </summary>
public class DashboardInfoController : Controller
{
    private readonly IDashboardInfoQueryService _infoQuery;

    public DashboardInfoController(IDashboardInfoQueryService infoQuery)
    {
        _infoQuery = infoQuery;
    }

    /// <summary>Gateway basic info.</summary>
    [HttpGet("api/info")]
    public IActionResult GetInfo()
    {
        var info = _infoQuery.GetInfo();
        return Json(new { code = 200, data = info });
    }
}
