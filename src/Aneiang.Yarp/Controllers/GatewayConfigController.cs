using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Controllers
{
    /// <summary>
    /// Gateway configuration management API for dynamic YARP route registration.
    /// </summary>
    [Route("api/gateway")]
    [ApiController]
    [Produces("application/json")]
    public class GatewayConfigController : ControllerBase
    {
        private readonly DynamicYarpConfigService _dynamicConfig;

        /// <summary>
        /// Creates a new <see cref="GatewayConfigController"/>.
        /// </summary>
        public GatewayConfigController(DynamicYarpConfigService dynamicConfig)
        {
            _dynamicConfig = dynamicConfig;
        }

        /// <summary>
        /// Register or update a route. Creates the route and associated cluster if not exists, otherwise updates.
        /// </summary>
        [HttpPost("register-route")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public IActionResult RegisterRoute([FromBody] RegisterRouteRequest request)
        {
            // [ApiController] auto-handles model validation via data annotations.
            // Manual check kept for custom response format when SuppressModelStateInvalidFilter is enabled.
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage);
                return BadRequest(new { code = 400, info = string.Join("; ", errors) });
            }

            var (success, message) = _dynamicConfig.TryAddRoute(request);
            return Ok(new { code = 200, info = message });
        }

        /// <summary>
        /// Delete a route by name. If the associated cluster has no remaining routes, it will also be removed.
        /// </summary>
        [HttpDelete("{routeName}")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
        public IActionResult DeleteRoute(string routeName)
        {
            var (success, message) = _dynamicConfig.TryRemoveRoute(routeName);
            if (!success)
                return NotFound(new { code = 404, info = message });
            return Ok(new { code = 200, info = message });
        }

        /// <summary>
        /// Get all registered routes.
        /// </summary>
        [HttpGet("routes")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public IActionResult GetRoutes()
        {
            var routes = _dynamicConfig.GetRoutes();
            var data = routes.Select(r => new
            {
                routeId = r.RouteId,
                clusterId = r.ClusterId,
                path = r.Match?.Path,
                order = r.Order
            });
            return Ok(new { code = 200, data });
        }

        /// <summary>
        /// Health check / connectivity test.
        /// </summary>
        [HttpGet("ping")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        public IActionResult Ping()
        {
            return Ok(new { code = 200, info = "pong" });
        }
    }
}
