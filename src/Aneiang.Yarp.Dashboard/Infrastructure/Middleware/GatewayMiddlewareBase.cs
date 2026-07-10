using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Middleware;

/// <summary>
/// Base class for gateway middleware that share common patterns:
/// dashboard path skip detection, plugin enable check, and cluster UID resolution.
/// </summary>
public abstract class GatewayMiddlewareBase
{
    protected readonly RequestDelegate Next;
    protected string DashPrefix { get; set; }

    /// <summary>Content root path for the Dashboard static files.</summary>
    protected const string ContentRoot = "/_content/Aneiang.Yarp.Dashboard";

    private readonly IGatewayPluginManager? _pluginManager;
    private readonly IDynamicYarpConfigService? _yarpConfig;

    protected GatewayMiddlewareBase(
        RequestDelegate next,
        IOptions<DashboardOptions> dashOptions,
        IGatewayPluginManager? pluginManager = null,
        IDynamicYarpConfigService? yarpConfig = null)
    {
        Next = next;
        DashPrefix = "/" + dashOptions.Value.RoutePrefix.Trim('/');
        _pluginManager = pluginManager;
        _yarpConfig = yarpConfig;
    }

    /// <summary>
    /// Returns <c>true</c> if the request targets the Dashboard UI or its static content.
    /// </summary>
    protected bool IsDashboardRequest(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        return path.StartsWith(DashPrefix, StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(ContentRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> if the named plugin is currently enabled.
    /// Returns <c>true</c> when no plugin manager was provided (pass-through).
    /// </summary>
    protected bool IsPluginEnabled(string pluginName)
        => _pluginManager?.IsPluginEnabled(pluginName) ?? true;

    /// <summary>
    /// Resolves the stable cluster UID from the dynamic YARP configuration.
    /// Returns <c>null</c> when no config service was provided or the cluster is not found.
    /// </summary>
    protected string? ResolveClusterUid(string clusterId)
        => _yarpConfig?.GetDynamicConfig()?.Clusters.FirstOrDefault(c =>
            string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))
            ?.ClusterUid;
}
