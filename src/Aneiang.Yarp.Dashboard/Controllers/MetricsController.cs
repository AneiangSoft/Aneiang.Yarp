using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Controllers;

/// <summary>
/// API controller for Prometheus metrics exposition endpoint.
/// Only active when <c>EnableMetrics</c> is true in <c>DashboardOptions</c>.
/// </summary>
[ApiController]
public class MetricsController : ControllerBase
{
    private readonly IGatewayMetricsService _metricsService;
    private readonly DashboardOptions _dashboardOptions;

    public MetricsController(
        IGatewayMetricsService metricsService,
        IOptions<DashboardOptions> dashboardOptions)
    {
        _metricsService = metricsService;
        _dashboardOptions = dashboardOptions.Value;
    }

    /// <summary>
    /// Expose Prometheus-format metrics.
    /// Route: <c>/apigateway/api/metrics</c>
    /// </summary>
    [HttpGet]
    [Route("api/metrics")]
    public IActionResult GetMetrics()
    {
        if (!_dashboardOptions.EnableMetrics)
        {
            return NotFound(new { code = 404, message = "Metrics endpoint is not enabled. Set Gateway:Dashboard:EnableMetrics to true." });
        }

        var text = _metricsService.GetPrometheusText();
        return Content(text, "text/plain; version=0.0.4; charset=utf-8");
    }
}
