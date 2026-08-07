using Aneiang.Yarp.Infrastructure.State;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Plugins.CircuitBreaker.Controllers;

[ApiController]
[Route("api/circuit-breaker")]
public sealed class CircuitBreakerController(
    ICircuitStateStore stateStore,
    GatewayPluginExecutionPlanProvider planProvider) : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStates()
    {
        var states = stateStore.GetAllStateInfos();
        return Ok(states);
    }

    [HttpPost("reset")]
    public IActionResult ResetAll()
    {
        stateStore.ResetAll();
        return Ok(new { message = "All circuits reset to Closed", count = stateStore.Count });
    }

    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        return Ok(planProvider.Current.CircuitBreakerByCluster);
    }
}
