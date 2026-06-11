using Aneiang.Yarp.Dashboard.Modules.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

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
