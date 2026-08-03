using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Dashboard.Infrastructure.State;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Controllers;

/// <summary>
/// API controller for circuit breaker status monitoring and management.
/// </summary>
[Route("api/circuit-breaker")]
public class CircuitBreakerController : Controller
{
    private readonly IDynamicYarpConfigService _yarpConfig;
    private readonly ICircuitStateStore _circuitStore;
    private readonly GatewayPluginExecutionPlanProvider _executionPlans;

    public CircuitBreakerController(
        IDynamicYarpConfigService yarpConfig,
        ICircuitStateStore circuitStore,
        GatewayPluginExecutionPlanProvider executionPlans)
    {
        _yarpConfig = yarpConfig;
        _circuitStore = circuitStore;
        _executionPlans = executionPlans;
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

        var states = _circuitStore.GetAllStateInfos();
        var dynConfig = _yarpConfig.GetDynamicConfig();
        var clusters = dynConfig?.Clusters;

        var enriched = states.Select(s =>
        {
            var cluster = clusters?.FirstOrDefault(c =>
                string.Equals(c.Config.ClusterId, s.ClusterKeySnapshot, StringComparison.OrdinalIgnoreCase)
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
        _circuitStore.ResetAll();
        return Json(new { code = 200, message = "All circuit breakers reset" });
    }

    /// <summary>
    /// Pre-create circuit entries for clusters with CB enabled so they are visible in the dashboard.
    /// </summary>
    private void SyncCircuitsFromConfig()
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        foreach (var (clusterId, cbConfig) in _executionPlans.Current.CircuitBreakerByCluster)
        {
            if (!cbConfig.Enabled) continue;
            var clusterUid = dynConfig?.Clusters.FirstOrDefault(cluster =>
                string.Equals(cluster.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase))?.ClusterUid;
            _circuitStore.EnsureCircuitExists(clusterId, cbConfig, clusterUid);
        }
    }

    /// <summary>
    /// Remove circuit entries for clusters that no longer have CB enabled.
    /// </summary>
    private void CleanupStaleCircuits()
    {
        var dynConfig = _yarpConfig.GetDynamicConfig();
        var cbEnabledClusters = _executionPlans.Current.CircuitBreakerByCluster
            .Where(item => item.Value.Enabled)
            .Select(item => new
            {
                ClusterId = item.Key,
                ClusterUid = dynConfig?.Clusters.FirstOrDefault(cluster =>
                    string.Equals(cluster.Config.ClusterId, item.Key, StringComparison.OrdinalIgnoreCase))?.ClusterUid
            })
            .ToList();

        var allStates = _circuitStore.GetAllStateInfos();
        foreach (var state in allStates)
        {
            var stillEnabled = cbEnabledClusters.Any(c =>
                string.Equals(c.ClusterId, state.ClusterKeySnapshot, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(c.ClusterUid)
                    && string.Equals(c.ClusterUid, state.ClusterUid, StringComparison.OrdinalIgnoreCase)));

            if (!stillEnabled)
            {
                _circuitStore.RemoveForCluster(state.ClusterKeySnapshot, state.ClusterUid);
            }
        }
    }
}
