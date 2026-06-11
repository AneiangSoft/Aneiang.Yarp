using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

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
