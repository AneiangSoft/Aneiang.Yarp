using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Dashboard.Infrastructure.Yarp;
using Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Retry.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// IApplicationBuilder and IEndpointRouteBuilder extensions for Aneiang.Yarp.Dashboard.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    private const string DashboardConfiguredKey = "__AneiangYarpDashboard_Configured";

    /// <summary>
    /// Register Dashboard middleware and map the YARP proxy endpoint with core middleware
    /// in the correct pipeline order.
    /// <para>
    /// This method sets up request capture middleware on the main pipeline and maps
    /// the YARP proxy branch with core middleware inside it
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
        // Guard: prevent double registration that would cause duplicate
        // middleware, duplicate MapReverseProxy, and double counting of requests.
        if (app.Properties.TryGetValue(DashboardConfiguredKey, out _))
        {
            return app;
        }
        app.Properties[DashboardConfiguredKey] = true;

        // Response compression must be FIRST — before any middleware that reads/writes the body.
        // This compresses static files, API JSON responses, and Razor HTML output.
        app.UseResponseCompression();

        app.UseStaticFiles();

        // Request capture runs on the main pipeline (before endpoint routing)
        app.UseMiddleware<YarpRequestCaptureMiddleware>();

        // WAF middleware runs on the main pipeline before proxy routing
        app.UseMiddleware<WafMiddleware>();

        if (app is IEndpointRouteBuilder endpoints)
        {
            endpoints.Map("/_content/Aneiang.Yarp.Dashboard/{**path}", async context =>
            {
                var path = context.Request.RouteValues["path"]?.ToString() ?? "";
                var filePath = $"_content/Aneiang.Yarp.Dashboard/{path}";

                var fileInfo = context.RequestServices
                    .GetRequiredService<IWebHostEnvironment>()
                    .WebRootFileProvider
                    .GetFileInfo(filePath);

                if (fileInfo.Exists)
                {
                    context.Response.ContentType = GetContentType(filePath);
                    await context.Response.SendFileAsync(fileInfo);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            });

            // Real-time traffic hub for topology page
            endpoints.MapHub<TrafficHub>("/hubs/traffic");

            endpoints.MapReverseProxy(proxyPipeline =>
            {
                proxyPipeline.UseMiddleware<BuiltinTransformMiddleware>();
                proxyPipeline.UseMiddleware<CircuitBreakerMiddleware>();
                proxyPipeline.UseMiddleware<RequestRetryMiddleware>();
                configureProxyPipeline?.Invoke(proxyPipeline);
            });
        }

        return app;
    }

    private static string GetContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".js" => "application/javascript",
            ".css" => "text/css",
            ".html" => "text/html",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".eot" => "application/vnd.ms-fontobject",
            _ => "application/octet-stream"
        };
    }
}
