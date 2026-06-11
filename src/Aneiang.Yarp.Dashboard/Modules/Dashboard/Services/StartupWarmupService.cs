using System.Diagnostics;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Warmup service that runs during application startup to eliminate cold-start latency.
/// Initializes: IGatewayRepository (SQLite tables), MemoryCache entries, route/cluster query results,
/// and any other lazily-initialized resources.
/// </summary>
public sealed class StartupWarmupService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupWarmupService> _logger;

    public StartupWarmupService(IServiceProvider serviceProvider, ILogger<StartupWarmupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>(4);

        tasks.Add(WarmupRepositoryAsync(ct));
        tasks.Add(WarmupQueryCacheAsync(ct));
        tasks.Add(WarmupProxyLogStoreAsync(ct));

        try { await Task.WhenAll(tasks); }
        catch (Exception ex) { _logger.LogWarning(ex, "Some warmup tasks failed — application will continue"); }

        sw.Stop();
        _logger.LogInformation("Application warmup completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task WarmupRepositoryAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();

            // Initialize IGatewayRepository (creates SQLite tables)
            if (scope.ServiceProvider.GetService<IGatewayRepository>() is IGatewayRepository repo)
            {
                await repo.InitializeAsync(ct);
                _logger.LogDebug("IGatewayRepository warmup done");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repository warmup failed");
        }
    }

    private async Task WarmupQueryCacheAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var routeQuery = scope.ServiceProvider.GetService<IDashboardRouteQueryService>();
            var clusterQuery = scope.ServiceProvider.GetService<IDashboardClusterQueryService>();

            await Task.WhenAll(
                Task.Run(() => _ = routeQuery?.GetRoutes(), ct),
                Task.Run(() => _ = clusterQuery?.GetClusters(), ct)
            );

            _logger.LogDebug("Query cache warmup done");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Query cache warmup failed");
        }
    }

    private async Task WarmupProxyLogStoreAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var logStore = scope.ServiceProvider.GetService<IProxyLogStore>();

            if (logStore != null)
            {
                var snapshot = logStore.GetRecent(10);
                _logger.LogDebug("ProxyLogStore warmup done ({Count} entries)", snapshot.Entries.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProxyLogStore warmup failed");
        }
    }
}
