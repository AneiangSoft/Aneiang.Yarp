using Aneiang.Yarp.Dashboard.Middleware;
using Aneiang.Yarp.Dashboard.Services;
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// IApplicationBuilder and IEndpointRouteBuilder extensions for Aneiang.Yarp.Dashboard.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Register Dashboard middleware and map the YARP proxy endpoint with core middleware
    /// in the correct pipeline order.
    /// <para>
    /// This method sets up request capture middleware on the main pipeline and maps
    /// the YARP proxy branch with response caching and metrics middleware inside it
    /// (which require <c>IReverseProxyFeature</c>).
    /// </para>
    /// <para>
    /// <b>Important:</b> Do NOT call <c>MapReverseProxy()</c> separately — this method
    /// handles it internally. Usage pattern:
    /// </para>
    /// <code>
    /// app.UseRouting();
    /// app.UseAneiangYarpDashboard();
    /// app.MapControllers();
    /// </code>
    /// </summary>
    public static IApplicationBuilder UseAneiangYarpDashboard(
        this IApplicationBuilder app,
        Action<IReverseProxyApplicationBuilder>? configureProxyPipeline = null)
    {
        // Static files for Dashboard UI
        app.UseStaticFiles();

        // Request capture runs on the main pipeline (before endpoint routing)
        app.UseMiddleware<YarpRequestCaptureMiddleware>();

        // Map the YARP proxy branch WITH core + dashboard middleware inside it.
        if (app is IEndpointRouteBuilder endpoints)
        {
            endpoints.MapReverseProxy(proxyPipeline =>
            {
                var metricsOpts = app.ApplicationServices.GetService<IOptions<GatewayMetricsOptions>>()?.Value;
                var cacheOpts = app.ApplicationServices.GetService<IOptions<ResponseCacheOptions>>()?.Value;

                // Core gateway middleware inside the proxy branch
                proxyPipeline.UseAneiangYarp();

                // Dashboard-managed middleware (inside proxy branch for IReverseProxyFeature)
                if (cacheOpts?.Enabled == true)
                    proxyPipeline.UseMiddleware<ResponseCacheMiddleware>();

                if (metricsOpts?.Enabled == true)
                    proxyPipeline.UseMiddleware<MetricsMiddleware>();

                // Always register circuit breaker and retry middleware
                proxyPipeline.UseMiddleware<CircuitBreakerMiddleware>();
                proxyPipeline.UseMiddleware<RequestRetryMiddleware>();

                // Allow user customization of the proxy pipeline
                configureProxyPipeline?.Invoke(proxyPipeline);
            });
        }

        return app;
    }
}
