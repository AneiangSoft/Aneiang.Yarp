using Aneiang.Yarp.Dashboard.Modules.Policy.Models;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Tools;

public partial class GatewayToolExecutor
{
    // ===================== POLICY TOOLS =====================

    private async Task<object> ExecuteGetPoliciesAsync()
    {
        var routePolicies = await _policyService.GetAllRoutePoliciesAsync();
        var clusterPolicies = await _policyService.GetAllClusterPoliciesAsync();

        return new
        {
            route_policies_count = routePolicies.Count,
            cluster_policies_count = clusterPolicies.Count,
            route_policies = routePolicies.Select(p => new
            {
                policy_id = p.PolicyId,
                name = p.DisplayName,
                description = p.Description,
                enabled = p.Enabled,
                applied_routes = p.AppliedRoutes,
                has_retry = p.Retry != null,
                has_rate_limit = p.RateLimit != null,
                waf_enabled = p.WafEnabled
            }),
            cluster_policies = clusterPolicies.Select(p => new
            {
                policy_id = p.PolicyId,
                name = p.DisplayName,
                description = p.Description,
                enabled = p.Enabled,
                applied_clusters = p.AppliedClusters,
                circuit_breaker = p.CircuitBreaker != null ? new
                {
                    enabled = p.CircuitBreaker.Enabled,
                    failure_threshold = p.CircuitBreaker.FailureThreshold,
                    recovery_timeout_seconds = p.CircuitBreaker.RecoveryTimeoutSeconds,
                    half_open_max_attempts = p.CircuitBreaker.HalfOpenMaxAttempts
                } : null
            })
        };
    }

    private async Task<object> ExecuteCreateClusterPolicyAsync(ToolArgs args)
    {
        var policyId = args.GetString("policy_id");
        var name = args.Get("name");
        var description = args.GetString("description");

        var policy = new ClusterPolicy
        {
            PolicyId = policyId ?? Guid.NewGuid().ToString("N")[..12],
            DisplayName = name,
            Description = description,
            Enabled = true,
            CircuitBreaker = new PolicyCircuitBreaker
            {
                Enabled = true,
                FailureThreshold = args.GetInt("failure_threshold", 5),
                RecoveryTimeoutSeconds = args.GetInt("recovery_timeout_seconds", 30),
                HalfOpenMaxAttempts = args.GetInt("half_open_max_attempts", 1),
                FailureStatusCodes = args.GetIntArray("failure_status_codes", new List<int> { 500, 502, 503, 504 })
            }
        };

        var created = await _policyService.CreateClusterPolicyAsync(policy);

        // Auto-apply to clusters if cluster_ids provided
        var appliedClusters = new List<string>();
        var failedClusters = new List<string>();
        foreach (var cid in args.GetStringArray("cluster_ids"))
        {
            var ok = await _policyService.ApplyClusterPolicyAsync(created.PolicyId, cid);
            if (ok) appliedClusters.Add(cid); else failedClusters.Add(cid);
        }

        var appliedMsg = appliedClusters.Count > 0 ? $" Applied to: {string.Join(", ", appliedClusters)}." : "";
        var failedMsg = failedClusters.Count > 0 ? $" Failed on: {string.Join(", ", failedClusters)}." : "";

        return new
        {
            success = true,
            policy_id = created.PolicyId,
            name = created.DisplayName,
            applied_clusters = appliedClusters,
            failed_clusters = failedClusters,
            message = $"Cluster policy '{created.DisplayName}' created (ID: {created.PolicyId}).{appliedMsg}{failedMsg}"
        };
    }

    private async Task<object> ExecuteApplyClusterPolicyAsync(ToolArgs args)
    {
        var policyId = args.Get("policy_id");
        var clusterId = args.Get("cluster_id");

        var success = await _policyService.ApplyClusterPolicyAsync(policyId, clusterId);
        return new
        {
            success,
            policy_id = policyId,
            cluster_id = clusterId,
            message = success
                ? $"Cluster policy '{policyId}' applied to cluster '{clusterId}'."
                : $"Failed to apply policy '{policyId}' to cluster '{clusterId}'."
        };
    }

    private async Task<object> ExecuteCreateRoutePolicyAsync(ToolArgs args)
    {
        var policyId = args.GetString("policy_id");
        var name = args.Get("name");
        var description = args.GetString("description");

        var policy = new RoutePolicy
        {
            PolicyId = policyId ?? Guid.NewGuid().ToString("N")[..12],
            DisplayName = name,
            Description = description,
            Enabled = true
        };

        // Optional retry config
        if (args.GetBool("retry_enabled"))
        {
            policy.Retry = new PolicyRetry
            {
                Enabled = true,
                MaxRetries = args.GetInt("max_retries", 3),
                BackoffBaseMs = args.GetInt("backoff_base_ms", 100),
                RetryStatusCodes = args.GetIntArray("retry_status_codes", new List<int> { 502, 503, 504 })
            };
        }

        // Optional rate limit config
        if (args.GetBool("rate_limit_enabled"))
        {
            policy.RateLimit = new PolicyRateLimit
            {
                Enabled = true,
                PermitLimit = args.GetInt("permit_limit", 100),
                Window = args.GetString("window") ?? "1m",
                Algorithm = args.GetString("algorithm") ?? "FixedWindow"
            };
        }

        var created = await _policyService.CreateRoutePolicyAsync(policy);

        // Auto-apply to routes if route_ids provided
        var appliedRoutes = new List<string>();
        var failedRoutes = new List<string>();
        foreach (var rid in args.GetStringArray("route_ids"))
        {
            var ok = await _policyService.ApplyRoutePolicyAsync(created.PolicyId, rid);
            if (ok) appliedRoutes.Add(rid); else failedRoutes.Add(rid);
        }

        var appliedMsg = appliedRoutes.Count > 0 ? $" Applied to: {string.Join(", ", appliedRoutes)}." : "";
        var failedMsg = failedRoutes.Count > 0 ? $" Failed on: {string.Join(", ", failedRoutes)}." : "";

        return new
        {
            success = true,
            policy_id = created.PolicyId,
            name = created.DisplayName,
            applied_routes = appliedRoutes,
            failed_routes = failedRoutes,
            message = $"Route policy '{created.DisplayName}' created (ID: {created.PolicyId}).{appliedMsg}{failedMsg}"
        };
    }

    private async Task<object> ExecuteApplyRoutePolicyAsync(ToolArgs args)
    {
        var policyId = args.Get("policy_id");
        var routeId = args.Get("route_id");

        var success = await _policyService.ApplyRoutePolicyAsync(policyId, routeId);
        return new
        {
            success,
            policy_id = policyId,
            route_id = routeId,
            message = success
                ? $"Route policy '{policyId}' applied to route '{routeId}'."
                : $"Failed to apply policy '{policyId}' to route '{routeId}'."
        };
    }

    private async Task<object> ExecuteDeletePolicyAsync(ToolArgs args)
    {
        var policyId = args.Get("policy_id");
        var type = args.GetString("type") ?? "auto";

        // Try cluster first, then route
        if (type == "cluster" || type == "auto")
        {
            var deleted = await _policyService.DeleteClusterPolicyAsync(policyId);
            if (deleted)
                return new { success = true, policy_id = policyId, type = "cluster", message = $"Cluster policy '{policyId}' deleted." };
        }

        if (type == "route" || type == "auto")
        {
            var deleted = await _policyService.DeleteRoutePolicyAsync(policyId);
            if (deleted)
                return new { success = true, policy_id = policyId, type = "route", message = $"Route policy '{policyId}' deleted." };
        }

        return new { success = false, policy_id = policyId, message = $"Policy '{policyId}' not found." };
    }
}
