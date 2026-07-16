using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Controllers;

/// <summary>
/// Cluster status and configuration endpoints.
/// </summary>
public class DashboardClusterController : Controller
{
    private readonly IDashboardClusterQueryService _clusterQuery;

    public DashboardClusterController(IDashboardClusterQueryService clusterQuery)
    {
        _clusterQuery = clusterQuery;
    }

    /// <summary>Cluster status and config.</summary>
    [HttpGet("api/clusters")]
    public IActionResult GetClusters()
    {
        var clusters = _clusterQuery.GetClusters();
        return Json(ApiResponse.Ok(clusters));
    }
}
