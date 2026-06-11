namespace Aneiang.Yarp.Storage;

/// <summary>
/// Unified gateway repository aggregating all sub-repositories.
/// Implementations initialize database schema via <see cref="InitializeAsync"/>.
/// </summary>
public interface IGatewayRepository : IRouteRepository, IClusterRepository,
    IConfigHistoryRepository, IPolicyRepository, IAuditLogRepository,
    IWebhookSettingsRepository, IProxyLogRepository, IAsyncDisposable, IDisposable
{
    /// <summary>Initialize database schema (create tables, indexes, etc.).</summary>
    Task InitializeAsync(CancellationToken ct = default);
}
