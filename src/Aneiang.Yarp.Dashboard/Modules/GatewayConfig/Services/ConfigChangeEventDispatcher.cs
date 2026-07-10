using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Background service that dispatches config-change events from a queue.
/// Directly notifies <see cref="INotificationService"/> for config change events.
/// Also fires the <see cref="ConfigChangeAuditLog.OnConfigChanged"/> event for external subscribers.
/// </summary>
internal sealed class ConfigChangeEventDispatcher : BackgroundService
{
    private readonly ConfigChangeAuditLog _auditLog;
    private readonly INotificationService _notificationService;
    private readonly ILogger<ConfigChangeEventDispatcher> _logger;

    public ConfigChangeEventDispatcher(
        ConfigChangeAuditLog auditLog,
        INotificationService notificationService,
        ILogger<ConfigChangeEventDispatcher> logger)
    {
        _auditLog = auditLog;
        _notificationService = notificationService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the host and all other services time to finish initializing.
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogDebug("ConfigChangeEventDispatcher started — all services initialized");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_auditLog.TryDequeuePendingNotification(out var notification))
                {
                    _logger.LogDebug(
                        "ConfigChangeEventDispatcher: dispatching {EventType} on {Target}",
                        notification.EventType, notification.Target);

                    // Send via the unified notification system (channels + rules + history)
                    _notificationService.NotifyConfigChange(
                        notification.EventType, notification.Target,
                        notification.Operator, notification.Details);

                    // Also fire legacy event for any external subscribers
                    _auditLog.InvokeOnConfigChanged(
                        notification.EventType, notification.Target,
                        notification.Operator, notification.Details);
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
                _logger.LogWarning(ex, "Error dispatching pending notification — will retry");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
