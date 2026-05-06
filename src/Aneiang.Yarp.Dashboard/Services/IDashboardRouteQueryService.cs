using Aneiang.Yarp.Dashboard.Models.Dtos;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Service for querying route information.
/// </summary>
public interface IDashboardRouteQueryService
{
    /// <summary>
    /// Gets all routes with their configurations and match criteria.
    /// </summary>
    /// <returns>List of route responses.</returns>
    IReadOnlyList<DashboardRouteResponse> GetRoutes();
}
