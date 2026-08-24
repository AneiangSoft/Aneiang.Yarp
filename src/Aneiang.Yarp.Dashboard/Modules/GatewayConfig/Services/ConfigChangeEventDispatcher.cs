using Aneiang.Yarp.Dashboard.Infrastructure.Notifications;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Background service that dispatches config-change events from a queue.
/// Fires the <see cref="ConfigChangeAuditLog.OnConfigChanged"/> event for external subscribers
/// and enqueues webhook notifications for subscribed event types on the central dispatcher.
/// </summary>
internal sealed class ConfigChangeEventDispatcher : BackgroundService
{
    private readonly ConfigChangeAuditLog _auditLog;
    private readonly NotificationDispatcher _dispatcher;
    private readonly ILogger<ConfigChangeEventDispatcher> _logger;

    public ConfigChangeEventDispatcher(
        ConfigChangeAuditLog auditLog,
        NotificationDispatcher dispatcher,
        ILogger<ConfigChangeEventDispatcher> logger)
    {
        _auditLog = auditLog;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the host and all other services time to finish initializing.
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogDebug("ConfigChangeEventDispatcher started - all services initialized");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_auditLog.TryDequeuePendingNotification(out var notification))
                {
                    _logger.LogDebug(
                        "ConfigChangeEventDispatcher: dispatching {EventType} on {Target}",
                        notification.EventType, notification.Target);

                    // Fire event for external subscribers
                    _auditLog.InvokeOnConfigChanged(
                        notification.EventType, notification.Target,
                        notification.Operator, notification.Details);

                    // Enqueue webhook notification on the central dispatcher
                    // (event routing + cooldown handled there; fire-and-forget)
                    _dispatcher.EnqueueConfigChange(notification.EventType, notification.Target, notification.Operator);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error dispatching pending notification - will retry");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
