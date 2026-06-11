using Aneiang.Yarp.Dashboard.Infrastructure.Storage;
using Aneiang.Yarp.Dashboard.Modules.Policy.Models;
using Aneiang.Yarp.Storage;

namespace Aneiang.Yarp.Dashboard.Modules.Policy.Services;

/// <summary>
/// Persists gateway policies via <see cref="IGatewayRepository"/>.
/// </summary>
public class GatewayPolicyPersistenceService : IGatewayPolicyPersistenceService
{
    private readonly IGatewayRepository _repository;

    public GatewayPolicyPersistenceService(IGatewayRepository repository)
    {
        _repository = repository;
    }

    /// <inheritdoc />
    public GatewayPolicyCollection Load()
    {
        var entities = _repository.GetAllPoliciesAsync().GetAwaiter().GetResult();
        return new GatewayPolicyCollection
        {
            Policies = entities.ToGatewayPolicies(),
            LastModified = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public void Save(GatewayPolicyCollection collection)
    {
        collection.LastModified = DateTime.UtcNow;
        foreach (var policy in collection.Policies)
        {
            _repository.SavePolicyAsync(policy.ToEntity()).GetAwaiter().GetResult();
        }

        // Clean up policies that are no longer in the collection
        var existingEntities = _repository.GetAllPoliciesAsync().GetAwaiter().GetResult();
        var targetIds = new HashSet<string>(collection.Policies.Select(p => p.PolicyId), StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingEntities)
        {
            if (!targetIds.Contains(existing.PolicyId))
            {
                _repository.DeletePolicyAsync(existing.PolicyId).GetAwaiter().GetResult();
            }
        }
    }
}
