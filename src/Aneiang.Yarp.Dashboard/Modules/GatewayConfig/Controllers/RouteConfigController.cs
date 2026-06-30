using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers;

/// <summary>
/// Route CRUD operations — save, delete, rename.
/// </summary>
[Route("api/config")]
[ApiController]
public class RouteConfigController : ConfigControllerBase
{
    private readonly ILogger<RouteConfigController> _logger;
    private readonly IGatewayIdentityService _identityService;

    public RouteConfigController(
        IConfigPersistenceService persistenceService,
        IDynamicYarpConfigService dynamicConfig,
        ILogger<RouteConfigController> logger,
        IMemoryCache memoryCache,
        IGatewayIdentityService identityService,
        IConfigSnapshotScheduler snapshotScheduler,
        IOptionsMonitor<ConfigHistoryOptions> configHistoryOptions)
        : base(persistenceService, dynamicConfig, memoryCache, snapshotScheduler, configHistoryOptions)
    {
        _logger = logger;
        _identityService = identityService;
    }

    /// <summary>
    /// Save or update a single route from a full native YARP route config.
    /// </summary>
    [HttpPut("routes/{routeId}")]
    public async Task<IActionResult> SaveRoute(string routeId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Save route requested: {RouteId}", routeId);

            RouteConfig? route;
            try
            {
                route = Aneiang.Yarp.Serialization.YarpJsonConfig.DeserializeRoute(config);
            }
            catch (Exception parseEx)
            {
                return BadRequest(new { code = 400, message = "Invalid route configuration: " + parseEx.Message });
            }

            if (route == null)
                return BadRequest(new { code = 400, message = "Route configuration is required" });

            route = route with { RouteId = routeId };

            if (string.IsNullOrWhiteSpace(route.ClusterId))
                return BadRequest(new { code = 400, message = "ClusterId is required" });

            if (route.Match == null || (string.IsNullOrWhiteSpace(route.Match.Path) && (route.Match.Hosts == null || route.Match.Hosts.Count == 0)))
                return BadRequest(new { code = 400, message = "Match.Path or Match.Hosts is required" });

            await EnsureClusterForRouteAsync(route.ClusterId!, config);

            await SnapshotLowRiskMutationAsync($"After route '{routeId}' saved via dashboard");

            var result = await DynamicConfig.TryAddRouteConfig(route, "dashboard", "dashboard-user");

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = routeId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Save failed", ex) });
        }
    }

    /// <summary>
    /// Delete a route.
    /// </summary>
    [HttpDelete("routes/{routeId}")]
    public async Task<IActionResult> DeleteRoute(string routeId, [FromQuery] bool removeOrphanedCluster = false)
    {
        try
        {
            _logger.LogInformation("Delete route requested: {RouteId}", routeId);

            await PersistenceService.SaveSnapshotAsync($"Before route '{routeId}' deleted via dashboard", GetClientIp());

            var result = await DynamicConfig.TryRemoveRoute(routeId, null, removeOrphanedCluster);

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = routeId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Delete failed", ex) });
        }
    }

    /// <summary>
    /// Rename a route atomically.
    /// </summary>
    [HttpPut("routes/{routeId}/rename")]
    public async Task<IActionResult> RenameRoute(string routeId, [FromBody] JsonElement config)
    {
        try
        {
            _logger.LogInformation("Rename route requested: {OldRouteId}", routeId);

            string? newRouteId = null;
            if (config.TryGetProperty("newRouteId", out var newIdProp))
                newRouteId = newIdProp.GetString();
            else if (config.TryGetProperty("routeId", out var ridProp))
                newRouteId = ridProp.GetString();

            if (string.IsNullOrWhiteSpace(newRouteId))
                return BadRequest(new { code = 400, message = "newRouteId is required" });

            if (string.Equals(routeId, newRouteId, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { code = 400, message = "New route ID is the same as the old one" });

            string? matchPath = null;
            string? clusterId = null;
            int? order = null;
            List<Dictionary<string, string>>? transforms = null;

            if (config.TryGetProperty("matchPath", out var mp) || config.TryGetProperty("MatchPath", out mp))
                matchPath = mp.GetString();
            if (config.TryGetProperty("clusterId", out var ci) || config.TryGetProperty("ClusterId", out ci))
                clusterId = ci.GetString();
            if (config.TryGetProperty("order", out var o) || config.TryGetProperty("Order", out o))
                order = o.GetInt32();
            if (config.TryGetProperty("transforms", out var t) || config.TryGetProperty("Transforms", out t))
                transforms = t.Deserialize<List<Dictionary<string, string>>>();

            var request = new RegisterRouteRequest
            {
                RouteName = newRouteId,
                ClusterName = clusterId ?? string.Empty,
                MatchPath = matchPath ?? string.Empty,
                Order = order,
                Transforms = transforms
            };

            var result = await _identityService.RenameRouteAsync(
                routeId, newRouteId, request,
                clientIp: GetClientIp(), operatorName: "dashboard-user",
                ct: HttpContext.RequestAborted);

            if (result.Success) InvalidateQueryCaches();
            return result.Success
                ? Ok(new { code = 200, message = result.Message, routeId = newRouteId })
                : BadRequest(new { code = 400, message = result.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename route: {RouteId}", routeId);
            return StatusCode(500, new { code = 500, message = SafeErrorMessages.Create(HttpContext, "Rename failed", ex) });
        }
    }
}
