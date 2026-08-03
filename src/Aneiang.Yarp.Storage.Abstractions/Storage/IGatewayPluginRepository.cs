namespace Aneiang.Yarp.Storage;

/// <summary>Persists installed gateway plugin state independently from route/cluster bindings.</summary>
public interface IGatewayPluginRepository
{
    Task<IReadOnlyList<GatewayPluginEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<GatewayPluginEntity?> GetAsync(string pluginId, CancellationToken cancellationToken = default);
    Task UpsertAsync(GatewayPluginEntity plugin, CancellationToken cancellationToken = default);
    Task DeleteAsync(string pluginId, CancellationToken cancellationToken = default);
}
