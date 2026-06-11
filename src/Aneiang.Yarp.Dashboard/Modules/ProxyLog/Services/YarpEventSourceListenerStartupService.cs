using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.Waf.Models;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Modules.Alert.Models;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Models;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;

/// <summary>
/// Ensures YarpEventSourceListener is instantiated at startup to begin capturing events.
/// </summary>
public sealed class YarpEventSourceListenerStartupService : IHostedService
{
    private readonly YarpEventSourceListener _listener;
    private readonly IOptions<DashboardOptions> _options;

    /// <summary>
    /// Creates a new startup service.
    /// </summary>
    public YarpEventSourceListenerStartupService(YarpEventSourceListener listener, IOptions<DashboardOptions> options)
    {
        _listener = listener;
        _options = options;
    }

    /// <summary>
    /// Forces the listener to be instantiated (triggers EventSource subscription).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.EnableProxyLogging)
            return Task.CompletedTask;

        // Just accessing the listener is enough to ensure it's created
        // The EventListener base class will automatically subscribe to EventSources
        _ = _listener;
        return Task.CompletedTask;
    }

    /// <summary>
    /// No cleanup needed on shutdown.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
