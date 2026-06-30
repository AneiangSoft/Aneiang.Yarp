using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Immutable proxy configuration snapshot. Serves both YARP (via <see cref="Routes"/> / <see cref="Clusters"/>)
/// and the dashboard/middleware layer (via <see cref="DynamicRoutes"/> / <see cref="DynamicClusters"/>).
/// <para>
/// Invariant: <c>DynamicRoutes[i].Config</c> is the same reference as the matching entry in
/// <see cref="Routes"/>, and likewise for clusters. This guarantees YARP and the dashboard always
/// observe identical data with zero synchronization cost.
/// </para>
/// </summary>
internal sealed class AneiangProxyConfig : IProxyConfig
{
    /// <summary>Native YARP routes (consumed by the reverse proxy).</summary>
    public IReadOnlyList<RouteConfig> Routes { get; init; } = Array.Empty<RouteConfig>();

    /// <summary>Native YARP clusters (consumed by the reverse proxy).</summary>
    public IReadOnlyList<ClusterConfig> Clusters { get; init; } = Array.Empty<ClusterConfig>();

    /// <summary>Change token signalled when this snapshot is superseded.</summary>
    public IChangeToken ChangeToken { get; init; } = new CancellationChangeToken(CancellationToken.None);

    /// <summary>Route metadata records. Each <c>.Config</c> aliases the matching entry in <see cref="Routes"/>.</summary>
    public IReadOnlyList<Models.DynamicRouteConfig> DynamicRoutes { get; init; } = Array.Empty<Models.DynamicRouteConfig>();

    /// <summary>Cluster metadata records. Each <c>.Config</c> aliases the matching entry in <see cref="Clusters"/>.</summary>
    public IReadOnlyList<Models.DynamicClusterConfig> DynamicClusters { get; init; } = Array.Empty<Models.DynamicClusterConfig>();

    /// <summary>Monotonically increasing snapshot version.</summary>
    public long Version { get; init; }
}
