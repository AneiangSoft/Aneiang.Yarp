using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Preloads webhook settings into memory cache during startup.
/// This prevents sync-over-async deadlocks when settings are accessed synchronously.
/// </summary>
public class WebhookSettingsPreloadService : IHostedService
{
    private readonly WebhookSettingsPersistenceService _webhookPersistence;

    public WebhookSettingsPreloadService(WebhookSettingsPersistenceService webhookPersistence)
    {
        _webhookPersistence = webhookPersistence;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _webhookPersistence.PreloadAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Swallow - webhook loading failure shouldn't block startup
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
