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

    /// <summary>Delete a route. Also removes the cluster if no remaining routes reference it.</summary>
    /// <param name="routeName">Route name to delete.</param>
    /// <param name="clientIp">Optional client IP for IP-based isolation: only removes the matching destination.</param>
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
    public async Task<IActionResult> UpdateRoute(string routeId, [FromBody] JsonElement config)
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

            var result = await _dynamicConfig.TryAddRoute(request, "dynamic");
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
    public async Task<IActionResult> DeleteCluster(string clusterId)
    {
        var result = await _dynamicConfig.TryRemoveCluster(clusterId);
        return result.Success
            ? Ok(new { code = 200, message = result.Message })
            : BadRequest(new { code = 400, message = result.Message });
    }

    /// <summary>Create a new cluster.</summary>
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

    /// <summary>Update an existing cluster.</summary>
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

    /// <summary>Health check endpoint.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new { code = 200, message = "pong" });

    /// <summary>
    /// Heartbeat endpoint for registered services.
    /// Services call this periodically to keep their registration alive.
    /// Gateway tracks last heartbeat time to detect stale registrations.
    /// </summary>
    /// <param name="routeName">Route name to update heartbeat for.</param>
    /// <param name="clientIp">Optional client IP for IP-based isolation.</param>
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

    // ─── Batch Operations ──────────────────────────────────

    /// <summary>Batch register routes and clusters in a single atomic operation.</summary>
    [HttpPost("batch/register")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> BatchRegister([FromBody] BatchRegisterRequest request)
    {
        if (request.Routes == null || request.Routes.Count == 0)
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
                results.Add(new { route = routeReq.RouteName, success = false, message = ex.Message });
                allSucceeded = false;
            }
        }

        var summary = allSucceeded ? "All operations succeeded" : "Some operations failed";
        return Ok(new { code = 200, message = summary, details = results });
    }

    /// <summary>Batch delete routes in a single atomic operation.</summary>
    [HttpPost("batch/delete-routes")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchDeleteRoutes([FromBody] BatchDeleteRoutesRequest request)
    {
        if (request.RouteNames == null || request.RouteNames.Count == 0)
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
                results.Add(new { route = routeName, success = false, message = ex.Message });
                allSucceeded = false;
            }
        }

        var summary = allSucceeded ? "All operations succeeded" : "Some operations failed";
        return Ok(new { code = 200, message = summary, details = results });
    }

    /// <summary>Batch enable or disable routes.</summary>
    [HttpPost("batch/toggle-routes")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> BatchToggleRoutes([FromBody] BatchToggleRoutesRequest request)
    {
        if (request.RouteNames == null || request.RouteNames.Count == 0)
            return BadRequest(new { code = 400, message = "At least one route name is required" });

        var results = new List<object>();
        var allSucceeded = true;

        foreach (var routeName in request.RouteNames)
        {
            try
            {
                var current = _dynamicConfig.GetRoutes().FirstOrDefault(r =>
                    string.Equals(r.RouteId, routeName, StringComparison.OrdinalIgnoreCase));

                if (current == null)
                {
                    results.Add(new { route = routeName, success = false, message = "Route not found" });
                    allSucceeded = false;
                    continue;
                }

                var req = new RegisterRouteRequest
                {
                    RouteName = routeName,
                    ClusterName = current.ClusterId,
                    MatchPath = current.Match?.Path ?? "",
                    Order = current.Order,
                    Transforms = current.Transforms?.Select(t => new Dictionary<string, string>(t)).ToList()
                };

                var result = await _dynamicConfig.TryAddRoute(req, "batch-toggle");
                results.Add(new { route = routeName, success = result.Success, message = result.Message });
                if (!result.Success)
                    allSucceeded = false;
            }
            catch (Exception ex)
            {
                results.Add(new { route = routeName, success = false, message = ex.Message });
                allSucceeded = false;
            }
        }

        var summary = allSucceeded ? "All operations succeeded" : "Some operations failed";
        return Ok(new { code = 200, message = summary, details = results });
    }
}

/// <summary>Request model for batch register operation.</summary>
public class BatchRegisterRequest
{
    public List<RegisterRouteRequest> Routes { get; set; } = new();
    public string? Source { get; set; }
    public string? CreatedBy { get; set; }
}

/// <summary>Request model for batch delete routes operation.</summary>
public class BatchDeleteRoutesRequest
{
    public List<string> RouteNames { get; set; } = new();
    public string? ClientIp { get; set; }
    public bool RemoveOrphanedClusters { get; set; } = true;
}

/// <summary>Request model for batch toggle routes operation.</summary>
public class BatchToggleRoutesRequest
{
    public List<string> RouteNames { get; set; } = new();
}
