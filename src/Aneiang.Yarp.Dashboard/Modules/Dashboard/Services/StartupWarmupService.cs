using System.Diagnostics;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Auth;
using Aneiang.Yarp.Dashboard.Infrastructure.I18n;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Infrastructure.Realtime;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;
using Aneiang.Yarp.Dashboard.Modules.Webhook.Services;
using Aneiang.Yarp.Dashboard.Modules.Policy.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Alert.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Warmup service that runs during application startup to eliminate cold-start latency.
/// Initializes: SQLite connection pool, MemoryCache entries, route/cluster query results,
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
        var tasks = new List<Task>(6);

        // Pre-warm all data stores concurrently
        tasks.Add(WarmupStorageAsync(ct));
        tasks.Add(WarmupQueryCacheAsync(ct));
        tasks.Add(WarmupProxyLogStoreAsync(ct));

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Some warmup tasks failed — application will continue");
        }

        sw.Stop();
        _logger.LogInformation("Application warmup completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Pre-initializes all storage backends, which triggers SQLite connection pool
    /// creation and Redis connection establishment during startup instead of on first request.
    /// </summary>
    private async Task WarmupStorageAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();

            // Trigger StructuredSqliteStore initialization
            if (scope.ServiceProvider.GetService<IStructuredDataStore>() is IStructuredDataStore store)
            {
                await store.InitializeAsync(ct);
                _logger.LogDebug("StructuredSqliteStore warmup done");
            }

            // Trigger IDataStore initialization
            if (scope.ServiceProvider.GetService<IDataStore>() is IDataStore dataStore)
            {
                await dataStore.InitializeAsync(ct);
                _logger.LogDebug("IDataStore warmup done");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Storage warmup failed");
        }
    }

    /// <summary>
    /// Triggers the route and cluster query services to populate their in-memory cache
    /// during startup, so the first dashboard request is served from cache instantly.
    /// </summary>
    private async Task WarmupQueryCacheAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var routeQuery = scope.ServiceProvider.GetService<IDashboardRouteQueryService>();
            var clusterQuery = scope.ServiceProvider.GetService<IDashboardClusterQueryService>();

            // Warmup in parallel
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

    /// <summary>
    /// Pre-touches the proxy log store to ensure the ring buffer is ready.
    /// </summary>
    private async Task WarmupProxyLogStoreAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var logStore = scope.ServiceProvider.GetService<IProxyLogStore>();

            if (logStore != null)
            {
                // Access recent entries to trigger ring buffer readiness
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
