using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.Operations.Application;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Health check and system snapshot endpoints.
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsHealthController : ControllerBase
{
    private readonly IHealthAppService _healthService;

    public OperationsHealthController(IHealthAppService healthService)
    {
        _healthService = healthService;
    }

    [HttpGet("health-summary")]
    public IActionResult GetHealthSummary()
    {
        var data = _healthService.GetHealthSummary();
        return Ok(ApiResponse.Ok(data));
    }

    [HttpGet("snapshot")]
    public IActionResult ExportSnapshot()
    {
        var snapshot = _healthService.ExportSnapshot();
        return Ok(ApiResponse.Ok(snapshot));
    }
}
