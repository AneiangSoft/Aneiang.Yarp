using Aneiang.Yarp.Dashboard.Middleware;
using Aneiang.Yarp.Dashboard.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// API controller for circuit breaker status monitoring and management.
/// </summary>
public class CircuitBreakerController : Controller
{
    /// <summary>
    /// Get circuit breaker status for all clusters.
    /// </summary>
    [HttpGet("circuit-breaker/status")]
    public IActionResult GetCircuitBreakerStatus()
    {
        var states = CircuitBreakerMiddleware.GetAllCircuitStates();
        return Json(new { code = 200, data = states });
    }

    /// <summary>
    /// Reset all circuit breakers to Closed state.
    /// </summary>
    [HttpPost("circuit-breaker/reset")]
    public IActionResult ResetCircuitBreakers()
    {
        CircuitBreakerMiddleware.ResetAll();
        return Json(new { code = 200, message = "All circuit breakers reset" });
    }
}
