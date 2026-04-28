using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Controllers;

/// <summary>Gateway config API: dynamic route register/delete/query / 网关配置管理 API：路由动态注册、删除、查询.</summary>
[Route("api/gateway")]
[ApiController]
[Produces("application/json")]
public class GatewayConfigController : ControllerBase
{
    private readonly DynamicYarpConfigService _dynamicConfig;

    /// <summary>Creates the controller / 构造函数.</summary>
    public GatewayConfigController(DynamicYarpConfigService dynamicConfig)
        => _dynamicConfig = dynamicConfig;

    /// <summary>Register or update a route and cluster / 注册或更新路由及集群.</summary>
    [HttpPost("register-route")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public IActionResult RegisterRoute([FromBody] RegisterRouteRequest request)
    {
        if (!ModelState.IsValid)
        {
            var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
            return BadRequest(new { code = 400, info = string.Join("; ", errors) });
        }

        var (success, message) = _dynamicConfig.TryAddRoute(request);
        return Ok(new { code = 200, info = message });
    }

    /// <summary>Delete a route (and orphaned cluster) / 删除路由（及无引用的集群）.</summary>
    [HttpDelete("{routeName}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public IActionResult DeleteRoute(string routeName)
    {
        var (success, message) = _dynamicConfig.TryRemoveRoute(routeName);
        return success
            ? Ok(new { code = 200, info = message })
            : NotFound(new { code = 404, info = message });
    }

    /// <summary>Get all registered routes / 获取所有已注册路由.</summary>
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

    /// <summary>Health check / 健康检查.</summary>
    [HttpGet("ping")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Ping() => Ok(new { code = 200, info = "pong" });
}
