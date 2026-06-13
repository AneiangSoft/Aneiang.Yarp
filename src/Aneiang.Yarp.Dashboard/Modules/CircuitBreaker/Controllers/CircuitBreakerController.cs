using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Controllers;

/// <summary>
/// API controller for circuit breaker status monitoring and management.
/// </summary>
[Route("api/circuit-breaker")]
public class CircuitBreakerController : Controller
{
    private readonly IDynamicYarpConfigService _yarpConfig;

    public CircuitBreakerController(IDynamicYarpConfigService yarpConfig)
    {
        _yarpConfig = yarpConfig;
    }

    /// <summary>
    /// Get circuit breaker status for all clusters.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetCircuitBreakerStatus()
    {
        // Ensure circuits exist for all clusters that have CB enabled
        SyncCircuitsFromConfig();

        var states = CircuitBreakerMiddleware.GetAllCircuitStates();
        return Json(new { code = 200, data = states });
    }

    /// <summary>
    /// Reset all circuit breakers to Closed state.
    /// </summary>
    [HttpPost("reset")]
    public IActionResult ResetCircuitBreakers()
    {
        CircuitBreakerMiddleware.ResetAll();
        return Json(new { code = 200, message = "All circuit breakers reset" });
    }

    /// <summary>
    /// Pre-create circuit entries for clusters with CB enabled so they are visible in the dashboard.
    /// </summary>
    private void SyncCircuitsFromConfig()
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        if (dynConfig?.Clusters == null) return;

        foreach (var cluster in dynConfig.Clusters)
        {
            if (cluster.CircuitBreaker is { Enabled: true } cbConfig)
            {
                CircuitBreakerMiddleware.EnsureCircuitExists(cluster.ClusterId, cbConfig);
            }
        }
    }
}
