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

    /// <summary>Controls which built-in middleware is automatically mounted by <see cref="UseAneiangYarpDashboard(IApplicationBuilder, DashboardUseOptions?, Action{IReverseProxyApplicationBuilder}?)"/>.</summary>
    public sealed class DashboardUseOptions
    {
        /// <summary>Override <see cref="DeploymentOptions.AutoUseMiddleware"/>. Null means use deployment configuration.</summary>
        public bool? AutoUseMiddleware { get; set; }

        /// <summary>Mount deployment endpoint-router and health-check middleware when auto-use is enabled.</summary>
        public bool UseDeploymentMiddleware { get; set; } = true;

        /// <summary>Mount proxy request/response capture middleware when auto-use is enabled.</summary>
        public bool UseProxyRequestCapture { get; set; } = true;

        /// <summary>Mount WAF middleware when auto-use is enabled.</summary>
        public bool UseWaf { get; set; } = true;

        /// <summary>Mount built-in proxy branch middleware when auto-use is enabled.</summary>
        public bool UseBuiltInProxyPipeline { get; set; } = true;

        /// <summary>
        /// Automatically register a default permissive CORS middleware (AllowAnyOrigin/Method/Header).
        /// Set to <c>false</c> and call <c>app.UseCors(...)</c> yourself before <c>app.UseAneiangYarpDashboard(...)</c>
        /// to use a custom CORS policy.
        /// </summary>
        public bool AutoUseCors { get; set; } = true;

        /// <summary>
        /// Automatically register authorization middleware (<c>UseAuthorization()</c>).
        /// Required for YARP routes with non-<c>Anonymous</c> <c>AuthorizationPolicy</c>.
        /// Set to <c>false</c> if you call <c>app.UseAuthorization()</c> yourself.
        /// Call <c>app.UseAuthentication()</c> before <c>UseAneiangYarpDashboard()</c> if your
        /// auth policies require authenticated users.
        /// </summary>
        public bool AutoUseAuthorization { get; set; } = true;
    }

    /// <summary>
    /// Register Dashboard middleware and map the YARP proxy endpoint with core middleware
    /// in the correct pipeline order. When <see cref="DeploymentOptions.Mode"/> is set,
    /// this method intelligently mounts only the components needed for the current mode
    /// (AllInOne / Split / ProxyOnly / DashboardOnly).
    /// </summary>
    public static IApplicationBuilder UseAneiangYarpDashboard(
        this IApplicationBuilder app,
        Action<IReverseProxyApplicationBuilder>? configureProxyPipeline = null)
        => app.UseAneiangYarpDashboard(useOptions: null, configureProxyPipeline);

    /// <summary>
    /// Register Dashboard middleware with explicit opt-out controls for advanced custom pipelines.
    /// </summary>
    public static IApplicationBuilder UseAneiangYarpDashboard(
        this IApplicationBuilder app,
        DashboardUseOptions? useOptions,
        Action<IReverseProxyApplicationBuilder>? configureProxyPipeline = null)
    {
        if (app.Properties.TryGetValue(DashboardConfiguredKey, out _))
        {
            return app;
        }
        app.Properties[DashboardConfiguredKey] = true;

        useOptions ??= new DashboardUseOptions();
        var mode = DeploymentMode.AllInOne;
        var autoUseMiddleware = true;
        try
        {
            var options = app.ApplicationServices.GetService<IOptions<DeploymentOptions>>();
            if (options != null)
            {
                mode = options.Value.Mode;
                autoUseMiddleware = options.Value.AutoUseMiddleware;
            }
        }
        catch
        {
            // Fall back to AllInOne if DeploymentOptions is not registered (backward compat)
        }

        autoUseMiddleware = useOptions.AutoUseMiddleware ?? autoUseMiddleware;

        var dashboardActive = mode != DeploymentMode.ProxyOnly;
        var proxyActive = mode != DeploymentMode.DashboardOnly;

        // CORS middleware must appear after UseRouting() but before endpoint execution.
        // YARP routes may carry CorsPolicy metadata (set via appsettings or Dashboard),
        // which requires an active CORS middleware in the pipeline.
        // Set AutoUseCors = false and call app.UseCors(...) yourself for a custom policy.
        if (useOptions.AutoUseCors)
        {
            app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        }

        // Authorization middleware — same rationale as CORS above.
        // YARP routes may carry AuthorizationPolicy metadata; without UseAuthorization()
        // in the pipeline, ASP.NET Core will throw InvalidOperationException at runtime.
        // Set AutoUseAuthorization = false if you already call UseAuthorization() before
        // UseAneiangYarpDashboard(), or if you need UseAuthentication() before it.
        if (useOptions.AutoUseAuthorization)
        {
            app.UseAuthorization();
        }

        if (autoUseMiddleware && useOptions.UseDeploymentMiddleware)
        {
            UseDeploymentMiddlewareIfAvailable(app);
        }

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
                    if (autoUseMiddleware && useOptions.UseBuiltInProxyPipeline)
                    {
                        proxyPipeline.UseMiddleware<BuiltinTransformMiddleware>();
                        proxyPipeline.UseMiddleware<RateLimitMiddleware>();
                        proxyPipeline.UseMiddleware<CircuitBreakerMiddleware>();
                        proxyPipeline.UseMiddleware<RequestRetryMiddleware>();
                    }
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
