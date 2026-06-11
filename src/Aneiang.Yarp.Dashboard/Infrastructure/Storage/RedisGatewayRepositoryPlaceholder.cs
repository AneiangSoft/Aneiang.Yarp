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

    public Task<WebhookSettingsEntity?> GetWebhookSettingsAsync(CancellationToken ct = default) =>
        throw NotImplementedException();
    public Task SaveWebhookSettingsAsync(WebhookSettingsEntity settings, CancellationToken ct = default) =>
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
