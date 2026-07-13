using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using Aneiang.Yarp.Models;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

public partial class GatewayToolExecutor
{
    // ===================== CIRCUIT BREAKER TOOLS =====================

    private object ExecuteGetCircuitStatus()
    {
        var allStates = _circuitStore.GetAll();
        var openCircuits = allStates.Count(s => s.Value.Status == CircuitStatus.Open);
        var halfOpen = allStates.Count(s => s.Value.Status == CircuitStatus.HalfOpen);
        var closed = allStates.Count(s => s.Value.Status == CircuitStatus.Closed);

        return new
        {
            total = allStates.Count,
            open = openCircuits,
            half_open = halfOpen,
            closed,
            circuits = allStates.Select(s => new
            {
                key = s.Key,
                cluster = s.Value.ClusterKeySnapshot,
                status = s.Value.Status.ToString(),
                consecutive_failures = s.Value.ConsecutiveFailures,
                failure_threshold = s.Value.FailureThreshold
            })
        };
    }

    private async Task<object> ExecuteCreateCircuitBreakerAsync(ToolArgs args)
    {
        var clusterId = args.Get("cluster_id");

        var clusters = _clusterQuery.GetClusters();
        if (clusters.All(c => !string.Equals(c.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase)))
            return new { success = false, message = $"Cluster '{clusterId}' not found. Create the cluster first." };

        var enabled = args.GetBool("enabled", true);

        if (!enabled)
        {
            var removed = await _dynamicConfig.UpdateClusterCircuitBreakerAsync(clusterId, null);
            return new
            {
                success = removed,
                cluster_id = clusterId,
                message = removed
                    ? $"Circuit breaker disabled for cluster '{clusterId}'."
                    : $"Failed to disable circuit breaker for cluster '{clusterId}'."
            };
        }

        var config = new CircuitBreakerConfig
        {
            Enabled = true,
            FailureThreshold = args.GetInt("failure_threshold", 5),
            RecoveryTimeoutSeconds = args.GetInt("recovery_timeout_seconds", 30),
            HalfOpenMaxAttempts = args.GetInt("half_open_max_attempts", 1),
            FailureStatusCodes = args.GetIntArray("failure_status_codes", new List<int> { 500, 502, 503, 504 })
        };

        var success = await _dynamicConfig.UpdateClusterCircuitBreakerAsync(clusterId, config);
        return new
        {
            success,
            cluster_id = clusterId,
            config = new
            {
                enabled = true,
                failure_threshold = config.FailureThreshold,
                recovery_timeout_seconds = config.RecoveryTimeoutSeconds,
                half_open_max_attempts = config.HalfOpenMaxAttempts,
                failure_status_codes = config.FailureStatusCodes
            },
            message = success
                ? $"Circuit breaker created for cluster '{clusterId}': threshold={config.FailureThreshold}, recovery={config.RecoveryTimeoutSeconds}s, half-open max={config.HalfOpenMaxAttempts}."
                : $"Failed to create circuit breaker for cluster '{clusterId}'."
        };
    }

    private object ExecuteResetCircuitBreaker(ToolArgs args)
    {
        var clusterId = args.GetString("cluster_id");

        if (!string.IsNullOrEmpty(clusterId))
        {
            if (_circuitStore.TryGet(clusterId, out var state) && state != null)
            {
                lock (state.SyncRoot)
                {
                    state.Status = CircuitStatus.Closed;
                    state.ConsecutiveFailures = 0;
                    state.HalfOpenRequests = 0;
                }
                return new
                {
                    cluster_id = clusterId,
                    status = "Closed",
                    message = $"Circuit for '{clusterId}' reset to Closed."
                };
            }
            return new { cluster_id = clusterId, message = $"No circuit found for '{clusterId}'." };
        }

        _circuitStore.ResetAll();
        var total = _circuitStore.Count;
        return new
        {
            cluster_id = "all",
            total,
            message = $"All {total} circuit(s) reset to Closed."
        };
    }
}
