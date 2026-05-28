using Aneiang.Yarp.Dashboard.Services;
using Aneiang.Yarp.Extensions;
using Aneiang.Yarp.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
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
    /// <param name="app">The IApplicationBuilder.</param>
    /// <param name="configureProxyPipeline">
    /// Optional callback to customize the YARP proxy branch pipeline 
    /// (e.g., add custom middleware inside the proxy branch).
    /// </param>
    /// <returns>The IApplicationBuilder for chaining.</returns>
    public static IApplicationBuilder UseAneiangYarpDashboard(
        this IApplicationBuilder app,
        Action<IReverseProxyApplicationBuilder>? configureProxyPipeline = null)
    {
        // Static files for Dashboard UI
        app.UseStaticFiles();

        // Request capture runs on the main pipeline (before endpoint routing)
        // so it can record the original incoming request path/method.
        app.UseMiddleware<YarpRequestCaptureMiddleware>();

        // Map the YARP proxy branch WITH core middleware inside it.
        // This ensures IReverseProxyFeature is available for MetricsMiddleware
        // and ResponseCacheMiddleware.
        if (app is IEndpointRouteBuilder endpoints)
        {
            endpoints.MapReverseProxy(proxyPipeline =>
            {
                // Core gateway middleware inside the proxy branch
                proxyPipeline.UseAneiangYarp();

                // Allow user customization of the proxy pipeline
                configureProxyPipeline?.Invoke(proxyPipeline);
            });
        }

        return app;
    }
}
