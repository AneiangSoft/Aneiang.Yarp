using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Notification.Services;

/// <summary>
/// Initializes the notification service at startup.
/// </summary>
public sealed class NotificationWarmupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NotificationWarmupService> _logger;

    public NotificationWarmupService(IServiceProvider serviceProvider, ILogger<NotificationWarmupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notificationService.PreloadAsync(ct);
            _logger.LogDebug("Notification service warmup completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Notification service warmup failed, will retry on first use");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
