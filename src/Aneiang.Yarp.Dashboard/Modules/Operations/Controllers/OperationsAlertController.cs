using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.Operations.Application;
using Aneiang.Yarp.Dashboard.Modules.Operations.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Alert monitoring API - provides alert summary for the top alert bar.
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsAlertController : ControllerBase
{
    private readonly IAlertAppService _alertService;
    private readonly IMemoryCache _memoryCache;

    public OperationsAlertController(IAlertAppService alertService, IMemoryCache memoryCache)
    {
        _alertService = alertService;
        _memoryCache = memoryCache;
    }

    [HttpGet("alert-summary")]
    public async Task<IActionResult> GetAlertSummary()
    {
        var data = await _memoryCache.GetOrCreateAsync("ops:alert-summary", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            return await _alertService.ComputeAlertSummaryAsync();
        })!;
        return Ok(ApiResponse.Ok(data));
    }
}
