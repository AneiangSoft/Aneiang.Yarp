namespace Aneiang.Yarp.Storage;

/// <summary>
/// Audit log repository for config change audit persistence.
/// </summary>
public interface IAuditLogRepository
{
    Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsAsync(int limit = 200, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsByTargetAsync(string target, int limit = 50, CancellationToken ct = default);
    Task SaveAuditLogAsync(AuditLogEntity audit, CancellationToken ct = default);
    Task DeleteOldAuditLogsAsync(int keepCount, CancellationToken ct = default);
}
