namespace Aneiang.Yarp.Storage;

/// <summary>
/// Policy repository for gateway policy entity CRUD operations.
/// </summary>
public interface IPolicyRepository
{
    Task<PolicyEntity?> GetPolicyAsync(string policyId, CancellationToken ct = default);
    Task<IReadOnlyList<PolicyEntity>> GetAllPoliciesAsync(CancellationToken ct = default);
    Task SavePolicyAsync(PolicyEntity policy, CancellationToken ct = default);
    Task DeletePolicyAsync(string policyId, CancellationToken ct = default);
}
