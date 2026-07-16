using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.Operations.Dtos;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Application;

/// <summary>
/// Application service for computing alert summaries.
/// Encapsulates multi-source (lock-free stats, SQL, in-memory) alert logic
/// previously embedded in <see cref="Controllers.OperationsAlertController"/>.
/// </summary>
public interface IAlertAppService
{
    Task<AlertSummaryData> ComputeAlertSummaryAsync();
}

/// <inheritdoc/>
public class AlertAppService : IAlertAppService
{
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IProxyLogRepository _logRepository;
    private readonly LockFreeStatistics _statistics;
    private readonly DashboardOptions _options;
    private readonly ICircuitStateStore _circuitStore;

    public AlertAppService(
        IDashboardLogQueryService logQuery,
        IDashboardClusterQueryService clusterQuery,
        IProxyLogRepository logRepository,
        LockFreeStatistics statistics,
        ICircuitStateStore circuitStore,
        IOptions<DashboardOptions> options)
    {
        _logQuery = logQuery;
        _clusterQuery = clusterQuery;
        _logRepository = logRepository;
        _statistics = statistics;
        _circuitStore = circuitStore;
        _options = options.Value;
    }

    public async Task<AlertSummaryData> ComputeAlertSummaryAsync()
    {
        var clusters = _clusterQuery.GetClusters();
        int unhealthyCount = 0;
        int totalDestinations = 0;
        foreach (var cluster in clusters)
        {
            if (cluster.Destinations != null)
            {
                foreach (var dest in cluster.Destinations)
                {
                    totalDestinations++;
                    if (IsUnhealthy(dest.Health))
                        unhealthyCount++;
                }
            }
        }

        var statsSnapshot = _statistics.GetSnapshot();
        var total5xx = statsSnapshot.StatusCodes
            .Where(kvp => kvp.Key >= 500)
            .Sum(kvp => kvp.Value);

        int recentErrors;
        if (_options.LogPersistenceEnabled)
        {
            try
            {
                recentErrors = await _logRepository.GetRecent5xxCountAsync(5);
            }
            catch
            {
                recentErrors = (int)total5xx;
            }
        }
        else
        {
            var logSnapshot = _logQuery.GetLogs(1000);
            var fiveMinAgo = DateTime.Now.AddMinutes(-5);
            recentErrors = logSnapshot.Entries.Count(e =>
                e.EventType == LogEventType.ProxyResponse &&
                e.Timestamp >= fiveMinAgo &&
                e.StatusCode >= 500);
        }

        return new AlertSummaryData
        {
            UnhealthyCount = unhealthyCount,
            UnhealthyTotal = totalDestinations,
            CircuitBreakerCount = CountCircuitBreakers(),
            RecentErrors = recentErrors,
            UnhandledEvents = recentErrors + unhealthyCount,
            LastUpdated = DateTime.Now
        };
    }

    private int CountCircuitBreakers()
    {
        var allStates = _circuitStore.GetAllStateInfos();
        int openCount = 0;
        foreach (var state in allStates)
        {
            if (state.Status == "Open" || state.Status == "HalfOpen")
                openCount++;
        }
        return openCount;
    }

    private static bool IsUnhealthy(string? health) =>
        !string.IsNullOrEmpty(health) && health.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase);
}
