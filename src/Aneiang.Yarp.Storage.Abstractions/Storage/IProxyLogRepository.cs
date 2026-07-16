using Aneiang.Yarp.Storage.Entities;

namespace Aneiang.Yarp.Storage;

public interface IProxyLogRepository
{
    Task WriteBatchAsync(IEnumerable<ProxyLogMetaEntity> metaEntries, IEnumerable<ProxyLogBodyEntity> bodyEntries, CancellationToken ct = default);
    Task<ProxyLogBodyEntity?> GetBodyByMetaIdAsync(long metaId, CancellationToken ct = default);
    Task<ProxyLogMetaEntity?> GetMetaByIdAsync(long metaId, CancellationToken ct = default);

    Task<(List<ProxyLogMetaEntity> Items, int TotalCount)> SearchAsync(
        int page, int pageSize,
        string? routeId = null, string? clusterId = null, string? level = null,
        int? statusCodeMin = null, int? statusCodeMax = null,
        DateTime? startTime = null, DateTime? endTime = null,
        string? keyword = null, string? eventType = null,
        CancellationToken ct = default);

    Task CleanupAsync(int metaRetentionDays, int bodyRetentionDays, CancellationToken ct = default);
    Task CheckpointAsync(CancellationToken ct = default);
    Task<ProxyLogStatsResult> GetStatsAsync(int recentMinutes, CancellationToken ct = default);
    Task<List<ProxyLogTrafficBucket>> GetTrafficDataAsync(DateTime startTime, CancellationToken ct = default);
    Task<List<ProxyLogRouteIssue>> GetTopIssuesAsync(DateTime startTime, int count, CancellationToken ct = default);
    Task<int> GetRecent5xxCountAsync(int minutes, CancellationToken ct = default);
}
