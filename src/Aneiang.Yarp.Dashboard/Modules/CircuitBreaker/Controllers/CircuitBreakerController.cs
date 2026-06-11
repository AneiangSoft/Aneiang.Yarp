using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Controllers;

/// <summary>
/// API controller for circuit breaker status monitoring and management.
/// </summary>
[Route("api/circuit-breaker")]
public class CircuitBreakerController : Controller
{
    /// <summary>
    /// Get circuit breaker status for all clusters.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetCircuitBreakerStatus()
    {
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
}
