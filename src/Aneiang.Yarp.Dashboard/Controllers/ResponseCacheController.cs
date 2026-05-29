using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>Response cache management API.</summary>
[Route("apigateway/api/response-cache")]
[ApiController]
public class ResponseCacheController : ControllerBase
{
    private readonly ResponseCacheService _cache;

    /// <summary>Creates a new instance.</summary>
    public ResponseCacheController(ResponseCacheService cache) => _cache = cache;

    /// <summary>Get cache statistics.</summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var stats = _cache.GetStats();
        return Ok(new { code = 200, data = stats });
    }

    /// <summary>Clear all cache entries.</summary>
    [HttpDelete("")]
    public IActionResult ClearCache()
    {
        _cache.Clear();
        return Ok(new { code = 200, message = "Cache cleared" });
    }

    /// <summary>Invalidate cache for a specific route.</summary>
    [HttpDelete("routes/{routeId}")]
    public IActionResult InvalidateRoute(string routeId)
    {
        _cache.RemoveByPrefix(routeId);
        return Ok(new { code = 200, message = $"Cache invalidated for route '{routeId}'" });
    }
}
