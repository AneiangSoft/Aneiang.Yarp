using Aneiang.Yarp.Dashboard.Models.Dtos;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Service for querying cluster information.
/// </summary>
public interface IDashboardClusterQueryService
{
    /// <summary>
    /// Gets all clusters with their configurations and health status.
    /// </summary>
    /// <returns>List of cluster responses.</returns>
    IReadOnlyList<DashboardClusterResponse> GetClusters();
}
