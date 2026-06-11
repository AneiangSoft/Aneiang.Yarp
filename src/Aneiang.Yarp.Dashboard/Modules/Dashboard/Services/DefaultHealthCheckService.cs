using Aneiang.Yarp.Models;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Dashboard.Services;

/// <summary>
/// Applies default passive health check configuration to all clusters at startup.
/// When <see cref="DashboardOptions.EnablePassiveHealthCheck"/> is true, this service
/// updates all existing clusters with passive health check settings and refreshes YARP config.
/// </summary>
internal sealed class DefaultHealthCheckService : IHostedService
{
    private readonly DynamicYarpConfigService _dynamicConfig;
    private readonly DashboardOptions _options;
    private readonly ILogger<DefaultHealthCheckService> _logger;

    public DefaultHealthCheckService(
        DynamicYarpConfigService dynamicConfig,
        IOptions<DashboardOptions> options,
        ILogger<DefaultHealthCheckService> logger)
    {
        _dynamicConfig = dynamicConfig;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnablePassiveHealthCheck)
        {
            _logger.LogInformation("Default passive health check is disabled");
            return;
        }

        _logger.LogInformation("Applying default passive health check to all clusters");

        var dynamicConfig = _dynamicConfig.GetDynamicConfig();
        if (dynamicConfig == null) return;

        var reactivationPeriod = TimeSpan.TryParse(_options.PassiveHealthCheckReactivationPeriod, out var period)
            ? period
            : TimeSpan.FromSeconds(30);

        foreach (var cluster in dynamicConfig.Clusters)
        {
            if (cluster.HealthCheck == null)
            {
                cluster.HealthCheck = new HealthCheckConfig
                {
                    Passive = true,
                    PassivePolicy = _options.PassiveHealthCheckPolicy,
                    PassiveReactivationPeriod = reactivationPeriod
                };
            }
            else if (!cluster.HealthCheck.Passive)
            {
                cluster.HealthCheck.Passive = true;
                cluster.HealthCheck.PassivePolicy = _options.PassiveHealthCheckPolicy;
                cluster.HealthCheck.PassiveReactivationPeriod = reactivationPeriod;
            }
        }

        // Refresh YARP config from updated dynamic config
        _dynamicConfig.RefreshConfig();
        await _dynamicConfig.SaveDynamicConfig();

        _logger.LogInformation("Passive health check applied to {Count} clusters", dynamicConfig.Clusters.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
