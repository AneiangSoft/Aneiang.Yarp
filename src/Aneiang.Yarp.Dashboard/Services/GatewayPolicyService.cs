using System.Collections.Concurrent;
using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Storage;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Services;

/// <summary>
/// Service for managing gateway policies.
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
/// In-memory implementation of gateway policy service with JSON file persistence.
/// </summary>
public class GatewayPolicyService : IGatewayPolicyService
{
    private const string Category = "gateway-policies";
    private readonly IDataStore _store;
    private readonly ILogger<GatewayPolicyService> _logger;
    private GatewayPolicyCollection? _policies;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GatewayPolicyService(IDataStore store, ILogger<GatewayPolicyService> logger)
    {
        _store = store;
        _logger = logger;
    }

    private async Task<GatewayPolicyCollection> LoadOrCreateAsync()
    {
        if (_policies != null)
            return _policies;

        await _lock.WaitAsync();
        try
        {
            if (_policies != null)
                return _policies;

            _policies = await _store.GetDocumentAsync<GatewayPolicyCollection>(Category) ?? new GatewayPolicyCollection();
            _logger.LogInformation("Loaded {Count} policies from storage", _policies.Policies.Count);
            return _policies;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveAsync(GatewayPolicyCollection policies)
    {
        policies.LastModified = DateTime.UtcNow;
        await _store.SetDocumentAsync(Category, policies);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GatewayPolicy>> GetAllPoliciesAsync()
    {
        var policies = await LoadOrCreateAsync();
        return policies.Policies.AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<GatewayPolicy?> GetPolicyAsync(string policyId)
    {
        var policies = await LoadOrCreateAsync();
        return policies.Policies.FirstOrDefault(p =>
            string.Equals(p.PolicyId, policyId, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<GatewayPolicy> CreatePolicyAsync(GatewayPolicy policy)
    {
        var policies = await LoadOrCreateAsync();

        if (string.IsNullOrWhiteSpace(policy.PolicyId))
        {
            policy.PolicyId = Guid.NewGuid().ToString("N")[..12];
        }

        if (policies.Policies.Any(p => string.Equals(p.PolicyId, policy.PolicyId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Policy with ID '{policy.PolicyId}' already exists");
        }

        policy.CreatedAt = DateTime.UtcNow;
        policies.Policies.Add(policy);

        await SaveAsync(policies);
        _logger.LogInformation("Created policy '{PolicyId}' ({Name})", policy.PolicyId, policy.DisplayName);

        return policy;
    }

    /// <inheritdoc />
    public async Task<GatewayPolicy?> UpdatePolicyAsync(string policyId, GatewayPolicy policy)
    {
        var policies = await LoadOrCreateAsync();
        var existing = policies.Policies.FirstOrDefault(p =>
            string.Equals(p.PolicyId, policyId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return null;

        var index = policies.Policies.IndexOf(existing);
        policy.PolicyId = existing.PolicyId;
        policy.CreatedAt = existing.CreatedAt;
        policy.CreatedBy = existing.CreatedBy;
        policies.Policies[index] = policy;

        await SaveAsync(policies);
        _logger.LogInformation("Updated policy '{PolicyId}'", policyId);

        return policy;
    }

    /// <inheritdoc />
    public async Task<bool> DeletePolicyAsync(string policyId)
    {
        var policies = await LoadOrCreateAsync();
        var existing = policies.Policies.FirstOrDefault(p =>
            string.Equals(p.PolicyId, policyId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return false;

        policies.Policies.Remove(existing);
        await SaveAsync(policies);
        _logger.LogInformation("Deleted policy '{PolicyId}'", policyId);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ApplyPolicyToRouteAsync(string policyId, string routeId)
    {
        var policy = await GetPolicyAsync(policyId);
        if (policy == null)
            return false;

        _logger.LogInformation(
            "Applying policy '{PolicyId}' to route '{RouteId}'",
            policyId, routeId);

        // Note: This would need integration with DynamicYarpConfigService to actually apply metadata to routes
        // For now, we log the intent. The route would be updated with policy metadata.

        return true;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Dictionary<string, string>>> GetRouteMetadataAsync(string routeId)
    {
        // This would retrieve the effective metadata for a route after policy application
        return Task.FromResult<IReadOnlyList<Dictionary<string, string>>>(Array.Empty<Dictionary<string, string>>());
    }
}
