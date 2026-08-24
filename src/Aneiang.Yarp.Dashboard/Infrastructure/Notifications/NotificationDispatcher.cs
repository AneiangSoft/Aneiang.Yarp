using System.Threading.Channels;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Notifications;

/// <summary>
/// Central hub for all outbound webhook notifications. Events (config changes and
/// gateway alerts) are enqueued from anywhere in the app and processed on a
/// background channel loop with per-(platform, event, target) cooldown dedup
/// so alert storms (e.g. a flapping circuit breaker) cannot flood webhook rate limits.
/// </summary>
public sealed class NotificationDispatcher : BackgroundService
{
    private readonly Channel<WebhookNotificationMessage> _queue;
    private readonly IServiceProvider _services;
    private readonly ILogger<NotificationDispatcher> _logger;

    private readonly object _cooldownLock = new();
    private readonly Dictionary<string, DateTimeOffset> _cooldownUntil = new();

    public NotificationDispatcher(
        IServiceProvider services,
        ILogger<NotificationDispatcher> logger)
    {
        _services = services;
        _logger = logger;
        _queue = Channel.CreateBounded<WebhookNotificationMessage>(
            new BoundedChannelOptions(1000)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true
            });
    }

    /// <summary>Enqueue a config-change notification (fire-and-forget).</summary>
    public void EnqueueConfigChange(string eventType, string? target, string? operatorName)
        => Enqueue(new WebhookNotificationMessage
        {
            EventType = eventType,
            Target = target,
            Operator = operatorName,
            Severity = "Info"
        });

    /// <summary>Enqueue a gateway alert notification (fire-and-forget).</summary>
    public void EnqueueAlert(string alertType, string? target, string message, string severity = "Warning")
        => Enqueue(new WebhookNotificationMessage
        {
            EventType = alertType,
            Target = target,
            Detail = message,
            Severity = severity
        });

    /// <summary>Reset all cooldowns so suppressed events deliver again immediately.</summary>
    public void ClearCooldown()
    {
        lock (_cooldownLock)
        {
            _cooldownUntil.Clear();
        }
    }

    private void Enqueue(WebhookNotificationMessage message)
    {
        if (!_queue.Writer.TryWrite(message))
            _logger.LogWarning("Notification queue is full - dropped {EventType} event", message.EventType);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Give the host time to finish initializing before draining the queue.
        try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
        catch (OperationCanceledException) { return; }

        _logger.LogDebug("NotificationDispatcher started");

        await foreach (var message in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await DispatchAsync(message, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch webhook notification {EventType}", message.EventType);
            }
        }
    }

    private async Task DispatchAsync(WebhookNotificationMessage message, CancellationToken ct)
    {
        WebhookSettingsData settings;
        WebhookNotificationService sender;
        try
        {
            using (var scope = _services.CreateScope())
            {
                var repo = scope.ServiceProvider.GetRequiredService<IWebhookSettingsRepository>();
                settings = await repo.LoadAsync(ct);
            }
            sender = _services.GetRequiredService<WebhookNotificationService>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load webhook settings for {EventType}", message.EventType);
            return;
        }

        if (settings.Platforms.Count == 0) return;

        // Route: platforms with an explicit subscription list only receive their
        // subscribed events; platforms without one fall back to the global list.
        var platforms = new List<string>();
        foreach (var platform in settings.Platforms.Keys)
        {
            var subscribed = settings.PlatformEvents.TryGetValue(platform, out var events)
                ? events.Contains(message.EventType, StringComparer.OrdinalIgnoreCase)
                : settings.EnabledEvents.Contains(message.EventType, StringComparer.OrdinalIgnoreCase);

            if (subscribed)
                platforms.Add(platform);
        }

        if (platforms.Count == 0) return;

        // Cooldown dedup per (platform, event, target).
        var cooldown = TimeSpan.FromSeconds(Math.Clamp(settings.CooldownSeconds, 0, 3600));
        if (cooldown > TimeSpan.Zero)
        {
            var now = DateTimeOffset.UtcNow;
            lock (_cooldownLock)
            {
                platforms.RemoveAll(p =>
                {
                    var key = $"{p}|{message.EventType}|{message.Target ?? "-"}";
                    if (_cooldownUntil.TryGetValue(key, out var until) && until > now)
                        return true;
                    _cooldownUntil[key] = now + cooldown;
                    return false;
                });

                if (_cooldownUntil.Count > 5000)
                    _cooldownUntil.Clear(); // safety valve for unbounded key growth
            }

            if (platforms.Count == 0)
            {
                _logger.LogDebug("Notification {EventType} suppressed by cooldown", message.EventType);
                return;
            }
        }

        var report = await sender.SendToPlatformsAsync(message, platforms, ct: ct);
        if (report.Total > 0 && !report.Success)
        {
            _logger.LogWarning(
                "Webhook notification {EventType} partially failed: {Succeeded}/{Total} delivered",
                message.EventType, report.Succeeded, report.Total);
        }
    }
}
