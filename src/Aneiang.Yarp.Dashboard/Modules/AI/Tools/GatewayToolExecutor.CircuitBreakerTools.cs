using Aneiang.Yarp.Dashboard.Modules.CircuitBreaker.Middleware;
using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure.Plugin;
using Aneiang.Yarp.Storage.Entities;

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

        var cluster = _dynamicConfig.GetDynamicConfig()?.Clusters.FirstOrDefault(item =>
            string.Equals(item.Config.ClusterId, clusterId, StringComparison.OrdinalIgnoreCase));
        if (cluster == null)
            return new { success = false, message = $"Cluster '{clusterId}' not found. Create the cluster first." };

        var existing = (await _pluginBindings.GetBindingsAsync()).FirstOrDefault(binding =>
            binding.Scope == PluginBindingScope.Cluster &&
            string.Equals(binding.PluginId, "circuit-breaker", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(binding.ClusterUid, cluster.ClusterUid, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(binding.ScopeId, clusterId, StringComparison.OrdinalIgnoreCase)));
        var enabled = args.GetBool("enabled", true);
        if (!enabled)
        {
            var removed = existing != null && await _bindingMutations.DeleteAsync(existing.Id);
            return new
            {
                success = existing == null || removed,
                cluster_id = clusterId,
                message = existing == null || removed
                    ? $"Circuit breaker plugin binding disabled for cluster '{clusterId}'."
                    : $"Failed to disable circuit breaker for cluster '{clusterId}'."
            };
        }

        var manifest = _pluginManager.GetManifest("circuit-breaker");
        if (manifest == null)
            return new { success = false, message = "Circuit breaker plugin manifest is unavailable." };
        var config = new
        {
            enabled = true,
            failureThreshold = args.GetInt("failure_threshold", 5),
            recoveryTimeoutSeconds = args.GetInt("recovery_timeout_seconds", 30),
            halfOpenMaxAttempts = args.GetInt("half_open_max_attempts", 1),
            failureRatio = 0.5,
            minimumThroughput = Math.Max(1, args.GetInt("failure_threshold", 5)),
            samplingDurationSeconds = 30,
            failureStatusCodes = args.GetIntArray("failure_status_codes", new List<int> { 500, 502, 503, 504 })
        };
        var binding = new PluginBindingEntity
        {
            Id = existing?.Id ?? $"cluster:{cluster.ClusterUid}:circuit-breaker",
            PluginId = "circuit-breaker",
            PluginVersion = manifest.Version,
            Scope = PluginBindingScope.Cluster,
            ScopeId = clusterId,
            ClusterUid = cluster.ClusterUid,
            ConfigJson = JsonSerializer.Serialize(config),
            SchemaVersion = manifest.Schemas.FirstOrDefault()?.Version ?? 1,
            Enabled = true,
            Order = existing?.Order ?? 0,
            CreatedAt = existing?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _bindingMutations.UpsertAsync(binding);
        return new
        {
            success = true,
            cluster_id = clusterId,
            config,
            message = $"Circuit breaker plugin binding created for cluster '{clusterId}'."
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
