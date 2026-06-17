using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Deployment;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Middleware;

/// <summary>
/// Routes incoming requests based on the local port they arrived on.
/// Uses <see cref="EndpointRoleResolver"/> to determine the role of each port.
/// When only one endpoint is configured, acts as a pass-through (AllInOne).
/// When multiple endpoints are configured, enforces role-based routing.
/// </summary>
public class EndpointRouterMiddleware
{
    private readonly RequestDelegate _next;
    private readonly EndpointRoleResolver _resolver;
    private readonly DeploymentOptions _options;
    private readonly ILogger<EndpointRouterMiddleware> _logger;
    private readonly string _dashPrefix;

    private const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";
    private const string SignalRPrefix = "/hubs/";

    public EndpointRouterMiddleware(
        RequestDelegate next,
        EndpointRoleResolver resolver,
        IOptions<DeploymentOptions> options,
        ILogger<EndpointRouterMiddleware> logger,
        IOptions<DashboardOptions> dashOptions)
    {
        _next = next;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
        _dashPrefix = "/" + dashOptions.Value.RoutePrefix.Trim('/');
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Use the resolver's endpoint count as the authoritative signal.
        // When only one endpoint exists, all traffic is AllInOne (pass-through).
        // When 2+ endpoints exist, enforce role-based routing.
        var allEndpoints = _resolver.GetAll().ToList();
        if (allEndpoints.Count <= 1)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";

        // ProxyOnly / DashboardOnly modes
        if (_options.Mode == DeploymentMode.ProxyOnly)
        {
            if (IsDashboardPath(path)) { await Reject(context); return; }
            await _next(context);
            return;
        }
        if (_options.Mode == DeploymentMode.DashboardOnly)
        {
            if (!IsDashboardPath(path)) { await Reject(context); return; }
            await _next(context);
            return;
        }

        // Split mode: route by LocalPort + Role
        var localPort = context.Connection.LocalPort;
        var roleMap = _resolver.GetByPort(localPort);
        if (roleMap == null)
        {
            _logger.LogDebug("Rejecting {Path} on port {Port}: no role mapping found", path, localPort);
            await Reject(context);
            return;
        }

        bool allowed = roleMap.Role switch
        {
            "All"       => true,
            "Health"    => IsHealthPath(path),
            "Admin"     => path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase),
            "Dashboard" => IsDashboardPath(path),
            "Proxy"     => !IsDashboardPath(path),
            _           => true
        };

        if (!allowed)
        {
            _logger.LogDebug("Rejecting {Path} on port {Port} (role={Role})", path, localPort, roleMap.Role);
            await Reject(context);
            return;
        }

        await _next(context);
    }

    private bool IsDashboardPath(string path) =>
        path.StartsWith(_dashPrefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(SignalRPrefix, StringComparison.OrdinalIgnoreCase);

    private bool IsHealthPath(string path) =>
        path.Equals(_options.HealthCheck.Path, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(_options.HealthCheck.ReadyPath, StringComparison.OrdinalIgnoreCase) ||
        path.Equals(_options.HealthCheck.LivePath, StringComparison.OrdinalIgnoreCase);

    private static async Task Reject(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Not Found",
            message = "The requested resource is not available on this endpoint."
        });
    }
}
