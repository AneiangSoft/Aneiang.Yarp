using System.Text.Json;
using Yarp.ReverseProxy.Configuration;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Contract for config diff computation between snapshot versions and live config.
/// </summary>
public interface IConfigDiffService
{
    /// <summary>Resolve the change type from a snapshot description.</summary>
    string ResolveHistoryChangeType(string? description);

    /// <summary>Parse route entries from a snapshot JSON config.</summary>
    List<SnapshotRoute> ParseSnapshotRoutes(JsonElement config);

    /// <summary>Parse cluster entries from a snapshot JSON config.</summary>
    List<SnapshotCluster> ParseSnapshotClusters(JsonElement config);

    /// <summary>Build a structured diff of routes between a snapshot and live state.</summary>
    List<object> BuildRouteDiffs(
        List<SnapshotRoute> snapshotRoutes,
        IReadOnlyList<RouteConfig> currentRoutes);

    /// <summary>Build a structured diff of clusters between a snapshot and live state.</summary>
    List<object> BuildClusterDiffs(
        List<SnapshotCluster> snapshotClusters,
        IReadOnlyList<ClusterConfig> currentClusters);
}
