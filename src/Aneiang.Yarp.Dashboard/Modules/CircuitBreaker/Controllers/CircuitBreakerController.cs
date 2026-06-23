using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Controllers;

/// <summary>
/// API controller for circuit breaker status monitoring and management.
/// </summary>
[Route("api/circuit-breaker")]
public class CircuitBreakerController : Controller
{
    private readonly IDynamicYarpConfigService _yarpConfig;

    public CircuitBreakerController(IDynamicYarpConfigService yarpConfig)
    {
        _yarpConfig = yarpConfig;
    }

    /// <summary>
    /// Get circuit breaker status for all clusters.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetCircuitBreakerStatus()
    {
        // Ensure circuits exist for all clusters that have CB enabled
        SyncCircuitsFromConfig();

        // Remove circuits for clusters that no longer have CB enabled
        CleanupStaleCircuits();

        var states = CircuitBreakerMiddleware.GetAllCircuitStates();
        var dynConfig = _yarpConfig.GetDynamicConfig();
        var clusters = dynConfig?.Clusters;

        var enriched = states.Select(s =>
        {
            var cluster = clusters?.FirstOrDefault(c =>
                string.Equals(c.ClusterId, s.ClusterKeySnapshot, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.ClusterUid, s.ClusterUid, StringComparison.OrdinalIgnoreCase));
            s.ClusterName = !string.IsNullOrWhiteSpace(cluster?.DisplayName)
                ? cluster.DisplayName
                : s.ClusterKeySnapshot;
            return s;
        });

        return Json(new { code = 200, data = enriched });
    }

    /// <summary>
    /// Reset all circuit breakers to Closed state.
    /// </summary>
    [HttpPost("reset")]
    public IActionResult ResetCircuitBreakers()
    {
        CircuitBreakerMiddleware.ResetAll();
        return Json(new { code = 200, message = "All circuit breakers reset" });
    }

    /// <summary>
    /// Pre-create circuit entries for clusters with CB enabled so they are visible in the dashboard.
    /// </summary>
    private void SyncCircuitsFromConfig()
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        if (dynConfig?.Clusters == null) return;

        foreach (var cluster in dynConfig.Clusters)
        {
            if (cluster.CircuitBreaker is { Enabled: true } cbConfig)
            {
                CircuitBreakerMiddleware.EnsureCircuitExists(cluster.ClusterId, cbConfig, cluster.ClusterUid);
            }
        }
    }

    /// <summary>
    /// Remove circuit entries for clusters that no longer have CB enabled.
    /// </summary>
    private void CleanupStaleCircuits()
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        if (dynConfig?.Clusters == null) return;

        var cbEnabledClusters = dynConfig.Clusters
            .Where(c => c.CircuitBreaker is { Enabled: true })
            .Select(c => new { c.ClusterId, c.ClusterUid })
            .ToList();

        var allStates = CircuitBreakerMiddleware.GetAllCircuitStates();
        foreach (var state in allStates)
        {
            var stillEnabled = cbEnabledClusters.Any(c =>
                string.Equals(c.ClusterId, state.ClusterKeySnapshot, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(c.ClusterUid)
                    && string.Equals(c.ClusterUid, state.ClusterUid, StringComparison.OrdinalIgnoreCase)));

            if (!stillEnabled)
            {
                CircuitBreakerMiddleware.RemoveCircuitsForCluster(state.ClusterKeySnapshot, state.ClusterUid);
            }
        }
    }
}
