using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Storage;

/// <summary>
/// Placeholder Redis implementation of <see cref="IGatewayRepository"/>.
/// Throws <see cref="NotImplementedException"/> for all methods.
/// Will be implemented when Redis backend support is needed.
/// </summary>
public sealed class RedisGatewayRepositoryPlaceholder : IGatewayRepository
{
    private readonly ILogger<RedisGatewayRepositoryPlaceholder> _logger;

    public RedisGatewayRepositoryPlaceholder(StorageOptions options, ILogger<RedisGatewayRepositoryPlaceholder> logger)
    {
        _logger = logger;
        _logger.LogWarning("RedisGatewayRepositoryPlaceholder is being used — all operations will throw NotImplementedException");
    }

    public Task InitializeAsync(CancellationToken ct = default) =>
        throw new NotImplementedException("Redis backend is not yet implemented.");

    public Task<RouteEntity?> GetRouteAsync(string routeId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<RouteEntity>> GetAllRoutesAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<RouteEntity>> GetRoutesByClusterAsync(string clusterId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveRouteAsync(RouteEntity route, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveRoutesAsync(IEnumerable<RouteEntity> routes, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteRouteAsync(string routeId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteRoutesByClusterAsync(string clusterId, CancellationToken ct = default) =>
        throw NotImplementedException();

    public Task<ClusterEntity?> GetClusterAsync(string clusterId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<ClusterEntity>> GetAllClustersAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveClusterAsync(ClusterEntity cluster, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveClustersAsync(IEnumerable<ClusterEntity> clusters, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteClusterAsync(string clusterId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<DestinationEntity>> GetDestinationsAsync(string clusterId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveDestinationsAsync(string clusterId, IEnumerable<DestinationEntity> destinations, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteDestinationsAsync(string clusterId, CancellationToken ct = default) =>
        throw NotImplementedException();

    public Task<ConfigHistoryEntity?> GetConfigHistoryAsync(string versionId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<ConfigHistoryEntity>> GetConfigHistoryListAsync(int limit = 50, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveConfigHistoryAsync(ConfigHistoryEntity history, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteConfigHistoryAsync(string versionId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteOldConfigHistoryAsync(int keepCount, CancellationToken ct = default) =>
        throw NotImplementedException();

    public Task<PolicyEntity?> GetPolicyAsync(string policyId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<PolicyEntity>> GetAllPoliciesAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SavePolicyAsync(PolicyEntity policy, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeletePolicyAsync(string policyId, CancellationToken ct = default) =>
        throw NotImplementedException();

    public Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsAsync(int limit = 200, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<AuditLogEntity>> GetAuditLogsByTargetAsync(string target, int limit = 50, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveAuditLogAsync(AuditLogEntity audit, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteOldAuditLogsAsync(int keepCount, CancellationToken ct = default) =>
        throw NotImplementedException();

    public Task<WafSettingsEntity?> GetWafSettingsAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveWafSettingsAsync(WafSettingsEntity settings, CancellationToken ct = default) =>
        throw NotImplementedException();

    // ─── INotificationRepository ──────────────────────────────────────────────

    public Task<NotificationSettingsEntity?> LoadSettingsAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveSettingsAsync(NotificationSettingsEntity settings, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<List<NotificationChannel>> GetChannelsAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<NotificationChannel?> GetChannelAsync(string channelId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveChannelAsync(NotificationChannel channel, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteChannelAsync(string channelId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<List<NotificationRule>> GetRulesAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<NotificationRule?> GetRuleAsync(string ruleId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveRuleAsync(NotificationRule rule, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteRuleAsync(string ruleId, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<NotificationGlobalSettings> GetGlobalSettingsAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveGlobalSettingsAsync(NotificationGlobalSettings settings, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task RecordNotificationAsync(NotificationHistory record, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<(List<NotificationHistory> Records, int Total)> GetHistoryAsync(
        int page = 1, int pageSize = 100, string? eventType = null, string? severity = null,
        string? dateStart = null, string? dateEnd = null, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task ClearHistoryAsync(CancellationToken ct = default) =>
        throw NotImplementedException();

    public Task SaveProxyLogAsync(ProxyLogEntity log, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task<IReadOnlyList<ProxyLogEntity>> GetRecentProxyLogsAsync(int limit = 200, CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task DeleteOldProxyLogsAsync(int keepCount, CancellationToken ct = default) =>
        throw NotImplementedException();

    public void Dispose() => GC.SuppressFinalize(this);
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }

    private static NotImplementedException NotImplementedException() =>
        new("Redis backend is not yet implemented. Set Gateway:Storage:Provider to 'Sqlite'.");
}
