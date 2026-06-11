using Aneiang.Yarp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;

/// <summary>
/// Preloads dynamic config into memory cache during startup.
/// This prevents sync-over-async deadlocks when the config is accessed synchronously.
/// </summary>
public class DynamicConfigPreloadService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DynamicConfigPreloadService> _logger;

    public DynamicConfigPreloadService(IServiceProvider serviceProvider, ILogger<DynamicConfigPreloadService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var persistence = scope.ServiceProvider.GetRequiredService<IDynamicConfigPersistenceService>() as DynamicConfigPersistenceService;
            if (persistence != null)
            {
                await persistence.PreloadAsync(cancellationToken);
                _logger.LogInformation("Dynamic config preloaded successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to preload dynamic config");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
