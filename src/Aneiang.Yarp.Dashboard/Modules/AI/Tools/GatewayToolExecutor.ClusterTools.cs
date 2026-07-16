using Aneiang.Yarp.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

public partial class GatewayToolExecutor
{
    // ===================== CLUSTER TOOLS =====================

    private object ExecuteGetClusters()
    {
        var clusters = _clusterQuery.GetClusters();
        return new
        {
            total = clusters.Count,
            clusters = clusters.Select(c => new
            {
                cluster_id = c.ClusterId,
                load_balancing = c.LoadBalancingPolicy,
                destinations = c.Destinations?.Select(d => new
                {
                    name = d.Name,
                    address = d.Address,
                    health = d.Health
                })
            })
        };
    }

    private async Task<object> ExecuteCreateClusterAsync(ToolArgs args, CancellationToken ct)
    {
        var clusterId = args.Get("cluster_id");
        var destinations = args.GetStringMap("destinations");
        var lb = args.GetString("load_balancing") ?? "RoundRobin";

        var request = new CreateClusterRequest
        {
            ClusterId = clusterId,
            Destinations = destinations,
            LoadBalancingPolicy = lb
        };

        var result = await _dynamicConfig.TryAddCluster(request, "ai-assistant", "ai");
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster '{clusterId}' created with {destinations.Count} destination(s)."
                : $"Failed to create cluster: {result.Message}",
            cluster_id = clusterId
        };
    }

    private async Task<object> ExecuteUpdateClusterAsync(ToolArgs args, CancellationToken ct)
    {
        var clusterId = args.Get("cluster_id");
        var updateReq = new UpdateClusterRequest();

        if (args.HasValue("destinations"))
            updateReq.Destinations = args.GetStringMap("destinations");

        if (args.Has("load_balancing"))
            updateReq.LoadBalancingPolicy = args.GetString("load_balancing");

        var result = await _dynamicConfig.TryUpdateCluster(clusterId, updateReq);
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster '{clusterId}' updated."
                : $"Failed to update cluster: {result.Message}"
        };
    }

    private async Task<object> ExecuteDeleteClusterAsync(ToolArgs args, CancellationToken ct)
    {
        var clusterId = args.Get("cluster_id");
        var result = await _dynamicConfig.TryRemoveCluster(clusterId);
        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster '{clusterId}' deleted."
                : $"Failed to delete cluster: {result.Message}"
        };
    }

    private async Task<object> ExecuteRenameClusterAsync(ToolArgs args)
    {
        var oldClusterId = args.Get("old_cluster_id");
        var newClusterId = args.Get("new_cluster_id");

        var clusters = _clusterQuery.GetClusters();
        var existing = clusters.FirstOrDefault(c =>
            string.Equals(c.ClusterId, oldClusterId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return new { success = false, message = $"Cluster '{oldClusterId}' not found." };

        var destinations = existing.Destinations?
            .ToDictionary(d => d.Name, d => d.Address!) ?? new Dictionary<string, string>();

        var result = await _dynamicConfig.TryRenameCluster(
            oldClusterId, newClusterId, destinations,
            existing.LoadBalancingPolicy, null, "ai-assistant", "ai");

        return new
        {
            success = result.Success,
            message = result.Success
                ? $"Cluster renamed: '{oldClusterId}' → '{newClusterId}'."
                : $"Failed to rename cluster: {result.Message}"
        };
    }
}
