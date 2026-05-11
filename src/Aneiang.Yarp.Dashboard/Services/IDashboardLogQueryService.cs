using Aneiang.Yarp.Dashboard.Models;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Service for querying and managing proxy logs.
/// </summary>
public interface IDashboardLogQueryService
{
    /// <summary>
    /// Gets recent log entries.
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>Log store snapshot.</returns>
    ProxyLogStoreSnapshot GetLogs(int count = 100);

    /// <summary>
    /// Clears all log entries.
    /// </summary>
    void ClearLogs();
}
