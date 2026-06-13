using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>Service for managing configuration snapshots and diffs using config history repository.</summary>
public interface IConfigSnapshotService
{
    Task<ConfigDiffSnapshot> CreateSnapshotAsync(string? createdBy = null, string? source = null, string? description = null);
    Task<ConfigDiffSnapshot?> GetSnapshotAsync(string id);
    Task<List<ConfigDiffSnapshot>> GetSnapshotsAsync(int limit = 50);
    Task<ConfigDiffResult> CompareAsync(string fromId, string toId);
    Task<ConfigDiffResult> CompareWithCurrentAsync(string fromId);
    Task<int> CleanupOldSnapshotsAsync(int keepCount = 100);
}

public class ConfigSnapshotService : IConfigSnapshotService
{
    private readonly IConfigHistoryRepository _historyRepo;
    private readonly DynamicYarpConfigService _yarpConfig;

    public ConfigSnapshotService(IConfigHistoryRepository historyRepo, DynamicYarpConfigService yarpConfig)
    {
        _historyRepo = historyRepo;
        _yarpConfig = yarpConfig;
    }

    public async Task<ConfigDiffSnapshot> CreateSnapshotAsync(string? createdBy = null, string? source = null, string? description = null)
    {
        var config = _yarpConfig.GetDynamicConfig();
        var snapshot = new ConfigDiffSnapshot
        {
            Version = config.Version,
            CreatedBy = createdBy ?? "system",
            Source = source,
            Description = description,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var route in config.Routes)
            snapshot.Routes.Add(new RouteSnapshot { RouteId = route.RouteId, ClusterId = route.ClusterId, MatchPath = route.MatchPath, Order = route.Order });

        foreach (var cluster in config.Clusters)
        {
            var cs = new ClusterSnapshot { ClusterId = cluster.ClusterId, LoadBalancing = cluster.LoadBalancingPolicy };
            foreach (var dest in cluster.Destinations)
                cs.Destinations.Add(new DestinationSnapshot { DestinationId = dest.Key, Address = dest.Value });
            snapshot.Clusters.Add(cs);
        }

        var entity = new ConfigHistoryEntity
        {
            VersionId = snapshot.Id,
            Description = snapshot.Description,
            CreatedBy = snapshot.CreatedBy,
            CreatedAt = snapshot.CreatedAt,
            ConfigData = System.Text.Json.JsonSerializer.Serialize(snapshot)
        };
        await _historyRepo.SaveConfigHistoryAsync(entity);

        return snapshot;
    }

    public async Task<ConfigDiffSnapshot?> GetSnapshotAsync(string id)
    {
        var entity = await _historyRepo.GetConfigHistoryAsync(id);
        if (entity == null) return null;
        return System.Text.Json.JsonSerializer.Deserialize<ConfigDiffSnapshot>(entity.ConfigData);
    }

    public async Task<List<ConfigDiffSnapshot>> GetSnapshotsAsync(int limit = 50)
    {
        var entities = await _historyRepo.GetConfigHistoryListAsync(limit);
        var snapshots = new List<ConfigDiffSnapshot>();
        foreach (var entity in entities)
        {
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<ConfigDiffSnapshot>(entity.ConfigData);
            if (snapshot != null) snapshots.Add(snapshot);
        }
        return snapshots;
    }

    public async Task<ConfigDiffResult> CompareAsync(string fromId, string toId)
    {
        var from = await GetSnapshotAsync(fromId);
        var to = toId == "current"
            ? await CreateSnapshotFromCurrentAsync()
            : await GetSnapshotAsync(toId);

        if (from == null) throw new ArgumentException($"Source snapshot '{fromId}' not found");
        if (to == null) throw new ArgumentException($"Target snapshot '{toId}' not found");

        return ComputeDiff(from, to);
    }

    public async Task<ConfigDiffResult> CompareWithCurrentAsync(string fromId)
    {
        var from = await GetSnapshotAsync(fromId);
        if (from == null) throw new ArgumentException($"Source snapshot '{fromId}' not found");

        var current = await CreateSnapshotFromCurrentAsync();
        return ComputeDiff(from, current);
    }

    public async Task<int> CleanupOldSnapshotsAsync(int keepCount = 100)
    {
        var all = await _historyRepo.GetConfigHistoryListAsync(int.MaxValue);
        if (all.Count <= keepCount) return 0;

        var toDelete = all.Skip(keepCount).ToList();
        foreach (var entity in toDelete)
            await _historyRepo.DeleteConfigHistoryAsync(entity.VersionId);

        return toDelete.Count;
    }

    private Task<ConfigDiffSnapshot> CreateSnapshotFromCurrentAsync()
    {
        var config = _yarpConfig.GetDynamicConfig();
        var snapshot = new ConfigDiffSnapshot
        {
            Id = "current",
            Version = config.Version,
            CreatedBy = "current",
            Source = "live",
            Description = "Current live configuration"
        };

        foreach (var route in config.Routes)
            snapshot.Routes.Add(new RouteSnapshot { RouteId = route.RouteId, ClusterId = route.ClusterId, MatchPath = route.MatchPath, Order = route.Order });

        foreach (var cluster in config.Clusters)
        {
            var cs = new ClusterSnapshot { ClusterId = cluster.ClusterId, LoadBalancing = cluster.LoadBalancingPolicy };
            foreach (var dest in cluster.Destinations)
                cs.Destinations.Add(new DestinationSnapshot { DestinationId = dest.Key, Address = dest.Value });
            snapshot.Clusters.Add(cs);
        }

        return Task.FromResult(snapshot);
    }

    private static ConfigDiffResult ComputeDiff(ConfigDiffSnapshot from, ConfigDiffSnapshot to)
    {
        var result = new ConfigDiffResult
        {
            FromVersion = from.Version.ToString(),
            ToVersion = to.Version.ToString()
        };

        var fromRoutes = from.Routes.ToDictionary(r => r.RouteId);
        var toRoutes = to.Routes.ToDictionary(r => r.RouteId);

        foreach (var toRoute in to.Routes)
        {
            if (!fromRoutes.ContainsKey(toRoute.RouteId))
            {
                result.Summary.RoutesAdded++;
                result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Added, EntityType = DiffEntityType.Route, EntityId = toRoute.RouteId, Description = $"Route '{toRoute.RouteId}' added (Path: {toRoute.MatchPath})" });
            }
            else
            {
                var fromRoute = fromRoutes[toRoute.RouteId];
                var fieldChanges = CompareRoute(fromRoute, toRoute);
                if (fieldChanges.Any())
                {
                    result.Summary.RoutesModified++;
                    result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Modified, EntityType = DiffEntityType.Route, EntityId = toRoute.RouteId, FieldChanges = fieldChanges, Description = $"Route '{toRoute.RouteId}' modified: {string.Join(", ", fieldChanges.Select(f => f.FieldName))}" });
                }
            }
        }

        foreach (var fromRoute in from.Routes)
        {
            if (!toRoutes.ContainsKey(fromRoute.RouteId))
            {
                result.Summary.RoutesRemoved++;
                result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Removed, EntityType = DiffEntityType.Route, EntityId = fromRoute.RouteId, Description = $"Route '{fromRoute.RouteId}' removed" });
            }
        }

        var fromClusters = from.Clusters.ToDictionary(c => c.ClusterId);
        var toClusters = to.Clusters.ToDictionary(c => c.ClusterId);

        foreach (var toCluster in to.Clusters)
        {
            if (!fromClusters.ContainsKey(toCluster.ClusterId))
            {
                result.Summary.ClustersAdded++;
                result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Added, EntityType = DiffEntityType.Cluster, EntityId = toCluster.ClusterId, Description = $"Cluster '{toCluster.ClusterId}' added ({toCluster.Destinations.Count} destinations)" });
            }
            else
            {
                var fromCluster = fromClusters[toCluster.ClusterId];
                var fieldChanges = CompareCluster(fromCluster, toCluster);
                if (fieldChanges.Any())
                {
                    result.Summary.ClustersModified++;
                    result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Modified, EntityType = DiffEntityType.Cluster, EntityId = toCluster.ClusterId, FieldChanges = fieldChanges, Description = $"Cluster '{toCluster.ClusterId}' modified: {string.Join(", ", fieldChanges.Select(f => f.FieldName))}" });
                }

                var fromDests = fromCluster.Destinations.ToDictionary(d => d.DestinationId);
                var toDests = toCluster.Destinations.ToDictionary(d => d.DestinationId);

                foreach (var toDest in toCluster.Destinations)
                {
                    if (!fromDests.ContainsKey(toDest.DestinationId))
                    {
                        result.Summary.DestinationsAdded++;
                        result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Added, EntityType = DiffEntityType.Destination, EntityId = toDest.DestinationId, ParentId = toCluster.ClusterId, Description = $"Destination '{toDest.DestinationId}' added to cluster '{toCluster.ClusterId}'" });
                    }
                    else
                    {
                        var changes = CompareDestination(fromDests[toDest.DestinationId], toDest);
                        if (changes.Any())
                        {
                            result.Summary.DestinationsModified++;
                            result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Modified, EntityType = DiffEntityType.Destination, EntityId = toDest.DestinationId, ParentId = toCluster.ClusterId, FieldChanges = changes, Description = $"Destination '{toDest.DestinationId}' modified: {string.Join(", ", changes.Select(f => f.FieldName))}" });
                        }
                    }
                }

                foreach (var fromDest in fromCluster.Destinations)
                {
                    if (!toDests.ContainsKey(fromDest.DestinationId))
                    {
                        result.Summary.DestinationsRemoved++;
                        result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Removed, EntityType = DiffEntityType.Destination, EntityId = fromDest.DestinationId, ParentId = toCluster.ClusterId, Description = $"Destination '{fromDest.DestinationId}' removed from cluster '{toCluster.ClusterId}'" });
                    }
                }
            }
        }

        foreach (var fromCluster in from.Clusters)
        {
            if (!toClusters.ContainsKey(fromCluster.ClusterId))
            {
                result.Summary.ClustersRemoved++;
                result.Changes.Add(new DiffEntry { ChangeType = DiffChangeType.Removed, EntityType = DiffEntityType.Cluster, EntityId = fromCluster.ClusterId, Description = $"Cluster '{fromCluster.ClusterId}' removed" });
            }
        }

        return result;
    }

    private static List<DiffFieldChange> CompareRoute(RouteSnapshot from, RouteSnapshot to)
    {
        var changes = new List<DiffFieldChange>();
        if (from.ClusterId != to.ClusterId) changes.Add(new DiffFieldChange { FieldName = "ClusterId", OldValue = from.ClusterId, NewValue = to.ClusterId });
        if (from.MatchPath != to.MatchPath) changes.Add(new DiffFieldChange { FieldName = "MatchPath", OldValue = from.MatchPath, NewValue = to.MatchPath });
        if (from.Order != to.Order) changes.Add(new DiffFieldChange { FieldName = "Order", OldValue = from.Order.ToString(), NewValue = to.Order.ToString() });
        return changes;
    }

    private static List<DiffFieldChange> CompareCluster(ClusterSnapshot from, ClusterSnapshot to)
    {
        var changes = new List<DiffFieldChange>();
        if (from.LoadBalancing != to.LoadBalancing) changes.Add(new DiffFieldChange { FieldName = "LoadBalancing", OldValue = from.LoadBalancing, NewValue = to.LoadBalancing });
        return changes;
    }

    private static List<DiffFieldChange> CompareDestination(DestinationSnapshot from, DestinationSnapshot to)
    {
        var changes = new List<DiffFieldChange>();
        if (from.Address != to.Address) changes.Add(new DiffFieldChange { FieldName = "Address", OldValue = from.Address, NewValue = to.Address });
        return changes;
    }
}
