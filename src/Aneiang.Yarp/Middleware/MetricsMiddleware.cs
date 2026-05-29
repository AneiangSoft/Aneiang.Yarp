using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Middleware;

/// <summary>
/// Prometheus metrics middleware for YARP proxy requests.
/// Captures request duration, counts, and active requests gauge.
/// Reads route/cluster info from IReverseProxyFeature.
/// </summary>
public sealed class MetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly GatewayMetricsService _metricsService;
    private readonly GatewayMetricsOptions _options;

    public MetricsMiddleware(
        RequestDelegate next,
        GatewayMetricsService metricsService,
        IOptions<GatewayMetricsOptions> options)
    {
        _next = next;
        _metricsService = metricsService;
        _options = options.Value;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var proxyFeature = context.Features.Get<IReverseProxyFeature>();
        var routeId = proxyFeature?.Route?.Config?.RouteId ?? "unknown";
        var clusterId = proxyFeature?.Route?.Config?.ClusterId ?? "unknown";

        var sw = Stopwatch.StartNew();

        _metricsService.IncrementActiveRequests(routeId, clusterId);
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            var statusCode = context.Response.StatusCode;
            var method = context.Request.Method;

            _metricsService.DecrementActiveRequests(routeId, clusterId);
            _metricsService.RecordRequest(routeId, clusterId, method, statusCode, sw.Elapsed.TotalMilliseconds);
        }
    }
}
