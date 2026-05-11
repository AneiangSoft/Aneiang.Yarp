using Aneiang.Yarp.Dashboard.Models.Dtos;
using Aneiang.Yarp.Services;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Implementation of dashboard cluster query service.
/// </summary>
internal sealed class DashboardClusterQueryService : IDashboardClusterQueryService
{
    private readonly DynamicYarpConfigService _dynamicConfig;

    /// <summary>
    /// Initializes a new instance of DashboardClusterQueryService.
    /// </summary>
    /// <param name="dynamicConfig">Dynamic YARP config service.</param>
    public DashboardClusterQueryService(
        DynamicYarpConfigService dynamicConfig)
    {
        _dynamicConfig = dynamicConfig;
    }

    /// <inheritdoc />
    public IReadOnlyList<DashboardClusterResponse> GetClusters()
    {
        var clusters = _dynamicConfig.GetClusters();
        
        return clusters?
            .Select(cluster => DashboardClusterMapper.MapToResponse(cluster, _dynamicConfig))
            .ToList() ?? new List<DashboardClusterResponse>();
    }
}
