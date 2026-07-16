using System.Collections.Concurrent;
using Aneiang.Yarp.Models;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Services;

public sealed class AneiangProxyConfigProvider : IProxyConfigProvider
{
    private volatile AneiangProxyConfig _current;
    private CancellationTokenSource _changeTokenSource = new();
    private readonly ConcurrentDictionary<string, DateTime> _heartbeats = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _swapLock = new();

    private static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromHours(2);
    private DateTime _lastHeartbeatCleanup = DateTime.Now;
    private static readonly TimeSpan HeartbeatCleanupInterval = TimeSpan.FromMinutes(5);

    public AneiangProxyConfigProvider(
        IReadOnlyList<RouteConfig> initialRoutes,
        IReadOnlyList<ClusterConfig> initialClusters)
    {
        var normalizedRoutes = initialRoutes.Select(r => r.Order == null ? r with { Order = int.MaxValue } : r).ToList();

        var dynRoutes = normalizedRoutes.Select(r => new DynamicRouteConfig
        {
            Config = r,
            Source = "config",
            CreatedBy = "appsettings.json"
        }).ToList();

        var dynClusters = initialClusters.Select(c => new DynamicClusterConfig
        {
            Config = c,
            Source = "config",
            CreatedBy = "appsettings.json"
        }).ToList();

        _current = new AneiangProxyConfig
        {
            Routes = normalizedRoutes,
            Clusters = initialClusters,
            DynamicRoutes = dynRoutes,
            DynamicClusters = dynClusters,
            ChangeToken = new CancellationChangeToken(_changeTokenSource.Token),
            Version = 0
        };
    }

    internal AneiangProxyConfig Current => _current;

    public IProxyConfig GetConfig() => _current;

    public IReadOnlyList<RouteConfig> GetRoutes() => _current.Routes;

    public IReadOnlyList<ClusterConfig> GetClusters() => _current.Clusters;

    public ClusterConfig? GetCluster(string clusterId)
        => _current.Clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));

    public GatewayDynamicConfig? GetDynamicConfig()
    {
        var snap = _current;
        return new GatewayDynamicConfig
        {
            Version = snap.Version,
            LastModified = DateTime.Now,
            Routes = snap.DynamicRoutes.ToList(),
            Clusters = snap.DynamicClusters.Select(c =>
            {
                if (_heartbeats.TryGetValue(c.Config.ClusterId ?? string.Empty, out var hb)) c.LastHeartbeat = hb;
                return c;
            }).ToList()
        };
    }

    internal AneiangProxyConfig CreateSnapshot(
        IReadOnlyList<DynamicRouteConfig> dynRoutes,
        IReadOnlyList<DynamicClusterConfig> dynClusters,
        long version)
    {
        return new AneiangProxyConfig
        {
            Routes = dynRoutes.Select(r => r.Config).ToList(),
            Clusters = dynClusters.Select(c => c.Config).ToList(),
            DynamicRoutes = dynRoutes,
            DynamicClusters = dynClusters,
            ChangeToken = new CancellationChangeToken(_changeTokenSource.Token),
            Version = version
        };
    }

    internal void ApplySnapshot(AneiangProxyConfig snapshot)
    {
        CancellationTokenSource oldSource;
        lock (_swapLock)
        {
            var newSource = new CancellationTokenSource();
            var withToken = new AneiangProxyConfig
            {
                Routes = snapshot.Routes,
                Clusters = snapshot.Clusters,
                DynamicRoutes = snapshot.DynamicRoutes,
                DynamicClusters = snapshot.DynamicClusters,
                ChangeToken = new CancellationChangeToken(newSource.Token),
                Version = snapshot.Version
            };

            oldSource = _changeTokenSource;
            _changeTokenSource = newSource;
            _current = withToken;
        }

        oldSource.Cancel();
        oldSource.Dispose();
    }

    public bool UpdateHeartbeat(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId)) return false;
        _heartbeats[clusterId] = DateTime.Now;

        var now = DateTime.Now;
        if (now - _lastHeartbeatCleanup > HeartbeatCleanupInterval)
        {
            _lastHeartbeatCleanup = now;
            var activeClusterIds = new HashSet<string>(
                _current.Clusters.Select(c => c.ClusterId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _heartbeats)
            {
                if (!activeClusterIds.Contains(kvp.Key) || now - kvp.Value > MaxHeartbeatAge)
                    _heartbeats.TryRemove(kvp.Key, out _);
            }
        }

        return true;
    }

    public void ApplyFromDynamic(
        IReadOnlyList<DynamicRouteConfig> dynRoutes,
        IReadOnlyList<DynamicClusterConfig> dynClusters,
        long version)
    {
        var routesCopy = dynRoutes.ToList();
        var clustersCopy = dynClusters.ToList();
        ApplySnapshot(CreateSnapshot(routesCopy, clustersCopy, version));
    }

    public void Update(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters)
    {
        var snap = _current;
        var routeMeta = snap.DynamicRoutes.ToDictionary(r => r.Config.RouteId ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        var clusterMeta = snap.DynamicClusters.ToDictionary(c => c.Config.ClusterId ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var dynRoutes = new List<DynamicRouteConfig>(routes.Count);
        foreach (var r in routes)
        {
            var normalized = r.Order == null ? r with { Order = int.MaxValue } : r;
            var id = r.RouteId ?? string.Empty;
            if (routeMeta.TryGetValue(id, out var meta))
            {
                meta.Config = normalized;
                dynRoutes.Add(meta);
            }
            else
            {
                dynRoutes.Add(new DynamicRouteConfig { Config = normalized });
            }
        }

        var dynClusters = new List<DynamicClusterConfig>(clusters.Count);
        foreach (var c in clusters)
        {
            var id = c.ClusterId ?? string.Empty;
            if (clusterMeta.TryGetValue(id, out var meta))
            {
                meta.Config = c;
                dynClusters.Add(meta);
            }
            else
            {
                dynClusters.Add(new DynamicClusterConfig { Config = c });
            }
        }

        ApplySnapshot(CreateSnapshot(dynRoutes, dynClusters, _current.Version + 1));
    }
}
