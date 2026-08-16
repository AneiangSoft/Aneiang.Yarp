using Aneiang.Yarp.Dashboard.Infrastructure.Notifications;
using Aneiang.Yarp.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Background service that dispatches config-change events from a queue.
/// Fires the <see cref="ConfigChangeAuditLog.OnConfigChanged"/> event for external subscribers
/// and pushes webhook notifications for subscribed event types.
/// </summary>
internal sealed class ConfigChangeEventDispatcher : BackgroundService
{
    private readonly ConfigChangeAuditLog _auditLog;
    private readonly WebhookNotificationService _webhooks;
    private readonly ILogger<ConfigChangeEventDispatcher> _logger;

    public ConfigChangeEventDispatcher(
        ConfigChangeAuditLog auditLog,
        WebhookNotificationService webhooks,
        ILogger<ConfigChangeEventDispatcher> logger)
    {
        _auditLog = auditLog;
        _webhooks = webhooks;
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

                    // Push webhook notifications for subscribed event types (fire-and-forget)
                    _ = NotifyWebhooksAsync(notification, stoppingToken);
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

    /// <summary>
    /// Deliver a webhook notification for a config-change event.
    /// Exceptions are fully contained so webhook failures never affect the dispatch loop.
    /// </summary>
    private async Task NotifyWebhooksAsync(Aneiang.Yarp.Services.PendingNotification notification, CancellationToken ct)
    {
        try
        {
            var report = await _webhooks.NotifyConfigChangeAsync(
                notification.EventType, notification.Target, notification.Operator, ct);

            if (report.Total > 0 && !report.Success)
            {
                _logger.LogWarning(
                    "Webhook notification for {EventType} partially failed: {Succeeded}/{Total} delivered",
                    notification.EventType, report.Succeeded, report.Total);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host shutting down - ignore.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Webhook notification for {EventType} failed", notification.EventType);
        }
    }
}
