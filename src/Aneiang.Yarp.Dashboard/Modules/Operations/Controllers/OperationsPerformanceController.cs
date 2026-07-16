using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Modules.Operations.Application;
using Aneiang.Yarp.Dashboard.Modules.Operations.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Performance metrics - traffic time series and top issues.
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsPerformanceController : ControllerBase
{
    private readonly IPerformanceAppService _performanceService;
    private readonly IMemoryCache _memoryCache;

    public OperationsPerformanceController(IPerformanceAppService performanceService, IMemoryCache memoryCache)
    {
        _performanceService = performanceService;
        _memoryCache = memoryCache;
    }

    [HttpGet("traffic")]
    public async Task<IActionResult> GetTrafficData([FromQuery] int minutes = 15)
    {
        var data = await _memoryCache.GetOrCreateAsync($"ops:traffic:{minutes}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            return await _performanceService.ComputeTrafficDataAsync(minutes);
        })!;
        return Ok(ApiResponse.Ok(data));
    }

    [HttpGet("top-issues")]
    public async Task<IActionResult> GetTopIssues([FromQuery] int count = 5)
    {
        var data = await _memoryCache.GetOrCreateAsync($"ops:top-issues:{count}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await _performanceService.ComputeTopIssuesAsync(count);
        })!;
        return Ok(ApiResponse.Ok(data));
    }
}
