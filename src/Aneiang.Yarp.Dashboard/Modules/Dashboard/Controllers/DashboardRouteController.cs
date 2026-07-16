using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Route configuration endpoints.
/// </summary>
public class DashboardRouteController : Controller
{
    private readonly IDashboardRouteQueryService _routeQuery;

    public DashboardRouteController(IDashboardRouteQueryService routeQuery)
    {
        _routeQuery = routeQuery;
    }

    /// <summary>Route configuration.</summary>
    [HttpGet("api/routes")]
    public IActionResult GetRoutes()
    {
        var routes = _routeQuery.GetRoutes();
        return Json(ApiResponse.Ok(routes));
    }
}
