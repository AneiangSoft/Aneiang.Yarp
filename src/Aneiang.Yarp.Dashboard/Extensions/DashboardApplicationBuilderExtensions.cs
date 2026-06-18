using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Dashboard.Infrastructure.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Dashboard.Infrastructure.Yarp;
using Aneiang.Yarp.Dashboard.Modules.Waf.Middleware;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.Retry.Middleware;
using Aneiang.Yarp.Dashboard.Modules.RateLimit.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Extensions;

/// <summary>
/// IApplicationBuilder and IEndpointRouteBuilder extensions for Aneiang.Yarp.Dashboard.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    private const string DashboardConfiguredKey = "__AneiangYarpDashboard_Configured";

    /// <summary>
    /// Register Dashboard middleware and map the YARP proxy endpoint with core middleware
    /// in the correct pipeline order. When <see cref="DeploymentOptions.Mode"/> is set,
    /// this method intelligently mounts only the components needed for the current mode
    /// (AllInOne / Split / ProxyOnly / DashboardOnly).
    /// </summary>
    public static IApplicationBuilder UseAneiangYarpDashboard(
        this IApplicationBuilder app,
        Action<IReverseProxyApplicationBuilder>? configureProxyPipeline = null)
    {
        if (app.Properties.TryGetValue(DashboardConfiguredKey, out _))
        {
            return app;
        }
        app.Properties[DashboardConfiguredKey] = true;

        var mode = DeploymentMode.AllInOne;
        try
        {
            var options = app.ApplicationServices.GetService<IOptions<DeploymentOptions>>();
            if (options != null)
                mode = options.Value.Mode;
        }
        catch
        {
            // Fall back to AllInOne if DeploymentOptions is not registered (backward compat)
        }

        var dashboardActive = mode != DeploymentMode.ProxyOnly;
        var proxyActive = mode != DeploymentMode.DashboardOnly;

        UseDeploymentMiddlewareIfAvailable(app);

        if (dashboardActive)
        {
            app.UseResponseCompression();
            app.UseStaticFiles();
        }

        if (proxyActive)
        {
            app.UseMiddleware<YarpRequestCaptureMiddleware>();
        }

        app.UseMiddleware<WafMiddleware>();

        if (app is IEndpointRouteBuilder endpoints)
        {
            if (dashboardActive)
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

                endpoints.MapHub<TrafficHub>("/hubs/traffic");
            }

            if (proxyActive)
            {
                endpoints.MapReverseProxy(proxyPipeline =>
                {
                    proxyPipeline.UseMiddleware<BuiltinTransformMiddleware>();
                    proxyPipeline.UseMiddleware<RateLimitMiddleware>();
                    proxyPipeline.UseMiddleware<CircuitBreakerMiddleware>();
                    proxyPipeline.UseMiddleware<RequestRetryMiddleware>();
                    configureProxyPipeline?.Invoke(proxyPipeline);
                });
            }
        }

        return app;
    }

    private static void UseDeploymentMiddlewareIfAvailable(IApplicationBuilder app)
    {
        var services = app.ApplicationServices;
        var resolver = services.GetService<EndpointRoleResolver>();
        var deploymentOptions = services.GetService<IOptions<DeploymentOptions>>()?.Value;

        if (resolver != null && deploymentOptions != null && deploymentOptions.Mode != DeploymentMode.AllInOne)
        {
            app.UseMiddleware<EndpointRouterMiddleware>();
        }

        if (deploymentOptions?.HealthCheck.Enabled == true)
        {
            app.UseMiddleware<HealthCheckMiddleware>();
        }
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
