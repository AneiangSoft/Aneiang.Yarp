using Aneiang.Yarp.Models;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

internal class DynamicConfigState
{
    private GatewayDynamicConfig? _config;

    public GatewayDynamicConfig Config
    {
        get => _config ??= new GatewayDynamicConfig();
        set => _config = value;
    }

    public bool IsInitialized => _config != null;

    private long _version;

    public long IncrementVersion()
    {
        var v = Interlocked.Increment(ref _version);
        if (_config != null) _config.Version = v;
        return v;
    }

    public long Version => Interlocked.Read(ref _version);

    public HashSet<string> StaticRouteIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> StaticClusterIds { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RouteConfig> StaticRoutes { get; set; } = Array.Empty<RouteConfig>();

    public IReadOnlyList<ClusterConfig> StaticClusters { get; set; } = Array.Empty<ClusterConfig>();

    public void EnsureInitialized()
    {
        if (_config == null) _config = new GatewayDynamicConfig();
    }

    public string? ResolveClusterUid(string? clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId)) return null;
        return _config?.Clusters.FirstOrDefault(c =>
            string.Equals(c.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))?.ClusterUid;
    }
}
