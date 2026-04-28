using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Ensures YarpEventSourceListener is instantiated at startup to begin capturing events.
/// 确保应用启动时实例化 EventSource 监听器以开始捕获事件.
/// </summary>
public sealed class YarpEventSourceListenerStartupService : IHostedService
{
    private readonly YarpEventSourceListener _listener;

    /// <summary>
    /// Creates a new startup service.
    /// </summary>
    public YarpEventSourceListenerStartupService(YarpEventSourceListener listener)
    {
        _listener = listener;
    }

    /// <summary>
    /// Forces the listener to be instantiated (triggers EventSource subscription).
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
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
