using Aneiang.Yarp.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Builds an immutable snapshot from the mutable <see cref="GatewayDynamicConfig"/> working set
/// and pushes it to the <see cref="AneiangProxyConfigProvider"/> for YARP hot-reload.
/// </summary>
internal interface IDynamicConfigPublisher
{
    /// <summary>
    /// Publish the given config as a snapshot to the proxy provider. Merge policy metadata into
    /// route metadata and normalize transforms (YARP-facing copy only).
    /// </summary>
    void Publish(GatewayDynamicConfig config, long version);

    /// <summary>
    /// Remove destinations with empty/null addresses from a single cluster, preserving every other
    /// native field via <c>with</c>.
    /// </summary>
    ClusterConfig SanitizeCluster(ClusterConfig cluster);
}
