using System.Text.Json;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Controllers;

[Route("api/gateway")]
[ApiController]
[Produces("application/json")]
public class GatewayConfigController : ControllerBase
{
    private readonly DynamicYarpConfigService _dynamicConfig;
    private readonly ILogger<GatewayConfigController> _logger;

    public GatewayConfigController(DynamicYarpConfigService dynamicConfig, ILogger<GatewayConfigController> logger)
    {
        _dynamicConfig = dynamicConfig;
        _logger = logger;
    }

    [HttpPost("register-route")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterRoute([FromBody] RegisterRouteRequest request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(new { code = 400, message = string.Join("; ", errors) });
        }

        var result = await _dynamicConfig.TryAddRoute(request);
        return Ok(new { code = 200, message = result.Message });
    }

    [HttpDelete("{routeName}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRoute(string routeName, [FromQuery] string? clientIp = null)
    {
        var result = await _dynamicConfig.TryRemoveRoute(routeName, clientIp);
        return result.Success
            ? Ok(new { code = 200, message = result.Message })
            : NotFound(new { code = 404, message = result.Message });
    }

    [HttpGet("routes")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetRoutes()
    {
        var data = _dynamicConfig.GetRoutes().Select(r => new
        {
            routeId = r.RouteId,
            clusterId = r.ClusterId,
            path = r.Match.Path,
            order = r.Order
        });
        return Ok(new { code = 200, data });
    }

    [HttpGet("dynamic-config")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetDynamicConfig()
    {
        var config = _dynamicConfig.GetDynamicConfig();
        return Ok(new { code = 200, data = config });
    }

    [HttpPut("routes/{routeId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRoute(string routeId, [FromBody] JsonElement config)
    {
        try
        {
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
                Order = config.TryGetProperty("order", out var orderProp) ? orderProp.GetInt32() : int.MaxValue,
                Transforms = config.TryGetProperty("transforms", out var transformsProp)
                    ? transformsProp.Deserialize<List<Dictionary<string, string>>>()
                    : null
            };

            var result = await _dynamicConfig.TryAddRoute(request);
            return result.Success
                ? Ok(new { code = 200, message = result.Message })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update route {RouteId}: invalid JSON", routeId);
            return BadRequest(new { code = 400, message = $"Invalid JSON: {ex.Message}" });
        }
    }

    [HttpDelete("clusters/{clusterId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteCluster(string clusterId)
    {
        var result = await _dynamicConfig.TryRemoveCluster(clusterId);
        return result.Success
            ? Ok(new { code = 200, message = result.Message })
            : BadRequest(new { code = 400, message = result.Message });
    }

    [HttpPost("clusters")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCluster([FromBody] CreateClusterRequest request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(new { code = 400, message = string.Join("; ", errors) });
        }

        var result = await _dynamicConfig.TryAddCluster(request);
        return result.Success
            ? Ok(new { code = 200, message = result.Message })
            : BadRequest(new { code = 400, message = result.Message });
    }

    [HttpPut("clusters/{clusterId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCluster(string clusterId, [FromBody] UpdateClusterRequest request)
    {
        var result = await _dynamicConfig.TryUpdateCluster(clusterId, request);
        return result.Success
            ? Ok(new { code = 200, message = result.Message })
            : (result.Message.Contains("not found")
                ? NotFound(new { code = 404, message = result.Message })
                : BadRequest(new { code = 400, message = result.Message }));
    }

    [HttpGet("ping")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new { code = 200, message = "pong" });

    [HttpPost("{routeName}/heartbeat")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public IActionResult Heartbeat(string routeName, [FromQuery] string? clientIp = null)
    {
        var updated = _dynamicConfig.UpdateHeartbeat(routeName, clientIp);
        if (!updated)
            return NotFound(new { code = 404, message = $"Route '{routeName}' not found" });

        return Ok(new { code = 200, message = "heartbeat" });
    }

    #region Batch Operations

    [HttpPost("batch/register")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BatchRegister([FromBody] BatchRegisterRequest request)
    {
        if (request.Routes.Count == 0)
            return BadRequest(new { code = 400, message = "At least one route is required" });

        var results = new List<object>();
        var allSucceeded = true;

        foreach (var routeReq in request.Routes)
        {
            try
            {
                var result = await _dynamicConfig.TryAddRoute(routeReq, request.Source ?? "batch", request.CreatedBy);
                results.Add(new { route = routeReq.RouteName, success = result.Success, message = result.Message });
                if (!result.Success)
                    allSucceeded = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch register failed for route {RouteName}", routeReq.RouteName);
                results.Add(new { route = routeReq.RouteName, success = false, message = SafeErrorMessages.Create(HttpContext, "Batch route registration failed", ex) });
                allSucceeded = false;
            }
        }

        var summary = allSucceeded ? "All operations succeeded" : "Some operations failed";
        return Ok(new { code = 200, message = summary, details = results });
    }

    [HttpPost("batch/delete-routes")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchDeleteRoutes([FromBody] BatchDeleteRoutesRequest request)
    {
        if (request.RouteNames.Count == 0)
            return BadRequest(new { code = 400, message = "At least one route name is required" });

        var results = new List<object>();
        var allSucceeded = true;

        foreach (var routeName in request.RouteNames)
        {
            try
            {
                var result = await _dynamicConfig.TryRemoveRoute(routeName, request.ClientIp, request.RemoveOrphanedClusters);
                results.Add(new { route = routeName, success = result.Success, message = result.Message });
                if (!result.Success)
                    allSucceeded = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch delete failed for route {RouteName}", routeName);
                results.Add(new { route = routeName, success = false, message = ex.Message });
                allSucceeded = false;
            }
        }

        var summary = allSucceeded ? "All operations succeeded" : "Some operations failed";
        return Ok(new { code = 200, message = summary, details = results });
    }

    #endregion
}
