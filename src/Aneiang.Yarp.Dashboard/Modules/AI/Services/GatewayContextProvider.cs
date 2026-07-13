using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Services;
using Aneiang.Yarp.Dashboard.Modules.ProxyLog.Services;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Services;

/// <summary>
/// Collects real-time gateway context to inject into AI system prompts.
/// Provides routes, clusters, circuit states, recent errors, plugin status, etc.
/// </summary>
public class GatewayContextProvider
{
    private readonly IDashboardRouteQueryService _routeQuery;
    private readonly IDashboardClusterQueryService _clusterQuery;
    private readonly IDashboardLogQueryService _logQuery;
    private readonly ICircuitStateStore _circuitStore;
    private readonly IGatewayPluginManager _pluginManager;
    private readonly ILogger<GatewayContextProvider> _logger;

    public GatewayContextProvider(
        IDashboardRouteQueryService routeQuery,
        IDashboardClusterQueryService clusterQuery,
        IDashboardLogQueryService logQuery,
        ICircuitStateStore circuitStore,
        IGatewayPluginManager pluginManager,
        ILogger<GatewayContextProvider> logger)
    {
        _routeQuery = routeQuery;
        _clusterQuery = clusterQuery;
        _logQuery = logQuery;
        _circuitStore = circuitStore;
        _pluginManager = pluginManager;
        _logger = logger;
    }

    /// <summary>
    /// Build a structured text summary of the current gateway state
    /// for injection into the AI system prompt.
    /// </summary>
    public Task<string> BuildContextAsync(CancellationToken ct = default)
    {
        try
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("=== Gateway Current State ===");
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();

            // Routes summary
            AppendRoutesSummary(sb);

            // Clusters summary
            AppendClustersSummary(sb);

            // Circuit breaker states
            AppendCircuitBreakerSummary(sb);

            // Recent logs summary
            AppendRecentLogsSummary(sb);

            // Plugin status
            AppendPluginStatus(sb);

            return Task.FromResult(sb.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build gateway context for AI");
            return Task.FromResult("[Gateway context unavailable due to error]");
        }
    }

    private void AppendRoutesSummary(StringBuilder sb)
    {
        sb.AppendLine("## Routes");
        try
        {
            var routes = _routeQuery.GetRoutes();
            sb.AppendLine($"Total: {routes.Count} routes");
            foreach (var r in routes.Take(20))
            {
                sb.AppendLine($"  - {r.RouteId}: Path={r.Match?.Path ?? "*"}, Cluster={r.ClusterId}, Source={r.Source}");
            }
            if (routes.Count > 20)
                sb.AppendLine($"  ... and {routes.Count - 20} more");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [Error loading routes: {ex.Message}]");
        }
        sb.AppendLine();
    }

    private void AppendClustersSummary(StringBuilder sb)
    {
        sb.AppendLine("## Clusters");
        try
        {
            var clusters = _clusterQuery.GetClusters();
            sb.AppendLine($"Total: {clusters.Count} clusters");
            foreach (var c in clusters.Take(20))
            {
                var destCount = c.Destinations?.Count ?? 0;
                sb.AppendLine($"  - {c.ClusterId}: {destCount} destinations, LoadBalancing={c.LoadBalancingPolicy}");
            }
            if (clusters.Count > 20)
                sb.AppendLine($"  ... and {clusters.Count - 20} more");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [Error loading clusters: {ex.Message}]");
        }
        sb.AppendLine();
    }

    private void AppendCircuitBreakerSummary(StringBuilder sb)
    {
        sb.AppendLine("## Circuit Breaker States");
        try
        {
            var allStates = _circuitStore.GetAll();
            var openCircuits = allStates.Where(s => s.Value.Status == CircuitStatus.Open).ToList();
            var halfOpenCircuits = allStates.Where(s => s.Value.Status == CircuitStatus.HalfOpen).ToList();

            sb.AppendLine($"Total circuits: {allStates.Count}, Open: {openCircuits.Count}, HalfOpen: {halfOpenCircuits.Count}");

            foreach (var c in openCircuits.Take(10))
            {
                sb.AppendLine($"  OPEN: {c.Key} — Failures={c.Value.ConsecutiveFailures}, RecoveryAt={c.Value.OpenedAt + c.Value.RecoveryTimeout:HH:mm:ss}");
            }
            foreach (var c in halfOpenCircuits.Take(5))
            {
                sb.AppendLine($"  HALF-OPEN: {c.Key} — Probes={c.Value.HalfOpenRequests}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [Error loading circuit states: {ex.Message}]");
        }
        sb.AppendLine();
    }

    private void AppendRecentLogsSummary(StringBuilder sb)
    {
        sb.AppendLine("## Recent Proxy Logs (last 50)");
        try
        {
            var snapshot = _logQuery.GetLogs(50);
            var entries = snapshot.Entries;

            if (entries.Count == 0)
            {
                sb.AppendLine("  No recent logs.");
            }
            else
            {
                // Status code distribution
                var statusGroups = entries
                    .GroupBy(e => e.StatusCode / 100)
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}xx: {g.Count()}");
                sb.AppendLine($"  Status distribution: {string.Join(", ", statusGroups)}");

                // Top error routes
                var errorEntries = entries.Where(e => e.StatusCode >= 400).ToList();
                if (errorEntries.Count > 0)
                {
                    var topErrors = errorEntries
                        .GroupBy(e => e.RouteId ?? "unknown")
                        .OrderByDescending(g => g.Count())
                        .Take(5);
                    sb.AppendLine("  Top error routes:");
                    foreach (var g in topErrors)
                    {
                        sb.AppendLine($"    {g.Key}: {g.Count()} errors");
                    }
                }

                // Latency stats
                var latencies = entries.Where(e => e.ElapsedMs.HasValue && e.ElapsedMs > 0).Select(e => e.ElapsedMs!.Value).OrderBy(l => l).ToList();
                if (latencies.Count > 0)
                {
                    var p50 = latencies[latencies.Count / 2];
                    var p95 = latencies[(int)(latencies.Count * 0.95)];
                    var max = latencies[^1];
                    sb.AppendLine($"  Latency — P50: {p50}ms, P95: {p95}ms, Max: {max}ms");
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [Error loading logs: {ex.Message}]");
        }
        sb.AppendLine();
    }

    private void AppendPluginStatus(StringBuilder sb)
    {
        sb.AppendLine("## Plugins");
        try
        {
            var plugins = _pluginManager.GetAllPlugins();
            foreach (var p in plugins)
            {
                var enabled = _pluginManager.IsPluginEnabled(p.PluginId);
                sb.AppendLine($"  - {p.DisplayName} ({p.PluginId}): {(enabled ? "Enabled" : "Disabled")}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  [Error loading plugins: {ex.Message}]");
        }
    }
}
