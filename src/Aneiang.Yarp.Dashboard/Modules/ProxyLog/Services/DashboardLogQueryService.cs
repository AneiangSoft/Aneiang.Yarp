using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Implementation of dashboard log query service.
/// </summary>
internal sealed class DashboardLogQueryService : IDashboardLogQueryService
{
    private readonly ProxyLogStore _logStore;
    private readonly DashboardOptions _options;

    /// <summary>
    /// Initializes a new instance of DashboardLogQueryService.
    /// </summary>
    /// <param name="logStore">Proxy log store.</param>
    /// <param name="options">Dashboard options.</param>
    public DashboardLogQueryService(
        ProxyLogStore logStore,
        IOptions<DashboardOptions> options)
    {
        _logStore = logStore;
        _options = options.Value;
    }

    /// <inheritdoc />
    public ProxyLogStoreSnapshot GetLogs(int count = 100)
    {
        if (!_options.EnableProxyLogging)
        {
            return new ProxyLogStoreSnapshot
            {
                Entries = new List<LogEntry>(),
                BufferSize = 0,
                EvictedCount = 0,
                BufferCapacity = _options.LogBufferCapacity
            };
        }

        return _logStore.GetRecent(count);
    }

    /// <inheritdoc />
    public void ClearLogs()
    {
        if (_options.EnableProxyLogging)
        {
            _logStore.Clear();
        }
    }
}
