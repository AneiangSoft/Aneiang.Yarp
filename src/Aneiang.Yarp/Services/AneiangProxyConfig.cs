using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal sealed class AneiangProxyConfig : IProxyConfig
{
    public IReadOnlyList<RouteConfig> Routes { get; init; } = Array.Empty<RouteConfig>();

    public IReadOnlyList<ClusterConfig> Clusters { get; init; } = Array.Empty<ClusterConfig>();

    public IChangeToken ChangeToken { get; init; } = new CancellationChangeToken(CancellationToken.None);

    public IReadOnlyList<Models.DynamicRouteConfig> DynamicRoutes { get; init; } = Array.Empty<Models.DynamicRouteConfig>();

    public IReadOnlyList<Models.DynamicClusterConfig> DynamicClusters { get; init; } = Array.Empty<Models.DynamicClusterConfig>();

    public long Version { get; init; }
}
