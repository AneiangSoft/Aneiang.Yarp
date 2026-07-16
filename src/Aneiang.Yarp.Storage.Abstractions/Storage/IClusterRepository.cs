namespace Aneiang.Yarp.Storage;

public interface IClusterRepository
{
    Task<ClusterEntity?> GetClusterAsync(string clusterId, CancellationToken ct = default);
    Task<IReadOnlyList<ClusterEntity>> GetAllClustersAsync(CancellationToken ct = default);
    Task SaveClusterAsync(ClusterEntity cluster, CancellationToken ct = default);
    Task SaveClustersAsync(IEnumerable<ClusterEntity> clusters, CancellationToken ct = default);
    Task DeleteClusterAsync(string clusterId, CancellationToken ct = default);

    // Destinations (belong to cluster)
    Task<IReadOnlyList<DestinationEntity>> GetDestinationsAsync(string clusterId, CancellationToken ct = default);
    Task SaveDestinationsAsync(string clusterId, IEnumerable<DestinationEntity> destinations, CancellationToken ct = default);
    Task DeleteDestinationsAsync(string clusterId, CancellationToken ct = default);
}
