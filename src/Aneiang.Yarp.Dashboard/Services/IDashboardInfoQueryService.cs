using Aneiang.Yarp.Dashboard.Models.Dtos;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Service for querying dashboard basic information.
/// </summary>
public interface IDashboardInfoQueryService
{
    /// <summary>
    /// Gets dashboard basic information.
    /// </summary>
    /// <returns>Dashboard info response.</returns>
    DashboardInfoResponse GetInfo();
}
