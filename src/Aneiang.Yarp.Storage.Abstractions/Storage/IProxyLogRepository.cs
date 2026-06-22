namespace Aneiang.Yarp.Storage;

/// <summary>
/// Proxy log repository for request/response log persistence.
/// </summary>
public interface IProxyLogRepository
{
    Task SaveProxyLogAsync(ProxyLogEntity log, CancellationToken ct = default);
    Task<IReadOnlyList<ProxyLogEntity>> GetRecentProxyLogsAsync(int limit = 200, CancellationToken ct = default);
    Task DeleteOldProxyLogsAsync(int keepCount, CancellationToken ct = default);
}
