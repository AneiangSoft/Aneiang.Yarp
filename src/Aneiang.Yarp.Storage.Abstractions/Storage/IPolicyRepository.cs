namespace Aneiang.Yarp.Storage;

public interface IPolicyRepository
{
    Task<PolicyEntity?> GetPolicyAsync(string policyId, CancellationToken ct = default);
    Task<IReadOnlyList<PolicyEntity>> GetAllPoliciesAsync(CancellationToken ct = default);
    Task SavePolicyAsync(PolicyEntity policy, CancellationToken ct = default);
    Task DeletePolicyAsync(string policyId, CancellationToken ct = default);
    Task<IReadOnlyList<PolicyTargetEntity>> GetPolicyTargetsAsync(string policyId, string? targetType = null, CancellationToken ct = default);
    Task<IReadOnlyList<PolicyTargetEntity>> GetAllPolicyTargetsAsync(CancellationToken ct = default);
    Task SavePolicyTargetAsync(PolicyTargetEntity target, CancellationToken ct = default);
    Task DeletePolicyTargetAsync(string policyId, string targetType, string targetUid, CancellationToken ct = default);
    Task DeletePolicyTargetsAsync(string policyId, CancellationToken ct = default);
}
