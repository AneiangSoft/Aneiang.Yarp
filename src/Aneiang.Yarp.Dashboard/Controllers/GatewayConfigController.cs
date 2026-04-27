using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Controllers
{
    /// <summary>
    /// 网关配置管理接口（动态 YARP 路由注册）
    /// </summary>
    [Route("api/gateway")]
    [ApiController]
    public class GatewayConfigController : ControllerBase
    {
        private readonly DynamicYarpConfigService _dynamicConfig;

        /// <summary>
        /// 创建网关配置控制器
        /// </summary>
        public GatewayConfigController(DynamicYarpConfigService dynamicConfig)
        {
            _dynamicConfig = dynamicConfig;
        }

        /// <summary>
        /// 注册或更新路由：增量添加 YARP 转发配置
        /// 如果路由名已存在则更新配置，否则自动新增路由和集群
        /// </summary>
        [HttpPost("register-route")]
        public IActionResult RegisterRoute([FromBody] RegisterRouteRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.RouteName))
                return BadRequest(new { code = 400, info = "路由名称不能为空" });
            if (string.IsNullOrWhiteSpace(request.ClusterName))
                return BadRequest(new { code = 400, info = "集群名称不能为空" });
            if (string.IsNullOrWhiteSpace(request.MatchPath))
                return BadRequest(new { code = 400, info = "匹配路径不能为空" });
            if (string.IsNullOrWhiteSpace(request.DestinationAddress))
                return BadRequest(new { code = 400, info = "目标地址不能为空" });

            var (success, message) = _dynamicConfig.TryAddRoute(request);

            return Ok(new { code = 200, info = message });
        }

        /// <summary>
        /// 健康检查 / 连通性测试
        /// </summary>
        [HttpGet("ping")]
        public IActionResult Ping()
        {
            return Ok(new { code = 200, info = "pong" });
        }
    }
}
