using Aneiang.Yarp.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

/// <summary>
/// Shared mutable state for the dynamic config pipeline. Owned by <see cref="DynamicYarpConfigService"/>
/// and shared with the Route/Cluster config managers.
/// Not thread-safe on its own — callers must hold <see cref="SemaphoreSlim"/>.
/// </summary>
internal class DynamicConfigState
{
    private GatewayDynamicConfig? _config;

    /// <summary>Authoritative mutable working set of dynamic records.</summary>
    public GatewayDynamicConfig Config
    {
        get => _config ??= new GatewayDynamicConfig();
        set => _config = value;
    }

    /// <summary>Whether the config has been initialized at least once.</summary>
    public bool IsInitialized => _config != null;

    /// <summary>Monotonically increasing version; updated via <see cref="IncrementVersion"/>.</summary>
    private long _version;

    /// <summary>Bump the version and synchronize <see cref="Config"/>.Version in one step.</summary>
    public long IncrementVersion()
    {
        var v = Interlocked.Increment(ref _version);
        if (_config != null) _config.Version = v;
        return v;
    }

    /// <summary>Raw version value (for reads, not mutation).</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>IDs of routes from appsettings.json (static config).</summary>
    public HashSet<string> StaticRouteIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>IDs of clusters from appsettings.json (static config).</summary>
    public HashSet<string> StaticClusterIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Captured static (appsettings.json) routes from the provider's initial snapshot.</summary>
    public IReadOnlyList<RouteConfig> StaticRoutes { get; set; } = Array.Empty<RouteConfig>();

    /// <summary>Captured static (appsettings.json) clusters from the provider's initial snapshot.</summary>
    public IReadOnlyList<ClusterConfig> StaticClusters { get; set; } = Array.Empty<ClusterConfig>();

    /// <summary>Ensures <see cref="Config"/> is never null.</summary>
    public void EnsureInitialized()
    {
        if (_config == null) _config = new GatewayDynamicConfig();
    }

    /// <summary>
    /// Resolve the internal UID of a cluster by its ID.
    /// Returns null when the cluster is not found or the ID is empty.
    /// </summary>
    public string? ResolveClusterUid(string? clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId)) return null;
        return _config?.Clusters.FirstOrDefault(c =>
            string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))?.ClusterUid;
    }
}
