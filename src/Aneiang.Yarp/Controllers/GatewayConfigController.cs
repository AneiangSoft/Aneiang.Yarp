using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Controllers;

/// <summary>Gateway config API: dynamic route registration, deletion, and query.</summary>
[Route("api/gateway")]
[ApiController]
[Produces("application/json")]
public class GatewayConfigController : ControllerBase
{
    private readonly DynamicYarpConfigService _dynamicConfig;

    /// <summary>Creates a new instance of the controller.</summary>
    public GatewayConfigController(DynamicYarpConfigService dynamicConfig)
        => _dynamicConfig = dynamicConfig;

    /// <summary>Register or update a route and its cluster.</summary>
    [HttpPost("register-route")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public IActionResult RegisterRoute([FromBody] RegisterRouteRequest request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(new { code = 400, message = string.Join("; ", errors) });
        }

        var result = _dynamicConfig.TryAddRoute(request);
        return Ok(new { code = 200, message = result.Message });
    }

    /// <summary>Delete a route. Also removes the cluster if no remaining routes reference it.</summary>
    [HttpDelete("{routeName}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public IActionResult DeleteRoute(string routeName)
    {
        var result = _dynamicConfig.TryRemoveRoute(routeName);
        return result.Success
            ? Ok(new { code = 200, message = result.Message })
            : NotFound(new { code = 404, message = result.Message });
    }

    /// <summary>Get all registered routes.</summary>
    [HttpGet("routes")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetRoutes()
    {
        var data = _dynamicConfig.GetRoutes().Select(r => new
        {
            routeId = r.RouteId,
            clusterId = r.ClusterId,
            path = r.Match?.Path,
            order = r.Order
        });
        return Ok(new { code = 200, data });
    }

    /// <summary>Get dynamic configuration (routes and clusters with metadata).</summary>
    [HttpGet("dynamic-config")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetDynamicConfig()
    {
        var config = _dynamicConfig.GetDynamicConfig();
        return Ok(new { code = 200, data = config });
    }

    /// <summary>Update a route's configuration (JSON format).</summary>
    [HttpPut("routes/{routeId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public IActionResult UpdateRoute(string routeId, [FromBody] JsonElement config)
    {
        try
        {
            // Validate required fields
            if (!config.TryGetProperty("clusterId", out var clusterIdProp) ||
                !config.TryGetProperty("matchPath", out var matchPathProp))
            {
                return BadRequest(new { code = 400, message = "clusterId and matchPath are required" });
            }

            var request = new RegisterRouteRequest
            {
                RouteName = routeId,
                ClusterName = clusterIdProp.GetString() ?? string.Empty,
                MatchPath = matchPathProp.GetString() ?? string.Empty,
                Order = config.TryGetProperty("order", out var orderProp) ? orderProp.GetInt32() : 50,
                Transforms = config.TryGetProperty("transforms", out var transformsProp)
                    ? transformsProp.Deserialize<List<Dictionary<string, string>>>()
                    : null
            };

            var result = _dynamicConfig.TryAddRoute(request, "dynamic");
            return result.Success
                ? Ok(new { code = 200, message = result.Message })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { code = 400, message = $"Invalid JSON: {ex.Message}" });
        }
    }

    /// <summary>Delete a cluster (if no routes reference it).</summary>
    [HttpDelete("clusters/{clusterId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public IActionResult DeleteCluster(string clusterId)
    {
        var result = _dynamicConfig.TryRemoveCluster(clusterId);
        return result.Success
            ? Ok(new { code = 200, message = result.Message })
            : BadRequest(new { code = 400, message = result.Message });
    }

    /// <summary>Health check endpoint.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new { code = 200, message = "pong" });
}
