using System.Diagnostics;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.Notification.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Warmup service that runs during application startup to eliminate cold-start latency.
/// Initializes: repositories (SQLite tables via lazy-init), MemoryCache entries, route/cluster query results,
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

    /// <summary>Runs all warmup tasks in parallel to eliminate cold-start latency.</summary>
    public async Task StartAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tasks = new List<Task>(4);

        tasks.Add(WarmupRepositoryAsync(ct));
        tasks.Add(WarmupQueryCacheAsync(ct));
        tasks.Add(WarmupProxyLogStoreAsync(ct));
        tasks.Add(WarmupNotificationRulesAsync(ct));

        try { await Task.WhenAll(tasks); }
        catch (Exception ex) { _logger.LogWarning(ex, "Some warmup tasks failed — application will continue"); }

        sw.Stop();
        _logger.LogInformation("Application warmup completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    /// <summary>Stops the warmup service (no-op).</summary>
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private async Task WarmupRepositoryAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();

            // Trigger lazy initialization for key repositories (table creation happens on first query)
            var notificationRepo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var routeRepo = scope.ServiceProvider.GetRequiredService<IRouteRepository>();
            var clusterRepo = scope.ServiceProvider.GetRequiredService<IClusterRepository>();
            var policyRepo = scope.ServiceProvider.GetRequiredService<IPolicyRepository>();
            var historyRepo = scope.ServiceProvider.GetRequiredService<IConfigHistoryRepository>();
            var auditRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
            var wafRepo = scope.ServiceProvider.GetRequiredService<IWafSettingsRepository>();

            await Task.WhenAll(
                notificationRepo.GetRulesAsync(ct),
                routeRepo.GetAllRoutesAsync(),
                clusterRepo.GetAllClustersAsync(),
                policyRepo.GetAllPoliciesAsync(),
                historyRepo.GetConfigHistoryListAsync(1),
                auditRepo.GetAuditLogsAsync(1),
                wafRepo.GetWafSettingsAsync(ct)
            );
            _logger.LogDebug("Repository warmup done");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repository warmup failed");
        }
    }

    /// <summary>Warms up dashboard query cache entries.</summary>
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


    /// <summary>Warms up notification rules and seeds a default rule if none exist.</summary>
    private async Task WarmupNotificationRulesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

            // ── 1. Ensure notification global settings are explicitly enabled ──
            try
            {
                var gs = await repo.GetGlobalSettingsAsync(ct);
                if (gs == null) gs = new NotificationGlobalSettings();
                gs.Enabled = true;
                await repo.SaveGlobalSettingsAsync(gs, ct);
                _logger.LogDebug("Notification global settings initialized: Enabled=true, Locale={Locale}", gs.Locale);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize notification global settings");
            }

            var channels = await repo.GetChannelsAsync(ct);
            var rules = await repo.GetRulesAsync(ct);

            // ── 2. Seed a default rule for all common events if no rules exist ──
            // The rule always records to history; channelIds are populated only if channels exist.
            if (rules.Count == 0)
            {
                var defaultRule = new NotificationRule
                {
                    Id = "default-all-events",
                    Name = channels.Count > 0 ? "默认通知规则" : "默认通知规则（需配置渠道）",
                    Enabled = true,
                    EventTypes = new List<string>
                    {
                        "AddRoute", "UpdateRoute", "RemoveRoute",
                        "AddCluster", "UpdateCluster", "RemoveCluster",
                        "RollbackConfig",
                        "CircuitBreakerOpen",
                        "RetryExhausted",
                        "WafBlock",
                        "ProxyError",
                        "RateLimitExceeded"
                    },
                    MinSeverity = NotificationSeverity.Info,
                    ChannelIds = channels.Select(c => c.Id).ToList(),
                    CooldownSeconds = 60,
                    RecordToHistory = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                try { await repo.SaveRuleAsync(defaultRule, ct); }
                catch { /* rule may already exist */ }

                if (channels.Count > 0)
                    _logger.LogInformation("Default notification rule seeded ({ChannelCount} channels)", defaultRule.ChannelIds.Count);
                else
                    _logger.LogWarning("Default notification rule seeded but no channels configured — history will be recorded; push requires at least one channel");
            }

            _logger.LogDebug("NotificationRules warmup done ({RuleCount} rules, {ChannelCount} channels)", rules.Count, channels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotificationRules warmup failed");
        }
    }
}
