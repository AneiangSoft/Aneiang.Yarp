using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Models;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Infrastructure.Performance;
using Aneiang.Yarp.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.Operations.Controllers;

/// <summary>
/// Alert monitoring API — provides alert summary for the top alert bar.
/// </summary>
[Route("api/operations")]
[ApiController]
public class OperationsAlertController : ControllerBase
{
    private readonly IDashboardLogQueryService _logQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IMemoryCache _memoryCache;
    private readonly IProxyLogRepository _logRepository;
    private readonly LockFreeStatistics _statistics;
    private readonly DashboardOptions _options;
    private readonly ICircuitStateStore _circuitStore;

    public OperationsAlertController(
        IDashboardLogQueryService logQuery,
        IDashboardClusterQueryService clusterQuery,
        IMemoryCache memoryCache,
        IProxyLogRepository logRepository,
        LockFreeStatistics statistics,
        ICircuitStateStore circuitStore,
        IOptions<DashboardOptions> options)
    {
        _logQuery = logQuery;
        _clusterQuery = clusterQuery;
        _memoryCache = memoryCache;
        _logRepository = logRepository;
        _statistics = statistics;
        _circuitStore = circuitStore;
        _options = options.Value;
    }

    [HttpGet("alert-summary")]
    public IActionResult GetAlertSummary()
    {
        var data = _memoryCache.GetOrCreate("ops:alert-summary", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10);
            return ComputeAlertSummary();
        })!;
        return Ok(new { code = 200, data });
    }

    private AlertSummaryData ComputeAlertSummary()
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
            try { recentErrors = _logRepository.GetRecent5xxCountAsync(5).GetAwaiter().GetResult(); }
            catch { recentErrors = (int)total5xx; }
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

    private class AlertSummaryData
    {
        public int UnhealthyCount { get; set; }
        public int UnhealthyTotal { get; set; }
        public int CircuitBreakerCount { get; set; }
        public int RecentErrors { get; set; }
        public int UnhandledEvents { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
