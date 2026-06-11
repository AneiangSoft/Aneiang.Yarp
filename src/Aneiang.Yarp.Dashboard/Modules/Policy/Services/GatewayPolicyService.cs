using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Services;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Service for managing gateway policies using structured storage.
/// </summary>
public interface IGatewayPolicyService
{
    Task<IReadOnlyList<GatewayPolicy>> GetAllPoliciesAsync();
    Task<GatewayPolicy?> GetPolicyAsync(string policyId);
    Task<GatewayPolicy> CreatePolicyAsync(GatewayPolicy policy);
    Task<GatewayPolicy?> UpdatePolicyAsync(string policyId, GatewayPolicy policy);
    Task<bool> DeletePolicyAsync(string policyId);
    Task<bool> ApplyPolicyToRouteAsync(string policyId, string routeId);
    Task<IReadOnlyList<Dictionary<string, string>>> GetRouteMetadataAsync(string routeId);
}

/// <summary>
/// Implementation of gateway policy service with structured storage.
/// Integrates with DynamicYarpConfigService to apply policy metadata to routes.
/// </summary>
public class GatewayPolicyService : IGatewayPolicyService
{
    private readonly IStructuredDataStore _store;
    private readonly DynamicYarpConfigService _yarpConfig;
    private readonly ILogger<GatewayPolicyService> _logger;

    public GatewayPolicyService(
        IStructuredDataStore store,
        DynamicYarpConfigService yarpConfig,
        ILogger<GatewayPolicyService> logger)
    {
        _store = store;
        _yarpConfig = yarpConfig;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GatewayPolicy>> GetAllPoliciesAsync()
    {
        var entities = await _store.GetAllPoliciesAsync();
        return entities.ToGatewayPolicies().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<GatewayPolicy?> GetPolicyAsync(string policyId)
    {
        var entity = await _store.GetPolicyAsync(policyId);
        return entity?.ToGatewayPolicy();
    }

    /// <inheritdoc />
    public async Task<GatewayPolicy> CreatePolicyAsync(GatewayPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(policy.PolicyId))
        {
            policy.PolicyId = Guid.NewGuid().ToString("N")[..12];
        }

        var existing = await _store.GetPolicyAsync(policy.PolicyId);
        if (existing != null)
        {
            throw new InvalidOperationException($"Policy with ID '{policy.PolicyId}' already exists");
        }

        policy.CreatedAt = DateTime.UtcNow;
        await _store.SavePolicyAsync(policy.ToEntity());

        _logger.LogInformation("Created policy '{PolicyId}' ({Name})", policy.PolicyId, policy.DisplayName);
        return policy;
    }

    /// <inheritdoc />
    public async Task<GatewayPolicy?> UpdatePolicyAsync(string policyId, GatewayPolicy policy)
    {
        var existing = await _store.GetPolicyAsync(policyId);
        if (existing == null)
            return null;

        policy.PolicyId = policyId;
        policy.CreatedAt = existing.CreatedAt;
        policy.CreatedBy = existing.CreatedBy;

        await _store.SavePolicyAsync(policy.ToEntity());
        _logger.LogInformation("Updated policy '{PolicyId}'", policyId);

        return policy;
    }

    /// <inheritdoc />
    public async Task<bool> DeletePolicyAsync(string policyId)
    {
        var existing = await _store.GetPolicyAsync(policyId);
        if (existing == null)
            return false;

        await _store.DeletePolicyAsync(policyId);
        _logger.LogInformation("Deleted policy '{PolicyId}'", policyId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ApplyPolicyToRouteAsync(string policyId, string routeId)
    {
        var policy = await GetPolicyAsync(policyId);
        if (policy == null)
        {
            _logger.LogWarning("ApplyPolicyToRouteAsync: policy '{PolicyId}' not found", policyId);
            return false;
        }

        if (!policy.Enabled)
        {
            _logger.LogWarning("ApplyPolicyToRouteAsync: policy '{PolicyId}' is disabled", policyId);
            return false;
        }

        // Merge all enabled feature metadata into a flat dictionary
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (policy.CircuitBreaker?.Enabled == true)
        {
            foreach (var kvp in policy.CircuitBreaker.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (policy.Retry?.Enabled == true)
        {
            foreach (var kvp in policy.Retry.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (policy.RateLimit?.Enabled == true)
        {
            foreach (var kvp in policy.RateLimit.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (policy.Waf?.Enabled == true)
        {
            foreach (var kvp in policy.Waf.ToMetadata())
                metadata[kvp.Key] = kvp.Value;
        }

        if (metadata.Count == 0)
        {
            _logger.LogWarning("ApplyPolicyToRouteAsync: policy '{PolicyId}' has no enabled features", policyId);
            return false;
        }

        // Tag the route with the applied policy ID so we know which policy is in effect
        metadata["Policy:Id"] = policy.PolicyId;
        metadata["Policy:Name"] = policy.DisplayName;

        var success = await _yarpConfig.UpdateRouteMetadataAsync(routeId, metadata);

        if (success)
        {
            _logger.LogInformation(
                "Policy '{PolicyId}' ({Name}) applied to route '{RouteId}': {Keys}",
                policyId, policy.DisplayName, routeId, string.Join(", ", metadata.Keys));
        }

        return success;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Dictionary<string, string>>> GetRouteMetadataAsync(string routeId)
    {
        var config = _yarpConfig.GetDynamicConfig();
        var route = config?.Routes.FirstOrDefault(r =>
            r.RouteId.Equals(routeId, StringComparison.OrdinalIgnoreCase));

        if (route == null || route.Metadata.Count == 0)
            return Task.FromResult<IReadOnlyList<Dictionary<string, string>>>(Array.Empty<Dictionary<string, string>>());

        return Task.FromResult<IReadOnlyList<Dictionary<string, string>>>(
            new List<Dictionary<string, string>> { route.Metadata });
    }
}
