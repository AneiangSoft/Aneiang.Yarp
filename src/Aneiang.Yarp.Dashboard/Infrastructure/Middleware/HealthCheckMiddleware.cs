using System.Diagnostics;
using System.Reflection;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Middleware;

/// <summary>
/// Serves liveness (/live), readiness (/ready), and combined (/health) endpoints for
/// container orchestrators (K8s) and load balancers.
/// Honors IP allow-list and optional shared-secret token. Skips non-health paths quickly.
/// </summary>
public class HealthCheckMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DeploymentOptions _options;
    private readonly IDynamicYarpConfigService _configService;
    private readonly ILogger<HealthCheckMiddleware> _logger;
    private readonly DateTime _processStart = Process.GetCurrentProcess().StartTime.ToUniversalTime();
    private readonly string _version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public HealthCheckMiddleware(
        RequestDelegate next,
        IOptions<DeploymentOptions> options,
        IDynamicYarpConfigService configService,
        ILogger<HealthCheckMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _configService = configService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.HealthCheck.Enabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        if (!IsHealthPath(path))
        {
            await _next(context);
            return;
        }

        // IP allow-list
        if (_options.HealthCheck.AllowedIps.Count > 0)
        {
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            if (!_options.HealthCheck.AllowedIps.Contains(clientIp))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        // Token authentication
        if (!string.IsNullOrEmpty(_options.HealthCheck.Token))
        {
            var token = context.Request.Headers["X-Health-Token"].FirstOrDefault()
                      ?? context.Request.Query["token"].FirstOrDefault();
            if (!string.Equals(token, _options.HealthCheck.Token, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        if (path.Equals(_options.HealthCheck.LivePath, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJson(context, 200, new
            {
                status = "alive",
                timestamp = DateTime.UtcNow,
                uptime = (DateTime.UtcNow - _processStart).TotalSeconds
            });
            return;
        }

        var (isReady, checks) = PerformReadinessCheck();

        if (path.Equals(_options.HealthCheck.ReadyPath, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJson(context, isReady ? 200 : 503, new
            {
                status = isReady ? "ready" : "not-ready",
                checks
            });
            return;
        }

        if (path.Equals(_options.HealthCheck.Path, StringComparison.OrdinalIgnoreCase))
        {
            await WriteJson(context, isReady ? 200 : 503, new
            {
                status = isReady ? "healthy" : "degraded",
                timestamp = DateTime.UtcNow,
                version = _version,
                endpoints = _options.ResolvedEndpoints.Select(e => new
                {
                    name = e.EndpointName,
                    port = e.Port,
                    role = e.Role,
                    @public = e.IsPubliclyBound
                }),
                uptime = (DateTime.UtcNow - _processStart).TotalSeconds,
                checks
            });
        }
    }

    private (bool IsReady, Dictionary<string, object> Checks) PerformReadinessCheck()
    {
        var checks = new Dictionary<string, object>();
        var isReady = true;

        if (_options.HealthCheck.CheckConfigLoaded)
        {
            try
            {
                var routes = _configService.GetRoutes();
                var loaded = routes != null;
                checks["config"] = new { status = loaded ? "ok" : "not-ready", routeCount = routes?.Count ?? 0 };
                isReady &= loaded;
            }
            catch (Exception ex)
            {
                checks["config"] = new { status = "fail", error = ex.Message };
                isReady = false;
            }
        }

        return (isReady, checks);
    }

    private bool IsHealthPath(string path) =>
        path.Equals(_options.HealthCheck.Path, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(_options.HealthCheck.ReadyPath, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(_options.HealthCheck.LivePath, StringComparison.OrdinalIgnoreCase);

    private static async Task WriteJson(HttpContext context, int status, object payload)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(payload);
    }
}
